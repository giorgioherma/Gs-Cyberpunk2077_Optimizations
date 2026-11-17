using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// R5 generic quest-fact reader/writer/listener inventory. Runs across every
/// current REDscript method with the ONE existing CallbackSemanticResolver
/// parser and SourceAdmission source seal. A lexical CName argument is only an
/// exact name candidate, not evidence of receiver identity, callback ordering,
/// complete engine writes or authorization to coalesce subscriptions.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    internal sealed record QuestFactSummary(int SourceFiles, int SourceMethods,
        int FactCallsites, int LiteralCallsites, int DynamicCallsites,
        int LiteralFactNames, int CrossOwnerListenerFactNames);

    private sealed record FactSite(
        string Path, string Owner, string Method, int Line, string Operation,
        string? FactName, string Resolution, long? MeasuredOutgoingEdges);

    internal static QuestFactSummary ScanQuestFactDependencies(
        string gameRoot, string outputPath, string? analysisPath)
    {
        var game = Path.GetFullPath(gameRoot);
        var output = Path.GetFullPath(outputPath);
        var analysis = analysisPath is null ? null : Path.GetFullPath(analysisPath);
        if (Inside(output, game) || File.Exists(output) || Directory.Exists(output) ||
            (analysis is not null && output.Equals(analysis,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("QUEST_FACT_OUTPUT_NOT_ISOLATED");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (_, methods) = ReadAllMethodSources(game, admission);
        var measured = analysis is null
            ? new Dictionary<string, Evidence>(StringComparer.Ordinal)
            : ReadEvidence(analysis, admission, methods);
        var sites = new List<FactSite>();
        var operations = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int literal = 0, dynamic = 0;

        foreach (var m in methods)
        {
            var tokens = m.File.Tokens;
            for (int i = m.Open + 1; i + 2 < m.Close; i++)
            {
                string op = tokens[i].Value;
                if (op is not ("GetFact" or "GetFactStr" or
                    "SetFact" or "SetFactStr" or
                    "RegisterListener" or "UnregisterListener"))
                    continue;
                if (tokens[i + 1].Value != "(") continue;
                // Direct n"fact" literals only. Other expressions are
                // explicitly reported dynamic; never infer their runtime value.
                string? fact = null;
                if (i + 3 < m.Close && tokens[i + 2].Value == "n" &&
                    tokens[i + 3].Value == "<literal>")
                {
                    var literalToken = tokens[i + 3];
                    string raw = m.File.Text[literalToken.Start..literalToken.End];
                    if (raw.Length >= 2 && raw[0] == '"' &&
                        raw[^1] == '"' && !raw.Contains('\\'))
                        fact = raw[1..^1];
                }
                if (fact is null) dynamic++; else literal++;
                operations[op] = operations.GetValueOrDefault(op) + 1;
                measured.TryGetValue(m.Identity, out var evidence);
                sites.Add(new FactSite(m.File.Relative, m.Owner, m.Name,
                    tokens[i].Line, op, fact, fact is null ?
                    "DYNAMIC_OR_NON_CNAME_LITERAL_UNPROVEN" :
                    "EXACT_LITERAL_NAME_RECEIVER_UNVERIFIED",
                    evidence?.OutgoingEdges));
            }
        }

        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("QUEST_FACT_SOURCE_CHANGED_DURING_SCAN");

        var literalSites = sites.Where(x => x.FactName is not null).ToArray();
        var factGraph = literalSites.GroupBy(x => x.FactName!,
                StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var listenerOwners = g.Where(x => x.Operation == "RegisterListener")
                    .Select(x => x.Path.Split('/')[2])
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                return new
                {
                    fact = g.Key,
                    sourceReaderOwners = g.Where(x => x.Operation.StartsWith("Get", StringComparison.Ordinal))
                        .Select(x => x.Path.Split('/')[2]).Distinct(StringComparer.Ordinal)
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    sourceWriterOwners = g.Where(x => x.Operation.StartsWith("Set", StringComparison.Ordinal))
                        .Select(x => x.Path.Split('/')[2]).Distinct(StringComparer.Ordinal)
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    literalListenerOwners = listenerOwners,
                    crossOwnerRegistered = listenerOwners.Length > 1,
                    registeredListenerCallsites = g.Count(x => x.Operation == "RegisterListener"),
                    unregisterCallsites = g.Count(x => x.Operation == "UnregisterListener"),
                    readerCallsites = g.Count(x => x.Operation.StartsWith("Get", StringComparison.Ordinal)),
                    writerCallsites = g.Count(x => x.Operation.StartsWith("Set", StringComparison.Ordinal)),
                    everyWriterAndAliasProven = false,
                    receiverTypeProven = false,
                    listenerCallbackReturnAndOrderProven = false,
                    sharedSubscriptionAuthorized = false
                };
            }).ToArray();

        var report = new
        {
            schemaVersion = "gco.redscript.quest-fact.graph.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = admission.InspectedFileCount,
            sourceMethods = methods.Count,
            scope = "ALL_LIVE_REDSCRIPT_METHODS_NOT_JUST_1200_REFRESH_NAMES",
            evidenceKind = analysis is null ? "SOURCE_ONLY" :
                "SEALED_F11_SOURCE_MATCHED_METHOD_OUTGOING_EDGES",
            factCallsites = sites.Count,
            literalCallsites = literal,
            dynamicOrUnresolvedCallsites = dynamic,
            distinctLiteralFactNames = factGraph.Length,
            factsWithCrossOwnerLiteralRegistrations =
                factGraph.Count(g => g.crossOwnerRegistered),
            operations,
            allDynamicFactNamesRemainUnproven = true,
            capturedEdgesAreNotFactAccessCountsOrPredictedSavings = true,
            sourcePathsRemainInPrivateOutputOnly = true,
            completeProducerAndMutationGraph = false,
            engineCallbackSemanticsValidated = false,
            generationAuthorized = false,
            sourceWrites = false,
            generatedFiles = 0,
            facts = factGraph,
            callsites = sites.OrderBy(s => s.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.Line).Select(s => new {
                    s.Path, s.Owner, s.Method, s.Line, s.Operation, s.FactName,
                    s.Resolution, s.MeasuredOutgoingEdges,
                    engineReceiverProven = false,
                    stateChangeCallbackGuaranteed = false,
                    originalCallbackBehaviorPreserved = false,
                    generationAuthorized = false
                })
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(false));
        return new(admission.InspectedFileCount, methods.Count, sites.Count,
            literal, dynamic, factGraph.Length,
            factGraph.Count(g => g.crossOwnerRegistered));
    }
}
