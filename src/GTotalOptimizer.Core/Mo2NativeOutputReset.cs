using System.IO.Enumeration;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GTotalOptimizer.Core;

// MO2 changes only where the existing owner-defined live output lives.
// CET's manifest is the same authority used by Direct discard-live; GRSP's
// manager owns only the RESULTS subtree. Never sweep Overwrite or mod packages.
internal static class Mo2NativeOutputReset
{
    private const string CetRoot = "bin/x64/plugins/cyber_engine_tweaks";
    private const string GrspResults = "red4ext/plugins/G-REDscript-Profiler/RESULTS";

    internal static string[] ReadCetPatterns(string cetExecutable)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Path.GetDirectoryName(cetExecutable)!, "MANIFEST.json")));
        var root = doc.RootElement;
        var patterns = root.GetProperty("liveResultFiles").EnumerateArray()
            .Concat(root.GetProperty("liveResultPatterns").EnumerateArray())
            .Select(x => x.GetString() ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        ValidatePatterns(patterns);
        if (patterns.Length == 0)
            throw new InvalidDataException("CET owner result inventory is empty.");
        return patterns;
    }

    private static void ValidatePatterns(string[] patterns)
    {
        if (patterns.Length > 1024 || patterns.Any(p =>
            !p.StartsWith("CET_Runtime_Profile_", StringComparison.OrdinalIgnoreCase) ||
            p.IndexOfAny(new[] { '/', '\\', ':', '\0' }) >= 0 ||
            p.Contains("..", StringComparison.Ordinal)))
            throw new InvalidDataException("CET owner result inventory is invalid.");
    }

    internal static bool IsNativeResult(string relative, string[] cetPatterns)
    {
        if (relative.StartsWith(GrspResults + "/", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!relative.StartsWith(CetRoot + "/", StringComparison.OrdinalIgnoreCase))
            return false;
        var leaf = relative[(CetRoot.Length + 1)..];
        return !leaf.Contains('/') && cetPatterns.Any(p =>
            FileSystemName.MatchesSimpleExpression(p, leaf, ignoreCase: true));
    }

    internal static void Discard(JsonObject journal)
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077");
        try
        {
            if (processes.Length != 0)
                throw new InvalidOperationException("Close Cyberpunk before discarding uncollected results.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
        var game = Path.GetFullPath(journal["gameRoot"]?.GetValue<string>() ??
            throw new InvalidDataException("Missing session game root."));
        if (!File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")))
            throw new InvalidDataException("Session game root is no longer available.");
        var overwrite = Path.GetFullPath(journal["overwriteRoot"]?.GetValue<string>() ??
            throw new InvalidDataException("Missing session Overwrite root."));
        var patterns = journal["cetResultPatterns"]?.Deserialize<string[]>() ??
            throw new InvalidDataException("Missing CET owner result inventory.");
        ValidatePatterns(patterns);

        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The session-owned stage is also a possible output destination.
        var stage = Path.GetFullPath(journal["stageRoot"]?.GetValue<string>() ??
            throw new InvalidDataException("Missing session stage root."));
        foreach (var root in new[] { game, overwrite, stage }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var cet = Path.Combine(root, CetRoot);
            CheckParents(cet);
            if (Directory.Exists(cet))
                foreach (var file in Directory.EnumerateFiles(cet, "*", SearchOption.TopDirectoryOnly))
                    if (IsNativeResult(CetRoot + "/" + Path.GetFileName(file), patterns))
                    {
                        CheckRegular(file);
                        files.Add(file);
                    }
            var red = Path.Combine(root, GrspResults);
            CheckParents(red);
            if (Directory.Exists(red)) InspectTree(red);
        }

        // Validate the entire deletion scope before touching any file. A
        // locked or linked result keeps recovery actionable; no archive is made.
        foreach (var file in files)
        {
            CheckParents(Path.GetDirectoryName(file)!);
            CheckRegular(file);
            File.Delete(file);
            if (File.Exists(file)) throw new IOException("Native result could not be discarded: " + file);
        }
        foreach (var directory in directories.OrderByDescending(p => p.Length))
        {
            CheckParents(directory);
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: false);
        }

        void InspectTree(string folder)
        {
            CheckParents(folder);
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked profiler result refused: " + entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    InspectTree(entry);
                    directories.Add(entry);
                }
                else
                {
                    CheckRegular(entry);
                    files.Add(entry);
                }
            }
        }
    }

    private static void CheckRegular(string path)
    {
        if (!File.Exists(path) || (File.GetAttributes(path) &
                (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0)
            throw new InvalidDataException("Unsafe profiler result: " + path);
    }

    private static void CheckParents(string path)
    {
        for (string? parent = Path.GetFullPath(path); parent is not null;
            parent = Path.GetDirectoryName(parent))
            if (Directory.Exists(parent) &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked profiler output directory refused: " + parent);
    }
}
