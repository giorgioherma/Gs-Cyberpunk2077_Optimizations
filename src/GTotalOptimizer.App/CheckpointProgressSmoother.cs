namespace GTotalOptimizer.App;

/// <summary>
/// UI-only interpolation between real source-owner progress checkpoints.
/// Relative phase weights begin with the owner's representative five-minute
/// capture timings. They are estimates, never work-completion evidence.
/// </summary>
internal sealed class CheckpointProgressSmoother
{
    public const double EstimatedCeiling = 99.0;
    private double[] baseline = [0, 8];
    private int[] timingAnchors = [1];
    private double confirmedStage;
    private double confirmedPercent;
    private double displayedPercent;
    private double lastRealSeconds;
    private double lastTickSeconds;
    private int lastTimingAnchor;
    private double lastTimingSeconds;
    private double durationScale;
    private bool haveTimingSample;
    private bool running;

    public double Value => displayedPercent;

    public void Start(int stageCount)
    {
        // These cumulative seconds follow the actual producer checkpoint order.
        // Collect: native collection/admission (~48s), reports (~44s).
        // Analyze: CET analysis, REDscript plan, CET preview, REDscript preview,
        // then final availability/cleanup.
        // Generate: CET (~73s), REDscript analysis + staging (~79s),
        // then combined staging, ZIP and publication (provisional 10s reserve).
        // Internal substage splits are provisional, not separately timed.
        (baseline, timingAnchors) = stageCount switch
        {
            10 => (new double[] { 0, 2, 44, 46, 48, 51, 65, 81, 87, 90, 92 },
                   new int[] { 4, 10 }),
            5 => (new double[] { 0, 57, 80, 146, 228, 230 },
                  new int[] { 1, 2, 3, 4, 5 }),
            6 => (new double[] { 0, 73, 83, 152, 154, 160, 162 },
                  new int[] { 1, 3, 6 }),
            _ => (Enumerable.Range(0, Math.Max(1, stageCount) + 1)
                       .Select(i => i * 8.0).ToArray(),
                  Enumerable.Range(1, Math.Max(1, stageCount)).ToArray())
        };
        confirmedStage = 0;
        confirmedPercent = 0;
        displayedPercent = 0;
        lastRealSeconds = 0;
        lastTickSeconds = 0;
        lastTimingAnchor = 0;
        lastTimingSeconds = 0;
        durationScale = 1;
        haveTimingSample = false;
        running = true;
    }

    private double BaselineAt(double stage)
    {
        int lower = (int)Math.Floor(stage);
        if (lower >= baseline.Length - 1) return baseline[^1];
        return baseline[lower] +
            (baseline[lower + 1] - baseline[lower]) * (stage - lower);
    }

    public void Confirm(int completed, int total, double elapsedSeconds)
    {
        if (!running || total <= 0 || !double.IsFinite(elapsedSeconds)) return;
        if (completed < 0 || completed > total || elapsedSeconds < lastRealSeconds) return;

        // Fractional ZIP-entry progress maps to the same weighted stage, not
        // to a new series of equal-width stages or new machine-speed samples.
        double stage = completed * (double)(baseline.Length - 1) / total;
        if (stage <= confirmedStage) return;
        int integerStage = (int)Math.Round(stage);
        if (Math.Abs(stage - integerStage) < 1e-9)
            stage = integerStage;

        if (stage == integerStage && integerStage > lastTimingAnchor &&
            Array.IndexOf(timingAnchors, integerStage) >= 0)
        {
            double expected = baseline[integerStage] - baseline[lastTimingAnchor];
            double actual = elapsedSeconds - lastTimingSeconds;
            // Do not let tiny cleanup/publication callbacks, or a burst of
            // ZIP-entry notifications, overwrite the useful machine-speed sample.
            if (expected >= 8 && actual >= 0.05)
            {
                double sample = Math.Clamp(actual / expected, 0.1, 10.0);
                durationScale = haveTimingSample
                    ? 0.4 * durationScale + 0.6 * sample
                    : sample;
                haveTimingSample = true;
            }
            lastTimingAnchor = integerStage;
            lastTimingSeconds = elapsedSeconds;
        }

        confirmedStage = stage;
        confirmedPercent = 100.0 * BaselineAt(stage) / baseline[^1];
        lastRealSeconds = elapsedSeconds;
    }

    public double Advance(double elapsedSeconds)
    {
        if (!running || !double.IsFinite(elapsedSeconds)) return displayedPercent;
        elapsedSeconds = Math.Max(lastTickSeconds, elapsedSeconds);
        var dt = Math.Min(0.5, elapsedSeconds - lastTickSeconds);
        lastTickSeconds = elapsedSeconds;
        if (dt <= 0) return displayedPercent;

        double nextStage = Math.Min(baseline.Length - 1, Math.Floor(confirmedStage) + 1);
        double remainingBaseline = BaselineAt(nextStage) - BaselineAt(confirmedStage);
        double predictedSeconds = Math.Max(0.15, remainingBaseline * durationScale);
        double ratio = Math.Max(0, elapsedSeconds - lastRealSeconds) / predictedSeconds;
        // Cover 95% of this interval at predicted arrival. If work takes
        // longer, ease to exactly 99% of this interval and hold there until
        // its real checkpoint arrives. Distribute waiting across the flow.
        double late = Math.Clamp((ratio - 1) / 0.25, 0.0, 1.0);
        double fraction = ratio <= 1
            ? 0.95 * ratio
            : 0.95 + 0.04 * late * (2 - late);
        double width = 100.0 * remainingBaseline / baseline[^1];
        double target = Math.Min(EstimatedCeiling,
            confirmedPercent + width * fraction);
        target = Math.Max(displayedPercent, target);

        // Retain smooth catch-up when real work arrives early, and never
        // regress or display 100% until the operation owner confirms success.
        var ratePerSecond = Math.Clamp(2.0 + 4.0 * (target - displayedPercent), 2.0, 60.0);
        displayedPercent = Math.Min(target, displayedPercent + dt * ratePerSecond);
        return displayedPercent;
    }

    public void Stop() => running = false;
}
