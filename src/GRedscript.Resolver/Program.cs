using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.VisualBasic.FileIO;
using GRedscript.Resolver.Phase2;

namespace GRedscript.Resolver;

public static class Program
{
    private const string Schema = "grsp.resolver.input.v1";

    // Invoke the existing verified CLI analyzer from GCO's one bundled
    // .NET 10 runtime. No second capture parser or global dotnet install.
    public static void AnalyzeCollected(string sessionFolder, string gameRoot,
        string outputFile)
    {
        if (Main(["analyze", "--capture", sessionFolder, "--game", gameRoot,
                  "--out", outputFile]) != 0 || !File.Exists(outputFile))
            throw new InvalidDataException(
                "REDscript Resolver rejected the indexed F11 capture or live source.");
    }

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length > 0 && args[0] == "source-audit")
                return RunSourceAudit(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-generic")
                return RunGenericPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-generic")
                return RunGenericScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-cname-membership")
                return RunCnameMembershipScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-cname-membership")
                return RunCnameMembershipPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "resolve-callbacks")
                return RunCallbackSemantics(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-shared-work")
                return RunSharedWorkScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-callback-work")
                return RunGenericCallbackWorkScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-ui-outputs")
                return RunUiOutputScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-quest-facts")
                return RunQuestFactScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-player-position")
                return RunPlayerPositionScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "plan-redscript")
                return RunRedscriptPlan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "stage-redscript-plan")
                return RunRedscriptPlanStage(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "phase2-footsteps")
                return RunPhase2Footsteps(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "phase2-dark-future")
                return RunPhase2DarkFuture(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "phase2-dark-future-icons")
                return RunPhase2DarkFutureIcons(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "phase2-fe-minimap")
                return RunPhase2FeMinimap(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "phase2-fe-input")
                return RunPhase2FeInput(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-native-location-subscribers")
                return RunNativeSubscriberScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-native-location-subscribers")
                return RunNativeSubscriberPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "stage-shared-native")
                return RunSharedNativeStage(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-callbacks")
                return RunCallbackScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-pure-constants")
                return RunPureConstantScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-pure-constants")
                return RunPureConstantPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-stable-array-bound")
                return RunStableArrayBoundScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-stable-array-bound")
                return RunStableArrayBoundPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-blackboard-defs")
                return RunBlackboardDefinitionScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-blackboard-defs")
                return RunBlackboardDefinitionPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-late-system-read")
                return RunLateSystemScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-late-system-read")
                return RunLateSystemPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-angle-sort")
                return RunAngleSortScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-angle-sort")
                return RunAngleSortPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-shared-player")
                return RunSharedPlayerScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-shared-player")
                return RunSharedPlayerPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-action-snapshot")
                return RunActionSnapshotScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-action-snapshot")
                return RunActionSnapshotPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scan-action-filter")
                return RunActionFilterScan(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-action-filter")
                return RunActionFilterPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "preview-callback-index")
                return RunCallbackIndexPreview(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length > 0 && args[0] == "scc-headless")
                return HeadlessScc.Run(ParseArguments(args.Skip(1).ToArray()));
            if (args.Length == 0 || args[0] != "analyze")
                throw new ArgumentException(
                    "Usage: GRedscript.Resolver source-audit --game <live game root> " +
                    "--out <external JSON path> OR analyze --capture <sealed capture folder> " +
                    "--game <live game root> --out <external JSON path> " +
                    "[--frame-evidence <GCO-PresentMon-Frames.v1.json>]");
            var options = ParseArguments(args.Skip(1).ToArray());
            string captureRoot = Path.GetFullPath(Required(options, "--capture"));
            string gameRoot = Path.GetFullPath(Required(options, "--game"));
            string output = Path.GetFullPath(Required(options, "--out"));
            if (!Directory.Exists(captureRoot))
                throw new InvalidDataException("Capture directory does not exist");
            if (Inside(output, gameRoot) || Inside(output, captureRoot))
                throw new InvalidDataException(
                    "Refusing to write Resolver output into installed game or sealed capture");

            // Current dated GCO capture sessions use G-RED. Older flat
            // sessions and standalone GRSP captures retain their layouts.
            string nativeRoot = Directory.Exists(Path.Combine(captureRoot, "G-RED"))
                ? Path.Combine(captureRoot, "G-RED")
                : Directory.Exists(Path.Combine(captureRoot, "G-REDscript"))
                    ? Path.Combine(captureRoot, "G-REDscript")
                    : captureRoot;
            string metadata = Path.Combine(nativeRoot, "Data", "Metadata");
            string completion = Path.Combine(metadata, "GCO-F11-Complete.flag");
            if (!File.Exists(completion) ||
                !string.Equals(File.ReadAllText(completion).Trim(), "F11_STOP",
                    StringComparison.Ordinal))
                throw new InvalidDataException("GCO explicit F11 completion proof is absent");

            string status = Path.Combine(metadata, "GRSP_Status.txt");
            if (!File.Exists(status))
                throw new InvalidDataException("GRSP status file missing");
            var captureStates = File.ReadLines(status)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("State:", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (captureStates.Length != 1 ||
                !string.Equals(captureStates[0], "State: COMPLETE",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GRSP capture is not exactly COMPLETE");

            string summaryFile = Path.Combine(nativeRoot, "GRSP_Summary.json");
            using var summaryDoc = JsonDocument.Parse(File.ReadAllText(summaryFile));
            var summary = summaryDoc.RootElement;
            var health = summary.GetProperty("health");
            if (health.GetProperty("frameQuality").GetString() != "GOOD" ||
                !health.GetProperty("shardMergeOk").GetBoolean() ||
                health.GetProperty("droppedSpikes").GetInt64() != 0 ||
                health.GetProperty("droppedHotPaths").GetInt64() != 0)
                throw new InvalidDataException(
                    "GRSP capture quality is insufficient for Resolver source admission");

            string collectionProvenance = GcoCollectionIndex.Verify(captureRoot, nativeRoot);

            var tables = new SortedDictionary<string, CsvTable>(StringComparer.Ordinal);
            foreach (string bucket in new[] { "Developer", "Runtime" })
            {
                string folder = Path.Combine(nativeRoot, "Data", bucket);
                if (!Directory.Exists(folder))
                    throw new InvalidDataException("Missing native evidence directory: " + bucket);
                foreach (string csv in Directory.EnumerateFiles(folder, "*.csv")
                             .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                    tables.Add(bucket + "/" + Path.GetFileName(csv), CsvTable.Load(csv));
            }
            foreach (string required in new[] {
                "Developer/RSP_FunctionMap.csv",
                "Developer/RSP_CallSites.csv",
                "Developer/RSP_Cadence.csv",
                "Developer/RSP_WorkMap.csv",
                "Developer/RSP_CallbackPressure.csv",
                "Developer/RSP_CallbackTargets.csv",
                "Runtime/GRSP_ByFunction.csv",
                "Runtime/GRSP_Frames.csv",
                "Runtime/GRSP_Markers.csv",
                "Runtime/GRSP_ByMod.csv"
            })
                if (!tables.ContainsKey(required) || tables[required].Rows.Count == 0)
                    throw new InvalidDataException("Missing/empty required native evidence: " + required);

            var markers = tables["Runtime/GRSP_Markers.csv"].Rows;
            if (!markers.Any(x => Field(x, "event") == "START") ||
                !markers.Any(x => Field(x, "event") == "STOP_REQUEST"))
                throw new InvalidDataException("Missing actual GRSP F11 START/STOP markers");

            object frameEvidence = ReadFrameEvidence(options, captureRoot);

            // IDs are observations, not unique row keys: the real capture
            // contains repeated stable_callsite_id values with distinct counts.
            var callsites = tables["Developer/RSP_CallSites.csv"].Rows;
            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            int repeatedIds = 0;
            var sourceAdmission = new SourceAdmission(gameRoot);
            _ = sourceAdmission.AuditAll();
            RequireSourceIdentity(options, sourceAdmission);
            var observations = new List<object>(callsites.Count);
            var ranks = new List<RankedObservation>(callsites.Count);
            var statusCount = new SortedDictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in callsites)
            {
                string id = Field(row, "stable_callsite_id");
                if (string.IsNullOrWhiteSpace(id))
                    throw new InvalidDataException("Callsite identity missing");
                if (!seenIds.Add(id)) repeatedIds++;

                string funcId = Field(row, "stable_function_id");
                string owner = Field(row, "owner");
                string sourcePath = Field(row, "source_path");
                string symbol = Field(row, "source_function");
                int line = Int(row, "source_line");
                double work = Number(row, "exclusive_instrumented_ms");
                double rate = Number(row, "calls_per_sec");
                long edges = Long(row, "calls");
                if (work < 0 || rate < 0 || edges < 0)
                    throw new InvalidDataException("Negative observed work/call count");

                SourceMatch match = sourceAdmission.Check(sourcePath, symbol, line);
                statusCount[match.Status] = statusCount.GetValueOrDefault(match.Status) + 1;
                string resolution = Field(row, "target_resolution");
                string caveat = resolution.StartsWith("UNRESOLVED", StringComparison.OrdinalIgnoreCase)
                    ? "Unresolved target, no native/callee attribution or rewrite authority"
                    : "Outgoing VM call edge, not callback entry; source match is not rewrite authority";
                observations.Add(new {
                    stableCallsiteId = id,
                    stableFunctionId = funcId,
                    owner,
                    sourceFunction = symbol,
                    capturedPath = sourcePath,
                    sourceLine = line,
                    callKind = Field(row, "call_kind"),
                    target = Field(row, "target"),
                    targetResolution = resolution,
                    evidence = match,
                    candidateStage = "EVIDENCE_ONLY",
                    caveat
                });
                ranks.Add(new RankedObservation(
                    id, funcId, owner, symbol, Field(row, "target"),
                    work, rate, edges, match.Status, resolution, caveat));
            }

            object proxyAttribution = ProxyAttributionEvidence.Interpret(
                tables.TryGetValue("Developer/RSP_ProxyAttribution.csv",
                    out var proxyTable) ? proxyTable.Rows : null,
                callsites,
                sourceAdmission);

            object callbackReuse = CallbackReuseEvidence.Interpret(
                tables.TryGetValue("Developer/RSP_CallbackReuse.csv",
                    out var reuseTable) ? reuseTable.Rows : null,
                tables["Developer/RSP_CallbackTargets.csv"].Rows);

            int sourceCount = sourceAdmission.InspectedFileCount;
            var top = ranks.OrderByDescending(x => x.ExclusiveInstrumentedMs)
                .ThenByDescending(x => x.OutgoingEdgesPerSec)
                .ThenBy(x => x.StableCallsiteId, StringComparer.Ordinal)
                .Take(100).ToArray();

            var payload = new {
                schemaVersion = Schema,
                producer = "GRSP_OWNER_REDSCRIPT_RESOLVER",
                stage = "R3_R4_READ_ONLY_EVIDENCE_NOT_GENERATION",
                generationAuthorized = false,
                timeGeneratedUtc = DateTimeOffset.UtcNow,
                capture = new {
                    scenario = summary.GetProperty("capture").GetProperty("scenario").GetString(),
                    durationSeconds = summary.GetProperty("capture")
                        .GetProperty("durationSeconds").GetDouble(),
                    frameQuality = health.GetProperty("frameQuality").GetString(),
                    explicitF11Complete = true,
                    nativeSourceOwner = "GRSP",
                    collectionProvenance,
                    scope = "REDscript outgoing VM call edges, NOT callback entries or total CPU",
                    rawSummarySha256 = Sha(summaryFile)
                },
                frameEvidence,
                source = new {
                    inputKind = "CURRENT_LIVE_R6_SCRIPTS",
                    fileCount = sourceCount,
                    sourceStackSha256 = sourceAdmission.SourceStackSha256,
                    inspectedDistinctFiles = sourceAdmission.InspectedFileCount,
                    admissionScope = "NAME_AND_LINE_MATCH_ONLY_NO_RECEIVER_OR_SEMANTIC_PROOF",
                    transformationState = "UNSAFE_OR_UNPROVEN",
                    sourceStateProof = "NOT_IMPLEMENTED_UNTIL_RULE_SPECIFIC_POSTCONDITIONS"
                },
                counts = new {
                    rawCallsiteRows = callsites.Count,
                    distinctCallsiteIds = seenIds.Count,
                    repeatedCallsiteIdRows = repeatedIds,
                    sourceMatchStatus = statusCount
                },
                rankedObservedCallsites = top,
                proxyAttribution,
                callbackReuse,
                sourceObservations = observations,
                rawTables = tables,
                safety = new {
                    allRawRowsPreserved = true,
                    cpuDomainsAdditive = false,
                    callbackEntryCountsKnown = false,
                    unchangedStateRatesKnown = false,
                    parserResultAuthorizesMutation = false,
                    editorWritesToLiveGame = false
                }
            };

            VerifySourceUnchanged(sourceAdmission);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            using (var stream = File.Create(output))
                JsonSerializer.Serialize(stream, payload,
                    new JsonSerializerOptions { WriteIndented = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            Console.WriteLine($"GRSP R3/R4 PASS: {callsites.Count} callsite rows, " +
                $"{seenIds.Count} distinct IDs, {repeatedIds} repeated-ID rows, " +
                $"{sourceAdmission.InspectedFileCount} inspected source files; " +
                $"READ-ONLY evidence: {output}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("GRSP Resolver refused: " + ex.Message);
            return 2;
        }
    }

    private static int RunSourceAudit(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not (
                "--game" or "--out" or "--expected-source-sha256")))
            throw new ArgumentException("Source audit accepts --game, --out and optional --expected-source-sha256");
        string gameRoot = Path.GetFullPath(Required(options, "--game"));
        string output = Path.GetFullPath(Required(options, "--out"));
        if (Inside(output, gameRoot))
            throw new InvalidDataException("Refusing to write source audit into the game");
        var source = new SourceAdmission(gameRoot);
        object result = source.AuditAll();
        RequireSourceIdentity(options, source);
        VerifySourceUnchanged(source);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        // Use a disposable file and atomic move: failures must not leave a
        // misleading partially written successful audit.
        string temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = File.Create(temporary))
                JsonSerializer.Serialize(stream, result,
                    new JsonSerializerOptions {
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    });
            File.Move(temporary, output, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        Console.WriteLine("GRSP source audit complete: read-only source evidence; " +
                          "no source transformation or compiler proof");
        return 0;
    }

    private static void RequireSourceIdentity(
        IReadOnlyDictionary<string, string> options, SourceAdmission source)
    {
        if (!options.TryGetValue("--expected-source-sha256", out string? expected))
            return;
        if (expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("Expected source SHA256 must be 64 hexadecimal characters");
        if (!string.Equals(expected, source.SourceStackSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SOURCE_DRIFT_DETECTED: live REDscript stack differs from admitted source");
    }

    private static void VerifySourceUnchanged(SourceAdmission source)
    {
        if (!string.Equals(source.SourceStackSha256,
                source.FingerprintCurrentSources(), StringComparison.Ordinal))
            throw new InvalidDataException(
                "SOURCE_DRIFT_DETECTED: live REDscript source changed during Resolver analysis");
    }

    private static int RunGenericPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--analysis" or "--out")))
            throw new ArgumentException(
                "Preview only accepts --game, --analysis and --out");
        _ = GenericMembershipPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GRSP generic PREVIEW only: staged external source, " +
                          "not compiled, not a deployment ZIP.");
        return 0;
    }


    // Source-proven membership admission is independent of whether the
    // measured playthrough happened to execute an eligible loop.
    private static int RunCnameMembershipScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException(
                "CName membership scan accepts --game --out only");
        var summary = GenericMembershipPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO source CName membership: files=" +
            summary.SourceFiles + " eligible=" + summary.Eligible +
            " already=" + summary.AlreadyComplete +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunCnameMembershipPreview(
        Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not (
                "--game" or "--analysis" or "--out" or "--base-preview")))
            throw new ArgumentException(
                "CName membership preview accepts --game --analysis --out [--base-preview]");
        int count = GenericMembershipPass.PreviewOrdered(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out var basePreview)
                ? Path.GetFullPath(basePreview) : null);
        Console.WriteLine("GCO source CName membership: transformed=" + count +
            " gameplayValidated=false deployable=false");
        return 0;
    }

    // Read-only cross-mod discovery, separate from source preview generation.
    private static int RunGenericScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--analysis" or "--out")))
            throw new ArgumentException(
                "Scan only accepts --game, optional --analysis and --out");
        var report = GenericOpportunityScanner.Scan(
            Path.GetFullPath(Required(options, "--game")),
            options.TryGetValue("--analysis", out string? analysis) ? analysis : null,
            Path.GetFullPath(Required(options, "--out")));
        // Print ONLY counts; third-party source paths remain in private local JSON.
        Console.WriteLine("GRSP generic opportunity scan: files=" + report.Files +
            " candidates=" + report.Candidates + " measured=" + report.Measured +
            " unmeasured=" + report.Unmeasured + " rejected=" + report.Rejected +
            " generated=0 deployable=false");
        return 0;
    }

    // Authoritative CET-style classification stage for all current REDscript
    // callback sources, including PlayerPuppet @wrapMethod extensions.
    private static int RunCallbackSemantics(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out" or "--analysis")))
            throw new ArgumentException(
                "Callback semantic resolver accepts --game, --out, optional --analysis");
        var result = CallbackSemanticResolver.Analyze(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--analysis", out string? evidence)
                ? Path.GetFullPath(evidence) : null);
        Console.WriteLine("GCO REDscript callback semantic catalog: files=" +
            result.Files + " callbacks=" + result.Callbacks +
            " OnAction=" + result.OnAction +
            " PlayerPuppetWraps=" + result.PlayerPuppetWraps +
            " tracedForwarders=" + result.Forwarders +
            " otherCallbacks=" + result.OtherCallbacks +
            " measuredSourceMatched=" + result.MeasuredCallbacks +
            " routeProofBlocked=" + result.UnprovenRoutes +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    // R5 source/callsite work analysis: no new profiler or generator
    // admission. Ranking exclusions are supplied by the caller, not
    // embedded as a hardcoded list in any generic source rule.
    private static int RunGenericCallbackWorkScan(
        Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--analysis" or
                "--out" or "--exclude-ranking-owners")))
            throw new ArgumentException(
                "Callback work scan accepts --game, --analysis, --out and optional --exclude-ranking-owners.");
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (options.TryGetValue("--exclude-ranking-owners", out string? owners))
            foreach (var item in owners.Split(';', StringSplitOptions.TrimEntries |
                StringSplitOptions.RemoveEmptyEntries))
                excluded.Add(item);
        var result = CallbackSemanticResolver.ScanGenericCallbackWork(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")), excluded);
        Console.WriteLine("GCO generic callback work: files=" +
            result.SourceFiles + " OnAction=" + result.OnActionMethods +
            " ranked=" + result.RankedMethods +
            " multiOwnerPatterns=" + result.CrossOwnerPatterns +
            " measured=" + result.MeasuredMethods +
            " generationAuthorized=false");
        return 0;
    }

    // R5 high-value work elimination: read-only source-wide evidence with
    // explicit first failed prerequisite. Uses the existing callback owner
    // parser and verified F11 callsite join; NEVER authorizes a UI skip.
    private static int RunSharedWorkScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out" or "--analysis")))
            throw new ArgumentException(
                "Shared-work scan accepts --game, --out and optional --analysis only");
        var summary = CallbackSemanticResolver.ScanSharedWork(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--analysis", out string? analysis)
                ? Path.GetFullPath(analysis) : null);
        Console.WriteLine("GCO REDscript shared work: files=" + summary.SourceFiles +
            " methods=" + summary.ExaminedMethods +
            " measured=" + summary.SourceMatchedMeasured +
            " unmeasured=" + summary.Unmeasured +
            " sourceWrites=0 deployable=false savingsClaimed=false");
        return 0;
    }

    // R5 generic UI output dependency audit: source-only structural evidence,
    // verified optional F11 method join, never a permission to throttle.
    private static int RunUiOutputScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out" or "--analysis")))
            throw new ArgumentException(
                "UI output scan accepts --game, --out and optional --analysis only");
        var result = CallbackSemanticResolver.ScanUiOutputSlices(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--analysis", out string? evidence)
                ? Path.GetFullPath(evidence) : null);
        Console.WriteLine("GCO generic UI output slices: sourceFiles=" +
            result.SourceFiles + " SetDataSites=" + result.SetDataSites +
            " mixedStructuralAndTime=" + result.MixedTimerSites +
            " sourceMatchedMethodSites=" + result.SourceMatchedMethodSites +
            " sourceTypedReceiverSites=" + result.SourceTypedReceivers +
            " generationAuthorized=false sourceWrites=0");
        return 0;
    }

    // Quest fact exact-name/receiver-refusal evidence: no callback rewrites.
    private static int RunQuestFactScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out" or "--analysis")))
            throw new ArgumentException(
                "Quest fact scan accepts --game, --out and optional --analysis only");
        var scan = CallbackSemanticResolver.ScanQuestFactDependencies(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--analysis", out string? analysis)
                ? Path.GetFullPath(analysis) : null);
        Console.WriteLine("GCO quest fact graph: sourceFiles=" + scan.SourceFiles +
            " sourceMethods=" + scan.SourceMethods +
            " callsites=" + scan.FactCallsites +
            " literal=" + scan.LiteralCallsites +
            " dynamic=" + scan.DynamicCallsites +
            " names=" + scan.LiteralFactNames +
            " crossOwnerListenerNames=" + scan.CrossOwnerListenerFactNames +
            " generationAuthorized=false sourceWrites=0");
        return 0;
    }

    // Source-wide position origin and bounded helper traversal. This reports
    // candidates, not rewrite permissions or native subscription admission.
    private static int RunPlayerPositionScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException(
                "Player position scan accepts --game and --out only");
        var summary = CallbackSemanticResolver.ScanPlayerPositionDependencies(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO player position source graph: files=" +
            summary.SourceFiles + " calls=" + summary.WorldPositionCalls +
            " local=" + summary.LocalPlayer + " npc=" + summary.Npc +
            " vehicle=" + summary.Vehicle + " unknown=" +
            summary.Unknown + " mixed=" + summary.Mixed +
            " indirectConsumers=" + summary.IndirectConsumers +
            " rewrites=0 productionZipAuthorized=false");
        return 0;
    }

    // CET-inspired callback family discovery is deliberately read-only.
    private static int RunRedscriptPlan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("REDscript plan needs --game and --out");
        var plan = RedscriptGenerationPlan.Create(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("REDscript Resolver ordered generation plan: " +
            " implementedRules=" + plan.ImplementedRuleCount +
            " readyRules=" + plan.ReadyRuleCount +
            " eligibleSourceEdits=" + plan.EligibleSourceEdits +
            " deferredExperimentalSystems=" + plan.DeferredExperimentalSystems +
            " zipCreated=false");
        return 0;
    }

    private static int RunRedscriptPlanStage(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--plan" or
            "--analysis" or "--out")))
            throw new ArgumentException(
                "REDscript stage needs --game --plan --analysis --out");
        var result = RedscriptGenerationPlan.Stage(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--plan")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("REDscript pass generator consumed ordered plan: " +
            " generatedFiles=" + result.GeneratedFiles +
            " candidateEdits=" + result.AppliedEdits +
            " passes=" + result.Completed.Length +
            " productionZip=false");
        return 0;
    }

    private static int RunPhase2FeMinimap(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not (
            "--game" or "--out" or "--base-overlay")))
            throw new ArgumentException(
                "Phase 2 FE minimap needs --game --out [--base-overlay]");
        var result = FlushingEtiquetteMinimapDormancyPass.Generate(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-overlay", out string? prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("REDscript Phase 2 FE minimap: status=" +
            result.Status + " edits=" + result.AppliedEdits +
            (result.Reason is null ? "" : " reason=" + result.Reason));
        return 0;
    }

    private static int RunPhase2FeInput(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not (
            "--game" or "--out" or "--base-overlay")))
            throw new ArgumentException(
                "Phase 2 FE input needs --game and --out [--base-overlay]");
        var result = FlushingEtiquetteInputGatePass.Generate(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-overlay", out string? prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("REDscript Phase 2 FlushingEtiquette input: " +
            "status=" + result.Status + " edits=" + result.AppliedEdits +
            (result.Reason is null ? "" : " reason=" + result.Reason));
        return 0;
    }

    private static int RunPhase2Footsteps(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out" or "--base-overlay")))
            throw new ArgumentException(
                "Phase 2 Footsteps needs --game and --out [--base-overlay]");
        var result = DigitalVixenFootstepsPass.Generate(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-overlay", out string? prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("REDscript Phase 2 Footsteps: " +
            "status=" + result.Status +
            " edits=" + result.AppliedEdits +
            " already=" + result.AlreadyComplete +
            (result.Reason is null ? "" : " reason=" + result.Reason));
        return 0;
    }

    private static int RunPhase2DarkFuture(
        Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not (
            "--game" or "--out" or "--base-overlay")))
            throw new ArgumentException(
                "Dark Future Phase 2 needs --game --out [--base-overlay]");
        var result = DarkFutureInfiniteTagCachePass.Generate(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-overlay", out string? prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("REDscript Phase 2 Dark Future: status=" +
            result.Status + " edits=" + result.AppliedEdits +
            " already=" + result.AlreadyComplete +
            (result.Reason is null ? "" : " reason=" + result.Reason));
        return 0;
    }

    private static int RunPhase2DarkFutureIcons(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out" or "--base-overlay")))
            throw new ArgumentException(
                "Dark Future icon cadence requires --game --out [--base-overlay]");
        var result = DarkFutureStatusIconCadencePass.Generate(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-overlay", out string? prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("REDscript Phase 2 Dark Future icons: status=" +
            result.Status + " edits=" + result.AppliedEdits +
            (result.Reason is null ? "" : " reason=" + result.Reason));
        return 0;
    }

    private static int RunCallbackScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException("Callback scan only accepts --game and --out");
        var result = GenericCallbackRules.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript callback inventory: files=" + result.SourceFiles +
            " callbacks=" + result.Callbacks +
            " fixedIndexCandidates=" + result.IndexCandidates +
            " already=" + result.AlreadyComplete +
            " rejected=" + result.Rejected +
            " generated=0 deployable=false");
        return 0;
    }

    private static int RunCallbackIndexPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--analysis" or "--out")))
            throw new ArgumentException("Callback preview only accepts --game, --analysis and --out");
        int count = GenericCallbackRules.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript fixed-index preview: transformed=" + count +
            " compilerValidated=false gameplayValidated=false deployable=false");
        return 0;
    }

    private static int RunActionFilterScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException("Action filter scan only accepts --game and --out");
        var summary = GenericActionFilterPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript generic action filter: files=" +
            summary.SourceFiles + " OnAction=" + summary.OnActionMethods +
            " eligible=" + summary.Eligible + " already=" +
            summary.AlreadyComplete + " refused=" + summary.Refused +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunActionFilterPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key =>
                key is not ("--game" or "--analysis" or "--out")))
            throw new ArgumentException("Action filter preview accepts only --game, --analysis, --out");
        int rewritten = GenericActionFilterPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript action filter preview: transformed=" +
            rewritten + " compilerValidated=false gameplayValidated=false deployable=false");
        return 0;
    }

    // One immutable action event, decoded once per interested input callback.
    // This does not alter engine registrations, consumption or wrapper order.
    private static int RunActionSnapshotScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("Action snapshot scan accepts --game and --out");
        var result = CallbackSemanticResolver.ScanActionEventSnapshots(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO shared input event snapshot: files=" +
            result.SourceFiles + " eligible=" + result.Eligible +
            " files=" + result.EligibleFiles +
            " already=" + result.AlreadyComplete + " sourceWrites=0");
        return 0;
    }

    private static int RunActionSnapshotPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not (
                "--game" or "--analysis" or "--out" or "--base-preview")))
            throw new ArgumentException("Action snapshot preview accepts --game, --analysis, --out, --base-preview");
        int count = CallbackSemanticResolver.PreviewActionEventSnapshots(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out var b) ?
                Path.GetFullPath(b) : null);
        Console.WriteLine("GCO shared input event generated .reds: transformed=" +
            count + " gameplayValidated=false deployable=false");
        return 0;
    }

    // G-PureConstantSlice: preserve the surrounding author method; substitute
    // only source-proven pure final-static Bool literal getters, as a staged
    // REDscript source preview for official SCC and user gameplay tests.
    private static int RunPureConstantScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("Pure constant scan accepts --game and --out only");
        var result = CallbackSemanticResolver.ScanPureConstantSlices(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO pure constant partial-work scan: files=" +
            result.SourceFiles + " eligibleSites=" + result.EligibleSites +
            " eligibleFiles=" + result.EligibleFiles +
            " already=" + result.AlreadyComplete +
            " refused=" + result.Refusals + " sourceWrites=0");
        return 0;
    }

    private static int RunPureConstantPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--analysis" or "--out" or "--base-preview")))
            throw new ArgumentException(
                "Pure constant preview accepts --game --analysis --out [--base-preview]");
        int count = CallbackSemanticResolver.PreviewPureConstantSlices(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out string? basePreview)
                ? Path.GetFullPath(basePreview) : null);
        Console.WriteLine("GCO pure constant generated .reds preview: transformed=" +
            count + " originalSourceWrites=0 compilerValidated=false deployable=false");
        return 0;
    }

    private static int RunStableArrayBoundScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("Stable array-bound scan accepts --game and --out");
        var result = GenericStableArrayBoundPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO stable array bound scan: files=" +
            result.SourceFiles + " eligible=" + result.Eligible +
            " already=" + result.AlreadyComplete +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunStableArrayBoundPreview(
        Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--analysis" or
            "--out" or "--base-preview")))
            throw new ArgumentException("Stable array-bound preview accepts --game, --analysis, --out and optional --base-preview");
        int count = GenericStableArrayBoundPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out var prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("GCO stable array bound preview: transformed=" +
            count + " gameplayValidated=false deployable=false");
        return 0;
    }

    private static int RunBlackboardDefinitionScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("Blackboard definition scan accepts --game, --out only");
        var summary = GenericBlackboardDefinitionPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO blackboard definition scan: files=" + summary.Files +
            " eligible=" + summary.Eligible + " already=" + summary.AlreadyComplete +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunBlackboardDefinitionPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--analysis" or "--out" or "--base-preview")))
            throw new ArgumentException("Blackboard definition preview accepts --game, --analysis, --out, optional --base-preview only");
        int count = GenericBlackboardDefinitionPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out string? basePreview)
                ? Path.GetFullPath(basePreview) : null);
        Console.WriteLine("GCO blackboard definition preview: transformed=" + count +
            " compilerValidated=false gameplayValidated=false deployable=false");
        return 0;
    }

    private static int RunLateSystemScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--out")))
            throw new ArgumentException("Late-system scanner accepts --game and --out");
        var result = GenericLateSystemReadPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO late system-read scan: files=" + result.Sources +
            " eligible=" + result.Eligible + " already=" + result.Complete +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunLateSystemPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(x => x is not ("--game" or "--analysis" or
            "--out" or "--base-preview")))
            throw new ArgumentException("Late-system preview accepts --game, --analysis, --out, --base-preview");
        int count = GenericLateSystemReadPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")),
            options.TryGetValue("--base-preview", out var prior)
                ? Path.GetFullPath(prior) : null);
        Console.WriteLine("GCO late system-read preview: transformed=" + count +
            " compilerValidated=false gameplayValidated=false deployable=false");
        return 0;
    }

    private static int RunAngleSortScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException("Angle-sort scanner accepts --game and --out only");
        var summary = GenericAngleSortPass.Scan(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript generic angle-sort scan: files=" +
            summary.Files + " eligible=" + summary.Eligible +
            " already=" + summary.AlreadyComplete +
            " sourceWrites=0 deployable=false");
        return 0;
    }

    private static int RunAngleSortPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--analysis" or "--out")))
            throw new ArgumentException("Angle-sort preview accepts --game, --analysis, --out only");
        int count = GenericAngleSortPass.Preview(
            Path.GetFullPath(Required(options, "--game")),
            Path.GetFullPath(Required(options, "--analysis")),
            Path.GetFullPath(Required(options, "--out")));
        Console.WriteLine("GCO REDscript angle-sort preview: transformed=" + count +
            " compilerValidated=false gameplayValidated=false deployable=false");
        return 0;
    }

    private static int RunSharedPlayerScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException(
                "Shared player scan accepts --game and --out only");
        var result = GenericSharedPlayerSignalPass.Scan(
            Required(options, "--game"), Required(options, "--out"));
        Console.WriteLine("GCO shared player scan: eligible=" + result.Eligible +
            " files=" + result.EligibleFiles + " complete=" +
            result.AlreadyComplete);
        return 0;
    }

    private static int RunSharedPlayerPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not (
            "--game" or "--analysis" or "--out" or "--base-preview")))
            throw new ArgumentException(
                "Shared player preview accepts --game, --analysis, --out and --base-preview");
        int edits = GenericSharedPlayerSignalPass.Preview(
            Required(options, "--game"), Required(options, "--analysis"),
            Required(options, "--out"),
            options.GetValueOrDefault("--base-preview"));
        Console.WriteLine("GCO shared player preview: edits=" + edits +
            " productionZipAuthorized=false");
        return 0;
    }

    private static int RunNativeSubscriberScan(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out")))
            throw new ArgumentException("Native subscriber scan accepts --game and --out");
        int count = GenericNativeLocationSubscriberPass.Scan(
            Required(options, "--game"), Required(options, "--out"));
        Console.WriteLine("GCO native location subscriber scan: eligible=" + count +
            " deployable=false");
        return 0;
    }

    private static int RunNativeSubscriberPreview(Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not ("--game" or "--out" or "--source-sha" or "--base-preview")))
            throw new ArgumentException(
                "Native subscriber preview accepts --game, --out, --source-sha, --base-preview");
        int count = GenericNativeLocationSubscriberPass.Preview(
            Required(options, "--game"), Required(options, "--out"),
            options.GetValueOrDefault("--source-sha"),
            options.GetValueOrDefault("--base-preview"));
        Console.WriteLine("GCO native location subscriber preview: adapters=" +
            count + " originalModSourcesUnchanged=true nativeFarPredicateEdits=true deployable=false");
        return 0;
    }

    private static int RunSharedNativeStage(
        Dictionary<string, string> options)
    {
        if (options.Keys.Any(key => key is not (
            "--cet-runtime" or "--framework" or "--game" or "--out" or "--red-locations")))
            throw new ArgumentException(
                "Shared native stage accepts --cet-runtime, --framework, --game, --out, --red-locations");
        var result = SharedNativeRuntimeStage.Stage(
            Required(options, "--cet-runtime"),
            Required(options, "--game"),
            Required(options, "--out"),
            Required(options, "--framework"),
            options.GetValueOrDefault("--red-locations"));
        Console.WriteLine(JsonSerializer.Serialize(result));
        return 0;
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        if (args.Length % 2 != 0)
            throw new ArgumentException("Expected --option value pairs");
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (args[i] is not ("--capture" or "--game" or "--out" or "--analysis" or "--frame-evidence" or "--expected-source-sha256" or
                    "--workspace" or "--r6" or "--cache" or "--compiled" or "--scc-native" or "--log" or "--external-scripts" or "--base-preview" or "--base-overlay" or "--plan" or "--cet-runtime" or "--framework" or "--red-locations" or "--source-sha" or "--exclude-ranking-owners"))
                throw new ArgumentException("Unknown Resolver option: " + args[i]);
            if (!options.TryAdd(args[i], args[i + 1]))
                throw new ArgumentException("Duplicate Resolver option: " + args[i]);
        }
        return options;
    }

    private static string Required(Dictionary<string, string> options, string key) =>
        options.TryGetValue(key, out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing required option: " + key);

    private static bool Inside(string child, string parent)
    {
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        child = Path.GetFullPath(child);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static object ReadFrameEvidence(
        Dictionary<string, string> options, string captureRoot) =>
        PresentMonEvidence.Read(options, captureRoot);

    private static string Sha(string filename) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(filename)));

    private static string Field(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out string? value)
            ? value : throw new InvalidDataException("Native CSV field absent: " + key);

    private static long Long(Dictionary<string, string> row, string key) =>
        long.TryParse(Field(row, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long value)
            ? value : throw new InvalidDataException("Invalid native integer: " + key);

    private static int Int(Dictionary<string, string> row, string key) =>
        int.TryParse(Field(row, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException("Invalid native integer: " + key);

    private static double Number(Dictionary<string, string> row, string key) =>
        double.TryParse(Field(row, key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double value) && double.IsFinite(value)
            ? value : throw new InvalidDataException("Invalid native number: " + key);

    private sealed record RankedObservation(
        string StableCallsiteId, string StableFunctionId, string Owner,
        string SourceFunction, string Target, double ExclusiveInstrumentedMs,
        double OutgoingEdgesPerSec, long OutgoingEdges, string SourceMatch,
        string TargetResolution, string Caveat);

    private sealed record CsvTable(
        string Sha256, string[] Header, List<Dictionary<string, string>> Rows)
    {
        internal static CsvTable Load(string filename)
        {
            using var parser = new TextFieldParser(filename);
            parser.TextFieldType = FieldType.Delimited;
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;
            var header = parser.ReadFields() ??
                throw new InvalidDataException("CSV has no header: " + filename);
            if (header.Length == 0 || header.Any(string.IsNullOrWhiteSpace) ||
                header.Distinct(StringComparer.Ordinal).Count() != header.Length)
                throw new InvalidDataException("Invalid CSV columns: " + filename);
            // Retain every row, including repeated stable IDs. Keep all source-native
            // fields as strings, rather than losing precision through JSON numbers.
            var rows = new List<Dictionary<string, string>>();
            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields() ??
                    throw new InvalidDataException("Invalid CSV row: " + filename);
                if (fields.Length != header.Length)
                    throw new InvalidDataException("Invalid native CSV field count: " + filename);
                var row = new Dictionary<string, string>(header.Length,
                    StringComparer.Ordinal);
                for (int i = 0; i < header.Length; i++)
                    row.Add(header[i], fields[i]);
                rows.Add(row);
            }
            return new(Sha(filename), header, rows);
        }
    }
}
