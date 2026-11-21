using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Three independently admitted hot-owner operations, applied to the effective
/// post-generic source. No callbacks, subscriptions, framework state, timers,
/// native adapters or historical whole-file replacements are introduced.
/// </summary>
internal static class NewHotOwnerSemanticPass
{
    internal enum Owner { Apartment, Negotiable, Shattered }

    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string[] SourcePaths,
        string[] Refusals, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Open, int Close);
    private sealed record Decision(string Status, string? Reason,
        string? Changed);

    private static readonly RegexOptions Rx = RegexOptions.CultureInvariant |
        RegexOptions.Singleline;

    private static bool Has(string source, string pattern) =>
        Regex.IsMatch(source, pattern, Rx);

    private static Match Match(string source, string pattern) =>
        Regex.Match(source, pattern, Rx);

    private static int Pair(IReadOnlyList<SourceAdmission.Token> t,
        int start, string begin, string end)
    {
        if (start < 0 || start >= t.Count || t[start].Value != begin) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == begin) depth++;
            else if (t[i].Value == end && --depth == 0) return i;
        }
        return -1;
    }

    // The lexer, rather than line numbers or source-file names, owns offsets.
    private static Block? Method(string source, string method,
        string? inClass = null, bool wrappedPlayer = false)
    {
        var t = SourceAdmission.ReadTokens(source);
        int classBegin = 0, classEnd = source.Length, classes = 0;
        if (inClass is not null)
        {
            for (int i = 0; i + 2 < t.Count; i++)
            {
                if (t[i].Value != "class" || t[i + 1].Value != inClass) continue;
                int op = i + 2;
                while (op < t.Count && t[op].Value is not ("{" or ";")) op++;
                if (op >= t.Count || t[op].Value != "{") return null;
                int cl = Pair(t, op, "{", "}");
                if (cl < 0) return null;
                classBegin = t[op].End;
                classEnd = t[cl].Start;
                classes++;
                i = cl;
            }
            if (classes != 1) return null;
        }

        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != method ||
                t[i + 2].Value != "(" || t[i].Start < classBegin ||
                t[i].Start >= classEnd) continue;
            if (wrappedPlayer)
            {
                string previous = source[Math.Max(0, t[i].Start - 250)..t[i].Start];
                if (!Has(previous, @"@\s*wrapMethod\s*\(\s*PlayerPuppet\s*\)" +
                    @"(?:(?:\s+|(?://[^\r\n]*\r?\n))|(?:public|protected|private|cb|final)\s+)*$"))
                    continue;
            }
            int args = Pair(t, i + 2, "(", ")");
            if (args < 0) return null;
            int op = args + 1;
            while (op < t.Count && t[op].Value is not ("{" or ";")) op++;
            if (op >= t.Count || t[op].Value != "{") return null;
            int cl = Pair(t, op, "{", "}");
            if (cl < 0 || t[cl].Start > classEnd || found is not null)
                return null;
            found = new(t[op].End, t[cl].Start);
            i = cl;
        }
        return found;
    }

    private static string Text(string source, Block method) =>
        source[method.Open..method.Close];

    private static string NL(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string[] Names(string body, string variable) =>
        Regex.Matches(body,
                @"\bEquals\s*\(\s*" + Regex.Escape(variable) +
                @"\s*,\s*(n""[^""]+"")\s*\)", Rx)
            .Select(x => x.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).ToArray();

    private static string Filter(string variable, string[] names) =>
        string.Join(" && ", names.Select(n =>
            "NotEquals(" + variable + ", " + n + ")"));

    private static bool NoUnexpectedNameUses(string body, string variable)
    {
        string other = Regex.Replace(body,
            @"\bEquals\s*\(\s*" + Regex.Escape(variable) +
            @"\s*,\s*n""[^""]+""\s*\)", "", Rx);
        other = Regex.Replace(other,
            @"\blet\s+" + Regex.Escape(variable) +
            @"\s*=\s*(?:ListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)|" +
            @"__gcoNaChoiceName)\s*;", "", Rx);
        // Pre-existing GCO negative name conditions are recognized separately.
        other = Regex.Replace(other,
            @"(?:NotEquals|!Equals)\s*\(\s*" + Regex.Escape(variable) +
            @"\s*,\s*n""[^""]+""\s*\)", "", Rx);
        return !Regex.IsMatch(other, @"\b" + Regex.Escape(variable) + @"\b");
    }

    private static bool IsNegativeNameGuard(
        string expression, string variable)
    {
        string remainder = Regex.Replace(expression,
            @"(?:NotEquals|!Equals)\s*\(\s*" + Regex.Escape(variable) +
            @"\s*,\s*n""[^""]+""\s*\)", "", Rx);
        remainder = remainder.Replace("&&", "", StringComparison.Ordinal)
            .Replace("(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(remainder) &&
            Has(expression, @"(?:NotEquals|!Equals)\s*\(");
    }

    private static Decision Apartment(string source)
    {
        var listener = Method(source, "OnAction", "Gt8ugAptInputListener");
        var action = Method(source, "OnChoiceAction", "Gt8ugAptSystem");
        if (listener is null || action is null)
            return new("REFUSED", "APT_LISTENER_OR_DISPATCH_AMBIGUOUS", null);
        string handler = Text(source, listener), dispatch = Text(source, action);
        if (!Has(handler, @"\blet\s+apt\s*=\s*Gt8ugAptSystem\s*\.\s*Get\s*\(\s*\)\s*;") ||
            !Has(handler, @"\bif\s+IsDefined\s*\(\s*apt\s*\)\s*\{\s*apt\s*\.\s*OnChoiceAction\s*\(\s*action\s*,\s*consumer\s*\)\s*;") ||
            !Has(handler, @"\breturn\s+false\s*;") ||
            !Has(dispatch, @"\bif\s*!\s*this\s*\.\s*m_shown\s*\{") ||
            !Has(dispatch, @"\bif\s+NotEquals\s*\(\s*ListenerAction\.GetType\s*\(\s*action\s*\)\s*,\s*gameinputActionType\.BUTTON_PRESSED\s*\)\s*\{") ||
            !Has(dispatch, @"\blet\s+name\s*=\s*ListenerAction\.GetName\s*\(\s*action\s*\)\s*;") ||
            !Has(dispatch, @"\bconsumer\s*\.\s*Consume\s*\(\s*\)\s*;") ||
            !Has(dispatch, @"\bthis\s*\.\s*Activate\s*\(\s*\)\s*;"))
            return new("REFUSED", "APT_CHOICE_OR_CONSUMER_FLOW_UNPROVEN", null);

        // The stock ignored-action path only writes a constant None to a
        // private unused field. If a custom version observes or mutates it,
        // bypassing the dispatch could change behavior, so refuse.
        string remains = Regex.Replace(source,
            @"\bprivate\s+let\s+m_lastInput\s*:\s*CName\s*;|" +
            @"(?:this\s*\.\s*)?m_lastInput\s*=\s*n""None""\s*;", "", Rx);
        if (Has(remains, @"\bm_lastInput\b"))
            return new("REFUSED", "APT_IGNORED_ACTION_SIDE_EFFECT_UNPROVEN", null);

        // Before the name read, only the two original reset-and-return
        // guards are eligible. Unknown extra callbacks must still execute.
        var nameRead = Match(dispatch,
            @"\blet\s+name\s*=\s*ListenerAction\.GetName\s*\(\s*action\s*\)\s*;");
        string beforeName = dispatch[..nameRead.Index];
        if (!Has(beforeName,
            @"^\s*if\s*!\s*this\.m_shown\s*\{\s*this\.m_lastInput\s*=\s*n""None""\s*;\s*return\s+false\s*;\s*\}\s*;?\s*if\s+NotEquals\s*\(\s*ListenerAction\.GetType\s*\(\s*action\s*\)\s*,\s*gameinputActionType\.BUTTON_PRESSED\s*\)\s*\{\s*this\.m_lastInput\s*=\s*n""None""\s*;\s*return\s+false\s*;\s*\}\s*;?\s*$") ||
            !Has(dispatch[(nameRead.Index + nameRead.Length)..],
                @"\bthis\.m_lastInput\s*=\s*n""None""\s*;\s*return\s+false\s*;\s*$"))
            return new("REFUSED", "APT_INPUT_RESET_LIFECYCLE_CHANGED", null);
        var names = Names(dispatch, "name");
        if (names.Length == 0 ||
            !names.Contains("n\"ChoiceApply\"", StringComparer.Ordinal) ||
            !NoUnexpectedNameUses(dispatch, "name"))
            return new("REFUSED", "APT_DYNAMIC_INPUT_NAME_UNPROVEN", null);
        var entry = Match(handler, @"\blet\s+apt\s*=\s*Gt8ugAptSystem\s*\.\s*Get\s*\(\s*\)\s*;");
        string before = handler[..entry.Index];
        if (!string.IsNullOrWhiteSpace(before) &&
            !Has(before, @"^\s*if\s+"))
            return new("REFUSED", "APT_PRE_DISPATCH_SIDE_EFFECT_UNPROVEN", null);

        string filter = Filter("ListenerAction.GetName(action)", names) +
            " || NotEquals(ListenerAction.GetType(action), gameinputActionType.BUTTON_PRESSED)";
        var existing = Match(before, @"\bif\s+(?<expr>[^{]+)\{\s*return\s+false\s*;\s*\}\s*;?");
        if (existing.Success)
        {
            string expr = existing.Groups["expr"].Value;
            // Only our already-admitted name/type fast path may be completed.
            if (!Has(expr, @"ListenerAction\.GetName\s*\(\s*action\s*\)") ||
                !Has(expr, @"NotEquals\s*\(\s*ListenerAction\.GetName") ||
                (Has(expr, @"ListenerAction\.GetType") &&
                 !Has(expr, @"gameinputActionType\.BUTTON_PRESSED")))
                return new("REFUSED", "APT_PARTIAL_INPUT_GUARD_UNPROVEN", null);
            string current = Regex.Replace(expr, @"\s+", "");
            string wanted = Regex.Replace(filter, @"\s+", "");
            if (current == wanted)
                return new("COMPLETE", null, null);
            string changed = source[..listener.Open + existing.Groups["expr"].Index] +
                filter + source[listener.Open + existing.Groups["expr"].Index +
                existing.Groups["expr"].Length..];
            return new("APPLIED", null, changed);
        }
        if (!string.IsNullOrWhiteSpace(before))
            return new("REFUSED", "APT_UNKNOWN_PRE_DISPATCH_GUARD", null);
        string nl = NL(source);
        string prelude = nl + "    if " + filter +
            " { return false; }" + nl + "    ";
        return new("APPLIED", null,
            source[..listener.Open] + prelude + source[listener.Open..]);
    }

    private static Decision NAMain(string source)
    {
        var method = Method(source, "OnAction", wrappedPlayer: true);
        if (method is null)
            return new("REFUSED", "NA_MAIN_PLAYER_WRAPPER_AMBIGUOUS", null);
        string body = Text(source, method);
        if (!Has(body, @"\blet\s+result\s*=\s*wrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)\s*;") ||
            !Has(body, @"\bif\s*!\s*Equals\s*\(\s*ListenerAction\.GetType\s*\(\s*action\s*\)\s*,\s*gameinputActionType\.BUTTON_RELEASED\s*\)") ||
            !Has(body, @"\bif\s*!\s*IsDefined\s*\(\s*qs\s*\)\s*\{\s*return\s+result\s*;\s*\}") ||
            !Has(body, @"\breturn\s+result\s*;"))
            return new("REFUSED", "NA_MAIN_ORIGINAL_CALLBACK_FLOW_UNPROVEN", null);
        var quest = Match(body, @"\blet\s+qs\s*=\s*GameInstance\s*\.\s*GetQuestsSystem\s*\(\s*this\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*;");
        var name = Match(body, @"\blet\s+actionName\s*=\s*ListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)\s*;");
        if (!quest.Success || !name.Success)
            return new("REFUSED", "NA_MAIN_NAME_OR_QUEST_ANCHOR_MISSING", null);
        string[] actions = Names(body, "actionName");
        if (actions.Length < 4 || !NoUnexpectedNameUses(body, "actionName"))
            return new("REFUSED", "NA_MAIN_ACTION_SET_OR_CUSTOM_CONSUMER_UNPROVEN", null);
        string nl = NL(source), indent = "    ";
        if (name.Index > quest.Index)
        {
            string between = body[(quest.Index + quest.Length)..name.Index];
            if (!Has(between, @"^\s*if\s*!\s*IsDefined\s*\(\s*qs\s*\)\s*\{\s*return\s+result\s*;\s*\}\s*$"))
                return new("REFUSED", "NA_MAIN_PRE_NAME_SIDE_EFFECT_UNPROVEN", null);
            string prefix = "let actionName = ListenerAction.GetName(action);" +
                nl + indent + "if " + Filter("actionName", actions) +
                " { return result; }" + nl + indent;
            string updated = body[..quest.Index] + prefix +
                body[quest.Index..name.Index] + body[(name.Index + name.Length)..];
            return new("APPLIED", null,
                source[..method.Open] + updated + source[method.Close..]);
        }
        string betweenEarly = body[(name.Index + name.Length)..quest.Index];
        var guard = Match(betweenEarly,
            @"\bif\s+(?<expr>[^{]+)\{\s*return\s+result\s*;\s*\}\s*;?");
        if (guard.Success)
        {
            string expr = guard.Groups["expr"].Value;
            if (!IsNegativeNameGuard(expr, "actionName") ||
                !string.IsNullOrWhiteSpace(
                    betweenEarly[..guard.Index] + betweenEarly[(guard.Index + guard.Length)..]))
                return new("REFUSED", "NA_MAIN_PARTIAL_NAME_GUARD_UNPROVEN", null);
            string missing = Filter("actionName", actions);
            var oldNames = Regex.Matches(expr,
                @"(?:NotEquals|!Equals)\s*\(\s*actionName\s*,\s*(n""[^""]+"")\s*\)", Rx)
                .Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
            if (actions.All(oldNames.Contains))
                return new("COMPLETE", null, null);
            string changed = betweenEarly[..guard.Groups["expr"].Index] +
                missing + betweenEarly[(guard.Groups["expr"].Index +
                guard.Groups["expr"].Length)..];
            string updated = body[..name.Index + name.Length] + changed +
                body[quest.Index..];
            return new("APPLIED", null,
                source[..method.Open] + updated + source[method.Close..]);
        }
        if (!string.IsNullOrWhiteSpace(betweenEarly))
            return new("REFUSED", "NA_MAIN_PRE_QUEST_FLOW_CHANGED", null);
        string added = nl + indent + "if " + Filter("actionName", actions) +
            " { return result; }" + nl + indent;
        return new("APPLIED", null, source[..method.Open + name.Index + name.Length] +
            added + source[method.Open + name.Index + name.Length..]);
    }

    private static Decision NAChoice(string source)
    {
        var method = Method(source, "OnAction", wrappedPlayer: true);
        if (method is null)
            return new("REFUSED", "NA_CHOICE_PLAYER_WRAPPER_AMBIGUOUS", null);
        string body = Text(source, method);
        if (!Has(body, @"\blet\s+qs\s*=\s*GameInstance\s*\.\s*GetQuestsSystem") ||
            !Has(body, @"\bqs\s*\.\s*GetFact\s*\(\s*n""na_active""\s*\)") ||
            !Has(body, @"\bthis\s*\.\s*m_naDialogueChoiceSystem\b") ||
            !Has(body, @"\breturn\s+wrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)\s*;") ||
            !Has(body, @"ListenerActionConsumer\s*\.\s*Consume\s*\(\s*consumer\s*\)"))
            return new("REFUSED", "NA_CHOICE_QUEST_OR_CONSUMER_FLOW_UNPROVEN", null);
        var quest = Match(body, @"\blet\s+qs\s*=\s*GameInstance\s*\.\s*GetQuestsSystem\s*\(\s*this\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*;");
        var name = Match(body,
            @"\blet\s+actionName\s*=\s*(?:ListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)|__gcoNaChoiceName)\s*;");
        if (!quest.Success || !name.Success)
            return new("REFUSED", "NA_CHOICE_GETTER_ANCHOR_UNPROVEN", null);
        string[] names = Names(body, "actionName");
        if (names.Length < 8 || !NoUnexpectedNameUses(body, "actionName"))
            return new("REFUSED", "NA_CHOICE_DYNAMIC_ACTIONS_UNPROVEN", null);

        string nl = NL(source), prefixName = "__gcoNaChoiceName";
        var prefetch = Match(body, @"\blet\s+__gcoNaChoiceName\s*=\s*ListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)\s*;");
        var edits = new List<(int Start, int End, string Value)>();
        if (!prefetch.Success)
        {
            if (name.Groups[0].Value.Contains("__gcoNaChoiceName", StringComparison.Ordinal))
                return new("REFUSED", "NA_CHOICE_MISSING_PREFETCH_DECLARATION", null);
            if (!string.IsNullOrWhiteSpace(body[..quest.Index]))
                return new("REFUSED", "NA_CHOICE_EARLY_SIDE_EFFECT_UNPROVEN", null);
            edits.Add((quest.Index, quest.Index, "let " + prefixName +
                " = ListenerAction.GetName(action);" + nl + "    if " +
                Filter(prefixName, names) +
                " { return wrappedMethod(action, consumer); }" + nl + "    "));
            edits.Add((name.Index, name.Index + name.Length,
                "let actionName = " + prefixName + ";"));
        }
        else
        {
            if (prefetch.Index > quest.Index)
                return new("REFUSED", "NA_CHOICE_PREFETCH_AFTER_QUEST", null);
            string between = body[(prefetch.Index + prefetch.Length)..quest.Index];
            var guard = Match(between,
                @"\bif\s+(?<expr>[^{]+)\{\s*return\s+wrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)\s*;\s*\}\s*;?");
            if (guard.Success)
            {
                if (!IsNegativeNameGuard(guard.Groups["expr"].Value, prefixName) ||
                    !string.IsNullOrWhiteSpace(
                        between[..guard.Index] + between[(guard.Index + guard.Length)..]))
                    return new("REFUSED", "NA_CHOICE_PARTIAL_GATE_UNPROVEN", null);
                var currentNames = Regex.Matches(guard.Groups["expr"].Value,
                    @"(?:NotEquals|!Equals)\s*\(\s*__gcoNaChoiceName\s*,\s*(n""[^""]+"")\s*\)", Rx)
                    .Select(x => x.Groups[1].Value).ToHashSet(StringComparer.Ordinal);
                if (!names.All(currentNames.Contains))
                    edits.Add((prefetch.Index + prefetch.Length +
                        guard.Groups["expr"].Index,
                        prefetch.Index + prefetch.Length +
                        guard.Groups["expr"].Index + guard.Groups["expr"].Length,
                        Filter(prefixName, names)));
            }
            else if (string.IsNullOrWhiteSpace(between))
                edits.Add((prefetch.Index + prefetch.Length,
                    prefetch.Index + prefetch.Length,
                    nl + "    if " + Filter(prefixName, names) +
                    " { return wrappedMethod(action, consumer); }" + nl + "    "));
            else
                return new("REFUSED", "NA_CHOICE_UNKNOWN_PRE_QUEST_FLOW", null);
            if (name.Value.Contains("ListenerAction", StringComparison.Ordinal))
                edits.Add((name.Index, name.Index + name.Length,
                    "let actionName = " + prefixName + ";"));
        }
        if (edits.Count == 0)
            return new("COMPLETE", null, null);
        string changed = body;
        foreach (var e in edits.OrderByDescending(x => x.Start))
            changed = changed[..e.Start] + e.Value + changed[e.End..];
        return new("APPLIED", null,
            source[..method.Open] + changed + source[method.Close..]);
    }

    private static Decision Shattered(string source)
    {
        var m = Method(source, "SC_DotRow");
        if (m is null || !Has(source, @"\bmodule\s+ShatteredChrome\b") ||
            !Has(source, @"\bfunc\s+SCLeaderWidth\s*\(\s*\)\s*->\s*Int32\s*=\s*\d+\s*;?"))
            return new("REFUSED", "SC_DOT_ROW_OR_WIDTH_UNPROVEN", null);
        string body = Text(source, m);
        if (!Has(body, @"\blet\s+out\s*:\s*String\s*=\s*label\s*\+\s*""\s""\s*;") ||
            !Has(body, @"\breturn\s+out\s*\+\s*""\s""\s*\+\s*value\s*;"))
            return new("REFUSED", "SC_DOT_ROW_OUTPUT_CHANGED", null);

        var original = Match(body, @"\bwhile\s+StrLen\s*\(\s*out\s*\)\s*<\s*SCLeaderWidth\s*\(\s*\)\s*\{\s*out\s*=\s*out\s*\+\s*""\.""\s*;\s*\}\s*;?");
        var prior = Match(body, @"\blet\s+(?<name>[A-Za-z_]\w*)\s*:\s*Int32\s*=\s*SCLeaderWidth\s*\(\s*\)\s*-\s*StrLen\s*\(\s*out\s*\)\s*;");
        string counter = prior.Success ? prior.Groups["name"].Value : "__gcoScDotRemaining";
        if (original.Success)
        {
            if (!prior.Success && Has(body, @"\b__gcoScDotRemaining\b"))
                return new("REFUSED", "SC_DOT_LOCAL_COLLISION", null);
            if (prior.Success && prior.Index > original.Index)
                return new("REFUSED", "SC_DOT_COUNTER_ORDER_UNPROVEN", null);
            string nl = NL(source);
            string loop = "while " + counter + " > 0 {" + nl +
                "    out = out + \".\";" + nl +
                "    " + counter + " -= 1;" + nl + "  };";
            string replacement = (prior.Success ? "" :
                "let " + counter + ": Int32 = SCLeaderWidth() - StrLen(out);" +
                nl + "  ") + loop;
            string updated = body[..original.Index] + replacement +
                body[(original.Index + original.Length)..];
            return new("APPLIED", null,
                source[..m.Open] + updated + source[m.Close..]);
        }
        if (!prior.Success)
            return new("REFUSED", "SC_DOT_LOOP_PATTERN_CHANGED", null);
        var loop2 = Match(body, @"\bwhile\s+" + Regex.Escape(counter) +
            @"\s*>\s*0\s*\{\s*out\s*=\s*out\s*\+\s*""\.""\s*;\s*(?:" +
            Regex.Escape(counter) + @"\s*-=\s*1\s*;\s*)?\}\s*;?");
        if (!loop2.Success)
            return new("REFUSED", "SC_DOT_PARTIAL_COUNTER_FLOW_UNPROVEN", null);
        if (Has(loop2.Value, @"\b" + Regex.Escape(counter) + @"\s*-=\s*1\s*;"))
            return new("COMPLETE", null, null);
        int close = loop2.Value.LastIndexOf('}');
        string changed2 = body[..loop2.Index + close] + counter +
            " -= 1; " + body[loop2.Index + close..];
        return new("APPLIED", null,
            source[..m.Open] + changed2 + source[m.Close..]);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot, Owner owner)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || File.Exists(output) ||
            Directory.Exists(output) || Inside(output, game))
            throw new InvalidDataException("HOT_OWNER_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("HOT_OWNER_OVERLAY_UNSAFE");

        string folder = owner switch
        {
            Owner.Apartment => "gt_8ug_apt",
            Owner.Negotiable => "negotiableaffection",
            _ => "ShatteredChrome"
        };
        string rule = owner switch
        {
            Owner.Apartment => "APT_CHOICE_INPUT_EARLY_ADMISSION_V1",
            Owner.Negotiable => "NA_PLAYER_ACTION_EARLY_ADMISSION_V1",
            _ => "SHATTERED_CHROME_DOT_ROW_BOUND_V1"
        };
        var sourceAudit = new SourceAdmission(game);
        _ = sourceAudit.AuditAll();
        string fingerprint = sourceAudit.SourceStackSha256 ??
            throw new InvalidDataException("HOT_OWNER_SOURCE_HASH_MISSING");
        var staged = new List<(string Relative, string Source)>();
        var refusals = new List<string>();
        int applied = 0, complete = 0;
        bool present = Directory.Exists(Path.Combine(scripts, folder));
        if (present)
        {
            string root = Path.Combine(scripts, folder);
            var files = Directory.EnumerateFiles(root, "*.reds",
                SearchOption.AllDirectories).ToArray();
            (string Id, Func<string, bool> Pick, Func<string, Decision> Rewrite)[] operations =
                owner switch
                {
                    Owner.Apartment => [
                        ("apartment-listener",
                         s => Has(s, @"\bclass\s+Gt8ugAptInputListener\b"),
                         Apartment)],
                    Owner.Negotiable => [
                        ("na-main",
                         s => s.Contains("na_walk_beat", StringComparison.Ordinal) &&
                              s.Contains("na_ponc_finish", StringComparison.Ordinal) &&
                              Has(s, @"@\s*wrapMethod\s*\(\s*PlayerPuppet\s*\)"),
                         NAMain),
                        ("na-dialogue",
                         s => s.Contains("m_naDialogueChoiceSystem", StringComparison.Ordinal) &&
                              s.Contains("na_choice_apply", StringComparison.Ordinal) &&
                              Has(s, @"@\s*wrapMethod\s*\(\s*PlayerPuppet\s*\)"),
                         NAChoice)],
                    _ => [
                        ("sc-dot-row",
                         s => Has(s, @"\bfunc\s+SC_DotRow\s*\("),
                         Shattered)]
                };

            foreach (var op in operations)
            {
                var candidates = files.Where(file =>
                    op.Pick(File.ReadAllText(file, Encoding.UTF8))).ToArray();
                if (candidates.Length != 1)
                {
                    refusals.Add(op.Id + ":HOT_OWNER_TARGET_MISSING_OR_AMBIGUOUS");
                    continue;
                }
                string file = candidates[0];
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("HOT_OWNER_SOURCE_LINK_UNSAFE");
                string rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\', '/');
                string src = File.ReadAllText(file, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string effective = Path.GetFullPath(Path.Combine(
                        basePreviewRoot, rel.Replace('/',
                            Path.DirectorySeparatorChar)));
                    if (!Inside(effective, basePreviewRoot))
                        throw new InvalidDataException("HOT_OWNER_EFFECTIVE_PATH_ESCAPE");
                    if (File.Exists(effective))
                        src = File.ReadAllText(effective, Encoding.UTF8);
                }
                var existing = staged.FindIndex(x => x.Relative == rel);
                if (existing >= 0) src = staged[existing].Source;
                Decision decision = op.Rewrite(src);
                if (decision.Status == "APPLIED" && decision.Changed is not null)
                {
                    if (decision.Changed == src)
                        throw new InvalidDataException("HOT_OWNER_NO_CHANGE");
                    Decision check = op.Rewrite(decision.Changed);
                    if (check.Status != "COMPLETE")
                        throw new InvalidDataException(
                            "HOT_OWNER_GENERATED_POSTCONDITION:" + op.Id +
                            ":" + (check.Reason ?? check.Status));
                    foreach (Match marker in Regex.Matches(src,
                        @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
                        if (!decision.Changed.Contains(marker.Value,
                            StringComparison.Ordinal))
                            throw new InvalidDataException(
                                "HOT_OWNER_GENERIC_MARKER_LOST:" + op.Id);
                    if (existing >= 0) staged[existing] = (rel, decision.Changed);
                    else staged.Add((rel, decision.Changed));
                    applied++;
                }
                else if (decision.Status == "COMPLETE") complete++;
                else if (decision.Status == "REFUSED")
                    refusals.Add(op.Id + ":" + (decision.Reason ?? "SOURCE_INCOMPATIBLE"));
                else throw new InvalidDataException("HOT_OWNER_UNKNOWN_STATUS");
            }
        }
        if (!string.Equals(fingerprint,
            sourceAudit.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("HOT_OWNER_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        foreach (var (relative, value) in staged)
        {
            string destination = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(destination, output) ||
                !destination.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HOT_OWNER_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, value, new UTF8Encoding(false));
        }
        string status = applied > 0 ? "APPLIED" :
            refusals.Count > 0 ? "REFUSED" :
            complete > 0 ? "COMPLETE" : "NOT_APPLICABLE";
        var result = new Result(rule, status, applied, complete,
            staged.Select(x => x.Relative).ToArray(), refusals.ToArray(),
            true, false);
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
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
