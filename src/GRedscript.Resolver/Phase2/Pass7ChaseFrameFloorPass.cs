using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Independent historical Pass 7 owners. This is an integration-ready
/// generator, deliberately not registered in the central stage order while
/// Pass 2.2/3 and Pass 5 are developed on other branches.
/// </summary>
public static class Pass7ChaseFrameFloorPass
{
    public const string RuleId = "PASS7_CHASE_FRAME_FLOOR_V1";
    public sealed record Operation(string Id, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason);
    public sealed record Result(string RuleId, int AppliedEdits,
        Operation[] Operations, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Start, int End);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Changed,
        int Edits, int Complete, string? Reason);
    private static readonly RegexOptions Rx = RegexOptions.CultureInvariant |
        RegexOptions.Multiline;

    private static bool Has(string text, string pattern) =>
        Regex.IsMatch(text, pattern, Rx);

    private static int Close(IReadOnlyList<SourceAdmission.Token> tokens,
        int start, string open, string close)
    {
        if (start < 0 || start >= tokens.Count || tokens[start].Value != open)
            return -1;
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Value == open) depth++;
            else if (tokens[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }

    // Locate real wrapper/method boundaries. The source path, whitespace,
    // surrounding helpers and line numbers are not acceptance criteria.
    private static Block? Find(string source, string owner, string method,
        string attribute)
    {
        var tokens = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < tokens.Count; i++)
        {
            if (tokens[i].Value != "func" ||
                tokens[i + 1].Value != method ||
                tokens[i + 2].Value != "(") continue;
            int args = Close(tokens, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < tokens.Count && tokens[open].Value is not ("{" or ";"))
                open++;
            if (open >= tokens.Count || tokens[open].Value != "{")
                return null;
            int end = Close(tokens, open, "{", "}");
            if (end < 0) return null;
            string before = source[Math.Max(0, tokens[i].Start - 300)..tokens[i].Start];
            if (Regex.IsMatch(before,
                @"@\s*" + Regex.Escape(attribute) + @"\s*\(\s*" +
                Regex.Escape(owner) +
                @"\s*\)\s*(?:(?:public|private|protected|static|final|const|cb)\s+)*$",
                Rx))
            {
                if (found is not null) return null;
                found = new(tokens[open].End, tokens[end].Start);
            }
            i = end;
        }
        return found;
    }

    private static string Apply(string source, IEnumerable<Edit> edits)
    {
        var ordered = edits.OrderByDescending(e => e.Start)
            .ThenByDescending(e => e.End).ToArray();
        for (int i = 1; i < ordered.Length; i++)
            if (ordered[i].End > ordered[i - 1].Start)
                throw new InvalidDataException("PASS7_OVERLAPPING_EDIT");
        foreach (var edit in ordered)
            source = source[..edit.Start] + edit.Text + source[edit.End..];
        return source;
    }

    private static string Newline(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static Decision SpawnCulling(string source)
    {
        var span = Find(source, "NPCPuppet", "OnGameAttached", "wrapMethod");
        if (span is null || !Has(source, @"\bNCR_CullChanceForTier\s*\("))
            return new("REFUSED", null, 0, 0, "NCR_SPAWN_CALLBACK_MISSING");
        string body = source[span.Start..span.End];
        var wrapped = Regex.Match(body,
            @"\blet\s+(?<result>\w+)\s*:\s*Bool\s*=\s*wrappedMethod\s*\(\s*\)\s*;", Rx);
        var game = Regex.Match(body, @"\blet\s+gi\s*:\s*GameInstance\s*=\s*this\.GetGame\s*\(\s*\)\s*;", Rx);
        var player = Regex.Match(body, @"\blet\s+player\s*:[^;]+\bGetPlayer\s*\(\s*gi\s*\)\s*;", Rx);
        var system = Regex.Match(body, @"\bNCRSystem\s*\.\s*Get\s*\(\s*gi\s*\)", Rx);
        var roll = Regex.Match(body, @"\bRandF\s*\(\s*\)", Rx);
        if (!wrapped.Success || !game.Success || !player.Success ||
            !system.Success || !roll.Success ||
            !(wrapped.Index < game.Index && game.Index < player.Index &&
              player.Index < system.Index && system.Index < roll.Index) ||
            !Has(body[..game.Index], @"\bIsPreGame\s*\(\s*\)"))
            return new("REFUSED", null, 0, 0, "NCR_SPAWN_FLOW_UNPROVEN");

        var patterns = new[]
        {
            @"this\s*\.\s*IsQuest\s*\(\s*\)",
            @"ScriptedPuppet\s*\.\s*IsPlayerCompanion\s*\(\s*this\s*\)",
            @"this\s*\.\s*IsCrowd\s*\(\s*\)"
        };
        var edits = new List<Edit>();
        var move = new List<string>();
        int complete = 0;
        foreach (string predicate in patterns)
        {
            var guards = Regex.Matches(body,
                @"^[ \t]*if\s+" + predicate +
                @"\s*\{\s*return\s+" +
                Regex.Escape(wrapped.Groups["result"].Value) +
                @"\s*;\s*\}\s*;?[ \t]*(?:\r?\n)?", Rx);
            if (guards.Count != 1)
                return new("REFUSED", null, 0, complete,
                    "NCR_SPAWN_GUARD_MISSING_OR_DUPLICATED");
            var guard = guards[0];
            if (guard.Index > game.Index && guard.Index < player.Index)
            {
                complete++;
                continue;
            }
            if (guard.Index < player.Index || guard.Index > roll.Index)
                return new("REFUSED", null, 0, complete,
                    "NCR_SPAWN_GUARD_ALREADY_RELOCATED_UNPROVEN");
            move.Add(guard.Value.Trim());
            edits.Add(new(span.Start + guard.Index,
                span.Start + guard.Index + guard.Length, ""));
        }
        if (edits.Count == 0) return new("COMPLETE", null, 0, 3, null);
        string nl = Newline(source);
        string indent = "    ";
        string additions = string.Join(nl,
            move.Select(x => indent + x)) + nl;
        // All original guard expressions are moved; the RNG, affiliation,
        // NPC scene checks and spawn callback are byte-preserved.
        edits.Add(new(span.Start + player.Index,
            span.Start + player.Index, additions));
        string changed = Apply(source, edits);
        var checkedState = SpawnCulling(changed);
        if (checkedState.Status != "COMPLETE")
            throw new InvalidDataException("NCR_SPAWN_POSTCONDITION_FAILED");
        return new("APPLIED", changed, edits.Count, complete, null);
    }

    private static Decision StealthDetection(string source)
    {
        var span = Find(source, "NPCPuppet",
            "OnPlayerDetectionChangedEvent", "wrapMethod");
        if (span is null || !Has(source,
            @"\blet\s+m_stealthRunnerStatus\s*:\s*ref\s*<\s*NPCPuppetStealthRunnerStatus\s*>"))
            return new("REFUSED", null, 0, 0, "SR_DETECTION_OWNER_OR_FIELD_MISSING");
        string body = source[span.Start..span.End];
        var wrapped = Regex.Match(body,
            @"\bwrappedMethod\s*\(\s*evt\s*\)\s*;", Rx);
        var player = Regex.Match(body,
            @"\blet\s+player\s*:[^;]+;", Rx);
        if (!wrapped.Success || !player.Success ||
            wrapped.Index + wrapped.Length > player.Index ||
            !Has(body[player.Index..], @"\bSpottedPlayer\s*\(\s*player\s*\)"))
            return new("REFUSED", null, 0, 0, "SR_DETECTION_CALL_ORDER_UNPROVEN");
        string[] expressions =
        [
            @"this\s*\.\s*m_stealthRunnerStatus",
            @"this\s*\.\s*m_stealthRunnerStatus\s*\.\s*quest"
        ];
        var missing = new List<string>();
        int complete = 0;
        foreach (string expr in expressions)
        {
            var guards = Regex.Matches(body,
                @"\bif\s*!\s*IsDefined\s*\(\s*" + expr +
                @"\s*\)\s*\{\s*return\s+false\s*;\s*\}", Rx);
            if (guards.Count > 1 || guards.Cast<Match>().Any(
                g => g.Index < wrapped.Index || g.Index > player.Index))
                return new("REFUSED", null, 0, complete,
                    "SR_DETECTION_GUARD_POSITION_INCOMPATIBLE");
            if (guards.Count == 1) complete++;
            else missing.Add("  if !IsDefined(" +
                (complete == 0 && missing.Count == 0 ?
                    "this.m_stealthRunnerStatus" :
                    expr.Contains("quest", StringComparison.Ordinal) ?
                    "this.m_stealthRunnerStatus.quest" :
                    "this.m_stealthRunnerStatus") +
                ") { return false; }");
        }
        if (missing.Count == 0) return new("COMPLETE", null, 0, 2, null);
        // In the normal empty and status-only partial cases the status guard
        // executes before accessing quest. Quest-only partials are refused.
        if (complete == 1 &&
            !Has(body[..player.Index],
                @"!\s*IsDefined\s*\(\s*this\s*\.\s*m_stealthRunnerStatus\s*\)"))
            return new("REFUSED", null, 0, complete,
                "SR_DETECTION_QUEST_ONLY_PARTIAL");
        string nl = Newline(source);
        string addition = nl + string.Join(nl, missing) + nl;
        int insertion = wrapped.Index + wrapped.Length;
        if (complete == 1 && missing.Count == 1 &&
            missing[0].Contains(".quest", StringComparison.Ordinal))
        {
            // A handwritten status-only partial guard must run before the
            // added quest dereference, not after it. Preserve that proof.
            var statusGuard = Regex.Match(body,
                @"\bif\s*!\s*IsDefined\s*\(\s*this\s*\.\s*m_stealthRunnerStatus\s*\)\s*\{\s*return\s+false\s*;\s*\}", Rx);
            if (!statusGuard.Success)
                return new("REFUSED", null, 0, complete,
                    "SR_DETECTION_PARTIAL_STATUS_GUARD_UNPROVEN");
            insertion = statusGuard.Index + statusGuard.Length;
        }
        string changed = Apply(source,
        [
            new Edit(span.Start + insertion,
                span.Start + insertion, addition)
        ]);
        if (StealthDetection(changed).Status != "COMPLETE")
            throw new InvalidDataException("SR_DETECTION_POSTCONDITION_FAILED");
        return new("APPLIED", changed, missing.Count, complete, null);
    }

    private static readonly Regex PerkLocal = new(
        @"\blet\s+hasDoubleRunPerk\s*:\s*Bool\s*=\s*(?<expr>PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\([^;]+?)\s*;",
        Rx);
    private const string SprintField = "m_gredRunningManDoubleRunPerk";

    private static Decision DoubleSprint(string source)
    {
        var eval = Find(source, "SprintEvents",
            "EvaluateTwoStepSprint", "replaceMethod");
        var enter = Find(source, "SprintEvents", "OnEnter", "wrapMethod");
        if (eval is null || enter is null)
            return new("REFUSED", null, 0, 0, "RM_SPRINT_METHODS_MISSING");
        string body = source[eval.Start..eval.End],
            arrival = source[enter.Start..enter.End];
        var queryEval = PerkLocal.Matches(body);
        var queryEnter = PerkLocal.Matches(arrival);
        var originalUseEval = Regex.Matches(body,
            @"\bif\s+hasDoubleRunPerk\b", Rx);
        var originalUseEnter = Regex.Matches(arrival,
            @"\bif\s+hasDoubleRunPerk\s*&&", Rx);
        bool cachedEval = Has(body, @"\bif\s+this\s*\.\s*" + SprintField + @"\b"),
            cachedEnter = Has(arrival, @"\bif\s+this\s*\.\s*" + SprintField + @"\s*&&"),
            assigned = Has(arrival, @"\bthis\s*\.\s*" + SprintField + @"\s*=");
        var wrapper = Regex.Match(arrival,
            @"\bwrappedMethod\s*\(\s*stateContext\s*,\s*scriptInterface\s*\)\s*;", Rx);
        if (!wrapper.Success ||
            (queryEval.Count != (cachedEval ? 0 : 1)) ||
            (queryEnter.Count != (cachedEnter ? 0 : 1)) ||
            (originalUseEval.Count != (cachedEval ? 0 : 1)) ||
            (originalUseEnter.Count != (cachedEnter ? 0 : 1)) ||
            (cachedEval && originalUseEval.Count > 0) ||
            (cachedEnter && originalUseEnter.Count > 0))
            return new("REFUSED", null, 0, 0, "RM_SPRINT_PERK_FLOW_INCOMPATIBLE");
        string? expression = queryEnter.Count == 1 ?
            queryEnter[0].Groups["expr"].Value.Trim() :
            queryEval.Count == 1 ? queryEval[0].Groups["expr"].Value.Trim() : null;
        if (expression is not null &&
            (!Has(expression, @"Body_Left_Milestone_1") ||
             !Has(expression, @">\s*1\s*$")))
            return new("REFUSED", null, 0, 0, "RM_SPRINT_PERK_ID_OR_THRESHOLD_CHANGED");
        if (queryEval.Count == 1 && queryEnter.Count == 1 &&
            Regex.Replace(queryEval[0].Groups["expr"].Value, @"\s+", "") !=
            Regex.Replace(queryEnter[0].Groups["expr"].Value, @"\s+", ""))
            return new("REFUSED", null, 0, 0, "RM_SPRINT_PERK_EXPRESSIONS_DIVERGED");
        var field = Regex.Matches(source,
            @"@\s*addField\s*\(\s*SprintEvents\s*\)\s*private\s+let\s+" +
            SprintField + @"\s*:\s*Bool\s*;", Rx);
        if (field.Count > 1 || (field.Count == 0 &&
            Has(source, @"\blet\s+" + SprintField + @"\b")))
            return new("REFUSED", null, 0, 0, "RM_SPRINT_FIELD_COLLISION");
        if (assigned &&
            (!Has(arrival, @"\bthis\s*\.\s*" + SprintField +
                @"\s*=\s*PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\(") ||
             arrival.IndexOf(SprintField + " =", StringComparison.Ordinal) <
                wrapper.Index + wrapper.Length))
            return new("REFUSED", null, 0, 0, "RM_SPRINT_ENTRY_CACHE_UNPROVEN");
        if (!assigned && expression is null)
            return new("REFUSED", null, 0, 0, "RM_SPRINT_CACHE_SOURCE_QUERY_MISSING");
        var changes = new List<Edit>();
        if (field.Count == 0)
            changes.Add(new(0, 0,
                "@addField(SprintEvents)" + Newline(source) +
                "private let " + SprintField + ": Bool;" +
                Newline(source) + Newline(source)));
        if (queryEval.Count == 1)
        {
            changes.Add(new(eval.Start + queryEval[0].Index,
                eval.Start + queryEval[0].Index + queryEval[0].Length, ""));
            var use = originalUseEval[0];
            changes.Add(new(eval.Start + use.Index, eval.Start + use.Index + use.Length,
                "if this." + SprintField));
        }
        if (queryEnter.Count == 1)
        {
            changes.Add(new(enter.Start + queryEnter[0].Index,
                enter.Start + queryEnter[0].Index + queryEnter[0].Length, ""));
            var use = originalUseEnter[0];
            changes.Add(new(enter.Start + use.Index,
                enter.Start + use.Index + use.Length,
                "if this." + SprintField + " &&"));
        }
        if (!assigned)
        {
            changes.Add(new(enter.Start + wrapper.Index + wrapper.Length,
                enter.Start + wrapper.Index + wrapper.Length,
                Newline(source) + "  this." + SprintField + " = " +
                expression + ";"));
        }
        if (changes.Count == 0)
            return new("COMPLETE", null, 0, 4, null);
        string updated = Apply(source, changes);
        if (DoubleSprint(updated).Status != "COMPLETE")
            throw new InvalidDataException("RM_SPRINT_POSTCONDITION_FAILED");
        return new("APPLIED", updated, changes.Count,
            (field.Count > 0 ? 1 : 0) + (cachedEval ? 1 : 0) +
            (cachedEnter ? 1 : 0) + (assigned ? 1 : 0), null);
    }


    private static readonly Regex DirectionLocal = new(
        @"\blet\s+hasSprintDirectionPerk\s*:\s*Bool\s*=\s*(?<expr>PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\([^;]+?)\s*;",
        Rx);
    private const string DirectionField = "m_gredRunningManSprintDirectionPerk";
    private const string DirectionValid = "m_gredRunningManSprintDirectionValid";

    private static Decision SprintDirection(string source)
    {
        var stand = Find(source, "SprintDecisions", "ToStand", "replaceMethod");
        var enter = Find(source, "SprintDecisions", "EnterCondition", "replaceMethod");
        if (stand is null || enter is null)
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_METHODS_MISSING");
        string standBody = source[stand.Start..stand.End],
            enterBody = source[enter.Start..enter.End];
        var oldStand = DirectionLocal.Matches(standBody);
        var oldEnter = DirectionLocal.Matches(enterBody);
        var standUse = Regex.Matches(standBody, @"\bif\s+hasSprintDirectionPerk\s*\{", Rx);
        var enterUse = Regex.Matches(enterBody, @"\bif\s+hasSprintDirectionPerk\s*\{", Rx);
        bool standby = Has(standBody,
            @"\blet\s+hasSprintDirectionPerk\s*:\s*Bool\s*=\s*this\s*\.\s*" +
            DirectionField + @"\s*;") &&
            Has(standBody, @"\bif\s*!\s*this\s*\.\s*" + DirectionValid +
                @"\s*\{\s*hasSprintDirectionPerk\s*=\s*PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\(");
        bool assigned = Has(enterBody, @"\bthis\s*\.\s*" + DirectionField +
            @"\s*=\s*PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\(");
        bool validSet = Has(enterBody, @"\bthis\s*\.\s*" + DirectionValid + @"\s*=\s*true\s*;");
        bool cachedEnter = Has(enterBody, @"\bif\s+this\s*\.\s*" + DirectionField + @"\s*\{");
        if (oldStand.Count != (standby ? 0 : 1) ||
            oldEnter.Count != (assigned ? 0 : 1) ||
            standUse.Count != 1 || enterUse.Count != (cachedEnter ? 0 : 1) ||
            (assigned && !cachedEnter) || (validSet && !assigned))
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_PERK_FLOW_INCOMPATIBLE");

        string? expression = oldEnter.Count == 1 ?
            oldEnter[0].Groups["expr"].Value.Trim() :
            oldStand.Count == 1 ? oldStand[0].Groups["expr"].Value.Trim() : null;
        if (expression is not null &&
            (!Has(expression, @"Body_Left_Milestone_1") || !Has(expression, @">\s*0\s*$")))
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_PERK_ID_CHANGED");
        if (oldStand.Count == 1 && oldEnter.Count == 1 &&
            Regex.Replace(oldStand[0].Groups["expr"].Value, @"\s+", "") !=
            Regex.Replace(oldEnter[0].Groups["expr"].Value, @"\s+", ""))
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_PERK_QUERIES_DIFFER");

        // The author's EnterCondition is absent when EasierControllerSprint
        // is installed. In that configuration, ToStand must keep querying
        // the original live perk predicate rather than trust an uninitialized
        // cache. The optional conditional must be verified in this source.
        if (!Has(source, @"@\s*if\s*\(\s*!\s*ModuleExists\s*\(\s*""EasierControllerSprint""\s*\)\s*\)"))
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_OPTIONAL_ENTRY_CONDITION_UNKNOWN");
        if (!standby && expression is null)
            return new("REFUSED", null, 0, 0, "RM_DIRECTION_PERK_QUERY_UNAVAILABLE");

        var edits = new List<Edit>();
        int complete = 0;
        foreach (string fieldName in new[] { DirectionField, DirectionValid })
        {
            var decl = Regex.Matches(source,
                @"@\s*addField\s*\(\s*SprintDecisions\s*\)\s*private\s+let\s+" +
                fieldName + @"\s*:\s*Bool\s*;", Rx);
            if (decl.Count > 1 || (decl.Count == 0 &&
                Has(source, @"\blet\s+" + fieldName + @"\b")))
                return new("REFUSED", null, 0, complete,
                    "RM_DIRECTION_FIELD_COLLISION:" + fieldName);
            if (decl.Count == 0)
                edits.Add(new(0, 0, "@addField(SprintDecisions)" + Newline(source) +
                    "private let " + fieldName + ": Bool;" +
                    Newline(source) + Newline(source)));
            else complete++;
        }

        if (oldStand.Count == 1)
        {
            var old = oldStand[0];
            string replacement =
                "let hasSprintDirectionPerk: Bool = this." + DirectionField + ";" +
                Newline(source) +
                "  if !this." + DirectionValid + " {" + Newline(source) +
                "    hasSprintDirectionPerk = " + old.Groups["expr"].Value.Trim() +
                ";" + Newline(source) + "  };";
            edits.Add(new(stand.Start + old.Index,
                stand.Start + old.Index + old.Length, replacement));
        }
        else complete++;

        if (oldEnter.Count == 1)
        {
            var old = oldEnter[0];
            string replacement =
                "this." + DirectionField + " = " + old.Groups["expr"].Value.Trim() +
                ";" + (validSet ? "" : Newline(source) +
                "  this." + DirectionValid + " = true;");
            edits.Add(new(enter.Start + old.Index,
                enter.Start + old.Index + old.Length, replacement));
        }
        else complete++;

        if (!validSet && oldEnter.Count == 0 && assigned)
        {
            var assignment = Regex.Match(enterBody,
                @"\bthis\s*\.\s*" + DirectionField +
                @"\s*=\s*PlayerDevelopmentSystem\s*\.\s*GetInstance\s*\([^;]+?\s*;", Rx);
            if (!assignment.Success)
                return new("REFUSED", null, 0, complete,
                    "RM_DIRECTION_ASSIGNMENT_PARTIAL_UNPROVEN");
            edits.Add(new(enter.Start + assignment.Index + assignment.Length,
                enter.Start + assignment.Index + assignment.Length,
                Newline(source) + "  this." + DirectionValid + " = true;"));
        }
        else if (validSet) complete++;

        if (enterUse.Count == 1)
        {
            var use = enterUse[0];
            edits.Add(new(enter.Start + use.Index, enter.Start + use.Index + use.Length,
                "if this." + DirectionField + " {"));
        }
        else complete++;
        if (edits.Count == 0)
            return new("COMPLETE", null, 0, complete, null);
        string changed = Apply(source, edits);
        if (SprintDirection(changed).Status != "COMPLETE")
            throw new InvalidDataException("RM_DIRECTION_POSTCONDITION_FAILED");
        return new("APPLIED", changed, edits.Count, complete, null);
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("PASS7_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(basePreviewRoot, output) ||
             Inside(output, basePreviewRoot)))
            throw new InvalidDataException("PASS7_BASE_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string before = admission.SourceStackSha256 ??
            throw new InvalidDataException("PASS7_SOURCE_HASH_MISSING");
        var operationNames = new[]
        {
            ("NCR_SPAWN_EARLY_CULL_GUARDS_V1", "NightCityRemembers",
                "NCR_CullChanceForTier", (Func<string, Decision>)SpawnCulling),
            ("STEALTH_RUNNER_QUEST_DETECTION_GATE_V1", "StealthRunner",
                "OnPlayerDetectionChangedEvent", (Func<string, Decision>)StealthDetection),
            ("RUNNING_MAN_SPRINT_ENTRY_PERK_CACHE_V1", "RunningMan",
                "EvaluateTwoStepSprint", (Func<string, Decision>)DoubleSprint),
            ("RUNNING_MAN_SPRINT_DIRECTION_PERK_CACHE_V1", "RunningMan",
                "@replaceMethod(SprintDecisions)", (Func<string, Decision>)SprintDirection)
        };
        var statuses = new List<Operation>();
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, owner, signature, rewrite) in operationNames)
        {
            string directory = Path.Combine(scripts, owner);
            if (!Directory.Exists(directory))
            {
                statuses.Add(new(id, "NOT_APPLICABLE", 0, 0, null, null));
                continue;
            }
            var files = Directory.EnumerateFiles(directory, "*.reds",
                SearchOption.AllDirectories)
                .Where(f => File.ReadAllText(f, Encoding.UTF8)
                    .Contains(signature, StringComparison.Ordinal)).ToArray();
            if (files.Length != 1)
            {
                statuses.Add(new(id, "REFUSED", 0, 0, null,
                    files.Length == 0 ? "PASS7_OWNER_TARGET_MISSING" :
                    "PASS7_OWNER_TARGET_AMBIGUOUS"));
                continue;
            }
            string path = Path.GetFullPath(files[0]);
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("PASS7_SOURCE_LINK_UNSAFE");
            string relative = "r6/scripts/" +
                Path.GetRelativePath(scripts, path).Replace('\\', '/');
            string source = File.ReadAllText(path, Encoding.UTF8);
            if (basePreviewRoot is not null)
            {
                string fromPreview = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, relative.Replace('/',
                        Path.DirectorySeparatorChar)));
                if (!Inside(fromPreview, basePreviewRoot))
                    throw new InvalidDataException("PASS7_OVERLAY_ESCAPE");
                if (File.Exists(fromPreview))
                    source = File.ReadAllText(fromPreview, Encoding.UTF8);
            }
            var decision = rewrite(source);
            if (decision.Changed is not null)
            {
                foreach (Match marker in Regex.Matches(source,
                    @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+", Rx))
                    if (!decision.Changed.Contains(marker.Value,
                        StringComparison.Ordinal))
                        throw new InvalidDataException("PASS7_GENERIC_LOST");
                staged.Add(relative, decision.Changed);
            }
            statuses.Add(new(id, decision.Status, decision.Edits,
                decision.Complete, relative, decision.Reason));
        }
        if (!string.Equals(before, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PASS7_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach (var file in staged)
        {
            string dest = Path.GetFullPath(Path.Combine(output,
                file.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dest, output) || !dest.EndsWith(".reds",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PASS7_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, file.Value, new UTF8Encoding(false));
        }
        var result = new Result(RuleId, statuses.Sum(x => x.AppliedEdits),
            statuses.ToArray(), true, false);
        File.WriteAllText(Path.Combine(output, "pass7-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }
}
