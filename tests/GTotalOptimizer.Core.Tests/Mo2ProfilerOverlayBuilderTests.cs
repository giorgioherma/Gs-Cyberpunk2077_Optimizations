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
            const string emitter = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/EventEmitter.lua";
            const string sibling = "bin/x64/plugins/cyber_engine_tweaks/config.json";
            const string shadowed = "bin/x64/plugins/cyber_engine_tweaks/mods/SelectedMod/init.lua";
            Put(game, emitter, "original_emitter");
            Put(game, sibling, "original_config");
            Put(game, shadowed, "physical_shadowed");
            Put(mod, binding, "{\"key\":\"user-selected-key\"}");
            Put(mod, zero, "original_zero_engine()");
            Put(mod, owner, "original_mod_callback()");
            Put(instanceRoot, "ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Heavy\n");
            Put(instanceRoot, "profiles/Heavy/modlist.txt", "+SelectedMod\n");
            Put(instanceRoot, "profiles/Other/modlist.txt", "-SelectedMod\n");

            const string prior = "red4ext/plugins/G-REDscript-Profiler/RESULTS/Capture_0001_WORLD_123/GRSP_Summary.csv";
            const string priorStatus = "red4ext/plugins/G-REDscript-Profiler/RESULTS/GRSP_Status.txt";
            var overwrite = Path.Combine(instanceRoot, "overwrite");
            Put(overwrite, prior, "previous capture");
            Put(overwrite, priorStatus, "previous status");
            const string cetResult = "bin/x64/plugins/cyber_engine_tweaks/CET_Runtime_Profile_ByMod.csv";
            const string unrelated = "red4ext/plugins/Codeware/user.log";
            Put(game, cetResult, "old physical CET capture");
            Put(overwrite, cetResult, "old virtual CET capture");
            Put(overwrite, unrelated, "unrelated Overwrite data");
            Put(root, "RESULTS/Collected/GRSP_Summary.csv", "sealed capture");
            var instance = Mo2SourceInspector.Discover(instanceRoot);
            var stack = Mo2SourceInspector.Resolve(game, instance, "Heavy");
            if (!stack.Ready || !stack.ProfileVerified ||
                stack.Winner(binding) != Path.Combine(mod, binding.Replace('/', Path.DirectorySeparatorChar)))
                throw new InvalidDataException("MO2_EFFECTIVE_CET_BINDING_SOURCE_MISSING");

            static void FakeOwnerInstall(string fixture)
            {
                var grspData = Path.Combine(fixture, "red4ext", "plugins", "G-REDscript-Profiler");
                if (Directory.Exists(grspData) && Directory.EnumerateFileSystemEntries(grspData).Any())
                    throw new InvalidOperationException("GRSP owner refuses nonempty data folder");
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
                localAppData: local, cetResultPatterns: ["CET_Runtime_Profile_*.csv"]);
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
            {
                if (j.RootElement.GetProperty("phase").GetString() != "prepared")
                    throw new InvalidDataException("MO2_SESSION_JOURNAL_NOT_PREPARED");
                if (j.RootElement.TryGetProperty("priorGrspResultsRoot", out _) ||
                    !File.Exists(Path.Combine(overwrite, prior)) ||
                    !File.Exists(Path.Combine(game, cetResult)) ||
                    File.Exists(Path.Combine(result.StagingDirectory, prior)) ||
                    File.Exists(Path.Combine(result.StagingDirectory, cetResult)))
                    throw new InvalidDataException("MO2_INSTALL_MUST_NOT_ARCHIVE_OR_DISCARD_CAPTURE");
                var physical = j.RootElement.GetProperty("physicalPassthrough")
                    .EnumerateArray().Select(x => x.GetString()).ToHashSet();
                if (!physical.SetEquals(new[] { emitter, sibling }) ||
                    File.Exists(Path.Combine(result.StagingDirectory, emitter)) ||
                    File.Exists(Path.Combine(result.StagingDirectory, sibling)) ||
                    !Directory.Exists(Path.GetDirectoryName(Path.Combine(result.StagingDirectory, emitter))))
                    throw new InvalidDataException("MO2_PHYSICAL_PASSTHROUGH_WINNERS_OR_PARENTS_INCORRECT");
            }
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
            // START discards uncollected output without archiving it.
            Mo2ProfilerOverlayBuilder.DiscardBeforeStart(result);
            Mo2ProfilerOverlayBuilder.DiscardBeforeStart(result);
            if (File.Exists(Path.Combine(overwrite, prior)) ||
                File.Exists(Path.Combine(overwrite, priorStatus)) ||
                File.Exists(Path.Combine(game, cetResult)) ||
                File.Exists(Path.Combine(overwrite, cetResult)) ||
                File.ReadAllText(Path.Combine(overwrite, unrelated)) != "unrelated Overwrite data" ||
                File.ReadAllText(Path.Combine(root, "RESULTS/Collected/GRSP_Summary.csv")) != "sealed capture" ||
                Directory.Exists(Path.Combine(local, "GCO", "MO2Bridge", "recovery", Session, "prior-grsp-results")))
                throw new InvalidDataException("MO2_START_UNCOLLECTED_DISCARD_OR_FOREIGN_PRESERVATION_FAILED");
            stack = Mo2SourceInspector.Resolve(game, instance, "Heavy");
            Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack);
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
            Put(overwrite, prior, "new uncollected capture");
            Put(result.StagingDirectory, cetResult, "new stage capture");
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
            var bridge = Path.Combine(local, "GCO", "MO2Bridge");
            var pending = Path.Combine(bridge, "instance-" + new string('a', 24) + ".processing.json");
            File.WriteAllText(pending, JsonSerializer.Serialize(new
            {
                sessionId = result.SessionId, operation = "launch",
                stagingRoot = result.StagingDirectory
            }));
            var foreignCommand = Path.Combine(bridge, "instance-" + new string('b', 24) + ".processing.json");
            File.WriteAllText(foreignCommand, JsonSerializer.Serialize(new { sessionId = new string('c', 32) }));
            void RequireClosed() => throw new InvalidOperationException("MO2 still running");
            if (Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, RequireClosed, deferPending: true) != 1 ||
                Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local) ||
                !Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore(local) ||
                !File.Exists(pending) || !Directory.Exists(result.StagingDirectory) ||
                !File.Exists(Path.Combine(overwrite, prior)) ||
                Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, RequireClosed, deferPending: true) != 0)
                throw new InvalidDataException("MO2_RESTORE_MUST_RESET_WITHOUT_CLOSING_MO2");
            RejectVerification(() => Mo2ProfilerOverlayBuilder.VerifyPrepared(result, stack));
            bool refused = false;
            try
            {
                Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, RequireClosed, deferredOnly: true);
            }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || !File.Exists(pending) ||
                !Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore(local))
                throw new InvalidDataException("MO2_VERIFY_RECOVERY_ACCEPTED_WHILE_BRIDGE_RUNNING");
            if (Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { }, deferredOnly: true) != 1 ||
                Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore(local) ||
                Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession(local) ||
                File.Exists(pending) || !File.Exists(foreignCommand) ||
                Directory.GetFiles(Path.Combine(bridge, "recovery", result.SessionId)).Length != 1 ||
                !Directory.Exists(result.StagingDirectory) ||
                File.Exists(Path.Combine(overwrite, prior)) ||
                File.Exists(Path.Combine(result.StagingDirectory, cetResult)) ||
                File.ReadAllText(Path.Combine(overwrite, unrelated)) != "unrelated Overwrite data" ||
                Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(local, () => { }, deferredOnly: true) != 0)
                throw new InvalidDataException("MO2_VERIFY_DEFERRED_RESTORE_OR_IDEMPOTENCE_FAILED");
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
