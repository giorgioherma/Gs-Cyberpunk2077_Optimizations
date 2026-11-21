namespace GTotalOptimizer.Core;

/// <summary>
/// Counts actual completed milestones of a single operation, not elapsed time.
/// </summary>
public readonly record struct WorkflowStepProgress(int Completed, int Total, string Detail);
