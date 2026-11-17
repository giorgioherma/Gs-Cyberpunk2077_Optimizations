using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Source-wide R5 refresh/maintenance evidence. Reuses the authoritative
/// CallbackSemanticResolver methods, lexer, provenance and callsite join.
/// It ranks possible full-body work to eliminate, but never claims purity,
/// complete writers, callback entry counts, or generation authorization.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    internal sealed record SharedWorkSummary(int SourceFiles, int ExaminedMethods,
        int SourceMatchedMeasured, int Unmeasured, double DirectInstrumentedMs);

    internal static SharedWorkSummary ScanSharedWork(string gameRoot, string outputPath,
        string? analysisPath)
    {
        string root = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputPath);
        string? analysis = analysisPath is null ? null : Path.GetFullPath(analysisPath);
        if (Inside(output, root) || File.Exists(output) || Directory.Exists(output) ||
            (analysis is not null && output.Equals(analysis,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("SHARED_WORK_OUTPUT_NOT_ISOLATED");

        var source = new SourceAdmission(root);
        _ = source.AuditAll();
        // Parse once with the same source/type/method ownership logic as the
        // existing, independently tested CET-style callback resolver.
        var (files, methods) = ReadAllMethodSources(root, source);
        var evidence = analysis is null
            ? new Dictionary<string, Evidence>(StringComparer.Ordinal)
            : ReadEvidence(analysis, source, methods);

        var findings = new List<SharedWorkFinding>();
        var blockers = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var families = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var dependencyCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var triggerCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var primaryTriggerCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var measuredDependencyCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unmeasuredDependencyCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var measuredPrimaryCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var unmeasuredPrimaryCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        long matchedEdges = 0;
        double measuredMs = 0;
        int measured = 0, unmeasured = 0;

        foreach (var method in methods.Where(m => SharedWorkFamily(m.Name) is not null))
        {
            var t = method.File.Tokens;
            var body = t.Skip(method.Open + 1)
                .Take(method.Close - method.Open - 1).ToArray();
            string family = SharedWorkFamily(method.Name)!;
            families[family] = families.GetValueOrDefault(family) + 1;
            DirectDependencyProfile dependencies = InspectDirectDependencies(method, body);
            foreach (string category in dependencies.Categories)
                dependencyCounts[category] =
                    dependencyCounts.GetValueOrDefault(category) + 1;
            foreach (string trigger in dependencies.CandidateTriggers)
                triggerCounts[trigger] = triggerCounts.GetValueOrDefault(trigger) + 1;
            string primary = dependencies.PrimaryCandidateTrigger;
            primaryTriggerCounts[primary] =
                primaryTriggerCounts.GetValueOrDefault(primary) + 1;

            bool clock = body.Any(x => x.Value is "timeLeft" or "timeRemaining" or
                "deltaTime" or "dt" or "GetGameTime" or "GetSimTime" or
                "GetTime" or "GetTimeDilation" or "GetSystemTime" or "tickCount" or
                "m_duration" or "GetTimeRemaining");
            bool widget = body.Any(x => x.Value is "SetData" or "SetText" or
                "SetVisible" or "SetEffectParamValue" or "SetTimeRemaining" or
                "SetStackCount" or "Reparent" or "RemoveChild" or
                "SpawnFromLocal" or "GetController" or "SetOpacity" or
                "SetSize" or "SetTranslation" or "SetAnchor");
            bool eventEffects = body.Any(x => x.Value is "QueueEvent" or
                "RegisterListener" or "UnregisterListener" or
                "RegisterInputListener" or "UnregisterInputListener" or
                "DelayCallback" or "CancelCallback" or "StartEffectEvent" or
                "StopEffectEvent");
            bool arrayMutation = body.Any(x => x.Value is "ArrayPush" or "ArrayErase" or
                "ArrayClear" or "ArrayInsert" or "ArrayRemove" or
                "ArrayResize" or "ArrayPop");
            bool player = body.Any(x => x.Value is "GetPlayer" or
                "GetLocalPlayerMainGameObject" or "GetPlayerControlledObject" or
                "GetAttachedPlayer" or "HasAuthoritativePlayer");
            bool signal = body.Any(x => x.Value is "GetPlayerSignalGeneration" or
                "ChangedSince");
            bool earlyGuard = body.Take(45).Any(x => x.Value == "if");
            bool loops = body.Any(x => x.Value is "while" or "for");
            bool wrapped = method.Annotation is "wrapMethod" or "replaceMethod" ||
                body.Any(x => x.Value == "wrappedMethod");
            bool hasMemberRead = false;
            var memberNames = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = 0; i + 2 < body.Length; i++)
            {
                if (body[i].Value == "this" && body[i + 1].Value == "." &&
                    body[i + 2].Value.Length > 1 &&
                    (char.IsLetter(body[i + 2].Value[0]) ||
                     body[i + 2].Value[0] == '_'))
                {
                    hasMemberRead = true;
                    memberNames.Add(body[i + 2].Value);
                }
            }

            bool returnsValue = !t.Skip(method.SignatureClose + 1)
                .Take(method.Open - method.SignatureClose - 1)
                .Any(x => x.Value == "Void");

            // The first failed prerequisite is stable and useful for future
            // generator admission. No lexical observation is accepted as a
            // complete write-side invalidation or purity proof.
            evidence.TryGetValue(method.Identity, out Evidence? measure);
            var sourceBucket = measure is null ? unmeasuredDependencyCounts : measuredDependencyCounts;
            foreach (string category in dependencies.Categories)
                sourceBucket[category] = sourceBucket.GetValueOrDefault(category) + 1;
            var primaryBucket = measure is null ? unmeasuredPrimaryCounts : measuredPrimaryCounts;
            primaryBucket[primary] = primaryBucket.GetValueOrDefault(primary) + 1;
            string reason = measure is null ? "SOURCE_MATCHED_MEASUREMENT_MISSING" :
                wrapped ? "WRAPPER_SIDE_EFFECT_OR_ORDER_UNPROVEN" :
                clock ? "TIME_OR_ANIMATION_DEPENDENCY" :
                eventEffects ? "EVENT_DELIVERY_SIDE_EFFECT_UNPROVEN" :
                widget ? "UI_WIDGET_VALUE_OR_LIFETIME_UNPROVEN" :
                arrayMutation ? "MUTATOR_INCOMPLETE" :
                hasMemberRead ? "MUTATOR_INCOMPLETE" :
                player ? "LIFETIME_UNKNOWN" :
                returnsValue ? "RETURN_CONSUMPTION_UNPROVEN" :
                "PURE_INPUT_DEPENDENCIES_NOT_PROVEN";

            blockers[reason] = blockers.GetValueOrDefault(reason) + 1;
            if (measure is null) unmeasured++;
            else
            {
                measured++;
                matchedEdges = checked(matchedEdges + measure.OutgoingEdges);
                measuredMs += measure.InstrumentedExclusiveMs;
            }
            findings.Add(new SharedWorkFinding(
                method.File.Relative, method.Owner, method.Name, method.Line,
                family, reason, measure?.OutgoingEdges, measure?.InstrumentedExclusiveMs,
                measure?.MatchedCallsites, clock, widget, eventEffects,
                arrayMutation, hasMemberRead, player, signal, earlyGuard, loops,
                wrapped, returnsValue, memberNames.ToArray(), dependencies));
        }

        if (!string.Equals(source.SourceStackSha256,
            source.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_WORK_SOURCE_CHANGED_DURING_SCAN");

        // Rank measured work by source-aligned outgoing calls. This is
        // discovery priority, never an estimate of skipped CPU or frames.
        var ranked = findings.OrderByDescending(x => x.DirectMs ?? -1)
            .ThenByDescending(x => x.OutgoingEdges ?? -1)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Line).ToArray();

        var report = new
        {
            schemaVersion = "gco.redscript.shared-work.scan.v1",
            scope = "ALL_CURRENT_REDSCRIPT_REFRESH_AND_MAINTENANCE_METHODS",
            evidenceKind = analysis is null ? "NOT_CAPTURED" :
                "SOURCE_ALIGNED_OUTGOING_VM_CALLSITES_ONLY",
            sourceFiles = source.InspectedFileCount,
            sourceStackSha256 = source.SourceStackSha256,
            recognizedMethods = methods.Count,
            candidateMethods = findings.Count,
            sourceMatchedMeasured = measured,
            unmeasured,
            sourceMatchedOutgoingEdges = matchedEdges,
            sourceMatchedDirectInstrumentedMs = measuredMs,
            firstFailedPrerequisiteCounts = blockers,
            familyCounts = families,
            directDependencyCategoryCounts = dependencyCounts,
            candidateSharedTriggerCounts = triggerCounts,
            primaryCandidateSharedTriggerCounts = primaryTriggerCounts,
            measuredDependencyCategoryCounts = measuredDependencyCounts,
            unmeasuredDependencyCategoryCounts = unmeasuredDependencyCounts,
            measuredPrimaryTriggerCounts = measuredPrimaryCounts,
            unmeasuredPrimaryTriggerCounts = unmeasuredPrimaryCounts,
            sourceDependencyCategoriesAreLexicalObservations = true,
            sharedTriggersAreHypothesesNotPublishers = true,
            directInstanceFieldsAreNotCompleteWriterGraph = true,
            helperCallsHaveUnresolvedTransitiveEffects = true,
            parser = "CALLBACK_SEMANTIC_RESOLVER_AUTHORITATIVE",
            captureRowsAreOutgoingEdgesNotMethodEntries = true,
            directInstrumentedMsNotPredictedSavingsOrTotalCpu = true,
            mutatorAndObserverProofIncomplete = true,
            frameworkUnknownMustUseOriginalBehavior = true,
            sourceState = "CLASSIFICATION_ONLY",
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            sourceWrites = false,
            generatedFiles = 0,
            candidates = ranked.Select(x => new
            {
                source = new { path = x.Path, type = x.Owner, method = x.Name, line = x.Line },
                family = x.Family,
                firstFailedPrerequisite = x.Reason,
                measuredOutgoingEdges = x.OutgoingEdges,
                directInstrumentedMs = x.DirectMs,
                sourceMatchedCallsiteRows = x.CallsiteRows,
                callbackEntries = (long?)null,
                expectedSavingsMs = (double?)null,
                directDependencies = new
                {
                    instanceFields = x.Dependencies.InstanceFields,
                    instanceMethodCalls = x.Dependencies.InstanceMethodCalls,
                    usedCallbackParameters = x.Dependencies.UsedCallbackParameters,
                    observedCategoryHints = x.Dependencies.Categories,
                    possibleSharedTriggers = x.Dependencies.CandidateTriggers,
                    primaryCandidateSharedTrigger = x.Dependencies.PrimaryCandidateTrigger,
                    completeCallGraphProven = false,
                    completeWriterGraphProven = false,
                    sharedTriggerProven = false
                },
                sourceHints = new
                {
                    timeOrAnimation = x.Clock,
                    widgetEffects = x.Widget,
                    eventDelivery = x.EventEffects,
                    arrayMutation = x.ArrayMutation,
                    memberAccess = x.MemberRead,
                    playerHandle = x.Player,
                    versionOrGenerationRead = x.Signal,
                    earlyGuardPresent = x.EarlyGuard,
                    loopsPresent = x.Loops,
                    wrapper = x.Wrapped,
                    returnValueMayBeConsumed = x.ReturnsValue,
                    memberNames = x.Members
                },
                completeWriterSetProven = false,
                valueEqualityProven = false,
                callbackLifecycleProven = false,
                safeToSkipBody = false,
                generationAuthorized = false
            })
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = false }), new UTF8Encoding(false));
        return new(files.Count, findings.Count, measured, unmeasured, measuredMs);
    }

    private sealed record SharedWorkFinding(
        string Path, string Owner, string Name, int Line, string Family, string Reason,
        long? OutgoingEdges, double? DirectMs, int? CallsiteRows,
        bool Clock, bool Widget, bool EventEffects, bool ArrayMutation,
        bool MemberRead, bool Player, bool Signal, bool EarlyGuard,
        bool Loops, bool Wrapped, bool ReturnsValue, string[] Members,
        DirectDependencyProfile Dependencies);

    // Broad lexical family only; all real sources are examined and rejected
    // with diagnostics rather than relying on a fixed mod or method allowlist.
    private static string? SharedWorkFamily(string name)
    {
        if (name.Contains("Refresh", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rebuild", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Populate", StringComparison.OrdinalIgnoreCase))
            return "REFRESH_REBUILD";
        if (name.Contains("Update", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Render", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Redraw", StringComparison.OrdinalIgnoreCase))
            return "UPDATE_RENDER";
        if (name.Equals("Tick", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith("Tick", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("Tick", StringComparison.OrdinalIgnoreCase))
            return "TICK_MAINTENANCE";
        return null;
    }
}
