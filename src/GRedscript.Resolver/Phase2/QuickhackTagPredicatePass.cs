using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Port of the game-tested Quickhack_Fixes pass 7 status-tag query.
/// The historical result avoids constructing an array of StatusEffect
/// objects merely to test whether the CommsNoiseIgnore tag exists.
/// Preserve both original wrappers, their results, and unrelated source.
/// </summary>
internal static class QuickhackTagPredicatePass
{
    internal const string Rule = "QUICKHACK_FIXES_DIRECT_TAG_PREDICATE_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Method(int Start, int End, string Name);
    private sealed record Edit(int Start, int End, string Text);
    private static readonly string[] Targets = ["ShouldStimBeProcessed", "HelpAlly"];

    private static int Match(IReadOnlyList<SourceAdmission.Token> tokens,
        int first, string opening, string closing)
    {
        if (first < 0 || first >= tokens.Count || tokens[first].Value != opening)
            return -1;
        int depth = 0;
        for (int i = first; i < tokens.Count; i++)
        {
            if (tokens[i].Value == opening) depth++;
            else if (tokens[i].Value == closing && --depth == 0)
                return i;
        }
        return -1;
    }

    private static IReadOnlyList<Method> Methods(string text)
    {
        var tokens = SourceAdmission.ReadTokens(text);
        var results = new List<Method>();
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value != "func" ||
                !Targets.Contains(tokens[i + 1].Value, StringComparer.Ordinal) ||
                tokens[i + 2].Value != "(")
                continue;
            int argsEnd = Match(tokens, i + 2, "(", ")");
            if (argsEnd < 0) throw new InvalidDataException("QHF_METHOD_ARGS_UNBALANCED");
            int body = argsEnd + 1;
            while (body < tokens.Count && tokens[body].Value is not ("{" or ";"))
                body++;
            if (body == tokens.Count || tokens[body].Value != "{")
                throw new InvalidDataException("QHF_METHOD_BODY_MISSING");
            int end = Match(tokens, body, "{", "}");
            if (end < 0) throw new InvalidDataException("QHF_METHOD_UNBALANCED");
            int begin = Math.Max(0, tokens[i].Start - 450);
            string head = text[begin..tokens[i].Start];
            // Only the author-owned ReactionManagerComponent wrapper is eligible.
            if (Regex.IsMatch(head,
                @"@wrapMethod\s*\(\s*ReactionManagerComponent\s*\)(?:\s|private|protected|public|final|cb|static)*$"))
                results.Add(new(tokens[i].Start, tokens[end].End, tokens[i + 1].Value));
            i = end;
        }
        return results;
    }

    private static string WithoutComments(string text) =>
        Regex.Replace(text, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");

    private static (string Status, string? Reason, string? Changed) InspectMethod(
        string text, string name)
    {
        if (!Regex.IsMatch(text, @"\bwrappedMethod\s*\("))
            return ("REFUSED", "QHF_WRAPPED_METHOD_MISSING", null);

        // An equivalent markerless historical optimization is complete.
        var direct = Regex.Match(text,
            @"\bif\s+IsDefined\s*\(\s*(?<obj>[A-Za-z_]\w*)\s*\)\s*&&\s*StatusEffectSystem\s*\.\s*ObjectHasStatusEffectWithTag\s*\(\s*\k<obj>\s*,\s*n\s*""CommsNoiseIgnore""\s*\)");
        bool legacyCall = Regex.IsMatch(text,
            @"\bStatusEffectHelper\s*\.\s*GetAppliedEffectsWithTag\s*\(");
        if (direct.Success && !legacyCall) return ("COMPLETE", null, null);
        if (direct.Success && legacyCall)
            return ("REFUSED", "QHF_DUPLICATE_TAG_PATHS", null);

        var decl = Regex.Matches(text,
            @"\blet\s+(?<tmp>[A-Za-z_]\w*)\s*:\s*array\s*<\s*ref\s*<\s*StatusEffect\s*>\s*>\s*;");
        if (decl.Count != 1)
            return ("REFUSED", "QHF_TEMP_ARRAY_DECLARATION_UNPROVEN", null);
        string temp = decl[0].Groups["tmp"].Value;
        string escaped = Regex.Escape(temp);
        var call = Regex.Matches(text,
            @"\bStatusEffectHelper\s*\.\s*GetAppliedEffectsWithTag\s*\(\s*(?<obj>[A-Za-z_]\w*)\s*,\s*n\s*""CommsNoiseIgnore""\s*,\s*" +
            escaped + @"\s*\)\s*;");
        if (call.Count != 1)
            return ("REFUSED", "QHF_TAG_QUERY_UNPROVEN", null);
        string actor = call[0].Groups["obj"].Value;
        var guard = Regex.Matches(text,
            @"\bif\s+ArraySize\s*\(\s*" + escaped +
            @"\s*\)\s*>\s*0\s*\{");
        if (guard.Count != 1 ||
            !(decl[0].Index < call[0].Index &&
              call[0].Index + call[0].Length <= guard[0].Index))
            return ("REFUSED", "QHF_GUARD_FLOW_UNPROVEN", null);

        // No hidden consumers of the temporary effects, including branches
        // introduced by a newer or custom version.
        var tokens = SourceAdmission.ReadTokens(text);
        if (tokens.Count(t => t.Value == temp) != 3)
            return ("REFUSED", "QHF_TEMP_ARRAY_HAS_OTHER_READERS", null);
        if (!string.IsNullOrWhiteSpace(WithoutComments(text[
            (call[0].Index + call[0].Length)..guard[0].Index])))
            return ("REFUSED", "QHF_QUERY_TO_GUARD_SIDE_EFFECTS", null);

        int brace = guard[0].Index + guard[0].Length - 1;
        int depth = 0, closing = -1;
        var braceTokens = SourceAdmission.ReadTokens(text);
        for (int i = 0; i < braceTokens.Count; i++)
        {
            if (braceTokens[i].Start < brace) continue;
            if (braceTokens[i].Value == "{") depth++;
            else if (braceTokens[i].Value == "}" && --depth == 0)
            {
                closing = braceTokens[i].Start;
                break;
            }
        }
        if (closing < 0)
            return ("REFUSED", "QHF_GUARD_BODY_UNBALANCED", null);
        string actual = WithoutComments(text[(brace + 1)..closing]).Trim();
        string required = name == "HelpAlly" ? "return;" : "return false;";
        if (!Regex.IsMatch(actual,
            @"^return\s*" + (name == "HelpAlly" ? "" : "false") + @"\s*;$"))
            return ("REFUSED", "QHF_GUARD_HAS_SIDE_EFFECTS", null);
        if (!Regex.IsMatch(text[(closing + 1)..],
            @"\bwrappedMethod\s*\("))
            return ("REFUSED", "QHF_FALLBACK_ORDER_UNPROVEN", null);

        var edits = new List<Edit>
        {
            new(decl[0].Index, decl[0].Index + decl[0].Length, ""),
            new(call[0].Index, call[0].Index + call[0].Length, ""),
            new(guard[0].Index, guard[0].Index + guard[0].Length,
                "if IsDefined(" + actor +
                ") && StatusEffectSystem.ObjectHasStatusEffectWithTag(" +
                actor + ", n\"CommsNoiseIgnore\") {")
        };
        string result = text;
        foreach (var e in edits.OrderByDescending(x => x.Start))
            result = result[..e.Start] + e.Text + result[e.End..];
        if (Regex.IsMatch(result, @"\bGetAppliedEffectsWithTag\s*\(") ||
            Regex.IsMatch(result, @"\bArraySize\s*\(\s*" + escaped + @"\s*\)"))
            throw new InvalidDataException("QHF_REWRITE_POSTCONDITION_FAILED");
        return ("APPLIED", null, result);
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
            throw new InvalidDataException("QHF_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) || Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("QHF_BASE_PREVIEW_UNSAFE");
        var audit = new SourceAdmission(game);
        _ = audit.AuditAll();
        string sourceHash = audit.SourceStackSha256 ??
            throw new InvalidDataException("QHF_SOURCE_HASH_MISSING");

        string owner = Path.Combine(scripts, "Quickhack_Fixes");
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reasons = new List<string>();
        int already = 0, edits = 0;
        if (Directory.Exists(owner))
        {
            foreach (string full in Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories))
            {
                if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("QHF_SOURCE_LINK_REFUSED");
                string rel = Path.GetRelativePath(scripts, full);
                string effective = File.ReadAllText(full, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string stagedInput = Path.GetFullPath(Path.Combine(
                        basePreviewRoot, "r6", "scripts", rel));
                    if (!Inside(stagedInput, basePreviewRoot))
                        throw new InvalidDataException("QHF_BASE_SOURCE_ESCAPE");
                    if (File.Exists(stagedInput))
                        effective = File.ReadAllText(stagedInput, Encoding.UTF8);
                }
                var methods = Methods(effective);
                if (methods.Count == 0) continue;
                if (methods.GroupBy(x => x.Name).Any(x => x.Count() != 1))
                {
                    reasons.Add("QHF_DUPLICATE_WRAPPER_TARGET");
                    continue;
                }
                var changes = new List<Edit>();
                foreach (var method in methods)
                {
                    if (!seen.Add(method.Name))
                    {
                        reasons.Add("QHF_MULTIPLE_TARGET_OWNERS:" + method.Name);
                        continue;
                    }
                    string original = effective[method.Start..method.End];
                    var result = InspectMethod(original, method.Name);
                    if (result.Status == "COMPLETE") already++;
                    else if (result.Status == "REFUSED")
                        reasons.Add(method.Name + ":" + result.Reason);
                    else if (result.Status == "APPLIED" && result.Changed is not null)
                    {
                        edits++;
                        changes.Add(new(method.Start, method.End, result.Changed));
                    }
                }
                if (changes.Count > 0)
                {
                    foreach (var change in changes.OrderByDescending(x => x.Start))
                        effective = effective[..change.Start] + change.Text +
                            effective[change.End..];
                    // Both methods must now either be semantically complete,
                    // or independently reported as an incompatible operation.
                    foreach (var method in Methods(effective))
                    {
                        string updated = effective[method.Start..method.End];
                        var post = InspectMethod(updated, method.Name);
                        if (post.Status != "COMPLETE" && !reasons.Any(x =>
                            x.StartsWith(method.Name + ":", StringComparison.Ordinal)))
                            throw new InvalidDataException(
                                "QHF_REWRITE_SEMANTIC_POSTCONDITION:" + method.Name);
                    }
                    staged.Add("r6/scripts/" + rel.Replace('\\', '/'), effective);
                }
            }
        }
        // An individual missing callback does not prevent optimizing its peer.
        if (Directory.Exists(owner))
            foreach (string target in Targets)
                if (!seen.Contains(target))
                    reasons.Add("QHF_TARGET_ABSENT:" + target);
        if (!string.Equals(sourceHash, audit.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("QHF_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        foreach (var file in staged)
        {
            string path = Path.GetFullPath(Path.Combine(output,
                file.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(path, output) || !path.EndsWith(".reds",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("QHF_STAGED_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Value, new UTF8Encoding(false));
        }
        string status = !Directory.Exists(owner) ? "NOT_APPLICABLE" :
            edits > 0 ? "APPLIED" :
            reasons.Count > 0 ? "REFUSED" : "COMPLETE";
        Result report = new(Rule, status, edits, already,
            staged.Keys.Order(StringComparer.Ordinal).ToArray(),
            reasons.Count > 0 ? string.Join("; ", reasons) : null, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
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
