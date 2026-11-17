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
                if (t[i].Value != "if") continue;
                var (conditionEnd, branchStart) =
                    LocateFactGuard(t, i, method.Close);
                if (conditionEnd <= i + 1 || branchStart < 0) continue;
                var facts = new SortedSet<string>(StringComparer.Ordinal);
                var otherCalls = new SortedSet<string>(StringComparer.Ordinal);
                int literals = 0, dynamic = 0;
                for (int j = i + 2; j < conditionEnd; j++)
                {
                    if (j + 1 >= conditionEnd || t[j + 1].Value != "(" ||
                        !Ident(t[j].Value)) continue;
                    if (t[j].Value is not ("GetFact" or "GetFactStr"))
                    {
                        otherCalls.Add(t[j].Value);
                        continue;
                    }
                    if (j + 3 < conditionEnd && t[j + 2].Value == "n" &&
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
                bool braced = branchStart < method.Close &&
                    t[branchStart].Value == "{";
                int firstBranchToken = branchStart + (braced ? 1 : 0);
                int branchEndExclusive = branchEnd + (braced ? 0 : 1);
                var branch = branchEnd >= branchStart
                    ? t.Skip(firstBranchToken)
                        .Take(Math.Max(0, branchEndExclusive - firstBranchToken))
                        .ToArray()
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

    // REDscript accepts BOTH "if (condition) { ... }" and
    // "if condition { ... }". Parentheses are not a mandatory if token.
    // Recognize only a closed condition followed by an unambiguous branch
    // boundary; never consume through a following statement or method.
    private static (int ConditionEnd, int BranchStart) LocateFactGuard(
        IReadOnlyList<SourceAdmission.Token> t, int ifIndex, int limit)
    {
        int start = ifIndex + 1;
        if (start >= limit) return (-1, -1);
        if (t[start].Value == "(")
        {
            int closing = MatchFactGuard(t, start, limit, "(", ")");
            return closing >= 0 && closing + 1 < limit
                ? (closing, closing + 1) : (-1, -1);
        }

        int roundDepth = 0, squareDepth = 0;
        for (int i = start; i < limit; i++)
        {
            string v = t[i].Value;
            if (v == "(") roundDepth++;
            else if (v == ")") roundDepth--;
            else if (v == "[") squareDepth++;
            else if (v == "]") squareDepth--;
            if (roundDepth < 0 || squareDepth < 0) return (-1, -1);
            if (roundDepth == 0 && squareDepth == 0)
            {
                if (v is "{" or "return" or "break" or "continue")
                    return (i, i);
                if (v is ";" or "}" or "else") break;
            }
        }
        return (-1, -1);
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
