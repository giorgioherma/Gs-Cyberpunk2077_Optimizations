namespace GTotalOptimizer.Core;

// Content-based discovery for MO2 users who keep CET ASI, Lua mods, REDscript,
// and RED4ext in one to four unrelated physical folders. Read-only: these
// findings are NOT an MO2 enabled-profile inventory or runtime VFS proof.
[Flags]
public enum ModSourceParts
{
    None = 0,
    CetAsi = 1,
    CetMods = 2,
    Redscript = 4,
    Red4ext = 8,
    All = CetAsi | CetMods | Redscript | Red4ext
}

public sealed record ModFolderProbe(string Folder, ModSourceParts Parts,
    int CetOwners, int RedscriptOwners, bool HasZeroEngine, string Detail);

public sealed record ModSourceCoverage(ModSourceParts Parts, int CetOwners,
    int RedscriptOwners, bool HasZeroEngine, int UsedFolders,
    int VisibleFolders, IReadOnlyList<ModFolderProbe> Folders)
{
    public bool Complete => Parts == ModSourceParts.All;

    public bool Has(ModSourceParts part) => (Parts & part) == part;
}

public static class Mo2FolderSources
{
    public const int MaximumFolders = 4;
    private const int MaximumDepth = 12;
    private const int MaximumEntries = 250_000;

    public static ModSourceCoverage Inspect(IReadOnlyList<string?> selected, CancellationToken cancellation = default)
    {
        var all = new List<ModFolderProbe>();
        var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var cet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var red = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = ModSourceParts.None;
        var zeroEngine = false;
        var used = 0;

        for (var i = 0; i < Math.Min(MaximumFolders, selected.Count); i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var input = selected[i];
            if (string.IsNullOrWhiteSpace(input))
            {
                all.Add(new("", ModSourceParts.None, 0, 0, false, "Select a source folder."));
                if (parts != ModSourceParts.All) break;
                continue;
            }

            string full;
            try { full = Path.GetFullPath(input.Trim().Trim('"')); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                all.Add(new(input, ModSourceParts.None, 0, 0, false, "Invalid folder path."));
                break;
            }

            if (!unique.Add(full))
            {
                all.Add(new(full, ModSourceParts.None, 0, 0, false, "Duplicate source folder; already inspected."));
                break;
            }

            if (!Directory.Exists(full))
            {
                all.Add(new(full, ModSourceParts.None, 0, 0, false, "Folder not found."));
                break;
            }

            // Stage each folder's inventory until the full walk succeeds. No partial
            // recognition may escape if a directory becomes unreadable or too large.
            var stagedCet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var stagedRed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var one = Scan(full, stagedCet, stagedRed, cancellation);
            all.Add(one);
            used++;
            parts |= one.Parts;
            zeroEngine |= one.HasZeroEngine;
            if (one.Parts != ModSourceParts.None)
            {
                cet.UnionWith(stagedCet);
                red.UnionWith(stagedRed);
            }
            if (parts == ModSourceParts.All) break;
        }

        var visible = parts == ModSourceParts.All ? Math.Max(1, used) :
            Math.Min(MaximumFolders, Math.Max(1, used + 1));
        // An incomplete or invalid selected row should stay visible for correction.
        visible = Math.Min(MaximumFolders, Math.Max(visible, all.Count));
        if (parts == ModSourceParts.All) visible = Math.Min(MaximumFolders, Math.Max(1, used));

        return new(parts, cet.Count, red.Count, zeroEngine, used, visible, all);
    }

    private static ModFolderProbe Scan(string root, HashSet<string> cet, HashSet<string> red,
        CancellationToken token)
    {
        var parts = ModSourceParts.None;
        var zeroEngine = false;
        var cetBefore = cet.Count;
        var redBefore = red.Count;
        var stack = new Stack<(string Path, int Depth)>();
        stack.Push((root, 0));
        var entries = 0;
        try
        {
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested();
                var (dir, depth) = stack.Pop();
                if (++entries > MaximumEntries)
                    return new(root, ModSourceParts.None, 0, 0, false, "Inventory limit exceeded; source not trusted.");

                // Prevent junction/symlink escape and accidental recursion loops.
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) continue;
                foreach (var path in Directory.EnumerateFileSystemEntries(dir))
                {
                    token.ThrowIfCancellationRequested();
                    if (++entries > MaximumEntries)
                        return new(root, ModSourceParts.None, 0, 0, false, "Inventory limit exceeded; source not trusted.");

                    var name = Path.GetFileName(path);
                    if (name.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
                    var attributes = File.GetAttributes(path);
                    if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        if (depth < MaximumDepth && name is not (".git" or ".vs" or "node_modules"))
                            stack.Push((path, depth + 1));
                        continue;
                    }

                    if (name.Equals("cyber_engine_tweaks.asi", StringComparison.OrdinalIgnoreCase) ||
                        name.Equals("cet.asi", StringComparison.OrdinalIgnoreCase))
                        parts |= ModSourceParts.CetAsi;

                    if (name.Equals("init.lua", StringComparison.OrdinalIgnoreCase) &&
                        IsCetModInit(root, path))
                    {
                        parts |= ModSourceParts.CetMods;
                        cet.Add(Path.GetFullPath(path));
                        if (Path.GetFileName(Path.GetDirectoryName(path)!)
                            .Equals("0-Engine", StringComparison.OrdinalIgnoreCase))
                            zeroEngine = true;
                    }
                    if (name.EndsWith(".reds", StringComparison.OrdinalIgnoreCase) &&
                        IsRedscriptSource(root, path, out var owner))
                    {
                        parts |= ModSourceParts.Redscript;
                        red.Add(Path.GetFullPath(owner));
                    }
                    if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                        IsRed4extPlugin(root, path))
                        parts |= ModSourceParts.Red4ext;
                }
            }
            return new(root, parts, cet.Count - cetBefore, red.Count - redBefore, zeroEngine,
                parts == ModSourceParts.None ? "No recognized game component files." : "Recognized by file contents.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // An unreadable/partially scanned directory cannot claim a complete source.
            return new(root, ModSourceParts.None, 0, 0, false, "Cannot fully inspect: " + ex.Message);
        }
    }

    private static bool IsCetModInit(string root, string file)
    {
        var owner = Path.GetDirectoryName(file)!;
        var mods = Directory.GetParent(owner)?.FullName;
        if (mods is null) return false;
        // Canonical CET mods/<owner>/init.lua; an explicitly selected owner
        // directory with init.lua is also valid as a source of that one mod.
        return Path.GetFileName(mods).Equals("mods", StringComparison.OrdinalIgnoreCase)
            || owner.Equals(root, StringComparison.OrdinalIgnoreCase) &&
               Path.GetFileName(file).Equals("init.lua", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRedscriptSource(string root, string file, out string owner)
    {
        owner = "";
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var segments = relative.Split('/');
        var index = Array.FindIndex(segments, x => x.Equals("scripts", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= segments.Length)
        {
            // Allow a directly selected r6/scripts owner folder with loose .reds.
            if (!Path.GetFileName(root).Equals("scripts", StringComparison.OrdinalIgnoreCase) &&
                !Path.GetFileName(Directory.GetParent(root)?.FullName ?? "")
                    .Equals("scripts", StringComparison.OrdinalIgnoreCase)) return false;
            owner = segments.Length >= 2 ? Path.Combine(root, segments[0])
                : file;
            return true;
        }
        if (index > 0 && !segments[index - 1].Equals("r6", StringComparison.OrdinalIgnoreCase) &&
            !Path.GetFileName(root).Equals("r6", StringComparison.OrdinalIgnoreCase))
            return false;
        owner = index + 2 < segments.Length
            ? Path.Combine(root, Path.Combine(segments.Take(index + 2).ToArray()))
            : file;
        return true;
    }

    private static bool IsRed4extPlugin(string root, string file)
    {
        var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
        var parts = relative.Split('/');
        for (var i = 0; i < parts.Length - 2; i++)
            if (parts[i].Equals("red4ext", StringComparison.OrdinalIgnoreCase) &&
                parts[i + 1].Equals("plugins", StringComparison.OrdinalIgnoreCase))
                return true;
        return Path.GetFileName(root).Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            Path.GetFileName(Directory.GetParent(root)?.FullName ?? "")
                .Equals("red4ext", StringComparison.OrdinalIgnoreCase);
    }
}
