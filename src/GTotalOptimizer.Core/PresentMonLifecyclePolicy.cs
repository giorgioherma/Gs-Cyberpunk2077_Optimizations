namespace GTotalOptimizer.Core;

// One authoritative GCO-owned PresentMon instance. A second launch must
// terminate the previous packaged executable, never an unrelated user tool.
public static class PresentMonLifecyclePolicy
{
    public static string Executable(string packageRoot) =>
        Path.GetFullPath(Path.Combine(packageRoot, "components", "presentmon", "PresentMon.exe"));

    public static bool IsOwnedProcess(string packageRoot, string executablePath) =>
        Path.GetFullPath(executablePath).Equals(Executable(packageRoot),
            StringComparison.OrdinalIgnoreCase);

    public static string ValidateOutputCsv(string packageRoot, string outputCsv)
    {
        var expected = Path.GetFullPath(Path.Combine(packageRoot, "RESULTS"));
        var actual = Path.GetFullPath(outputCsv);
        var relative = Path.GetRelativePath(expected, actual);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            !Path.GetFileName(actual).Equals("PresentMon.csv", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PresentMon destination must be a GCO session CSV inside RESULTS.");
        return actual;
    }
}
