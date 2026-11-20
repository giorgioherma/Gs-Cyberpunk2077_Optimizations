using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Dark Future: cache the immutable infinite-duration gameplay-tag membership
/// at each cooldown activation. Does NOT throttle Update, suppress animation,
/// skip finite timer renders, change widget output or change outro cadence.
/// Source-only, one-pass, partial-state-aware. Original cache miss fallback
/// remains intact until ActivateCooldown has finished initializing tags.
/// </summary>
internal static class DarkFutureInfiniteTagCachePass
{
    internal const string Rule = "DARK_FUTURE_INFINITE_TAG_CACHE_V1";
    private const string Tag = "\"DarkFutureInfiniteDurationEffect\"";
    private const string ValidField = "m_gcoDfInfiniteTagValid";
    private const string CachedField = "m_gcoDfInfiniteTagCached";

    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Edit(int Start, int End, string Text);
    private sealed record Analysis(string State, string? Reason,
        List<Edit> Missing, int Existing);

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t, int i,
        string value) => i >= 0 && i < t.Count && t[i].Value == value;

    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t, int i,
        params string[] values) =>
        values.Select((v, n) => Is(t, i + n, v)).All(b => b);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t, int i,
        string open, string close)
    {
        if (!Is(t, i, open)) return -1;
        int depth = 0;
        for (int k = i; k < t.Count; k++)
        {
            if (Is(t, k, open)) depth++;
            else if (Is(t, k, close) && --depth == 0) return k;
        }
        return -1;
    }

    private static string LineIndent(string text, int offset)
    {
        int start = text.LastIndexOf('\n', Math.Max(0, offset - 1)) + 1;
        string prefix = text[start..offset];
        if (prefix.Any(c => c is not (' ' or '\t')))
            throw new InvalidDataException("DARK_FUTURE_SOURCE_INSERTION_LINE_UNPROVEN");
        return prefix;
    }

    private static bool Literal(string text, SourceAdmission.Token token) =>
        token.Value == "<literal>" &&
        string.Equals(text[token.Start..token.End], Tag, StringComparison.Ordinal);

    private static int LocateMethod(IReadOnlyList<SourceAdmission.Token> t,
        string annotation, string method)
    {
        var hits = new List<int>();
        for (int i = 0; i + 7 < t.Count; i++)
        {
            if (!Seq(t, i, "@", annotation, "(", "SingleCooldownManager", ")"))
                continue;
            for (int k = i + 5; k < t.Count && k < i + 20; k++)
            {
                if (Seq(t, k, "func", method, "("))
                {
                    int argsEnd = Close(t, k + 2, "(", ")");
                    if (argsEnd < 0) break;
                    int body = argsEnd + 1;
                    while (body < t.Count && body < argsEnd + 18 &&
                           !Is(t, body, "{") && !Is(t, body, ";")) body++;
                    if (Is(t, body, "{"))
                    {
                        hits.Add(body);
                        break;
                    }
                }
                if (Is(t, k, "@") && k > i + 5) break;
            }
        }
        return hits.Count == 1 ? hits[0] : -1;
    }

    private static string CacheCondition(string valid, string cached,
        string sourceCall) =>
        "((this." + valid + " && !this." + cached + ")" +
        " || (!this." + valid + " && !" + sourceCall + "))";

    private static Analysis Inspect(string source,
        IReadOnlyList<SourceAdmission.Token> t)
    {
        static Analysis Refuse(string reason) =>
            new("REFUSED", reason, [], 0);

        // We recognize the owning @addField and both owned methods in actual
        // source; the full file name, installed version and line layout vary.
        var tagFields = new List<int>();
        var boolFields = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i + 10 < t.Count; i++)
        {
            if (!Seq(t, i, "@", "addField", "(", "SingleCooldownManager", ")"))
                continue;
            int j = i + 5;
            while (j < t.Count && j < i + 12 && !Is(t, j, "let")) j++;
            if (!Is(t, j, "let")) continue;
            if (Seq(t, j + 1, "m_gameplayTags", ":", "array", "<", "CName", ">", ";"))
                tagFields.Add(j + 7);
            else if (j + 4 < t.Count &&
                Seq(t, j + 2, ":", "Bool", ";"))
                boolFields[t[j + 1].Value] = j + 4;
        }
        if (tagFields.Count != 1)
            return Refuse("DARK_FUTURE_TAG_FIELD_UNPROVEN");
        int activate = LocateMethod(t, "wrapMethod", "ActivateCooldown");
        int update = LocateMethod(t, "replaceMethod", "Update");
        if (activate < 0 || update < 0)
            return Refuse("DARK_FUTURE_COOLDOWN_HOOKS_MISSING_OR_AMBIGUOUS");
        int actEnd = Close(t, activate, "{", "}");
        int updEnd = Close(t, update, "{", "}");
        if (actEnd < 0 || updEnd < 0)
            return Refuse("DARK_FUTURE_COOLDOWN_BODY_UNBALANCED");

        // The cache must be invalid before any call to wrappedMethod(), since
        // the wrapped activation may synchronously invoke Update on a reused
        // widget. The original UI always executes even on cache hits.
        var wrapped = new List<int>();
        var writes = new List<(int Start, int Semi)>();
        for (int i = activate + 1; i < actEnd; i++)
        {
            if (Is(t, i, "wrappedMethod") && Is(t, i + 1, "("))
                wrapped.Add(i);
            if (!Seq(t, i, "this", ".", "m_gameplayTags", "=")) continue;
            int semi = i + 4;
            while (semi < actEnd && !Is(t, semi, ";")) semi++;
            if (Is(t, semi, ";") && 
                (i + 4 < semi && Seq(t, i + 4, "TweakDBInterface", ".",
                    "GetStatusEffectRecord", "(")) &&
                t.Skip(i + 4).Take(semi - i - 4)
                    .Any(x => x.Value == "GameplayTags"))
                writes.Add((i, semi));
        }
        if (wrapped.Count != 1 || writes.Count != 1 ||
            wrapped[0] > writes[0].Start)
            return Refuse("DARK_FUTURE_ACTIVATION_TAG_FLOW_UNPROVEN");

        // The original condition must be the precise negated membership
        // operation, or our exact equivalent cache/fallback expression.
        var tagCalls = new List<(int Start, int Close)>();
        for (int i = update + 1; i < updEnd; i++)
        {
            if (!Seq(t, i, "ArrayContains", "(", "this", ".", "m_gameplayTags",
                    ",", "n") || i + 7 >= updEnd || !Literal(source, t[i + 7]) ||
                !Is(t, i + 8, ")"))
                continue;
            tagCalls.Add((i, i + 8));
        }
        if (tagCalls.Count != 1)
            return Refuse("DARK_FUTURE_INFINITE_TAG_TEST_MISSING_OR_AMBIGUOUS");
        var call = tagCalls[0];
        if (!Is(t, call.Start - 1, "!"))
            return Refuse("DARK_FUTURE_INFINITE_TAG_NEGATION_CHANGED");

        // A second write to the authoritative tag list (outside the original
        // activation assignment) would invalidate the cache.
        for (int i = 0; i + 3 < t.Count; i++)
            if (Seq(t, i, "this", ".", "m_gameplayTags", "=") &&
                i != writes[0].Start)
                return Refuse("DARK_FUTURE_TAGS_MUTATED_AFTER_ACTIVATION");

        var missing = new List<Edit>();
        bool validField = boolFields.ContainsKey(ValidField);
        bool cachedField = boolFields.ContainsKey(CachedField);
        var nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!validField || !cachedField)
        {
            // A literal collision with a differently-typed member is unsafe.
            for (int i = 0; i + 1 < t.Count; i++)
                if ((Is(t, i, ValidField) && !validField) ||
                    (Is(t, i, CachedField) && !cachedField))
                    return Refuse("DARK_FUTURE_CUSTOM_CACHE_FIELD_COLLISION");
            string fields = "";
            if (!validField)
                fields += nl + "@addField(SingleCooldownManager)" + nl +
                    "private let " + ValidField + ": Bool;" + nl;
            if (!cachedField)
                fields += nl + "@addField(SingleCooldownManager)" + nl +
                    "private let " + CachedField + ": Bool;" + nl;
            missing.Add(new(t[tagFields[0]].End, t[tagFields[0]].End, fields));
        }

        bool invalidated = false, cached = false, validated = false;
        for (int i = activate + 1; i + 5 < actEnd; i++)
        {
            if (Seq(t, i, "this", ".", ValidField, "=", "false", ";"))
            {
                if (i < wrapped[0]) invalidated = true;
                else return Refuse("DARK_FUTURE_LATE_CACHE_INVALIDATION");
            }
            if (Seq(t, i, "this", ".", CachedField, "=", "ArrayContains", "(",
                    "this", ".", "m_gameplayTags", ",", "n") &&
                i + 11 < actEnd && Literal(source, t[i + 11]) &&
                Seq(t, i + 12, ")", ";"))
            {
                if (i > writes[0].Semi) cached = true;
                else return Refuse("DARK_FUTURE_CACHE_BEFORE_TAG_LOAD");
            }
            if (Seq(t, i, "this", ".", ValidField, "=", "true", ";"))
            {
                if (i > writes[0].Semi) validated = true;
                else return Refuse("DARK_FUTURE_CACHE_VALID_BEFORE_TAG_LOAD");
            }
        }

        // Protect against a partially applied custom rewrite that already
        // writes either cache member with a different, unproven value.
        for (int i = activate + 1; i + 3 < actEnd; i++)
        {
            if (!Seq(t, i, "this", ".")) continue;
            if (Is(t, i + 2, ValidField) && Is(t, i + 3, "=") &&
                !Seq(t, i + 4, "false", ";") &&
                !Seq(t, i + 4, "true", ";"))
                return Refuse("DARK_FUTURE_UNKNOWN_CACHE_VALIDITY_WRITE");
            if (Is(t, i + 2, CachedField) && Is(t, i + 3, "=") &&
                !Seq(t, i + 4, "ArrayContains", "("))
                return Refuse("DARK_FUTURE_UNKNOWN_CACHE_MEMBERSHIP_WRITE");
        }

        int conditionStart = call.Start - 1;
        int conditionEnd = t[call.Close].End;
        string originalCall = source[t[call.Start].Start..t[call.Close].End];
        string replacement = CacheCondition(ValidField, CachedField, originalCall);
        var expected = SourceAdmission.ReadTokens(replacement)
            .Select(x => x.Value).ToArray();
        int expectedStart = call.Start - 1 - (expected.Length - 9);
        // Search the same method for the exact semantic guard, ignoring
        // comments and whitespace. Never trust an optimization marker alone.
        bool conditionCached = false;
        for (int i = update + 1; i + expected.Length <= updEnd; i++)
        {
            if (expected.Select((value, offset) => Is(t, i + offset, value))
                .All(b => b))
            {
                conditionCached = true;
                break;
            }
        }
        if (conditionCached)
        {
            // No rewrite of the already-cached comparison.
        }
        else
        {
            // Refuse a different partially optimized expression around the
            // same call; never nest this fallback twice.
            if (call.Start - 2 >= update &&
                !Is(t, call.Start - 2, "&") &&
                !Is(t, call.Start - 2, "|") &&
                !Is(t, call.Start - 2, "("))
                return Refuse("DARK_FUTURE_MEMBERSHIP_CONDITION_DRIFT");
            missing.Add(new(t[conditionStart].Start,
                conditionEnd, replacement));
        }

        string indent;
        try { indent = LineIndent(source, t[wrapped[0]].Start); }
        catch (InvalidDataException) {
            return Refuse("DARK_FUTURE_ACTIVATION_LINE_UNPROVEN");
        }
        if (!invalidated)
            missing.Add(new(t[activate].End, t[activate].End,
                nl + indent + "this." + ValidField + " = false;"));

        string assignIndent;
        try { assignIndent = LineIndent(source, t[writes[0].Start].Start); }
        catch (InvalidDataException) {
            return Refuse("DARK_FUTURE_TAG_ASSIGNMENT_LINE_UNPROVEN");
        }
        string tail = "";
        if (!cached)
            tail += nl + assignIndent + "this." + CachedField +
                " = " + originalCall + ";";
        if (!validated)
            tail += nl + assignIndent + "this." + ValidField + " = true;";
        if (tail.Length > 0)
            missing.Add(new(t[writes[0].Semi].End,
                t[writes[0].Semi].End, tail));

        int existing = 2 - (validField ? 0 : 1) - (cachedField ? 0 : 1) +
            (invalidated ? 1 : 0) + (cached ? 1 : 0) +
            (validated ? 1 : 0) + (conditionCached ? 1 : 0);
        return new(missing.Count == 0 ? "COMPLETE" : "ELIGIBLE",
            null, missing, existing);
    }

    private static string Transform(string source,
        IReadOnlyList<SourceAdmission.Token> t)
    {
        var admission = Inspect(source, t);
        if (admission.State != "ELIGIBLE")
            throw new InvalidDataException("DARK_FUTURE_NOT_APPLICABLE_FOR_TRANSFORM");
        foreach (var edit in admission.Missing
            .OrderByDescending(x => x.Start).ThenByDescending(x => x.End))
            source = source[..edit.Start] + edit.Text + source[edit.End..];
        var post = Inspect(source, SourceAdmission.ReadTokens(source));
        if (post.State != "COMPLETE" || post.Missing.Count != 0)
            throw new InvalidDataException("DARK_FUTURE_GENERATED_POSTCONDITION_FAILED:" +
                post.Reason);
        return source;
    }

    internal static Result Generate(string gameRoot,
        string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            output = Path.GetFullPath(outputRoot),
            scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DARK_FUTURE_OUTPUT_NOT_ISOLATED");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("DARK_FUTURE_BASE_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("DARK_FUTURE_SOURCE_HASH_UNAVAILABLE");
        var candidates = new List<(string Rel, string Text)>();
        // Owner selection follows installed source, not the historical
        // filename/version. Other mods with conditional class replacements
        // are never turned into Dark Future owners.
        string owner = Path.Combine(scripts, "Dark Future");
        if (Directory.Exists(owner))
            foreach (string path in Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("DARK_FUTURE_LINKED_SOURCE_REFUSED");
                string text = File.ReadAllText(path, Encoding.UTF8);
                if (!text.Contains("DarkFutureInfiniteDurationEffect",
                        StringComparison.Ordinal) ||
                    !text.Contains("SingleCooldownManager", StringComparison.Ordinal))
                    continue;
                candidates.Add((Path.GetRelativePath(scripts, path), text));
            }

        Result result;
        string? effective = null, transformed = null, relative = null;
        if (candidates.Count == 0)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "DARK_FUTURE_TARGET_NOT_INSTALLED", true, false);
        else if (candidates.Count > 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                "DARK_FUTURE_MULTIPLE_TARGET_FILES", true, false);
        else
        {
            (relative, effective) = candidates[0];
            if (basePreviewRoot is not null)
            {
                string overlay = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, "r6", "scripts", relative));
                if (!Inside(overlay, basePreviewRoot))
                    throw new InvalidDataException("DARK_FUTURE_BASE_OVERLAY_PATH_ESCAPE");
                if (File.Exists(overlay))
                    effective = File.ReadAllText(overlay, Encoding.UTF8);
            }
            var analysis = Inspect(effective, SourceAdmission.ReadTokens(effective));
            if (analysis.State == "ELIGIBLE")
            {
                // Only a dedicated immutable tag membership is hoisted. The
                // full original cooldown UI and all animation calls remain.
                transformed = Transform(effective,
                    SourceAdmission.ReadTokens(effective));
                result = new(Rule, "APPLIED", 1, analysis.Existing,
                    "r6/scripts/" + relative.Replace('\\', '/'),
                    null, true, false);
            }
            else result = new(Rule, analysis.State, 0,
                analysis.State == "COMPLETE" ? 1 : 0,
                "r6/scripts/" + relative.Replace('\\', '/'),
                analysis.Reason, true, false);
        }

        // Prevent an external mod from updating the private tag array outside
        // the proven activation route. Be strict about source lifetime.
        if (transformed is not null)
            foreach (var file in Directory.EnumerateFiles(scripts, "*.reds",
                SearchOption.AllDirectories))
            {
                if (Path.GetRelativePath(scripts, file) == relative)
                    continue;
                if (File.ReadAllText(file, Encoding.UTF8).Contains(
                    "m_gameplayTags", StringComparison.Ordinal))
                {
                    transformed = null;
                    result = new(Rule, "REFUSED", 0, 0,
                        result.SourcePath,
                        "DARK_FUTURE_OTHER_OWNER_WRITES_TAG_FIELD_UNPROVEN",
                        true, false);
                    break;
                }
            }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DARK_FUTURE_SOURCE_CHANGED_DURING_GENERATE");

        Directory.CreateDirectory(output);
        if (transformed is not null && relative is not null)
        {
            string target = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", relative));
            if (!Inside(target, output))
                throw new InvalidDataException("DARK_FUTURE_GENERATED_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, transformed, new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return path.StartsWith(root + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
