namespace GTotalOptimizer.Core;

// Payload installation evidence must come from the authoritative profiler
// owners' verified install/status transactions. Keybind discovery is independent.
public sealed record ProfilerSetupView(
    bool CetInstalled, bool RedscriptInstalled, bool CapXInstalled,
    CaptureKeyCheck CetKey, CaptureKeyCheck CapXKey)
{
    // CapFrameX is optional frame-time measurement. CET + GRSP are the actual
    // required native profilers; a CapX X is still shown if its payload is absent.
    public string Heading => CetInstalled && RedscriptInstalled
        ? "PROFILERS INSTALLED" : "PROFILERS NOT INSTALLED";

    public string CetKeyDisplay => Display(CetKey);
    public string CapXKeyDisplay => Display(CapXKey);

    public string? SynchronizationWarning
    {
        get
        {
            var offenders = new List<string>();
            if (Mismatch(CetKey)) offenders.Add("CET");
            if (Mismatch(CapXKey)) offenders.Add("CapX");
            if (offenders.Count == 0) return null;
            var names = string.Join(" and ", offenders);
            return "Set " + names + " keybind" + (offenders.Count > 1 ? "s" : "") +
                " to F11. REDscript profiler currently uses hardcoded F11.";
        }
    }

    private static string Display(CaptureKeyCheck item) =>
        item.Value switch
        {
            "Pending" or "Optional" or "Unavailable" => "F11 (preset, not detected)",
            _ => item.Value
        };

    private static bool Mismatch(CaptureKeyCheck item) =>
        !item.Confirmed && item.Value is not
            ("Pending" or "Optional" or "Unavailable" or "Unknown");

    public static ProfilerSetupView Unverified(CaptureKeyCheck? cet = null,
        CaptureKeyCheck? capx = null) =>
        new(false, false, false,
            cet ?? new CaptureKeyCheck("CET", "Pending", false, "Not yet detected."),
            capx ?? new CaptureKeyCheck("CapFrameX", "Optional", false, "Not yet bundled."));
}

// User-visible first-page copy is centralized for exact layout/content tests.
public static class SetupCopy
{
    public const string CaptureStartInstruction =
        "Start Cyberpunk 2077 and load into the world. Press F11 to START; press F11 again to STOP.\r\n" +
        "REDscript uses audio signals; CET displays its capture timer in the upper left corner.";

    public const string StepInstall =
        "1. INSTALL PROFILERS — CET controls / scheduler + GRSP. Component verification required.";
    public const string StepStart =
        "2. START CYBERPUNK — Record CET and REDscript; frame-time currently disabled.";
    public const string StepCollect =
        "3. COLLECT RESULTS — Copy completed captures; Continue restores originals.";

    public const string CapFrameXProcess =
        "Frame-time provider paused after crash; CET + REDscript captures remain available.";

    public const string CaptureDetails =
        "Stay in-world. Use an old save or Phantom Liberty start; aim for heavy combat.\r\n" +
        "If you're having issues getting a longer combat capture, fight police. If you die, stop recording with F11 before loading.\r\n" +
        "Shorter captures also work. Close the game, return to this screen and use COLLECT RESULTS.";
}
