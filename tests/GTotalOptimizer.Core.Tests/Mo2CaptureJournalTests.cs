using GTotalOptimizer.Core;

internal static class Mo2CaptureJournalTests
{
    public static async Task RunAsync(string packageRoot, string game, string instance,
        Mo2Stack stack, Mo2OverlayPreparation prepared)
    {
        var workflow = new UnifiedProfilerWorkflow(packageRoot);
        var selected = new Mo2ProfileSelection(true, "", instance, instance,
            game, prepared.Profile, [prepared.Profile], stack);

        // The same sealed owner fixture must supply the package identity.
        workflow.BeginMo2CaptureAttempt(selected, prepared);
        var id = prepared.SessionId;
        var index = workflow.ReadCollected(game, verifyHashes: true);
        if (!workflow.HasMo2CaptureAttempt || !workflow.HasRecoveryJournal ||
            workflow.ReadRecoverySessionId(game) != id || index?.SessionId != id ||
            index.HasAny || index.CollectionAttempted || index.CollectInvoked ||
            workflow.HasCaptureAttempt(game))
            throw new InvalidDataException("MO2_CAPTURE_IDENTITY_OR_DIRECT_ADMISSION_INVALID");

        // Tray reopen has no in-memory overlay. It must reconstruct the
        // acknowledged deployment from the same package identity, never
        // from a different currently selected profile or an uncertain launch.
        var local = Directory.GetParent(Directory.GetParent(
            Path.GetDirectoryName(prepared.JournalFile)!)!.FullName)!.FullName;
        var resumed = new UnifiedProfilerWorkflow(packageRoot);
        var binding = resumed.ReadMo2CaptureIdentity();
        if (binding is null || !binding.Matches(selected) || binding.SessionId != id ||
            resumed.ResumeMo2CaptureAttempt(selected, local) is not null)
            throw new InvalidDataException("MO2_PREPARED_CAPTURE_FALSELY_RESUMED_AS_LAUNCHED");
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
        if (resumed.ResumeMo2CaptureAttempt(selected, local) is not null)
            throw new InvalidDataException("MO2_PENDING_LAUNCH_FALSELY_RESUMED");
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending", "test-launched", 1);
        try
        {
            var recovered = resumed.ResumeMo2CaptureAttempt(selected, local);
            if (recovered is null || recovered.SessionId != id ||
                recovered.StagingDirectory != prepared.StagingDirectory ||
                !recovered.Files.SequenceEqual(prepared.Files))
                throw new InvalidDataException("MO2_ACKNOWLEDGED_CAPTURE_NOT_REHYDRATED");
            try
            {
                resumed.ResumeMo2CaptureAttempt(selected with { Profile = "Other" }, local);
                throw new Exception("MO2_RESUMED_WRONG_PROFILE");
            }
            catch (InvalidDataException) { }
            try
            {
                resumed.ResumeMo2CaptureAttempt(selected with {
                    InstanceDirectory = Path.Combine(instance, "other") }, local);
                throw new Exception("MO2_RESUMED_WRONG_INSTANCE");
            }
            catch (InvalidDataException) { }
            try
            {
                resumed.ResumeMo2CaptureAttempt(selected with {
                    CandidateSource = stack with { OverwriteDirectory = Path.Combine(instance, "other-output") } }, local);
                throw new Exception("MO2_RESUMED_CHANGED_OUTPUT_ROOT");
            }
            catch (InvalidDataException) { }
        }
        finally
        {
            // Synthetic acknowledgement only; restore the fixture's
            // unlaunched phase for the original owner-overlay tests.
            Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launched", "prepared");
        }

        // A matching game path alone must never authorize the physical owner
        // managers for an MO2 attempt, including recovery after reopen.
        var reopened = new UnifiedProfilerWorkflow(packageRoot);
        if (!reopened.HasMo2CaptureAttempt ||
            (await reopened.ResetNativeOutputAsync(game)).Ok ||
            (await reopened.RestoreOwnedAsync(game)).Ok ||
            (await reopened.CollectResultsAsync(game)).Ok ||
            (await reopened.ContinueAsync(game, false, false, false)).Ok ||
            reopened.ReadRecoverySessionId(game) != id)
            throw new InvalidDataException("MO2_CAPTURE_JOURNAL_ADMITTED_DIRECT_OWNER_OPERATION");

        try
        {
            reopened.BeginMo2CaptureAttempt(selected, prepared);
            throw new Exception("MO2_CAPTURE_OVERWROTE_EXISTING_JOURNAL");
        }
        catch (InvalidOperationException) { }
        try
        {
            reopened.RetireMo2CaptureAttempt(game, Guid.NewGuid().ToString("N"));
            throw new Exception("MO2_CAPTURE_FOREIGN_ID_DISCARDED");
        }
        catch (InvalidOperationException) { }

        // This fixture never dispatches a real game or starts a frame writer.
        // The production caller retires its deployment before invoking this.
        var folder = CollectedCaptureStore.SessionFolder(packageRoot, id);
        // A lost bridge acknowledgement must not delete a controller that
        // may still be starting. The original controller settles this proof.
        var frame = Path.Combine(folder, "PresentMon");
        Directory.CreateDirectory(frame);
        var proof = Path.Combine(frame, "PresentMon-controller.json");
        File.WriteAllText(proof, """{"ok":false,"pending":true}""");
        try
        {
            reopened.RetireMo2CaptureAttempt(game, id);
            throw new Exception("MO2_PENDING_FRAME_DISPATCH_DISCARDED");
        }
        catch (InvalidOperationException) { }
        if (!reopened.HasRecoveryJournal || !File.Exists(proof))
            throw new InvalidDataException("MO2_PENDING_FRAME_EVIDENCE_LOST");
        File.WriteAllText(proof, """{"ok":false,"detail":"controller exited"}""");
        reopened.RetireMo2CaptureAttempt(game, id);
        if (reopened.HasRecoveryJournal || Directory.Exists(folder))
            throw new InvalidDataException("MO2_UNCOLLECTED_CAPTURE_NOT_RETIRED");
        Console.WriteLine("PASS MO2 shared capture identity, Direct refusal and uncollected cleanup");
    }
}
