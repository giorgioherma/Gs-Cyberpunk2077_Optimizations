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
        ["rm"] = ("RunningMan", "EvaluateTwoStepSprint")
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
    var paths = new Dictionary<string, string>
    {
        ["ncr"] = "NightCityRemembers/NCR_SpawnCulling.reds",
        ["sr"] = "StealthRunner/npc_puppet.reds",
        ["rm"] = "RunningMan/double_sprint.reds"
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
    Require(files.Length == 3, "EXPECTED_THREE_OUTPUTS");
    foreach (var key in new[] { "ncr", "sr", "rm" })
    {
        string owner = key switch
        {
            "ncr" => "NightCityRemembers",
            "sr" => "StealthRunner",
            _ => "RunningMan"
        };
        var candidates = files.Where(f => f.Replace('\\', '/').Contains(
            "/r6/scripts/" + owner + "/", StringComparison.Ordinal)).ToArray();
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
            int gate = content.IndexOf("if !IsDefined(this.m_stealthRunnerStatus)",
                StringComparison.Ordinal);
            int gateQuest = content.IndexOf(
                "if !IsDefined(this.m_stealthRunnerStatus.quest)",
                StringComparison.Ordinal);
            int player = content.LastIndexOf("let player:",
                StringComparison.Ordinal);
            Require(gate >= 0 && gateQuest > gate && gateQuest < player,
                "STEALTH_QUEST_GUARD_MISSING");
            Require(content.Contains("wrappedMethod(evt);",
                StringComparison.Ordinal), "STEALTH_WRAPPED_METHOD_LOST");
        }
        else
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
        foreach (string id in new[] { "NCR_SPAWN", "STEALTH_RUNNER", "RUNNING_MAN" })
            AssertStatus(report, id, "APPLIED");
        CheckContent(originals, result);
        Require(Sha(string.Join("|", ReadCurrent(game)
            .OrderBy(x => x.Key).Select(x => x.Value))) == originalSha,
            "STOCK_SOURCE_MUTATED");

        // Full handwritten equivalent, even without a marker, is a no-op.
        var completed = new Dictionary<string, string>();
        foreach (var file in GeneratedFiles(result))
        {
            string norm = file.Replace('\\', '/');
            string key = norm.Contains("/NightCityRemembers/") ? "ncr" :
                norm.Contains("/StealthRunner/") ? "sr" : "rm";
            completed[key] = File.ReadAllText(file);
        }
        var fullGame = MakeGame(temp, "complete", completed);
        var fullReport = Pass7ChaseFrameFloorPass.Generate(fullGame,
            Path.Combine(temp, "full-out"));
        Require(fullReport.AppliedEdits == 0 &&
            GeneratedFiles(Path.Combine(temp, "full-out")).Length == 0,
            "FULL_SOURCE_MUST_BE_NOOP");
        foreach (var id in new[] { "NCR_SPAWN", "STEALTH_RUNNER", "RUNNING_MAN" })
            AssertStatus(fullReport, id, "COMPLETE");

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
        partial["sr"] = partial["sr"].Replace("  wrappedMethod(evt);",
            "  wrappedMethod(evt);\n  if !IsDefined(this.m_stealthRunnerStatus) { return false; }",
            StringComparison.Ordinal);
        partial["rm"] = "@addField(SprintEvents)\nprivate let m_gredRunningManDoubleRunPerk: Bool;\n\n"
            + partial["rm"];
        var partialReport = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "partial", partial), Path.Combine(temp, "partial-out"));
        foreach (var id in new[] { "NCR_SPAWN", "STEALTH_RUNNER", "RUNNING_MAN" })
            AssertStatus(partialReport, id, "APPLIED");
        CheckContent(partial, Path.Combine(temp, "partial-out"));

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
        AssertStatus(bad, "STEALTH_RUNNER", "REFUSED");
        AssertStatus(bad, "NCR_SPAWN", "APPLIED");
        AssertStatus(bad, "RUNNING_MAN", "APPLIED");
        Require(GeneratedFiles(Path.Combine(temp, "incompatible-out")).Length == 2,
            "INCOMPATIBLE_SCOPE_TOO_BROAD");

        var absent = Pass7ChaseFrameFloorPass.Generate(
            MakeGame(temp, "absent", new()), Path.Combine(temp, "absent-out"));
        Require(absent.Operations.All(x => x.Status == "NOT_APPLICABLE") &&
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
