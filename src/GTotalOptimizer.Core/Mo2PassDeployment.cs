namespace GTotalOptimizer.Core;

// Only actual output paths (after unchanged framework omission) participate.
// The verified effective-source snapshot, not a captured mod inventory,
// determines the destination of an installed owner.
internal static class Mo2PassDeployment
{
    private const string CetPrefix = "bin/x64/plugins/cyber_engine_tweaks/mods/";
    private const string RedPrefix = "r6/scripts/";

    private static string OwnerGroup(string path)
    {
        if (CombinedPassOutputInspector.IsNativeRuntimePath(path))
            return "native:G-NativeRuntime";
        string prefix = path.StartsWith(CetPrefix, StringComparison.OrdinalIgnoreCase)
            ? CetPrefix : RedPrefix;
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unknown optimization deployment owner: " + path);
        string rest = path[prefix.Length..];
        int slash = rest.IndexOf('/');
        // REDscript loose files are their own source units.
        // New helpers under an owner inherit that owner's origin.
        return prefix + (slash < 0 ? rest : rest[..slash]);
    }

    private static bool IsOwnerSource(string path) =>
        CombinedPassOutputInspector.IsNativeRuntimePath(path) ||
        (path.StartsWith(CetPrefix, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".lua", StringComparison.OrdinalIgnoreCase)) ||
        (path.StartsWith(RedPrefix, StringComparison.OrdinalIgnoreCase) &&
            path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));

    // True in origins means virtual; returned paths are physical. Files in
    // a genuinely new owner have no installed origin and default to MO2.
    // Do not silently split a single already-mixed runtime source owner.
    internal static HashSet<string> DirectPaths(IEnumerable<string> output,
        IReadOnlyDictionary<string, bool> origins)
    {
        var groups = output.GroupBy(OwnerGroup, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var ownerOrigins = new Dictionary<string, HashSet<bool>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, fromMo2) in origins)
        {
            if (!IsOwnerSource(path)) continue;
            string group = OwnerGroup(path);
            if (!groups.ContainsKey(group)) continue;
            if (!ownerOrigins.TryGetValue(group, out var set))
                ownerOrigins[group] = set = [];
            set.Add(fromMo2);
        }
        var direct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (group, paths) in groups)
        {
            if (!ownerOrigins.TryGetValue(group, out var set)) set = [];
            // Existing emitted data/config files must agree with their
            // source owner; unrelated mutable data does not classify it.
            foreach (string path in paths)
                if (origins.TryGetValue(path, out bool fromMo2)) set.Add(fromMo2);
            if (set.Count > 1)
                throw new InvalidDataException(
                    "Optimization deployment is ambiguous for " + group +
                    ": effective source is already split between the game and MO2. " +
                    "No ZIP published; keep this owner in one installation location before generating.");
            if (set.Contains(false)) direct.UnionWith(paths);
        }
        return direct;
    }
}
