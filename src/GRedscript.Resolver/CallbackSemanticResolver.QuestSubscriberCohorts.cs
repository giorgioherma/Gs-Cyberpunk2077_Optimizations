using System.Text;

namespace GRedscript.Resolver;

/// <summary>
/// Bidirectional R5 source evidence: which exact fact-name consumers share an
/// input and which ALSO depend on unrelated state. Uses ONLY the existing
/// authoritative method spans and SourceAdmission tokens. A helper-shaped
/// read is not proof of a complete call graph, event equivalence, or a
/// deployable subscription.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private sealed record QuestSubscriberRead(
        string Path, string Owner, string Method, int Line, string FactName,
        string ReadShape, string WrapperEvidence, bool RefreshFamily,
        bool SourceMatchedMeasured, long? MethodOutgoingEdges,
        double? MethodExclusiveMs,
        string[] NonFactDependencyHints, string[] InstanceFields,
        string[] InstanceHelperCalls, string[] UsedParameters,
        bool OtherFactReadsMayBeDynamic, bool MixedDependencies,
        bool IsolatedExpressionProven, bool AllWritersProven,
        bool RuntimeNotificationEquivalent, bool OriginalExecutionPreserved,
        bool CanReplaceOriginalGetter, bool CanSkipOriginalMethod,
        bool SourceMutationAuthorized);

    private sealed record QuestSubscriberCohort(
        string FactName, int SourceReaders, int DistinctReaderMethods,
        int DistinctReaderOwners, int RefreshReaders, int OtherReaders,
        int SourceMatchedMeasuredReaders, int SameOwnerWrapperReaders,
        int DirectReadShapeReaders, int UnresolvedReadShapeReaders,
        int MixedDependencyReaders, int SimpleLexicalReaders,
        int DistinctDependencyProfiles, bool ContainsMixedAndSimpleReaders,
        bool ContainsWrapperAndDirectReaders, bool SharedAcrossOwners,
        bool AllWritersProven, bool ProducerCoversAllMutations,
        bool ConsumerLifecycleProven, bool SharedSubscriptionAuthorized,
        bool AnyOriginalGetterReplacementAuthorized,
        bool AnyOriginalMethodSkipAuthorized);

    private sealed record QuestSubscriberInventory(
        QuestSubscriberRead[] Readers, QuestSubscriberCohort[] Cohorts)
    {
        internal int MixedCohorts => Cohorts.Count(c => c.ContainsMixedAndSimpleReaders);
        internal int WrapperCohorts => Cohorts.Count(c => c.ContainsWrapperAndDirectReaders);
        internal int CrossOwnerCohorts => Cohorts.Count(c => c.SharedAcrossOwners);
    }

    private static QuestSubscriberInventory InspectQuestSubscribers(
        List<MethodSource> methods, Dictionary<string, Evidence> measured)
    {
        // The EXACT same owner and file must declare the wrapper. A class of
        // the same name in another mod/file is not sufficient. No source
        // reparse, new lexer, or hardcoded third-party names are introduced.
        var trustedWrapperShapes = methods
            .Where(IsSourceDeclaredQuestFactWrapperShape)
            .GroupBy(m => (m.File.Relative, m.Owner, m.Name))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        var reads = new List<QuestSubscriberRead>();
        foreach (var method in methods)
        {
            var tokens = method.File.Tokens;
            var body = tokens.Skip(method.Open + 1)
                .Take(method.Close - method.Open - 1).ToArray();
            var deps = InspectDirectDependencies(method, body);
            string[] nonfact = deps.Categories.Where(c => c != "QUEST_FACT_JOURNAL")
                .OrderBy(x => x, StringComparer.Ordinal).ToArray();
            bool freshOther = nonfact.Length != 0;
            bool dynamic = false;
            for (int i = 0; i + 1 < body.Length; i++)
            {
                if (body[i].Value is not ("GetFact" or "GetFactStr") ||
                    body[i + 1].Value != "(") continue;
                bool literal = i + 3 < body.Length &&
                    body[i + 2].Value == "n" && body[i + 3].Value == "<literal>";
                if (!literal)
                {
                    dynamic = true;
                    continue;
                }
                var tok = body[i + 3];
                string raw = method.File.Text[tok.Start..tok.End];
                if (raw.Length < 2 || raw[0] != '"' || raw[^1] != '"' ||
                    raw.Contains('\\'))
                {
                    dynamic = true;
                    continue;
                }
                string fact = raw[1..^1];
                if (string.IsNullOrWhiteSpace(fact)) continue;
                string kind, witness;
                if (i >= 2 && body[i - 1].Value == "." &&
                    body[i - 2].Value == "this")
                {
                    if (trustedWrapperShapes.TryGetValue(
                            (method.File.Relative, method.Owner, body[i].Value),
                            out var wrapper))
                    {
                        kind = "SAME_OWNER_QUEST_WRAPPER_SHAPE";
                        witness = wrapper.File.Relative + ":" + wrapper.Line;
                    }
                    else
                    {
                        kind = "UNRESOLVED_SAME_OWNER_HELPER";
                        witness = "WRAPPER_SOURCE_OR_TRANSTIVE_EFFECTS_UNKNOWN";
                    }
                }
                else
                {
                    kind = "DIRECT_GETFACT_LEXICAL";
                    witness = SourceDeclaresQuestReceiver(method,
                        method.Open + 1 + i)
                        ? "SAME_SCOPE_DECLARED_QUESTSYSTEM_RECEIVER" :
                        "DIRECT_RECEIVER_TYPE_NOT_PROVEN";
                }
                measured.TryGetValue(method.Identity, out var hit);
                reads.Add(new QuestSubscriberRead(
                    method.File.Relative, method.Owner, method.Name,
                    method.Line, fact, kind, witness,
                    SharedWorkFamily(method.Name) is not null,
                    hit is not null, hit?.OutgoingEdges,
                    hit?.InstrumentedExclusiveMs,
                    nonfact, deps.InstanceFields, deps.InstanceMethodCalls,
                    deps.UsedCallbackParameters, false, freshOther,
                    false, false, false, false, false, false, false));
            }
            // Store a method-wide dynamic-read marker. An exact literal
            // site can coexist with dynamic names in the SAME method;
            // the latter MUST keep its original access.
            if (dynamic)
            {
                int first = reads.FindLastIndex(r =>
                    r.Path == method.File.Relative && r.Owner == method.Owner &&
                    r.Method == method.Name && r.Line == method.Line);
                if (first >= 0)
                {
                    for (int i = first; i >= 0; i--)
                    {
                        var entry = reads[i];
                        if (entry.Path != method.File.Relative ||
                            entry.Owner != method.Owner ||
                            entry.Method != method.Name ||
                            entry.Line != method.Line) break;
                        reads[i] = entry with { OtherFactReadsMayBeDynamic = true };
                    }
                }
            }
        }

        // A source-only comparison, never a proof of "compatible" consumer:
        // read shapes are ordered by exact CName, then separated by the
        // presence/absence of OTHER input hints. Both preserve the original
        // method and original getter until stronger producer/side-effect proof.
        var cohorts = reads.GroupBy(r => r.FactName, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(group =>
            {
                var arr = group.ToArray();
                var methodGroups = arr.GroupBy(r =>
                    (r.Path, r.Owner, r.Method, r.Line)).ToArray();
                int mixed = methodGroups.Count(g => g.Any(x => x.MixedDependencies));
                int simple = methodGroups.Length - mixed;
                return new QuestSubscriberCohort(
                    group.Key, arr.Length, methodGroups.Length,
                    arr.Select(r => ScriptOwner(r.Path))
                        .Distinct(StringComparer.Ordinal).Count(),
                    methodGroups.Count(g => g.First().RefreshFamily),
                    methodGroups.Count(g => !g.First().RefreshFamily),
                    methodGroups.Count(g => g.First().SourceMatchedMeasured),
                    arr.Count(r => r.ReadShape == "SAME_OWNER_QUEST_WRAPPER_SHAPE"),
                    arr.Count(r => r.ReadShape == "DIRECT_GETFACT_LEXICAL"),
                    arr.Count(r => r.ReadShape == "UNRESOLVED_SAME_OWNER_HELPER"),
                    mixed, simple,
                    methodGroups.Select(g => string.Join("|",
                        g.First().NonFactDependencyHints))
                        .Distinct(StringComparer.Ordinal).Count(),
                    mixed > 0 && simple > 0,
                    arr.Any(r => r.ReadShape == "SAME_OWNER_QUEST_WRAPPER_SHAPE") &&
                        arr.Any(r => r.ReadShape == "DIRECT_GETFACT_LEXICAL"),
                    arr.Select(r => ScriptOwner(r.Path))
                        .Distinct(StringComparer.Ordinal).Count() > 1,
                    false, false, false, false, false, false);
            }).ToArray();

        return new QuestSubscriberInventory(
            reads.OrderBy(r => r.Path, StringComparer.OrdinalIgnoreCase)
                 .ThenBy(r => r.Line).ThenBy(r => r.FactName, StringComparer.Ordinal)
                 .ToArray(), cohorts);
    }

    private static bool IsSourceDeclaredQuestFactWrapperShape(MethodSource method)
    {
        // This is a narrow lexical *witness*, NOT proof that the wrapper is
        // side-effect free or that its GetFact result can be cached.
        if (method.Name is not ("GetFact" or "GetFactStr") ||
            string.IsNullOrEmpty(method.Owner))
            return false;
        var t = method.File.Tokens;
        bool nameArg = false;
        for (int i = method.SignatureOpen + 1; i + 2 < method.SignatureClose; i++)
        {
            if (t[i].Value == "name" && t[i + 1].Value == ":" &&
                t[i + 2].Value == "CName") nameArg = true;
        }
        if (!nameArg) return false;
        var body = t.Skip(method.Open + 1)
            .Take(method.Close - method.Open - 1).ToArray();
        bool questDeclared = body.Any(x => x.Value == "QuestsSystem");
        bool engineGetter = false;
        for (int i = 0; i + 2 < body.Length; i++)
        {
            if (body[i].Value is ("GetFact" or "GetFactStr") &&
                body[i + 1].Value == "(" &&
                body[i + 2].Value == "name" &&
                i >= 1 && body[i - 1].Value == ".")
                engineGetter = true;
        }
        return questDeclared && engineGetter &&
            body.Any(x => x.Value == "return") &&
            !body.Any(x => x.Value is "SetFact" or "SetFactStr" or
                "QueueEvent" or "RegisterListener" or "wrappedMethod");
    }
}
