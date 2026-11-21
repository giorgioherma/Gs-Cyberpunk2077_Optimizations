using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Port RedWindows historical Pass 8 retained-widget no-op setters.
/// Shape color uses the owner's existing SameColor, opacity/border
/// opacity use the historical tested 0.001 visual tolerance, and text
/// tint avoids a duplicate node write when the value is unchanged.
/// Each operation is independent; no reordering or node rebuild changes.
/// </summary>
internal static class RedWindowsRedundantSettersPass
{
    internal const string Rule = "REDWINDOWS_HISTORICAL_REDUNDANT_SETTERS_V1";
    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(string Name, int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);

    private static readonly string[] Targets =
    [
        "SetShapeColor", "SetShapeOpacity",
        "SetShapeBorderOpacity", "SetWidgetColor"
    ];

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

    private static IReadOnlyList<Method> Methods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var found = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                !Targets.Contains(t[i + 1].Value,
                    StringComparer.Ordinal) ||
                t[i + 2].Value != "(")
                continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{")
                continue;
            int close = Close(t, open, "{", "}");
            if (close < 0) continue;
            found.Add(new(t[i + 1].Value,
                t[open].End, t[close].Start));
            i = close;
        }
        return found;
    }

    private static readonly Regex ColorStock = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*shape\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex ColorGuard = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*shape\s*\)\s*\|\|\s*" +
        @"RWWindow\s*\.\s*SameColor\s*\(\s*shape\s*\.\s*fill\s*,\s*color\s*\)" +
        @"\s*\{\s*return\s*;\s*\}");

    private static Outcome ExamineColor(string body, bool hasColorComparator)
    {
        if (!hasColorComparator ||
            !Regex.IsMatch(body,
                @"\bshape\s*\.\s*fill\s*=\s*color\s*;") ||
            !body.Contains("shape.node.SetTintColor(color)",
                StringComparison.Ordinal) ||
            !body.Contains("shape.imgNode.SetTintColor(color)",
                StringComparison.Ordinal))
            return new("REFUSED", "RW_SET_SHAPE_COLOR_FLOW_UNPROVEN", null);
        var old = ColorStock.Matches(body);
        var done = ColorGuard.Matches(body);
        if (done.Count == 1 && old.Count == 0)
            return new("COMPLETE", null, null);
        if (done.Count != 0 || old.Count != 1)
            return new("REFUSED", "RW_SET_SHAPE_COLOR_GUARD_UNPROVEN", null);
        string updated = body[..old[0].Index] +
            "if !IsDefined(shape) || RWWindow.SameColor(shape.fill, color) { return; }" +
            body[(old[0].Index + old[0].Length)..];
        return new("APPLIED", null, updated);
    }

    private sealed record Outcome(string Status, string? Reason,
        string? Updated);

    private static Outcome ExamineOpacity(string body,
        string property, string guardName)
    {
        string prop = Regex.Escape(property);
        string decl = @"\blet\s+clamped\s*:\s*Float\s*=\s*" +
            @"ClampF\s*\(\s*opacity\s*,\s*0\.0\s*,\s*1\.0\s*\)\s*;";
        string oldPattern = @"\bshape\s*\.\s*" + prop +
            @"\s*=\s*ClampF\s*\(\s*opacity\s*,\s*0\.0\s*,\s*1\.0\s*\)\s*;";
        string assign = @"\bshape\s*\.\s*" + prop +
            @"\s*=\s*clamped\s*;";
        string guard = @"\bif\s+AbsF\s*\(\s*shape\s*\.\s*" +
            prop + @"\s*-\s*clamped\s*\)\s*<\s*0\.001\s*\{\s*return\s*;\s*\}";
        string sentinel = "shape." + property;
        if (!body.Contains(sentinel, StringComparison.Ordinal) ||
            !body.Contains(guardName, StringComparison.Ordinal) ||
            !body.Contains("SetOpacity", StringComparison.Ordinal))
            return new("REFUSED", "RW_SET_" + property +
                "_WIDGET_SYNC_UNPROVEN", null);
        var completeDecl = Regex.Matches(body, decl);
        var old = Regex.Matches(body, oldPattern);
        var assignment = Regex.Matches(body, assign);
        var condition = Regex.Matches(body, guard);
        if (completeDecl.Count > 1 || old.Count > 1 ||
            assignment.Count > 1 || condition.Count > 1)
            return new("REFUSED", "RW_SET_" + property +
                "_AMBIGUOUS_WRITES", null);
        if (completeDecl.Count == 1 && assignment.Count == 1 &&
            condition.Count == 1 && old.Count == 0 &&
            completeDecl[0].Index < condition[0].Index &&
            condition[0].Index < assignment[0].Index)
            return new("COMPLETE", null, null);
        string nl = body.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        string replacement = "let clamped: Float = ClampF(opacity, 0.0, 1.0);" +
            nl + "    if AbsF(shape." + property +
            " - clamped) < 0.001 { return; }" +
            nl + "    shape." + property + " = clamped;";
        if (old.Count == 1 &&
            completeDecl.Count == 0 &&
            assignment.Count == 0 && condition.Count == 0)
        {
            if (Regex.IsMatch(body,
                @"\blet\s+clamped\s*:") ||
                Regex.IsMatch(body, @"\bclamped\b"))
                return new("REFUSED", "RW_SET_" + property +
                    "_LOCAL_COLLISION", null);
            return new("APPLIED", null,
                body[..old[0].Index] + replacement +
                body[(old[0].Index + old[0].Length)..]);
        }
        if (old.Count == 0 && completeDecl.Count == 1 &&
            assignment.Count == 1 && condition.Count == 0 &&
            completeDecl[0].Index < assignment[0].Index)
        {
            string guardText = "if AbsF(shape." + property +
                " - clamped) < 0.001 { return; }" + nl + "    ";
            return new("APPLIED", null,
                body[..assignment[0].Index] + guardText +
                body[assignment[0].Index..]);
        }
        return new("REFUSED", "RW_SET_" + property +
            "_CUSTOM_VALUE_OR_PARTIAL_FLOW_UNPROVEN", null);
    }

    private static Outcome ExamineWidgetText(string body)
    {
        if (!Regex.IsMatch(body,
            @"\bif\s+Equals\s*\(\s*part\s*,\s*RWPart\s*\.\s*Text\s*\)") ||
            !Regex.IsMatch(body,
                @"\bchanged\s*=\s*!\s*widget\s*\.\s*hasColor\s*\|\|\s*" +
                @"!\s*RWWindow\s*\.\s*SameColor\s*\(") ||
            !body.Contains("widget.text.SetTintColor(color)",
                StringComparison.Ordinal) ||
            !Regex.IsMatch(body, @"\bchanged\s*=\s*false\s*;"))
            return new("REFUSED", "RW_SET_TEXT_TINT_FLOW_UNPROVEN", null);
        var old=Regex.Matches(body,
            @"\bif\s+IsDefined\s*\(\s*widget\s*\.\s*text\s*\)\s*\{");
        var guarded=Regex.Matches(body,
            @"\bif\s+changed\s*&&\s*IsDefined\s*" +
            @"\(\s*widget\s*\.\s*text\s*\)\s*\{");
        if (guarded.Count == 1 && old.Count == 0)
            return new("COMPLETE", null, null);
        if (old.Count != 1 || guarded.Count != 0)
            return new("REFUSED", "RW_SET_TEXT_TINT_GUARD_UNPROVEN", null);
        return new("APPLIED", null,
            body[..old[0].Index] +
            "if changed && IsDefined(widget.text) {" +
            body[(old[0].Index + old[0].Length)..]);
    }

    private static (string Status, string? Reason, string? Updated,
        int Applied, int Complete) Rewrite(string source)
    {
        if (!Regex.IsMatch(source, @"\bclass\s+RWWindow\s*\{"))
            return ("REFUSED", "RW_SET_WINDOW_OWNER_MISSING", null, 0, 0);
        var methods = Methods(source);
        var edits = new List<Edit>();
        var refused = new List<string>();
        int applied = 0, completed = 0;
        bool hasColorComparator = Regex.IsMatch(source,
            @"\b(?:private|public|protected)\s+static\s+func\s+SameColor\s*\(");
        foreach (string target in Targets)
        {
            var matching = methods.Where(m => m.Name == target).ToArray();
            if (matching.Length != 1)
            {
                refused.Add(target + ":RW_SET_METHOD_MISSING_OR_AMBIGUOUS");
                continue;
            }
            var method = matching[0];
            string body = source[method.Open..method.Close];
            Outcome result = target switch
            {
                "SetShapeColor" => ExamineColor(body, hasColorComparator),
                "SetShapeOpacity" => ExamineOpacity(body, "opacity",
                    "shape.node.SetOpacity(shape.opacity)"),
                "SetShapeBorderOpacity" => ExamineOpacity(body, "borderOpacity",
                    "shape.borderNode.SetOpacity(shape.borderOpacity)"),
                "SetWidgetColor" => ExamineWidgetText(body),
                _ => throw new InvalidDataException("RW_SET_UNKNOWN_TARGET")
            };
            if (result.Status == "REFUSED")
                refused.Add(target + ":" + result.Reason);
            else if (result.Status == "COMPLETE")
                completed++;
            else if (result.Updated is not null)
            {
                applied++;
                edits.Add(new(method.Open, method.Close,
                    result.Updated));
            }
        }
        if (edits.Count == 0)
            return (refused.Count > 0 ? "REFUSED" : "COMPLETE",
                refused.Count > 0 ? string.Join("; ", refused) : null,
                null, 0, completed);
        string updated = source;
        foreach (var e in edits.OrderByDescending(x => x.Start))
            updated = updated[..e.Start] + e.Text + updated[e.End..];

        var finalMethods = Methods(updated);
        foreach (string target in Targets)
        {
            if (refused.Any(x => x.StartsWith(target + ":",
                StringComparison.Ordinal)))
                continue;
            var m = finalMethods.Single(x => x.Name == target);
            string body=updated[m.Open..m.Close];
            Outcome check=target switch
            {
                "SetShapeColor" => ExamineColor(body, hasColorComparator),
                "SetShapeOpacity" => ExamineOpacity(body, "opacity",
                    "shape.node.SetOpacity(shape.opacity)"),
                "SetShapeBorderOpacity" => ExamineOpacity(body,
                    "borderOpacity",
                    "shape.borderNode.SetOpacity(shape.borderOpacity)"),
                "SetWidgetColor" => ExamineWidgetText(body),
                _ => throw new InvalidDataException("RW_SET_UNKNOWN_TARGET")
            };
            if (check.Status != "COMPLETE")
                throw new InvalidDataException(
                    "RW_SET_GENERATED_POSTCONDITION:" +
                    target + ":" + check.Reason);
        }
        return ("APPLIED",
            refused.Count > 0 ? string.Join("; ", refused) : null,
            updated, applied, completed);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("RW_SET_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot,game) ||
            Inside(output,basePreviewRoot) ||
            Inside(basePreviewRoot,output)))
            throw new InvalidDataException("RW_SET_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string hash=admission.SourceStackSha256 ??
            throw new InvalidDataException("RW_SET_HASH_MISSING");
        string owner=Path.Combine(scripts,"RedWindows");
        string status="NOT_APPLICABLE";
        string? relative=null,reason=null,updated=null;
        int edits=0,completed=0;
        if (Directory.Exists(owner))
        {
            var matches=Directory.EnumerateFiles(owner,"*.reds",
                SearchOption.AllDirectories)
                .Where(p=>Regex.IsMatch(
                    File.ReadAllText(p,Encoding.UTF8),
                    @"\bclass\s+RWWindow\s*\{"))
                .ToArray();
            if (matches.Length != 1)
            {
                status="REFUSED";
                reason="RW_SET_OWNER_NOT_UNIQUE";
            }
            else
            {
                string file=matches[0];
                if ((File.GetAttributes(file) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("RW_SET_LINKED_SOURCE");
                relative="r6/scripts/"+
                    Path.GetRelativePath(scripts,file).Replace('\\','/');
                string source=File.ReadAllText(file,Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string earlier=Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if (!Inside(earlier,basePreviewRoot))
                        throw new InvalidDataException("RW_SET_BASE_ESCAPE");
                    if (File.Exists(earlier))
                        source=File.ReadAllText(earlier,Encoding.UTF8);
                }
                var result=Rewrite(source);
                status=result.Status;
                reason=result.Reason;
                updated=result.Updated;
                edits=result.Applied;
                completed=result.Complete;
            }
        }
        if (!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RW_SET_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (updated is not null && relative is not null)
        {
            string file=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(file,output) ||
                !file.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RW_SET_STAGE_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,updated,new UTF8Encoding(false));
        }
        var report=new Result(Rule,status,edits,completed,relative,reason,
            true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions
            {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string path,string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
