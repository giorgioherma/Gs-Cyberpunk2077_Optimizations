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
        string? FactName, string Resolution, long? MeasuredOutgoingEdges,
        bool QuestReceiverSourceTyped);

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
                bool typedReceiver = SourceDeclaresQuestReceiver(m, i);
                sites.Add(new FactSite(m.File.Relative, m.Owner, m.Name,
                    tokens[i].Line, op, fact, fact is null ?
                    "DYNAMIC_OR_NON_CNAME_LITERAL_UNPROVEN" :
                    typedReceiver ? "EXACT_LITERAL_DECLARED_QUEST_SYSTEM_RECEIVER" :
                    "EXACT_LITERAL_NAME_RECEIVER_UNVERIFIED",
                    evidence?.OutgoingEdges, typedReceiver));
            }
        }

        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("QUEST_FACT_SOURCE_CHANGED_DURING_SCAN");

        // RegisterListener also belongs to UI, resources and other systems:
        // only source-declared QuestsSystem receivers count as fact watchers.
        // Unknown receiver calls are visible as rejected callsite evidence.
        var literalSites = sites.Where(x => x.FactName is not null &&
            (x.Operation is not ("RegisterListener" or "UnregisterListener") ||
             x.QuestReceiverSourceTyped)).ToArray();
        var factGraph = literalSites.GroupBy(x => x.FactName!,
                StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g =>
            {
                var listenerOwners = g.Where(x => x.Operation == "RegisterListener")
                    .Select(x => ScriptOwner(x.Path))
                    .Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                return new
                {
                    fact = g.Key,
                    sourceReaderOwners = g.Where(x => x.Operation.StartsWith("Get", StringComparison.Ordinal))
                        .Select(x => ScriptOwner(x.Path)).Distinct(StringComparer.Ordinal)
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray(),
                    sourceWriterOwners = g.Where(x => x.Operation.StartsWith("Set", StringComparison.Ordinal))
                        .Select(x => ScriptOwner(x.Path)).Distinct(StringComparer.Ordinal)
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
            lexicalListenerCallsitesReceiverUnverified =
                sites.Count(x => (x.Operation is "RegisterListener" or "UnregisterListener") &&
                    !x.QuestReceiverSourceTyped),
            literalQuestListenerCallsitesSourceTyped =
                sites.Count(x => x.QuestReceiverSourceTyped &&
                    x.FactName is not null &&
                    (x.Operation is "RegisterListener" or "UnregisterListener")),
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
                    questReceiverSourceTyped = s.QuestReceiverSourceTyped,
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
    private static string ScriptOwner(string path)
    {
        var segments = path.Split('/');
        return segments.Length >= 4 ? segments[2] : "ROOT_LEVEL";
    }

    private static bool SourceDeclaresQuestReceiver(MethodSource method, int opIndex)
    {
        var t = method.File.Tokens;
        if (opIndex < 2 || t[opIndex - 1].Value != ".") return false;
        string receiver = t[opIndex - 2].Value;
        if (!Ident(receiver)) return false;
        // Reject untyped receiver names. More importantly, a typed field of
        // the same name in ANOTHER class or a variable local to a DIFFERENT
        // method must never prove this receiver.
        bool TypedAt(int index) =>
            index + 4 < t.Count && t[index].Value == receiver &&
            t[index + 1].Value == ":" &&
            (t[index + 2].Value is "ref" or "wref") &&
            t[index + 3].Value == "<" && t[index + 4].Value == "QuestsSystem";

        // Method parameters are inside the method's own signature.
        for (int i = method.SignatureOpen + 1; i + 4 < method.SignatureClose; i++)
            if (TypedAt(i)) return true;

        // Explicit local declarations in this method's source, before use.
        // A lexical match is still not a proof of runtime receiver identity.
        for (int i = method.Open + 1; i + 4 < opIndex; i++)
            if (TypedAt(i)) return true;

        // For this.m_quests.RegisterListener, only same-class top-level field
        // declarations count. Do not borrow fields from other classes or from
        // local variables inside unrelated methods in the same file.
        if (opIndex < 4 || t[opIndex - 4].Value != "this" ||
            t[opIndex - 3].Value != "." || string.IsNullOrEmpty(method.Owner))
            return false;

        int classOpen = -1;
        for (int i = 0; i + 2 < method.First; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != method.Owner)
                continue;
            int open = i + 2;
            while (open < method.First && open < i + 48 &&
                   t[open].Value is not ("{" or ";")) open++;
            if (open >= method.First || t[open].Value != "{") continue;
            int close = Match(t, open, "{", "}");
            if (close > method.Close && open > classOpen) classOpen = open;
        }
        if (classOpen < 0) return false;
        int nested = 0;
        for (int i = classOpen + 1; i + 4 < method.First; i++)
        {
            if (t[i].Value == "{") { nested++; continue; }
            if (t[i].Value == "}") { nested--; continue; }
            if (nested == 0 && TypedAt(i)) return true;
        }
        return false;
    }

}
