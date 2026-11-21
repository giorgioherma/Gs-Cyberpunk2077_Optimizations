using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using GRedscript.Resolver.Phase2;

if (args.Length == 0)
    throw new InvalidDataException("PASS7_COMMAND_REQUIRED");
if (args[0] == "generate")
{
    if (args.Length != 3 && args.Length != 4)
        throw new InvalidDataException("PASS7_GENERATE_REQUIRES_GAME_OUT_OVERLAY");
    var report = Pass7ChaseFrameFloorPass.Generate(args[1], args[2],
        args.Length == 4 ? args[3] : null);
    Console.WriteLine(JsonSerializer.Serialize(report));
    return;
}
if (args[0] == "selftest")
{
    if (args.Length != 2)
        throw new InvalidDataException("PASS7_SELFTEST_REQUIRES_POSTGENERIC");
    Run(args[1]);
    Console.WriteLine("PASS7_SOURCE_STATE_CONTRACTS_PASS");
    return;
}
throw new InvalidDataException("PASS7_UNKNOWN_COMMAND");

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidDataException("PASS7_ASSERT: " + message);
}
static string Sha(string text) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
static Dictionary<string, string> ReadCurrent(string root)
{
    string scripts = Path.Combine(root, "r6", "scripts");
    var samples = new Dictionary<string, (string Owner, string Signature)>
    {
        ["ncr"] = ("NightCityRemembers", "NCR_CullChanceForTier"),
        ["sr"] = ("StealthRunner", "OnPlayerDetectionChangedEvent"),
        ["rm"] = ("RunningMan", "EvaluateTwoStepSprint"),
        ["dir"] = ("RunningMan", "@replaceMethod(SprintDecisions)"),
        ["lizzies"] = ("LizziesBDs", "LizziesBDsIsEnt"),
        ["rs"] = ("ReinforcementsSystem", "HandleStimEvent"),
        ["chip"] = ("zzy_bChipwareExpansion", "@wrapMethod(PlayerDevelopmentData)")
    };
    var result = new Dictionary<string, string>();
    foreach (var sample in samples)
    {
        string dir = Path.Combine(scripts, sample.Value.Owner);
        Require(Directory.Exists(dir), "CURRENT_OWNER_MISSING:" + sample.Key);
        var files = Directory.EnumerateFiles(dir, "*.reds",
            SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(sample.Value.Signature,
                StringComparison.Ordinal)).ToArray();
        Require(files.Length == 1, "SOURCE_NOT_UNIQUE:" + sample.Key);
        result[sample.Key] = File.ReadAllText(files[0]);
    }
    return result;
}
static string MakeGame(string temp, string label,
    Dictionary<string, string> sources, bool relocate = false)
{
    string game = Path.Combine(temp, label);
    string scripts = Path.Combine(game, "r6", "scripts");
    Directory.CreateDirectory(scripts);
    var paths = new Dictionary<string, string>
    {
        ["ncr"] = "NightCityRemembers/NCR_SpawnCulling.reds",
        ["sr"] = "StealthRunner/npc_puppet.reds",
        ["rm"] = "RunningMan/double_sprint.reds",
        ["dir"] = "RunningMan/sprint_direction.reds",
        ["lizzies"] = "LizziesBDs/NPCPuppet.reds",
        ["rs"] = "ReinforcementsSystem/ReinforcementsCall.reds",
        ["chip"] = "zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds"
    };
    foreach (var entry in sources)
    {
        string rel = paths[entry.Key];
        if (relocate) rel = rel.Replace("/", "/user-relocated/");
        string path = Path.Combine(scripts,
            rel.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, entry.Value, new UTF8Encoding(false));
    }
    if (sources.Count == 0)
    {
        string unrelated = Path.Combine(scripts, "Unrelated", "Unrelated.reds");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelated)!);
        File.WriteAllText(unrelated, "module Unrelated\npublic class UnrelatedSystem {}\n",
            new UTF8Encoding(false));
    }
    return game;
}
static string[] GeneratedFiles(string output) =>
    Directory.EnumerateFiles(output, "*.reds",
        SearchOption.AllDirectories).ToArray();
static void AssertStatus(Pass7ChaseFrameFloorPass.Result report,
    string idPart, string status)
{
    var matches = report.Operations.Where(x =>
        x.Id.Contains(idPart, StringComparison.Ordinal)).ToArray();
    Require(matches.Length == 1 && matches[0].Status == status,
        "STATUS_EXPECTED:" + idPart + ":" + status + " GOT " +
        string.Join(",", matches.Select(x => x.Status + ":" + x.Reason)));
}
static string[] PreservedMarkers(string source) =>
    Regex.Matches(source, @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
        .Select(x => x.Value).Distinct().ToArray();
static void CheckContent(Dictionary<string, string> original, string output,
    bool relocated = false)
{
    var files = GeneratedFiles(output);
    Require(files.Length == 6, "EXPECTED_SIX_OUTPUTS_NO_UNSAFE_CHIPWARE_MEMO");
    foreach (var key in new[] { "ncr", "sr", "rm", "dir", "lizzies", "rs" })
    {
        string owner = key switch
        {
            "ncr" => "NightCityRemembers",
            "sr" => "StealthRunner",
            "lizzies" => "LizziesBDs",
            "rs" => "ReinforcementsSystem",
            _ => "RunningMan"
        };
        var ownerName = key switch
        {
            "ncr" => "NCR_SpawnCulling.reds",
            "sr" => "npc_puppet.reds",
            "rm" => "double_sprint.reds",
            "lizzies" => "NPCPuppet.reds",
            "rs" => "ReinforcementsCall.reds",
            _ => "sprint_direction.reds"
        };
        var candidates = files.Where(f =>
            f.Replace('\\', '/').Contains("/r6/scripts/" + owner + "/", StringComparison.Ordinal) &&
            f.EndsWith(ownerName, StringComparison.Ordinal)).ToArray();
        Require(candidates.Length == 1, "EXPECTED_OWNER_FILE:" + key);
        string content = File.ReadAllText(candidates[0]);
        foreach (string marker in PreservedMarkers(original[key]))
            Require(content.Contains(marker, StringComparison.Ordinal),
                "LOST_GENERIC:" + key + ":" + marker);
        if (relocated)
            Require(content.Contains("user-relocated", StringComparison.Ordinal),
                "RELOCATED_PATH_NOT_USED:" + key);
        if (key == "ncr")
        {
            int getPlayer = content.IndexOf("GetPlayer(gi)", StringComparison.Ordinal);
            int quest = content.IndexOf("if this.IsQuest()", StringComparison.Ordinal);
            int crowd = content.IndexOf("if this.IsCrowd()", StringComparison.Ordinal);
            Require(quest >= 0 && crowd >= 0 && quest < getPlayer &&
                crowd < getPlayer, "NCR_EARLY_GUARDS_NOT_COMPOSED");
            Require(content.Contains("if RandF() >= cullChance",
                StringComparison.Ordinal), "NCR_RNG_CHANGED");
        }
        else if (key == "sr")
        {
            int callback = content.LastIndexOf(
                "func OnPlayerDetectionChangedEvent", StringComparison.Ordinal);
            Require(callback >= 0, "STEALTH_DETECTION_CALLBACK_MISSING");
            string detection = content[callback..];
            int gate = detection.IndexOf("if !IsDefined(this.m_stealthRunnerStatus)",
                StringComparison.Ordinal);
            int gateQuest = detection.IndexOf(
                "if !IsDefined(this.m_stealthRunnerStatus.quest)",
                StringComparison.Ordinal);
            int player = detection.IndexOf("let player:",
                StringComparison.Ordinal);
            Require(gate >= 0 && gateQuest > gate && gateQuest < player,
                "STEALTH_QUEST_GUARD_MISSING");
            Require(content.Contains("wrappedMethod(evt);",
                StringComparison.Ordinal), "STEALTH_WRAPPED_METHOD_LOST");
            Require(Regex.Matches(content,
                @"private let m_stealthRunnerPlayer\s*:\s*wref").Count == 1,
                "STEALTH_PLAYER_CACHE_FIELD_DUPLICATED");
            Require(content.Contains("npc.m_stealthRunnerPlayer = player;",
                StringComparison.Ordinal), "STEALTH_PLAYER_INIT_CACHE_MISSING");
            Require(content.Contains("this.m_stealthRunnerPlayer = null;",
                StringComparison.Ordinal), "STEALTH_PLAYER_DETACH_CLEAR_MISSING");
            Require(content.Contains("let player: wref<PlayerPuppet> = this.m_stealthRunnerPlayer;",
                StringComparison.Ordinal), "STEALTH_PLAYER_CACHED_READ_MISSING");
        }
        else if (key == "lizzies")
        {
            Require(content.Contains("let __gcoLizzieRecordID: TweakDBID = this.GetRecordID();",
                StringComparison.Ordinal), "LIZZIES_RECORD_FAST_GUARD_MISSING");
            Require(content.Contains("if __gcoLizzieRecordID == t\"Character.LizziesBDs_Performer\"",
                StringComparison.Ordinal), "LIZZIES_PERFORMER_LIST_MISSING");
            Require(content.Contains("this.LizziesBDsZmenitObleceni(questsSystem, 1);",
                StringComparison.Ordinal), "LIZZIES_GAMEPLAY_CALLBACK_LOST");
            Require(content.Contains("OnLizziesBDsNPCEvent", StringComparison.Ordinal),
                "LIZZIES_EVENT_CALLBACK_LOST");
            Require(Regex.Matches(content, @"private let m_gredLizzieIndexValid").Count == 1 &&
                Regex.Matches(content, @"private let m_gredLizzieIndexRecord").Count == 1 &&
                Regex.Matches(content, @"private let m_gredLizzieIndexValue").Count == 1,
                "LIZZIES_CACHE_FIELDS_DUPLICATED");
            Require(content.Contains("if this.m_gredLizzieIndexValid &&", StringComparison.Ordinal) &&
                content.Contains("this.m_gredLizzieIndexRecord == __gcoLizzieCurrentRecord", StringComparison.Ordinal),
                "LIZZIES_IDENTITY_INVALIDATION_MISSING");
            Require(Regex.Matches(content, @"this.m_gredLizzieIndexValue = ").Count == 8,
                "LIZZIES_INDEX_MAPPING_INCOMPLETE");
        }
        else if (key == "rs")
        {
            int wrapped = content.IndexOf("wrappedMethod(stimData);", StringComparison.Ordinal);
            int filtered = content.IndexOf("stimData.cachedEvt;", StringComparison.Ordinal);
            int player = content.IndexOf("sourceObject.IsPlayer()", StringComparison.Ordinal);
            int police = content.IndexOf("ownerPuppet.IsPrevention()", StringComparison.Ordinal);
            int throttle = content.IndexOf("grt_rs_nextReinforcementProbe = __gcoRsNow + 0.10", StringComparison.Ordinal);
            int stats = content.IndexOf("GameInstance.GetStatsSystem(ownerPuppet.GetGame())", StringComparison.Ordinal);
            Require(wrapped >= 0 && filtered > wrapped && player > filtered &&
                police > player && throttle > police && stats > throttle,
                "REINFORCEMENTS_STIMULUS_PREVENTION_THROTTLE_ORDER");
            foreach(string stim in new[] { "CombatHit", "Explosion", "Gunshot",
                "HijackVehicle", "CarryBody", "Bullet" })
                Require(content.Contains("case gamedataStimType." + stim + ":",
                    StringComparison.Ordinal), "RS_STIMULUS_MISSING:" + stim);
            Require(content.Contains("n\"police\"", StringComparison.Ordinal) &&
                content.Contains("TryCallingReinforcements(ownerPuppet);", StringComparison.Ordinal),
                "REINFORCEMENTS_POLICE_GUARD_OR_ORIGINAL_CALLBACK_MISSING");
            Require(Regex.Matches(content, @"private let grt_rs_nextReinforcementProbe").Count == 1,
                "REINFORCEMENTS_THROTTLE_FIELD_DUPLICATED");
        }
        else if (key == "rm")
        {
            Require(Regex.Matches(content, @"private let m_gredRunningManDoubleRunPerk").Count == 1,
                "RM_DUPLICATE_CACHE_FIELD");
            Require(Regex.Matches(content, @"IsNewPerkBought").Count == 2,
                "RM_PERK_NOT_SAMPLED_ON_ENTRY_ONCE");
            Require(content.Contains("if this.m_gredRunningManDoubleRunPerk",
                StringComparison.Ordinal), "RM_EVALUATOR_NOT_CACHED");
            Require(content.Contains("SetPermanentBoolParameter",
                StringComparison.Ordinal), "RM_RESUME_TRANSITIONS_CHANGED");
        }
        else
        {
            Require(Regex.Matches(content, @"private let m_gredRunningManSprintDirectionPerk").Count == 1,
                "RM_DIRECTION_DUPLICATE_PERK_FIELD");
            Require(Regex.Matches(content, @"private let m_gredRunningManSprintDirectionValid").Count == 1,
                "RM_DIRECTION_DUPLICATE_VALID_FIELD");
            int module = content.IndexOf("module RunningMan", StringComparison.Ordinal);
            int cacheField = content.IndexOf("@addField(SprintDecisions)",
                StringComparison.Ordinal);
            Require(module >= 0 && cacheField > module,
                "RM_DIRECTION_FIELDS_PRECEDE_MODULE_DECLARATION");
            Require(content.Contains("if !this.m_gredRunningManSprintDirectionValid",
                StringComparison.Ordinal), "RM_DIRECTION_OPTIONAL_MODULE_FALLBACK_LOST");
            Require(content.Contains("@if(!ModuleExists(\"EasierControllerSprint\"))",
                StringComparison.Ordinal), "RM_DIRECTION_MODULE_EXCLUSION_LOST");
            Require(Regex.Matches(content, @"IsNewPerkBought").Count ==
                Regex.Matches(original[key], @"IsNewPerkBought").Count,
                "RM_DIRECTION_OTHER_AUTHOR_PERK_QUERIES_LOST");
        }
    }
}
static void Run(string baseline)
{
    var originals = ReadCurrent(baseline);
    string temp = Path.Combine(Path.GetTempPath(),
        "gco-pass7-contract-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(temp);
    try
    {
        string originalSha = Sha(string.Join("|",
            originals.OrderBy(x => x.Key).Select(x => x.Value)));
        var game = MakeGame(temp, "stock", originals);
        string result = Path.Combine(temp, "generated");
        var report = Pass7ChaseFrameFloorPass.Generate(game, result);
        Require(report.AppliedEdits >= 7, "STOCK_BATCH_EDITS");
        foreach (string id in new[] { "NCR_SPAWN", "STEALTH_RUNNER_QUEST_DETECTION", "STEALTH_RUNNER_PLAYER_HANDLE", "RUNNING_MAN_SPRINT_ENTRY", "RUNNING_MAN_SPRINT_DIRECTION", "LIZZIES_BDS_PERFORMER_ATTACH", "LIZZIES_BDS_PERFORMER_INDEX", "REINFORCEMENTS_CHASE_STIMULUS", "REINFORCEMENTS_PREVENTION", "REINFORCEMENTS_100MS" })
            AssertStatus(report, id, "APPLIED");
        CheckContent(originals, result);
        AssertStatus(report, "CHIPWARE_24_PERK", "REFUSED");
        Require(Sha(string.Join("|", ReadCurrent(game)
            .OrderBy(x => x.Key).Select(x => x.Value))) == originalSha,
            "STOCK_SOURCE_MUTATED");

        // Full handwritten equivalent, even without a marker, is a no-op.
        var completed = new Dictionary<string, string>();
        foreach (var file in GeneratedFiles(result))
        {
            string norm = file.Replace('\\', '/');
            string key = norm.Contains("/NightCityRemembers/") ? "ncr" :
                norm.Contains("/StealthRunner/") ? "sr" :
                norm.EndsWith("/sprint_direction.reds", StringComparison.Ordinal) ?
                "dir" : norm.EndsWith("/LizziesBDs/NPCPuppet.reds", StringComparison.Ordinal) ?
                "lizzies" : norm.Contains("/ReinforcementsSystem/") ?
                "rs" : "rm";
            completed[key] = File.ReadAllText(file);
        }
        completed["chip"] = originals["chip"];
        var fullGame = MakeGame(temp, "complete", completed);
        var fullReport = Pass7ChaseFrameFloorPass.Generate(fullGame,
            Path.Combine(temp, "full-out"));
        Require(fullReport.AppliedEdits == 0 &&
            GeneratedFiles(Path.Combine(temp, "full-out")).Length == 0,
            "FULL_SOURCE_MUST_BE_NOOP");
        foreach (var id in new[] { "NCR_SPAWN", "STEALTH_RUNNER_QUEST_DETECTION", "STEALTH_RUNNER_PLAYER_HANDLE", "RUNNING_MAN_SPRINT_ENTRY", "RUNNING_MAN_SPRINT_DIRECTION", "LIZZIES_BDS_PERFORMER_ATTACH", "LIZZIES_BDS_PERFORMER_INDEX", "REINFORCEMENTS_CHASE_STIMULUS", "REINFORCEMENTS_PREVENTION", "REINFORCEMENTS_100MS" })
            AssertStatus(fullReport, id, "COMPLETE");
        AssertStatus(fullReport, "CHIPWARE_24_PERK", "REFUSED");

        // Repeat on staged effective source, without modifying original source.
        var repeated = Pass7ChaseFrameFloorPass.Generate(game,
            Path.Combine(temp, "repeat"), result);
        Require(repeated.AppliedEdits == 0 &&
            GeneratedFiles(Path.Combine(temp, "repeat")).Length == 0,
            "REPEATED_GENERATION_CHANGED_SOURCE");

        // Markerless, manually partial: move one NCR guard early, insert only
        // the Stealth status test, and pre-add the RunningMan owner field.
        var partial = new Dictionary<string, string>(originals);
        string q = "    if this.IsQuest() { return result; };";
        Require(partial["ncr"].Contains(q), "NCR_PARTIAL_FIXTURE_CHANGED");
        partial["ncr"] = partial["ncr"].Replace(q, "", StringComparison.Ordinal)
            .Replace("    let player: ref<PlayerPuppet> = GetPlayer(gi);",
                q + "\n    let player: ref<PlayerPuppet> = GetPlayer(gi);",
                StringComparison.Ordinal);
        partial["sr"] = partial["sr"].Replace(
            "module StealthRunner\n",
            "module StealthRunner\n@addField(NPCPuppet)\nprivate let m_stealthRunnerPlayer: wref<PlayerPuppet>;\n",
            StringComparison.Ordinal).Replace("  wrappedMethod(evt);",
                "  wrappedMethod(evt);\n  if !IsDefined(this.m_stealthRunnerStatus) { return false; }",
                StringComparison.Ordinal);
        partial["rm"] = "@addField(SprintEvents)\nprivate let m_gredRunningManDoubleRunPerk: Bool;\n\n"
            + partial["rm"];
        partial["dir"] = partial["dir"].Replace("\r\n", "\n",
            StringComparison.Ordinal).Replace("module RunningMan\n",
            "module RunningMan\n@addField(SprintDecisions)\n" +
            "private let m_gredRunningManSprintDirectionPerk: Bool;\n",
            StringComparison.Ordinal);
        partial["rs"] = partial["rs"].Replace(
            "wrappedMethod(stimData);",
            "wrappedMethod(stimData);\n\tif !IsDefined(stimData) { return; }",
            StringComparison.Ordinal).Replace("@wrapMethod(ReactionManagerComponent)",
            "@addField(ReactionManagerComponent)\nprivate let grt_rs_nextReinforcementProbe: Float;\n\n@wrapMethod(ReactionManagerComponent)",
            StringComparison.Ordinal);
        partial["lizzies"] = partial["lizzies"].Replace(
            "\tif !this.IsCrowd() {",
            "\tlet __gcoLizzieRecordID: TweakDBID = this.GetRecordID();\n\tif !this.IsCrowd() {",
            StringComparison.Ordinal);
        var partialReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "partial", partial), Path.Combine(temp, "partial-out"));
        foreach (var id in new[] { "NCR_SPAWN", "STEALTH_RUNNER_QUEST_DETECTION", "STEALTH_RUNNER_PLAYER_HANDLE", "RUNNING_MAN_SPRINT_ENTRY", "RUNNING_MAN_SPRINT_DIRECTION", "LIZZIES_BDS_PERFORMER_ATTACH", "LIZZIES_BDS_PERFORMER_INDEX", "REINFORCEMENTS_CHASE_STIMULUS", "REINFORCEMENTS_PREVENTION", "REINFORCEMENTS_100MS" })
            AssertStatus(partialReport, id, "APPLIED");
        CheckContent(partial, Path.Combine(temp, "partial-out"));
        AssertStatus(partialReport, "CHIPWARE_24_PERK", "REFUSED");

        // Manually pre-added one out of three performer cache fields: only
        // missing fields and original return-path stores may be emitted.
        var partialIndex = new Dictionary<string, string>(originals);
        partialIndex["lizzies"] = partialIndex["lizzies"].Replace(
            "private let lizziesBDsIsNaked: Bool = false;",
            "private let lizziesBDsIsNaked: Bool = false;\n\n" +
            "@addField(NPCPuppet)\nprivate let m_gredLizzieIndexRecord: TweakDBID;",
            StringComparison.Ordinal);
        var indexReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "partial-index-field", partialIndex),
            Path.Combine(temp, "partial-index-field-out"));
        AssertStatus(indexReport, "LIZZIES_BDS_PERFORMER_INDEX", "APPLIED");
        string indexed = File.ReadAllText(GeneratedFiles(
            Path.Combine(temp, "partial-index-field-out"))
            .Single(x => x.Contains("LizziesBDs", StringComparison.Ordinal)));
        Require(Regex.Matches(indexed,
            @"private let m_gredLizzieIndexRecord").Count == 1,
            "LIZZIES_PARTIAL_DUPLICATED_EXISTING_FIELD");

        // A pre-existing stimulus selector with a missing allowed case, and
        // a partial police guard, are completed without touching already
        // present throttling or the other five owners.
        var partialChase = new Dictionary<string, string>(completed);
        partialChase["rs"] = partialChase["rs"]
            .Replace("case gamedataStimType.Bullet:", "", StringComparison.Ordinal)
            .Replace("if Equals(ownerPuppet.GetAttitudeAgent().GetAttitudeGroup(), n\"police\") { return; }",
                "", StringComparison.Ordinal);
        var chaseReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "partial-stimulus-police", partialChase),
            Path.Combine(temp, "partial-stimulus-police-out"));
        AssertStatus(chaseReport, "REINFORCEMENTS_CHASE_STIMULUS", "APPLIED");
        AssertStatus(chaseReport, "REINFORCEMENTS_PREVENTION", "APPLIED");
        AssertStatus(chaseReport, "REINFORCEMENTS_100MS", "COMPLETE");
        Require(GeneratedFiles(Path.Combine(temp,
            "partial-stimulus-police-out")).Length == 1,
            "RS_PARTIAL_MUST_ONLY_REWRITE_ONE_OWNER");
        string chaseFixed = File.ReadAllText(GeneratedFiles(
            Path.Combine(temp, "partial-stimulus-police-out")).Single());
        Require(Regex.Matches(chaseFixed, @"case gamedataStimType.Bullet:").Count == 1 &&
            Regex.Matches(chaseFixed, @"private let grt_rs_nextReinforcementProbe").Count == 1,
            "RS_PARTIAL_DUPLICATED_CASE_OR_THROTTLE");

        // A custom nonstandard cached-event binding refuses the stimulus
        // transformation only; independent police and probe work still runs.
        var customStim = new Dictionary<string, string>(originals);
        customStim["rs"] = customStim["rs"].Replace(
            "wrappedMethod(stimData);",
            "wrappedMethod(stimData);\n  let unsafeCustomEvent = stimData.cachedEvt;",
            StringComparison.Ordinal);
        var customStimReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "custom-stimulus-refusal", customStim),
            Path.Combine(temp, "custom-stimulus-refusal-out"));
        AssertStatus(customStimReport, "REINFORCEMENTS_CHASE_STIMULUS", "REFUSED");
        AssertStatus(customStimReport, "REINFORCEMENTS_PREVENTION", "APPLIED");
        AssertStatus(customStimReport, "REINFORCEMENTS_100MS", "APPLIED");
        Require(GeneratedFiles(Path.Combine(temp,
            "custom-stimulus-refusal-out")).Length == 6,
            "RS_STIMULUS_REFUSAL_MUST_NOT_SKIP_OTHER_OWNERS");

        // Handwritten but incomplete record switch: keep the pre-existing
        // first identity and append only the six missing performer identities.
        var incompleteRecords = new Dictionary<string, string>(originals);
        string liz = partial["lizzies"].Replace("\r\n", "\n",
            StringComparison.Ordinal);
        string oldCrowd = "\tif !this.IsCrowd() {";
        string newCrowd =
            "\tif __gcoLizzieRecordID == t\"Character.LizziesBDs_Performer\" {\n" +
            oldCrowd;
        Require(liz.Contains(oldCrowd), "LIZZIES_PARTIAL_CROWD_ANCHOR_MISSING");
        liz = liz.Replace(oldCrowd, newCrowd, StringComparison.Ordinal);
        string endCrowd = "\n}\n\n@addMethod(NPCPuppet)";
        int closing = liz.IndexOf(endCrowd, StringComparison.Ordinal);
        Require(closing >= 0, "LIZZIES_PARTIAL_WRAPPER_END_MISSING");
        liz = liz[..closing] + "\n  };" + liz[closing..];
        incompleteRecords["lizzies"] = liz;
        var missingReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "partial-record-guard", incompleteRecords),
            Path.Combine(temp, "partial-record-out"));
        AssertStatus(missingReport, "LIZZIES_BDS_PERFORMER_ATTACH_GATE", "APPLIED");
        CheckContent(incompleteRecords, Path.Combine(temp, "partial-record-out"));

        // Relocation and custom independent code must survive.
        var custom = originals.ToDictionary(x => x.Key, x =>
            "// user-relocated custom independent comment\n" +
            x.Value + "\n// source-owned extra content preserved\n");
        var customReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "custom", custom, true), Path.Combine(temp, "custom-out"));
        Require(customReport.AppliedEdits >= 7, "CUSTOM_NOT_OPTIMIZED");
        CheckContent(custom, Path.Combine(temp, "custom-out"), true);
        foreach (string file in GeneratedFiles(Path.Combine(temp, "custom-out")))
            Require(File.ReadAllText(file).Contains(
                "source-owned extra content preserved", StringComparison.Ordinal),
                "CUSTOM_ADDITION_LOST");

        // One incompatible callback must not disable other owners.
        var incompatible = new Dictionary<string, string>(originals);
        incompatible["sr"] = incompatible["sr"].Replace(
            "wrappedMethod(evt);", "this.UnprovenCustomDetection(evt);",
            StringComparison.Ordinal);
        var bad = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "incompatible", incompatible),
            Path.Combine(temp, "incompatible-out"));
        AssertStatus(bad, "STEALTH_RUNNER_QUEST_DETECTION", "REFUSED");
        AssertStatus(bad, "STEALTH_RUNNER_PLAYER_HANDLE", "REFUSED");
        AssertStatus(bad, "NCR_SPAWN", "APPLIED");
        AssertStatus(bad, "RUNNING_MAN_SPRINT_ENTRY", "APPLIED");
        AssertStatus(bad, "RUNNING_MAN_SPRINT_DIRECTION", "APPLIED");
        Require(GeneratedFiles(Path.Combine(temp, "incompatible-out")).Length == 5,
            "INCOMPATIBLE_SCOPE_TOO_BROAD");

        // When EasierControllerSprint integration changes, refuse the affected
        // direction cache without suppressing independent RunningMan work.
        var changedModule = new Dictionary<string, string>(originals);
        changedModule["dir"] = changedModule["dir"].Replace(
            "@if(!ModuleExists(\"EasierControllerSprint\"))",
            "@if(!ModuleExists(\"ChangedSprintOwner\"))",
            StringComparison.Ordinal);
        var moduleReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "changed-module", changedModule),
            Path.Combine(temp, "changed-module-out"));
        AssertStatus(moduleReport, "RUNNING_MAN_SPRINT_DIRECTION", "REFUSED");
        AssertStatus(moduleReport, "RUNNING_MAN_SPRINT_ENTRY", "APPLIED");
        Require(GeneratedFiles(Path.Combine(temp, "changed-module-out")).Length == 5,
            "CHANGED_MODULE_REFUSAL_SCOPE_TOO_BROAD");

        var absent = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "absent", new()), Path.Combine(temp, "absent-out"));
        Require(absent.Operations.Length == 11 &&
            absent.Operations.All(x => x.Status == "NOT_APPLICABLE") &&
            GeneratedFiles(Path.Combine(temp, "absent-out")).Length == 0,
            "ABSENT_OWNER_CREATED_SOURCE");
        Console.WriteLine("PASS Pass7 stock / partial / complete / idempotent / " +
            "relocated / custom / incompatible / absent");
    }
    finally
    {
        Directory.Delete(temp, recursive: true);
    }
}
