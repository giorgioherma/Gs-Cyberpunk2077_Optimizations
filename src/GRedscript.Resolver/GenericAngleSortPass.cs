using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Strict, mod-agnostic four-vertex angle sort rewrite. The inner loop's
/// angle for pts[i] is loop-invariant until a swap. Reuse it and replace
/// it with the already-evaluated angleJ after that swap. This reduces
/// AtanF evaluations from 12 to 9 per four-vertex sort, without changing
/// comparison order, swap order, vertex order, callbacks or RNG.
/// Source matching is intentionally narrow and preview-only.
/// </summary>
internal static class GenericAngleSortPass
{
    internal const string Rule = "ANGLE_SORT_INVARIANT_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;


    // G-BoundedSpatial V1: reject only *far* outside points with finite
    // coordinates and every possible triangle non-degenerate. The original
    // sorter/triangle test remains the exact fallback for all other cases.
    // This guards the historical Sign() zero-area behavior, where degenerate
    // triangles can otherwise report true even for outside points.
    private const string QuadRule = "QUAD_SAFE_AABB_BROADPHASE_V1";
    private const string QuadMarker = "// GCO-GENERIC:" + QuadRule;
    private const string QuadOriginal = """
        public func IsPointInQuad(p: Vector2, sq: SquareArea) -> Bool {
            let pts = SortSquare(sq);
            if (PointInTriangle(p, pts[0], pts[1], pts[2])) {
                return true;
            }
            if (PointInTriangle(p, pts[0], pts[2], pts[3])) {
                return true;
            }
            return false;
        }
        """;
    private const string QuadGuard = """
        if p.X > -100000.0 && p.X < 100000.0 &&
           p.Y > -100000.0 && p.Y < 100000.0 &&
           sq.a.X > -100000.0 && sq.a.X < 100000.0 &&
           sq.a.Y > -100000.0 && sq.a.Y < 100000.0 &&
           sq.b.X > -100000.0 && sq.b.X < 100000.0 &&
           sq.b.Y > -100000.0 && sq.b.Y < 100000.0 &&
           sq.c.X > -100000.0 && sq.c.X < 100000.0 &&
           sq.c.Y > -100000.0 && sq.c.Y < 100000.0 &&
           sq.d.X > -100000.0 && sq.d.X < 100000.0 &&
           sq.d.Y > -100000.0 && sq.d.Y < 100000.0 {
            let outside =
                (p.X < sq.a.X - 16.0 && p.X < sq.b.X - 16.0 &&
                 p.X < sq.c.X - 16.0 && p.X < sq.d.X - 16.0) ||
                (p.X > sq.a.X + 16.0 && p.X > sq.b.X + 16.0 &&
                 p.X > sq.c.X + 16.0 && p.X > sq.d.X + 16.0) ||
                (p.Y < sq.a.Y - 16.0 && p.Y < sq.b.Y - 16.0 &&
                 p.Y < sq.c.Y - 16.0 && p.Y < sq.d.Y - 16.0) ||
                (p.Y > sq.a.Y + 16.0 && p.Y > sq.b.Y + 16.0 &&
                 p.Y > sq.c.Y + 16.0 && p.Y > sq.d.Y + 16.0);
            if outside &&
               AbsF(Sign(sq.a, sq.b, sq.c)) > 1.0 &&
               AbsF(Sign(sq.a, sq.b, sq.d)) > 1.0 &&
               AbsF(Sign(sq.a, sq.c, sq.d)) > 1.0 &&
               AbsF(Sign(sq.b, sq.c, sq.d)) > 1.0 {
                return false;
            }
        }
        """;
    private const string SquareDeclaration = """
        public struct SquareArea {
            public let a: Vector2;
            public let b: Vector2;
            public let c: Vector2;
            public let d: Vector2;
        }
        """;
    private const string SignDefinition = """
        private func Sign(p1: Vector2, p2: Vector2, p3: Vector2) -> Float {
            return (p1.X - p3.X) * (p2.Y - p3.Y) - (p2.X - p3.X) * (p1.Y - p3.Y);
        }
        """;
    private const string TriangleDefinition = """
        private func PointInTriangle(pt: Vector2, v1: Vector2, v2: Vector2, v3: Vector2) -> Bool {
            let d1 = Sign(pt, v1, v2);
            let d2 = Sign(pt, v2, v3);
            let d3 = Sign(pt, v3, v1);
            let hasNeg = (d1 < 0.0) || (d2 < 0.0) || (d3 < 0.0);
            let hasPos = (d1 > 0.0) || (d2 > 0.0) || (d3 > 0.0);
            return !(hasNeg && hasPos);
        }
        """;
    private static readonly string[] QuadOriginalTokens = Values(QuadOriginal);
    private static readonly string[] QuadOptimizedTokens = Values(
        QuadOriginal.Replace("let pts = SortSquare(sq);",
            QuadGuard + "\nlet pts = SortSquare(sq);",
            StringComparison.Ordinal));
    private static readonly string[] SquareTokens = Values(SquareDeclaration);
    private static readonly string[] SignTokens = Values(SignDefinition);
    private static readonly string[] TriangleTokens = Values(TriangleDefinition);

    private const string Prelude = """
        let pts: array<Vector2> = [sq.a, sq.b, sq.c, sq.d];
        let i = 0;
        let j: Int32;
        """;

    private const string Original = """
        while (i < ArraySize(pts)) {
            j = i + 1;
            while (j < ArraySize(pts)) {
                let angleI = AtanF(pts[i].Y - center.Y, pts[i].X - center.X);
                let angleJ = AtanF(pts[j].Y - center.Y, pts[j].X - center.X);

                if (angleI > angleJ) {
                    let tmp = pts[i];
                    pts[i] = pts[j];
                    pts[j] = tmp;
                }

                j += 1;
            }
            i += 1;
        }
        """;

    private const string Optimized = """
        while (i < ArraySize(pts)) {
            j = i + 1;
            if (j < ArraySize(pts)) {
                let angleI = AtanF(pts[i].Y - center.Y, pts[i].X - center.X);
                while (j < ArraySize(pts)) {
                    let angleJ = AtanF(pts[j].Y - center.Y, pts[j].X - center.X);
                    if (angleI > angleJ) {
                        let tmp = pts[i];
                        pts[i] = pts[j];
                        pts[j] = tmp;
                        angleI = angleJ;
                    }
                    j += 1;
                }
            }
            i += 1;
        }
        """;

    private static readonly string[] PreludeTokens = Values(Prelude);
    private static readonly string[] OriginalTokens = Values(Original);
    private static readonly string[] OptimizedTokens = Values(Optimized);
    private static readonly string[] ReturnTokens = Values("return pts;");

    private sealed record Edit(int Start, int End, int Line, string Kind = "SORT");
    internal sealed record Summary(int Files, int Eligible, int AlreadyComplete);

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputFile);
        if (Within(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("ANGLE_SORT_SCAN_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var rows = new List<object>();
        int eligible = 0, complete = 0;
        foreach (var file in Files(game, admission))
        {
            var found = Examine(file.Text, file.Tokens);
            eligible += found.Edits.Count;
            complete += found.Complete;
            foreach (var edit in found.Edits)
                rows.Add(new {
                    path = "r6/scripts/" + file.Relative, line = edit.Line,
                    rule = edit.Kind == "QUAD" ? QuadRule : Rule,
                    state = "PRISTINE_APPLICABLE",
                    comparisonOrderPreserved = true,
                    callbackCadenceUnchanged = true,
                    finiteAndNondegenerateFastRejectOnly = edit.Kind == "QUAD",
                    originalGeometryFallbackRetained = true,
                    generationAuthorized = false
                });
        }
        AssertStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.angle-sort.scan.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = admission.InspectedFileCount,
            eligible, alreadyComplete = complete,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            measuredSavingsClaimed = false,
            sourceWrites = false,
            candidates = rows
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, complete);
    }

    internal static int Preview(string gameRoot, string analysisFile, string outputRoot)
    {
        string game = Path.GetFullPath(gameRoot);
        string analysis = Path.GetFullPath(analysisFile);
        string output = Path.GetFullPath(outputRoot);
        if (Within(output, game) || File.Exists(output) ||
            (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("ANGLE_SORT_PREVIEW_OUTPUT_NOT_ISOLATED");
        if (!File.Exists(analysis)) throw new InvalidDataException("ANGLE_SORT_CAPTURE_MISSING");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var doc = JsonDocument.Parse(File.ReadAllText(analysis)))
        {
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance").GetString() !=
                    "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source").GetProperty("sourceStackSha256")
                        .GetString(), admission.SourceStackSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ANGLE_SORT_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");
        }
        var staged = new List<(string Relative, string Source)>();
        int count = 0;
        foreach (var file in Files(game, admission))
        {
            var found = Examine(file.Text, file.Tokens);
            if (found.Edits.Count == 0) continue;
            string changed = file.Text;
            foreach (var edit in found.Edits.OrderByDescending(e => e.Start))
            {
                string newline = changed.Contains("\r\n", StringComparison.Ordinal)
                    ? "\r\n" : "\n";
                int lineStart = changed.LastIndexOf('\n', Math.Max(0, edit.Start - 1)) + 1;
                string indent = changed[lineStart..edit.Start];
                if (indent.Any(c => c is not (' ' or '\t')))
                    throw new InvalidDataException("ANGLE_SORT_UNSAFE_INDENT");
                string replacement = edit.Kind == "QUAD"
                    ? QuadMarker + newline + indent +
                        QuadGuard.Replace("\n", newline + indent,
                            StringComparison.Ordinal) + newline + indent
                    : Marker + newline + indent +
                        Optimized.Replace("\n", newline + indent,
                            StringComparison.Ordinal);
                changed = changed[..edit.Start] + replacement + changed[edit.End..];
                count++;
            }
            staged.Add((file.Relative, changed));
        }
        AssertStable(admission);
        foreach (var item in staged)
        {
            string target = Path.GetFullPath(Path.Combine(output, "r6", "scripts",
                item.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(target, output) || File.Exists(target))
                throw new InvalidDataException("ANGLE_SORT_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, item.Source, new UTF8Encoding(false));
        }
        return count;
    }

    private sealed record Script(string Relative, string Text,
        IReadOnlyList<SourceAdmission.Token> Tokens);
    private sealed record Examined(List<Edit> Edits, int Complete);

    private static IEnumerable<Script> Files(string game, SourceAdmission admission)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        foreach (string path in Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!admission.IsFileIndexed(path))
                continue;
            var bytes = File.ReadAllBytes(path);
            string text = Encoding.UTF8.GetString(bytes);
            yield return new(Path.GetRelativePath(scripts, path).Replace('\\', '/'),
                text, SourceAdmission.ReadTokens(text));
        }
    }

    private static Examined Examine(string text, IReadOnlyList<SourceAdmission.Token> t)
    {
        var edits = new List<Edit>();
        int complete = 0;
        for (int i = 0; i < t.Count; i++)
        {
            if (!Match(t, i, PreludeTokens.Length * -1, PreludeTokens))
                continue;
            if (Match(t, i, 0, OriginalTokens) &&
                Match(t, i, OriginalTokens.Length, ReturnTokens))
            {
                int endIndex = i + OriginalTokens.Length - 1;
                int start = t[i].Start;
                int end = t[endIndex].End;
                string span = text[start..end];
                if (span.Contains("//", StringComparison.Ordinal) ||
                    span.Contains("/*", StringComparison.Ordinal))
                    throw new InvalidDataException("ANGLE_SORT_COMMENT_IN_REWRITTEN_SCOPE");
                edits.Add(new(start, end, t[i].Line));
            }
            else if (Match(t, i, 0, OptimizedTokens) &&
                     Match(t, i, OptimizedTokens.Length, ReturnTokens))
            {
                int markerPos = text.LastIndexOf(Marker, t[i].Start,
                    StringComparison.Ordinal);
                if (markerPos < 0 ||
                    !string.IsNullOrWhiteSpace(text[
                        (markerPos + Marker.Length)..t[i].Start]))
                    throw new InvalidDataException("ANGLE_SORT_UNOWNED_OPTIMIZED_SHAPE");
                complete++;
            }
        }
        // Broader spatial elimination is admitted only when the current file
        // contains the entire original 4-point struct, signed-area predicate,
        // triangle policy and the already structurally verified sort. No
        // geometry is cached across callbacks or owners.
        int quadComplete = 0;
        bool provider = (edits.Count > 0 || complete > 0) &&
            CountShape(t, SquareTokens) == 1 &&
            CountShape(t, SignTokens) == 1 &&
            CountShape(t, TriangleTokens) == 1 &&
            !t.Any(x => x.Value is ("ModuleExists" or "ModuleNotExists"));
        if (provider)
        {
            for (int i = 0; i < t.Count; i++)
            {
                if (Match(t, i, 0, QuadOriginalTokens))
                {
                    // Insert directly before SortSquare; never rewrite the
                    // original value, sorting, sign or callback source.
                    int first = i + Array.IndexOf(QuadOriginalTokens, "let");
                    if (first <= i || first >= t.Count ||
                        t[first].Value != "let" || t[first + 1].Value != "pts")
                        throw new InvalidDataException(
                            "QUAD_BROADPHASE_UNPROVEN_ORIGINAL");
                    edits.Add(new(t[first].Start, t[first].Start,
                        t[first].Line, "QUAD"));
                }
                else if (Match(t, i, 0, QuadOptimizedTokens))
                {
                    int first = i + Array.IndexOf(QuadOptimizedTokens, "if");
                    int pos = t[first].Start;
                    int marker = text.LastIndexOf(QuadMarker, pos,
                        StringComparison.Ordinal);
                    if (marker < 0 || !string.IsNullOrWhiteSpace(
                        text[(marker + QuadMarker.Length)..pos]))
                        throw new InvalidDataException(
                            "QUAD_BROADPHASE_UNOWNED_GENERATED_SOURCE");
                    quadComplete++;
                }
            }
        }
        int quadMarkers = text.Split(QuadMarker, StringSplitOptions.None).Length - 1;
        if (quadMarkers != quadComplete)
            throw new InvalidDataException(
                "QUAD_BROADPHASE_PARTIAL_OR_DRIFTED_MARKER");
        // The two distinct markers are validated against their own
        // transformation counts. A completed quad guard must not be mistaken
        // for an extra completed angle-sort body.
        int markers = text.Split(Marker, StringSplitOptions.None).Length - 1;
        if (markers != complete)
            throw new InvalidDataException("ANGLE_SORT_PARTIAL_OR_DRIFTED_GENERATED_MARKER");
        return new(edits, complete + quadComplete);
    }

    private static int CountShape(IReadOnlyList<SourceAdmission.Token> t,
        string[] shape)
    {
        int count = 0;
        for (int i = 0; i < t.Count; i++)
            if (Match(t, i, 0, shape)) count++;
        return count;
    }

    private static bool Match(IReadOnlyList<SourceAdmission.Token> source,
        int index, int offset, string[] shape)
    {
        int at = index + offset;
        if (at < 0 || at + shape.Length > source.Count) return false;
        for (int i = 0; i < shape.Length; i++)
            if (!string.Equals(source[at + i].Value, shape[i],
                StringComparison.Ordinal))
                return false;
        return true;
    }

    private static string[] Values(string src) =>
        SourceAdmission.ReadTokens(src).Select(t => t.Value).ToArray();

    private static void AssertStable(SourceAdmission admission)
    {
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(), StringComparison.Ordinal))
            throw new InvalidDataException("ANGLE_SORT_SOURCE_DRIFT");
    }

    private static bool Within(string path, string root) =>
        path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
