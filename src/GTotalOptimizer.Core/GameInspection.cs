namespace GTotalOptimizer.Core;

public enum CheckState
{
    Present,
    Missing,
    Unverified
}

public sealed record ComponentCheck(string Name, CheckState State, string Detail);

public sealed record GameInspection(bool IsValidGame, IReadOnlyList<ComponentCheck> Checks)
{
    public string Summary => IsValidGame
        ? "Cyberpunk 2077 installation found. Component presence does not prove runtime compatibility."
        : "Select a Cyberpunk 2077 installation containing bin\\x64\\Cyberpunk2077.exe.";
}

public static class GameInspector
{
    public static GameInspection Inspect(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot))
            return new(false, [new("Game installation", CheckState.Missing, "No game folder selected.")]);

        string root;
        try { root = Path.GetFullPath(gameRoot.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new(false, [new("Game installation", CheckState.Missing, "Invalid folder path.")]);
        }

        var exe = Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe");
        var valid = File.Exists(exe);
        if (!valid)
            return new(false, [new("Game installation", CheckState.Missing, "Cyberpunk2077.exe not found under bin\\x64.")]);

        var modsRoot = Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
        var redscriptRoot = Path.Combine(root, "r6", "scripts");
        var red4extRoot = Path.Combine(root, "red4ext", "plugins");
        var checks = new List<ComponentCheck>
        {
            new("Game executable", CheckState.Present, "Cyberpunk2077.exe found."),
            CheckDirectory("CET source folder", modsRoot),
            CheckFile("0-Engine source", Path.Combine(modsRoot, "0-Engine", "init.lua")),
            CheckDirectory("RED4ext plugin folder", red4extRoot),
            CheckDirectory("REDscript source folder", redscriptRoot),
            CheckFile("G-REDruntime source", Path.Combine(redscriptRoot, "G-RedRuntime", "Core", "Runtime.reds"))
        };

        return new(true, checks);
    }

    private static ComponentCheck CheckDirectory(string name, string path) =>
        Directory.Exists(path)
            ? new(name, CheckState.Unverified, "Folder detected; installation and API compatibility have not been verified.")
            : new(name, CheckState.Missing, "Not detected.");

    private static ComponentCheck CheckFile(string name, string path) =>
        File.Exists(path)
            ? new(name, CheckState.Unverified, "Source file detected; integration compatibility has not been verified.")
            : new(name, CheckState.Missing, "Not detected.");
}
