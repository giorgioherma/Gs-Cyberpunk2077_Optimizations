using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// CET-style GENERIC-FIRST callback classification for the entire REDscript
/// source stack. This is the common semantic/evidence layer used BEFORE choosing
/// individual rewrite recipes. It does not mutate scripts, register callbacks,
/// claim Lua-equivalent action routing, or infer entry counts from VM call edges.
/// </summary>
internal static class CallbackSemanticResolver
{
    private sealed record FileSource(string Path, string Relative, string Module,
        string Text, IReadOnlyList<SourceAdmission.Token> Tokens);

    private sealed record MethodSource(FileSource File, string Owner, string Name,
        string Kind, string? Annotation, int First, int Open, int Close,
        int SignatureOpen, int SignatureClose)
    {
        internal int Line => File.Tokens[First].Line;
        internal string Identity => File.Relative + "\0" + Owner + "\0" + Name + "\0" + Line;
    }

    private sealed record ClassSpan(string Name, int Open, int Close, bool Delay);

    private sealed record Evidence(long OutgoingEdges, double InstrumentedExclusiveMs,
        int MatchedCallsites);

    internal sealed record Result(int Files, int Callbacks, int OnAction,
        int PlayerPuppetWraps, int Forwarders, int OtherCallbacks,
        int MeasuredCallbacks, int UnprovenRoutes);

    internal static Result Analyze(string gameRoot, string outputPath, string? analysisPath)
    {
        string root = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputPath);
        string? analysis = analysisPath is null ? null : Path.GetFullPath(analysisPath);
        if (Inside(output, root) || File.Exists(output) || Directory.Exists(output) ||
            (analysis is not null && output.Equals(analysis, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("CALLBACK_SEMANTICS_OUTPUT_NOT_ISOLATED");

        var source = new SourceAdmission(root);
        _ = source.AuditAll();
        var files = new List<FileSource>();
        var methods = new List<MethodSource>();
        string scripts = Path.Combine(root, "r6", "scripts");
        foreach (var path in Directory.EnumerateFiles(scripts, "*.reds", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!source.IsFileIndexed(path)) continue;
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CALLBACK_SEMANTICS_REPARSE_SOURCE_REFUSED");
            string raw = File.ReadAllText(path, Encoding.UTF8);
            var t = SourceAdmission.ReadTokens(raw);
            var file = new FileSource(path, "r6/scripts/" +
                Path.GetRelativePath(scripts, path).Replace('\\', '/'),
                Module(t), raw, t);
            files.Add(file);
            methods.AddRange(ParseMethods(file));
        }

        var methodIndex = methods.GroupBy(m => m.File.Module + "\0" + m.Owner + "\0" + m.Name)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var evidence = analysis is null
            ? new Dictionary<string, Evidence>(StringComparer.Ordinal)
            : ReadEvidence(analysis, source, methods);
        var callbackRows = new List<object>();
        var familyCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var recipeCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var blockerCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        int actionCount = 0, wrapCount = 0, forwarding = 0,
            other = 0, measured = 0, unproven = 0;

        foreach (var method in methods.Where(IsCallback)
                     .OrderBy(m => m.File.Relative, StringComparer.Ordinal)
                     .ThenBy(m => m.Line))
        {
            var t = method.File.Tokens;
            var body = t.Skip(method.Open + 1).Take(method.Close - method.Open - 1).ToArray();
            string family = Family(method);
            familyCounts[family] = familyCounts.GetValueOrDefault(family) + 1;
            bool input = family == "ONACTION";
            if (input) actionCount++; else other++;
            bool wrapped = method.Annotation == "wrapMethod";
            if (wrapped && method.Owner == "PlayerPuppet" && input) wrapCount++;

            var names = new SortedSet<string>(StringComparer.Ordinal);
            var callees = new SortedSet<string>(StringComparer.Ordinal);
            var unresolved = new SortedSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal) { method.Identity };
            InspectCalls(method, methodIndex, 0, visited, names, callees, unresolved);
            if (callees.Count > 0) forwarding++;

            bool usesWrapped = Contains(body, "wrappedMethod");
            bool usesConsumer = Contains(body, "consumer");
            bool mutatesConsumer = Contains(body, "Consume") ||
                Contains(body, "ConsumeSingleAction");
            bool hasBridge = body.Any(x => x.Value is "SetLastUsedKBM" or
                "ExecuteFunction" or "CallLua" or "CallNative") ||
                unresolved.Any(x => x.StartsWith("EXTERNAL_BRIDGE:", StringComparison.Ordinal));
            bool hasSelector = Contains(body, "GetName") ||
                Contains(body, "IsAction") || callees.Count > 0 && names.Count > 0;
            bool hasGuard = Contains(body, "if") || Contains(body, "switch");
            bool hasRead = Contains(body, "GetScriptableSystemsContainer") ||
                Contains(body, "GetQuestsSystem") || Contains(body, "GetClasses") ||
                Contains(body, "GetFunction") || Contains(body, "GetGame");
            bool hasUi = Contains(body, "SetData") || Contains(body, "UpdateBuffDebuffList") ||
                Contains(body, "SetText") || Contains(body, "SetVisible");
            bool hasLog = Contains(body, "Log") || Contains(body, "LogChannel") ||
                Contains(body, "RedLogger") || Contains(body, "LogError");
            bool hasStateWrite = Contains(body, "QueueEvent") ||
                Contains(body, "Set") || Contains(body, "ArrayPush") ||
                Contains(body, "RegisterInputListener");

            var recipes = new List<string>();
            if (input) recipes.Add("G_INPUT_ROUTE");
            if (hasGuard) recipes.Add("G_GUARD_HOIST");
            if (hasRead) recipes.Add("G_READ_REUSE");
            if (hasUi) recipes.Add("G_UI_UNCHANGED");
            if (hasLog) recipes.Add("G_DIAGNOSTIC_GUARD");
            foreach (string recipe in recipes)
                recipeCounts[recipe] = recipeCounts.GetValueOrDefault(recipe) + 1;

            var blockers = new SortedSet<string>(StringComparer.Ordinal);
            if (input)
            {
                if (names.Count == 0) blockers.Add("ACTION_SELECTOR_SET_NOT_PROVEN");
                if (unresolved.Count > 0) blockers.Add("FORWARDED_HELPER_EFFECTS_OR_BINDING_UNPROVEN");
                if (usesWrapped) blockers.Add("WRAPPED_METHOD_ORDER_AND_RETURN_MUST_BE_PRESERVED");
                if (mutatesConsumer) blockers.Add("CONSUMER_MUTATION_MUST_BE_PRESERVED");
                if (hasBridge) blockers.Add("EXTERNAL_BRIDGE_EFFECTS_UNPROVEN");
                if (hasStateWrite) blockers.Add("CALLBACK_STATE_EFFECTS_UNPROVEN");
                blockers.Add("ACTION_SELECTOR_COMPLETENESS_NOT_PROVEN");
                blockers.Add("CALLBACK_ENTRY_COUNTS_UNAVAILABLE");
            }
            if (hasRead) blockers.Add("RECEIVER_AND_INVALIDATION_NOT_PROVEN");
            if (hasUi) blockers.Add("UI_VALUE_WIDGET_LIFETIME_NOT_PROVEN");
            if (hasGuard) blockers.Add("EARLY_GUARD_SIDE_EFFECT_ORDER_NOT_PROVEN");
            if (hasLog) blockers.Add("LOGGING_TOGGLE_AND_ARGUMENT_EFFECTS_UNPROVEN");
            foreach (string blocker in blockers)
                blockerCounts[blocker] = blockerCounts.GetValueOrDefault(blocker) + 1;
            if (input) unproven++;

            string[] registrations = RegistrationHints(method, methods);
            evidence.TryGetValue(method.Identity, out Evidence? measure);
            if (measure is not null) measured++;
            callbackRows.Add(new
            {
                source = new { path = method.File.Relative, line = method.Line,
                    owner = method.Owner, method = method.Name, module = method.File.Module },
                family,
                declaration = new { kind = method.Kind,
                    annotation = method.Annotation ?? "NONE",
                    playerPuppetOnActionWrapper = input && wrapped &&
                        method.Owner == "PlayerPuppet",
                    registeredInputListenerHints = registrations },
                action = new {
                    actionNameSelectorsObserved = hasSelector,
                    candidateLiteralNames = names.ToArray(),
                    literalNamesAreCompleteSet = false,
                    forwardedHelpers = callees.ToArray(),
                    unresolvedForwardingOrDynamicCalls = unresolved.ToArray(),
                    wrappedMethodInvoked = usesWrapped,
                    consumerReferenced = usesConsumer,
                    consumerMayBeMutated = mutatesConsumer,
                    externalBridgeObserved = hasBridge,
                    selectorCompleteness = "UNKNOWN",
                    routingAutomatable = false
                },
                patterns = new {
                    cheapGuardObserved = hasGuard,
                    repeatedOrEngineReadCandidate = hasRead,
                    uiSetterCandidate = hasUi,
                    diagnosticCallCandidate = hasLog,
                    stateMutationCandidate = hasStateWrite
                },
                candidateRecipeFamilies = recipes,
                blockers,
                measurement = new {
                    sourceMatchedOutgoingVmCallEdges = measure?.OutgoingEdges,
                    instrumentedExclusiveMs = measure?.InstrumentedExclusiveMs,
                    sourceMatchedCallsiteRows = measure?.MatchedCallsites,
                    evidenceKind = measure is null ? "NOT_CAPTURED_OR_NOT_SOURCE_ALIGNED" :
                        "OUTGOING_VM_CALLSITES_NOT_CALLBACK_ENTRIES",
                    callbackEntries = (long?)null,
                    estimatedSavingsMs = (double?)null
                },
                generationAuthorized = false
            });
        }

        if (!string.Equals(source.SourceStackSha256,
                source.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CALLBACK_SEMANTICS_SOURCE_CHANGED_DURING_SCAN");

        var report = new
        {
            schemaVersion = "gco.redscript.callback.semantics.v1",
            architecture = "CET_GENERIC_CLASSIFICATION_REDSCRIPT_BACKEND",
            sourceStackSha256 = source.SourceStackSha256,
            sourceFiles = files.Count,
            discoveredMethods = methods.Count,
            classifiedCallbacks = callbackRows.Count,
            onActionMethods = actionCount,
            playerPuppetOnActionWrappers = wrapCount,
            callbacksWithForwardedHelpers = forwarding,
            otherCallbackMethods = other,
            sourceMatchedMeasuredCallbacks = measured,
            unresolvedActionRouteCount = unproven,
            familyCounts,
            recipeCandidateCounts = recipeCounts,
            blockerCounts,
            registeredCallbacksMayBeOutsideParsedSources = true,
            actionSelectorCompletenessProven = false,
            callbackEntryCountsKnown = false,
            sourceEffectSummaryAuthorizesMutation = false,
            generationAuthorized = false,
            gameplayValidated = false,
            sourceWrites = false,
            callbacks = callbackRows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = false }),
            new UTF8Encoding(false));
        return new(files.Count, callbackRows.Count, actionCount, wrapCount,
            forwarding, other, measured, unproven);
    }

    private static string Family(MethodSource m) =>
        m.Name switch
        {
            "OnAction" => "ONACTION",
            "OnUpdate" => "ONUPDATE",
            "OnDraw" => "ONDRAW",
            "OnTick" => "ONTICK",
            "Call" when m.Kind == "DELAY_CALLBACK" => "DELAY_CALLBACK",
            _ => "ON_EVENT"
        };

    private static bool IsCallback(MethodSource m) =>
        m.Kind == "DELAY_CALLBACK" || m.Kind == "CB" ||
        (m.Name.StartsWith("On", StringComparison.Ordinal) && m.Name.Length > 2);

    private static List<MethodSource> ParseMethods(FileSource f)
    {
        var t = f.Tokens;
        var classes = new List<ClassSpan>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" || !Ident(t[i + 1].Value)) continue;
            int j = i + 2;
            while (j < t.Count && j < i + 48 && t[j].Value is not ("{" or ";")) j++;
            if (j >= t.Count || t[j].Value != "{") continue;
            int end = Match(t, j, "{", "}");
            bool delay = t.Skip(i + 2).Take(j - i - 2)
                .Any(x => x.Value == "DelayCallback");
            classes.Add(new ClassSpan(t[i + 1].Value, j, end, delay));
        }
        var methods = new List<MethodSource>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || !Ident(t[i + 1].Value) ||
                t[i + 2].Value != "(") continue;
            int argsEnd = Match(t, i + 2, "(", ")");
            int open = argsEnd + 1;
            while (open < t.Count && open < argsEnd + 32 &&
                   t[open].Value is not ("{" or ";" or "=")) open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int close = Match(t, open, "{", "}");
            var cls = classes.Where(c => c.Open < i && c.Close > close)
                .OrderByDescending(c => c.Open).FirstOrDefault();
            var annotation = Annotation(t, i);
            string owner = cls?.Name ?? annotation.Target ?? "";
            bool cb = t.Skip(Math.Max(0, i - 7)).Take(Math.Min(i, 7))
                .Any(x => x.Value == "cb");
            string kind = cls?.Delay == true && t[i + 1].Value == "Call"
                ? "DELAY_CALLBACK" : cb ? "CB" : "METHOD";
            methods.Add(new MethodSource(f, owner, t[i + 1].Value, kind,
                annotation.Kind, i, open, close, i + 2, argsEnd));
        }
        return methods;
    }

    private static (string? Kind, string? Target) Annotation(
        IReadOnlyList<SourceAdmission.Token> t, int function)
    {
        // Class-target annotations are supported even for top-level extensions:
        // @wrapMethod(PlayerPuppet) protected cb func OnAction(...) {...}
        // Do not carry an annotation past a prior method body boundary.
        int begin = Math.Max(0, function - 36);
        for (int i = function - 1; i >= begin; i--)
        {
            if (t[i].Value is "}" or "{" or ";") break;
            if (t[i].Value is not ("wrapMethod" or "replaceMethod" or "addMethod"))
                continue;
            if (i == 0 || t[i - 1].Value != "@" || i + 3 >= function ||
                t[i + 1].Value != "(" || !Ident(t[i + 2].Value) ||
                t[i + 3].Value != ")") continue;
            return (t[i].Value, t[i + 2].Value);
        }
        return (null, null);
    }

    private static string[] RegistrationHints(MethodSource callback, List<MethodSource> all)
    {
        if (callback.Name != "OnAction") return [];
        var names = new SortedSet<string>(StringComparer.Ordinal);
        // Evidence only. Registration of "this" or dynamically constructed
        // listeners cannot be globally bound to callback receiver by name.
        foreach (var method in all.Where(m => m.File.Module == callback.File.Module))
        {
            var t = method.File.Tokens;
            for (int i = method.Open + 1; i + 3 < method.Close; i++)
                if (t[i].Value is "RegisterInputListener" or "UnregisterInputListener" &&
                    t[i + 1].Value == "(")
                {
                    int end = Match(t, i + 1, "(", ")");
                    for (int k = i + 2; k + 1 < end; k++)
                        if (t[k].Value == "n" && t[k + 1].Value == "<literal>")
                            names.Add(Literal(method.File.Text, t[k + 1]));
                }
        }
        return names.Take(64).ToArray();
    }

    private static void InspectCalls(MethodSource root,
        IReadOnlyDictionary<string, MethodSource[]> index,
        int depth, HashSet<string> visited, SortedSet<string> names,
        SortedSet<string> callees, SortedSet<string> unresolved)
    {
        if (depth > 3 || visited.Count > 32)
        {
            unresolved.Add("FORWARD_DEPTH_OR_BUDGET_EXCEEDED");
            return;
        }
        var t = root.File.Tokens;
        for (int i = root.Open + 1; i + 1 < root.Close; i++)
        {
            if (t[i].Value == "n" && t[i + 1].Value == "<literal>")
                names.Add(Literal(root.File.Text, t[i + 1]));
            if (i + 3 >= root.Close || !Ident(t[i].Value) ||
                t[i + 1].Value != "." || !Ident(t[i + 2].Value) ||
                t[i + 3].Value != "(") continue;
            string receiver = t[i].Value, fn = t[i + 2].Value;
            if (fn == "SetLastUsedKBM" || fn == "ExecuteFunction")
                unresolved.Add("EXTERNAL_BRIDGE:" + fn);
            if (receiver is "this" or "super" or "ListenerAction" or
                "GameInstance" or "Reflection") continue;
            var key = root.File.Module + "\0" + receiver + "\0" + fn;
            if (!index.TryGetValue(key, out var methods))
            {
                // Cross-module imports/aliases are not silently resolved.
                if (fn is "Handle" or "OnChoiceAction" or "OnAction" ||
                    fn.StartsWith("Handle", StringComparison.Ordinal))
                    unresolved.Add("UNRESOLVED_HELPER:" + receiver + "." + fn);
                continue;
            }
            if (methods.Length != 1)
            {
                unresolved.Add("AMBIGUOUS_HELPER:" + receiver + "." + fn);
                continue;
            }
            var next = methods[0];
            callees.Add(receiver + "." + fn);
            if (visited.Add(next.Identity))
                InspectCalls(next, index, depth + 1, visited, names, callees, unresolved);
        }
    }

    private static Dictionary<string, Evidence> ReadEvidence(
        string path, SourceAdmission admission, IReadOnlyList<MethodSource> methods)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "grsp.resolver.input.v1" ||
            root.GetProperty("generationAuthorized").GetBoolean() ||
            root.GetProperty("capture").GetProperty("collectionProvenance").GetString() !=
                "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
            !string.Equals(root.GetProperty("source").GetProperty("sourceStackSha256")
                    .GetString(), admission.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CALLBACK_SEMANTICS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");

        var rows = root.GetProperty("rawTables").GetProperty("Developer/RSP_CallSites.csv")
            .GetProperty("rows");
        var observations = root.GetProperty("sourceObservations");
        if (rows.GetArrayLength() != observations.GetArrayLength())
            throw new InvalidDataException("CALLBACK_SEMANTICS_CALLSITE_ROW_ALIGNMENT_FAILED");
        var byFile = methods.GroupBy(m => m.File.Relative,
            StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key,
            x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var totals = new Dictionary<string, Evidence>(StringComparer.Ordinal);
        int k = 0;
        foreach (var o in observations.EnumerateArray())
        {
            var row = rows[k++];
            string rel = SourceAdmission.RelativeScriptPath(
                o.GetProperty("capturedPath").GetString() ?? "") ?? "";
            string script = "r6/scripts/" + rel;
            if (!byFile.TryGetValue(script, out var matching)) continue;
            var proof = o.GetProperty("evidence");
            if (proof.GetProperty("status").GetString() !=
                "LINE_METHOD_NAME_MATCH_RECEIVER_UNVERIFIED") continue;
            int line = o.GetProperty("sourceLine").GetInt32();
            string symbol = o.GetProperty("sourceFunction").GetString() ?? "";
            string name = symbol.Split(new[] { "::", "." }, StringSplitOptions.None).Last();
            var methodsAtCall = matching.Where(m => m.Name == name &&
                m.Line <= line && m.File.Tokens[m.Close].Line >= line).ToArray();
            if (methodsAtCall.Length != 1) continue;
            if (!string.Equals(proof.GetProperty("sha256").GetString(),
                admission.Check(script, name, line).Sha256,
                StringComparison.OrdinalIgnoreCase)) continue;
            var cols = row.EnumerateObject().ToDictionary(x => x.Name,
                x => x.Value.GetString() ?? "", StringComparer.Ordinal);
            if (!cols.TryGetValue("calls", out string? countText) ||
                !long.TryParse(countText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long edges) || edges < 0)
                throw new InvalidDataException("CALLBACK_SEMANTICS_INVALID_CALLSITE_COUNT");
            double work = 0;
            if (cols.TryGetValue("exclusive_instrumented_ms", out var workText) &&
                (!double.TryParse(workText, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out work) ||
                 !double.IsFinite(work) || work < 0))
                throw new InvalidDataException("CALLBACK_SEMANTICS_INVALID_WORK");
            string key = methodsAtCall[0].Identity;
            totals.TryGetValue(key, out Evidence? current);
            totals[key] = new Evidence(checked((current?.OutgoingEdges ?? 0) + edges),
                (current?.InstrumentedExclusiveMs ?? 0) + work,
                (current?.MatchedCallsites ?? 0) + 1);
        }
        return totals;
    }

    private static bool Contains(IReadOnlyList<SourceAdmission.Token> body, string word) =>
        body.Any(x => x.Value == word);

    private static string Literal(string source, SourceAdmission.Token token) =>
        source[token.Start..token.End].Trim('"', '\'');

    private static string Module(IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i + 1 < t.Count && i < 60; i++)
        {
            if (t[i].Value != "module") continue;
            int line = t[i].Line;
            var part = new StringBuilder();
            for (int k = i + 1; k < t.Count && t[k].Line == line; k++)
            {
                if (!Ident(t[k].Value) && t[k].Value != ".") break;
                part.Append(t[k].Value);
            }
            return part.ToString();
        }
        return "";
    }

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int start, string open, string close)
    {
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        throw new InvalidDataException("CALLBACK_SEMANTICS_UNBALANCED_TOKENS");
    }

    private static bool Ident(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(x => char.IsLetterOrDigit(x) || x == '_');

    private static bool Inside(string child, string parent)
    {
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        child = Path.GetFullPath(child);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
