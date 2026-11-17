using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// G-PureConstantSlice V1. A provably pure, zero-argument final static Bool
/// method that returns a literal is equivalent to that literal at each direct
/// callsite. It is a partial expression rewrite: no enclosing caller, event,
/// setter or live game-state read is suppressed or reordered.
///
/// Uses the existing SourceAdmission tokens, method index and same F11 source
/// seal as every other generic. Never uses a mod/folder allowlist.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private const string PureConstantRule = "PURE_FINAL_STATIC_BOOL_V1";
    private const string PureConstantMarker =
        "GCO-GENERIC:" + PureConstantRule;

    private sealed record ProvenLiteral(
        string ClassName, string Module, string Method, string Value,
        FileSource File);
    private sealed record ConstantEdit(int Begin, int End, string Replacement,
        string Rule, string Target, string Value, int Line);
    private sealed record ConstantState(List<ConstantEdit> Edits, int Completed,
        int UnsafeRefusals, List<object> Sites);

    internal sealed record ConstantSummary(
        int SourceFiles, int EligibleSites, int EligibleFiles,
        int AlreadyComplete, int Refusals);

    internal static ConstantSummary ScanPureConstantSlices(
        string gameRoot, string outputPath)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputPath);
        if (Inside(output, game) || File.Exists(output) ||
            Directory.Exists(output))
            throw new InvalidDataException("PURE_BOOL_SCAN_OUTPUT_NOT_ISOLATED");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (files, methods) = ReadAllMethodSources(game, admission);
        var literals = BuildSourceProvenLiterals(files, methods);
        var rows = new List<object>();
        int eligible = 0, touched = 0, complete = 0, refusals = 0;
        foreach (var file in files)
        {
            var state = ExaminePureConstantCalls(file, literals, methods);
            eligible += state.Edits.Count;
            if (state.Edits.Count > 0) touched++;
            complete += state.Completed;
            refusals += state.UnsafeRefusals;
            rows.AddRange(state.Sites);
        }
        AssertConstantSourceStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.pure-final-bool.scan.v1",
            sourceFiles = admission.InspectedFileCount,
            sourceStackSha256 = admission.SourceStackSha256,
            rule = PureConstantRule,
            eligibleSites = eligible,
            eligibleFiles = touched,
            alreadyComplete = complete,
            refusedSites = refusals,
            provenLiteralProviders = literals.Count,
            lexicalShapeAloneDoesNotAuthorizeRewrite = true,
            noClassOrModNameAllowlist = true,
            callerExecutionAndLiveStateUnchanged = true,
            sourceCallSideEffectsProvenAbsent = true,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            performanceSavingsClaimed = false,
            sourceWrites = false,
            sites = rows
        }, new JsonSerializerOptions { WriteIndented = false }),
        new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, touched,
            complete, refusals);
    }

    internal static int PreviewPureConstantSlices(
        string gameRoot, string analysisPath, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            analysis = Path.GetFullPath(analysisPath),
            output = Path.GetFullPath(outputRoot);
        if (Inside(output, game) || File.Exists(output) ||
            (Directory.Exists(output) &&
            Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("PURE_BOOL_PREVIEW_OUTPUT_NOT_ISOLATED");
        if (!File.Exists(analysis))
            throw new InvalidDataException("PURE_BOOL_SEALED_CAPTURE_MISSING");

        string? baseScripts = null;
        if (basePreviewRoot is not null)
        {
            string basePreview = Path.GetFullPath(basePreviewRoot);
            if (!Directory.Exists(basePreview) ||
                Inside(basePreview, game) || Inside(basePreview, output) ||
                Inside(output, basePreview))
                throw new InvalidDataException("PURE_BOOL_BASE_PREVIEW_NOT_ISOLATED");
            baseScripts = Path.Combine(basePreview, "r6", "scripts");
            if (!Directory.Exists(baseScripts))
                throw new InvalidDataException("PURE_BOOL_BASE_PREVIEW_SCRIPTS_MISSING");
        }

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var document = JsonDocument.Parse(
            File.ReadAllText(analysis)))
        {
            var root = document.RootElement;
            if (root.GetProperty("schemaVersion").GetString() !=
                "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance")
                    .GetString() != "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source")
                    .GetProperty("sourceStackSha256").GetString(),
                    admission.SourceStackSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "PURE_BOOL_CAPTURE_OR_SOURCE_DRIFT");
        }

        var (files, methods) = ReadAllMethodSources(game, admission);
        var sources = BuildSourceProvenLiterals(files, methods);
        var staged = new List<(string Relative, string Result)>();
        int edits = 0;
        foreach (var file in files)
        {
            var original = ExaminePureConstantCalls(file, sources, methods);
            if (original.Edits.Count == 0) continue;
            FileSource effectiveFile = file;
            if (baseScripts is not null)
            {
                string path = Path.GetFullPath(Path.Combine(baseScripts,
                    file.Relative["r6/scripts/".Length..]
                        .Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(path, baseScripts))
                    throw new InvalidDataException("PURE_BOOL_BASE_PATH_ESCAPE");
                if (File.Exists(path))
                {
                    string stagedSource = File.ReadAllText(path, Encoding.UTF8);
                    effectiveFile = new FileSource(path, file.Relative,
                        file.Module, stagedSource,
                        SourceAdmission.ReadTokens(stagedSource));
                    var effectiveState = ExaminePureConstantCalls(
                        effectiveFile, sources, methods);
                    // An existing generic stage can change unrelated parts
                    // of the same file; this rule requires every ORIGINAL
                    // target expression + surrounding token context to remain.
                    if (!PureConstantEditWitness(original)
                        .SequenceEqual(PureConstantEditWitness(effectiveState),
                            StringComparer.Ordinal) ||
                        effectiveState.Completed != original.Completed)
                        throw new InvalidDataException(
                            "PURE_BOOL_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT");
                }
            }
            var state = ExaminePureConstantCalls(effectiveFile,
                sources, methods);
            string updated = effectiveFile.Text;
            foreach (var edit in state.Edits
                .OrderByDescending(x => x.Begin))
            {
                if (!updated.AsSpan(edit.Begin, edit.End - edit.Begin)
                    .SequenceEqual(effectiveFile.Text.AsSpan(
                        edit.Begin, edit.End - edit.Begin)))
                    throw new InvalidDataException(
                        "PURE_BOOL_SOURCE_CHANGED_DURING_COMPOSITION");
                updated = updated[..edit.Begin] + edit.Replacement +
                    updated[edit.End..];
                edits++;
            }
            var generatedFile = new FileSource(effectiveFile.Path,
                file.Relative, file.Module, updated,
                SourceAdmission.ReadTokens(updated));
            var generated = ExaminePureConstantCalls(
                generatedFile, sources, methods);
            if (generated.Completed != state.Completed + state.Edits.Count ||
                generated.Edits.Count != 0)
                throw new InvalidDataException(
                    "PURE_BOOL_GENERATED_POSTCONDITION_FAILED");
            staged.Add((file.Relative, updated));
        }
        AssertConstantSourceStable(admission);
        foreach (var item in staged)
        {
            string target = Path.GetFullPath(Path.Combine(output,
                item.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(target, output) || File.Exists(target))
                throw new InvalidDataException("PURE_BOOL_STAGE_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, item.Result, new UTF8Encoding(false));
        }
        return edits;
    }

    private static string[] PureConstantEditWitness(ConstantState state) =>
        state.Edits.Select(x => x.Target + "\0" + x.Value + "\0" +
            x.Rule).OrderBy(x => x, StringComparer.Ordinal).ToArray();

    private static Dictionary<(string Class, string Method), ProvenLiteral>
        BuildSourceProvenLiterals(List<FileSource> files,
            List<MethodSource> methods)
    {
        // A short type name is used only if it resolves to ONE real class
        // declaration in the complete current source tree. A different mod's
        // same name, method overload, @wrapMethod or @replaceMethod refuses.
        var classCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var t = file.Tokens;
            for (int i = 0; i + 1 < t.Count; i++)
                if (t[i].Value == "class" && Ident(t[i + 1].Value))
                    classCounts[t[i + 1].Value] =
                        classCounts.GetValueOrDefault(t[i + 1].Value) + 1;
        }
        var overloads = methods
            .GroupBy(m => (m.Owner, m.Name))
            .ToDictionary(g => g.Key, g => g.Count());
        var foreignOverrides = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (var file in files)
        {
            var t = file.Tokens;
            for (int i = 0; i + 4 < t.Count; i++)
                if (t[i].Value == "@" &&
                    t[i + 1].Value is ("wrapMethod" or
                        "replaceMethod" or "addMethod") &&
                    t[i + 2].Value == "(" &&
                    Ident(t[i + 3].Value) && t[i + 4].Value == ")")
                    foreignOverrides.Add(t[i + 3].Value);
        }
        var proven = new Dictionary<(string Class, string Method),
            ProvenLiteral>();
        foreach (var m in methods)
        {
            var t = m.File.Tokens;
            if (m.Owner.Length == 0 || m.Annotation is not null ||
                foreignOverrides.Contains(m.Owner) ||
                classCounts.GetValueOrDefault(m.Owner) != 1 ||
                overloads.GetValueOrDefault((m.Owner, m.Name)) != 1 ||
                m.SignatureClose != m.SignatureOpen + 1 ||
                m.Open != m.SignatureClose + 4 ||
                t[m.SignatureClose + 1].Value != "-" ||
                t[m.SignatureClose + 2].Value != ">" ||
                t[m.SignatureClose + 3].Value != "Bool" ||
                m.Close != m.Open + 4 ||
                t[m.Open + 1].Value != "return" ||
                t[m.Open + 2].Value is not ("true" or "false") ||
                t[m.Open + 3].Value != ";")
                continue;

            // Check modifiers in this exact method declaration, stopping at
            // its preceding declaration/brace; not at another method's
            // "static" or "final" tokens elsewhere in the file.
            bool isStatic = false, isFinal = false, isConditional = false;
            for (int j = m.First - 1; j >= 0 &&
                j >= m.First - 10; j--)
            {
                if (t[j].Value is ("{" or "}" or ";" or ")")) break;
                if (t[j].Value == "static") isStatic = true;
                if (t[j].Value == "final") isFinal = true;
                if (t[j].Value is ("if" or "@" or "native"))
                    isConditional = true;
            }
            if (!isStatic || !isFinal || isConditional) continue;

            // Do not interpret a declaration under conditional compilation.
            // An external library may choose a different implementation.
            if (m.File.Tokens.Any(x => x.Value == "ModuleExists") ||
                m.File.Tokens.Any(x => x.Value == "ModuleNotExists"))
                continue;

            var entry = new ProvenLiteral(m.Owner, m.File.Module,
                m.Name, t[m.Open + 2].Value, m.File);
            proven.Add((m.Owner, m.Name), entry);
        }
        return proven;
    }

    private static ConstantState ExaminePureConstantCalls(
        FileSource file,
        IReadOnlyDictionary<(string Class, string Method), ProvenLiteral>
            providers, IReadOnlyList<MethodSource> methods)
    {
        var edits = new List<ConstantEdit>();
        var sites = new List<object>();
        var t = file.Tokens;
        int completed = VerifyConstantMarkers(file, providers);
        int refused = 0;

        // Caller must be a real parsed method. Do not rewrite class field
        // initializers, preprocessor blocks, comments or string literals.
        var fileMethods = methods.Where(m =>
            ReferenceEquals(m.File, file)).ToArray();
        // Effective base-preview files have the same method declarations,
        // but their token offsets can shift. Parse again with the *existing*
        // source-method parser, not with a parallel regex.
        if (fileMethods.Length == 0 ||
            fileMethods[0].File.Text != file.Text)
            fileMethods = ParseMethods(file).ToArray();

        foreach (var m in fileMethods)
        {
            for (int i = m.Open + 1; i + 4 < m.Close; i++)
            {
                if (!Ident(t[i].Value) || t[i + 1].Value != "." ||
                    !Ident(t[i + 2].Value) ||
                    t[i + 3].Value != "(" || t[i + 4].Value != ")" ||
                    (i > 0 && t[i - 1].Value is ("." or "@")))
                    continue;
                string owner = t[i].Value, method = t[i + 2].Value;
                if (!providers.TryGetValue((owner, method),
                    out var entry)) { continue; }
                if (!SameLiteralModuleAccessible(file, entry) ||
                    ReceiverCouldBeShadowedByLocal(m, t, owner, i))
                { refused++; continue; }
                string target = owner + "." + method;
                string replacement = entry.Value + " /* " +
                    PureConstantMarker + " type=" + owner +
                    " method=" + method + " value=" +
                    entry.Value + " */";
                edits.Add(new ConstantEdit(t[i].Start,
                    t[i + 4].End, replacement, PureConstantRule,
                    target, entry.Value, t[i].Line));
                sites.Add(new {
                    path = file.Relative,
                    method = m.Name,
                    line = t[i].Line,
                    rule = PureConstantRule,
                    literal = entry.Value,
                    target,
                    completeSourceBodyAndFinalStaticTypeProven = true,
                    uniqueGlobalOwnerAndMethodProven = true,
                    moduleImportProven = true,
                    wrapperAndOverrideAmbiguityRefused = true,
                    originalCallerAndStateUnchanged = true,
                    actualRuntimeSavingsUnknown = true,
                    productionGenerationAuthorized = false
                });
                i += 4;
            }
        }
        return new(edits, completed, refused, sites);
    }

    private static bool ReceiverCouldBeShadowedByLocal(
        MethodSource caller, IReadOnlyList<SourceAdmission.Token> t,
        string className, int call)
    {
        // A token spelled like a class can actually be an author variable.
        // Reject same-name signature parameters, local declarations or
        // assignments instead of changing virtual instance calls.
        for (int i = caller.SignatureOpen; i < call; i++)
        {
            if (t[i].Value != className) continue;
            if (i > 0 && t[i - 1].Value is ("let" or "var"))
                return true;
            if (i + 1 < call && t[i + 1].Value == ":" &&
                i > caller.SignatureOpen && i < caller.SignatureClose)
                return true;
            if (i + 1 < call && t[i + 1].Value == "=" &&
                i > caller.Open)
                return true;
        }
        return false;
    }

    private static bool SameLiteralModuleAccessible(
        FileSource file, ProvenLiteral entry)
    {
        if (ReferenceEquals(file, entry.File) ||
            file.Path.Equals(entry.File.Path,
                StringComparison.OrdinalIgnoreCase) ||
            (file.Module.Length > 0 &&
            file.Module.Equals(entry.Module, StringComparison.Ordinal)))
            return true;
        if (entry.Module.Length == 0) return false;
        var t = file.Tokens;
        for (int i = 0; i < t.Count - 3; i++)
        {
            if (t[i].Value != "import") continue;
            var parts = new StringBuilder();
            int line = t[i].Line;
            for (int j = i + 1; j < t.Count &&
                t[j].Line == line; j++)
                parts.Append(t[j].Value);
            if (parts.ToString().Equals(entry.Module + ".*",
                StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static int VerifyConstantMarkers(
        FileSource file,
        IReadOnlyDictionary<(string Class, string Method), ProvenLiteral>
            providers)
    {
        string source = file.Text;
        int count = source.Split(PureConstantMarker,
            StringSplitOptions.None).Length - 1;
        if (count == 0) return 0;
        var regex = new Regex(
            @"(?<value>true|false) /\* " +
            Regex.Escape(PureConstantMarker) +
            @" type=(?<type>[A-Za-z_][A-Za-z0-9_]*)" +
            @" method=(?<method>[A-Za-z_][A-Za-z0-9_]*)" +
            @" value=(?<declared>true|false) \*/",
            RegexOptions.CultureInvariant);
        var matches = regex.Matches(source);
        if (count != matches.Count)
            throw new InvalidDataException("PURE_BOOL_PARTIAL_OR_DRIFTED_MARKER");
        foreach (Match marker in matches)
        {
            string type = marker.Groups["type"].Value,
                method = marker.Groups["method"].Value,
                value = marker.Groups["value"].Value;
            if (marker.Groups["declared"].Value != value ||
                !providers.TryGetValue((type, method), out var entry) ||
                entry.Value != value ||
                !SameLiteralModuleAccessible(file, entry))
                throw new InvalidDataException(
                    "PURE_BOOL_SOURCE_LITERAL_OR_PROVIDER_DRIFT");
            // An attacker-supplied marker in a comment or string is not
            // equivalent to generated code; enforce lexer code token.
            if (!file.Tokens.Any(t => t.Start ==
                marker.Groups["value"].Index && t.Value == value))
                throw new InvalidDataException(
                    "PURE_BOOL_MARKER_NOT_ON_CODE_VALUE");
        }
        return count;
    }

    private static void AssertConstantSourceStable(SourceAdmission admission)
    {
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "PURE_BOOL_SOURCE_CHANGED_DURING_OPERATION");
    }
}
