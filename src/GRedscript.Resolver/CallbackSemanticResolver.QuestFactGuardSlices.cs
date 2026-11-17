using System.Text;

namespace GRedscript.Resolver;

/// <summary>
/// Read-only, source-token guard-slice inventory for partial quest subscribers.
/// A shared fact input may appear in a predicate while the enclosing method
/// still has callbacks, native getters, mutable fields or side effects.
/// This uses the ONE existing source parser, source seal and F11 evidence join.
/// It neither replaces GetFact nor authorizes scheduling, skipping or rewriting.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private sealed record QuestFactGuardSlice(
        string Path, string Owner, string Method, int MethodLine, int GuardLine,
        string[] FactNames, int LiteralReads, int DynamicReads,
        string[] OtherPredicateCalls, string[] MethodDependencyCategories,
        string[] MethodInstanceFields, string[] MethodInstanceCalls,
        string[] MethodUsedParameters, bool EarlyReturnOnly,
        bool OriginalWrappedMethodInBranch, string[] BranchCallNames,
        int FallthroughCallsites, bool SourceMatchedMeasured,
        long? MeasuredMethodOutgoingEdges, double? MeasuredMethodExclusiveMs,
        string FirstUnresolvedGate, string[] AdditionalBlockers,
        bool CompletePredicateEffectsProven, bool AllFactMutationsObserved,
        bool OriginalMethodSemanticsPreserved, bool PartialRewriteAuthorized);

    private static QuestFactGuardSlice[] InspectQuestFactGuardSlices(
        List<MethodSource> methods, Dictionary<string, Evidence> measured)
    {
        var findings = new List<QuestFactGuardSlice>();
        foreach (var method in methods)
        {
            var t = method.File.Tokens;
            var body = t.Skip(method.Open + 1)
                .Take(method.Close - method.Open - 1).ToArray();
            for (int i = method.Open + 1; i + 2 < method.Close; i++)
            {
                if (t[i].Value != "if" || t[i + 1].Value != "(")
                    continue;
                int close = MatchFactGuard(t, i + 1, method.Close, "(", ")");
                if (close <= i + 2) continue;
                var facts = new SortedSet<string>(StringComparer.Ordinal);
                var otherCalls = new SortedSet<string>(StringComparer.Ordinal);
                int literals = 0, dynamic = 0;
                for (int j = i + 2; j < close; j++)
                {
                    if (j + 1 >= close || t[j + 1].Value != "(" ||
                        !Ident(t[j].Value)) continue;
                    if (t[j].Value is not ("GetFact" or "GetFactStr"))
                    {
                        otherCalls.Add(t[j].Value);
                        continue;
                    }
                    if (j + 3 < close && t[j + 2].Value == "n" &&
                        t[j + 3].Value == "<literal>")
                    {
                        var literal = t[j + 3];
                        string raw = method.File.Text[literal.Start..literal.End];
                        if (raw.Length > 2 && raw[0] == '"' && raw[^1] == '"' &&
                            !raw.Contains('\\'))
                        {
                            facts.Add(raw[1..^1]);
                            literals++;
                            continue;
                        }
                    }
                    dynamic++;
                }
                if (literals == 0) continue;

                int branchStart = close + 1;
                int branchEnd = -1;
                if (branchStart < method.Close && t[branchStart].Value == "{")
                    branchEnd = MatchFactGuard(t, branchStart, method.Close, "{", "}");
                else
                {
                    for (int j = branchStart; j < method.Close; j++)
                    {
                        if (t[j].Value == ";")
                        {
                            branchEnd = j;
                            break;
                        }
                        if (t[j].Value == "}") break;
                    }
                }
                var branch = branchEnd >= branchStart
                    ? t.Skip(branchStart + (t[branchStart].Value == "{" ? 1 : 0))
                        .Take(Math.Max(0, branchEnd - branchStart -
                            (t[branchStart].Value == "{" ? 1 : 0))).ToArray()
                    : Array.Empty<SourceAdmission.Token>();
                var branchCalls = new SortedSet<string>(StringComparer.Ordinal);
                for (int j = 0; j + 1 < branch.Length; j++)
                    if (branch[j + 1].Value == "(" && Ident(branch[j].Value))
                        branchCalls.Add(branch[j].Value);
                // A return that invokes wrappedMethod or another helper is
                // NOT an effect-free early exit and cannot be discarded.
                bool hasWrapped = branch.Any(x => x.Value == "wrappedMethod");
                bool returnOnly = branch.Length > 1 &&
                    branch[0].Value == "return" &&
                    branch.Count(x => x.Value == ";") == 1 &&
                    !branchCalls.Any() &&
                    !branch.Skip(1).Any(x => x.Value is "{" or "}");
                int fallthroughCalls = 0;
                if (branchEnd >= 0)
                    for (int j = branchEnd + 1; j + 1 < method.Close; j++)
                        if (Ident(t[j].Value) && t[j + 1].Value == "(")
                            fallthroughCalls++;

                var deps = InspectDirectDependencies(method, body);
                measured.TryGetValue(method.Identity, out var evidence);
                var blockers = new List<string>();
                if (dynamic != 0) blockers.Add("DYNAMIC_FACT_IN_SAME_PREDICATE");
                if (otherCalls.Count != 0)
                    blockers.Add("OTHER_PREDICATE_CALLEES_NOT_PROVEN");
                if (hasWrapped) blockers.Add("ORIGINAL_WRAPPEDMETHOD_MUST_RUN");
                if (!returnOnly) blockers.Add("BRANCH_HAS_OTHER_OR_UNKNOWN_EFFECTS");
                if (deps.InstanceFields.Length != 0 ||
                    deps.InstanceMethodCalls.Length != 0 ||
                    deps.UsedCallbackParameters.Length != 0)
                    blockers.Add("METHOD_HAS_INDEPENDENT_MUTABLE_INPUTS_OR_CALLEES");
                if (evidence is null)
                    blockers.Add("SOURCE_MATCHED_RUNTIME_EVIDENCE_MISSING");
                findings.Add(new QuestFactGuardSlice(
                    method.File.Relative, method.Owner, method.Name,
                    method.Line, t[i].Line, facts.ToArray(), literals, dynamic,
                    otherCalls.ToArray(), deps.Categories, deps.InstanceFields,
                    deps.InstanceMethodCalls, deps.UsedCallbackParameters,
                    returnOnly, hasWrapped, branchCalls.ToArray(),
                    fallthroughCalls, evidence is not null,
                    evidence?.OutgoingEdges, evidence?.InstrumentedExclusiveMs,
                    "FACT_ENGINE_NOTIFICATIONS_AND_ALL_WRITERS_UNPROVEN",
                    blockers.ToArray(), false, false, false, false));
            }
        }

        // Measured source-method work is only a research rank. It is NOT
        // attributable to the fact guard or to avoidable work.
        return findings.OrderByDescending(x => x.SourceMatchedMeasured)
            .ThenByDescending(x => x.MeasuredMethodExclusiveMs ?? 0)
            .ThenBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.GuardLine).ToArray();
    }

    private static int MatchFactGuard(
        IReadOnlyList<SourceAdmission.Token> t, int start, int limit,
        string open, string close)
    {
        int depth = 0;
        for (int i = start; i < limit; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }
}
