using System.IO.Compression;

namespace GTotalOptimizer.Core;

/// <summary>Final independently enforced ZIP contract for Vortex overlay use.</summary>
public static class CombinedPassOutputInspector
{
    // The same game-only allowlist is used while staging source-owner output
    // and after writing the ZIP. No separate corrective packaging pass.
    public static bool AllowedRuntimePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(x => x is "" or "." or ".."))
            return false;
        string file = Path.GetFileName(path);
        if (new[] { "README", "LICENSE", "LICENCE", "CHANGELOG",
                    "HANDOFF", "CONTRIBUTORS", "NOTICE" }
            .Any(prefix => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            new[] { ".exe", ".pdb", ".dll", ".md", ".csv", ".html",
                    ".log", ".ps1", ".zip", ".bak", ".vortex_backup" }
            .Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase) &&
                !path.StartsWith("red4ext/plugins/G-CET-StaticLocations/",
                    StringComparison.OrdinalIgnoreCase)))
            return false;

        if (path.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase))
            return path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase);
        if (path.StartsWith("red4ext/plugins/G-CET-StaticLocations/",
                StringComparison.OrdinalIgnoreCase))
            return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".locations", StringComparison.OrdinalIgnoreCase);
        return path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",
                StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/CETProfiler", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/G-REDscript-Profiler", StringComparison.OrdinalIgnoreCase);
    }

    public static int Validate(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (var item in zip.Entries)
        {
            string path = item.FullName;
            if (item.Length < 1 || !AllowedRuntimePath(path) || !seen.Add(path))
                throw new InvalidDataException("GCO_PASS_ARCHIVE_UNSAFE_OR_NONRUNTIME_ENTRY");
            files++;
        }
        if (files == 0)
            throw new InvalidDataException("GCO_PASS_ARCHIVE_EMPTY");
        return files;
    }
}
