using GTotalOptimizer.Core;
using System.Diagnostics;
using System.Text.Json;

internal static class Mo2CompanionDiscoveryTests
{
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "gco-mo2-companion-" + Guid.NewGuid().ToString("N"));
        try
        {
            string app = Path.Combine(root, "app");
            string game = Path.Combine(root, "game");
            string mods = Path.Combine(root, "mods");
            string profiles = Path.Combine(root, "profiles");
            string overwrite = Path.Combine(root, "overwrite");
            string statusDir = Path.Combine(root, "GCO", "MO2Bridge");
            string statusPath = Path.Combine(statusDir, "instance-test.json");
            Directory.CreateDirectory(app);
            Directory.CreateDirectory(Path.Combine(game, "bin", "x64"));
            Directory.CreateDirectory(Path.Combine(profiles, "Other"));
            Directory.CreateDirectory(statusDir);
            File.WriteAllText(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), "");
            var instance = new Mo2Instance(true, "", mods, profiles, overwrite,
                ["Selected", "Other"]);
            var source = new Dictionary<string, object>
            {
                ["schemaVersion"] = 1,
                ["kind"] = "gco.mo2.readonly.status",
                ["mo2Executable"] = Path.Combine(app, "ModOrganizer.exe"),
                ["instanceRoot"] = root,
                ["gameRoot"] = game,
                ["gameName"] = "Cyberpunk2077",
                // Selected profile can differ from MO2's currently displayed profile.
                ["currentProfile"] = "Other",
                ["currentProfilePath"] = Path.Combine(profiles, "Other"),
                ["modsRoot"] = mods,
                ["overwriteRoot"] = overwrite,
                ["processId"] = Process.GetCurrentProcess().Id,
                ["updatedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["profileSpecificModMutationProven"] = false,
                ["runtimeNativeOverlayProven"] = false,
                ["installationEnabled"] = false,
            };
            void Publish() => File.WriteAllText(statusPath, JsonSerializer.Serialize(source));
            Publish();
            var live = Mo2CompanionDiscovery.Inspect(app, instance, root);
            if (!live.Connected || live.CurrentProfile != "Other" ||
                live.CanMutateSelectedProfile || live.CanInstallProfilers ||
                !string.Equals(live.GameDirectory, game, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MO2_COMPANION_STATUS_VALIDATION_FAILED");

            source["updatedUtc"] = DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O");
            Publish();
            if (Mo2CompanionDiscovery.Inspect(app, instance, root).Connected)
                throw new InvalidDataException("MO2_STALE_COMPANION_ACCEPTED");

            source["updatedUtc"] = DateTimeOffset.UtcNow.ToString("O");
            source["modsRoot"] = Path.Combine(root, "wrong-mods");
            Publish();
            if (Mo2CompanionDiscovery.Inspect(app, instance, root).Connected)
                throw new InvalidDataException("MO2_FOREIGN_MODS_ROOT_ACCEPTED");

            source["modsRoot"] = mods;
            source["installationEnabled"] = true;
            Publish();
            if (Mo2CompanionDiscovery.Inspect(app, instance, root).Connected)
                throw new InvalidDataException("MO2_UNVERIFIED_INSTALL_CAPABILITY_ACCEPTED");

            File.WriteAllText(statusPath, "[]");
            if (Mo2CompanionDiscovery.Inspect(app, instance, root).Connected)
                throw new InvalidDataException("MO2_INVALID_COMPANION_JSON_ACCEPTED");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("PASS MO2 companion read-only status, stale/mismatch refusal and no installer activation");
    }
}
