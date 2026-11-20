using System.IO.Compression;

namespace GTotalOptimizer.Core;

/// <summary>Final independently enforced ZIP contract for Vortex overlay use.</summary>
public static class CombinedPassOutputInspector
{
    public static int Validate(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (var item in zip.Entries)
        {
            string path = item.FullName;
            if (item.Length < 1 || path.StartsWith('/') || path.Contains('\\') ||
                path.Contains(':') ||
                path.Split('/').Any(part => part is "" or "." or "..") ||
                !seen.Add(path))
                throw new InvalidDataException("GCO_PASS_ARCHIVE_UNSAFE_OR_DUPLICATE_PATH");
            if (!(path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",
                    StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase) ||
                  path.StartsWith("red4ext/plugins/G-CET-StaticLocations/",
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("GCO_PASS_ARCHIVE_NOT_GAME_RUNTIME");
            if ((path.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase) &&
                 !path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase)) ||
                path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".html", StringComparison.OrdinalIgnoreCase) ||
                path.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/CETProfiler", StringComparison.OrdinalIgnoreCase) ||
                path.Contains("/G-REDscript-Profiler", StringComparison.OrdinalIgnoreCase) ||
                (path.StartsWith("red4ext/", StringComparison.OrdinalIgnoreCase) &&
                 !(path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                   path.EndsWith(".locations", StringComparison.OrdinalIgnoreCase))))
                throw new InvalidDataException("GCO_PASS_ARCHIVE_NONRUNTIME_FILE");
            files++;
        }
        if (files == 0)
            throw new InvalidDataException("GCO_PASS_ARCHIVE_EMPTY");
        return files;
    }
}
