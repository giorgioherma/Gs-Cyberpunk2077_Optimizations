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

    private static Decision StealthPlayerHandle(string source)
    {
        var init = Find(source, "NPCInitTask", "OnActivate", "wrapMethod");
        var detach = Find(source, "NPCPuppet", "OnDetach", "wrapMethod");
        var detection = Find(source, "NPCPuppet",
            "OnPlayerDetectionChangedEvent", "wrapMethod");
        if (init is null || detach is null || detection is null)
            return new("REFUSED", null, 0, 0, "SR_PLAYER_LIFECYCLE_MISSING");
        string onInit = source[init.Start..init.End],
            onDetach = source[detach.Start..detach.End],
            onDetect = source[detection.Start..detection.End];
        if (!Has(onInit, @"\bAIBehaviorScriptBase\s*\.\s*GetNPCPuppet\s*\(\s*context\s*\)") ||
            !Has(onInit, @"\bStealthRunnerQuestEvent\s*\.\s*NPCInitEvent\s*\(\s*npc\s*,\s*player\s*\)") ||
            !Has(onInit, @"\bwrappedMethod\s*\(\s*context\s*\)") ||
            !Has(onDetach, @"\bwrappedMethod\s*\(\s*\)") ||
            !Has(onDetach, @"\bUnregisterEnemyEvent\s*\(") ||
            !Has(onDetect, @"\bwrappedMethod\s*\(\s*evt\s*\)"))
            return new("REFUSED", null, 0, 0, "SR_PLAYER_LIFECYCLE_FLOW_INCOMPATIBLE");

        const string fieldName = "m_stealthRunnerPlayer";
        var fields = Regex.Matches(source,
            @"@\s*addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+" +
            fieldName + @"\s*:\s*wref\s*<\s*PlayerPuppet\s*>\s*;", Rx);
        if (fields.Count > 1 || (fields.Count == 0 &&
            Has(source, @"\blet\s+" + fieldName + @"\b")))
            return new("REFUSED", null, 0, 0, "SR_PLAYER_CACHE_FIELD_COLLISION");

        var playerInit = Regex.Match(onInit,
            @"\blet\s+player\s*:\s*wref\s*<\s*PlayerPuppet\s*>\s*=\s*" +
            @"GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*npc\s*\.\s*GetGame\s*\(\s*\)\s*\)" +
            @"\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s+as\s+PlayerPuppet\s*;", Rx);
        var oldPlayer = Regex.Match(onDetect,
            @"\blet\s+player\s*:\s*ref\s*<\s*PlayerPuppet\s*>\s*=\s*" +
            @"GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*this\s*\.\s*GetGame\s*\(\s*\)\s*\)" +
            @"\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s+as\s+PlayerPuppet\s*;", Rx);
        bool initCached = Has(onInit,
            @"\bnpc\s*\.\s*" + fieldName + @"\s*=\s*player\s*;");
        bool cleared = Has(onDetach,
            @"\bthis\s*\.\s*" + fieldName + @"\s*=\s*null\s*;");
        bool detectionCached = Has(onDetect,
            @"\blet\s+player\s*:\s*wref\s*<\s*PlayerPuppet\s*>\s*=\s*this\s*\.\s*" +
            fieldName + @"\s*;") &&
            Has(onDetect, @"\bif\s+!\s*IsDefined\s*\(\s*player\s*\)\s*\{" +
                @"\s*player\s*=\s*GameInstance\s*\.\s*GetPlayerSystem\s*\(") &&
            Has(onDetect, @"\bthis\s*\.\s*" + fieldName + @"\s*=\s*player\s*;");
        if (!playerInit.Success ||
            (initCached && onInit.IndexOf("npc." + fieldName, StringComparison.Ordinal) <
                playerInit.Index + playerInit.Length) ||
            (!detectionCached && !oldPlayer.Success) ||
            (detectionCached && oldPlayer.Success) ||
            (detectionCached && Regex.Matches(onDetect,
                @"\blet\s+player\s*:", Rx).Count != 1))
            return new("REFUSED", null, 0, 0, "SR_PLAYER_OWNER_OR_CACHE_FLOW_UNPROVEN");

        var edits = new List<Edit>();
        int complete = 0;
        if (fields.Count == 0)
        {
            var module = Regex.Match(source, @"\bmodule\s+StealthRunner\s*\r?\n", Rx);
            if (!module.Success)
                return new("REFUSED", null, 0, 0, "SR_PLAYER_MODULE_ANCHOR_MISSING");
            edits.Add(new(module.Index + module.Length, module.Index + module.Length,
                "@addField(NPCPuppet)" + Newline(source) +
                "private let " + fieldName + ": wref<PlayerPuppet>;" +
                Newline(source)));
        }
        else complete++;
        if (!initCached)
        {
            edits.Add(new(init.Start + playerInit.Index + playerInit.Length,
                init.Start + playerInit.Index + playerInit.Length,
                Newline(source) + "    npc." + fieldName + " = player;"));
        }
        else complete++;
        if (!cleared)
        {
            edits.Add(new(detach.Start, detach.Start,
                Newline(source) + "  this." + fieldName + " = null;"));
        }
        else complete++;
        if (!detectionCached)
        {
            string replacement =
                "let player: wref<PlayerPuppet> = this." + fieldName + ";" +
                Newline(source) + "  if !IsDefined(player) {" +
                Newline(source) +
                "    player = GameInstance.GetPlayerSystem(this.GetGame()).GetLocalPlayerMainGameObject() as PlayerPuppet;" +
                Newline(source) + "    this." + fieldName + " = player;" +
                Newline(source) + "  }";
            edits.Add(new(detection.Start + oldPlayer.Index,
                detection.Start + oldPlayer.Index + oldPlayer.Length,
                replacement));
        }
        else complete++;
        if (edits.Count == 0) return new("COMPLETE", null, 0, complete, null);
        string changed = Apply(source, edits);
        if (StealthPlayerHandle(changed).Status != "COMPLETE")
            throw new InvalidDataException("SR_PLAYER_CACHE_POSTCONDITION_FAILED");
        return new("APPLIED", changed, edits.Count, complete, null);
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


    // On NPC attach, LizziesBDs only needs performer records, but the
    // original wrapper obtains QuestsSystem and three facts for every NPC.
    // Restrict just the author-owned block; leave wrappedMethod and any
    // extra custom code before/after it in their original order.
    private static Decision LizziePerformerGate(string source)
    {
        var attach = Find(source, "NPCPuppet", "OnGameAttached", "wrapMethod");
        var classification = Find(source, "NPCPuppet",
            "LizziesBDsIsEnt", "addMethod");
        if (attach is null)
            return new("REFUSED", null, 0, 0, "LIZZIES_ATTACH_WRAPPER_MISSING");
        string body = source[attach.Start..attach.End];
        // The game-tested historical performer switch has already replaced
        // the helper. Do not create a competing record filter.
        if (classification is null && Has(source,
            @"\blet\s+recordID\s*:\s*TweakDBID\s*=\s*this\s*\.\s*GetRecordID\s*\(") &&
            Has(source, @"\blizziesBDsNPC\s*:\s*Int32") &&
            Has(body, @"\belse\s*\{\s*return\s+result\s*;\s*\}") &&
            Has(body, @"\bthis\s*\.\s*LizziesBDsZmenitObleceni\s*\(") &&
            Has(body, @"\bthis\s*\.\s*LizziesBDsNastavitMoaning\s*\("))
        {
            // The historical cached-index layout must prove ALL seven
            // performer identities before it can count as complete; a
            // partially rewritten markerless switch is not a no-op.
            var ids = Regex.Matches(body,
                @"\brecordID\s*==\s*t""(?<id>Character\.LizziesBDs_Performer(?:_\d+|_Preview)?)""", Rx)
                .Select(m => m.Groups["id"].Value)
                .ToHashSet(StringComparer.Ordinal);
            string[] historical =
            [
                "Character.LizziesBDs_Performer",
                "Character.LizziesBDs_Performer_2",
                "Character.LizziesBDs_Performer_3",
                "Character.LizziesBDs_Performer_4",
                "Character.LizziesBDs_Performer_5",
                "Character.LizziesBDs_Performer_6",
                "Character.LizziesBDs_Performer_Preview"
            ];
            if (historical.All(ids.Contains))
                return new("COMPLETE", null, 0, 1, null);
            return new("REFUSED", null, 0, 0,
                "LIZZIES_HISTORICAL_RECORD_SWITCH_PARTIAL");
        }
        if (classification is null)
            return new("REFUSED", null, 0, 0, "LIZZIES_RECORD_HELPER_MISSING");
        string chooser = source[classification.Start..classification.End];
        var records = Regex.Matches(chooser,
            @"\bthis\s*\.\s*GetRecordID\s*\(\s*\)\s*==\s*" +
            @"(?:TDBID\s*\.\s*Create\s*\(\s*""(?<id>[^""]+)""\s*\)|t""(?<id>[^""]+)"")" +
            @"\s*\{\s*return\s+-?\d+\s*;", Rx)
            .Select(m => m.Groups["id"].Value).ToArray();
        if (records.Length < 1 || records.Distinct(StringComparer.Ordinal).Count() !=
            records.Length ||
            Regex.Matches(chooser, @"\bthis\s*\.\s*GetRecordID\s*\(\s*\)", Rx).Count !=
            records.Length ||
            !Has(chooser, @"\breturn\s+-1\s*;"))
            return new("REFUSED", null, 0, 0, "LIZZIES_PERFORMER_CLASSIFIER_INCOMPATIBLE");

        var prefix = Regex.Match(body,
            @"\bwrappedMethod\s*\(\s*\)\s*;", Rx);
        var crowd = Regex.Match(body,
            @"\bif\s*!\s*this\s*\.\s*IsCrowd\s*\(\s*\)\s*\{", Rx);
        if (!prefix.Success || !crowd.Success ||
            prefix.Index + prefix.Length > crowd.Index ||
            !Has(body[crowd.Index..], @"\bthis\s*\.\s*LizziesBDsIsEnt\s*\(") ||
            !Has(body[crowd.Index..], @"\bGameInstance\s*\.\s*GetQuestsSystem\s*\(") ||
            !Has(body[crowd.Index..], @"\bthis\s*\.\s*LizziesBDsZmenitObleceni\s*\(") ||
            !Has(body[crowd.Index..], @"\bthis\s*\.\s*LizziesBDsNastavitMoaning\s*\("))
            return new("REFUSED", null, 0, 0, "LIZZIES_ATTACH_GAMEPLAY_FLOW_UNPROVEN");

        const string local = "__gcoLizzieRecordID";
        bool localPresent = Has(body, @"\blet\s+" + local +
            @"\s*:\s*TweakDBID\s*=\s*this\s*\.\s*GetRecordID\s*\(\s*\)\s*;");
        bool wrapped = Has(body, @"\bif\s+" + local + @"\s*==\s*t""");
        if (wrapped)
        {
            var guard = Regex.Match(body,
                @"\bif\s+" + local + @"\s*==\s*t""[^""]+""" +
                @"(?:\s*\|\|\s*" + local + @"\s*==\s*t""[^""]+"")*\s*\{", Rx);
            if (!localPresent || !guard.Success || guard.Index > crowd.Index)
                return new("REFUSED", null, 0, 0,
                    "LIZZIES_EXISTING_GATE_STRUCTURE_UNPROVEN");
            var known = Regex.Matches(guard.Value, @"t""(?<id>[^""]+)""", Rx)
                .Select(m => m.Groups["id"].Value).ToHashSet(StringComparer.Ordinal);
            var missing = records.Where(id => !known.Contains(id)).ToArray();
            if (missing.Length == 0)
                return new("COMPLETE", null, 0, 1, null);
            string gateNl = Newline(source);
            string addition = " ||" + gateNl + "    " +
                string.Join(" ||" + gateNl + "    ",
                    missing.Select(id => local + " == t\"" + id + "\""));
            int brace = body.LastIndexOf('{', guard.Index + guard.Length - 1);
            if (brace < guard.Index || brace >= crowd.Index)
                return new("REFUSED", null, 0, 0,
                    "LIZZIES_EXISTING_GATE_BRACE_UNPROVEN");
            string updated = Apply(source,
            [
                new Edit(attach.Start + brace, attach.Start + brace,
                    addition + " ")
            ]);
            if (LizziePerformerGate(updated).Status != "COMPLETE")
                throw new InvalidDataException("LIZZIES_PARTIAL_GATE_POSTCONDITION_FAILED");
            return new("APPLIED", updated, 1, 1, null);
        }
        if (!localPresent && SourceAdmission.ReadTokens(body)
            .Any(t => t.Value == local))
            return new("REFUSED", null, 0, 0, "LIZZIES_RECORD_LOCAL_COLLISION");

        var tokens = SourceAdmission.ReadTokens(body);
        int open = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].Value == "{" &&
                tokens[i].Start >= crowd.Index &&
                tokens[i].Start < crowd.Index + crowd.Length)
            {
                open = i;
                break;
            }
        }
        int close = Close(tokens, open, "{", "}");
        if (close < 0)
            return new("REFUSED", null, 0, 0, "LIZZIES_CROWD_SCOPE_UNBALANCED");
        string nl = Newline(source);
        string predicate = string.Join(" ||" + nl + "    ",
            records.Select(id => local + " == t\"" + id + "\""));
        string header = (localPresent ? "" :
            "  let " + local + ": TweakDBID = this.GetRecordID();" + nl) +
            "  if " + predicate + " {" + nl;
        var edits = new List<Edit>
        {
            new(attach.Start + crowd.Index, attach.Start + crowd.Index, header),
            new(attach.Start + tokens[close].End, attach.Start + tokens[close].End,
                nl + "  };")
        };
        string changed = Apply(source, edits);
        if (LizziePerformerGate(changed).Status != "COMPLETE")
            throw new InvalidDataException("LIZZIES_PERFORMER_POSTCONDITION_FAILED");
        return new("APPLIED", changed, edits.Count, localPresent ? 1 : 0, null);
    }


    // Pass 7.0/7.1: preserve the vanilla reaction callback verbatim. The
    // stimulus filter only gates this mod's additional reinforcement attempt.
    private static Decision ReinforcementStimulus(string source)
    {
        var span = Find(source, "ReactionManagerComponent", "HandleStimEvent", "wrapMethod");
        if (span is null) return new("REFUSED", null, 0, 0,
            "RS_STIMULUS_WRAPPER_MISSING");
        string body = source[span.Start..span.End];
        var wrapped = Regex.Match(body, @"\bwrappedMethod\s*\(\s*stimData\s*\)\s*;", Rx);
        var call = Regex.Match(body,
            @"\breinSystem\s*\.\s*TryCallingReinforcements\s*\(\s*ownerPuppet\s*\)\s*;", Rx);
        if (!wrapped.Success || !call.Success || wrapped.Index >= call.Index ||
            !Has(body, @"\bGetOwnerPuppet\s*\(\s*\)") ||
            !Has(source, @"\bStartCallReinforcement\s*\("))
            return new("REFUSED", null, 0, 0, "RS_STIMULUS_CALL_FLOW_UNPROVEN");

        string nl = Newline(source);
        var evt = Regex.Match(body,
            @"\blet\s+(?<name>\w+)\s*:\s*ref\s*<\s*StimuliEvent\s*>\s*=\s*stimData\s*\.\s*cachedEvt\s*;", Rx);
        bool nullable = Has(body[..call.Index],
            @"\bif\s*!\s*IsDefined\s*\(\s*stimData\s*\)\s*\{\s*return\s*;\s*\}");
        if (!evt.Success)
        {
            if (Has(body, @"\bstimData\s*\.\s*cachedEvt\b") ||
                Has(body, @"\bGetStimType\s*\(\s*\)") ||
                Has(body, @"\bsourceObject\s*\.\s*IsPlayer\s*\("))
                return new("REFUSED", null, 0, 0,
                    "RS_STIMULUS_PARTIAL_EVENT_BINDING_UNPROVEN");
            string addition = nl +
                (nullable ? "" : "    if !IsDefined(stimData) { return; }" + nl) +
                "    let __gcoRsStimEvent: ref<StimuliEvent> = stimData.cachedEvt;" + nl +
                "    if IsDefined(__gcoRsStimEvent) {" + nl +
                "        if !IsDefined(__gcoRsStimEvent.sourceObject) || !__gcoRsStimEvent.sourceObject.IsPlayer() {" + nl +
                "            return;" + nl + "        }" + nl +
                "        switch __gcoRsStimEvent.GetStimType() {" + nl +
                "            case gamedataStimType.CombatHit:" + nl +
                "            case gamedataStimType.Explosion:" + nl +
                "            case gamedataStimType.Gunshot:" + nl +
                "            case gamedataStimType.HijackVehicle:" + nl +
                "            case gamedataStimType.CarryBody:" + nl +
                "            case gamedataStimType.Bullet:" + nl +
                "                break;" + nl +
                "            default:" + nl + "                return;" + nl +
                "        }" + nl + "    }" + nl;
            int insertion = wrapped.Index + wrapped.Length;
            if (nullable)
            {
                // A handwritten partial null check must execute before any
                // cachedEvt dereference. Append the remaining filter after it.
                var firstGuard = Regex.Match(body,
                    @"\bif\s*!\s*IsDefined\s*\(\s*stimData\s*\)\s*\{\s*return\s*;\s*\}", Rx);
                if (!firstGuard.Success || firstGuard.Index < wrapped.Index ||
                    firstGuard.Index > call.Index)
                    return new("REFUSED", null, 0, 0,
                        "RS_STIMULUS_NULL_GUARD_ORDER_UNPROVEN");
                insertion = firstGuard.Index + firstGuard.Length;
            }
            string updated = Apply(source,
            [
                new Edit(span.Start + insertion,
                    span.Start + insertion, addition)
            ]);
            if (ReinforcementStimulus(updated).Status != "COMPLETE")
                throw new InvalidDataException("RS_STIMULUS_POSTCONDITION");
            return new("APPLIED", updated, 1, 0, null);
        }
        if (evt.Index < wrapped.Index + wrapped.Length || evt.Index > call.Index ||
            Regex.Matches(body, @"\bstimData\s*\.\s*cachedEvt\b", Rx).Count != 1)
            return new("REFUSED", null, 0, 0,
                "RS_STIMULUS_EVENT_POSITION_UNPROVEN");

        string v = Regex.Escape(evt.Groups["name"].Value);
        bool player = Has(body[..call.Index],
            @"!\s*IsDefined\s*\(\s*" + v + @"\s*\.\s*sourceObject\s*\)") &&
            Has(body[..call.Index],
                v + @"\s*\.\s*sourceObject\s*\.\s*IsPlayer\s*\(\s*\)") &&
            Has(body[..call.Index], @"\bif\s+IsDefined\s*\(\s*" + v + @"\s*\)");
        var choice = Regex.Match(body,
            @"\bswitch\s*\(?\s*" + v + @"\s*\.\s*GetStimType\s*\(\s*\)\s*\)?\s*\{", Rx);
        bool hasSwitch = choice.Success && choice.Index > evt.Index &&
            choice.Index < call.Index;
        var tokens = SourceAdmission.ReadTokens(body);
        int choiceOpen = -1, choiceClose = -1;
        if (hasSwitch)
        {
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Value == "{" &&
                    tokens[i].Start == choice.Index + choice.Length - 1)
                { choiceOpen = i; break; }
            choiceClose = Close(tokens, choiceOpen, "{", "}");
            if (choiceClose < 0) return new("REFUSED", null, 0, 0,
                "RS_STIMULUS_SWITCH_UNBALANCED");
        }
        string switchBody = hasSwitch
            ? body[tokens[choiceOpen].End..tokens[choiceClose].Start] : "";
        bool defaultExit = Has(switchBody,
            @"\bdefault\s*:\s*return\s*;");
        if (hasSwitch && !defaultExit)
            return new("REFUSED", null, 0, 0,
                "RS_STIMULUS_CUSTOM_SWITCH_DEFAULT_UNPROVEN");
        string[] expected =
        [
            "CombatHit", "Explosion", "Gunshot",
            "HijackVehicle", "CarryBody", "Bullet"
        ];
        var found = Regex.Matches(switchBody,
            @"\bcase\s+gamedataStimType\s*\.\s*(?<name>\w+)\s*:", Rx)
            .Select(m => m.Groups["name"].Value).ToArray();
        if (hasSwitch && found.Except(expected, StringComparer.Ordinal).Any())
            return new("REFUSED", null, 0, 0,
                "RS_STIMULUS_CUSTOM_STIMULUS_SET_UNPROVEN");
        if (nullable && player && hasSwitch &&
            expected.All(x => found.Count(y => y == x) == 1))
            return new("COMPLETE", null, 0, 3, null);

        var edits = new List<Edit>();
        if (!nullable)
            edits.Add(new(span.Start + wrapped.Index + wrapped.Length,
                span.Start + wrapped.Index + wrapped.Length,
                nl + "    if !IsDefined(stimData) { return; }"));
        if (!player)
        {
            if (Has(body[..call.Index], @"\bsourceObject\s*\.\s*IsPlayer\s*\("))
                return new("REFUSED", null, 0, 0,
                    "RS_STIMULUS_EXISTING_SOURCE_GUARD_UNPROVEN");
            edits.Add(new(span.Start + evt.Index + evt.Length,
                span.Start + evt.Index + evt.Length,
                nl + "    if IsDefined(" + evt.Groups["name"].Value + ") {" + nl +
                "        if !IsDefined(" + evt.Groups["name"].Value + ".sourceObject) || !" +
                evt.Groups["name"].Value + ".sourceObject.IsPlayer() { return; }" +
                nl + "    }"));
        }
        if (!hasSwitch)
        {
            if (Has(body[..call.Index], @"\bGetStimType\s*\("))
                return new("REFUSED", null, 0, 0,
                    "RS_STIMULUS_EXISTING_TYPE_GATE_UNPROVEN");
            string cases = string.Join(nl, expected.Select(x =>
                "            case gamedataStimType." + x + ":"));
            edits.Add(new(span.Start + evt.Index + evt.Length,
                span.Start + evt.Index + evt.Length,
                nl + "    if IsDefined(" + evt.Groups["name"].Value + ") {" +
                nl + "        switch " + evt.Groups["name"].Value +
                ".GetStimType() {" + nl + cases + nl +
                "                break;" + nl +
                "            default: return;" + nl +
                "        }" + nl + "    }"));
        }
        else
        {
            var missing = expected.Where(x => !found.Contains(x,
                StringComparer.Ordinal)).ToArray();
            if (found.Length != found.Distinct(StringComparer.Ordinal).Count())
                return new("REFUSED", null, 0, 0,
                    "RS_STIMULUS_DUPLICATE_TYPE_CASE");
            if (missing.Length > 0)
            {
                var def = Regex.Match(switchBody, @"\bdefault\s*:", Rx);
                if (!def.Success)
                    return new("REFUSED", null, 0, 0,
                        "RS_STIMULUS_DEFAULT_POSITION_UNPROVEN");
                int at = tokens[choiceOpen].End + def.Index;
                edits.Add(new(span.Start + at, span.Start + at,
                    string.Join(nl, missing.Select(x =>
                        "            case gamedataStimType." + x + ":")) +
                    nl + "                break;" + nl + "            "));
            }
        }
        if (edits.Count == 0)
            return new("REFUSED", null, 0, 0,
                "RS_STIMULUS_PARTIAL_FLOW_UNPROVEN");
        // Insertions at the same anchor are combined deterministically.
        var combined = edits.GroupBy(x => x.Start).Select(g =>
            new Edit(g.Key, g.Key, string.Concat(g.Select(x => x.Text))));
        string changed = Apply(source, combined);
        if (ReinforcementStimulus(changed).Status != "COMPLETE")
            throw new InvalidDataException("RS_STIMULUS_PARTIAL_POSTCONDITION");
        return new("APPLIED", changed, edits.Count, 0, null);
    }

    private static Match ReinforcementOwner(string body) => Regex.Match(body,
        @"(?:\blet\s+ownerPuppet\s*:\s*(?:ref|wref)\s*<\s*ScriptedPuppet\s*>\s*=\s*|\bownerPuppet\s*=\s*)this\s*\.\s*GetOwnerPuppet\s*\(\s*\)\s*;", Rx);

    private static Decision ReinforcementPrevention(string source)
    {
        var span = Find(source, "ReactionManagerComponent", "HandleStimEvent", "wrapMethod");
        if (span is null) return new("REFUSED", null, 0, 0,
            "RS_PREVENTION_WRAPPER_MISSING");
        string body = source[span.Start..span.End];
        var owner = ReinforcementOwner(body);
        var stat = Regex.Match(body, @"\bGameInstance\s*\.\s*GetStatsSystem\s*\(", Rx);
        var call = Regex.Match(body, @"\bTryCallingReinforcements\s*\(", Rx);
        var wrapped = Regex.Match(body, @"\bwrappedMethod\s*\(\s*stimData\s*\)", Rx);
        if (!owner.Success || !stat.Success || !call.Success || !wrapped.Success ||
            !(wrapped.Index < owner.Index && owner.Index < stat.Index &&
              stat.Index < call.Index))
            return new("REFUSED", null, 0, 0,
                "RS_PREVENTION_OWNER_AND_STAT_ORDER_UNPROVEN");
        string region = body[(owner.Index + owner.Length)..stat.Index];
        bool nullGuard = Has(region,
            @"\bif\s*!\s*IsDefined\s*\(\s*ownerPuppet\s*\)\s*\{\s*return\s*;\s*\}");
        bool prevention = Has(region,
            @"\bownerPuppet\s*\.\s*IsPrevention\s*\(\s*\)");
        bool police = Has(region,
            @"\bEquals\s*\(\s*ownerPuppet\s*\.\s*GetAttitudeAgent\s*\(\s*\)\s*\.\s*GetAttitudeGroup\s*\(\s*\)\s*,\s*n""police""\s*\)");
        if (nullGuard && prevention && police)
            return new("COMPLETE", null, 0, 3, null);
        if ((prevention && !Has(region, @"\bif\b")) ||
            (police && !Has(region, @"\bif\b")))
            return new("REFUSED", null, 0, 0,
                "RS_PREVENTION_PARTIAL_CUSTOM_CONDITION_UNPROVEN");
        string nl = Newline(source);
        var missing = new StringBuilder();
        if (!nullGuard) missing.Append(nl).Append(
            "        if !IsDefined(ownerPuppet) { return; }");
        if (!prevention) missing.Append(nl).Append(
            "        if ownerPuppet.IsPrevention() { return; }");
        if (!police) missing.Append(nl).Append(
            "        if Equals(ownerPuppet.GetAttitudeAgent().GetAttitudeGroup(), n\"police\") { return; }");
        // Complete handwritten null/prevention-only guards *after* the
        // existing null check, never before it. That protects mixed partial
        // installations from dereferencing an absent NPC owner.
        int insertion = owner.Index + owner.Length;
        var presentNull = Regex.Match(region,
            @"\bif\s*!\s*IsDefined\s*\(\s*ownerPuppet\s*\)\s*\{\s*return\s*;\s*\}", Rx);
        if (presentNull.Success)
            insertion = Math.Max(insertion,
                owner.Index + owner.Length + presentNull.Index + presentNull.Length);
        var presentPrevention = Regex.Match(region,
            @"\bif\s+ownerPuppet\s*\.\s*IsPrevention\s*\(\s*\)\s*\{\s*return\s*;\s*\}", Rx);
        if (presentPrevention.Success)
            insertion = Math.Max(insertion,
                owner.Index + owner.Length + presentPrevention.Index +
                presentPrevention.Length);
        // A combined historical if has its own safe guard and still counts as
        // present without being rewritten into two duplicate tests.
        string changed = Apply(source,
        [
            new Edit(span.Start + insertion,
                span.Start + insertion, missing.ToString())
        ]);
        if (ReinforcementPrevention(changed).Status != "COMPLETE")
            throw new InvalidDataException("RS_PREVENTION_POSTCONDITION");
        return new("APPLIED", changed,
            (nullGuard ? 0 : 1) + (prevention ? 0 : 1) + (police ? 0 : 1),
            (nullGuard ? 1 : 0) + (prevention ? 1 : 0) + (police ? 1 : 0), null);
    }

    private static Decision ReinforcementBurstThrottle(string source)
    {
        var span = Find(source, "ReactionManagerComponent", "HandleStimEvent", "wrapMethod");
        if (span is null) return new("REFUSED", null, 0, 0,
            "RS_THROTTLE_WRAPPER_MISSING");
        string body = source[span.Start..span.End];
        var owner = ReinforcementOwner(body);
        var stat = Regex.Match(body, @"\bGameInstance\s*\.\s*GetStatsSystem\s*\(", Rx);
        var call = Regex.Match(body, @"\bTryCallingReinforcements\s*\(", Rx);
        if (!owner.Success || !stat.Success || !call.Success ||
            !(owner.Index < stat.Index && stat.Index < call.Index))
            return new("REFUSED", null, 0, 0,
                "RS_THROTTLE_OWNER_AND_STATS_UNPROVEN");
        const string field = "grt_rs_nextReinforcementProbe";
        var fields = Regex.Matches(source,
            @"@\s*addField\s*\(\s*ReactionManagerComponent\s*\)\s*private\s+let\s+" +
            field + @"\s*:\s*Float\s*;", Rx);
        if (fields.Count > 1 || (fields.Count == 0 &&
            Has(source, @"\blet\s+" + field + @"\b")))
            return new("REFUSED", null, 0, 0,
                "RS_THROTTLE_FIELD_COLLISION");
        string region = body[(owner.Index + owner.Length)..stat.Index];
        bool sampled = Has(region,
            @"\bEngineTime\s*\.\s*ToFloat\s*\(\s*GameInstance\s*\.\s*GetEngineTime\s*\(\s*ownerPuppet\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*\)");
        bool guard = Has(region, @"\bif\s+\w+\s*<\s*this\s*\.\s*" + field);
        bool store = Has(region, @"\bthis\s*\.\s*" + field +
            @"\s*=\s*\w+\s*\+\s*0\.10\s*;");
        if (fields.Count == 1 && sampled && guard && store)
            return new("COMPLETE", null, 0, 3, null);
        if ((sampled || guard || store) && !(sampled && guard && store))
            return new("REFUSED", null, 0, 0,
                "RS_THROTTLE_PARTIAL_TIME_FLOW_UNPROVEN");
        string nl = Newline(source);
        var edits = new List<Edit>();
        if (!sampled)
        {
            // Always perform the prevention check before consuming the
            // per-component 100 ms probe window. Do not alter vanilla events.
            if (!Has(region, @"\bownerPuppet\s*\.\s*IsPrevention\s*\(") ||
                !Has(region, @"\bn""police"""))
                return new("REFUSED", null, 0, 0,
                    "RS_THROTTLE_PREVENTION_NOT_PROVEN");
            var police = Regex.Match(region,
                @"\bif\s+Equals\s*\(\s*ownerPuppet\s*\.\s*GetAttitudeAgent\s*\(\s*\)\s*\.\s*GetAttitudeGroup\s*\(\s*\)\s*,\s*n""police""\s*\)\s*\{\s*return\s*;\s*\}", Rx);
            if (!police.Success)
                return new("REFUSED", null, 0, 0,
                    "RS_THROTTLE_POLICE_GATE_NOT_LOCAL");
            int afterPolice = owner.Index + owner.Length + police.Index + police.Length;
            string extra = nl +
                "        let __gcoRsNow: Float = EngineTime.ToFloat(GameInstance.GetEngineTime(ownerPuppet.GetGame()));" + nl +
                "        if __gcoRsNow < this.grt_rs_nextReinforcementProbe &&" + nl +
                "            this.grt_rs_nextReinforcementProbe - __gcoRsNow <= 0.10 {" + nl +
                "            return;" + nl + "        }" + nl +
                "        this.grt_rs_nextReinforcementProbe = __gcoRsNow + 0.10;";
            edits.Add(new(span.Start + afterPolice,
                span.Start + afterPolice, extra));
        }
        if (fields.Count == 0)
        {
            var directive = Regex.Match(source,
                @"@\s*wrapMethod\s*\(\s*ReactionManagerComponent\s*\)\s*protected\s+final\s+func\s+HandleStimEvent\s*\(", Rx);
            if (!directive.Success)
                return new("REFUSED", null, 0, 0,
                    "RS_THROTTLE_FIELD_ANCHOR_UNPROVEN");
            edits.Add(new(directive.Index, directive.Index,
                "@addField(ReactionManagerComponent)" + nl +
                "private let grt_rs_nextReinforcementProbe: Float;" + nl + nl));
        }
        string changed = Apply(source, edits);
        if (ReinforcementBurstThrottle(changed).Status != "COMPLETE")
            throw new InvalidDataException("RS_THROTTLE_POSTCONDITION");
        return new("APPLIED", changed, edits.Count, 0, null);
    }


    // Reuse the existing classifier from appearance, placement and NPC events,
    // rather than copying the archived Pass 7 whole-file index rewrite. The
    // current record is rechecked on EVERY call, so reused NPC/identity changes
    // invalidate immediately; unrelated quest and outfit facts stay live.
    private static Decision LizziePerformerIndex(string source)
    {
        var method = Find(source, "NPCPuppet", "LizziesBDsIsEnt", "addMethod");
        if (method is null)
        {
            // A game-tested historical/full handwritten owner can replace the
            // classifier with an index populated by OnGameAttached. Never add
            // a competing field/cache to that independently complete shape.
            var attached = Find(source, "NPCPuppet", "OnGameAttached", "wrapMethod");
            string attachBody = attached is null ? "" :
                source[attached.Start..attached.End];
            string[] mapped = ["0", "1", "2", "3", "4", "5", "100"];
            var assigned = Regex.Matches(attachBody,
                @"\bthis\s*\.\s*lizziesBDsNPC\s*=\s*(?<id>\d+)\s*;", Rx)
                .Select(m => m.Groups["id"].Value).ToArray();
            if (Has(source, @"@\s*addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+lizziesBDsNPC\s*:\s*Int32") &&
                mapped.All(x => assigned.Contains(x, StringComparer.Ordinal)) &&
                Has(source, @"\bLizziesBDsZmenitObleceni\b") &&
                Has(source, @"\bLizziesBDsNastavitPozici\b") &&
                Has(source, @"\bOnLizziesBDsNPCEvent\b") &&
                Has(source, @"\bthis\s*\.\s*lizziesBDsNPC\b"))
                return new("COMPLETE", null, 0, 1, null);
            return new("REFUSED", null, 0, 0,
                "LIZZIES_INDEX_CLASSIFIER_ABSENT_OR_HISTORICAL_CACHE_UNPROVEN");
        }

        string body = source[method.Start..method.End];
        const string record = "__gcoLizzieCurrentRecord";
        const string recordField = "m_gredLizzieIndexRecord";
        const string valueField = "m_gredLizzieIndexValue";
        const string validField = "m_gredLizzieIndexValid";
        var identities = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Character.LizziesBDs_Performer"] = "0",
            ["Character.LizziesBDs_Performer_2"] = "1",
            ["Character.LizziesBDs_Performer_3"] = "2",
            ["Character.LizziesBDs_Performer_4"] = "3",
            ["Character.LizziesBDs_Performer_5"] = "4",
            ["Character.LizziesBDs_Performer_6"] = "5",
            ["Character.LizziesBDs_Performer_Preview"] = "100"
        };
        // Only classify side-effect-free identity-return branches; a custom
        // helper with other actions cannot be safely memoized.
        var cases = Regex.Matches(body,
            @"(?:(?:this\s*\.\s*GetRecordID\s*\(\s*\))|" + record +
            @")\s*==\s*(?:TDBID\s*\.\s*Create\s*\(\s*""(?<id>[^""]+)""\s*\)|t""(?<id>[^""]+)"")\s*\{(?<branch>[^{}]*)\}", Rx);
        if (cases.Count != identities.Count ||
            cases.Cast<Match>().Select(x => x.Groups["id"].Value)
                .Distinct(StringComparer.Ordinal).Count() != identities.Count ||
            cases.Cast<Match>().Any(x =>
                !identities.TryGetValue(x.Groups["id"].Value, out string? expected) ||
                !Has(x.Groups["branch"].Value,
                    @"\breturn\s+" + expected + @"\s*;")))
            return new("REFUSED", null, 0, 0,
                "LIZZIES_INDEX_RECORD_TO_VALUE_MAPPING_UNPROVEN");
        var other = Regex.Match(body,
            @"\belse\s*\{\s*(?<branch>[^{}]*\breturn\s+-1\s*;[^{}]*)\}", Rx);
        if (!other.Success ||
            Regex.Matches(body, @"\breturn\s+-?\d+\s*;", Rx).Count != 8)
            return new("REFUSED", null, 0, 0,
                "LIZZIES_INDEX_FALLBACK_OR_CUSTOM_RETURN_UNPROVEN");

        string nl = Newline(source);
        var edits = new List<Edit>();
        int already = 0;
        // Treat a pre-existing field individually. Never add a duplicate or
        // reinterpret an unrelated custom field with the same name.
        (string Name, string Type)[] fields =
        [
            (recordField, "TweakDBID"), (valueField, "Int32"),
            (validField, "Bool")
        ];
        var absent = new List<(string Name, string Type)>();
        foreach (var field in fields)
        {
            string rx = @"@\s*addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+" +
                field.Name + @"\s*:\s*" + field.Type + @"\s*;";
            int count = Regex.Matches(source, rx, Rx).Count;
            if (count > 1 || (count == 0 &&
                Has(source, @"\b" + field.Name + @"\b")))
                return new("REFUSED", null, 0, already,
                    "LIZZIES_INDEX_FIELD_COLLISION:" + field.Name);
            if (count == 0) absent.Add(field);
            else already++;
        }
        if (absent.Count > 0)
        {
            var declaration = Regex.Match(source,
                @"@\s*addField\s*\(\s*NPCPuppet\s*\)\s*private\s+let\s+lizziesBDsIsNaked\s*:\s*Bool\s*=\s*false\s*;", Rx);
            if (!declaration.Success)
                return new("REFUSED", null, 0, already,
                    "LIZZIES_INDEX_FIELD_DECLARATION_ANCHOR_MISSING");
            edits.Add(new(declaration.Index + declaration.Length,
                declaration.Index + declaration.Length,
                nl + nl + string.Join(nl + nl,
                    absent.Select(x => "@addField(NPCPuppet)" + nl +
                        "private let " + x.Name + ": " + x.Type + ";"))));
        }

        var current = Regex.Match(body,
            @"\blet\s+" + record +
            @"\s*:\s*TweakDBID\s*=\s*this\s*\.\s*GetRecordID\s*\(\s*\)\s*;", Rx);
        var fast = Regex.Match(body, @"\bif\s+this\s*\.\s*" + validField +
            @"\s*&&\s*this\s*\.\s*" + recordField +
            @"\s*==\s*" + record +
            @"\s*\{\s*return\s+this\s*\.\s*" + valueField + @"\s*;\s*\}", Rx);
        if (current.Success != fast.Success ||
            (current.Success && (current.Index > fast.Index ||
                current.Index > cases[0].Index)) ||
            (!current.Success && Has(body, @"\b" + record + @"\b")))
            return new("REFUSED", null, 0, already,
                "LIZZIES_INDEX_PARTIAL_FASTPATH_UNPROVEN");
        if (!current.Success)
        {
            string header = nl +
                "    let " + record + ": TweakDBID = this.GetRecordID();" + nl +
                "    if this." + validField + " && this." + recordField +
                " == " + record + " {" + nl +
                "        return this." + valueField + ";" + nl +
                "    }" + nl;
            edits.Add(new(method.Start, method.Start, header));
        }
        else already++;

        foreach (var c in cases.Cast<Match>().Append(other))
        {
            string branch = c.Groups["branch"].Value;
            var value = Regex.Match(branch, @"\breturn\s+(?<id>-?\d+)\s*;", Rx);
            if (!value.Success ||
                (c != other && value.Groups["id"].Value !=
                    identities[c.Groups["id"].Value]))
                return new("REFUSED", null, 0, already,
                    "LIZZIES_INDEX_BRANCH_RESULT_DRIFT");
            string v = value.Groups["id"].Value;
            string[] post =
            [
                "this." + valueField + " = " + v + ";",
                "this." + recordField + " = " + record + ";",
                "this." + validField + " = true;"
            ];
            string cleaned = Regex.Replace(branch, @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
            cleaned = Regex.Replace(cleaned, @"\breturn\s+-?\d+\s*;", "");
            foreach (var statement in post)
                cleaned = cleaned.Replace(statement, "", StringComparison.Ordinal);
            if (!string.IsNullOrWhiteSpace(cleaned))
                return new("REFUSED", null, 0, already,
                    "LIZZIES_INDEX_BRANCH_HAS_OTHER_SIDE_EFFECTS");
            string[] missing = post.Where(s =>
                !branch.Contains(s, StringComparison.Ordinal)).ToArray();
            if (missing.Length == 0) { already++; continue; }
            int at = method.Start + c.Groups["branch"].Index + value.Index;
            edits.Add(new(at, at, string.Join(nl + "        ", missing) +
                nl + "        "));
        }

        foreach (var c in cases.Cast<Match>())
        {
            string condition = c.Value[..c.Value.IndexOf('{')];
            var lookup = Regex.Match(condition,
                @"this\s*\.\s*GetRecordID\s*\(\s*\)", Rx);
            if (!lookup.Success) { already++; continue; }
            edits.Add(new(method.Start + c.Index + lookup.Index,
                method.Start + c.Index + lookup.Index + lookup.Length, record));
        }
        if (edits.Count == 0)
            return new("COMPLETE", null, 0, already, null);
        string changed = Apply(source, edits);
        // Verify all cache elements, correct identity mapping and caller reuse.
        var verified = LizziePerformerIndex(changed);
        if (verified.Status != "COMPLETE")
            throw new InvalidDataException("LIZZIES_INDEX_POSTCONDITION:" + verified.Reason);
        return new("APPLIED", changed, edits.Count, already, null);
    }


    // Pass 8's historical 24-entry/100 ms result table has no proven
    // invalidation for perk purchase/reset, equipment-stat and chip-charge
    // mutation, and even caches wrappedMethod's mutable vanilla result.
    // Pass 4's relevance guard and Pass 8's source-local combat cache remain
    // independent and are never superseded by an unsafe competing cache.
    private static Decision ChipwarePerkResultMemoization(string source)
    {
        var method = Find(source, "PlayerDevelopmentData",
            "IsNewPerkBought", "wrapMethod");
        if (method is null)
            return new("REFUSED", null, 0, 0,
                "CHIPWARE_PERK_MEMO_WRAPPER_MISSING");
        string body = source[method.Start..method.End];
        if (!Has(body, @"\bwrappedMethod\s*\(\s*perkType\s*\)") ||
            !Has(source, @"\bgamedataNewPerkType\b"))
            return new("REFUSED", null, 0, 0,
                "CHIPWARE_PERK_FALLBACK_OR_ENUM_UNPROVEN");
        if (Has(body, @"\bTryGetPerkResult\s*\(") ||
            Has(body, @"\bStorePerkResult\s*\("))
            return new("REFUSED", null, 0, 0,
                "CHIPWARE_MEMO_PRESENT_BUT_100MS_RESULT_INVALIDATION_UNPROVEN");
        return new("REFUSED", null, 0, 0,
            "CHIPWARE_24_PERK_RESULT_MEMO_REFUSED_NO_PERK_PURCHASE_EQUIPMENT_CHARGE_OR_WRAPPED_RESULT_INVALIDATION_EPOCH");
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
        // REDscript requires the module declaration before any @addField.
        // Both current and handwritten sources may move the module line
        // behind comments; never prepend fields blindly at byte zero.
        var moduleLine = Regex.Match(source,
            @"^[ \t]*module[ \t]+[A-Za-z_][\w.]*[ \t]*\r?\n", Rx);
        int fieldInsertion = moduleLine.Success ?
            moduleLine.Index + moduleLine.Length : 0;
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
                edits.Add(new(fieldInsertion, fieldInsertion, "@addField(SprintDecisions)" + Newline(source) +
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
            ("STEALTH_RUNNER_PLAYER_HANDLE_CACHE_V1", "StealthRunner",
                "OnPlayerDetectionChangedEvent", (Func<string, Decision>)StealthPlayerHandle),
            ("RUNNING_MAN_SPRINT_ENTRY_PERK_CACHE_V1", "RunningMan",
                "EvaluateTwoStepSprint", (Func<string, Decision>)DoubleSprint),
            ("RUNNING_MAN_SPRINT_DIRECTION_PERK_CACHE_V1", "RunningMan",
                "@replaceMethod(SprintDecisions)", (Func<string, Decision>)SprintDirection),
            ("LIZZIES_BDS_PERFORMER_ATTACH_GATE_V1", "LizziesBDs",
                "LizziesBDsIsEnt", (Func<string, Decision>)LizziePerformerGate),
            ("LIZZIES_BDS_PERFORMER_INDEX_MEMO_V1", "LizziesBDs",
                "LizziesBDsZmenitObleceni", (Func<string, Decision>)LizziePerformerIndex),
            ("REINFORCEMENTS_CHASE_STIMULUS_GATE_V1", "ReinforcementsSystem",
                "HandleStimEvent", (Func<string, Decision>)ReinforcementStimulus),
            ("REINFORCEMENTS_PREVENTION_POLICE_GATE_V1", "ReinforcementsSystem",
                "HandleStimEvent", (Func<string, Decision>)ReinforcementPrevention),
            ("REINFORCEMENTS_100MS_STIMULUS_PROBE_V1", "ReinforcementsSystem",
                "HandleStimEvent", (Func<string, Decision>)ReinforcementBurstThrottle),
            ("CHIPWARE_24_PERK_RESULT_MEMO_V1", "zzy_bChipwareExpansion",
                "@wrapMethod(PlayerDevelopmentData)",
                (Func<string, Decision>)ChipwarePerkResultMemoization)
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
            // Independently discovered operations may share an owner file on
            // a customized installation. Complete each against the current
            // effective bytes instead of creating competing overlay files.
            if (staged.TryGetValue(relative, out string? earlier))
                source = earlier;
            var decision = rewrite(source);
            if (decision.Changed is not null)
            {
                foreach (Match marker in Regex.Matches(source,
                    @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+", Rx))
                    if (!decision.Changed.Contains(marker.Value,
                        StringComparison.Ordinal))
                        throw new InvalidDataException("PASS7_GENERIC_LOST");
                staged[relative] = decision.Changed;
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
