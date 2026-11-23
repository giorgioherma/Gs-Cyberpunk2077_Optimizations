using GTotalOptimizer.Core;

internal static class Mo2RealCompanionProbeTests
{
    // A running official MO2 process is required; no fake Python mobase module
    // and no game launch. The command remains the read-only signed "probe".
    public static async Task RunAsync(string mo2Application, string instanceRoot,
        string expectedGame, string expectedProfile)
    {
        var instance = Mo2SourceInspector.Discover(instanceRoot);
        if (!instance.Recognized ||
            !instance.Profiles.Contains(expectedProfile, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2_REAL_GUI_INSTANCE_OR_PROFILE_NOT_ADMITTED");

        var live = Mo2CompanionDiscovery.Inspect(mo2Application, instance);
        if (!live.Connected)
            throw new InvalidDataException("MO2_REAL_GUI_PLUGIN_STATUS_NOT_VERIFIED: " + live.Detail);
        if (!Path.GetFullPath(live.GameDirectory).Equals(
                Path.GetFullPath(expectedGame), StringComparison.OrdinalIgnoreCase) ||
            live.CurrentProfile != expectedProfile)
            throw new InvalidDataException("MO2_REAL_GUI_WRONG_CURRENT_PROFILE_OR_GAME");

        var reply = await Mo2CompanionCommandClient.ProbeAsync(live);
        if (!reply.Ok || reply.ProcessId != 0 || reply.RequestId.Length != 32)
            throw new InvalidDataException("MO2_REAL_GUI_SIGNED_PROBE_NOT_ACKNOWLEDGED: " + reply.Detail);

        if (Mo2FeatureGate.Enabled || live.CanInstallProfilers || live.CanMutateSelectedProfile)
            throw new InvalidDataException("MO2_NATIVE_GUI_PROBE_UNEXPECTEDLY_GRANTED_DEPLOYMENT");

        Console.WriteLine("PASS official MO2 running GUI Python plugin registered: status PID=" +
            live.ProcessId + " profile=" + live.CurrentProfile +
            " with authentic GCO HMAC probe; no profiler install, game launch or modlist edits");
    }
}
