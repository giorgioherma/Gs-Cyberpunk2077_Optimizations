namespace GTotalOptimizer.Core;

// The public package mirrors CET: a small root launcher and a contained
// managed app/runtime in app/. All component paths resolve from package root.
public static class PortablePackagePaths
{
    public const string RootLauncherName = "G-Total-Optimizer.exe";
    public const string ManagedExecutableName = "G-Total-Optimizer.App.exe";

    public static string RootFromAppDirectory(string appDirectory)
    {
        var directory = Path.GetFullPath(appDirectory);
        if (Path.GetFileName(Path.TrimEndingDirectorySeparator(directory))
            .Equals("app", StringComparison.OrdinalIgnoreCase) &&
            File.Exists(Path.Combine(directory, ManagedExecutableName)))
        {
            var parent = Directory.GetParent(directory);
            if (parent is not null)
                return parent.FullName;
        }
        // IDE and source-only debug builds retain their existing layout.
        return directory;
    }
}
