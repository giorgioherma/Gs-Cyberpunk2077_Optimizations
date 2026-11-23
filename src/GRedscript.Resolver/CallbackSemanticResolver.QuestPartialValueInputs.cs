namespace GRedscript.Resolver;

/// <summary>
/// Read-only per-value subscriber discovery. Every existing exact literal
/// reader (direct or same-owner wrapper) is a potential shared input even if
/// its enclosing method has dynamic UI, arguments or callback side effects.
/// This observes the SAME tokenized source/method index as the authoritative
/// quest cohort scanner. Partial value flow is a research witness, never an
/// authorization to replace a live getter or skip the method.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private sealed record QuestPartialValueInput(
        string Path, string Owner, string Method, int MethodLine, int ReadLine,
        string FactName, string ReadShape, string ReceiverWitness,
        string ValueUseShape, string? LocalName, string[] LocalUseKinds,
        int LocalReadsAfterAssignment, bool LocalMutationOrShadowingDetected,
        bool FactUsedInsideCondition, bool MethodHasUnrelatedInputs,
        string[] OtherMethodDependencyHints, string[] InstanceFields,
        string[] InstanceHelpers, string[] UsedParameters,
        bool SourceMatchedMeasured, long? MeasuredMethodOutgoingEdges,
        double? MeasuredMethodExclusiveMs,
        bool PartialInputCanBeObservedInPrinciple,
        bool PureDerivedValueProven, bool AllProducerWritesObserved,
        bool ConsumerLifecycleProven, bool OriginalNonfactWorkPreserved,
        bool OriginalGetterReplacementAuthorized,
        bool PartialSourceRewriteAuthorized);

    private static QuestPartialValueInput[] InspectQuestPartialValueInputs(
        List<MethodSource> methods, QuestSubscriberInventory subscribers)
    {
        // Reuse the exact read token index from the existing bidirectional
        // scanner. No parallel callsite parser, fixed owner/fact list or source
        // mutation is allowed.
        var methodIndex = methods
            .GroupBy(m => (m.File.Relative, m.Owner, m.Name, m.Line))
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single());

        var results = new List<QuestPartialValueInput>();
        foreach (var read in subscribers.Readers)
        {
            if (!methodIndex.TryGetValue(
                (read.Path, read.Owner, read.Method, read.Line), out var method))
                throw new InvalidDataException("QUEST_PARTIAL_SOURCE_METHOD_NOT_UNIQUE");
            var t = method.File.Tokens;
            int at = read.ReadTokenIndex;
            if (at <= method.Open || at + 1 >= method.Close ||
                t[at].Value is not ("GetFact" or "GetFactStr") ||
                t[at + 1].Value != "(")
                throw new InvalidDataException("QUEST_PARTIAL_READ_TOKEN_DRIFT");
            // Every source reader must still be a literal exact fact. A
            // changed token or string is source drift, not proof of a new
            // or implicitly permitted subscriber.
            if (at + 3 >= method.Close ||
                t[at + 2].Value != "n" || t[at + 3].Value != "<literal>")
                throw new InvalidDataException("QUEST_PARTIAL_FACT_LITERAL_DRIFT");
            var raw = method.File.Text[t[at + 3].Start..t[at + 3].End];
            if (raw.Length < 2 || raw[1..^1] != read.FactName)
                throw new InvalidDataException("QUEST_PARTIAL_FACT_NAME_DRIFT");

            int stmt = at - 1;
            while (stmt > method.Open &&
                   t[stmt].Value is not (";" or "{" or "}")) stmt--;
            int first = stmt + 1;
            int end = at;
            while (end < method.Close && t[end].Value != ";") end++;
            bool withinCondition = false;
            for (int i = method.Open + 1; i < at; i++)
            {
                if (t[i].Value != "if") continue;
                var (conditionEnd, _) = (PartialConditionEnd(t, i, method.Close), 0);
                if (i < at && at < conditionEnd)
                {
                    withinCondition = true;
                    break;
                }
            }

            string? local = null;
            bool alias = false, changed = false;
            string shape = "OTHER_EXPRESSION";
            if (withinCondition)
                shape = "INLINE_CONDITIONAL_INPUT";
            else if (first < at && t[first].Value == "return")
                shape = "INLINE_RETURN_INPUT";
            else if (first + 2 < at && t[first].Value == "let" &&
                     Ident(t[first + 1].Value))
            {
                int equals = -1;
                for (int i = first + 2; i < at; i++)
                    if (t[i].Value == "=") { equals = i; break; }
                if (equals >= 0)
                {
                    local = t[first + 1].Value;
                    int close = MatchPartialTokens(t, at + 1, method.Close, "(", ")");
                    if (close < at)
                        throw new InvalidDataException("QUEST_PARTIAL_READ_UNBALANCED_CALL");
                    int rhs = equals + 1;
                    alias = (rhs == at - 2 &&
                             t[at - 1].Value == "." &&
                             Ident(t[at - 2].Value)) ||
                            (rhs == at - 4 &&
                             t[rhs].Value == "this" &&
                             t[rhs + 1].Value == "." &&
                             Ident(t[rhs + 2].Value) &&
                             t[at - 1].Value == ".");
                    shape = close + 1 == end && alias
                        ? "LOCAL_DIRECT_FACT_VALUE" : "LOCAL_DERIVED_OR_MIXED_VALUE";
                }
            }
            else if (first + 2 < at && t[first].Value == "this" &&
                     t[first + 1].Value == ".")
                shape = "INSTANCE_STATE_ASSIGNMENT_OR_USE";
            else if (first < at && t[first].Value == "if")
                shape = "CONDITIONAL_INPUT_UNRESOLVED_SHAPE";

            var localUses = new SortedSet<string>(StringComparer.Ordinal);
            int localReadCount = 0;
            if (local is not null && end < method.Close)
            {
                for (int i = end + 1; i < method.Close; i++)
                {
                    if (t[i].Value != local ||
                        (i > method.Open && t[i - 1].Value == "."))
                        continue;
                    bool declaredAgain = i > method.Open && t[i - 1].Value == "let";
                    bool assignedAgain = i + 1 < method.Close &&
                        ((t[i + 1].Value == "=" &&
                          !(i + 2 < method.Close && t[i + 2].Value == "=")) ||
                         (i + 2 < method.Close &&
                          t[i + 1].Value is ("+" or "-" or "*" or "/") &&
                          t[i + 2].Value == "="));
                    if (declaredAgain || assignedAgain)
                    {
                        changed = true;
                        localUses.Add("REASSIGNED_OR_SHADOWED");
                        continue;
                    }
                    localReadCount++;
                    bool inGuard = false;
                    for (int k = end + 1; k < i; k++)
                    {
                        if (t[k].Value != "if") continue;
                        var (ce, _) = (PartialConditionEnd(t, k, method.Close), 0);
                        if (k < i && i < ce) { inGuard = true; break; }
                    }
                    if (inGuard) localUses.Add("DOWNSTREAM_CONDITION");
                    else if (i > method.Open && t[i - 1].Value == "return")
                        localUses.Add("RETURN_VALUE");
                    else if (i + 1 < method.Close &&
                             t[i + 1].Value is ("," or ")"))
                        localUses.Add("POSSIBLE_CALLEE_ARGUMENT");
                    else localUses.Add("OTHER_DOWNSTREAM_USE");
                }
            }

            results.Add(new QuestPartialValueInput(
                read.Path, read.Owner, read.Method, read.Line, read.ReadLine,
                read.FactName, read.ReadShape, read.WrapperEvidence, shape,
                local, localUses.ToArray(), localReadCount, changed,
                withinCondition || localUses.Contains("DOWNSTREAM_CONDITION"),
                read.MixedDependencies, read.NonFactDependencyHints,
                read.InstanceFields, read.InstanceHelperCalls,
                read.UsedParameters, read.SourceMatchedMeasured,
                read.MethodOutgoingEdges, read.MethodExclusiveMs,
                true, false, false, false, false, false, false));
        }

        return results.OrderBy(x => x.Path, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ReadLine).ThenBy(x => x.FactName,
                StringComparer.Ordinal).ToArray();
    }

    // Only identifies a source-token condition boundary, not an interpreter
    // or semantic equivalence proof. In particular, no event reordering,
    // member mutation or original method changes are authorized.
    private static int PartialConditionEnd(
        IReadOnlyList<SourceAdmission.Token> tokens, int ifIndex, int limit)
    {
        int start = ifIndex + 1;
        if (start >= limit) return -1;
        if (tokens[start].Value == "(")
            return MatchPartialTokens(tokens, start, limit, "(", ")");
        int round = 0, square = 0;
        for (int i = start; i < limit; i++)
        {
            var v = tokens[i].Value;
            if (v == "(") round++;
            else if (v == ")") round--;
            else if (v == "[") square++;
            else if (v == "]") square--;
            if (round < 0 || square < 0) return -1;
            if (round == 0 && square == 0)
            {
                if (v is ("{" or "return" or "break" or "continue"))
                    return i;
                if (v is (";" or "}" or "else"))
                    return -1;
            }
        }
        return -1;
    }

    private static int MatchPartialTokens(
        IReadOnlyList<SourceAdmission.Token> tokens, int start, int limit,
        string opening, string closing)
    {
        int depth = 0;
        for (int i = start; i < limit; i++)
        {
            if (tokens[i].Value == opening) depth++;
            else if (tokens[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }
}
