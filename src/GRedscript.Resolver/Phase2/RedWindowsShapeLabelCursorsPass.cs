using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// RedWindows historical Pass 8 shape/label retained-index cursors.
/// These are search-start hints, not cached object references. Both searches
/// still scan every current array entry, so clearing or changing publicly
/// exposed widget arrays cannot return an obsolete shape/label reference.
/// </summary>
internal static class RedWindowsShapeLabelCursorsPass
{
    internal const string Rule = "REDWINDOWS_SHAPE_LABEL_CURSORS_V1";
    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);
    private sealed record Span(string Name, int Open, int End);
    private sealed record Edit(int Start, int End, string Replacement);

    private const string ShapeStock = """
        for shape in this.shapes {
          if Equals(shape.id, shapeId) { return shape; }
        }
        return null;
        """;
    private const string LabelStock = """
        for label in this.labels {
          if Equals(label.id, labelId) { return label; }
        }
        return null;
        """;
    private const string ShapeFinal = """
        let count: Int32 = ArraySize(this.shapes);
        if count <= 0 { return null; }
        let start: Int32 = this.m_shapeFindCursor;
        if start < 0 || start >= count { start = 0; }
        let offset: Int32 = 0;
        while offset < count {
          let idx: Int32 = start + offset;
          if idx >= count { idx -= count; }
          let shape: ref<RWShape> = this.shapes[idx];
          if IsDefined(shape) && Equals(shape.id, shapeId) {
            this.m_shapeFindCursor = idx;
            return shape;
          }
          offset += 1;
        }
        return null;
        """;
    private const string LabelFinal = """
        let count: Int32 = ArraySize(this.labels);
        if count <= 0 { return null; }
        let start: Int32 = this.m_labelFindCursor;
        if start < 0 || start >= count { start = 0; }
        let offset: Int32 = 0;
        while offset < count {
          let idx: Int32 = start + offset;
          if idx >= count { idx -= count; }
          let label: ref<RWShapeLabel> = this.labels[idx];
          if IsDefined(label) && Equals(label.id, labelId) {
            this.m_labelFindCursor = idx;
            return label;
          }
          offset += 1;
        }
        return null;
        """;
    private static string Shape(string text) =>
        string.Join("|", SourceAdmission.ReadTokens(text).Select(x => x.Value));

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int first, string open, string close)
    {
        if (first < 0 || first >= t.Count || t[first].Value != open)
            return -1;
        int depth = 0;
        for (int i = first; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }

    private static IReadOnlyList<Span> Methods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var result = new List<Span>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                t[i + 1].Value is not ("FindShape" or "FindLabel") ||
                t[i + 2].Value != "(") continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Close(t, open, "{", "}");
            if (end < 0) continue;
            string header = source[t[i].Start..t[open].Start];
            string expected = t[i + 1].Value == "FindShape" ?
                @"\bshapeId\s*:\s*CName\b" :
                @"\blabelId\s*:\s*CName\b";
            if (Regex.IsMatch(header, expected))
                result.Add(new(t[i + 1].Value, t[open].Start, t[end].End));
            i = end;
        }
        return result;
    }

    private static (string Status, string? Reason, string? Changed)
        Rewrite(string source)
    {
        if (!Regex.IsMatch(source, @"\bclass\s+RWWidget\s*\{") ||
            !Regex.IsMatch(source,
                @"\bpublic\s+let\s+shapes\s*:\s*array\s*<\s*ref\s*<\s*RWShape\s*>\s*>\s*;") ||
            !Regex.IsMatch(source,
                @"\bpublic\s+let\s+labels\s*:\s*array\s*<\s*ref\s*<\s*RWShapeLabel\s*>\s*>\s*;"))
            return ("REFUSED", "RW_CURSOR_WIDGET_OWNER_UNPROVEN", null);
        var methods = Methods(source);
        if (methods.Count(x => x.Name == "FindShape") != 1 ||
            methods.Count(x => x.Name == "FindLabel") != 1)
            return ("REFUSED", "RW_CURSOR_TARGET_AMBIGUOUS", null);
        var edits = new List<Edit>();
        var refusals = new List<string>();
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        foreach (string name in new[] { "FindShape", "FindLabel" })
        {
            var method = methods.Single(x => x.Name == name);
            string old = source[(method.Open + 1)..(method.End - 1)];
            string target = name == "FindShape" ? ShapeFinal : LabelFinal;
            if (Shape(old) == Shape(target)) continue;
            string stock = name == "FindShape" ? ShapeStock : LabelStock;
            if (Shape(old) != Shape(stock))
            {
                refusals.Add(name + ":RW_CURSOR_CUSTOM_SEARCH_UNPROVEN");
                continue;
            }
            // The source may contain custom comments but no extra executable
            // statements. Preserve the comments when replacing its loop.
            var comments = Regex.Matches(old,
                @"//[^\r\n]*|/\*[\s\S]*?\*/")
                .Select(m => m.Value).ToArray();
            string prefix = comments.Length == 0 ? "" :
                string.Join(nl + "    ", comments) + nl + "    ";
            string formatted = nl + "    " + prefix +
                target.Replace("\n", nl + "    ").TrimEnd() + nl + "  ";
            edits.Add(new(method.Open + 1, method.End - 1, formatted));
        }
        // Complete a partially cached implementation without duplicating
        // either private field. An unproven conflicting declaration prevents
        // changing this owner rather than silently shadowing that field.
        var fields = new[]
        {
            "m_shapeFindCursor", "m_labelFindCursor"
        };
        var missing = new List<string>();
        foreach (string field in fields)
        {
            var declarations = Regex.Matches(source,
                @"\b(?:private|protected|public)\s+let\s+" +
                field + @"\s*:\s*(?<type>\w+)\s*;");
            if (declarations.Count > 1 ||
                (declarations.Count == 1 &&
                 declarations[0].Groups["type"].Value != "Int32"))
                return ("REFUSED", "RW_CURSOR_FIELD_CONFLICT:" + field, null);
            if (declarations.Count == 0)
            {
                if (SourceAdmission.ReadTokens(source)
                    .Any(t => t.Value == field) &&
                    !methods.Any(m => source[(m.Open + 1)..(m.End - 1)]
                        .Contains("this." + field,StringComparison.Ordinal)))
                    return ("REFUSED", "RW_CURSOR_UNOWNED_FIELD_NAME:" + field,
                        null);
                missing.Add(field);
            }
        }
        if (missing.Count > 0)
        {
            var anchor = Regex.Match(source,
                @"\bpublic\s+let\s+labels\s*:\s*array\s*<\s*ref\s*<\s*RWShapeLabel\s*>\s*>\s*;");
            string additions = string.Concat(missing.Select(x =>
                nl + "  private let " + x + ": Int32;"));
            edits.Add(new(anchor.Index + anchor.Length,
                anchor.Index + anchor.Length, additions));
        }

        if (edits.Count == 0)
        {
            return (refusals.Count > 0 ? "REFUSED" : "COMPLETE",
                refusals.Count > 0 ? string.Join("; ", refusals) : null, null);
        }
        string updated = source;
        foreach (var e in edits.OrderByDescending(x => x.Start))
            updated = updated[..e.Start] + e.Replacement + updated[e.End..];
        var after = Methods(updated);
        foreach (string name in new[] { "FindShape", "FindLabel" })
        {
            var method = after.Single(x => x.Name == name);
            string text = updated[(method.Open + 1)..(method.End - 1)];
            string target = name == "FindShape" ? ShapeFinal : LabelFinal;
            bool wasRejected = refusals.Any(x =>
                x.StartsWith(name + ":", StringComparison.Ordinal));
            if (!wasRejected && Shape(text) != Shape(target))
                throw new InvalidDataException(
                    "RW_CURSOR_POSTCONDITION:" + name);
        }
        foreach (string field in fields)
            if (!Regex.IsMatch(updated,
                @"\bprivate\s+let\s+" + field + @"\s*:\s*Int32\s*;"))
                throw new InvalidDataException(
                    "RW_CURSOR_FIELD_POSTCONDITION:" + field);
        return ("APPLIED",
            refusals.Count > 0 ? string.Join("; ", refusals) : null, updated);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("RW_CURSOR_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot,game) || Inside(output,basePreviewRoot) ||
            Inside(basePreviewRoot,output)))
            throw new InvalidDataException("RW_CURSOR_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sourceHash = admission.SourceStackSha256 ??
            throw new InvalidDataException("RW_CURSOR_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts,"RedWindows");
        string status = "NOT_APPLICABLE";
        string? rel = null,changed = null, reason = null;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner,"*.reds",
                SearchOption.AllDirectories).Where(f =>
                    File.ReadAllText(f,Encoding.UTF8).Contains(
                        "class RWWidget",StringComparison.Ordinal)).ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = "RW_CURSOR_WIDGET_OWNER_NOT_UNIQUE";
            }
            else
            {
                string file = files[0];
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("RW_CURSOR_OWNER_LINK");
                rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts,file).Replace('\\','/');
                string effective = File.ReadAllText(file,Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string previous = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if (!Inside(previous,basePreviewRoot))
                        throw new InvalidDataException("RW_CURSOR_OVERLAY_ESCAPE");
                    if (File.Exists(previous))
                        effective = File.ReadAllText(previous,Encoding.UTF8);
                }
                var state = Rewrite(effective);
                status = state.Status; reason = state.Reason;
                changed = state.Changed;
            }
        }
        if (!string.Equals(sourceHash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RW_CURSOR_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (changed is not null && rel is not null)
        {
            string file = Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(file,output) ||
                !file.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RW_CURSOR_STAGE_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,changed,new UTF8Encoding(false));
        }
        var report = new Result(Rule,status,status=="APPLIED"?1:0,
            status=="COMPLETE"?1:0,rel,reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return report;
    }
    private static bool Inside(string path,string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
