using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Preview-only generic late service read for exact action-type early return.
/// An unrelated action type must never perform the expensive system lookup.
/// Existing wrappedMethod runs first. No action registration, state, timing,
/// consumer or author effects are moved across the wrapper call.
/// This is a source-shape rule, never keyed to a mod folder or source SHA.
/// </summary>
internal static class GenericLateSystemReadPass
{
    internal const string Rule = "SYSTEM_READ_AFTER_ACTION_GUARD_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;
    private static readonly string[] ResultPrefix = T("= wrappedMethod(action, consumer);");
    private static readonly string[] QuestPrefix = T("= GameInstance.GetQuestsSystem(this.GetGame());");
    private static readonly string[] TypeGuard = T(
        "if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED)");

    private sealed record Edit(int Begin, int End, int GuardStart, int GuardEnd,
        int ReadStart, int ReadEnd, int Line);
    private sealed record FileView(string Relative, string Text,
        IReadOnlyList<SourceAdmission.Token> Tokens);
    private sealed record Classified(List<Edit> Eligible, int Complete);
    internal sealed record Summary(int Sources, int Eligible, int Complete);

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        var game = Path.GetFullPath(gameRoot);
        var output = Path.GetFullPath(outputFile);
        if (Within(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("LATE_SYSTEM_READ_SCAN_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var candidates = new List<object>();
        int eligible = 0, complete = 0;
        foreach (var file in Files(game, admission))
        {
            var e = Examine(file.Text, file.Tokens);
            var reuse = GenericLocalSystemReadReuse.Examine(file.Text, file.Tokens);
            eligible += e.Eligible.Count + reuse.Candidates.Count;
            complete += e.Complete + reuse.AlreadyComplete;
            foreach (var edit in reuse.Candidates)
                candidates.Add(new {
                    path = "r6/scripts/" + file.Relative,
                    line = edit.Line,
                    rule = GenericLocalSystemReadReuse.Rule,
                    state = "PRISTINE_APPLICABLE",
                    expensiveRead = "GameInstance." + edit.System,
                    guard = "CONSECUTIVE_SAME_BLOCK_SERVICE_HANDLE",
                    redundantReads = edit.AvoidedReads,
                    generationAuthorized = false
                });
            foreach (var edit in e.Eligible)
                candidates.Add(new {
                    path = "r6/scripts/" + file.Relative,
                    line = edit.Line,
                    rule = Rule,
                    state = "PRISTINE_APPLICABLE",
                    expensiveRead = "GameInstance.GetQuestsSystem",
                    guard = "BUTTON_RELEASED_EARLY_RETURN",
                    generationAuthorized = false
                });
        }
        AssertStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.late-system-read.scan.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = admission.InspectedFileCount,
            eligible, alreadyComplete = complete,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            measuredSavingsClaimed = false,
            sourceWrites = false,
            candidates
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, complete);
    }

    internal static int Preview(string gameRoot, string analysisFile,
        string outputRoot, string? basePreview = null)
    {
        var game = Path.GetFullPath(gameRoot);
        var analysis = Path.GetFullPath(analysisFile);
        var output = Path.GetFullPath(outputRoot);
        if (Within(output, game) || File.Exists(output) ||
            (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()) ||
            (basePreview is not null &&
                (Within(Path.GetFullPath(basePreview), game) ||
                 Within(Path.GetFullPath(basePreview), output) ||
                 Within(output, Path.GetFullPath(basePreview)))))
            throw new InvalidDataException("LATE_SYSTEM_READ_PREVIEW_OUTPUT_NOT_ISOLATED");
        if (!File.Exists(analysis))
            throw new InvalidDataException("LATE_SYSTEM_READ_VERIFIED_CAPTURE_REQUIRED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var doc = JsonDocument.Parse(File.ReadAllText(analysis)))
        {
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance").GetString() !=
                    "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source").GetProperty("sourceStackSha256")
                    .GetString(), admission.SourceStackSha256,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("LATE_SYSTEM_READ_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");
        }
        var outputFiles = new List<(string Relative, string Source)>();
        int edits = 0;
        foreach (var file in Files(game, admission))
        {
            var sourceSelection = Examine(file.Text, file.Tokens);
            var sourceReuse = GenericLocalSystemReadReuse.Examine(
                file.Text, file.Tokens);
            if (sourceSelection.Eligible.Count == 0 &&
                sourceReuse.Candidates.Count == 0)
                continue;
            string changed = file.Text;
            if (basePreview is not null)
            {
                string prior = Path.GetFullPath(Path.Combine(basePreview,
                    "r6", "scripts",
                    file.Relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Within(prior, basePreview))
                    throw new InvalidDataException(
                        "LATE_SYSTEM_READ_UNSAFE_BASE_PREVIEW");
                if (File.Exists(prior))
                    changed = File.ReadAllText(prior, Encoding.UTF8);
            }
            // Previous ordered passes may already have modified this same
            // owner file. Re-examine the effective bytes; do NOT replace an
            // earlier generated file with an original-source rewrite.
            var tokens = SourceAdmission.ReadTokens(changed);
            var selected = Examine(changed, tokens);
            var reusable = GenericLocalSystemReadReuse.Examine(changed, tokens);
            if (selected.Eligible.Count != sourceSelection.Eligible.Count ||
                reusable.Candidates.Count != sourceReuse.Candidates.Count)
                throw new InvalidDataException(
                    "LATE_SYSTEM_READ_COMPOSITION_CANDIDATE_DRIFT");
            foreach (var edit in selected.Eligible.OrderByDescending(e => e.Begin))
            {
                string nl = changed.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                int line = changed.LastIndexOf('\n', Math.Max(0, edit.Begin - 1)) + 1;
                string indent = changed[line..edit.Begin];
                if (indent.Any(c => c != ' ' && c != '\t'))
                    throw new InvalidDataException("LATE_SYSTEM_READ_INDENT_UNPROVEN");
                string gate = changed[edit.GuardStart..edit.GuardEnd];
                string originalRead = changed[edit.ReadStart..edit.ReadEnd];
                // Reorder only the two already-proven whitespace-separated
                // statements and leave body, return and action semantics intact.
                string replacement = Marker + nl + indent + gate + nl + indent + originalRead;
                changed = changed[..edit.Begin] + replacement + changed[edit.End..];
                edits++;
            }
            // Same canonical generator, source-index and sealed capture gate:
            // reuse stable system handles inside adjacent straight-line work.
            var composed = GenericLocalSystemReadReuse.Rewrite(changed,
                SourceAdmission.ReadTokens(changed));
            if (composed.Edits != reusable.Candidates.Count)
                throw new InvalidDataException(
                    "LATE_SYSTEM_READ_REUSE_COMPOSITION_COUNT_DRIFT");
            changed = composed.Source;
            edits += composed.Edits;
            outputFiles.Add((file.Relative, changed));
        }
        AssertStable(admission);
        foreach (var staged in outputFiles)
        {
            string path = Path.GetFullPath(Path.Combine(output, "r6", "scripts",
                staged.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(path, output) || File.Exists(path))
                throw new InvalidDataException("LATE_SYSTEM_READ_UNSAFE_OUTPUT");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, staged.Source, new UTF8Encoding(false));
        }
        return edits;
    }

    private static Classified Examine(string text, IReadOnlyList<SourceAdmission.Token> tokens)
    {
        var edits = new List<Edit>();
        int complete = 0;
        for (int i = 0; i + 7 < tokens.Count; i++)
        {
            if (tokens[i].Value != "let" || !Identifier(tokens[i + 1].Value) ||
                !Match(tokens, i + 2, ResultPrefix)) continue;

            string result = tokens[i + 1].Value;
            int next = i + 2 + ResultPrefix.Length;
            if (next + 7 >= tokens.Count) continue;

            // Full pristine: let <sys> = ... ; if !IsDefined(<sys>)
            // {return <result>;} if !Equals(GetType(action), RELEASED) {return <result>;}
            if (tokens[next].Value == "let" && Identifier(tokens[next + 1].Value) &&
                Match(tokens, next + 2, QuestPrefix))
            {
                string system = tokens[next + 1].Value;
                int checkAt = next + 2 + QuestPrefix.Length;
                var nullGuard = T($"if !IsDefined({system}) {{ return {result}; }}");
                if (!Match(tokens, checkAt, nullGuard)) continue;
                int actionAt = checkAt + nullGuard.Length;
                var actionGuard = T($"if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) {{ return {result}; }}");
                if (!Match(tokens, actionAt, actionGuard)) continue;
                int readStart = tokens[next].Start;
                int readEnd = tokens[actionAt - 1].End;
                int guardStart = tokens[actionAt].Start;
                int guardEnd = tokens[actionAt + actionGuard.Length - 1].End;
                if (!Whitespace(text, tokens[next - 1].End, readStart) ||
                    !Whitespace(text, tokens[checkAt - 1].End, tokens[checkAt].Start) ||
                    !Whitespace(text, tokens[actionAt - 1].End, guardStart) ||
                    text[readStart..guardEnd].Contains("//", StringComparison.Ordinal) ||
                    text[readStart..guardEnd].Contains("/*", StringComparison.Ordinal))
                    continue;
                // Adjacent source statements only; source type and wrapper
                // return are proved by their tokens, not a mod-name allowlist.
                edits.Add(new(readStart, guardEnd, guardStart, guardEnd,
                    readStart, readEnd, tokens[next].Line));
                continue;
            }
            // Optimized marker shape: action guard precedes the same
            // read/null guard, all within one statement sequence.
            var typeGuard = T($"if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) {{ return {result}; }}");
            if (!Match(tokens, next, typeGuard)) continue;
            int readAt = next + typeGuard.Length;
            if (readAt + 2 >= tokens.Count ||
                tokens[readAt].Value != "let" ||
                !Identifier(tokens[readAt + 1].Value) ||
                !Match(tokens, readAt + 2, QuestPrefix)) continue;
            string cached = tokens[readAt + 1].Value;
            int nullAt = readAt + 2 + QuestPrefix.Length;
            var expectedNull = T($"if !IsDefined({cached}) {{ return {result}; }}");
            if (!Match(tokens, nullAt, expectedNull)) continue;
            int marker = text.LastIndexOf(Marker, tokens[next].Start,
                StringComparison.Ordinal);
            if (marker < 0 ||
                !string.IsNullOrWhiteSpace(text[(marker + Marker.Length)..tokens[next].Start]))
                throw new InvalidDataException("LATE_SYSTEM_READ_UNOWNED_OPTIMIZED_SHAPE");
            complete++;
        }
        if (text.Split(Marker, StringSplitOptions.None).Length - 1 != complete)
            throw new InvalidDataException("LATE_SYSTEM_READ_PARTIAL_OR_DRIFTED_GENERATED_MARKER");
        return new(edits, complete);
    }

    private static bool Whitespace(string source, int start, int end) =>
        start <= end && string.IsNullOrWhiteSpace(source[start..end]);

    private static bool Identifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static string[] T(string text) =>
        SourceAdmission.ReadTokens(text).Select(x => x.Value).ToArray();

    private static bool Match(IReadOnlyList<SourceAdmission.Token> source, int index,
        string[] expected)
    {
        if (index < 0 || index + expected.Length > source.Count) return false;
        for (int k = 0; k < expected.Length; k++)
            if (!string.Equals(source[index + k].Value, expected[k], StringComparison.Ordinal))
                return false;
        return true;
    }

    private static IEnumerable<FileView> Files(string game, SourceAdmission admission)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories)
                 .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!admission.IsFileIndexed(file)) continue;
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(file));
            yield return new(Path.GetRelativePath(scripts, file).Replace('\\', '/'),
                text, SourceAdmission.ReadTokens(text));
        }
    }

    private static void AssertStable(SourceAdmission admission)
    {
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(), StringComparison.Ordinal))
            throw new InvalidDataException("LATE_SYSTEM_READ_SOURCE_DRIFT");
    }

    private static bool Within(string path, string root) =>
        path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
