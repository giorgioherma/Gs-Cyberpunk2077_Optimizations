using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// TriggerModeControl historical pass 5: remove repeatedly constructed
/// ForceAuto/RemoveAuto/InstantCharge CNames from GetTriggerModeKey.
/// Does not change aiming or ReadyEvents hint refresh timing.
/// Recognizes source-owned primary/secondary branches, handles partial
/// historical transformations, and preserves all unrelated owner code.
/// </summary>
internal static class TriggerModeLiteralTagsPass
{
    internal const string Rule = "TRIGGER_MODE_LITERAL_TAG_NAMES_V1";
    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Edit(int Start, int End, string Replacement);
    private sealed record Method(int Open, int Close);
    private sealed record Branch(int SecondaryStart, int SecondaryEnd,
        int PrimaryStart, int PrimaryEnd);

    private static readonly (string Prefix, string Var)[] Tags =
    [
        ("ForceAuto", "forceAutoTag"),
        ("RemoveAuto", "removeAutoTag"),
        ("InstantCharge", "instantChargeTag")
    ];

    private static int Closing(IReadOnlyList<SourceAdmission.Token> t,
        int start, string open, string close)
    {
        if (start < 0 || start >= t.Count || t[start].Value != open)
            return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }

    private static Method? Locate(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        Method? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                t[i + 1].Value != "GetTriggerModeKey" ||
                t[i + 2].Value != "(") continue;
            int args = Closing(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{")
                return null;
            int close = Closing(t, open, "{", "}");
            if (close < 0 || found is not null)
                return null;
            string header = source[t[i].Start..t[open].Start];
            string preceding = source[Math.Max(0, t[i].Start - 240)..t[i].Start];
            if (!Regex.IsMatch(header, @"\btrigger\s*:\s*Int32\b") ||
                !Regex.IsMatch(preceding,
                    @"@\s*addMethod\s*\(\s*InputContextTransitionEvents\s*\)\s*(?:(?:private|protected|public|final|static|const)\s+)*$"))
                return null;
            found = new(t[open].Start, t[close].Start);
            i = close;
        }
        return found;
    }

    private static Branch? FindBranches(string source, Method method)
    {
        string body = source[(method.Open + 1)..method.Close];
        var t = SourceAdmission.ReadTokens(body);
        Branch? found = null;
        for (int i = 0; i + 8 < t.Count; i++)
        {
            if (!(t[i].Value == "if" &&
                  t[i + 1].Value == "Equals" &&
                  t[i + 2].Value == "(" &&
                  t[i + 3].Value == "trigger" &&
                  t[i + 4].Value == "," &&
                  t[i + 5].Value == "2" &&
                  t[i + 6].Value == ")" &&
                  t[i + 7].Value == "{")) continue;
            int firstEnd = Closing(t, i + 7, "{", "}");
            if (firstEnd < 0 || firstEnd + 2 >= t.Count ||
                t[firstEnd + 1].Value != "else" ||
                t[firstEnd + 2].Value != "{") return null;
            int secondEnd = Closing(t, firstEnd + 2, "{", "}");
            if (secondEnd < 0 || found is not null) return null;
            int firstStart = method.Open + 1 + t[i + 7].End;
            int firstClose = method.Open + 1 + t[firstEnd].Start;
            int secondStart = method.Open + 1 + t[firstEnd + 2].End;
            int secondClose = method.Open + 1 + t[secondEnd].Start;
            if (!source[firstStart..firstClose].Contains(
                    "weaponRecord.SecondaryTriggerMode()", StringComparison.Ordinal) ||
                !source[secondStart..secondClose].Contains(
                    "weaponRecord.PrimaryTriggerMode()", StringComparison.Ordinal))
                return null;
            found = new(firstStart, firstClose, secondStart, secondClose);
        }
        return found;
    }

    private static string Shape(string s) =>
        string.Join("|", SourceAdmission.ReadTokens(s).Select(x => x.Value));

    private static (string Status, string? Reason, string? Changed)
        Rewrite(string source)
    {
        var method = Locate(source);
        if (method is null)
            return ("REFUSED", "TMC_TAG_HELPER_UNPROVEN", null);
        var branch = FindBranches(source, method);
        if (branch is null)
            return ("REFUSED", "TMC_PRIMARY_SECONDARY_BRANCH_UNPROVEN", null);

        string body = source[(method.Open + 1)..method.Close];
        var edits = new List<Edit>();
        var localNames = new List<string>();
        var secondary = new List<string>();
        var primary = new List<string>();
        int converted = 0;
        int dynamicCount = 0;
        string? strName = null;

        foreach (var tag in Tags)
        {
            string dynamicPattern =
                @"\bWeaponHasTag\s*\(\s*(?<call>StringToName\s*\(\s*""" +
                tag.Prefix + @"""\s*\+\s*(?<str>[A-Za-z_]\w*)\s*\))\s*\)";
            string fixedPattern =
                @"\bWeaponHasTag\s*\(\s*" + tag.Var + @"\s*\)";
            var old = Regex.Matches(body, dynamicPattern);
            var fixedMatches = Regex.Matches(body, fixedPattern);
            if (old.Count + fixedMatches.Count != 1)
                return ("REFUSED", "TMC_TAG_SITE_AMBIGUOUS:" + tag.Prefix, null);
            if (old.Count == 1)
            {
                string currentName = old[0].Groups["str"].Value;
                if (strName is not null &&
                    !string.Equals(strName, currentName, StringComparison.Ordinal))
                    return ("REFUSED", "TMC_DYNAMIC_SUFFIX_INCONSISTENT", null);
                strName = currentName;
                var span = old[0].Groups["call"];
                edits.Add(new(method.Open + 1 + span.Index,
                    method.Open + 1 + span.Index + span.Length, tag.Var));
                converted++;
                dynamicCount++;
            }

            // A partially rewritten source may already have the tag local,
            // one or both literal assignments, and its predicate. Only fill
            // the independently missing pieces; do not duplicate them.
            string declPattern = @"\blet\s+" + tag.Var +
                @"\s*:\s*(?<type>[A-Za-z_]\w*)\s*;";
            var declarations = Regex.Matches(body, declPattern);
            if (declarations.Count > 1 ||
                (declarations.Count == 1 &&
                 declarations[0].Groups["type"].Value != "CName"))
                return ("REFUSED", "TMC_TAG_FIELD_INCOMPATIBLE:" + tag.Var, null);
            if (declarations.Count == 0)
            {
                if (SourceAdmission.ReadTokens(body).Any(t => t.Value == tag.Var))
                    return ("REFUSED", "TMC_TAG_LOCAL_COLLISION:" + tag.Var, null);
                localNames.Add(tag.Var);
            }

            foreach (bool sec in new[] { true, false })
            {
                string literal = tag.Prefix + (sec ? "Secondary" : "Primary");
                int begin = sec ? branch.SecondaryStart : branch.PrimaryStart;
                int end = sec ? branch.SecondaryEnd : branch.PrimaryEnd;
                string arm = source[begin..end];
                var matches = Regex.Matches(arm,
                    @"\b" + tag.Var +
                    @"\s*=\s*n\s*""(?<value>[^""]+)""\s*;");
                if (matches.Count > 1 ||
                    (matches.Count == 1 &&
                     matches[0].Groups["value"].Value != literal))
                    return ("REFUSED",
                        "TMC_TAG_BRANCH_VALUE_INCOMPATIBLE:" + tag.Var, null);
                if (matches.Count == 0)
                {
                    if (sec) secondary.Add(tag.Var + " = n\"" + literal + "\";");
                    else primary.Add(tag.Var + " = n\"" + literal + "\";");
                }
            }
        }

        // The dynamic suffix may be removed only when its entire dataflow
        // consists of one String declaration and two literal assignments.
        // If custom source observes it elsewhere, retain that source.
        if (strName is not null)
        {
            string escaped = Regex.Escape(strName);
            var decl = Regex.Matches(body,
                @"\blet\s+" + escaped + @"\s*:\s*String\s*;");
            var assignSecondary = Regex.Matches(
                source[branch.SecondaryStart..branch.SecondaryEnd],
                @"\b" + escaped + @"\s*=\s*""Secondary""\s*;");
            var assignPrimary = Regex.Matches(
                source[branch.PrimaryStart..branch.PrimaryEnd],
                @"\b" + escaped + @"\s*=\s*""Primary""\s*;");
            if (decl.Count != 1 ||
                assignSecondary.Count != 1 || assignPrimary.Count != 1)
                return ("REFUSED", "TMC_DYNAMIC_SUFFIX_ASSIGNMENTS_UNPROVEN", null);
            // Only remove the obsolete suffix if all references were
            // the two original writes + declaration + converted call sites.
            int occurrences = SourceAdmission.ReadTokens(body)
                .Count(x => x.Value == strName);
            if (occurrences == 3 + dynamicCount)
            {
                edits.Add(new(method.Open + 1 + decl[0].Index,
                    method.Open + 1 + decl[0].Index + decl[0].Length, ""));
                edits.Add(new(branch.SecondaryStart + assignSecondary[0].Index,
                    branch.SecondaryStart + assignSecondary[0].Index +
                    assignSecondary[0].Length, ""));
                edits.Add(new(branch.PrimaryStart + assignPrimary[0].Index,
                    branch.PrimaryStart + assignPrimary[0].Index +
                    assignPrimary[0].Length, ""));
            }
        }

        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (localNames.Count > 0)
            edits.Add(new(method.Open + 1, method.Open + 1,
                string.Concat(localNames.Select(x =>
                    nl + "  let " + x + ": CName;"))));
        if (secondary.Count > 0)
            edits.Add(new(branch.SecondaryStart, branch.SecondaryStart,
                string.Concat(secondary.Select(x => nl + "    " + x))));
        if (primary.Count > 0)
            edits.Add(new(branch.PrimaryStart, branch.PrimaryStart,
                string.Concat(primary.Select(x => nl + "    " + x))));
        if (edits.Count == 0)
            return ("COMPLETE", null, null);

        string updated = source;
        foreach (Edit e in edits.OrderByDescending(x => x.Start))
            updated = updated[..e.Start] + e.Replacement + updated[e.End..];
        var verification = Locate(updated);
        if (verification is null || FindBranches(updated, verification) is null)
            throw new InvalidDataException("TMC_TAG_POSTCONDITION_BRANCH_LOST");
        string result = updated[(verification.Open + 1)..verification.Close];
        foreach (var tag in Tags)
        {
            if (!Regex.IsMatch(result,
                    @"\bWeaponHasTag\s*\(\s*" + tag.Var + @"\s*\)") ||
                !Regex.IsMatch(result,
                    @"\blet\s+" + tag.Var + @"\s*:\s*CName\s*;") ||
                !result.Contains("n\"" + tag.Prefix + "Primary\"",
                    StringComparison.Ordinal) ||
                !result.Contains("n\"" + tag.Prefix + "Secondary\"",
                    StringComparison.Ordinal) ||
                Regex.IsMatch(result,
                    @"\bStringToName\s*\(\s*""" + tag.Prefix + @""""))
                throw new InvalidDataException("TMC_TAG_POSTCONDITION:" + tag.Var);
        }
        return ("APPLIED", null, updated);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) ||
            Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TMC_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) || Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("TMC_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("TMC_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts, "TriggerModeControl");
        string? relative = null, rewritten = null, reason = null;
        string status = "NOT_APPLICABLE";
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).Where(f =>
                File.ReadAllText(f, Encoding.UTF8).Contains(
                    "func GetTriggerModeKey", StringComparison.Ordinal)).ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = files.Length == 0 ? "TMC_TAG_HELPER_MISSING" :
                    "TMC_TAG_HELPER_AMBIGUOUS";
            }
            else
            {
                string full = files[0];
                if ((File.GetAttributes(full) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("TMC_SOURCE_LINK_UNSAFE");
                relative = "r6/scripts/" +
                    Path.GetRelativePath(scripts, full).Replace('\\','/');
                string current = File.ReadAllText(full, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string staged = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(staged, basePreviewRoot))
                        throw new InvalidDataException("TMC_OVERLAY_ESCAPE");
                    if (File.Exists(staged))
                        current = File.ReadAllText(staged, Encoding.UTF8);
                }
                var decision = Rewrite(current);
                status = decision.Status;
                reason = decision.Reason;
                rewritten = decision.Changed;
            }
        }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("TMC_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        if (rewritten is not null && relative is not null)
        {
            string file = Path.GetFullPath(Path.Combine(
                output, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(file, output) ||
                !file.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("TMC_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, rewritten, new UTF8Encoding(false));
        }
        Result result = new(Rule, status,
            status == "APPLIED" ? 1 : 0,
            status == "COMPLETE" ? 1 : 0,
            relative, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
