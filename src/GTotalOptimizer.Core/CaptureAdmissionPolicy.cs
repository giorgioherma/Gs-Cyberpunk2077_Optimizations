namespace GTotalOptimizer.Core;

public static class CaptureAdmissionPolicy
{
    // Collect is an explicit one-time completion per capture attempt, not a
    // permanent launch-state toggle. A failed PresentMon stop may be retried;
    // after verified shutdown the button stays disabled even if no native
    // F11 capture exists. BeginCaptureAttempt assigns a new session ID and
    // therefore a fresh uncollected index for the next game run.
    public static bool CanCollect(bool hasOwnedSession, bool launchAttemptStarted,
        CollectedCaptureIndex? capture = null) =>
        hasOwnedSession && launchAttemptStarted &&
        !(capture?.CollectionAttempted == true && capture.PresentMonStopVerified);

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
            return status + " PresentMon stop failed; retry Collect.";
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
