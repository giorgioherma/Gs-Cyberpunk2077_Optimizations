using GTotalOptimizer.Core;
using System.Text.Json;

internal static class Mo2ProfilerOverlayBuilderTests
{
    private const string Session = "abcdef0123456789abcdef0123456789";

    public static async Task RunAsync()
    {
        var root = Path.Combine(Path.GetTempPath(), "gco-mo2-overlay-" + Guid.NewGuid().ToString("N"));
        try
        {
            var game = Path.Combine(root, "game");
            var instanceRoot = Path.Combine(root, "MO2");
            var mod = Path.Combine(instanceRoot, "mods", "SelectedMod");
            var local = Path.Combine(root, "local");
            static void Put(string root, string relative, string content)
            {
                string path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content);
            }
            const string asi = "bin/x64/plugins/cyber_engine_tweaks.asi";
            const string binding = "bin/x64/plugins/cyber_engine_tweaks/bindings.json";
            const string zero = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua";
            const string owner = "bin/x64/plugins/cyber_engine_tweaks/mods/SelectedMod/init.lua";
            const string scheduler = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/G-CET/Scheduler.lua";
            const string controls = "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua";
            const string controlTitle = "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/CaptureTitle.txt";
            const string dll = "red4ext/plugins/G-REDscript-Profiler.dll";
            const string grspTitle = "red4ext/plugins/G-REDscript-Profiler/CaptureTitle.txt";

            Put(game, "bin/x64/Cyberpunk2077.exe", "FAKE EXE");
            Put(game, asi, "ORIGINAL_CET_ASI");
            Put(mod, binding, "{\"key\":\"user-selected-key\"}");
            Put(mod, zero, "original_zero_engine()");
            Put(mod, owner, "original_mod_callback()");
            Put(instanceRoot, "ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Heavy\n");
            Put(instanceRoot, "profiles/Heavy/modlist.txt", "+SelectedMod\n");
            Put(instanceRoot, "profiles/Other/modlist.txt", "-SelectedMod\n");

            var instance = Mo2SourceInspector.Discover(instanceRoot);
            var stack = Mo2SourceInspector.Resolve(game, instance, "Heavy");
            if (!stack.Ready || !stack.ProfileVerified ||
                stack.Winner(binding) != Path.Combine(mod, binding.Replace('/', Path.DirectorySeparatorChar)))
                throw new InvalidDataException("MO2_EFFECTIVE_CET_BINDING_SOURCE_MISSING");

            static void FakeOwnerInstall(string fixture)
            {
                Put(fixture, asi, "AUTHORITATIVE_CET_PROFILER");
                Put(fixture, binding, "{\"key\":\"F11\"}");
                Put(fixture, zero, "integrated_zero_engine()");
                Put(fixture, scheduler, "CET_PROFILER_SCHEDULER");
                Put(fixture, controls, "CET_F11_CONTROL");
                Put(fixture, controlTitle, "WORLD");
                Put(fixture, dll, "GRSP_NATIVE_PROFILER");
                Put(fixture, grspTitle, "WORLD");
                Put(fixture, "bin/x64/plugins/.cet_runtime_profiler/state.json", "{}");
                Put(fixture, "red4ext/plugins/G-REDscript-Profiler/.manager_state.json", "{}");
            }

            var result = await Mo2ProfilerOverlayBuilder.PrepareAsync(game, stack, Session,
                (fixture, _) => { FakeOwnerInstall(fixture); return Task.CompletedTask; },
                localAppData: local);
            var expected = new[] { asi, binding, zero, scheduler, controls, controlTitle, dll, grspTitle };
            if (result.Files.Count != expected.Length ||
                expected.Any(e => !result.Files.Any(f => f.GameRelativePath == e)) ||
                result.Profile != "Heavy")
                throw new InvalidDataException("MO2_OWNER_DELTA_COMPOSITION_INCOMPLETE");
            foreach (var file in result.Files)
            {
                var staged = Path.Combine(result.StagingDirectory, file.GameRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(staged) || new FileInfo(staged).Length != file.Size)
                    throw new InvalidDataException("MO2_STAGED_OWNER_SOURCE_BYTES_INVALID");
            }
            if (Directory.Exists(Path.Combine(result.StagingDirectory, "bin", "x64", "plugins", ".cet_runtime_profiler")) ||
                File.Exists(Path.Combine(result.StagingDirectory, "red4ext", "plugins",
                    "G-REDscript-Profiler", ".manager_state.json")) ||
                File.ReadAllText(Path.Combine(game, asi.Replace('/', Path.DirectorySeparatorChar))) != "ORIGINAL_CET_ASI" ||
                File.ReadAllText(Path.Combine(mod, zero.Replace('/', Path.DirectorySeparatorChar))) != "original_zero_engine()" ||
                File.ReadAllText(Path.Combine(mod, binding.Replace('/', Path.DirectorySeparatorChar))) !=
                    "{\"key\":\"user-selected-key\"}")
                throw new InvalidDataException("MO2_ORIGINAL_MODS_OR_OWNER_METADATA_LEAKED");
            using (var j = JsonDocument.Parse(File.ReadAllText(result.JournalFile)))
                if (j.RootElement.GetProperty("phase").GetString() != "prepared")
                    throw new InvalidDataException("MO2_SESSION_JOURNAL_NOT_PREPARED");
            try
            {
                await Mo2ProfilerOverlayBuilder.PrepareAsync(game, stack, Session,
                    (_, _) => Task.CompletedTask, localAppData: local);
                throw new InvalidDataException("MO2_DUPLICATE_SESSION_REUSED");
            }
            catch (InvalidOperationException) { }

            Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack);
            static void RejectVerification(Action verify)
            {
                try { verify(); }
                catch (InvalidDataException) { return; }
                throw new Exception("MO2_STALE_PREPARED_OVERLAY_ACCEPTED");
            }
            Put(mod, owner, "changed after preparation");
            RejectVerification(() => Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack));
            Put(mod, owner, "original_mod_callback()");
            Put(result.StagingDirectory, controls, "changed staged controls");
            RejectVerification(() => Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack));
            Put(result.StagingDirectory, controls, "CET_F11_CONTROL");
            Put(mod, "bin/x64/plugins/cyber_engine_tweaks/mods/NewMod/init.lua", "new callback");
            var changedStack = Mo2SourceInspector.Resolve(game, instance, "Heavy");
            RejectVerification(() => Mo2ProfilerOverlayBuilder.VerifyPrepared(result, changedStack));
            Directory.Delete(Path.Combine(mod, "bin/x64/plugins/cyber_engine_tweaks/mods/NewMod"), true);
            Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack);

            if (!Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local))
                throw new InvalidDataException("MO2_PREPARED_SESSION_NOT_RECOVERABLE");
            if (!Mo2ProfilerOverlayBuilder.IsStillPrepared(result))
                throw new InvalidDataException("MO2_PRE_DISPATCH_RESTORE_NOT_AVAILABLE");
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(result, "prepared", "test-launch-pending");
            if (Mo2ProfilerOverlayBuilder.IsStillPrepared(result))
                throw new InvalidDataException("MO2_PENDING_DISPATCH_MISCLASSIFIED_AS_PREPARED");
            RejectVerification(() => Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack));
            if (!Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local))
                throw new InvalidDataException("MO2_UNCERTAIN_LAUNCH_NOT_BLOCKED");
            try
            {
                Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(result, "prepared", "retired-unlaunched");
                throw new InvalidDataException("MO2_UNCERTAIN_LAUNCH_RETIRED_AS_UNLAUNCHED");
            }
            catch (InvalidOperationException) { }
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(result, "test-launch-pending", "test-launched", 123);
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(result, "test-launched", "retired-test-capture", 123);
            if (!Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local))
                throw new InvalidDataException("MO2_UNCOLLECTED_CAPTURE_REUSE_ALLOWED");
            using (var journal = JsonDocument.Parse(File.ReadAllText(result.JournalFile)))
                if (journal.RootElement.GetProperty("generatedFiles").GetArrayLength() != expected.Length ||
                    journal.RootElement.GetProperty("originalSources").GetRawText().Length <= 2)
                    throw new InvalidDataException("MO2_TEST_RETIREMENT_LOST_RECOVERY_MANIFEST");

            async Task ShouldReject(string name, Action<string> change)
            {
                string id = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(name))).ToLowerInvariant()[..32];
                try
                {
                    await Mo2ProfilerOverlayBuilder.PrepareAsync(game, stack, id,
                        (fixture, _) =>
                        {
                            FakeOwnerInstall(fixture);
                            change(fixture);
                            return Task.CompletedTask;
                        }, localAppData: local);
                    throw new InvalidDataException("MO2_UNSAFE_OWNER_OUTPUT_ACCEPTED: " + name);
                }
                catch (InvalidDataException ex) when (!ex.Message.StartsWith("MO2_UNSAFE_OWNER_OUTPUT_ACCEPTED"))
                {
                    var journal = Path.Combine(local, "GCO", "MO2Bridge", "journals", id + ".json");
                    using var j = JsonDocument.Parse(File.ReadAllText(journal));
                    if (j.RootElement.GetProperty("phase").GetString() != "recovery-required")
                        throw new InvalidDataException("MO2_UNSAFE_OUTPUT_RECOVERY_JOURNAL_MISSING");
                }
            }

            await ShouldReject("extra", fixture =>
                Put(fixture, "r6/scripts/SomeMod/unapproved.reds", "unsafe"));
            await ShouldReject("changed", fixture =>
                Put(fixture, owner, "unapproved callback change"));
            await ShouldReject("deleted", fixture =>
                File.Delete(Path.Combine(fixture, owner.Replace('/', Path.DirectorySeparatorChar))));
            await ShouldReject("drift", _ =>
                Put(mod, owner, "original_source_drift"));
            Put(mod, owner, "original_mod_callback()");
            var foreign = Mo2SourceInspector.Resolve(game, instance, "NotAProfile");
            try
            {
                await Mo2ProfilerOverlayBuilder.PrepareAsync(game, foreign,
                    "1234567890abcdef1234567890abcdef", (_, _) => Task.CompletedTask,
                    localAppData: local);
                throw new InvalidDataException("MO2_FOREIGN_PROFILE_OVERLAY_ACCEPTED");
            }
            catch (InvalidDataException ex) when (!ex.Message.StartsWith("MO2_FOREIGN_PROFILE_OVERLAY_ACCEPTED")) { }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Console.WriteLine("PASS MO2 Stage 3 source-owner fixture projection, binding preservation, delta guards and journal refusals");
    }
}
