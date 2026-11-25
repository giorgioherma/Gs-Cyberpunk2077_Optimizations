using GTotalOptimizer.Core;
using System.Text.Json;

internal static class Mo2PhysicalCetDeploymentTests
{
    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "gco-physical-cet-" + Guid.NewGuid().ToString("N"));
        const string asi = Mo2ProfilerOverlayBuilder.CetAsiRelative;
        const string backup = "bin/x64/plugins/.cet_runtime_profiler/cyber_engine_tweaks.ORIGINAL.asi";
        const string controls = "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua";
        const string grsp = "red4ext/plugins/G-REDscript-Profiler.dll";
        const string original = "original native CET";
        const string instrumented = "owner-verified native CET";
        var game = Path.Combine(root, "game");
        var instance = Path.Combine(root, "MO2");
        var mod = Path.Combine(instance, "mods", "VirtualCET");
        var local = Path.Combine(root, "local");
        string Target(string relative) => Path.Combine(game, relative);
        static void Put(string basePath, string relative, string value)
        {
            var path = Path.Combine(basePath, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, value);
        }
        try
        {
            Put(game, "bin/x64/Cyberpunk2077.exe", "fixture");
            Put(game, asi, original);
            Put(instance, "ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Physical\n");
            Put(instance, "profiles/Physical/modlist.txt", "-VirtualCET\n");
            Put(instance, "profiles/Virtual/modlist.txt", "+VirtualCET\n");
            Put(mod, asi, original);
            Directory.CreateDirectory(Path.Combine(instance, "overwrite"));
            var discovered = Mo2SourceInspector.Discover(instance);
            Mo2Stack Resolve(string name) => Mo2SourceInspector.Resolve(game, discovered, name);
            async Task<Mo2OverlayPreparation> Prepare(string name, string id)
                => await Mo2ProfilerOverlayBuilder.PrepareAsync(game, Resolve(name), id,
                    (fixture, _) =>
                    {
                        Put(fixture, backup, File.ReadAllText(Path.Combine(fixture, asi)));
                        Put(fixture, asi, instrumented);
                        Put(fixture, controls, "owner controls");
                        Put(fixture, grsp, "owner GRSP");
                        return Task.CompletedTask;
                    }, localAppData: local);
            static void Require(bool condition, string reason)
            {
                if (!condition) throw new InvalidDataException(reason);
            }
            var physical = await Prepare("Physical", Guid.NewGuid().ToString("N"));
            Require(!Resolve("Physical").EffectiveFiles[asi].FromMo2, "Physical winner misclassified");
            Require(File.ReadAllText(Target(asi)) == original, "INSTALL modified physical ASI before START");
            Mo2ProfilerOverlayBuilder.VerifyPrepared(physical, Resolve("Physical"));
            Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(physical, Resolve("Physical"));
            Require(File.ReadAllText(Target(asi)) == instrumented &&
                !File.Exists(Path.Combine(physical.StagingDirectory, asi)) &&
                File.Exists(Path.Combine(physical.StagingDirectory, grsp)) &&
                File.Exists(Path.Combine(physical.StagingDirectory, controls)),
                "Physical launch did not isolate native CET deployment");
            using (var doc = JsonDocument.Parse(File.ReadAllText(physical.JournalFile)))
            {
                var j = doc.RootElement;
                Require(j.TryGetProperty("physicalCet", out _) &&
                    j.GetProperty("physicalPassthrough").EnumerateArray().Any(x => x.GetString() == asi) &&
                    !j.GetProperty("generatedFiles").EnumerateArray().Any(x =>
                        x.GetProperty("GameRelativePath").GetString() == asi),
                    "Physical deployment journal or mapping incorrect");
            }
            Require(!Mo2ProfilerOverlayBuilder.IsStillPrepared(physical), "Spent physical preparation reusable");
            Require(Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { }) == 1 &&
                File.ReadAllText(Target(asi)) == original, "Unlaunched physical rollback failed");
            Require(Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { }) == 0,
                "Repeated RESTORE changed a retired deployment");

            var virtualCet = await Prepare("Virtual", Guid.NewGuid().ToString("N"));
            Require(Resolve("Virtual").EffectiveFiles[asi].FromMo2, "Virtual winner misclassified");
            Mo2ProfilerOverlayBuilder.VerifyPrepared(virtualCet, Resolve("Virtual"));
            Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(virtualCet, Resolve("Virtual"));
            Require(File.ReadAllText(Target(asi)) == original &&
                File.ReadAllText(Path.Combine(mod, asi)) == original &&
                File.ReadAllText(Path.Combine(virtualCet.StagingDirectory, asi)) == instrumented &&
                Mo2ProfilerOverlayBuilder.IsStillPrepared(virtualCet),
                "MO2-managed CET route changed physical or virtual originals");
            Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { });

            var drift = await Prepare("Physical", Guid.NewGuid().ToString("N"));
            Mo2ProfilerOverlayBuilder.VerifyPrepared(drift, Resolve("Physical"));
            Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(drift, Resolve("Physical"));
            Put(game, asi, "foreign modification");
            try
            {
                Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { });
                throw new Exception("Foreign CET ASI was accepted for overwrite");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("changed outside GCO")) { }
            Require(File.ReadAllText(Target(asi)) == "foreign modification" &&
                Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local),
                "Drift refusal lost foreign bytes or recovery journal");
            Put(game, asi, instrumented); // fixture-only recovery of known bytes
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(drift, "prepared", "test-launch-pending");
            Require(Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () =>
                throw new Exception("Pending RESTORE closed MO2"), deferPending: true) == 1 &&
                File.ReadAllText(Target(asi)) == instrumented &&
                Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore(local),
                "Uncertain launch prematurely restored physical native CET");
            Require(Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { }, deferredOnly: true) == 1 &&
                File.ReadAllText(Target(asi)) == original,
                "Deferred VERIFY did not restore physical native CET");
            async Task<Mo2OverlayPreparation> AcknowledgedLaunch(UnifiedProfilerWorkflow? workflow = null)
            {
                var prepared = await Prepare("Physical", Guid.NewGuid().ToString("N"));
                Mo2ProfilerOverlayBuilder.VerifyPrepared(prepared, Resolve("Physical"));
                if (workflow is not null)
                    workflow.BeginMo2CaptureAttempt(new Mo2ProfileSelection(true, "", instance, instance,
                        game, "Physical", ["Physical", "Virtual"], Resolve("Physical")), prepared);
                Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(prepared, Resolve("Physical"));
                Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
                using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/d /c exit 0")
                    { UseShellExecute = false, CreateNoWindow = true })!;
                child.WaitForExit();
                Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending",
                    "test-launched", child.Id);
                return prepared;
            }
            var repeat = await AcknowledgedLaunch();
            Mo2ProfilerOverlayBuilder.RetireCompletedTest(repeat);
            Require(File.ReadAllText(Target(asi)) == original &&
                !Mo2ProfilerOverlayBuilder.CanRestartCompletedTest(repeat),
                "Repeated START did not restore physical CET before fresh preparation");

            var collected = await AcknowledgedLaunch();
            var binding = new Mo2CaptureIdentity(collected.SessionId, game, instance, "Physical", null);
            var resumed = Mo2ProfilerOverlayBuilder.ReadLaunchedPreparation(binding, Resolve("Physical"), local);
            Require(resumed is not null && !resumed.Files.Any(f => f.GameRelativePath == asi),
                "Resumed physical CET launch retained a redundant virtual ASI");
            Mo2ProfilerOverlayBuilder.RestoreCollectedDeployment(binding, local);
            Require(File.ReadAllText(Target(asi)) == original &&
                !Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local),
                "CONTINUE did not restore exact original physical CET");
            Mo2ProfilerOverlayBuilder.RestoreCollectedDeployment(binding, local);
            Require(File.ReadAllText(Target(asi)) == original, "Repeated CONTINUE changed original bytes");

            var invalid = await Prepare("Physical", Guid.NewGuid().ToString("N"));
            using (var doc = JsonDocument.Parse(File.ReadAllText(invalid.JournalFile)))
                Put(doc.RootElement.GetProperty("fixtureRoot").GetString()!, backup, "damaged owner backup");
            Mo2ProfilerOverlayBuilder.VerifyPrepared(invalid, Resolve("Physical"));
            try
            {
                Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(invalid, Resolve("Physical"));
                throw new Exception("Damaged CET owner backup was accepted");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("backup failed verification")) { }
            Require(File.ReadAllText(Target(asi)) == original &&
                File.Exists(Path.Combine(invalid.StagingDirectory, asi)),
                "Damaged owner backup changed native source or stage");
            Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { });
            Console.WriteLine("PASS physical/virtual CET deployment, exact rollback, drift, pending recovery, repeat START and CONTINUE");

            var frameworkWorkflow = new UnifiedProfilerWorkflow(Path.Combine(root, "framework-package"), local);
            Mo2ProfileSelection Selection(string profile) => new(true, "", instance, instance,
                game, profile, ["Physical", "Virtual"], Resolve(profile));
            Require(frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Physical")) is null,
                "Framework baseline admitted without a capture journal");
            var active = await AcknowledgedLaunch(frameworkWorkflow);
            var originalInput = frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Physical"));
            Require(originalInput is not null && File.ReadAllText(originalInput) == original,
                "Acknowledged physical CET profiler lost its verified framework baseline");
            Require(frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Virtual")) is null,
                "Framework baseline leaked to a different MO2 profile");
            Put(game, asi, "unrecognized CET binary");
            Require(frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Physical")) is null,
                "Framework baseline masked foreign CET bytes");
            Put(game, asi, instrumented);
            File.WriteAllText(originalInput!, "tampered backup");
            try
            {
                frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Physical"));
                throw new Exception("Framework baseline accepted a damaged owner backup");
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("backup failed verification")) { }
            File.WriteAllText(originalInput!, original);
            Mo2ProfilerOverlayBuilder.RetireCompletedTest(active);
            frameworkWorkflow.RetireMo2CaptureAttempt(game, active.SessionId);
            Require(frameworkWorkflow.VerifiedMo2FrameworkCet(Selection("Physical")) is null &&
                File.ReadAllText(Target(asi)) == original,
                "Retired framework baseline remained active");
            Console.WriteLine("PASS MO2 framework original CET input is bound to a verified active physical capture");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
