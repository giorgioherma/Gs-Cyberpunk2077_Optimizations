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
        // Some authors return the literal array directly rather than
        // assigning it to a local first. Both forms have stable ordering.
        foreach (Match inline in Regex.Matches(masked,
            @"\breturn\s*\[", RegexOptions.CultureInvariant))
        {
            int close = masked.IndexOf("];", inline.Index + inline.Length,
                StringComparison.Ordinal);
            if (close > inline.Index)
                spans.Add((inline.Index, close));
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

    // Source-driven location-dependency cohorts: a Bool helper which builds
    // a static Vector4 set and immediately tests the supplied player's
    // world position against that set. No mod name or callback class.
    // One native owner per source METHOD, not per source file.
    // Native radius is deliberately wider than admitted source thresholds:
    // a stale FAR during regular player motion is the accepted experimental
    // risk; game teleport/session invalidation remains mandatory.
    private const float LocationGroupNativeRadius = 256f;
    internal sealed record LocationDependencyGroup(
        string RelativePath, string Method, int Owner, int Count,
        string PlayerName, int OpenBrace,
        IReadOnlyList<(float X, float Y, float Z)> Positions,
        string Mode, int PredicateStart, int PredicateLength,
        float NativeRadius);

    private static readonly Regex GroupConsumerMethod = new(
        @"\b(?:public|private|protected)\s+static\s+func\s+" +
        @"(?<method>[A-Za-z_]\w*)\s*\(\s*(?<player>[A-Za-z_]\w*)\s*:\s*" +
        @"(?:ref|wref)\s*<\s*(?:GameObject|PlayerPuppet)\s*>\s*\)" +
        @"\s*->\s*Bool\s*\{", RegexOptions.CultureInvariant);

    private static readonly Regex GroupResult = new(
        @"^\s*let\s+(?<list>[A-Za-z_]\w*)\s*" +
        @"(?::\s*array\s*<\s*Vector4\s*>)?\s*=\s*" +
        @"(?<initializer>\[[\s\S]*?\]|[A-Za-z_]\w*\.[A-Za-z_]\w*\s*\(\s*\))" +
        @"\s*;\s*return\s+(?<checker>[A-Za-z_]\w*)\.(?<near>[A-Za-z_]\w*)" +
        @"\s*\(\s*(?<player>[A-Za-z_]\w*)\.GetWorldPosition\s*\(\s*\)" +
        @"\s*,\s*(?<arg>[A-Za-z_]\w*)\s*,\s*(?<radius>" + Number +
        @")\s*\)\s*;\s*$", RegexOptions.CultureInvariant);

    private static readonly Regex NumericVector = new(
        @"(?:new\s+)?Vector4\s*\(\s*(?<x>" + Number + @")\s*,\s*" +
        @"(?<y>" + Number + @")\s*,\s*(?<z>" + Number + @")\s*,\s*" +
        @"(?<w>" + Number + @")\s*\)", RegexOptions.CultureInvariant);

    private static int GroupBlockEnd(string source, int open)
    {
        int depth = 0;
        foreach (var token in SourceAdmission.ReadTokens(source))
        {
            if (token.Start < open) continue;
            if (token.Value == "{") depth++;
            else if (token.Value == "}" && --depth == 0)
                return token.Start;
        }
        return -1;
    }

    private static IReadOnlyList<(float X, float Y, float Z)>
        ParseGroupVectors(string initializer)
    {
        if (!initializer.StartsWith("[", StringComparison.Ordinal) ||
            !initializer.EndsWith("]", StringComparison.Ordinal)) return [];
        string inner = initializer[1..^1];
        var found = NumericVector.Matches(inner).Cast<Match>().ToArray();
        if (found.Length == 0 || found.Length > 256) return [];
        // Every list element must be a literal Vector4, with no runtime
        // expression, mutable helper call or computed coordinate.
        string leftover = NumericVector.Replace(inner, "");
        if (leftover.Any(c => !char.IsWhiteSpace(c) && c != ','))
            return [];
        var points = new List<(float X, float Y, float Z)>();
        foreach (var match in found)
        {
            float[] values = new float[4];
            string[] axes = ["x", "y", "z", "w"];
            for (int n = 0; n < 4; n++)
                if (!float.TryParse(match.Groups[axes[n]].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out values[n]) || !float.IsFinite(values[n]))
                    return [];
            if (Math.Abs(values[0]) > 100000f ||
                Math.Abs(values[1]) > 100000f ||
                Math.Abs(values[2]) > 100000f) return [];
            points.Add((values[0], values[1], values[2]));
        }
        return points;
    }

    // Uses existing SourceAdmission tokenization for method-body boundaries
    // and comment/string masking. The same inventory drives BOTH generation
    // and compiled native site mapping (never a corrective second pass).
    internal static IReadOnlyList<LocationDependencyGroup>
        LocationDependencyGroups(string relative, string source)
    {
        if (!source.Contains("GetWorldPosition", StringComparison.Ordinal) ||
            !source.Contains("Vector4", StringComparison.Ordinal))
            return [];
        string masked = CodeOnly(source);
        var result = new List<LocationDependencyGroup>();
        foreach (Match method in GroupConsumerMethod.Matches(masked))
        {
            string methodName = method.Groups["method"].Value;
            string player = method.Groups["player"].Value;
            if (GroupConsumerMethod.Matches(masked).Cast<Match>()
                .Count(x => x.Groups["method"].Value == methodName) != 1)
                continue;
            int open = method.Index + method.Length - 1;
            int close = GroupBlockEnd(masked, open);
            if (close <= open) continue;
            string body = masked[(open + 1)..close];
            // Existing generated FAR guards are inert for inventory and
            // idempotence; the original author expression remains visible.
            body = Regex.Replace(body,
                @"^\s*if\s+GCETNativeLocationGuard\.DefinitelyFarPlayer" +
                @"\s*\([^;]*\)\s*\{\s*return\s+false\s*;\s*\}\s*",
                "", RegexOptions.CultureInvariant);
            var matched = GroupResult.Match(body);
            if (!matched.Success ||
                matched.Groups["list"].Value != matched.Groups["arg"].Value ||
                matched.Groups["player"].Value != player)
                continue;
            if (!float.TryParse(matched.Groups["radius"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture,
                out float radius) || !float.IsFinite(radius) ||
                radius <= 0f || radius > 128f) continue;

            string nearMethod = matched.Groups["near"].Value;
            // The helper may be named anything, but its local behavior must
            // contain a distance check and a false result for no matches.
            var near = Regex.Match(masked,
                @"\b(?:public|private|protected)\s+static\s+func\s+" +
                Regex.Escape(nearMethod) +
                @"\s*\([^)]*\)\s*->\s*Bool\s*\{",
                RegexOptions.CultureInvariant);
            if (!near.Success) continue;
            int nearClose = GroupBlockEnd(masked,
                near.Index + near.Length - 1);
            if (nearClose <= near.Index) continue;
            string nearBody = masked[near.Index..(nearClose + 1)];
            if (!nearBody.Contains("Vector4.Distance", StringComparison.Ordinal) ||
                !Regex.IsMatch(nearBody, @"\breturn\s+false\s*;",
                    RegexOptions.CultureInvariant)) continue;

            string init = matched.Groups["initializer"].Value.Trim();
            IReadOnlyList<(float X, float Y, float Z)> positions;
            if (init.StartsWith("[", StringComparison.Ordinal))
            {
                positions = ParseGroupVectors(init);
            }
            else
            {
                var fac = Regex.Match(init,
                    @"^(?<class>[A-Za-z_]\w*)\.(?<name>[A-Za-z_]\w*)" +
                    @"\s*\(\s*\)$", RegexOptions.CultureInvariant);
                if (!fac.Success) continue;
                string name = fac.Groups["name"].Value;
                var factories = Regex.Matches(masked,
                    @"\b(?:public|private|protected)\s+static\s+func\s+" +
                    Regex.Escape(name) +
                    @"\s*\(\s*\)\s*->\s*array\s*<\s*Vector4\s*>\s*\{",
                    RegexOptions.CultureInvariant);
                if (factories.Count != 1) continue;
                Match f = factories[0];
                int end = GroupBlockEnd(masked, f.Index + f.Length - 1);
                if (end <= f.Index) continue;
                string factoryBody = masked[(f.Index + f.Length)..end];
                var literal = Regex.Match(factoryBody,
                    @"^\s*let\s+(?<var>[A-Za-z_]\w*)\s*" +
                    @"(?::\s*array\s*<\s*Vector4\s*>)?\s*=\s*" +
                    @"(?<array>\[[\s\S]*?\])\s*;\s*return\s+" +
                    @"(?<returned>[A-Za-z_]\w*)\s*;\s*$",
                    RegexOptions.CultureInvariant);
                positions = literal.Success &&
                    literal.Groups["var"].Value == literal.Groups["returned"].Value
                    ? ParseGroupVectors(literal.Groups["array"].Value) : [];
            }
            if (positions.Count == 0) continue;
            int owner = Owner(relative + "#location:" + methodName);
            result.Add(new(relative, methodName, owner,
                positions.Count, player, open, positions,
                "STATIC_VECTOR_GROUP", -1, 0, LocationGroupNativeRadius));
        }
        // Generic fixed-point quest checks: the caller may have different
        // lifecycle, work, facts and callbacks, but the one geographic
        // expression is identical for FAR. Replace ONLY that Boolean
        // expression, not its enclosing method, quest state or callback.
        var directMethods = Regex.Matches(masked,
            @"\b(?:public|private|protected)\s+(?:static\s+)?" +
            @"(?:cb\s+)?func\s+(?<method>[A-Za-z_]\w*)\s*\([^)]*\)" +
            @"\s*(?:->\s*[A-Za-z_]\w*\s*)?\{",
            RegexOptions.CultureInvariant);
        var directDistance = new Regex(
            @"\bVector4\s*\.\s*Distance\s*\(\s*" +
            @"(?<player>[A-Za-z_]\w*)\s*\.\s*GetWorldPosition\s*" +
            @"\(\s*\)\s*,\s*(?<class>[A-Za-z_]\w*)\s*\." +
            @"(?<factory>[A-Za-z_]\w*)\s*\(\s*\)\s*\)\s*" +
            @"(?:>=|>)\s*(?<radius>" + Number + @")",
            RegexOptions.CultureInvariant);
        foreach (Match method in directMethods)
        {
            string methodName = method.Groups["method"].Value;
            if (directMethods.Cast<Match>().Count(x =>
                x.Groups["method"].Value == methodName) != 1 ||
                result.Any(x => x.Method == methodName)) continue;
            int open = method.Index + method.Length - 1;
            int close = GroupBlockEnd(masked, open);
            if (close <= open) continue;
            string body = masked[(open + 1)..close];
            var distances = directDistance.Matches(body).Cast<Match>().ToArray();
            if (distances.Length != 1) continue;
            Match distance = distances[0];
            string player = distance.Groups["player"].Value;
            string beforeCall = body[..distance.Index];
            if (!Regex.IsMatch(beforeCall,
                @"\blet\s+" + Regex.Escape(player) +
                @"\s*(?::\s*(?:ref|wref)\s*<\s*PlayerPuppet\s*>)?" +
                @"\s*=\s*GetPlayer\s*\(",
                RegexOptions.CultureInvariant)) continue;
            if (!float.TryParse(distance.Groups["radius"].Value,
                NumberStyles.Float, CultureInfo.InvariantCulture,
                out float radius) || !float.IsFinite(radius) ||
                radius <= 0 || radius > 960f) continue;
            string factoryName = distance.Groups["factory"].Value;
            var factories = Regex.Matches(masked,
                @"\b(?:public|private|protected)\s+static\s+func\s+" +
                Regex.Escape(factoryName) +
                @"\s*\(\s*\)\s*->\s*Vector4\s*\{",
                RegexOptions.CultureInvariant);
            if (factories.Count != 1) continue;
            Match factory = factories[0];
            int factoryEnd = GroupBlockEnd(masked,
                factory.Index + factory.Length - 1);
            if (factoryEnd <= factory.Index) continue;
            string value = masked[(factory.Index + factory.Length)..factoryEnd];
            var returned = Regex.Match(value,
                @"^\s*return\s+(?<point>(?:new\s+)?Vector4\s*" +
                @"\([^;]*\))\s*;\s*$", RegexOptions.CultureInvariant);
            if (!returned.Success) continue;
            var vector = NumericVector.Match(returned.Groups["point"].Value);
            if (!vector.Success ||
                vector.Length != returned.Groups["point"].Length)
                continue;
            float[] coords = new float[4];
            string[] axes = ["x", "y", "z", "w"];
            bool valid = true;
            for (int n = 0; n < 4; n++)
                valid &= float.TryParse(vector.Groups[axes[n]].Value,
                    NumberStyles.Float, CultureInfo.InvariantCulture,
                    out coords[n]) && float.IsFinite(coords[n]);
            if (!valid || coords.Take(3).Any(x => Math.Abs(x) > 100000f))
                continue;
            int start = open + 1 + distance.Index;
            result.Add(new LocationDependencyGroup(relative, methodName,
                Owner(relative + "#location:" + methodName + ":distance"),
                1, player, open,
                [(coords[0], coords[1], coords[2])],
                "STATIC_QUEST_DISTANCE", start, distance.Length,
                MathF.Min(1024f, radius + 32f)));
        }
        // Generated guard markers are owned by this same discovery lane.
        // Never silently omit a partially edited/tampered location method
        // merely because its original expression is no longer recognized.
        var markers = Regex.Matches(source,
            @"GCO-GENERIC:LOCATION_DEPENDENCY_FAR_V1:(?<owner>\d+)",
            RegexOptions.CultureInvariant);
        foreach (Match marker in markers)
        {
            if (!int.TryParse(marker.Groups["owner"].Value,
                    NumberStyles.None, CultureInfo.InvariantCulture,
                    out int owner) ||
                result.Count(x => x.Owner == owner) != 1 ||
                markers.Cast<Match>().Count(x =>
                    x.Groups["owner"].Value == marker.Groups["owner"].Value) != 1)
                throw new InvalidDataException(
                    "NATIVE_GROUP_GENERATED_GUARD_DRIFT");
        }
        return result;
    }

    internal static IReadOnlyList<LocationDependencyGroup>
        LocationDependencyGroups(string gameRoot)
    {
        var root = Path.Combine(Path.GetFullPath(gameRoot), "r6", "scripts");
        var result = new List<LocationDependencyGroup>();
        if (!Directory.Exists(root)) return result;
        foreach (string file in Directory.EnumerateFiles(root, "*.reds",
            SearchOption.AllDirectories).OrderBy(x => x,
            StringComparer.OrdinalIgnoreCase))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.StartsWith("G-RedRuntime/",
                StringComparison.OrdinalIgnoreCase)) continue;
            result.AddRange(LocationDependencyGroups(relative,
                File.ReadAllText(file, Encoding.UTF8)));
        }
        return result;
    }


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
        // Include only source-matched location-dependent Bool consumers.
        // The same SOURCE recognition drives their generated FAR guards.
        // A wide native 256m radius is used only when the original authored
        // proximity threshold is <=128m.
        foreach (var group in LocationDependencyGroups(game))
        {
            if (owners.TryGetValue(group.Owner, out string? previous) &&
                previous != group.RelativePath)
                throw new InvalidDataException(
                    "SHARED_NATIVE_REDSCRIPT_OWNER_COLLISION");
            owners[group.Owner] = group.RelativePath;
            foreach (var point in group.Positions)
                sites.Add(new(group.Owner, point.X, point.Y, point.Z,
                    group.NativeRadius, false));
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
