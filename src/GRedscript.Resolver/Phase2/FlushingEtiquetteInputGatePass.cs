using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Input-only FE semantic optimization. Every unrelated action originally
/// falls through FEInput.Handle to PlayerPuppet.wrappedMethod. Reject it after
/// GetName but before GetType/GetGame/system resolution, preserving all
/// registered FE action and modifier paths. No new listeners, native state,
/// location cache, scheduler, or gameplay timing changes.
///
/// Admission is based on the current source control-flow and declared CName
/// actions, not a mod version, filename, exact historical text or hash.
/// </summary>
internal static class FlushingEtiquetteInputGatePass
{
    internal const string Rule = "FLUSHING_ETIQUETTE_UNRELATED_INPUT_GATE_V1";
    private const string Module = "FlushingEtiquette";
    private const string Service = "FEInput";

    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);
    private sealed record Span(int Start, int Open, int Close);
    private sealed record Decision(string Status, string? Reason,
        int Start, int End, string? Replacement);

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t,
        int i, string v) => i >= 0 && i < t.Count && t[i].Value == v;
    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t, int i,
        params string[] values) =>
        values.Select((v, j) => Is(t, i + j, v)).All(x => x);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int open, string a, string b)
    {
        if (!Is(t, open, a)) return -1;
        int depth = 0;
        for (int i = open; i < t.Count; i++)
        {
            if (Is(t, i, a)) depth++;
            else if (Is(t, i, b) && --depth == 0) return i;
        }
        return -1;
    }

    private static Span? Method(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, string name)
    {
        Span? found = null;
        for (int i = first; i < last - 3; i++)
        {
            if (!Seq(t, i, "func", name, "(")) continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < last && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            if (!Is(t, open, "{")) return null;
            int end = Close(t, open, "{", "}");
            if (end < 0 || end > last || found is not null) return null;
            found = new(i, open, end);
            i = end;
        }
        return found;
    }

    private static bool IsNameLiteral(string source,
        IReadOnlyList<SourceAdmission.Token> t, int n)
    {
        return Seq(t, n, "n", "<literal>") &&
            t[n + 1].End > t[n + 1].Start + 2 &&
            source[t[n + 1].Start] == '"' &&
            source[t[n + 1].End - 1] == '"';
    }

    private static string Value(string source,
        SourceAdmission.Token token) =>
        source[(token.Start + 1)..(token.End - 1)];

    private static string? NameEquals(string source,
        IReadOnlyList<SourceAdmission.Token> t, int i, string name)
    {
        // Source-local Equals can compare either operand order.
        if (Seq(t, i, "Equals", "(", name, ",") &&
            IsNameLiteral(source, t, i + 4) && Is(t, i + 6, ")"))
            return Value(source, t[i + 5]);
        if (Seq(t, i, "Equals", "(") &&
            IsNameLiteral(source, t, i + 2) &&
            Seq(t, i + 4, ",", name, ")"))
            return Value(source, t[i + 3]);
        return null;
    }

    private static Decision Inspect(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var classes = new List<(int Open, int Close)>();
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (!Seq(t, i, "class", Service)) continue;
            int open = i + 2;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int close = Close(t, open, "{", "}");
            if (close < 0) return new("REFUSED",
                "FE_INPUT_CLASS_UNBALANCED", 0, 0, null);
            classes.Add((open, close));
            i = close;
        }
        if (classes.Count != 1) return new("REFUSED",
            classes.Count == 0 ? "FE_INPUT_CLASS_MISSING" :
                "FE_INPUT_CLASS_AMBIGUOUS", 0, 0, null);
        var cls = classes[0];
        var handle = Method(t, cls.Open + 1, cls.Close, "Handle");
        var modifier = Method(t, cls.Open + 1, cls.Close,
            "IndexOfModifier");
        if (handle is null || modifier is null)
            return new("REFUSED", "FE_INPUT_REQUIRED_METHOD_MISSING",
                0, 0, null);
        Span h = handle, m = modifier;

        // Prove the helper only matches CName literals and falls through
        // with -1. Unknown helper work must not be bypassed by an early gate.
        var modNames = new HashSet<string>(StringComparer.Ordinal);
        int modifierTests = 0;
        for (int i = m.Open + 1; i < m.Close; i++)
        {
            string? matched = NameEquals(source, t, i, "name");
            if (matched is null) continue;
            if (!matched.StartsWith("FEM_", StringComparison.Ordinal))
                return new("REFUSED", "FE_MODIFIER_NAME_UNPROVEN",
                    0, 0, null);
            modNames.Add(matched);
            modifierTests++;
        }
        bool sentinel = false;
        for (int i = m.Open + 1; i + 3 < m.Close; i++)
            if (Seq(t, i, "return", "-", "1", ";"))
                sentinel = true;
        if (!sentinel || modNames.Count == 0 ||
            modifierTests != modNames.Count)
            return new("REFUSED", "FE_MODIFIER_NEGATIVE_RESULT_UNPROVEN",
                0, 0, null);

        // Require a single local GetName before GetType and GetGame.
        string? nameVar = null;
        int getNameEnd = -1, getType = -1, getGame = -1;
        int getNameCalls = 0;
        for (int i = h.Open + 1; i < h.Close - 7; i++)
        {
            if (Seq(t, i, "let") && Is(t, i + 2, "=") &&
                Seq(t, i + 3, "ListenerAction", ".", "GetName", "(", "action", ")") &&
                Is(t, i + 9, ";"))
            {
                nameVar = t[i + 1].Value;
                getNameEnd = i + 9;
                getNameCalls++;
            }
            if (Seq(t, i, "ListenerAction", ".", "GetType", "(",
                "action", ")"))
                getType = i;
            if (Seq(t, i, "player", ".", "GetGame", "(", ")"))
                getGame = i;
        }
        if (getNameCalls != 1 || nameVar is null || getType <= getNameEnd ||
            getGame <= getType)
            return new("REFUSED", "FE_ACTION_ORDER_OR_RECEIVER_UNPROVEN",
                0, 0, null);

        // No prior callback effect is allowed. This gate retains GetName
        // exactly where the author placed it, before all other lookups.
        if (getNameEnd - h.Open != 10)
            return new("REFUSED", "FE_PRE_NAME_EFFECTS_UNPROVEN",
                0, 0, null);

        var directNames = new HashSet<string>(StringComparer.Ordinal);
        bool modifierCall = false;
        for (int i = getType; i < h.Close; i++)
        {
            string? matched = NameEquals(source, t, i, nameVar);
            if (matched is not null) directNames.Add(matched);
            if (Seq(t, i, "FEInput", ".", "IndexOfModifier",
                "(", nameVar, ")"))
                modifierCall = true;
            // A custom use of name beyond proven Equals() or the pure
            // modifier lookup may handle an unrelated action: narrow refusal.
            if (Is(t, i, nameVar) &&
                !(i >= 3 && Is(t, i - 3, "Equals")) &&
                !(i >= 4 && Is(t, i - 4, "Equals")) &&
                !(i >= 4 && Seq(t, i - 4, "FEInput", ".",
                  "IndexOfModifier", "(")))
                return new("REFUSED", "FE_CUSTOM_ACTION_CONSUMER_UNPROVEN",
                    0, 0, null);
        }
        if (!modifierCall || directNames.Count < 4 ||
            !directNames.Contains("ShowStatusEffects") ||
            !directNames.Contains("Flush_Relief") ||
            !directNames.Contains("Flush_VehicleRelief") ||
            !directNames.Contains("Flush_WashHands"))
            return new("REFUSED", "FE_OWNED_ACTION_BRANCHES_UNPROVEN",
                0, 0, null);

        // All names are derived from the currently installed owner. Extra
        // compatible FE action branches are included automatically.
        var allowed = directNames.Concat(modNames).Distinct(
            StringComparer.Ordinal).OrderBy(x => x,
                StringComparer.Ordinal).ToArray();
        if (allowed.Any(x => x.Length == 0 || x.Contains('"') ||
            x.Contains('\n') || x.Contains('\r')))
            return new("REFUSED", "FE_SOURCE_ACTION_LITERAL_UNSAFE",
                0, 0, null);

        string indent = "    ";
        int line = source.LastIndexOf('\n',
            Math.Max(0, t[getNameEnd].End - 1)) + 1;
        string originalIndent = source[line..t[getNameEnd - 9].Start];
        if (originalIndent.All(c => c is ' ' or '\t'))
            indent = originalIndent;
        string nl = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string guard = indent+"if "+
            string.Join(" && ", allowed.Select(a =>
                "!Equals("+nameVar+", n\""+a+"\")"))+
            " { return false; }";

        int start = t[getNameEnd].End;
        int next = getNameEnd + 1;
        if (next >= getType) return new("REFUSED",
            "FE_INPUT_EARLY_GUARD_AMBIGUOUS", 0, 0, null);
        int beforeType = t[getType].Start;
        var inBetween = t.Skip(next).Take(getType - next).ToArray();
        if (inBetween.Length == 0)
            return new("ELIGIBLE", null, start, start, nl+guard);

        // Recognize markerless partial guards and complete equivalent
        // implementations without interpreting arbitrary custom predicates.
        int ifPos = next;
        if (!Is(t, ifPos, "if"))
            return new("REFUSED", "FE_PRE_TYPE_CUSTOM_CONTROL_FLOW",
                0, 0, null);
        int brace = ifPos + 1;
        while (brace < getType && !Is(t, brace, "{")) brace++;
        if (brace >= getType || !Seq(t, brace, "{", "return",
            "false", ";", "}"))
            return new("REFUSED", "FE_EARLY_GUARD_BODY_UNPROVEN",
                0, 0, null);
        int endIndex = brace + 4;
        if (endIndex + 1 != getType)
            return new("REFUSED", "FE_EXTRA_PRE_TYPE_EFFECTS_UNPROVEN",
                0, 0, null);
        var existing = new HashSet<string>(StringComparer.Ordinal);
        for (int i = ifPos + 1; i < brace;)
        {
            if (!Is(t, i, "!"))
                return new("REFUSED", "FE_UNKNOWN_GATE_PREDICATE",
                    0, 0, null);
            string? member = NameEquals(source, t, i + 1, nameVar);
            if (member is null || !allowed.Contains(member))
                return new("REFUSED", "FE_UNKNOWN_GATE_ACTION",
                    0, 0, null);
            existing.Add(member);
            i += 8; // ! Equals ( name , n <literal> )
            if (i < brace)
            {
                if (!Seq(t, i, "&&"))
                    return new("REFUSED", "FE_UNKNOWN_GATE_OPERATOR",
                        0, 0, null);
                i++;
            }
        }
        if (existing.SetEquals(allowed))
            return new("COMPLETE", null, start, start, null);
        // A partial guard excludes a proper subset and still falls
        // through safely after exactly the author's negative return.
        int guardStart = t[ifPos].Start;
        int guardEnd = t[endIndex].End;
        return new("ELIGIBLE", null, guardStart, guardEnd, guard);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_INPUT_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) ||
            Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("FE_INPUT_BASE_PREVIEW_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("FE_INPUT_SOURCE_HASH_MISSING");
        var owners = new List<(string Path, string Content)>();
        bool installed = false;
        bool fallback = false;
        foreach (string full in Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("FE_INPUT_SOURCE_LINK_REFUSED");
            string source = File.ReadAllText(full, Encoding.UTF8);
            if (!source.Contains(Module, StringComparison.Ordinal)) continue;
            var t = SourceAdmission.ReadTokens(source);
            bool own = false;
            for (int i = 0; i < t.Count - 1; i++)
                if (Seq(t, i, "module", Module)) own = true;
            if (!own) continue;
            installed = true;
            if (source.Contains("wrappedMethod", StringComparison.Ordinal) &&
                source.Contains("FEInput", StringComparison.Ordinal) &&
                source.Contains("OnAction", StringComparison.Ordinal))
                fallback = true;
            if (!source.Contains("IndexOfModifier", StringComparison.Ordinal) ||
                !source.Contains("GetName", StringComparison.Ordinal)) continue;
            bool classOwner = false;
            for (int i = 0; i < t.Count - 1; i++)
                if (Seq(t, i, "class", Service)) classOwner = true;
            if (classOwner) owners.Add((Path.GetRelativePath(scripts,full),
                source));
        }
        string? rewritten = null;
        string? rel = null;
        Result result;
        if (!installed)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "FE_INPUT_OWNER_NOT_INSTALLED", true, false);
        else if (!fallback || owners.Count != 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                fallback ? "FE_INPUT_TARGET_AMBIGUOUS" :
                    "FE_INPUT_WRAPPED_FALLBACK_UNPROVEN", true, false);
        else
        {
            (rel, string effective) = owners[0];
            if (basePreviewRoot is not null)
            {
                string existing = Path.GetFullPath(Path.Combine(
                    basePreviewRoot,"r6","scripts",rel));
                if (!Inside(existing,basePreviewRoot))
                    throw new InvalidDataException("FE_INPUT_OVERLAY_PATH_ESCAPE");
                if (File.Exists(existing)) effective =
                    File.ReadAllText(existing, Encoding.UTF8);
            }
            var state = Inspect(effective);
            if (state.Status == "ELIGIBLE")
            {
                rewritten = effective[..state.Start]+state.Replacement+
                    effective[state.End..];
                var post = Inspect(rewritten);
                if (post.Status != "COMPLETE")
                    throw new InvalidDataException(
                        "FE_INPUT_POSTCONDITION_FAILED:"+post.Reason);
                result = new(Rule, "APPLIED", 1, 0,
                    "r6/scripts/"+rel.Replace('\\','/'), null,true,false);
            }
            else result = new(Rule,state.Status,0,
                state.Status == "COMPLETE" ? 1 : 0,
                "r6/scripts/"+rel.Replace('\\','/'),
                state.Reason,true,false);
        }
        if (!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_INPUT_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        if (rewritten is not null && rel is not null)
        {
            string path=Path.GetFullPath(Path.Combine(output,
                "r6","scripts",rel));
            if (!Inside(path,output))
                throw new InvalidDataException("FE_INPUT_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,rewritten,new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string path,string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
