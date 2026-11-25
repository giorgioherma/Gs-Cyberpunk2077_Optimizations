using GTotalOptimizer.Core;

internal static class Mo2RestoreRuntimeAdditionTests
{
    private const string Asi = "bin/x64/plugins/cyber_engine_tweaks.asi";
    private const string Mods = "bin/x64/plugins/cyber_engine_tweaks/mods/";
    private const string Controls = Mods + "CETProfilerControls/init.lua";
    private const string Grsp = "red4ext/plugins/G-REDscript-Profiler.dll";
    private const string Binding = "bin/x64/plugins/cyber_engine_tweaks/bindings.json";

    public static async Task RunAsync()
    {
        foreach (var physical in new[] { true, false })
            await RunRoute(physical);
    }

    private static async Task RunRoute(bool physical)
    {
        var root = Path.Combine(Path.GetTempPath(), "gco-mo2-runtime-restore-" + Guid.NewGuid().ToString("N"));
        var game = Path.Combine(root, "game");
        var instance = Path.Combine(root, "MO2");
        var mod = Path.Combine(instance, "mods", "Active");
        var overwrite = Path.Combine(instance, "overwrite");
        var local = Path.Combine(root, "local");
        var package = Path.Combine(root, "package");
        var id = Guid.NewGuid().ToString("N");
        string At(string dir, string relative) => Path.Combine(dir, relative);
        void Put(string dir, string relative, string bytes)
        {
            var path = At(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, bytes);
        }
        void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidDataException((physical ? "Physical: " : "Virtual: ") + detail);
        }
        try
        {
            Put(game, "bin/x64/Cyberpunk2077.exe", "fixture");
            Put(game, Asi, "physical original");
            if (!physical) Put(mod, Asi, "MO2 original");
            Put(mod, Mods + "Active/init.lua", "original callback");
            const string existingConfig = Mods + "Active/settings.json";
            const string existingPhysical = Mods + "Active/physical.ini";
            const string packagedConfig = Mods + "Active/packaged.json";
            Put(overwrite, existingConfig, "old gameplay settings");
            Put(game, existingPhysical, "old physical settings");
            Put(mod, packagedConfig, "original packaged settings");
            Put(instance, "ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Test\n");
            Put(instance, "profiles/Test/modlist.txt", "+Active\n");
            Directory.CreateDirectory(overwrite);
            var discovered = Mo2SourceInspector.Discover(instance);
            Mo2Stack Resolve() => Mo2SourceInspector.Resolve(game, discovered, "Test");
            Mo2ProfileSelection Selection() => new(true, "", instance, instance,
                game, "Test", ["Test"], Resolve());
            var original = physical ? "physical original" : "MO2 original";
            var workflow = new UnifiedProfilerWorkflow(package, local);
            var prepared = await Mo2ProfilerOverlayBuilder.PrepareAsync(game, Resolve(), id,
                (fixture, _) =>
                {
                    Put(fixture, "bin/x64/plugins/.cet_runtime_profiler/cyber_engine_tweaks.ORIGINAL.asi", original);
                    Put(fixture, Asi, "owner profiler");
                    Put(fixture, Controls, "owner controls");
                    Put(fixture, Grsp, "owner GRSP");
                    Put(fixture, Binding, "{}");
                    return Task.CompletedTask;
                }, localAppData: local);
            workflow.BeginMo2CaptureAttempt(Selection(), prepared);
            Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(prepared, Resolve());
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/d /c exit 0")
                { UseShellExecute = false, CreateNoWindow = true })!;
            child.WaitForExit();
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending", "test-launched", child.Id);

            // Create exact synthetic archive indexes, as the existing shared
            // Continue contract does. Real owner collection is tested separately.
            foreach (var kind in new[] { BackendKind.Cet, BackendKind.Grsp })
            {
                var archive = Path.Combine(root, "owner-" + kind);
                Directory.CreateDirectory(archive);
                File.WriteAllText(Path.Combine(archive,
                    kind == BackendKind.Cet ? "CET_Summary.json" : "GRSP_Summary.json"), "{}");
                CollectedCaptureStore.CopyOwner(package, game, id, kind, archive);
            }
            CollectedCaptureStore.MarkCollectInvoked(package, game, id);
            CollectedCaptureStore.MarkCollectionAttempted(package, game, id, true);
            const string config = Mods + "Active/new-settings.json";
            const string data = Mods + "Active/data/state.ini";
            Put(overwrite, existingConfig, "updated gameplay settings");
            Put(game, existingPhysical, "updated physical settings");
            Put(overwrite, config, "new gameplay settings");
            Put(game, data, "new gameplay data");
            Put(overwrite, Binding, "{\"F11\":1}");

            // A real profiler remnant or new executable source must not be
            // silently accepted or removed as if it were ordinary mod data.
            foreach (var bad in new[] { Controls, Mods + "Active/new.lua" })
            {
                Put(overwrite, bad, "foreign source");
                var refused = await workflow.ContinueAsync(game, false, false, true, mo2Selection: Selection());
                Require(!refused.RestoreVerified && refused.Detail.Contains(bad) &&
                    workflow.HasRecoveryJournal && File.ReadAllText(At(overwrite, bad)) == "foreign source",
                    "unsafe added source accepted or recovery lost: " + bad);
                File.Delete(At(overwrite, bad));
            }
            Put(mod, Mods + "Active/init.lua", "changed callback");
            var drift = await workflow.ContinueAsync(game, false, false, true, mo2Selection: Selection());
            Require(!drift.RestoreVerified && workflow.HasRecoveryJournal &&
                File.ReadAllText(At(mod, Mods + "Active/init.lua")) == "changed callback",
                "existing source drift was accepted");
            Put(mod, Mods + "Active/init.lua", "original callback");
            Put(mod, packagedConfig, "changed package data");
            var packageDrift = await workflow.ContinueAsync(game, false, false, true, mo2Selection: Selection());
            Require(!packageDrift.RestoreVerified && packageDrift.Detail.Contains(packagedConfig) &&
                workflow.HasRecoveryJournal && File.ReadAllText(At(mod, packagedConfig)) == "changed package data",
                "existing package data drift was accepted");
            Put(mod, packagedConfig, "original packaged settings");

            // Both deployment routes must now continue with all new user
            // data preserved, original CET bytes and no active profiler files.
            var restored = await workflow.ContinueAsync(game, false, false, true, mo2Selection: Selection());
            Require(restored.RestoreVerified && !workflow.HasRecoveryJournal &&
                !Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local),
                "CONTINUE failed: " + restored.Detail);
            Require(File.ReadAllText(At(game, Asi)) == "physical original" &&
                (physical || File.ReadAllText(At(mod, Asi)) == "MO2 original"),
                "original CET ASI not restored");
            Require(File.ReadAllText(At(overwrite, existingConfig)) == "updated gameplay settings" &&
                File.ReadAllText(At(game, existingPhysical)) == "updated physical settings" &&
                File.ReadAllText(At(overwrite, config)) == "new gameplay settings" &&
                File.ReadAllText(At(game, data)) == "new gameplay data" &&
                File.ReadAllText(At(overwrite, Binding)) == "{\"F11\":1}",
                "runtime-created user data changed");
            var saved = CollectedCaptureStore.ReadRestored(package, game, id);
            using var view = Mo2AnalysisSource.Create(saved);
            Require(!File.Exists(At(view.Root, Controls)) && !File.Exists(At(view.Root, Grsp)) &&
                File.ReadAllText(At(view.Root, Asi)) == original,
                "profiler source still active in restored selected profile");
            Console.WriteLine("PASS MO2 " + (physical ? "physical" : "virtual") +
                " CET CONTINUE preserves new runtime data and restores original native source");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
