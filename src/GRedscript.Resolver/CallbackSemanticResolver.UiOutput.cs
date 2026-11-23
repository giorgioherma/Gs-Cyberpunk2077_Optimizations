using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// R5 generic, read-only UI output-slice analysis. A SetData output may have
/// structural/status inputs AND a live timer; the former does not authorize
/// suppressing the setter or its enclosing refresh. Uses only the established
/// method token index, SourceAdmission seal, and sealed F11 evidence join.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    internal sealed record UiOutputSummary(int SourceFiles, int SetDataSites,
        int MixedTimerSites, int SourceMatchedMethodSites,
        int SourceTypedReceivers);

    private static readonly HashSet<string> UiNeighborSetters =
        new(StringComparer.Ordinal) {
            "SetData", "SetVisible", "SetStatusEffectRecord", "SetText",
            "SetStackCount", "SetTimeRemaining", "SetEffectParamValue",
            "SendVisibilityUpdate", "SetState", "PlayLibraryAnimation"
        };

    internal static UiOutputSummary ScanUiOutputSlices(
        string gameRoot, string outputPath, string? analysisPath)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputPath);
        string? analysis = analysisPath is null ? null :
            Path.GetFullPath(analysisPath);
        if (Inside(output, game) || File.Exists(output) ||
            Directory.Exists(output) ||
            (analysis is not null && output.Equals(analysis,
                StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("UI_OUTPUT_ISOLATION_FAILED");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (_, methods) = ReadAllMethodSources(game, admission);
        var measured = analysis is null ?
            new Dictionary<string, Evidence>(StringComparer.Ordinal) :
            ReadEvidence(analysis, admission, methods);

        var rows = new List<object>();
        int mixedTimer = 0, measuredCount = 0, typedCount = 0;
        foreach (var method in methods)
        {
            var t = method.File.Tokens;
            var body = t.Skip(method.Open + 1)
                .Take(method.Close - method.Open - 1).ToArray();
            var deps = InspectDirectDependencies(method, body);
            var peerSetters = new SortedSet<string>(StringComparer.Ordinal);
            for (int i = method.Open + 1; i + 1 < method.Close; i++)
            {
                if (UiNeighborSetters.Contains(t[i].Value) &&
                    t[i + 1].Value == "(")
                    peerSetters.Add(t[i].Value);
            }
            for (int at = method.Open + 1; at + 1 < method.Close; at++)
            {
                if (t[at].Value != "SetData" || t[at + 1].Value != "(" ||
                    at < 2 || t[at - 1].Value != ".") continue;

                int closing = Match(t, at + 1, "(", ")");
                if (closing >= method.Close)
                    throw new InvalidDataException("UI_OUTPUT_CALL_OUTSIDE_METHOD");
                string receiver = t[at - 2].Value;
                // The receiver's explicit source type is a useful witness;
                // a familiar class suffix or variable name is NOT runtime
                // proof of setter purity or widget identity.
                string? receiverType = TypedLocalReceiver(t, method,
                    receiver, at);
                string[] args = PartitionUiArguments(t, at + 2, closing);
                bool[] live = args.Select(UiArgumentHasLiveTime).ToArray();
                bool[] stack = args.Select(UiArgumentHasStackCount).ToArray();
                bool[] status = args.Select(UiArgumentHasRecordDependency).ToArray();
                bool hasTime = live.Any(x => x) ||
                    deps.Categories.Contains("CLOCK_TIME");
                bool hasStructural = status.Any(x => x) ||
                    deps.InstanceFields.Contains("m_buffDataList") ||
                    deps.InstanceFields.Contains("m_debuffDataList");
                // Both are *lexical* categories. Even an identical tuple
                // cannot prove SetData is side-effect free.
                bool mixed = hasTime && hasStructural;
                if (mixed) mixedTimer++;
                measured.TryGetValue(method.Identity, out var hit);
                if (hit is not null) measuredCount++;
                if (receiverType is not null) typedCount++;
                rows.Add(new
                {
                    source = new { path = method.File.Relative, owner = method.Owner,
                        method = method.Name, methodLine = method.Line,
                        callLine = t[at].Line },
                    setter = "SetData",
                    receiver,
                    receiverSourceDeclaredType = receiverType,
                    receiverRuntimeIdentityProven = false,
                    arguments = args.Select((arg, ix) => new
                    {
                        index = ix,
                        tokenExpression = arg,
                        liveTimeLexemes = live[ix],
                        stackOrCountLexemes = stack[ix],
                        statusRecordLexemes = status[ix],
                        allWriterPathsProven = false
                    }).ToArray(),
                    argumentCount = args.Length,
                    containsTimeLikeInput = hasTime,
                    containsStatusLikeInput = hasStructural,
                    mixedStructuralAndTimeInputs = mixed,
                    otherOutputCallsInMethod = peerSetters
                        .Where(n => n != "SetData").ToArray(),
                    hasIndependentOutputs = peerSetters.Count > 1,
                    methodFieldHints = deps.InstanceFields,
                    methodHelperHints = deps.InstanceMethodCalls,
                    methodParameterHints = deps.UsedCallbackParameters,
                    sourceMatchedMeasuredMethod = hit is not null,
                    measuredMethodOutgoingEdges = hit?.OutgoingEdges,
                    measuredMethodExclusiveMs = hit?.InstrumentedExclusiveMs,
                    methodTimeIsNotSetterTimeOrSavings = true,
                    uiSetterImplementationSourceProven = false,
                    fullValueTupleEqualityProven = false,
                    widgetGenerationAndLifecycleProven = false,
                    timerAndAnimationSemanticsProven = false,
                    sourceChangePublisherComplete = false,
                    sameSourceOriginalEffectsPreserved = false,
                    suppressWholeRefreshAuthorized = false,
                    suppressSetterAuthorized = false,
                    generationAuthorized = false
                });
            }
        }
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("UI_OUTPUT_SOURCE_DRIFT");
        var report = new
        {
            schemaVersion = "gco.redscript.ui-output.slices.v1",
            sourceFiles = admission.InspectedFileCount,
            sourceMethods = methods.Count,
            sourceStackSha256 = admission.SourceStackSha256,
            evidenceKind = analysis is null ? "SOURCE_ONLY" :
                "SEALED_F11_METHOD_EDGES_NOT_SETTER_ENTRIES",
            parser = "CALLBACK_SEMANTIC_RESOLVER_AUTHORITATIVE",
            setDataSites = rows.Count,
            mixedStructuralTimeSites = mixedTimer,
            sourceMatchedMethodSites = measuredCount,
            sourceTypedReceiverSites = typedCount,
            outputsHaveIndependentInputs = true,
            lexicalTimeHintsAreNotActualTimerChangeRates = true,
            sourceProofOverridesProfilerRanking = true,
            allSetterEffectsAndUiLifecyclesProven = false,
            noCountOnlyOrNthFrameThrottlingAllowed = true,
            generationAuthorized = false,
            sourceWrites = false,
            generatedFiles = 0,
            outputs = rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(false));
        return new(admission.InspectedFileCount, rows.Count,
            mixedTimer, measuredCount, typedCount);
    }

    private static string? TypedLocalReceiver(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource method,
        string receiver, int before)
    {
        // A local or signature declaration in THIS method, before call.
        // Do not borrow a same-named variable from another function/class.
        for (int i = method.SignatureOpen + 1; i + 4 < before; i++)
        {
            if (t[i].Value != receiver || t[i + 1].Value != ":" ||
                t[i + 2].Value is not ("ref" or "wref") ||
                t[i + 3].Value != "<" || !Ident(t[i + 4].Value))
                continue;
            // Only method signature or local token spans can prove this.
            if (i < method.SignatureClose ||
                (i > method.Open && i < before))
                return t[i + 4].Value;
        }
        return null;
    }

    private static string[] PartitionUiArguments(
        IReadOnlyList<SourceAdmission.Token> t, int start, int end)
    {
        if (start >= end) return [];
        var items = new List<string>();
        var current = new List<string>();
        int round = 0, square = 0, brace = 0;
        for (int i = start; i < end; i++)
        {
            string value = t[i].Value;
            if (round == 0 && square == 0 && brace == 0 && value == ",")
            {
                items.Add(string.Join(" ", current));
                current.Clear();
                continue;
            }
            current.Add(value);
            if (value == "(") round++;
            else if (value == ")") round--;
            else if (value == "[") square++;
            else if (value == "]") square--;
            else if (value == "{") brace++;
            else if (value == "}") brace--;
            if (round < 0 || square < 0 || brace < 0)
                throw new InvalidDataException("UI_OUTPUT_ARGUMENTS_UNBALANCED");
        }
        if (round != 0 || square != 0 || brace != 0 ||
            current.Count == 0)
            throw new InvalidDataException("UI_OUTPUT_ARGUMENTS_UNBALANCED");
        items.Add(string.Join(" ", current));
        return items.ToArray();
    }

    // Hints only: data flow through locals and callee results may make these
    // categories incomplete, and a naming coincidence can cause false positives.
    private static bool UiArgumentHasLiveTime(string expression) =>
        expression.Split(' ').Any(s => s is
            ("timeLeft" or "timeRemaining" or "timeTotal" or
             "buffTimeRemaining" or "buffTimeTotal" or
             "deltaTime" or "GetTime" or "GetDuration"));
    private static bool UiArgumentHasStackCount(string expression) =>
        expression.Split(' ').Any(s => s is
            ("stackCount" or "stack" or "count" or "stacks"));
    private static bool UiArgumentHasRecordDependency(string expression) =>
        expression.Split(' ').Any(s => s is
            ("buffID" or "IconPath" or "UiData" or "StatusEffect_Record" or
             "TweakDBInterface" or "data" or "buffList" or "record"));
}
