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
        reopened.RetireMo2CaptureAttempt(game, id);
        if (reopened.HasRecoveryJournal || Directory.Exists(folder))
            throw new InvalidDataException("MO2_UNCOLLECTED_CAPTURE_NOT_RETIRED");
        Console.WriteLine("PASS MO2 shared capture identity, Direct refusal and uncollected cleanup");
    }
}
