namespace GTotalOptimizer.Core;

// Resolve the package from the actual managed host, never the game/MO2 path
// or the process working directory. The public launcher is one folder above app/.
public static class PortablePackagePaths
{
    public const string RootLauncherName = "Gs-Cyberpunk2077_Optimizer.exe";
    public const string ManagedExecutableName = "G-Total-Optimizer.App.exe";

    public static string RootFromAppDirectory(string appDirectory)
    {
        var directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(appDirectory));
        if (Path.GetFileName(directory).Equals("app", StringComparison.OrdinalIgnoreCase))
            return Directory.GetParent(directory)?.FullName ?? directory;

        // IDE/source-only debug runs do not have the packaged app/ layout.
        return directory;
    }

    public static string RootFromExecutablePath(string? executablePath, string appBaseDirectory)
    {
        if (!string.IsNullOrWhiteSpace(executablePath) &&
            Path.GetFileName(executablePath).Equals(ManagedExecutableName, StringComparison.OrdinalIgnoreCase))
        {
            var physicalDir = Path.GetDirectoryName(Path.GetFullPath(executablePath))!;
            return RootFromAppDirectory(physicalDir);
        }
        return RootFromAppDirectory(appBaseDirectory);
    }

    public static string CurrentPackageRoot() =>
        RootFromExecutablePath(Environment.ProcessPath, AppContext.BaseDirectory);

    public static string RuntimePathDiagnostic(string packageRoot) =>
        "Running executable: " + (Environment.ProcessPath ?? "(unavailable)") +
        Environment.NewLine + "App base: " + AppContext.BaseDirectory +
        Environment.NewLine + "Resolved GCO root: " + packageRoot +
        Environment.NewLine + "Expected manifest: " +
        Path.Combine(packageRoot, "components", "manifest.json");
}
