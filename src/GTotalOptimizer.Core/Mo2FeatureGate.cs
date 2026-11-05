namespace GTotalOptimizer.Core;

// MO2 directory/profile discovery is retained for a later milestone.
// The first integrated release must not install profilers, analyze effective
// MO2 files, or generate passes using unverified MO2 virtual filesystem state.
public static class Mo2FeatureGate
{
    public static readonly bool Enabled = false;
}
