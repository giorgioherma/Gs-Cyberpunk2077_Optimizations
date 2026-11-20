using System.Text;
using System.Text.Json;
using GRedscript.Resolver.Phase2;

namespace GRedscript.Resolver;

/// <summary>
/// One canonical Resolver -> generator handoff. This owns the sequence of the
/// EXISTING source generators. It is not a new REDscript syntax parser, rule
/// implementation or game ZIP generator.
///
/// Current source decides applicability. Generator executes ONLY this ordered
/// plan; unimplemented experimental families remain visible, never silently
/// counted as generated. All output is game-relative .reds in external staging.
/// </summary>
public static class RedscriptGenerationPlan
{
    private const string Schema = "gco.redscript.generation-plan.v1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public sealed record RulePlan(
        int? Order, string Id, string System, string Rule,
        string ScanCommand, string? Generator, int? Eligible,
        int? AlreadyComplete, string Status, string Risk,
        string[] SourceFolders)
    {
        public string? SourceRefusal { get; init; }
    }

    public sealed record Document(
        string SchemaVersion, string SourceStackSha256, int SourceFiles,
        bool BasedOnCurrentInstalledSource, bool ProductionZipAuthorized,
        bool GameplayValidated, int ImplementedRuleCount,
        int ReadyRuleCount, int EligibleSourceEdits,
        int DeferredExperimentalSystems, int CaptureGatedExistingRules,
        RulePlan[] Rules);

    public sealed record OutputItem(
        int Order, string Id, int RewrittenExpressions,
        int NewFiles, int ComposedFiles, string[] Collisions);
    public sealed record SkippedRule(
        int Order, string Id, int EligibleCandidates, string Reason);

    public sealed record StageResult(
        string SchemaVersion, string SourceStackSha256,
        int GeneratedFiles, int AppliedEdits,
        int DeferredCollisions, bool ProductionZipAuthorized,
        OutputItem[] Completed)
    {
        public SkippedRule[] Skipped { get; init; } = [];
    }

    // Canonical dependency/safety order: local callback/index transforms,
    // then pure independent geometric/guard work, followed by two source
    // generators which know how to compose against previously staged bytes.
    // This order MUST live here, not independently in CI or GCO's WPF page.
    private static readonly (string Id, string System, string Rule,
        string Scan, string Preview, string Count, string Complete,
        string Collection, string Risk)[] Implemented =
    [
        ("fixed-cname-index", "D", "CNAME_FIXED_INDEX_V1",
            "scan-callbacks", "preview-callback-index",
            "fixedCnameIndexEligible", "fixedCnameIndexAlreadyComplete",
            "fixedIndexCandidates", "Possible fixed-index method behavior mismatch"),
        ("literal-action-filter", "A", "ACTION_LITERAL_MEMBERSHIP_V1",
            "scan-action-filter", "preview-action-filter",
            "eligible", "alreadyComplete", "candidates",
            "Incorrect literal action classification"),
        ("angle-sort", "E", "ANGLE_SORT_INVARIANT_V1",
            "scan-angle-sort", "preview-angle-sort",
            "eligible", "alreadyComplete", "candidates",
            "Numerical boundary and NaN/Infinity parity"),
        ("late-system-read", "E", "SYSTEM_READ_AFTER_ACTION_GUARD_V1",
            "scan-late-system-read", "preview-late-system-read",
            "eligible", "alreadyComplete", "candidates",
            "Moved service lookup may change exceptional edge cases"),
        ("source-cname-membership", "D", "CNAME_ARRAY_MEMBERSHIP_V1",
            "scan-cname-membership", "preview-cname-membership",
            "eligible", "alreadyComplete", "candidates",
            "Typed CName ArrayContains equivalence or unusual source ownership"),
        ("blackboard-definitions", "C", "BLACKBOARD_DEFINITION_STATEMENT_AND_SEQUENCE_V1",
            "scan-blackboard-defs", "preview-blackboard-defs",
            "eligible", "alreadyComplete", "candidates",
            "Definition-group identity or setter effects"),
        ("pure-constant-slices", "B", "PURE_FINAL_STATIC_BOOL_V1",
            "scan-pure-constants", "preview-pure-constants",
            "eligibleSites", "alreadyComplete", "sites",
            "Dynamic method override or changing configuration"),
        ("immutable-action-event-snapshot", "A", "ACTION_EVENT_SNAPSHOT_V1",
            "scan-action-snapshot", "preview-action-snapshot",
            "eligible", "alreadyComplete", "candidates",
            "Native ListenerAction getter semantics or excess snapshot reads"),
        ("shared-player-signal", "C", "SHARED_PLAYER_SIGNAL_V1",
            "scan-shared-player", "preview-shared-player",
            "eligible", "alreadyComplete", "candidates",
            "Player attach generation or cached handle may differ from live getter"),
        ("stable-array-bound", "D", "STABLE_ARRAY_BOUND_V1",
            "scan-stable-array-bound", "preview-stable-array-bound",
            "eligible", "alreadyComplete", "candidates",
            "Array length may change through an unproven alias or call")
    ];

    // Explicit inventory for the approved A-E experimental systems.
    // An existing narrower rule does not mean that the system is implemented.
    private static readonly (string Id, string System, string Risk)[] Deferred =
    [
        ("wildcard-input-admission", "A",
            "Lost raw input events, controller actions, consumption/order"),
        ("dirty-ui-partial-refresh", "B",
            "Stale widgets, missed animation/visibility invalidation"),
        ("shared-player-quest-spatial-observers", "C",
            "Stale player/session state, teleport/fact notification timing"),
        ("keyed-collection-index", "D",
            "Stale indexes, duplicate-key precedence, reorder/delete"),
        ("spatial-resource-broadphase", "E",
            "False rejection at boundaries and missed resource events")
    ];

    public static Document Create(string gameRoot, string outFile)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outFile);
        if (IsInside(output, game) || File.Exists(output) ||
            Directory.Exists(output))
            throw new InvalidDataException("REDSCRIPT_PLAN_OUTPUT_COLLISION");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sourceHash = admission.SourceStackSha256 ??
            throw new InvalidDataException("REDSCRIPT_PLAN_NO_SOURCE_HASH");
        var temp = Path.Combine(Path.GetTempPath(),
            "gco-redscript-plan-" + Guid.NewGuid().ToString("N"));
        var rows = new List<RulePlan>();
        Directory.CreateDirectory(temp);
        try
        {
            for (int i = 0; i < Implemented.Length; i++)
            {
                var def = Implemented[i];
                string report = Path.Combine(temp, def.Id + ".json");
                try
                {
                    ScanRule(def.Id, game, report);
                }
                catch (InvalidDataException ex) when (IsLocalSourceRefusal(ex.Message))
                {
                    // One unsafe user source cannot stop all independent
                    // generator families. This refusal is re-proven at Stage.
                    rows.Add(new(i + 1, def.Id, def.System, def.Rule,
                        def.Scan, def.Preview, 0, 0,
                        "SOURCE_REFUSED", def.Risk, [])
                    {
                        SourceRefusal = ex.Message
                    });
                    continue;
                }
                using var json = JsonDocument.Parse(File.ReadAllText(report));
                var root = json.RootElement;
                if (!string.Equals(ReadString(root, "sourceStackSha256"),
                    sourceHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("REDSCRIPT_PLAN_SCAN_SOURCE_DRIFT");

                int eligible = ReadInt(root, def.Count);
                int complete = ReadInt(root, def.Complete);
                var folders = new SortedSet<string>(StringComparer.Ordinal);
                if (root.TryGetProperty(def.Collection, out var candidates) &&
                    candidates.ValueKind == JsonValueKind.Array)
                {
                    foreach (var row in candidates.EnumerateArray())
                    {
                        // Candidate lists may contain rejected or complete
                        // source shapes. The affected-folder report must
                        // contain only actually eligible new source work.
                        if (row.TryGetProperty("state", out var state) &&
                            state.ValueKind == JsonValueKind.String &&
                            state.GetString() != "PRISTINE_APPLICABLE")
                            continue;
                        if (!row.TryGetProperty("path", out var p) ||
                            p.ValueKind != JsonValueKind.String) continue;
                        string rel = p.GetString()!.Replace('\\', '/');
                        if (!rel.StartsWith("r6/scripts/", StringComparison.Ordinal))
                            continue;
                        string remaining = rel["r6/scripts/".Length..];
                        string folder = remaining.Contains('/')
                            ? remaining[..remaining.IndexOf('/')] : "(root)";
                        folders.Add(folder);
                    }
                }
                rows.Add(new(i + 1, def.Id, def.System, def.Rule,
                    def.Scan, def.Preview, eligible, complete,
                    eligible > 0 ? "READY_FOR_GENERATOR" :
                    complete > 0 ? "ALREADY_COMPLETE" : "NOT_APPLICABLE",
                    def.Risk, folders.ToArray()));
            }
            foreach (var deferred in Deferred)
                rows.Add(new(null, deferred.Id, deferred.System,
                    deferred.Id.ToUpperInvariant().Replace('-', '_'),
                    "NOT_IMPLEMENTED", null, null, null,
                    "NO_GENERATOR_YET", deferred.Risk, []));
            if (!string.Equals(sourceHash,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("REDSCRIPT_PLAN_CHANGED_DURING_SCAN");
            var document = new Document(Schema, sourceHash,
                admission.InspectedFileCount, true, false, false,
                Implemented.Length,
                rows.Count(x => x.Status == "READY_FOR_GENERATOR"),
                rows.Sum(x => x.Eligible ?? 0),
                Deferred.Length, 0, rows.ToArray());
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.WriteAllText(output, JsonSerializer.Serialize(document,
                JsonOptions), new UTF8Encoding(false));
            return document;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    public static StageResult Stage(string gameRoot,
        string planFile, string analysisFile, string outputRoot)
    {
        string game = Path.GetFullPath(gameRoot),
            planPath = Path.GetFullPath(planFile),
            analysis = Path.GetFullPath(analysisFile),
            output = Path.GetFullPath(outputRoot);
        if (IsInside(output, game) || File.Exists(output) ||
            Directory.Exists(output) ||
            !File.Exists(planPath) || !File.Exists(analysis))
            throw new InvalidDataException("REDSCRIPT_PLAN_STAGE_OUTPUT_OR_INPUT_INVALID");

        var document = JsonSerializer.Deserialize<Document>(
            File.ReadAllText(planPath), JsonOptions)
            ?? throw new InvalidDataException("REDSCRIPT_PLAN_INVALID_DOCUMENT");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        if (document.SchemaVersion != Schema ||
            !document.BasedOnCurrentInstalledSource ||
            document.ProductionZipAuthorized ||
            document.Rules.Length != Implemented.Length + Deferred.Length ||
            document.ImplementedRuleCount != Implemented.Length ||
            document.DeferredExperimentalSystems != Deferred.Length ||
            document.CaptureGatedExistingRules != 0 ||
            !string.Equals(document.SourceStackSha256,
                admission.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("REDSCRIPT_PLAN_STALE_OR_TAMPERED");

        // The generator never trusts arbitrary user-supplied command names or
        // reordering from a JSON plan: the checked-in Resolver determines
        // exactly which existing generator and order can execute.
        for (int i = 0; i < Implemented.Length; i++)
        {
            var expected = Implemented[i];
            var item = document.Rules[i];
            if (item.Order != i + 1 || item.Id != expected.Id ||
                item.System != expected.System || item.Rule != expected.Rule ||
                item.Generator != expected.Preview ||
                item.ScanCommand != expected.Scan || item.Eligible is null ||
                item.AlreadyComplete is null ||
                item.Eligible < 0 || item.AlreadyComplete < 0)
                throw new InvalidDataException("REDSCRIPT_PLAN_COMMAND_OR_ORDER_DRIFT");
            if (item.Status == "SOURCE_REFUSED")
            {
                if (item.Eligible != 0 || item.AlreadyComplete != 0 ||
                    string.IsNullOrWhiteSpace(item.SourceRefusal) ||
                    !IsLocalSourceRefusal(item.SourceRefusal))
                    throw new InvalidDataException("REDSCRIPT_PLAN_SOURCE_REFUSAL_INVALID");
                // The plan is editable JSON: prove that the SAME current
                // source actually causes the exact claimed local refusal.
                string probe = Path.Combine(Path.GetTempPath(),
                    "gco-source-refusal-" + Guid.NewGuid().ToString("N") + ".json");
                string? actual = null;
                try
                {
                    try { ScanRule(item.Id, game, probe); }
                    catch (InvalidDataException ex) when (IsLocalSourceRefusal(ex.Message))
                    { actual = ex.Message; }
                }
                finally { if (File.Exists(probe)) File.Delete(probe); }
                if (!string.Equals(actual, item.SourceRefusal,
                        StringComparison.Ordinal))
                    throw new InvalidDataException(
                        "REDSCRIPT_PLAN_SOURCE_REFUSAL_NOT_REPRODUCED");
                continue;
            }
            if (item.SourceRefusal is not null ||
                item.Status != (item.Eligible > 0 ?
                    "READY_FOR_GENERATOR" : item.AlreadyComplete > 0 ?
                    "ALREADY_COMPLETE" : "NOT_APPLICABLE"))
                throw new InvalidDataException("REDSCRIPT_PLAN_COMMAND_OR_ORDER_DRIFT");
        }
        for (int i = 0; i < Deferred.Length; i++)
        {
            var item = document.Rules[Implemented.Length + i];
            if (item.Id != Deferred[i].Id ||
                item.System != Deferred[i].System || item.Order is not null ||
                item.Generator is not null || item.Eligible is not null ||
                item.Status != "NO_GENERATOR_YET")
                throw new InvalidDataException("REDSCRIPT_PLAN_DEFERRED_RULE_TAMPERED");
        }
        if (document.ReadyRuleCount != document.Rules.Count(x =>
            x.Status == "READY_FOR_GENERATOR") ||
            document.EligibleSourceEdits != document.Rules.Sum(x =>
                x.Eligible ?? 0))
            throw new InvalidDataException("REDSCRIPT_PLAN_COUNTER_DRIFT");

        using (var capture = JsonDocument.Parse(File.ReadAllText(analysis)))
        {
            var root = capture.RootElement;
            if (ReadString(root, "schemaVersion") !=
                "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance")
                    .GetString() != "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source")
                    .GetProperty("sourceStackSha256").GetString(),
                    document.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("REDSCRIPT_PLAN_CAPTURE_MISMATCH");
        }

        string final = Path.Combine(output, "effective-preview");
        var results = new List<OutputItem>();
        int files = 0, expressions = 0, deferred = 0;
        var skipped = new List<SkippedRule>();
        Directory.CreateDirectory(output);
        foreach (var item in document.Rules.Take(Implemented.Length))
        {
            if (item.Status == "SOURCE_REFUSED")
            {
                skipped.Add(new SkippedRule(item.Order!.Value,
                    item.Id, 0, item.SourceRefusal!));
                continue;
            }
            if (item.Eligible <= 0) continue;
            string next = Path.Combine(output, "_stages",
                item.Order!.Value.ToString("D2") + "-" + item.Id);
            string? basePreview = Directory.Exists(final) ? final : null;
            int edits;
            try
            {
                edits = item.Id switch
            {
                "fixed-cname-index" =>
                    GenericCallbackRules.Preview(game, analysis, next),
                "literal-action-filter" =>
                    GenericActionFilterPass.Preview(game, analysis, next),
                "late-system-read" =>
                    GenericLateSystemReadPass.Preview(game, analysis, next,
                        basePreview),
                "angle-sort" =>
                    GenericAngleSortPass.Preview(game, analysis, next),
                "source-cname-membership" =>
                    GenericMembershipPass.PreviewOrdered(game, analysis,
                        next, basePreview),
                "blackboard-definitions" =>
                    GenericBlackboardDefinitionPass.Preview(game, analysis,
                        next, basePreview),
                "pure-constant-slices" =>
                    CallbackSemanticResolver.PreviewPureConstantSlices(game,
                        analysis, next, basePreview),
                "immutable-action-event-snapshot" =>
                    CallbackSemanticResolver.PreviewActionEventSnapshots(game,
                        analysis, next, basePreview),
                "shared-player-signal" =>
                    GenericSharedPlayerSignalPass.Preview(game,
                        analysis, next, basePreview),
                "stable-array-bound" =>
                    GenericStableArrayBoundPass.Preview(game,
                        analysis, next, basePreview),
                _ => throw new InvalidDataException(
                    "REDSCRIPT_PLAN_UNKNOWN_GENERATOR")
            };
            }
            catch (InvalidDataException ex) when (IsLocalSourceRefusal(ex.Message))
            {
                // A malformed or already-edited author source can invalidate
                // one approved family. Do not silently ship that family's
                // partial output; continue with independent later families.
                // Plan/capture/source drift and generator defects still fail.
                if (Directory.Exists(next))
                    Directory.Delete(next, recursive: true);
                skipped.Add(new SkippedRule(item.Order!.Value, item.Id,
                    item.Eligible ?? 0, ex.Message));
                continue;
            }
            if (edits <= 0 || edits > item.Eligible)
                throw new InvalidDataException(
                    "REDSCRIPT_PLAN_GENERATOR_COUNT_MISMATCH: " + item.Id);
            string stagedScripts = Path.Combine(next, "r6", "scripts");
            if (!Directory.Exists(stagedScripts))
                throw new InvalidDataException(
                    "REDSCRIPT_PLAN_GENERATOR_NO_SOURCES: " + item.Id);
            var collisions = new List<string>();
            int newlyCreated = 0, composed = 0;
            foreach (string file in Directory.EnumerateFiles(
                stagedScripts, "*.reds", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(stagedScripts, file);
                string live = Path.GetFullPath(Path.Combine(
                    game, "r6", "scripts", rel));
                string destination = Path.GetFullPath(Path.Combine(
                    final, "r6", "scripts", rel));
                if (!IsInside(destination, final) ||
                    !IsInside(live, game) ||
                    !File.Exists(live))
                    throw new InvalidDataException(
                        "REDSCRIPT_PLAN_GENERATED_SOURCE_PATH_INVALID");
                if (File.Exists(destination) &&
                    item.Id is not ("late-system-read" or
                        "source-cname-membership" or
                        "blackboard-definitions" or
                        "pure-constant-slices" or "immutable-action-event-snapshot" or
                        "shared-player-signal" or "stable-array-bound"))
                    throw new InvalidDataException(
                        "REDSCRIPT_PLAN_UNCOMPOSABLE_SOURCE_COLLISION: " +
                        item.Id + " " + rel);
                if (File.Exists(destination))
                {
                    composed++;
                }
                else
                {
                    newlyCreated++;
                    Directory.CreateDirectory(Path.GetDirectoryName(
                        destination)!);
                }
                File.Copy(file, destination, overwrite: composed > 0);
            }
            files += newlyCreated;
            expressions += edits;
            deferred += collisions.Count;
            results.Add(new(item.Order!.Value, item.Id, edits,
                newlyCreated, composed, collisions.ToArray()));
        }

        // The post-generic EFFECTIVE source is the Phase 2 semantic baseline.
        // The same source-aware generator is used by the fast independent
        // Phase 2 checkpoint and the one-invocation combined GCO Generate.
        // No historical semantic pass, repair pass or whole-file overwrite.
        string phase2Root = Path.Combine(output, "_stages", "11-dvcore-footsteps");
        var footstep = DigitalVixenFootstepsPass.Generate(game, phase2Root,
            Directory.Exists(final) ? final : null);
        if (footstep.Status == "APPLIED")
        {
            string rel = footstep.SourcePath ??
                throw new InvalidDataException("DVCORE_STAGE_SOURCE_PATH_MISSING");
            string source = Path.GetFullPath(Path.Combine(phase2Root,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            string target = Path.GetFullPath(Path.Combine(final,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(source, phase2Root) || !IsInside(target, final) ||
                !File.Exists(source) || !rel.StartsWith("r6/scripts/",
                    StringComparison.Ordinal) ||
                !target.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DVCORE_SEMANTIC_OUTPUT_UNSAFE");
            bool existing = File.Exists(target);
            if (existing)
            {
                // Semantic changes must retain all prior generic work even
                // when the same owner file receives multiple accepted passes.
                var earlier = System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(target), @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
                    .Select(x => x.Value).Distinct(StringComparer.Ordinal);
                string rewritten = File.ReadAllText(source);
                if (earlier.Any(marker => !rewritten.Contains(marker,
                        StringComparison.Ordinal)))
                    throw new InvalidDataException("DVCORE_LOST_PRIOR_GENERIC_MARKER");
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            }
            File.Copy(source, target, overwrite: existing);
            files += existing ? 0 : 1;
            expressions += footstep.AppliedEdits;
            results.Add(new(11, "dvcore-footsteps", footstep.AppliedEdits,
                existing ? 0 : 1, existing ? 1 : 0, []));
        }
        else if (footstep.Status == "REFUSED")
        {
            // Only the affected mod-specific operation is deferred.
            skipped.Add(new(11, "dvcore-footsteps", 1,
                footstep.Reason ?? "DVCORE_SEMANTIC_INCOMPATIBLE"));
        }
        else if (footstep.Status is not ("COMPLETE" or "NOT_APPLICABLE"))
            throw new InvalidDataException("DVCORE_UNKNOWN_GENERATOR_STATUS");

        if (!string.Equals(document.SourceStackSha256,
            admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("REDSCRIPT_PLAN_SOURCE_CHANGED_DURING_STAGE");

        var result = new StageResult("gco.redscript.staged-plan.v1",
            document.SourceStackSha256, files, expressions,
            deferred, false, results.ToArray())
        {
            Skipped = skipped.ToArray()
        };
        File.WriteAllText(Path.Combine(output, "stage-result.json"),
            JsonSerializer.Serialize(result, JsonOptions),
            new UTF8Encoding(false));
        return result;
    }

    private static void ScanRule(string id, string game, string report)
    {
        switch (id)
                {
                    case "fixed-cname-index":
                        GenericCallbackRules.Scan(game, report); break;
                    case "literal-action-filter":
                        GenericActionFilterPass.Scan(game, report); break;
                    case "late-system-read":
                        GenericLateSystemReadPass.Scan(game, report); break;
                    case "angle-sort":
                        GenericAngleSortPass.Scan(game, report); break;
                    case "source-cname-membership":
                        GenericMembershipPass.Scan(game, report); break;
                    case "blackboard-definitions":
                        GenericBlackboardDefinitionPass.Scan(game, report); break;
                    case "pure-constant-slices":
                        CallbackSemanticResolver.ScanPureConstantSlices(game, report); break;
                    case "immutable-action-event-snapshot":
                        CallbackSemanticResolver.ScanActionEventSnapshots(game, report); break;
                    case "shared-player-signal":
                        GenericSharedPlayerSignalPass.Scan(game, report); break;
                    case "stable-array-bound":
                        GenericStableArrayBoundPass.Scan(game, report); break;
                    default: throw new InvalidOperationException(
                        "REDSCRIPT_PLAN_UNKNOWN_IMPLEMENTED_RULE");
                }
    }

    private static bool IsLocalSourceRefusal(string message)
    {
        // Deliberately narrow. Unknown exceptions and postcondition failures
        // are developer defects or global integrity concerns, never skips.
        // These codes describe individual unsupported author-source shapes.
        string[] recognized =
        [
            "ARRAY_BOUND_PARTIAL_", "ARRAY_BOUND_DUPLICATE_ALIAS",
            "ARRAY_BOUND_MODIFIED_BODY",
            "BBDEFS_SEQUENCE_PARTIAL_", "BBDEFS_SEQUENCE_DUPLICATE_ALIAS",
            "BBDEFS_PARTIAL_OR_DRIFTED_",
            "ANGLE_SORT_UNSAFE_INDENT",
            "ANGLE_SORT_COMMENT_IN_REWRITTEN_SCOPE",
            "ANGLE_SORT_UNOWNED_OPTIMIZED_SHAPE",
            "ANGLE_SORT_PARTIAL_OR_DRIFTED_GENERATED_MARKER",
            "LATE_SYSTEM_READ_INDENT_UNPROVEN",
            "LATE_SYSTEM_READ_UNOWNED_OPTIMIZED_SHAPE",
            "LATE_SYSTEM_READ_PARTIAL_OR_DRIFTED_GENERATED_MARKER",
            "CNAME_MEMBERSHIP_COMPOSITION_DRIFT_OR_AMBIGUITY",
            "SHARED_PLAYER_MODULE_HEADER_MISSING",
            "Unbalanced REDscript method",
            "Unbalanced REDscript source block"
        ];
        return recognized.Any(code =>
            message.StartsWith(code, StringComparison.Ordinal));
    }

    private static bool IsInside(string child, string parent)
    {
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        child = Path.GetFullPath(child);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadString(JsonElement root, string key) =>
        root.GetProperty(key).GetString() ??
        throw new InvalidDataException("REDSCRIPT_PLAN_MISSING_" + key);

    private static int ReadInt(JsonElement root, string key) =>
        root.GetProperty(key).GetInt32();
}
