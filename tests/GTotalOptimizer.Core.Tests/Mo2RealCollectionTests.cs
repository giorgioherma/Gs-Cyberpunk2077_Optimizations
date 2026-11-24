using GTotalOptimizer.Core;
using System.IO.Compression;

internal static class Mo2RealCollectionTests
{
    // Replay raw recorded CSV bytes through the real packaged owners; this
    // is a disposable collection contract, not MO2 gameplay evidence.
    public static async Task RunAsync(string package, string game, string instance,
        string local, Mo2OverlayPreparation prepared, bool verifyPage2 = false)
    {
        var workflow = new UnifiedProfilerWorkflow(package, local);
        if (workflow.HasRecoveryJournal)
            throw new InvalidDataException("MO2_REAL_COLLECTION_TEST_PACKAGE_NOT_CLEAN");
        var selection = new Mo2ProfileSelection(true, "", instance, instance,
            game, prepared.Profile, [prepared.Profile],
            Mo2SourceInspector.Resolve(game, Mo2SourceInspector.Discover(instance), prepared.Profile));
        workflow.BeginMo2CaptureAttempt(selection, prepared);
        string sessionFolder = CollectedCaptureStore.SessionFolder(package, prepared.SessionId);
        var overwrite = selection.CandidateSource!.OverwriteDirectory!;
        const string cetRoot = "bin/x64/plugins/cyber_engine_tweaks";
        const string redRoot = "red4ext/plugins/G-REDscript-Profiler/RESULTS/Capture_0001_REPLAY_1000";
        var grspDir = Path.Combine(overwrite, redRoot);
        Directory.CreateDirectory(grspDir);
        try
        {
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
            using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/d /c exit 0")
                { UseShellExecute = false, CreateNoWindow = true })!;
            child.WaitForExit();
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending", "test-launched", child.Id);

            // The older fixture is sufficient for owner collection but does not
            // carry the full newer REDscript Resolver evidence contract.
            // For Page 2 use the existing, independently verified GRSP replay
            // fixture, with BOTH domains from that same recorded capture.
            string zip = Path.GetFullPath(verifyPage2
                ? "tests/fixtures/real-captures/GCO-Capture-15112026_155121.zip"
                : "tests/fixtures/real-captures/GCO-Capture-15112026_021913.zip");
            if (!File.Exists(zip)) throw new FileNotFoundException("Recorded raw capture fixture missing.", zip);
            var bundle = ComponentBundleVerifier.Inspect(package);
            var patterns = Mo2NativeOutputReset.ReadCetPatterns(bundle.CetExecutable!);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = ZipFile.OpenRead(zip))
            foreach (var entry in archive.Entries)
            {
                if (entry.Name.Length == 0) continue;
                if (verifyPage2)
                {
                    // Exclude derived Reports copies. Only the indexed owner
                    // folders contain the original native measurement bytes.
                    var parts = entry.FullName.Replace('\\', '/').Split('/');
                    if (parts.Any(part => part.Equals("Reports", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    if (!parts.Any(part => part is "G-CET" or "CET" or "G-REDscript" or "G-RED" or "REDscript"))
                        continue;
                }
                string? path = null;
                if (Mo2NativeOutputReset.IsNativeResult(cetRoot + "/" + entry.Name, patterns))
                    path = Path.Combine(game, cetRoot, entry.Name);
                else if ((entry.Name.StartsWith("GRSP_", StringComparison.OrdinalIgnoreCase) ||
                          entry.Name.StartsWith("RSP_", StringComparison.OrdinalIgnoreCase)) &&
                         (entry.Name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                          entry.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase)))
                {
                    // Reverse only the owner's archive layout for the recorded
                    // Page 2 input: raw native developer CSVs are in Developer/.
                    // Flattening them makes the actual manager classify them as
                    // Metadata, so its canonical analysis archive is incomplete.
                    var parts = entry.FullName.Replace('\\', '/').Split('/');
                    int developer = Array.FindIndex(parts, part =>
                        part.Equals("Developer", StringComparison.OrdinalIgnoreCase));
                    path = verifyPage2 && developer >= 0
                        ? Path.Combine(grspDir, "Developer", Path.Combine(parts[(developer + 1)..]))
                        : Path.Combine(grspDir, entry.Name);
                }
                if (path is null) continue;
                if (!written.Add(path))
                    throw new InvalidDataException("Duplicate native replay file: " + entry.Name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: false);
            }
            if (verifyPage2)
                foreach (var required in new[] { "GRSP_Markers.csv", "RSP_CallSites.csv",
                    "RSP_CallbackPressure.csv", "RSP_CallbackTargets.csv", "GRSP_Status.txt",
                    "RSP_FunctionMap.csv", "RSP_Cadence.csv", "RSP_WorkMap.csv",
                    "GRSP_ByFunction.csv", "GRSP_Frames.csv", "GRSP_ByMod.csv" })
                    if (!File.Exists(Path.Combine(grspDir, required)) &&
                        !File.Exists(Path.Combine(grspDir, "Developer", required)))
                        throw new InvalidDataException("MO2_PAGE2_REPLAY_NATIVE_EVIDENCE_MISSING: " + required);
            string marker = Path.Combine(grspDir, "GCO-F11-Complete.flag");
            var incomplete = await workflow.InspectMo2CaptureAsync(game, selection);
            if (!incomplete.BothInstalled || !incomplete.CetCaptureReady || incomplete.GrspCaptureReady)
                throw new InvalidDataException("MO2_REAL_OWNER_INCOMPLETE_F11_ADMISSION_WRONG");
            // Explicit synthetic F11 flag exercises the owner's current
            // completion gate; archived historic measurements are unchanged.
            File.WriteAllText(marker, "F11_STOP");
            var ready = await workflow.InspectMo2CaptureAsync(game, selection);
            if (!ready.BothCaptured)
                throw new InvalidDataException("MO2_REAL_OWNER_F11_READY_NOT_RECOGNIZED");
            workflow.RecordCollectInvoked(game, selection);
            var result = await workflow.CollectResultsAsync(game, mo2Selection: selection);
            if (!result.Ok) throw new InvalidDataException("MO2_REAL_OWNER_COLLECTION_FAILED: " + result.Detail);
            var index = workflow.RecordCollectionAttempt(game, true, selection);
            if (index.Cet is null || index.Redscript is null || !index.CollectionAttempted ||
                !CaptureAdmissionPolicy.CanContinue(index, true))
                throw new InvalidDataException("MO2_REAL_OWNER_COLLECTION_INDEX_INCOMPLETE");
            _ = workflow.ReadCollected(game, verifyHashes: true);
            if (verifyPage2)
            {
                // Diagnose the actual owner archive, not just the raw fixture.
                // Keep the production Resolver's complete admission intact.
                var archived = index.Redscript!.Files.Select(file => file.Path)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var required in new[] { "GRSP_Summary.json",
                    "Data/Metadata/GRSP_Status.txt", "Data/Metadata/GCO-F11-Complete.flag",
                    "Data/Runtime/GRSP_Markers.csv", "Data/Developer/RSP_CallSites.csv",
                    "Data/Developer/RSP_CallbackPressure.csv", "Data/Developer/RSP_CallbackTargets.csv" })
                    if (!archived.Contains(required))
                        throw new InvalidDataException("MO2_PAGE2_OWNER_ARCHIVE_MISSING: " + required);
            }
            if (written.Any(File.Exists) || File.Exists(marker))
                throw new InvalidDataException("MO2_REAL_OWNER_LIVE_OUTPUT_NOT_RESET");
            if (verifyPage2)
            {
                var restored = await workflow.ContinueAsync(game, false, false, true,
                    mo2Selection: selection);
                if (!restored.RestoreVerified)
                    throw new InvalidDataException("MO2_REAL_OWNER_CONTINUE_FAILED: " + restored.Detail);
                await Mo2RealGenerationTests.RunAsync(package, game, instance, prepared.SessionId);
            }
            else
            {
                Mo2ProfilerOverlayBuilder.RetireCompletedTest(prepared);
                workflow.RetireMo2CaptureAttempt(game, prepared.SessionId);
            }
            if (!Directory.Exists(sessionFolder) || workflow.HasRecoveryJournal)
                throw new InvalidDataException("MO2_REAL_OWNER_SEALED_CAPTURE_LOST");
            Console.WriteLine("PASS actual packaged CET/GRSP owners collect projected recorded raw files; F11 gate and sealed evidence verified");
        }
        finally
        {
            // The downloaded package and source fixtures are disposable test
            // inputs; remove only this test's newly created indexed capture.
            if (workflow.HasMo2CaptureAttempt &&
                workflow.ReadRecoverySessionId(game) == prepared.SessionId)
                workflow.RetireMo2CaptureAttempt(game, prepared.SessionId);
            if (Directory.Exists(sessionFolder))
                Directory.Delete(sessionFolder, recursive: true);
        }
    }
}
