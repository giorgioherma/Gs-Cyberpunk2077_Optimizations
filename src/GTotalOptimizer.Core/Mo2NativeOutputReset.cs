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

    private static void RequireClosed()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077");
        try
        {
            if (processes.Length != 0)
                throw new InvalidOperationException("Close Cyberpunk before discarding uncollected results.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private static (string[] Roots, string[] Patterns) ReadScope(JsonObject journal)
    {
        var game = Path.GetFullPath(journal["gameRoot"]?.GetValue<string>() ??
            throw new InvalidDataException("Missing session game root."));
        if (!File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")))
            throw new InvalidDataException("Session game root is no longer available.");
        var overwrite = Path.GetFullPath(journal["overwriteRoot"]?.GetValue<string>() ??
            throw new InvalidDataException("Missing session Overwrite root."));
        var patterns = journal["cetResultPatterns"]?.Deserialize<string[]>() ??
            throw new InvalidDataException("Missing CET owner result inventory.");
        ValidatePatterns(patterns);

        return (new[] { game, overwrite, stage }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), patterns);
    }

    private static (HashSet<string> Files, HashSet<string> Directories) InspectScope(
        IEnumerable<string> roots, string[] patterns)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
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

        return (files, directories);

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

    internal static void Discard(JsonObject journal)
    {
        RequireClosed();
        var (roots, patterns) = ReadScope(journal);
        var (files, directories) = InspectScope(roots, patterns);
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

    }

    // The existing owner-installed fixture is a projection, not an archive.
    // Its managers retain all validation and collection decisions. Never
    // write live game/Overwrite/stage files while inspecting readiness.
    internal static void ProjectForCollection(JsonObject journal, string fixture)
    {
        RequireClosed();
        var (roots, patterns) = ReadScope(journal);
        CheckParents(fixture);
        if (!Directory.Exists(fixture) || roots.Any(root =>
                Overlaps(root, fixture)))
            throw new InvalidDataException("MO2 capture fixture is unavailable or overlaps live output.");
        var winners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in roots)
        {
            var (files, _) = InspectScope(new[] { root }, patterns);
            foreach (var file in files)
            {
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!IsNativeResult(relative, patterns) ||
                    (winners.TryGetValue(relative, out var other) &&
                     !string.Equals(other, file, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Ambiguous MO2 native result: " + relative);
                winners[relative] = file;
            }
        }

        // All sources have been inventoried before writing to the fixture.
        // Remove only previously projected owner-result paths absent now;
        // this prevents stale data qualifying a later readiness check.
        var (prior, _) = InspectScope(new[] { fixture }, patterns);
        foreach (var old in prior)
            if (!winners.ContainsKey(Path.GetRelativePath(fixture, old).Replace('\\', '/')))
                File.Delete(old);
        foreach (var (relative, source) in winners)
        {
            RequireClosed();
            CheckParents(Path.GetDirectoryName(source)!);
            CheckRegular(source);
            var target = Path.Combine(fixture, relative.Replace('/', Path.DirectorySeparatorChar));
            CheckParents(Path.GetDirectoryName(target)!);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var original = Hash(source);
            if (File.Exists(target))
            {
                CheckRegular(target);
                if (Hash(target) == original) continue;
            }
            File.Copy(source, target, overwrite: true);
            if (Hash(target) != original || Hash(source) != original)
                throw new InvalidDataException("MO2 native output changed during projection: " + relative);
        }
        RequireClosed();

        static bool Overlaps(string first, string second)
        {
            string Prefix(string path) => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            var a = Prefix(first);
            var b = Prefix(second);
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase) ||
                   b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
        }
        static string Hash(string path)
        {
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
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
