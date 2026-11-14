namespace GTotalOptimizer.Core;

public static class CaptureAdmissionPolicy
{
    // Completed native F11 output from BOTH owners is required. An interrupted
    // Collect may have indexed or staged one domain; never accept a stale
    // pre-attempt result or PresentMon-only evidence as the missing domain.
    public static (bool Cet, bool Redscript) AvailableNativeCaptures(
        LiveProfilerState? state, CollectedCaptureIndex? capture,
        bool cetStaged = false, bool redscriptStaged = false)
    {
        if (state?.BothInstalled != true) return (false, false);
        return (
            capture?.Cet is not null ||
                (capture?.CetReadyBeforeAttempt != true && (state.CetCaptureReady || cetStaged)),
            capture?.Redscript is not null ||
                (capture?.RedscriptReadyBeforeAttempt != true && (state.GrspCaptureReady || redscriptStaged)));
    }

    // This attempt's COLLECT is one-shot even if collection fails. START makes
    // a new capture identity; RESTORE invokes the same source-owner restorers.
    public static bool CanCollect(bool hasOwnedSession, bool launchAttemptStarted,
        LiveProfilerState? state, CollectedCaptureIndex? capture = null,
        bool cetStaged = false, bool redscriptStaged = false)
    {
        var ready = AvailableNativeCaptures(state, capture, cetStaged, redscriptStaged);
        return hasOwnedSession && launchAttemptStarted && ready.Cet && ready.Redscript &&
            capture?.CollectInvoked != true && capture?.CollectionAttempted != true;
    }

    public static string PendingCaptureGuidance((bool Cet, bool Redscript) ready) =>
        "CET " + (ready.Cet ? "READY" : "MISSING") +
        " | REDscript " + (ready.Redscript ? "READY" : "MISSING") +
        (ready.Cet && ready.Redscript
            ? ". Click COLLECT RESULTS."
            : ". START again or RESTORE.");

    public static bool CanContinue(CollectedCaptureIndex? capture, bool presentMonStopped) =>
        presentMonStopped && capture?.PresentMonStopVerified == true && capture.CollectionAttempted &&
        capture.Cet is not null && capture.Redscript is not null;

    public static string CollectionStatus(CollectedCaptureIndex capture,
        string? monWarning = null, string? nativeWarning = null)
    {
        static string State(bool present) => present ? "OK" : "MISSING";
        var status = "CET " + State(capture.Cet is not null) +
            " | REDscript " + State(capture.Redscript is not null) +
            " | PresentMon " + State(capture.FrameTimeCsv is not null) + ".";

        if (!capture.PresentMonStopVerified)
            return status + " Collect ended. START again or RESTORE.";
        if (capture.Cet is null || capture.Redscript is null)
        {
            if (capture.CetReadyBeforeAttempt || capture.RedscriptReadyBeforeAttempt)
                return status + " Old native data excluded. Continue unavailable.";
            return status + " Continue unavailable.";
        }
        if (!string.IsNullOrWhiteSpace(monWarning) ||
            !string.IsNullOrWhiteSpace(nativeWarning))
            return status + " Continue available; check details.";
        return status + " Continue available.";
    }
}
