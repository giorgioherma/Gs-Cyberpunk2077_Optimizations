using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// RedWindows historical Pass 9 retained-widget Find optimization.
/// Admit the cursor only after proving an append-only private widget array
/// and Find-before-append callers. The final exact-hit + predicted-next
/// lookup supersedes historical Pass 5 last-only and Pass 8 cursor-only.
/// </summary>
internal static class RedWindowsWidgetLookupPass
{
    internal const string Rule = "REDWINDOWS_RETAINED_WIDGET_LOOKUP_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Span(int Start, int Open, int End, string Name);
    private sealed record Edit(int Start, int End, string Replacement);

    private const string Original = """
        for widget in this.m_widgets {
          if Equals(widget.id, id) { return widget; }
        }
        return null;
        """;
    private const string Pass5 = """
        if IsDefined(this.m_findCache) && Equals(this.m_findCacheID, id) {
          return this.m_findCache;
        }
        for widget in this.m_widgets {
          if Equals(widget.id, id) {
            this.m_findCacheID = id;
            this.m_findCache = widget;
            return widget;
          }
        }
        return null;
        """;
    private const string Pass8 = """
        let count: Int32 = ArraySize(this.m_widgets);
        if count <= 0 { return null; }
        let start: Int32 = this.m_findCursor;
        if start < 0 || start >= count { start = 0; }
        let offset: Int32 = 0;
        while offset < count {
          let idx: Int32 = start + offset;
          if idx >= count { idx -= count; }
          let widget: ref<RWWidget> = this.m_widgets[idx];
          if IsDefined(widget) && Equals(widget.id, id) {
            this.m_findCursor = idx;
            return widget;
          }
          offset += 1;
        }
        return null;
        """;
    private const string Final = """
        if IsDefined(this.m_lastFindWidget) && Equals(this.m_lastFindId, id) {
          return this.m_lastFindWidget;
        }

        let count: Int32 = ArraySize(this.m_widgets);
        if count <= 0 {
          return null;
        }

        let start: Int32 = this.m_findCursor;
        if start < 0 || start >= count {
          start = 0;
        }

        let offset: Int32 = 0;
        while offset < count {
          let idx: Int32 = start + offset;
          if idx >= count {
            idx -= count;
          }
          let widget: ref<RWWidget> = this.m_widgets[idx];
          if IsDefined(widget) && Equals(widget.id, id) {
            this.m_lastFindId = id;
            this.m_lastFindWidget = widget;
            this.m_findCursor = idx + 1;
            if this.m_findCursor >= count { this.m_findCursor = 0; }
            return widget;
          }
          offset += 1;
        }
        return null;
        """;
    private static string Shape(string value) =>
        string.Join("\u001f", SourceAdmission.ReadTokens(value).Select(t => t.Value));
    private static readonly string StockShape = Shape(Original);
    private static readonly string Pass5Shape = Shape(Pass5);
    private static readonly string Pass8Shape = Shape(Pass8);
    private static readonly string FinalShape = Shape(Final);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (start >= t.Count || t[start].Value != opening) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }
    private static (Span? Class, List<Span> Methods) Discover(string text)
    {
        var t = SourceAdmission.ReadTokens(text);
        Span? cls = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != "RWWindow")
                continue;
            int open = i + 2;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{")
                return (null, []);
            int end = Close(t, open, "{", "}");
            if (end < 0 || cls is not null)
                return (null, []);
            cls = new(t[i].Start, t[open].Start, t[end].End, "RWWindow");
        }
        if (cls is null) return (null, []);
        var methods = new List<Span>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Start <= cls.Open ||
                t[i].End >= cls.End || t[i].Value != "func" ||
                t[i + 2].Value != "(")
                continue;
            int argClose = Close(t, i + 2, "(", ")");
            if (argClose < 0) continue;
            int open = argClose + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Close(t, open, "{", "}");
            if (end < 0 || t[end].End > cls.End) continue;
            methods.Add(new(t[i].Start, t[open].Start, t[end].End,
                t[i + 1].Value));
            i = end;
        }
        return (cls, methods);
    }

    private static bool AppendOnlyAndUnique(string source, Span cls,
        IReadOnlyList<Span> methods, out string? reason)
    {
        reason = null;
        string body = source[cls.Open..cls.End];
        if (!Regex.IsMatch(body,
            @"\bprivate\s+let\s+m_widgets\s*:\s*array\s*<\s*ref\s*<\s*RWWidget\s*>\s*>\s*;"))
        {
            reason = "RW_WIDGET_ARRAY_NOT_PRIVATE_OR_TYPE_CHANGED";
            return false;
        }
        int pushes = Regex.Matches(body,
            @"\bArrayPush\s*\(\s*this\s*\.\s*m_widgets\s*,\s*widget\s*\)").Count;
        if (pushes != 2 ||
            Regex.IsMatch(body,
                @"\b(?:ArrayClear|ArrayErase|ArrayRemove|ArrayResize|ArrayInsert|ArrayPop|ArraySort)\s*\(\s*this\s*\.\s*m_widgets\b") ||
            Regex.IsMatch(body, @"\bthis\s*\.\s*m_widgets\s*(?:\[[^\]]+\]\s*)?(?:=|\+=|-=)"))
        {
            reason = "RW_WIDGET_ARRAY_MUTATION_NOT_APPEND_ONLY";
            return false;
        }
        // Ensure and EnsureSpan own the two verified writes. Their callers
        // must first use Find on the same id, preventing duplicate-id rows.
        var ensure = methods.Where(x => x.Name is "Ensure" or "EnsureSpan").ToArray();
        if (ensure.Length != 2 || ensure.Any(x =>
            !source[x.Open..x.End].Contains("ArrayPush(this.m_widgets, widget)",
                StringComparison.Ordinal)))
        {
            reason = "RW_WIDGET_INSERTION_OWNER_UNPROVEN";
            return false;
        }
        foreach (Span method in methods)
        {
            string methodBody = source[method.Open..method.End];
            if (method.Name == "EnsureSpan") continue;
            if (method.Name == "Ensure")
            {
                if (!Regex.IsMatch(methodBody,
                    @"\bthis\s*\.\s*Find\s*\(\s*id\s*\)"))
                {
                    reason = "RW_ENSURE_MISSING_FIRST_MATCH_LOOKUP";
                    return false;
                }
                continue;
            }
            if (!Regex.IsMatch(methodBody,
                @"\bthis\s*\.\s*EnsureSpan\s*\(")) continue;
            int insertion = methodBody.IndexOf("this.EnsureSpan(",
                StringComparison.Ordinal);
            string before = methodBody[..insertion];
            if (!Regex.IsMatch(before,
                @"\bthis\s*\.\s*Find\s*\(\s*id\s*\)") ||
                !Regex.IsMatch(before,
                @"\b(?:if\s*!?\s*IsDefined\s*\(\s*widget\s*\))"))
            {
                reason = "RW_ENSURE_SPAN_CALLER_DOES_NOT_GUARD_ID";
                return false;
            }
        }
        return true;
    }

    private static (string Status, string? Reason, string? Changed) Rewrite(
        string source)
    {
        var (cls, methods) = Discover(source);
        if (cls is null) return ("REFUSED", "RW_WINDOW_CLASS_UNPROVEN", null);
        if (!AppendOnlyAndUnique(source, cls, methods, out string? refusal))
            return ("REFUSED", refusal, null);
        var find = methods.Where(x => x.Name == "Find").ToArray();
        if (find.Length != 1)
            return ("REFUSED", "RW_FIND_TARGET_MISSING_OR_AMBIGUOUS", null);
        var method = find[0];
        string beforeBody = source[(method.Open + 1)..(method.End - 1)];
        string shape = Shape(beforeBody);
        bool complete = shape == FinalShape;
        bool stock = shape == StockShape;
        bool old5 = shape == Pass5Shape;
        bool old8 = shape == Pass8Shape;
        if (!complete && !stock && !old5 && !old8)
            return ("REFUSED", "RW_FIND_CUSTOM_FLOW_UNPROVEN", null);

        // Only the legacy Pass 5 fields are removed. Preserve unrelated
        // user fields and any already present Pass 8/9 state.
        string classText = source[cls.Open..cls.End];
        string[] required =
        [
            "m_findCursor: Int32",
            "m_lastFindId: CName",
            "m_lastFindWidget: ref<RWWidget>"
        ];
        var edits = new List<Edit>();
        var legacyFields = new[]
        {
            (Name:"m_findCache", Type:"ref<RWWidget>"),
            (Name:"m_findCacheID", Type:"CName")
        };
        if (old5)
        {
            foreach (var field in legacyFields)
            {
                var match = Regex.Match(classText,
                    @"\bprivate\s+let\s+" + field.Name +
                    @"\s*:\s*" + Regex.Escape(field.Type).Replace(
                        @"ref<RWWidget>", @"ref\s*<\s*RWWidget\s*>") + @"\s*;");
                if (!match.Success || Regex.Matches(classText,
                    @"\b" + field.Name + @"\b").Count !=
                    (field.Name == "m_findCache" ? 4 : 3))
                    return ("REFUSED", "RW_PASS5_LEGACY_FIELD_OWNERSHIP_UNPROVEN",
                        null);
                edits.Add(new(cls.Open + match.Index,
                    cls.Open + match.Index + match.Length, ""));
            }
        }
        foreach (string requiredField in required)
        {
            string name = requiredField.Split(':')[0];
            string type = requiredField[(name.Length + 2)..];
            var existing = Regex.Matches(classText,
                @"\b(?:private|protected|public)\s+let\s+" +
                name + @"\s*:\s*([^;]+);");
            if (existing.Count > 1)
                return ("REFUSED", "RW_DUPLICATE_CACHE_FIELD:" + name, null);
            if (existing.Count == 1)
            {
                if (Shape(existing[0].Groups[1].Value) != Shape(type))
                    return ("REFUSED", "RW_CACHE_FIELD_TYPE_CHANGED:" + name,
                        null);
                continue;
            }
            if (Regex.IsMatch(classText, @"\b" + name + @"\b") &&
                !complete && !old8 && !old5 && !stock)
                return ("REFUSED", "RW_CACHE_NAME_COLLISION:" + name, null);
        }
        var anchor = Regex.Match(classText,
            @"\bprivate\s+let\s+m_widgets\s*:\s*array\s*<\s*ref\s*<\s*RWWidget\s*>\s*>\s*;");
        if (!anchor.Success)
            return ("REFUSED", "RW_ARRAY_FIELD_ANCHOR_MISSING", null);
        string newline = source.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        var missing = required.Where(item =>
            !Regex.IsMatch(classText,
                @"\blet\s+" + item.Split(':')[0] + @"\s*:")).ToArray();
        if (missing.Length > 0)
            edits.Add(new(cls.Open + anchor.Index + anchor.Length,
                cls.Open + anchor.Index + anchor.Length,
                string.Concat(missing.Select(x => newline +
                    "  private let " + x + ";"))));
        if (!complete)
            edits.Add(new(method.Open + 1, method.End - 1,
                newline + "    " + Final.Replace("\n", newline + "    ").TrimEnd() +
                newline + "  "));

        if (edits.Count == 0) return ("COMPLETE", null, null);
        string updated = source;
        foreach (var edit in edits.OrderByDescending(x => x.Start))
            updated = updated[..edit.Start] + edit.Replacement +
                updated[edit.End..];
        var (postClass, postMethods) = Discover(updated);
        if (postClass is null ||
            !postMethods.Where(x => x.Name == "Find").Select(x =>
                Shape(updated[(x.Open + 1)..(x.End - 1)])).Contains(FinalShape))
            throw new InvalidDataException("RW_FIND_POSTCONDITION_FAILED");
        return ("APPLIED", null, updated);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string owner = Path.Combine(game, "r6", "scripts", "RedWindows");
        if (!Directory.Exists(Path.Combine(game, "r6", "scripts")) ||
            Directory.Exists(output) || File.Exists(output) ||
            Inside(output, game))
            throw new InvalidDataException("RW_FIND_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("RW_FIND_BASE_OVERLAY_UNSAFE");
        var audit = new SourceAdmission(game);
        _ = audit.AuditAll();
        string sourceHash = audit.SourceStackSha256 ??
            throw new InvalidDataException("RW_FIND_SOURCE_HASH_MISSING");

        string? target = null, effective = "";
        string status = "NOT_APPLICABLE";
        string? reason = null, rewritten = null;
        if (Directory.Exists(owner))
        {
            var candidates = new List<string>();
            foreach (string file in Directory.EnumerateFiles(owner,
                "*.reds", SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(file) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("RW_FIND_LINKED_SOURCE");
                string s = File.ReadAllText(file, Encoding.UTF8);
                if (Regex.IsMatch(s, @"\bclass\s+RWWindow\b"))
                    candidates.Add(file);
            }
            if (candidates.Count != 1)
            {
                status = "REFUSED";
                reason = "RW_FIND_CLASS_MISSING_OR_AMBIGUOUS";
            }
            else
            {
                target = "r6/scripts/" + Path.GetRelativePath(
                    Path.Combine(game, "r6", "scripts"), candidates[0])
                    .Replace('\\', '/');
                effective = File.ReadAllText(candidates[0], Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string previous = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        target.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(previous, basePreviewRoot))
                        throw new InvalidDataException("RW_FIND_BASE_SOURCE_ESCAPE");
                    if (File.Exists(previous))
                        effective = File.ReadAllText(previous, Encoding.UTF8);
                }
                var result = Rewrite(effective);
                status = result.Status;
                reason = result.Reason;
                rewritten = result.Changed;
            }
        }
        if (!string.Equals(sourceHash, audit.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RW_FIND_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (rewritten is not null && target is not null)
        {
            string file = Path.GetFullPath(Path.Combine(
                output, target.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(file, output) ||
                !file.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RW_FIND_STAGED_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, rewritten, new UTF8Encoding(false));
        }
        Result report = new(Rule, status, status == "APPLIED" ? 1 : 0,
            status == "COMPLETE" ? 1 : 0, target, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report,
                new JsonSerializerOptions {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }), new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string child, string parent) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
