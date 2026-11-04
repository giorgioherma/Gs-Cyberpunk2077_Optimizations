namespace GTotalOptimizer.Core;

public enum CheckState { Present, Missing, Unverified }

public sealed record ComponentCheck(string Name, CheckState State, string Detail);

public enum SourceKind { None, GameRoot, LiveCetMods, ExternalCetMods }

public sealed record GameInspection(
    bool IsValidGame,
    IReadOnlyList<ComponentCheck> Checks,
    string? GameRoot = null,
    string? CetModsRoot = null,
    SourceKind SourceKind = SourceKind.None,
    int CetModCount = 0)
{
    public bool HasCetSource => !string.IsNullOrWhiteSpace(CetModsRoot) && Directory.Exists(CetModsRoot);
    public bool HasVerifiedPhysicalSource => IsValidGame && SourceKind != SourceKind.ExternalCetMods && HasCetSource;
    public string Summary => SourceKind switch
    {
        SourceKind.GameRoot => $"Game found. {CetModCount} CET mod(s) with init.lua; source compatibility not yet verified.",
        SourceKind.LiveCetMods => $"Game and live CET mods folder found. {CetModCount} mod(s) with init.lua.",
        SourceKind.ExternalCetMods => $"External CET source detected: {CetModCount} mod(s). Game root and effective MO2/loader deployment are NOT verified.",
        _ => "Select the Cyberpunk 2077 game root or a CET mods directory containing mod folders with init.lua."
    };
}

public static class GameInspector
{
    public static GameInspection Inspect(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
            return Empty("No folder selected.");

        string selected;
        try { selected = Path.GetFullPath(selection.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Empty("Invalid folder path.");
        }

        if (!Directory.Exists(selected))
            return Empty("Selected folder does not exist.");

        var gameExe = Path.Combine(selected, "bin", "x64", "Cyberpunk2077.exe");
        if (File.Exists(gameExe))
        {
            var mods = Path.Combine(selected, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
            return Build(selected, Directory.Exists(mods) ? mods : null, SourceKind.GameRoot);
        }

        // An installed CET root can be selected directly, without guessing from a capture.
        // Only the exact known game-relative path establishes the game root.
        if (Path.GetFileName(selected).Equals("mods", StringComparison.OrdinalIgnoreCase))
        {
            var candidate = GetGameRootForDirectMods(selected);
            if (candidate is not null && File.Exists(Path.Combine(candidate, "bin", "x64", "Cyberpunk2077.exe")))
                return Build(candidate, selected, SourceKind.LiveCetMods);
        }

        if (CountCetMods(selected) > 0)
            return Build(null, selected, SourceKind.ExternalCetMods);

        return Empty("No Cyberpunk2077.exe or immediate CET mod folders with init.lua found.");
    }

    public static int CountCetMods(string? modsRoot)
    {
        if (string.IsNullOrWhiteSpace(modsRoot) || !Directory.Exists(modsRoot))
            return 0;
        try
        {
            return Directory.EnumerateDirectories(modsRoot)
                .Count(mod => File.Exists(Path.Combine(mod, "init.lua")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static string? GetGameRootForDirectMods(string mods)
    {
        var dir = new DirectoryInfo(mods);
        foreach (var expected in new[] { "mods", "cyber_engine_tweaks", "plugins", "x64", "bin" })
        {
            if (!dir.Name.Equals(expected, StringComparison.OrdinalIgnoreCase))
                return null;
            if (dir.Parent is null)
                return null;
            dir = dir.Parent;
        }
        return dir.FullName;
    }

    private static GameInspection Build(string? gameRoot, string? cetMods, SourceKind kind)
    {
        var count = CountCetMods(cetMods);
        var checks = new List<ComponentCheck>();
        if (gameRoot is null)
        {
            checks.Add(new("Game executable", CheckState.Missing,
                "External sources are not evidence of the active game root; profiler install and game launch are blocked."));
            checks.Add(new("Mod loader / MO2", CheckState.Unverified,
                "Source can be inspected; MO2 VFS precedence and effective deployment are not yet verified."));
        }
        else
        {
            checks.Add(new("Game executable", CheckState.Present, "Cyberpunk2077.exe found."));
            checks.Add(CheckDirectory("RED4ext plugin folder", Path.Combine(gameRoot, "red4ext", "plugins")));
            checks.Add(CheckDirectory("REDscript source folder", Path.Combine(gameRoot, "r6", "scripts")));
            checks.Add(CheckFile("G-REDruntime source",
                Path.Combine(gameRoot, "r6", "scripts", "G-RedRuntime", "Core", "Runtime.reds")));
        }

        checks.Add(cetMods is null
            ? new("CET source folder", CheckState.Missing, "No physical CET mods folder detected.")
            : new("CET source folder", CheckState.Unverified, $"Found {count} immediate mod folders with init.lua."));
        checks.Add(cetMods is not null
            ? CheckFile("0-Engine source", Path.Combine(cetMods, "0-Engine", "init.lua"))
            : new("0-Engine source", CheckState.Missing, "Cannot inspect without a CET mods folder."));

        return new(gameRoot is not null, checks, gameRoot, cetMods, kind, count);
    }

    private static GameInspection Empty(string reason) =>
        new(false, [new("Game / CET source", CheckState.Missing, reason)]);

    private static ComponentCheck CheckDirectory(string name, string path) =>
        Directory.Exists(path)
            ? new(name, CheckState.Unverified, "Folder exists; runtime compatibility is not verified.")
            : new(name, CheckState.Missing, "Not detected.");

    private static ComponentCheck CheckFile(string name, string path) =>
        File.Exists(path)
            ? new(name, CheckState.Unverified, "Source found; compatibility is not verified.")
            : new(name, CheckState.Missing, "Not detected.");
}
