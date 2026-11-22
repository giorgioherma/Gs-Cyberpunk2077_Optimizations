namespace GTotalOptimizer.App;

/// <summary>
/// UI-only estimation between real source-owner progress checkpoints.
/// Work completion and acceptance are never inferred from elapsed time.
/// </summary>
internal sealed class CheckpointProgressSmoother
{
    public const double EstimatedCeiling = 98.5;
    private double confirmedPercent;
    private double displayedPercent;
    private double lastRealSeconds;
    private double lastTickSeconds;
    private double stepWidthPercent;
    private double estimatedStepSeconds;
    private bool haveTimingSample;
    private bool running;

    public double Value => displayedPercent;

    public void Start(int stageCount)
    {
        confirmedPercent = 0;
        displayedPercent = 0;
        lastRealSeconds = 0;
        lastTickSeconds = 0;
        stepWidthPercent = 100.0 / Math.Max(1, stageCount);
        estimatedStepSeconds = 8.0; // initial visual pace; never used for completion
        haveTimingSample = false;
        running = true;
    }

    public void Confirm(int completed, int total, double elapsedSeconds)
    {
        if (!running || total <= 0 || !double.IsFinite(elapsedSeconds)) return;
        if (completed < 0 || completed > total) return;
        var percent = completed * 100.0 / total;
        if (percent <= confirmedPercent) return;

        var delta = percent - confirmedPercent;
        var seconds = Math.Max(0, elapsedSeconds - lastRealSeconds);
        var stageUnits = delta / stepWidthPercent;
        if (seconds >= 0.05 && stageUnits > 0)
        {
            // Learn an interval from this machine's actual measured checkpoints.
            // A jump over several stages contributes one normalized sample.
            var measured = Math.Clamp(seconds / stageUnits, 0.5, 120.0);
            estimatedStepSeconds = haveTimingSample
                ? 0.65 * estimatedStepSeconds + 0.35 * measured
                : measured;
            haveTimingSample = true;
        }

        confirmedPercent = percent;
        lastRealSeconds = Math.Max(lastRealSeconds, elapsedSeconds);
        stepWidthPercent = 100.0 / total;
    }

    public double Advance(double elapsedSeconds)
    {
        if (!running || !double.IsFinite(elapsedSeconds)) return displayedPercent;
        elapsedSeconds = Math.Max(lastTickSeconds, elapsedSeconds);
        var dt = Math.Min(0.5, elapsedSeconds - lastTickSeconds);
        lastTickSeconds = elapsedSeconds;
        if (dt <= 0) return displayedPercent;

        // A slow stage always leaves some runway: the estimate continuously
        // approaches, but never crosses, the next real checkpoint.
        var sinceCheckpoint = Math.Max(0, elapsedSeconds - lastRealSeconds);
        var fraction = sinceCheckpoint / (estimatedStepSeconds + sinceCheckpoint);
        var runway = Math.Max(0, Math.Min(stepWidthPercent * 0.98,
            EstimatedCeiling - confirmedPercent));
        var target = Math.Min(EstimatedCeiling,
            Math.Max(confirmedPercent, confirmedPercent + runway * fraction));
        target = Math.Max(displayedPercent, target);

        // Catch up smoothly when a real milestone arrives ahead of the visual
        // estimate; do not teleport the bar by a whole stage.
        var ratePerSecond = Math.Clamp(2.0 + 4.0 * (target - displayedPercent), 2.0, 60.0);
        displayedPercent = Math.Min(target, displayedPercent + dt * ratePerSecond);
        return displayedPercent;
    }

    public void Stop() => running = false;
}
