namespace GTotalOptimizer.Core;

// Display-only projection of live game inspection. A valid executable is not
// equivalent to an installed 0-Engine dependency.
public sealed record NormalSourcePresentation(
    string StatusText, bool IsWarning, bool ShowEngineLink,
    bool GamePathConfirmed);

public static class SetupSourcePresentation
{
    public static NormalSourcePresentation ForNormal(GameInspection inspection, int redscriptModCount)
    {
        if (!inspection.IsValidGame)
            return new("Select the Cyberpunk 2077 Game root - /Cyberpunk2077",
                IsWarning: true, ShowEngineLink: false, GamePathConfirmed: false);

        var hasEngine = inspection.Checks.Any(check =>
            check.Name == "0-Engine source" && check.State != CheckState.Missing);
        if (!hasEngine)
            return new("Game found; 0-ENGINE MISSING!!",
                IsWarning: true, ShowEngineLink: true, GamePathConfirmed: true);

        return new($"Game found. {inspection.CetModCount} CET mods and {redscriptModCount} REDscript mods.",
            IsWarning: false, ShowEngineLink: false, GamePathConfirmed: true);
    }
}
