using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Source-driven immutable spatial-site inventory for the existing GCAS
/// binary format. No new native engine; no mod-name catalog. The rows are
/// INERT unless a future generated consumer attaches a matching owner.
/// </summary>
internal static class RedscriptNativeLocations
{
    private const string Number =
        @"[-+]?(?:\d+(?:\.\d*)?|\.\d+)(?:[eE][-+]?\d+)?";
    private static readonly Regex Quad = new(
        @"\bSquareArea\s*\(\s*" +
        @"new\s+Vector2\s*\(\s*(?<x>" + Number + @")\s*,\s*(?<y>" + Number + @")\s*\)\s*,\s*" +
        @"new\s+Vector2\s*\(\s*(?<x>" + Number + @")\s*,\s*(?<y>" + Number + @")\s*\)\s*,\s*" +
        @"new\s+Vector2\s*\(\s*(?<x>" + Number + @")\s*,\s*(?<y>" + Number + @")\s*\)\s*,\s*" +
        @"new\s+Vector2\s*\(\s*(?<x>" + Number + @")\s*,\s*(?<y>" + Number + @")\s*\)\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private sealed record Site(int Owner, float X, float Y, float Z,
        float Radius, bool XY);

    // GCAS native indexes must follow the static prefix returned by an
    // author's actual source array, not every sphere-like constructor
    // elsewhere in the file (e.g. conditional helper factory fallbacks).
    // This is shared by inventory and compilation, never a corrective pass.
    private static readonly Regex LiteralArray = new(
        @"\blet\s+(?<array>[A-Za-z_]\w*)\s*=\s*\[",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static IReadOnlyList<Match> StaticSphereMatches(string masked)
    {
        var spans = new List<(int Start, int End)>();
        foreach (Match init in LiteralArray.Matches(masked))
        {
            string name = init.Groups["array"].Value;
            int close = masked.IndexOf("];", init.Index + init.Length,
                StringComparison.Ordinal);
            if (close < 0) continue;
            // Static records must be returned as the same initial array;
            // other literal helpers and conditionally appended results are
            // not eligible for positional native substitution.
            if (!Regex.IsMatch(masked[(close + 2)..],
                @"\breturn\s+" + Regex.Escape(name) + @"\s*;",
                RegexOptions.CultureInvariant))
                continue;
            spans.Add((init.Index, close));
        }
        return Sphere.Matches(masked).Cast<Match>()
            .Where(m => ValidSphere(m) &&
                spans.Any(s => m.Index >= s.Start &&
                    m.Index + m.Length <= s.End))
            .ToArray();
    }

    // Source-literal player proximity zones. Site admission is structural:
    // constructor(Enum.Value, Vector4(x,y,z,w), radius, Enum.Value).
    // It is NOT inferred from a generic NPC/world-object GetWorldPosition().
    private static readonly Regex Sphere = new(
        @"\b[A-Za-z_][A-Za-z0-9_]*\s*\(\s*" +
        @"[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*\s*,\s*" +
        @"(?:new\s+)?Vector4\s*\(\s*" +
        @"(?<x>" + Number + @")\s*,\s*" +
        @"(?<y>" + Number + @")\s*,\s*" +
        @"(?<z>" + Number + @")\s*,\s*" +
        @"(?<w>" + Number + @")\s*\)\s*,\s*" +
        @"(?<radius>" + Number + @")\s*,\s*" +
        @"[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*\s*\)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    internal sealed record SourceSites(string RelativePath, int Owner,
        int SphereCount, int QuadCount);

    internal static IReadOnlyList<SourceSites> Inventory(string gameRoot)
    {
        var scripts = Path.Combine(Path.GetFullPath(gameRoot), "r6", "scripts");
        var result = new List<SourceSites>();
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(scripts, file).Replace('\\', '/');
            if (relative.StartsWith("G-RedRuntime/", StringComparison.OrdinalIgnoreCase))
                continue;
            string masked = CodeOnly(File.ReadAllText(file, Encoding.UTF8));
            int sphereCount = StaticSphereMatches(masked).Count;
            int quadCount = Quad.Matches(masked).Count;
            if (sphereCount > 0 || quadCount > 0)
                result.Add(new(relative, Owner(relative), sphereCount, quadCount));
        }
        return result;
    }

    private static bool ValidSphere(Match match)
    {
        float[] vals = new float[5];
        string[] keys = ["x", "y", "z", "w", "radius"];
        for (int i = 0; i < keys.Length; i++)
            if (!float.TryParse(match.Groups[keys[i]].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture, out vals[i]) ||
                !float.IsFinite(vals[i]))
                return false;
        return vals[4] > 0f && vals[4] <= 1024f &&
            Math.Abs(vals[0]) <= 100000f && Math.Abs(vals[1]) <= 100000f &&
            Math.Abs(vals[2]) <= 100000f;
    }


    private static string CodeOnly(string source)
    {
        var chars = source.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] != '\r' && chars[i] != '\n') chars[i] = ' ';
        foreach (var token in SourceAdmission.ReadTokens(source))
        {
            if (token.Value == "<literal>") continue;
            for (int i = token.Start; i < token.End; i++)
                chars[i] = source[i];
        }
        return new string(chars);
    }

    // Stable source-file owner, independent of arbitrary mod names, stack
    // order or capture hashes. Refuse hash collisions rather than co-own sites.
    private static int Owner(string relative)
    {
        uint hash = 2166136261;
        foreach (byte value in Encoding.UTF8.GetBytes(relative))
        {
            hash ^= value;
            hash *= 16777619;
        }
        return 100000000 + (int)(hash % 1000000000u);
    }

    internal static byte[] Compile(string gameRoot)
    {
        var game = Path.GetFullPath(gameRoot);
        var folder = Path.Combine(game, "r6", "scripts");
        var sites = new List<Site>();
        var owners = new Dictionary<int, string>();
        if (Directory.Exists(folder))
        {
            foreach (var file in Directory.EnumerateFiles(folder, "*.reds",
                SearchOption.AllDirectories).OrderBy(x => x,
                    StringComparer.OrdinalIgnoreCase))
            {
                string rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (rel.StartsWith("G-RedRuntime/", StringComparison.OrdinalIgnoreCase))
                    continue;
                string source = File.ReadAllText(file, Encoding.UTF8);
                var masked = CodeOnly(source);
                int owner = Owner(rel);
                foreach (Match m in StaticSphereMatches(masked))
                {
                    float x = float.Parse(m.Groups["x"].Value,
                        CultureInfo.InvariantCulture);
                    float y = float.Parse(m.Groups["y"].Value,
                        CultureInfo.InvariantCulture);
                    float z = float.Parse(m.Groups["z"].Value,
                        CultureInfo.InvariantCulture);
                    float radius = float.Parse(m.Groups["radius"].Value,
                        CultureInfo.InvariantCulture) + 16.0f;
                    if (radius > 1024f) continue;
                    if (owners.TryGetValue(owner, out string? prior) &&
                        prior != rel)
                        throw new InvalidDataException(
                            "SHARED_NATIVE_REDSCRIPT_OWNER_COLLISION");
                    owners[owner] = rel;
                    sites.Add(new(owner, x, y, z, radius, false));
                }
                foreach (Match m in Quad.Matches(masked))
                {
                    var xs = m.Groups["x"].Captures;
                    var ys = m.Groups["y"].Captures;
                    if (xs.Count != 4 || ys.Count != 4) continue;
                    var x = new float[4];
                    var y = new float[4];
                    bool valid = true;
                    for (int i = 0; i < 4; i++)
                    {
                        valid &= float.TryParse(xs[i].Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture,
                            out x[i]);
                        valid &= float.TryParse(ys[i].Value,
                            NumberStyles.Float, CultureInfo.InvariantCulture,
                            out y[i]);
                        valid &= float.IsFinite(x[i]) && float.IsFinite(y[i]) &&
                            Math.Abs(x[i]) <= 100000 && Math.Abs(y[i]) <= 100000;
                    }
                    if (!valid) continue;
                    float cx = (x[0] + x[1] + x[2] + x[3]) * 0.25f;
                    float cy = (y[0] + y[1] + y[2] + y[3]) * 0.25f;
                    float max2 = 0;
                    for (int i = 0; i < 4; i++)
                        max2 = Math.Max(max2,
                            (x[i] - cx) * (x[i] - cx) +
                            (y[i] - cy) * (y[i] - cy));
                    float radius = MathF.Sqrt(max2) + 16f;
                    if (!float.IsFinite(radius) || radius <= 0 ||
                        radius > 1024) continue;
                    if (owners.TryGetValue(owner, out string? prior) &&
                        prior != rel)
                        throw new InvalidDataException(
                            "SHARED_NATIVE_REDSCRIPT_OWNER_COLLISION");
                    owners[owner] = rel;
                    sites.Add(new(owner, cx, cy, 0f, radius, true));
                }
            }
        }
        if (sites.Count > 100000)
            throw new InvalidDataException("SHARED_NATIVE_REDSCRIPT_SITE_LIMIT");
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("GCAS"u8);
        writer.Write((uint)1); writer.Write((uint)sites.Count);
        writer.Write((uint)0);
        for (int i = 0; i < sites.Count; i++)
        {
            var site = sites[i];
            writer.Write(i + 1);
            writer.Write(site.X); writer.Write(site.Y);
            writer.Write(site.Z); writer.Write(site.Radius);
            writer.Write(site.XY ? 1u : 0u); writer.Write(site.Owner);
        }
        writer.Flush();
        return stream.ToArray();
    }
}
