using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical DisablePositionReveal/TracePositionOverhaul Pass 7:
/// avoid setting NPC tracing to false when tracing is already inactive.
/// The three independent author-owned callbacks keep their original
/// wrappedMethod, super call, event order and return behavior.
/// </summary>
internal static class DisablePositionRevealTracingPass
{
    internal const string Rule = "DISABLE_POSITION_REVEAL_TRACING_GUARDS_V1";
    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Span(string Name, int Start, int End);
    private static readonly string[] Targets =
        ["ChangeHighLevelState", "OnDamageReceived", "OnDefeated"];

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (start < 0 || start >= t.Count || t[start].Value != opening)
            return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Span[] Methods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var results = new List<Span>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                !Targets.Contains(t[i + 1].Value, StringComparer.Ordinal) ||
                t[i + 2].Value != "(") continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int close = Close(t, open, "{", "}");
            if (close < 0) continue;
            string preceding = source[Math.Max(0,t[i].Start - 230)..t[i].Start];
            string expected = t[i + 1].Value == "ChangeHighLevelState" ?
                "wrapMethod" : "addMethod";
            if (!Regex.IsMatch(preceding, @"@\s*" + expected +
                @"\s*\(\s*NPCPuppet\s*\)\s*(?:(?:public|protected|private|final|static|cb|const)\s+)*$"))
                continue;
            results.Add(new(t[i + 1].Value, t[open].Start + 1,
                t[close].Start));
            i = close;
        }
        return results.ToArray();
    }

    private static readonly Regex SetObj = new(
        @"\bNPCPuppet\s*\.\s*SetIsTracing\s*\(\s*obj\s*,\s*false\s*\)\s*;",
        RegexOptions.CultureInvariant);
    private static readonly Regex SetThis = new(
        @"\bNPCPuppet\s*\.\s*SetIsTracing\s*\(\s*this\s*,\s*false\s*\)\s*;",
        RegexOptions.CultureInvariant);

    private static (string Status, string? Reason, string? Rewritten)
        Inspect(string text, string name)
    {
        string nl = text.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        if (name == "ChangeHighLevelState")
        {
            var setters = SetObj.Matches(text);
            if (setters.Count != 1 ||
                !Regex.IsMatch(text, @"\bwrappedMethod\s*\(\s*obj\s*,\s*newState\s*\)") ||
                !Regex.IsMatch(text,
                    @"\bif\s+Equals\s*\(\s*newState\s*,\s*gamedataNPCHighLevelState\s*\.\s*Combat\s*\)\s*\{") ||
                text.IndexOf("wrappedMethod", StringComparison.Ordinal) >
                    setters[0].Index)
                return ("REFUSED", "DPR_COMBAT_EVENT_FLOW_UNPROVEN", null);

            var guarded = Regex.Match(text,
                @"\bif\s+IsDefined\s*\(\s*(?<obj>[A-Za-z_]\w*)\s*\)" +
                @"\s*&&\s*\k<obj>\s*\.\s*GetIsTracing\s*\(\s*\)\s*\{" +
                @"\s*NPCPuppet\s*\.\s*SetIsTracing\s*\(\s*obj\s*,\s*false\s*\)\s*;");
            if (guarded.Success)
            {
                string v = guarded.Groups["obj"].Value;
                if (Regex.IsMatch(text,
                    @"\blet\s+" + Regex.Escape(v) +
                    @"\s*:\s*wref\s*<\s*NPCPuppet\s*>\s*=\s*obj\s+as\s+NPCPuppet\s*;"))
                    return ("COMPLETE", null, null);
                return ("REFUSED", "DPR_COMBAT_NPC_CAST_UNPROVEN", null);
            }
            var cast = Regex.Match(text,
                @"\blet\s+(?<obj>[A-Za-z_]\w*)\s*:\s*wref\s*<\s*NPCPuppet\s*>" +
                @"\s*=\s*obj\s+as\s+NPCPuppet\s*;");
            if (cast.Success && Regex.Matches(text,
                @"\bobj\s+as\s+NPCPuppet\b").Count != 1)
                return ("REFUSED", "DPR_COMBAT_EXISTING_NPC_CAST_AMBIGUOUS", null);
            string varName = cast.Success ? cast.Groups["obj"].Value :
                "__gcoDprNpc";
            if (!cast.Success && SourceAdmission.ReadTokens(text)
                .Any(t => t.Value == varName))
                return ("REFUSED", "DPR_COMBAT_LOCAL_NAME_COLLISION", null);
            if (Regex.IsMatch(text,
                @"\b" + Regex.Escape(varName) + @"\s*\.\s*GetIsTracing\s*\("))
                return ("REFUSED", "DPR_COMBAT_PARTIAL_GUARD_UNPROVEN", null);
            string indent = "    ";
            string guard = (cast.Success ? "" :
                "let " + varName + ": wref<NPCPuppet> = obj as NPCPuppet;" +
                nl + indent) +
                "if IsDefined(" + varName + ") && " + varName +
                ".GetIsTracing() {" + nl + indent + "  " +
                setters[0].Value + nl + indent + "}";
            return ("APPLIED", null, text[..setters[0].Index] + guard +
                text[(setters[0].Index + setters[0].Length)..]);
        }
        if (name == "OnDamageReceived")
        {
            if (!Regex.IsMatch(text,
                @"\bsuper\s*\.\s*OnDamageReceived\s*\(\s*evt\s*\)"))
                return ("REFUSED", "DPR_DAMAGE_SUPER_FALLBACK_MISSING", null);
            var setters = SetThis.Matches(text);
            if (setters.Count != 1)
                return ("REFUSED", "DPR_DAMAGE_TRACING_SET_UNPROVEN", null);
            var condition = Regex.Match(text,
                @"\bif\s+(?<guard>(?:this\s*\.\s*GetIsTracing\s*\(\s*\)\s*&&\s*)?" +
                @"evt\s*\.\s*hitEvent\s*\.\s*attackData\s*\.\s*HasFlag\s*" +
                @"\(\s*hitFlag\s*\.\s*WasKillingBlow\s*\))\s*\{");
            if (!condition.Success ||
                condition.Index > setters[0].Index)
                return ("REFUSED", "DPR_DAMAGE_KILL_FLAG_FLOW_UNPROVEN", null);
            string between = text[(condition.Index + condition.Length)..setters[0].Index];
            // Only comments/whitespace may separate the guard from the setter.
            if (!string.IsNullOrWhiteSpace(Regex.Replace(
                between, @"//[^\r\n]*|/\*[\s\S]*?\*/", "")))
                return ("REFUSED", "DPR_DAMAGE_UNRELATED_BRANCH_SIDE_EFFECTS", null);
            if (Regex.IsMatch(condition.Groups["guard"].Value,
                @"^\s*this\s*\.\s*GetIsTracing\s*\("))
                return ("COMPLETE", null, null);
            string expr = condition.Groups["guard"].Value;
            int start = condition.Groups["guard"].Index;
            return ("APPLIED", null,
                text[..start] + "this.GetIsTracing() && " + expr +
                text[(start + condition.Groups["guard"].Length)..]);
        }
        if (name == "OnDefeated")
        {
            if (!Regex.IsMatch(text,
                @"\bsuper\s*\.\s*OnDefeated\s*\(\s*evt\s*\)"))
                return ("REFUSED", "DPR_DEFEATED_SUPER_FALLBACK_MISSING", null);
            var setters = SetThis.Matches(text);
            if (setters.Count != 1)
                return ("REFUSED", "DPR_DEFEATED_TRACING_SET_UNPROVEN", null);
            if (Regex.IsMatch(text,
                @"\bif\s+this\s*\.\s*GetIsTracing\s*\(\s*\)\s*\{\s*" +
                @"NPCPuppet\s*\.\s*SetIsTracing\s*\(\s*this\s*,\s*false\s*\)\s*;"))
                return ("COMPLETE", null, null);
            if (Regex.IsMatch(text,
                @"\bif\s+this\s*\.\s*GetIsTracing\s*\("))
                return ("REFUSED", "DPR_DEFEATED_PARTIAL_GUARD_UNPROVEN", null);
            string replacement = "if this.GetIsTracing() {" +
                nl + "    " + setters[0].Value + nl + "  }";
            return ("APPLIED", null,
                text[..setters[0].Index] + replacement +
                text[(setters[0].Index + setters[0].Length)..]);
        }
        return ("REFUSED", "DPR_UNKNOWN_TARGET", null);
    }

    private static (string Status, int Edits, int Complete,
        string? Reason, string? Rewritten) Transform(string source)
    {
        var methods = Methods(source);
        if (methods.GroupBy(m => m.Name).Any(g => g.Count() != 1))
            return ("REFUSED", 0, 0, "DPR_TARGET_DUPLICATE", null);
        string updated = source;
        int edits = 0, complete = 0;
        var failures = new List<string>();
        foreach (string target in Targets)
        {
            var m = methods.SingleOrDefault(x => x.Name == target);
            if (m is null)
            {
                failures.Add(target + ":DPR_TARGET_MISSING");
                continue;
            }
            string sourceMethod = source[m.Start..m.End];
            var state = Inspect(sourceMethod, target);
            if (state.Status == "COMPLETE") complete++;
            else if (state.Status == "REFUSED")
                failures.Add(target + ":" + state.Reason);
            else if (state.Status == "APPLIED" && state.Rewritten is not null)
                edits++;
        }
        // Apply transformations from the end of the original source to avoid
        // changing method offsets when several independent callbacks qualify.
        foreach (var m in methods.OrderByDescending(x => x.Start))
        {
            var state = Inspect(source[m.Start..m.End], m.Name);
            if (state.Status != "APPLIED" || state.Rewritten is null) continue;
            updated = updated[..m.Start] + state.Rewritten + updated[m.End..];
        }
        foreach (var m in Methods(updated))
        {
            var state = Inspect(updated[m.Start..m.End], m.Name);
            if (state.Status == "APPLIED")
                throw new InvalidDataException("DPR_TRACING_POSTCONDITION_INCOMPLETE:" +
                    m.Name);
        }
        return (edits > 0 ? "APPLIED" :
            failures.Count > 0 ? "REFUSED" : "COMPLETE",
            edits, complete,
            failures.Count > 0 ? string.Join("; ", failures) : null,
            edits > 0 ? updated : null);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("DPR_TRACING_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("DPR_TRACING_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sourceHash = admission.SourceStackSha256 ??
            throw new InvalidDataException("DPR_TRACING_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts, "DisablePositionReveal");
        string status = "NOT_APPLICABLE";
        string? rel = null, modified = null, reason = null;
        int edits = 0, complete = 0;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).Where(f =>
                File.ReadAllText(f, Encoding.UTF8).Contains(
                    "func ChangeHighLevelState", StringComparison.Ordinal)).ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = files.Length == 0 ? "DPR_TRACING_OWNER_HELPER_MISSING" :
                    "DPR_TRACING_OWNER_AMBIGUOUS";
            }
            else
            {
                string full = files[0];
                if ((File.GetAttributes(full) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("DPR_TRACING_SOURCE_LINK");
                rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts,full).Replace('\\','/');
                string source = File.ReadAllText(full, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string previous = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(previous, basePreviewRoot))
                        throw new InvalidDataException("DPR_TRACING_OVERLAY_ESCAPE");
                    if (File.Exists(previous))
                        source = File.ReadAllText(previous, Encoding.UTF8);
                }
                var result = Transform(source);
                status = result.Status;
                edits = result.Edits;
                complete = result.Complete;
                reason = result.Reason;
                modified = result.Rewritten;
            }
        }
        if (!string.Equals(sourceHash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DPR_TRACING_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (modified is not null && rel is not null)
        {
            string dest = Path.GetFullPath(Path.Combine(
                output, rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dest, output) ||
                !dest.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DPR_TRACING_OUTPUT_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, modified, new UTF8Encoding(false));
        }
        Result report = new(Rule, status, edits, complete, rel, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
