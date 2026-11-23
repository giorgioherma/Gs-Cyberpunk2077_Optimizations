using GTotalOptimizer.Core;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Mo2RealOwnerOverlayTests
{
    public static async Task RunAsync(string verifiedPackageRoot, string officialCetAsi,
        string? realZeroEngineDirectory = null)
    {
        if (!File.Exists(officialCetAsi))
            throw new FileNotFoundException("Official CET fixture binary unavailable.", officialCetAsi);
        var bundle = ComponentBundleVerifier.Inspect(verifiedPackageRoot);
        if (!bundle.Ready)
            throw new InvalidDataException("GCO_MO2_OWNER_BUNDLE_UNVERIFIED: " + bundle.Detail);

        var folder = Path.Combine(Path.GetTempPath(), "gco-mo2-realowner-" + Guid.NewGuid().ToString("N"));
        const string sessionId = "abcdef0123456789abcdef0123456789";
        try
        {
            string game = Path.Combine(folder, "game");
            string instance = Path.Combine(folder, "MO2-instance");
            string local = Path.Combine(folder, "local");
            string asiRel = Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks.asi");
            string asi = Path.Combine(game, asiRel);
            Directory.CreateDirectory(Path.GetDirectoryName(asi)!);
            File.Copy(officialCetAsi, asi);
            var sourceSha = Sha256(asi);
            Directory.CreateDirectory(Path.Combine(game, "red4ext", "plugins"));
            File.WriteAllBytes(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), []);

            void Put(string relative, string value)
            {
                string target = Path.Combine(instance, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, value);
            }
            Put("ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Heavy\n");
            Put("profiles/Heavy/modlist.txt", "+SimpleCETMod\n");
            Put("profiles/Other/modlist.txt", "-SimpleCETMod\n");
            Put("mods/SimpleCETMod/bin/x64/plugins/cyber_engine_tweaks/mods/Example/init.lua",
                "registerForEvent('onInit', function() end)\n");
            // Optional private *historical real source* fixture. Never commit
            // or upload these files; CI checks them out read-only in RUNNER_TEMP.
            string? sourceZeroHash = null;
            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory))
            {
                var source = Path.GetFullPath(realZeroEngineDirectory);
                var sourceInit = Path.Combine(source, "init.lua");
                if (!File.Exists(sourceInit))
                    throw new InvalidDataException("MO2_REAL_0ENGINE_SOURCE_INIT_MISSING");
                sourceZeroHash = Sha256(sourceInit);
                var destination = Path.Combine(instance, "mods", "SimpleCETMod", "bin", "x64",
                    "plugins", "cyber_engine_tweaks", "mods", "0-Engine");
                int files = 0;
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("MO2_REAL_0ENGINE_FIXTURE_LINKED_INPUT_REFUSED");
                    if (++files > 5000)
                        throw new InvalidDataException("MO2_REAL_0ENGINE_FIXTURE_OVERSIZED");
                    var relative = Path.GetRelativePath(source, file);
                    var target = Path.Combine(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                    if (Sha256(file) != Sha256(target))
                        throw new InvalidDataException("MO2_REAL_0ENGINE_INPUT_PROJECTION_MISMATCH");
                }
            }
            var details = Mo2SourceInspector.Discover(instance);
            var effective = Mo2SourceInspector.Resolve(game, details, "Heavy");
            if (!effective.Ready || !effective.ProfileVerified || effective.GameOnlyFallback ||
                !effective.HasCetAsi || effective.CetOwners != 1)
                throw new InvalidDataException("MO2_REAL_OWNER_SELECTED_PROFILE_NOT_ADMITTED");

            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory) &&
                !effective.HasZeroEngine)
                throw new InvalidDataException("MO2_REAL_0ENGINE_EFFECTIVE_WINNER_NOT_ADMITTED");
            var output = await Mo2ProfilerOverlayBuilder.PrepareFromBundleAsync(
                verifiedPackageRoot, game, effective, sessionId, localAppData: local);
            var names = output.Files.Select(x => x.GameRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!names.Contains("bin/x64/plugins/cyber_engine_tweaks.asi") ||
                !names.Contains("red4ext/plugins/G-REDscript-Profiler.dll") ||
                !names.Contains("bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua"))
                throw new InvalidDataException("MO2_REAL_OWNER_PROFILER_PAYLOAD_INCOMPLETE");

            foreach (var item in output.Files)
            {
                var full = Path.Combine(output.StagingDirectory,
                    item.GameRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full) || Sha256(full) != item.Sha256 ||
                    new FileInfo(full).Length != item.Size)
                    throw new InvalidDataException("MO2_REAL_OWNER_STAGED_OUTPUT_HASH_MISMATCH");
            }
            if (Sha256(asi) != sourceSha ||
                File.Exists(Path.Combine(game, "red4ext", "plugins", "G-REDscript-Profiler.dll")) ||
                Directory.Exists(Path.Combine(game, "bin", "x64", "plugins",
                    ".cet_runtime_profiler")) ||
                Directory.Exists(Path.Combine(game, "bin", "x64", "plugins",
                    "cyber_engine_tweaks", "mods", "CETProfilerControls")))
                throw new InvalidDataException("MO2_REAL_OWNER_CHANGED_LIVE_GAME");

            using var json = JsonDocument.Parse(File.ReadAllText(output.JournalFile));
            if (json.RootElement.GetProperty("phase").GetString() != "prepared")
                throw new InvalidDataException("MO2_REAL_OWNER_SESSION_NOT_JOURNALED");
            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory) &&
                (Sha256(Path.Combine(realZeroEngineDirectory, "init.lua")) != sourceZeroHash ||
                !File.Exists(Path.Combine(instance, "mods", "SimpleCETMod", "bin", "x64",
                    "plugins", "cyber_engine_tweaks", "mods", "0-Engine", "init.lua"))))
                throw new InvalidDataException("MO2_REAL_0ENGINE_HISTORICAL_SOURCE_CHANGED");
            Console.WriteLine("PASS Stage 3 REAL CET + GRSP owner install on disposable " +
                (realZeroEngineDirectory is null ? "original no-0-Engine" : "historical existing-0-Engine") +
                " profile fixture: " + output.Files.Count +
                " verified virtual overlay files; game and mod originals unchanged");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static string Sha256(string file)
    {
        using var source = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }
}
