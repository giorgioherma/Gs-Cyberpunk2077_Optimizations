using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Approved DigitalVixenCore semantic rule: remember only a proven successful
/// RTTI class lookup. A miss still retries, and no footwear/carrier/session
/// calls, original loop conditions or callbacks are removed or throttled.
///
/// Reads the actual class and method tokens (not a version, line number,
/// historical hash or fully identical method string). Supports an incomplete
/// field/guard/store, including equivalent previously optimized field names.
/// </summary>
internal static class DigitalVixenFootstepsPass
{
    internal const string Rule = "DVCORE_FOOTSTEPS_REFLECTION_SUCCESS_CACHE_V1";
    internal const string Owner = "DigitalVixenCore";
    private const string TargetMethod = "FindAudioClass";
    private const string TargetClass = "DVFootstepsService";
    private const string TargetFunction = "\"EquipNewFootwearByPlayer\"";

    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Edit(int Start, string Insert);
    private sealed record Shape(string Status, string? Reason,
        string? Field, int? FieldInsert, string? FieldIndent,
        int? GuardInsert, string? GuardIndent,
        int? AssignmentInsert, string? AssignmentIndent,
        int ExistingCount)
    {
        internal string? DirectReturnExpression { get; init; }
    }

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t, int index, string value) =>
        index >= 0 && index < t.Count && t[index].Value == value;

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int open, string opening, string closing)
    {
        if (!Is(t, open, opening)) return -1;
        int depth = 0;
        for (int i = open; i < t.Count; i++)
        {
            if (Is(t, i, opening)) depth++;
            else if (Is(t, i, closing) && --depth == 0) return i;
        }
        return -1;
    }

    private static bool Ident(string v) =>
        v.Length != 0 && (char.IsLetter(v[0]) || v[0] == '_') &&
        v.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t,
        int i, params string[] expected) =>
        expected.Select((value, offset) => Is(t, i + offset, value)).All(v => v);

    private static int StartOfLine(string text, int position)
    {
        int n = text.LastIndexOf('\n', Math.Max(0, position - 1));
        return n < 0 ? 0 : n + 1;
    }

    private static string Indent(string text, int position)
    {
        int start = StartOfLine(text, position);
        string prefix = text[start..position];
        if (prefix.Any(c => c is not (' ' or '\t')))
            throw new InvalidDataException("DVCORE_METHOD_INSERTION_NOT_AT_LINE_START");
        return prefix;
    }

    private static string LeadingIndent(string text, int position)
    {
        int start = StartOfLine(text, position);
        int end = start;
        while (end < text.Length && text[end] is ' ' or '\t') end++;
        return text[start..end];
    }

    private static int AfterLine(string text, int position)
    {
        int n = text.IndexOf('\n', position);
        return n < 0 ? text.Length : n + 1;
    }

    private static bool IsTargetLiteral(string text, SourceAdmission.Token token) =>
        token.Value == "<literal>" &&
        string.Equals(text[token.Start..token.End], TargetFunction,
            StringComparison.Ordinal);

    private static Shape Examine(string text)
    {
        var t = SourceAdmission.ReadTokens(text);
        var classes = new List<(int Open, int Close)>();
        for (int i = 0; i < t.Count - 2; i++)
        {
            if (!Seq(t, i, "class", TargetClass)) continue;
            int at = i + 2;
            bool inheritsService = false;
            while (at < t.Count && !Is(t, at, "{") &&
                   !Is(t, at, ";"))
            {
                if (Seq(t, at, "extends", "ScriptableService"))
                    inheritsService = true;
                at++;
            }
            if (!inheritsService || !Is(t, at, "{"))
                return new("REFUSED", "DVCORE_SERVICE_BASE_CHANGED",
                    null, null, null, null, null, null, null, 0);
            int end = Match(t, at, "{", "}");
            if (end < 0) return new("REFUSED", "DVCORE_SERVICE_UNBALANCED",
                null, null, null, null, null, null, null, 0);
            classes.Add((at, end));
            i = end;
        }
        if (classes.Count == 0)
            return new("NOT_APPLICABLE", "DVCORE_SERVICE_NOT_INSTALLED",
                null, null, null, null, null, null, null, 0);
        if (classes.Count != 1)
            return new("REFUSED", "DVCORE_DUPLICATE_SERVICE_DEFINITION",
                null, null, null, null, null, null, null, 0);
        var scope = classes[0];

        var funcs = new List<(int Header, int Open, int Close)>();
        for (int i = scope.Open + 1; i < scope.Close - 2; i++)
        {
            if (!Seq(t, i, "func", TargetMethod, "(")) continue;
            int closeArgs = Match(t, i + 2, "(", ")");
            if (closeArgs < 0) break;
            int openBody = closeArgs + 1;
            while (openBody < scope.Close && !Is(t, openBody, "{") &&
                   !Is(t, openBody, ";")) openBody++;
            if (!Is(t, openBody, "{")) continue;
            int closeBody = Match(t, openBody, "{", "}");
            if (closeBody < 0 || closeBody >= scope.Close) break;
            bool returnClass = false;
            for (int k = closeArgs + 1; k + 3 < openBody; k++)
                if (Seq(t, k, "ref", "<", "ReflectionClass", ">"))
                    returnClass = true;
            if (!returnClass)
                return new("REFUSED", "DVCORE_LOOKUP_RETURN_TYPE_CHANGED",
                    null, null, null, null, null, null, null, 0);
            funcs.Add((i, openBody, closeBody));
            i = closeBody;
        }
        if (funcs.Count != 1)
            return new("REFUSED",
                funcs.Count == 0 ? "DVCORE_LOOKUP_METHOD_MISSING_OR_CHANGED" :
                    "DVCORE_LOOKUP_METHOD_AMBIGUOUS",
                null, null, null, null, null, null, null, 0);
        var f = funcs[0];

        // A single reflection-class snapshot must be the source of the lookup.
        var reflectionGets = new List<int>();
        for (int i = f.Open + 1; i < f.Close; i++)
            if (Seq(t, i, "Reflection", ".", "GetClasses", "(", ")"))
                reflectionGets.Add(i);
        if (reflectionGets.Count != 1)
            return new("REFUSED", "DVCORE_REFLECTION_DISCOVERY_INCOMPATIBLE",
                null, null, null, null, null, null, null, 0);

        int getAt = reflectionGets[0];
        string? classesVar = null;
        int classesLet = -1;
        // Accept standard local declarations, with an optional explicit type.
        for (int i = f.Open + 1; i < getAt; i++)
        {
            if (!Is(t, i, "let") || !Ident(t[i + 1].Value)) continue;
            for (int k = i + 2; k < getAt && k < i + 15; k++)
                if (Is(t, k, "=") && k + 1 == getAt)
                {
                    classesVar = t[i + 1].Value;
                    classesLet = i;
                    break;
                }
        }
        if (classesVar is null || classesLet < 0)
            return new("REFUSED", "DVCORE_REFLECTION_SNAPSHOT_UNPROVEN",
                null, null, null, null, null, null, null, 0);

        // Prove one targeted success arm. The target is an RTTI literal,
        // not a string merely mentioned in comments or unrelated methods.
        var matches = new List<(int If, int Open, int Close, string Index)>();
        for (int i = f.Open + 1; i + 16 < f.Close; i++)
        {
            if (!Seq(t, i, "if", "IsDefined", "(", classesVar, "[") ||
                !Ident(t[i + 5].Value) ||
                !Seq(t, i + 6, "]", ".", "GetFunction", "(", "n") ||
                !IsTargetLiteral(text, t[i + 11]) ||
                !Seq(t, i + 12, ")", ")", "{"))
                continue;
            int close = Match(t, i + 14, "{", "}");
            if (close < 0 || close >= f.Close) continue;
            matches.Add((i, i + 14, close, t[i + 5].Value));
        }
        if (matches.Count != 1)
            return new("REFUSED", "DVCORE_RTTI_SUCCESS_BRANCH_INCOMPATIBLE",
                null, null, null, null, null, null, null, 0);
        var hit = matches[0];

        // The cache field can be named by a newer/custom author. Only re-use
        // a member proven to be part of THIS method's guard/store.
        var classFields = new HashSet<string>(StringComparer.Ordinal);
        int depth = 1;
        for (int i = scope.Open + 1; i < scope.Close; i++)
        {
            if (Is(t, i, "{")) depth++;
            else if (Is(t, i, "}")) depth--;
            if (depth == 1 && Seq(t, i, "let") && i + 7 < t.Count &&
                Ident(t[i + 1].Value) &&
                Seq(t, i + 2, ":", "ref", "<", "ReflectionClass", ">", ";"))
                classFields.Add(t[i + 1].Value);
        }

        var candidates = new HashSet<string>(StringComparer.Ordinal);
        for (int i = f.Open + 1; i + 8 < f.Close; i++)
        {
            // A reference to another ReflectionClass field alone does NOT
            // make that field the successful-result cache. Prove the cache
            // guard's actual return or its write in the matched success arm.
            if (Seq(t, i, "if", "IsDefined", "(", "this", ".") &&
                Ident(t[i + 5].Value) && Is(t, i + 6, ")") &&
                i < classesLet)
            {
                int k = i + 7;
                if (Is(t, k, "{")) k++;
                if (Seq(t, k, "return", "this", ".", t[i + 5].Value, ";"))
                    candidates.Add(t[i + 5].Value);
            }
            if (i > hit.Open && i + 8 < hit.Close &&
                Seq(t, i, "this", ".") && Ident(t[i + 2].Value) &&
                Seq(t, i + 3, "=", classesVar, "[", hit.Index, "]", ";"))
                candidates.Add(t[i + 2].Value);
        }
        if (candidates.Count > 1)
            return new("REFUSED", "DVCORE_MULTIPLE_ACTIVE_REFLECTION_CACHES",
                null, null, null, null, null, null, null, 0);
        // The canonical field is reusable even when it is the only already
        // applied element of a partial optimization.
        string field = candidates.SingleOrDefault() ?? "m_audioClass";

        bool fieldPresent = classFields.Contains(field);
        if (!fieldPresent && t.Skip(scope.Open + 1).Take(scope.Close - scope.Open - 1)
            .Any(token => token.Value == field &&
                (field != "m_audioClass" || token.Start < t[f.Open].Start)))
            return new("REFUSED", "DVCORE_CACHE_FIELD_NAME_COLLISION",
                null, null, null, null, null, null, null, 0);

        // A real guard is an early successful-cache return before the scan,
        // not merely a marker or IsDefined(cache) expression.
        bool guard = false;
        for (int i = f.Open + 1; i + 11 < classesLet; i++)
        {
            if (!Seq(t, i, "if", "IsDefined", "(", "this", ".", field, ")"))
                continue;
            int k = i + 7;
            if (Is(t, k, "{")) k++;
            if (Seq(t, k, "return", "this", ".", field, ";"))
            {
                guard = true;
                break;
            }
        }

        bool directAssignment = false;
        for (int i = hit.Open + 1; i + 8 < hit.Close; i++)
        {
            if (!Seq(t, i, "this", ".", field, "=", classesVar, "[", hit.Index, "]", ";"))
                continue;
            directAssignment = true;
            break;
        }
        // Original success can instead name a hit local, then return it
        // after the loop. In this form we only insert a cache write on a
        // non-null return; all existing search and failure handling survives.
        string? resultVariable = null;
        int resultReturn = -1;
        for (int i = hit.Open + 1; i + 6 < hit.Close; i++)
        {
            if (!Ident(t[i].Value) || !Seq(t, i + 1, "=", classesVar, "[", hit.Index, "]", ";"))
                continue;
            string name = t[i].Value;
            bool declared = false;
            for (int k = f.Open + 1; k + 6 < hit.Open; k++)
                if (Seq(t, k, "let", name, ":", "ref", "<", "ReflectionClass", ">"))
                    declared = true;
            if (!declared) continue;
            for (int k = hit.Close + 1; k + 2 < f.Close; k++)
                if (Seq(t, k, "return", name, ";"))
                {
                    resultVariable = name;
                    resultReturn = k;
                    break;
                }
        }
        // Equivalent newer sources may return the successful class directly
        // instead of storing it in a temporary local. Preserve that return.
        int directReturn = -1;
        for (int i = hit.Open + 1; i + 5 < hit.Close; i++)
            if (Seq(t, i, "return", classesVar, "[", hit.Index, "]", ";"))
            {
                directReturn = i;
                break;
            }

        bool localAssignment = false;
        if (resultVariable is not null && resultReturn >= 0)
        {
            // Recognize a guarded cache write BEFORE the original local
            // result return, including a previously generated partial state.
            for (int i = hit.Close + 1; i + 12 < resultReturn; i++)
            {
                if (!Seq(t, i, "if", "IsDefined", "(", resultVariable,
                        ")", "{", "this", ".", field, "=", resultVariable,
                        ";", "}"))
                    continue;
                localAssignment = true;
                break;
            }
        }
        bool assignment = directAssignment || localAssignment;
        if (!assignment && (resultVariable is null || resultReturn < 0) &&
            directReturn < 0)
            return new("REFUSED", "DVCORE_SUCCESS_RESULT_FLOW_CHANGED",
                null, null, null, null, null, null, null, 0);

        // A cache assignment to the wrong value is not considered complete.
        // Do not add another assignment on top of a questionable write.
        for (int i = f.Open + 1; i + 3 < f.Close; i++)
            if (Seq(t, i, "this", ".", field, "=") && !assignment)
                return new("REFUSED", "DVCORE_UNPROVEN_EXISTING_CACHE_WRITE",
                    null, null, null, null, null, null, null, 0);

        // Returning the cached field from inside the proven success arm is
        // required for the early-return historical shape. The original hit
        // return shape instead remains unchanged.
        if (directAssignment)
        {
            bool successReturnsCache = directReturn >= 0;
            for (int i = hit.Open + 1; i + 4 < hit.Close; i++)
                if (Seq(t, i, "return", "this", ".", field, ";"))
                    successReturnsCache = true;
            if (!successReturnsCache)
                return new("REFUSED", "DVCORE_CACHE_SUCCESS_RETURN_UNPROVEN",
                    null, null, null, null, null, null, null, 0);
        }

        int? fieldInsert = null;
        string? fieldIndent = null;
        if (!fieldPresent)
        {
            // Append after an existing direct member field, avoiding
            // annotations and preserving unrelated method source.
            int lastFieldEnd = -1;
            int lastFieldLine = -1;
            int currentDepth = 1;
            for (int i = scope.Open + 1; i < f.Header; i++)
            {
                if (Is(t, i, "{")) currentDepth++;
                else if (Is(t, i, "}")) currentDepth--;
                if (currentDepth == 1 && Is(t, i, "let") && i + 1 < f.Header)
                {
                    int semi = i + 1;
                    while (semi < f.Header && !Is(t, semi, ";") &&
                           !Is(t, semi, "{")) semi++;
                    if (Is(t, semi, ";"))
                    {
                        lastFieldEnd = t[semi].End;
                        lastFieldLine = t[i].Start;
                        i = semi;
                    }
                }
            }
            if (lastFieldEnd < 0)
                return new("REFUSED", "DVCORE_FIELD_INSERTION_UNPROVEN",
                    null, null, null, null, null, null, null, 0);
            fieldInsert = AfterLine(text, lastFieldEnd);
            fieldIndent = LeadingIndent(text, lastFieldLine);
        }

        // Insert the read guard just before the original reflection snapshot.
        // Preserve any code performed before the snapshot, including custom
        // logging, and all local cache/miss transitions.
        int guardAt = StartOfLine(text, t[classesLet].Start);
        string guardIndent;
        try { guardIndent = Indent(text, t[classesLet].Start); }
        catch (InvalidDataException)
        {
            return new("REFUSED", "DVCORE_SCAN_LINE_NOT_STANDALONE",
                null, null, null, null, null, null, null, 0);
        }
        int? assignAt = null;
        string? assignIndent = null;
        if (!assignment)
        {
            if (resultReturn >= 0)
            {
                // Preserve pre-return effects by writing the cache just
                // before the original local result return.
                assignAt = StartOfLine(text, t[resultReturn].Start);
                try { assignIndent = Indent(text, t[resultReturn].Start); }
                catch (InvalidDataException)
                {
                    return new("REFUSED", "DVCORE_RETURN_LINE_NOT_STANDALONE",
                        null, null, null, null, null, null, null, 0);
                }
            }
            else if (directReturn >= 0)
            {
                // A direct successful return is an equally safe source
                // shape; cache the same expression immediately beforehand.
                assignAt = t[directReturn].Start;
            }
            else return new("REFUSED", "DVCORE_RESULT_RETURN_MISSING",
                null, null, null, null, null, null, null, 0);
        }
        int existing = (fieldPresent ? 1 : 0) + (guard ? 1 : 0) +
            (assignment ? 1 : 0);
        return new(existing == 3 ? "COMPLETE" : "ELIGIBLE", null,
            field, fieldInsert, fieldIndent,
            guard ? null : guardAt, guardIndent,
            assignAt, assignIndent, existing)
        {
            DirectReturnExpression = !assignment && resultReturn < 0 &&
                directReturn >= 0 ? classesVar + "[" + hit.Index + "]" : null
        };
    }

    private static string Apply(string text, Shape shape)
    {
        if (shape.Status != "ELIGIBLE" || shape.Field is null)
            throw new InvalidDataException("DVCORE_NOT_AN_ELIGIBLE_SOURCE");
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var edits = new List<Edit>();
        if (shape.FieldInsert is int fieldAt)
            edits.Add(new(fieldAt, shape.FieldIndent +
                "private let " + shape.Field + ": ref<ReflectionClass>;" + nl));
        if (shape.GuardInsert is int guardAt)
            edits.Add(new(guardAt, shape.GuardIndent +
                "if IsDefined(this." + shape.Field + ") {" + nl +
                shape.GuardIndent + "  return this." + shape.Field + ";" + nl +
                shape.GuardIndent + "}" + nl + nl));
        if (shape.AssignmentInsert is int at)
        {
            if (shape.DirectReturnExpression is string direct)
                edits.Add(new(at, "this." + shape.Field + " = " + direct + "; "));
            else
            {
            // Preserve the original return value and retry-on-lookup-miss.
            var t = SourceAdmission.ReadTokens(text);
            int resultAt = t.ToList().FindIndex(token => token.Start >= at &&
                token.Value == "return");
            if (resultAt < 0 || !Is(t, resultAt + 1, "this") &&
                !Ident(t[resultAt + 1].Value))
                throw new InvalidDataException("DVCORE_RETURN_VARIABLE_DRIFT");
            string result = t[resultAt + 1].Value;
            edits.Add(new(at, shape.AssignmentIndent +
                "if IsDefined(" + result + ") {" + nl +
                shape.AssignmentIndent + "  this." + shape.Field + " = " +
                result + ";" + nl + shape.AssignmentIndent + "}" + nl));
            }
        }
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            text = text[..edit.Start] + edit.Insert + text[edit.Start..];
        var post = Examine(text);
        if (post.Status != "COMPLETE" || post.ExistingCount != 3)
            throw new InvalidDataException("DVCORE_GENERATED_POSTCONDITION_FAILED:" +
                post.Reason);
        return text;
    }

    internal static Result Generate(string gameRoot,
        string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase) ||
            Inside(output, game) || Directory.Exists(output) ||
            File.Exists(output))
            throw new InvalidDataException("DVCORE_STAGE_UNSAFE_OUTPUT");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) || Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("DVCORE_BASE_PREVIEW_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string before = admission.SourceStackSha256 ??
            throw new InvalidDataException("DVCORE_SOURCE_HASH_UNAVAILABLE");

        var owners = new List<(string Relative, string Source)>();
        foreach (var full in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("DVCORE_LINKED_SOURCE_REFUSED");
            string contents = File.ReadAllText(full, Encoding.UTF8);
            if (!contents.Contains(TargetClass, StringComparison.Ordinal) ||
                !contents.Contains(Owner, StringComparison.Ordinal))
                continue;
            // Whitespace/comments/layout/version variations must not become
            // a false NOT_APPLICABLE. Admit by tokens, not source text bytes.
            var tokens = SourceAdmission.ReadTokens(contents);
            bool ownerModule = false;
            bool serviceDeclaration = false;
            for (int i = 0; i + 1 < tokens.Count; i++)
            {
                if (Seq(tokens, i, "module", Owner)) ownerModule = true;
                if (Seq(tokens, i, "class", TargetClass))
                    serviceDeclaration = true;
            }
            if (!ownerModule || !serviceDeclaration) continue;
            owners.Add((Path.GetRelativePath(scripts, full), contents));
        }
        Result result;
        string? transformed = null;
        string? targetPath = null;
        if (owners.Count == 0)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "DVCORE_SERVICE_NOT_INSTALLED", true, false);
        else if (owners.Count > 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                "DVCORE_MULTIPLE_SERVICE_SOURCE_OWNERS", true, false);
        else
        {
            var owner = owners[0];
            targetPath = owner.Relative;
            string effective = owner.Source;
            if (basePreviewRoot is not null)
            {
                string staged = Path.GetFullPath(Path.Combine(basePreviewRoot,
                    "r6", "scripts", owner.Relative));
                if (!Inside(staged, basePreviewRoot))
                    throw new InvalidDataException("DVCORE_EFFECTIVE_PATH_ESCAPE");
                if (File.Exists(staged)) effective = File.ReadAllText(staged);
            }
            var status = Examine(effective);
            if (status.Status == "ELIGIBLE")
            {
                transformed = Apply(effective, status);
                result = new(Rule, "APPLIED", 1, status.ExistingCount,
                    "r6/scripts/" + owner.Relative.Replace('\\', '/'),
                    null, true, false);
            }
            else result = new(Rule, status.Status, 0,
                status.Status == "COMPLETE" ? 1 : 0,
                "r6/scripts/" + owner.Relative.Replace('\\', '/'),
                status.Reason, true, false);
        }

        if (!string.Equals(before, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DVCORE_SOURCE_CHANGED_DURING_GENERATE");

        Directory.CreateDirectory(output);
        if (transformed is not null && targetPath is not null)
        {
            string destination = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", targetPath));
            if (!Inside(destination, output))
                throw new InvalidDataException("DVCORE_STAGE_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, transformed, new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions {
                WriteIndented = true, PropertyNamingPolicy =
                    JsonNamingPolicy.CamelCase
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string candidate, string root)
    {
        candidate = Path.GetFullPath(candidate);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return candidate.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
