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
