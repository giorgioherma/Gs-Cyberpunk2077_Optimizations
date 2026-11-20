using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Hoist a stable local/parameter array's length out of a simple while loop.
/// Structural token-based admission only; no knowledge of mods, callbacks,
/// historical source versions or capture-derived mod inventories.
/// </summary>
internal static class GenericStableArrayBoundPass
{
    internal const string Rule = "STABLE_ARRAY_BOUND_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;
    private sealed record Candidate(string Relative, int Line, string Index,
        string Array, string Alias, int Start, int End, string LoopText,
        string BodySha256);
    private sealed record Inspected(string Relative, string Text,
        IReadOnlyList<SourceAdmission.Token> Tokens);
    private sealed record State(List<Candidate> Eligible, int AlreadyComplete);
    internal sealed record Summary(int SourceFiles, int Eligible, int AlreadyComplete);

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputFile);
        if (IsInside(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("ARRAY_BOUND_SCAN_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var candidates = new List<object>();
        int count = 0, complete = 0;
        foreach (var file in ReadStack(game, admission))
        {
            var state = Examine(file);
            count += state.Eligible.Count;
            complete += state.AlreadyComplete;
            foreach (var e in state.Eligible)
                candidates.Add(new {
                    path = "r6/scripts/" + e.Relative, line = e.Line,
                    state = "PRISTINE_APPLICABLE", rule = Rule,
                    index = e.Index, array = e.Array,
                    removedLengthEvaluationsPerCompletedIteration = 1,
                    originalLoopBodyPreserved = true,
                    noUnknownCallsOrArrayWrites = true,
                    generationAuthorized = false
                });
        }
        AssertStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.array-bound.scan.v1",
            sourceFiles = admission.InspectedFileCount,
            sourceStackSha256 = admission.SourceStackSha256,
            eligible = count, alreadyComplete = complete, candidates,
            originalSourcesModified = false, gameplayValidated = false,
            measuredSavingsClaimed = false, generationAuthorized = false
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, count, complete);
    }

    internal static int Preview(string gameRoot, string analysisFile,
        string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string analysis = Path.GetFullPath(analysisFile);
        string output = Path.GetFullPath(outputRoot);
        if (IsInside(output, game) || File.Exists(output) ||
            (Directory.Exists(output) &&
             Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("ARRAY_BOUND_PREVIEW_OUTPUT_NOT_ISOLATED");
        string? priorScripts = null;
        if (basePreviewRoot is not null)
        {
            string prior = Path.GetFullPath(basePreviewRoot);
            if (!Directory.Exists(prior) || IsInside(prior, game) ||
                prior.Equals(output, StringComparison.OrdinalIgnoreCase) ||
                IsInside(prior, output) || IsInside(output, prior))
                throw new InvalidDataException("ARRAY_BOUND_BASE_PREVIEW_NOT_ISOLATED");
            priorScripts = Path.Combine(prior, "r6", "scripts");
            if (!Directory.Exists(priorScripts))
                throw new InvalidDataException("ARRAY_BOUND_BASE_PREVIEW_SCRIPTS_MISSING");
        }
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var doc = JsonDocument.Parse(File.ReadAllText(analysis)))
        {
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetString() !=
                    "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance")
                    .GetString() != "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source")
                    .GetProperty("sourceStackSha256").GetString(),
                    admission.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ARRAY_BOUND_CAPTURE_OR_SOURCE_DRIFT");
        }
        var staged = new List<(string Relative, string Text)>();
        int edits = 0;
        foreach (var original in ReadStack(game, admission))
        {
            State originalState = Examine(original);
            if (originalState.Eligible.Count == 0) continue;
            string effectiveText = original.Text;
            State effectiveState = originalState;
            if (priorScripts is not null)
            {
                string overlay = Path.GetFullPath(Path.Combine(priorScripts,
                    original.Relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsInside(overlay, priorScripts))
                    throw new InvalidDataException("ARRAY_BOUND_BASE_PREVIEW_PATH_INVALID");
                if (File.Exists(overlay))
                {
                    effectiveText = File.ReadAllText(overlay, Encoding.UTF8);
                    effectiveState = Examine(new(original.Relative, effectiveText,
                        SourceAdmission.ReadTokens(effectiveText)));
                    string[] before = originalState.Eligible
                        .Select(c => c.Index + "\0" + c.Array + "\0" + c.LoopText)
                        .OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    string[] after = effectiveState.Eligible
                        .Select(c => c.Index + "\0" + c.Array + "\0" + c.LoopText)
                        .OrderBy(s => s, StringComparer.Ordinal).ToArray();
                    if (originalState.AlreadyComplete != effectiveState.AlreadyComplete ||
                        !before.SequenceEqual(after, StringComparer.Ordinal))
                        throw new InvalidDataException(
                            "ARRAY_BOUND_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT");
                }
            }
            string result = effectiveText;
            string newline = result.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n" : "\n";
            foreach (var e in effectiveState.Eligible.OrderByDescending(x => x.Start))
            {
                int lineBegin = result.LastIndexOf('\n', Math.Max(0, e.Start - 1)) + 1;
                string indent = result[lineBegin..e.Start];
                string declaration = Marker + " array=" + e.Array +
                    " index=" + e.Index + " alias=" + e.Alias +
                    " bodySha256=" + e.BodySha256 + newline +
                    indent + "let " + e.Alias + ": Int32 = ArraySize(" +
                    e.Array + ");" + newline + indent;
                string rewrittenLoop = "while " + e.Index + " < " +
                    e.Alias + result[(e.Start + ("while " + e.Index +
                    " < ArraySize(" + e.Array + ")").Length)..e.End];
                result = result[..e.Start] + declaration + rewrittenLoop +
                    result[e.End..];
                edits++;
            }
            var generated = Examine(new(original.Relative, result,
                SourceAdmission.ReadTokens(result)));
            if (generated.AlreadyComplete !=
                    originalState.AlreadyComplete + effectiveState.Eligible.Count ||
                generated.Eligible.Count != 0)
                throw new InvalidDataException("ARRAY_BOUND_GENERATED_POSTCONDITION_FAILED");
            staged.Add((original.Relative, result));
        }
        AssertStable(admission);
        foreach (var f in staged)
        {
            string target = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", f.Relative.Replace('/',
                    Path.DirectorySeparatorChar)));
            if (!IsInside(target, output) || File.Exists(target))
                throw new InvalidDataException("ARRAY_BOUND_STAGE_SOURCE_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, f.Text, new UTF8Encoding(false));
        }
        return edits;
    }

    private static State Examine(Inspected file)
    {
        var t = file.Tokens;
        int complete = VerifyGenerated(file.Text, t);
        var candidates = new List<Candidate>();
        for (int k = 0; k + 7 < t.Count; k++)
        {
            if (t[k].Value != "while" || !Identifier(t[k + 1].Value) ||
                t[k + 2].Value != "<" || t[k + 3].Value != "ArraySize" ||
                t[k + 4].Value != "(" ||
                !Identifier(t[k + 5].Value) ||
                t[k + 6].Value != ")" || t[k + 7].Value != "{")
                continue;
            string index = t[k + 1].Value, array = t[k + 5].Value;
            if (index == array) continue;
            int lineBegin = file.Text.LastIndexOf('\n',
                Math.Max(0, t[k].Start - 1)) + 1;
            string indent = file.Text[lineBegin..t[k].Start];
            if (indent.Any(c => c is not (' ' or '\t'))) continue;
            int end = Closing(t, k + 7, "{", "}");
            if (end < 0 || !ArrayIsCurrentScopedValue(t, k, array) ||
                !ClosedReadOnlyBody(t, k + 7, end, array))
                continue;
            string alias = "__gcoArrBoundL" + t[k].Line;
            if (file.Text.Contains(alias, StringComparison.Ordinal)) continue;
            string body = file.Text[t[k + 7].Start..t[end].End];
            candidates.Add(new(file.Relative, t[k].Line, index, array, alias,
                t[k].Start, t[end].End, file.Text[t[k].Start..t[end].End],
                Sha(body)));
        }
        return new(candidates, complete);
    }

    // Resolve the array as a local typed array still in lexical scope, or
    // as a non-out array parameter. A property, global or unknown receiver is
    // not admitted. Never infer source identity from a mod's name/version.
    private static bool ArrayIsCurrentScopedValue(
        IReadOnlyList<SourceAdmission.Token> t, int loop, string array)
    {
        int methodOpen = -1, methodArgs = -1, methodArgsEnd = -1;
        for (int j = 0; j + 2 < loop; j++)
        {
            if (t[j].Value != "func" || !Identifier(t[j + 1].Value) ||
                t[j + 2].Value != "(") continue;
            int argsEnd = Closing(t, j + 2, "(", ")");
            if (argsEnd < 0 || argsEnd >= loop) continue;
            int open = -1;
            for (int p = argsEnd + 1; p < Math.Min(loop, argsEnd + 80); p++)
            {
                if (t[p].Value == ";") break;
                if (t[p].Value == "{") { open = p; break; }
            }
            if (open < 0 || Closing(t, open, "{", "}") < loop) continue;
            if (open > methodOpen)
            { methodOpen = open; methodArgs = j + 2; methodArgsEnd = argsEnd; }
        }
        if (methodOpen < 0) return false;
        bool parameter = false;
        for (int j = methodArgs + 1; j + 3 < methodArgsEnd; j++)
        {
            if (t[j].Value == array && t[j + 1].Value == ":" &&
                t[j + 2].Value == "array" && t[j + 3].Value == "<" &&
                (j <= methodArgs + 1 ||
                 t[j - 1].Value is not ("out" or "ref")))
                parameter = true;
        }
        // Scope stack contains only still-open lexical braces. An array local
        // declared inside a closed branch cannot become a candidate.
        var stack = new List<int> { methodOpen };
        var validDecl = false;
        for (int j = methodOpen + 1; j < loop; j++)
        {
            if (t[j].Value == "{") stack.Add(j);
            else if (t[j].Value == "}")
            {
                if (stack.Count < 2) return false;
                stack.RemoveAt(stack.Count - 1);
            }
        }
        var loopScope = stack.ToArray();
        stack.Clear(); stack.Add(methodOpen);
        int declared = 0;
        for (int j = methodOpen + 1; j < loop; j++)
        {
            if (t[j].Value == "{") stack.Add(j);
            else if (t[j].Value == "}")
            {
                if (stack.Count < 2) return false;
                stack.RemoveAt(stack.Count - 1);
            }
            if (j + 3 >= loop || t[j].Value != "let" ||
                t[j + 1].Value != array) continue;
            declared++;
            if (t[j + 2].Value == ":" && t[j + 3].Value == "array" &&
                stack.Count <= loopScope.Length &&
                stack.SequenceEqual(loopScope.Take(stack.Count)))
                validDecl = true;
        }
        // Shadowing/duplicate declarations are deliberately refused.
        return declared <= 1 && (validDecl || declared == 0 && parameter);
    }

    // No calls, nested loops, mutation of the array (including indexed or
    // member writes) or possible alias/escape. Ordinary conditional returns
    // and arithmetic on other variables remain untouched.
    private static bool ClosedReadOnlyBody(
        IReadOnlyList<SourceAdmission.Token> t, int open, int end, string array)
    {
        for (int j = open + 1; j < end; j++)
        {
            string v = t[j].Value;
            if (v is "while" or "for" or "foreach" or "switch" or "new" or
                "out" or "ref" or "goto" or "asm" or "ArrayPush" or "ArrayPop" or
                "ArrayClear" or "ArrayResize" or "ArrayErase" or "ArrayInsert")
                return false;
            // Even an apparently pure call can mutate a captured collection,
            // so initially admit ONLY the no-call family.
            if (j + 1 < end && t[j + 1].Value == "(" &&
                Identifier(v) && v is not ("if" or "unless"))
                return false;
            if (v != array) continue;
            if (j + 1 >= end || t[j + 1].Value != "[" ||
                j > open && t[j - 1].Value == ".")
                return false;
            int close = Closing(t, j + 1, "[", "]");
            if (close < 0 || close >= end) return false;
            if (close + 1 < end && t[close + 1].Value is "=" or "." or
                "++" or "--" or "+=" or "-=" or "*=" or "/=")
                return false;
            // ++ and compound writes can be tokenized as multiple symbols.
            if (close + 2 < end &&
                t[close + 1].Value is "+" or "-" or "*" or "/" &&
                t[close + 2].Value == "=")
                return false;
        }
        return true;
    }

    // Validate marker, original unchanged body hash and generated declaration.
    // A custom edit to an already-generated loop is not silently assimilated.
    private static int VerifyGenerated(string text,
        IReadOnlyList<SourceAdmission.Token> tokens)
    {
        int count = text.Split(Marker, StringSplitOptions.None).Length - 1;
        if (count == 0) return 0;
        var lines = text.Split('\n');
        var rx = new Regex("^" + Regex.Escape(Marker) +
            @" array=(?<array>[A-Za-z_][A-Za-z0-9_]*)" +
            @" index=(?<index>[A-Za-z_][A-Za-z0-9_]*)" +
            @" alias=(?<alias>__gcoArrBoundL[0-9]+)" +
            @" bodySha256=(?<sha>[0-9A-F]{64})$",
            RegexOptions.CultureInvariant);
        var aliases = new HashSet<string>(StringComparer.Ordinal);
        int accepted = 0;
        for (int line = 0; line < lines.Length; line++)
        {
            string trimmed = lines[line].Trim();
            if (!trimmed.Contains(Marker, StringComparison.Ordinal)) continue;
            var m = rx.Match(trimmed);
            if (!m.Success || line + 2 >= lines.Length)
                throw new InvalidDataException("ARRAY_BOUND_PARTIAL_MARKER");
            string array = m.Groups["array"].Value, index = m.Groups["index"].Value;
            string alias = m.Groups["alias"].Value;
            if (!aliases.Add(alias))
                throw new InvalidDataException("ARRAY_BOUND_DUPLICATE_ALIAS");
            string indent = lines[line][..(lines[line].Length -
                lines[line].TrimStart(' ', '\t').Length)];
            if (lines[line + 1].TrimEnd('\r') != indent + "let " +
                    alias + ": Int32 = ArraySize(" + array + ");")
                throw new InvalidDataException("ARRAY_BOUND_PARTIAL_DECLARATION");
            int w = -1;
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Line == line + 3 && tokens[i].Value == "while")
                { w = i; break; }
            if (w < 0 || w + 4 >= tokens.Count ||
                tokens[w + 1].Value != index ||
                tokens[w + 2].Value != "<" ||
                tokens[w + 3].Value != alias ||
                tokens[w + 4].Value != "{" ||
                !lines[line + 2].StartsWith(indent + "while " + index +
                    " < " + alias + " {", StringComparison.Ordinal))
                throw new InvalidDataException("ARRAY_BOUND_PARTIAL_LOOP");
            int end = Closing(tokens, w + 4, "{", "}");
            if (end < 0 || Sha(text[tokens[w + 4].Start..tokens[end].End]) !=
                    m.Groups["sha"].Value)
                throw new InvalidDataException("ARRAY_BOUND_MODIFIED_BODY");
            accepted++;
        }
        if (accepted != count)
            throw new InvalidDataException("ARRAY_BOUND_PARTIAL_MARKER_COUNT");
        return accepted;
    }

    private static int Closing(IReadOnlyList<SourceAdmission.Token> t,
        int start, string open, string close)
    {
        if (start >= t.Count || t[start].Value != open) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }

    private static string Sha(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static bool Identifier(string s) =>
        s.Length > 0 && (char.IsLetter(s[0]) || s[0] == '_') &&
        s.All(c => char.IsLetterOrDigit(c) || c == '_');
    private static IEnumerable<Inspected> ReadStack(
        string game, SourceAdmission admission)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        foreach (string full in Directory.EnumerateFiles(
            scripts, "*.reds", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!admission.IsFileIndexed(full)) continue;
            string relative = Path.GetRelativePath(scripts, full).Replace('\\', '/');
            string text = File.ReadAllText(full, Encoding.UTF8);
            yield return new(relative, text, SourceAdmission.ReadTokens(text));
        }
    }
    private static void AssertStable(SourceAdmission a)
    {
        if (!string.Equals(a.SourceStackSha256,
            a.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ARRAY_BOUND_SOURCE_CHANGED_DURING_PASS");
    }
    private static bool IsInside(string candidate, string root)
    {
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        candidate = Path.GetFullPath(candidate);
        return candidate.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
