using GTotalOptimizer.Core;

internal static class Mo2ContinueTests
{
    public static async Task RunAsync(string root, string game, string instance, string local,
        Mo2Stack stack, Func<string, CancellationToken, Task> install)
    {
        const string id = "9876543210abcdef9876543210abcdef";
        var prepared = await Mo2ProfilerOverlayBuilder.PrepareAsync(game, stack, id, install,
            localAppData: local, cetResultPatterns: ["CET_Runtime_Profile_*.csv"]);
        var package = Path.Combine(root, "continue-package");
        var workflow = new UnifiedProfilerWorkflow(package, local);
        var selection = new Mo2ProfileSelection(true, "", instance, instance, game,
            prepared.Profile, [prepared.Profile], stack);
        workflow.BeginMo2CaptureAttempt(selection, prepared);
        Mo2ProfilerOverlayBuilder.DiscardBeforeStart(prepared);
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe", "/d /c exit 0")
            { UseShellExecute = false, CreateNoWindow = true })!;
        child.WaitForExit();
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending", "test-launched", child.Id);

        // Synthetic archive indexes isolate CONTINUE's lifecycle; real owner
        // completion and collection are exercised by Mo2RealCollectionTests.
        foreach (var kind in new[] { BackendKind.Cet, BackendKind.Grsp })
        {
            var archive = Path.Combine(root, "continue-owner-" + kind);
            Directory.CreateDirectory(archive);
            File.WriteAllText(Path.Combine(archive,
                kind == BackendKind.Cet ? "CET_Summary.json" : "GRSP_Summary.json"), "{}");
            CollectedCaptureStore.CopyOwner(package, game, id, kind, archive);
        }
        CollectedCaptureStore.MarkCollectInvoked(package, game, id);
        CollectedCaptureStore.MarkCollectionAttempted(package, game, id, true);
        var wrong = await workflow.ContinueAsync(game, false, false, true,
            mo2Selection: selection with { Profile = "Other" });
        if (wrong.Ok || !workflow.HasRecoveryJournal)
            throw new InvalidDataException("MO2_CONTINUE_WRONG_PROFILE_ACCEPTED");
        const string relative = "bin/x64/plugins/cyber_engine_tweaks/mods/SelectedMod/init.lua";
        var original = stack.Winner(relative)!;
        var bytes = File.ReadAllBytes(original);
        try
        {
            File.AppendAllText(original, "\n-- source changed");
            var drift = await workflow.ContinueAsync(game, false, false, true, mo2Selection: selection);
            if (drift.Ok || !workflow.HasRecoveryJournal)
                throw new InvalidDataException("MO2_CONTINUE_SOURCE_DRIFT_ACCEPTED");
        }
        finally { File.WriteAllBytes(original, bytes); }
        var completed = await workflow.ContinueAsync(game, false, false, true, mo2Selection: selection);
        var captured = CollectedCaptureStore.ReadRestored(package, game, id);
        if (!completed.RestoreVerified || workflow.HasRecoveryJournal ||
            captured.Mo2Source?.Profile != selection.Profile ||
            captured.Mo2Source.InstanceDirectory != instance ||
            Mo2ProfilerOverlayBuilder.CanRestartCompletedTest(prepared))
            throw new InvalidDataException("MO2_CONTINUE_NOT_RESTORED_OR_BINDING_LOST");

        // Selected-profile winners, not the physical shadowed files, must
        // reach both resolvers. Reproject current changes without touching
        // captured evidence; refuse concurrent drift before publication.
        string projected;
        using (var source = Mo2AnalysisSource.Create(captured))
        {
            projected = source.Root;
            if (File.ReadAllText(Path.Combine(projected, relative)) != "original_mod_callback()" ||
                new FileInfo(Path.Combine(projected, "bin/x64/Cyberpunk2077.exe")).Length != 0)
                throw new InvalidDataException("MO2_PAGE2_WRONG_SOURCE_WINNER");
            source.RequireBinding(captured);
            try
            {
                File.AppendAllText(original, "\n-- live drift");
                try
                {
                    source.VerifyCurrent();
                    throw new Exception("MO2_PAGE2_SOURCE_DRIFT_ACCEPTED");
                }
                catch (InvalidDataException) { }
            }
            finally { File.WriteAllBytes(original, bytes); }
            source.VerifyCurrent();
        }
        if (Directory.Exists(projected) ||
            !File.ReadAllBytes(original).SequenceEqual(bytes))
            throw new InvalidDataException("MO2_PAGE2_PROJECTION_CLEANUP_FAILED");
        Console.WriteLine("PASS MO2 shared Continue, original source restoration, pinned effective source and drift refusal");
    }
}
