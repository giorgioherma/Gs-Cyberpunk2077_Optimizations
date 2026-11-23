using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Read-only R5 callback-work discovery. Reuses the canonical method/token
/// index and verified GRSP callsite join; never interprets call edges as
/// callback entries or promotes an action selector into an exhaustive filter.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    internal sealed record GenericCallbackWorkSummary(
        int SourceFiles, int OnActionMethods, int RankedMethods,
        int CrossOwnerPatterns, int MeasuredMethods);

    private sealed record CallbackWorkSite(
        MethodSource Method, string SourceOwner, string Pattern,
        string[] ObservedPatterns, string[] ForwardedCalls,
        int NameReads, int TypeReads, int ActionPredicateCalls,
        bool LookupBeforeSelector,
        string[] PreSelectorReads, string[] Blockers, Evidence? Measure,
        bool RankingIncluded);

    internal static GenericCallbackWorkSummary ScanGenericCallbackWork(
        string gameRoot, string analysisPath, string outputPath,
        IReadOnlySet<string> rankingExcludedOwners)
    {
        string game = Path.GetFullPath(gameRoot);
        string analysis = Path.GetFullPath(analysisPath);
        string output = Path.GetFullPath(outputPath);
        if (Inside(output, game) || File.Exists(output) ||
            Directory.Exists(output) || output.Equals(analysis,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CALLBACK_WORK_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (files, methods) = ReadAllMethodSources(game, admission);
        // Existing owner contract checks collection provenance, source stack
        // fingerprint, row alignment, and each method/line/callsite SHA.
        var evidence = ReadEvidence(analysis, admission, methods);
        var findings = new List<CallbackWorkSite>();
        foreach (var method in methods.Where(m =>
            m.Name == "OnAction" &&
            InputActionSignature(m.File.Tokens, m)))
        {
            var t = method.File.Tokens;
            var getters = new List<(int Index, string Name)>();
            var forwards = new SortedSet<string>(StringComparer.Ordinal);
            var preReads = new SortedSet<string>(StringComparer.Ordinal);
            int earliest = int.MaxValue;
            int actionPredicateCalls = 0;
            for (int i = method.Open + 1; i + 5 < method.Close; i++)
            {
                if (t[i].Value != "ListenerAction" ||
                    t[i + 1].Value != "." ||
                    t[i + 3].Value != "(" ||
                    t[i + 4].Value != "action") continue;
                string operation = t[i + 2].Value;
                if ((operation is "GetName" or "GetType") &&
                    t[i + 5].Value == ")")
                {
                    getters.Add((i, operation));
                    earliest = Math.Min(earliest, i);
                }
                else if (operation is "IsAction" or "IsButtonJustPressed"
                    or "IsButtonJustReleased" or "IsButtonJustHeld"
                    or "IsButtonJustTapped" or "IsButtonJustHoldComplete")
                {
                    // Action/button predicates are selectors for discovery,
                    // not proof of a complete action-name admission set.
                    actionPredicateCalls++;
                    earliest = Math.Min(earliest, i);
                }
            }
            int nameReads = getters.Count(x => x.Name == "GetName");
            int typeReads = getters.Count(x => x.Name == "GetType");
            for (int i = method.Open + 1; i + 3 < method.Close; i++)
            {
                if (!Ident(t[i].Value) || t[i + 1].Value != "." ||
                    !Ident(t[i + 2].Value) || t[i + 3].Value != "(")
                    continue;
                string receiver = t[i].Value;
                string fn = t[i + 2].Value;
                if (i < earliest &&
                    (fn is "Get" or "GetQuestsSystem" or
                       "GetScriptableSystemsContainer" or "GetStatsSystem" or
                       "GetPlayerSystem" or "GetFact" or "GetStatValue" ||
                     fn.StartsWith("Get", StringComparison.Ordinal) &&
                     receiver == "GameInstance"))
                    preReads.Add(receiver + "." + fn);
                if (receiver == "ListenerAction") continue;
                int close = Match(t, i + 3, "(", ")");
                if (close >= method.Close) continue;
                bool forwardsAction = false;
                for (int k = i + 4; k < close; k++)
                    if (t[k].Value == "action" &&
                        (k == i + 4 || t[k - 1].Value != "."))
                        forwardsAction = true;
                if (forwardsAction)
                    forwards.Add(receiver + "." + fn);
            }
            // With no local selector, a forwarded event may be selected
            // by another method. The receiver/callee and effects are not
            // assumed resolved merely because a method name looks familiar.
            var patterns = new SortedSet<string>(StringComparer.Ordinal);
            if (nameReads >= 2 || typeReads >= 2)
                patterns.Add("REPEATED_ACTION_METADATA");
            if (earliest == int.MaxValue && forwards.Count > 0)
                patterns.Add("DELEGATES_WITHOUT_LOCAL_SELECTOR");
            if (preReads.Count > 0 && earliest != int.MaxValue)
                patterns.Add("ENGINE_LOOKUP_BEFORE_ACTION_SELECTOR");
            if (earliest != int.MaxValue && preReads.Count == 0)
                patterns.Add("ACTION_SELECTOR_WITHOUT_EARLY_ENGINE_LOOKUP");
            if (earliest == int.MaxValue && forwards.Count == 0)
                patterns.Add("NO_LOCAL_ACTION_SELECTOR_OR_FORWARD");
            string primary = patterns.Contains("ENGINE_LOOKUP_BEFORE_ACTION_SELECTOR")
                ? "ENGINE_LOOKUP_BEFORE_ACTION_SELECTOR" :
                patterns.Contains("DELEGATES_WITHOUT_LOCAL_SELECTOR")
                ? "DELEGATES_WITHOUT_LOCAL_SELECTOR" :
                patterns.Contains("REPEATED_ACTION_METADATA")
                ? "REPEATED_ACTION_METADATA" :
                patterns.First();
            var blockers = new SortedSet<string>(StringComparer.Ordinal)
            {
                "ACTION_SELECTOR_COMPLETENESS_UNPROVEN",
                "CALLBACK_ENTRY_COUNTS_UNAVAILABLE",
                "NO_MEASURED_REMOVABLE_WORK"
            };
            if (forwards.Count > 0)
                blockers.Add("FORWARDED_CALLEE_BINDING_AND_EFFECTS_UNPROVEN");
            if (method.Annotation is "wrapMethod" or "replaceMethod")
                blockers.Add("WRAPPER_AND_REPLACEMENT_EFFECTS_UNPROVEN");
            if (preReads.Count > 0)
                blockers.Add("EARLIER_GETTER_SIDE_EFFECT_ORDER_UNPROVEN");
            if (nameReads >= 2 || typeReads >= 2)
                blockers.Add("CHECK_EXISTING_ACTION_EVENT_SNAPSHOT_V1");
            evidence.TryGetValue(method.Identity, out Evidence? measured);
            if (measured is null)
                blockers.Add("NO_SOURCE_MATCHED_CAPTURE_CALLSITES");
            string owner = OwnerFolder(method.File.Relative);
            findings.Add(new CallbackWorkSite(method, owner, primary,
                patterns.ToArray(), forwards.ToArray(), nameReads,
                typeReads, actionPredicateCalls, preReads.Count > 0,
                preReads.ToArray(), blockers.ToArray(), measured,
                !rankingExcludedOwners.Contains(owner)));
        }
        var ranking = findings.Where(x => x.RankingIncluded).ToArray();
        var groups = ranking.GroupBy(x => x.Pattern)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => new
            {
                pattern = x.Key,
                sourceOwnerFolders = x.Select(y => y.SourceOwner)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                callbackMethods = x.Count(),
                sourceMatchedMeasuredMethods = x.Count(y => y.Measure is not null),
                sourceMatchedOutgoingCallEdges = x.Sum(y =>
                    y.Measure?.OutgoingEdges ?? 0),
                sourceMatchedCallsiteExclusiveMs = x.Sum(y =>
                    y.Measure?.InstrumentedExclusiveMs ?? 0),
                crossOwnerStructure = x.Select(y => y.SourceOwner)
                    .Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 3,
                optimizationAdmitted = false
            }).ToArray();
        var ranked = ranking.OrderByDescending(x =>
                x.Measure?.InstrumentedExclusiveMs ?? -1)
            .ThenBy(x => x.Method.File.Relative, StringComparer.Ordinal)
            .ThenBy(x => x.Method.Line).ToArray();
        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CALLBACK_WORK_SOURCE_CHANGED_DURING_SCAN");
        var report = new
        {
            schemaVersion = "gco.redscript.generic-callback-work.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = files.Count,
            analyzedOnActionMethods = findings.Count,
            rankedOnActionMethods = ranked.Length,
            excludedFromRankingMethods = findings.Count - ranked.Length,
            rankingExclusionsAffectGeneratorAdmission = false,
            rankingExcludedOwnerFolders = rankingExcludedOwners
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            sourceMatchedMeasuredMethods = ranked.Count(x => x.Measure is not null),
            crossOwnerPatterns = groups.Count(x => x.crossOwnerStructure),
            patterns = groups,
            // Include all source owners in discovery, including excluded
            // measured owners, so a generic rule cannot become a mod whitelist.
            callbacks = findings.OrderBy(x => x.Method.File.Relative,
                StringComparer.Ordinal).ThenBy(x => x.Method.Line)
                .Select(x => new
                {
                    source = new { path = x.Method.File.Relative,
                        line = x.Method.Line, owner = x.Method.Owner,
                        sourceOwnerFolder = x.SourceOwner },
                    pattern = x.Pattern,
                    observedPatterns = x.ObservedPatterns,
                    rankingIncluded = x.RankingIncluded,
                    actionNameReadSites = x.NameReads,
                    actionTypeReadSites = x.TypeReads,
                    directActionPredicateSites = x.ActionPredicateCalls,
                    observedActionForwarding = x.ForwardedCalls,
                    preSelectorEngineReads = x.PreSelectorReads,
                    preSelectorReadPresence = x.LookupBeforeSelector,
                    blockers = x.Blockers,
                    sourceMatchedOutgoingVmCallEdges =
                        x.Measure?.OutgoingEdges,
                    sourceMatchedCallsiteExclusiveMs =
                        x.Measure?.InstrumentedExclusiveMs,
                    sourceMatchedCallsiteRows =
                        x.Measure?.MatchedCallsites,
                    captureMatchesOnlyOriginalCallsites = true,
                    callbackEntries = (long?)null,
                    irrelevantActionFraction = (double?)null,
                    predictedSavingsMs = (double?)null,
                    generationAuthorized = false
                }).ToArray(),
            sourceMethodAndTokenParser = "CallbackSemanticResolver.ReadAllMethodSources",
            evidenceReader = "CallbackSemanticResolver.ReadEvidence",
            performanceAttribution = "CAPTURED_VM_CALLSITE_EXCLUSIVE_ONLY_NOT_CALLBACK_ENTRIES",
            anyFullForwardedCalleeEffectsProven = false,
            canSkipCallback = false,
            generationAuthorized = false,
            originalSourcesModified = false,
            gameplayValidated = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return new(files.Count, findings.Count, ranked.Length,
            groups.Count(x => x.crossOwnerStructure),
            ranked.Count(x => x.Measure is not null));
    }

    private static string OwnerFolder(string relative)
    {
        const string prefix = "r6/scripts/";
        var tail = relative.StartsWith(prefix, StringComparison.Ordinal)
            ? relative[prefix.Length..] : relative;
        int slash = tail.IndexOf('/');
        return slash >= 0 ? tail[..slash] : "(root)";
    }
}
