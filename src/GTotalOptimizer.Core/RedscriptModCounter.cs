namespace GTotalOptimizer.Core;

/// <summary>
/// Counts visible REDscript source owner groups, not .reds files. In a plain game
/// installation, top-level r6/scripts folders (and standalone root .reds files)
/// are the only structural owner evidence. MO2 uses package ownership for
/// active virtual files. This is an estimate, not a validated author/mod manifest.
/// </summary>
public static class RedscriptModCounter
{
    private const string RootPrefix = "r6/scripts/";

    public static int CountPhysical(string? gameRoot)
    {
        if (string.IsNullOrWhiteSpace(gameRoot)) return 0;
        var scripts = Path.Combine(gameRoot, "r6", "scripts");
        if (!Directory.Exists(scripts)) return 0;
        try
        {
            var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in Directory.EnumerateFiles(scripts, "*.reds", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(scripts, file).Replace('\\', '/');
                var top = FirstSegment(relative);
                if (!string.IsNullOrEmpty(top)) owners.Add(top);
            }
            return owners.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return 0;
        }
    }

    public static int CountEffective(Mo2Stack? stack)
    {
        if (stack?.Ready != true) return 0;
        var owners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in stack.EffectiveFiles.Values)
        {
            var relative = file.GameRelativePath.Replace('\\', '/');
            if (!relative.StartsWith(RootPrefix, StringComparison.OrdinalIgnoreCase) ||
                !relative.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                continue;

            if (file.FromMo2 && !file.Owner.Equals("(MO2 Overwrite)", StringComparison.OrdinalIgnoreCase))
                owners.Add("mo2:" + file.Owner);
            else
                owners.Add("physical:" + FirstSegment(relative[RootPrefix.Length..]));
        }
        return owners.Count;
    }

    private static string FirstSegment(string path)
    {
        var slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
    }
}
