using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Immutable blackboard definition lookup, statement-local V1.
/// Reuses the same definition group twice within one straight-line REDscript
/// statement. Live GetInt/GetBool/SetVariant and blackboard instance access
/// are never cached. Preview-only; matches source shape, never a mod name.
/// </summary>
internal static class GenericBlackboardDefinitionPass
{
    internal const string Rule = "BLACKBOARD_DEFINITION_STATEMENT_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;
    private sealed record Span(int Start, int End);
    private sealed record Candidate(string Relative, int Line, string Group,
        string LocalName, int Begin, int End, Span Left, Span Right);
    private sealed record FileView(string Relative, string Content,
        IReadOnlyList<SourceAdmission.Token> Tokens);
    internal sealed record Summary(int Files, int Eligible, int AlreadyComplete);

    internal static Summary Scan(string gameRoot, string outputPath)
    {
        string game = Path.GetFullPath(gameRoot), output = Path.GetFullPath(outputPath);
        if (Within(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("BBDEFS_SCAN_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var found = new List<object>();
        int eligible = 0, complete = 0;
        foreach (var view in Files(game, admission))
        {
            var state = Examine(view);
            complete += state.Complete;
            eligible += state.Candidates.Count;
            foreach (var c in state.Candidates)
                found.Add(new {
                    path = "r6/scripts/" + c.Relative,
                    line = c.Line, rule = Rule,
                    group = c.Group,
                    state = "PRISTINE_APPLICABLE",
                    immutableDefinitionsOnly = true,
                    liveBlackboardValuesRetained = true,
                    generationAuthorized = false
                });
        }
        AssertStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.bbdefs.scan.v1",
            sourceFiles = admission.InspectedFileCount,
            sourceStackSha256 = admission.SourceStackSha256,
            eligible, alreadyComplete = complete,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            measuredSavingsClaimed = false,
            sourceWrites = false,
            candidates = found
        }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, complete);
    }

    internal static int Preview(string gameRoot, string analysisPath, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            analysis = Path.GetFullPath(analysisPath),
            output = Path.GetFullPath(outputRoot);
        // The preceding generic rules generate a private, source-proven
        // effective preview. Compose against those bytes, never against live
        // files, and never stage over an existing output.
        string? baseScripts = null;
        if (basePreviewRoot is not null)
        {
            string basePreview = Path.GetFullPath(basePreviewRoot);
            if (!Directory.Exists(basePreview) || Within(basePreview, game) ||
                basePreview.Equals(output, StringComparison.OrdinalIgnoreCase) ||
                Within(basePreview, output) || Within(output, basePreview))
                throw new InvalidDataException("BBDEFS_BASE_PREVIEW_NOT_ISOLATED");
            baseScripts = Path.Combine(basePreview, "r6", "scripts");
            if (!Directory.Exists(baseScripts))
                throw new InvalidDataException("BBDEFS_BASE_PREVIEW_SCRIPTS_MISSING");
        }
        if (Within(output, game) || File.Exists(output) ||
            (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("BBDEFS_PREVIEW_OUTPUT_NOT_ISOLATED");
        if (!File.Exists(analysis))
            throw new InvalidDataException("BBDEFS_VERIFIED_CAPTURE_MISSING");
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
                throw new InvalidDataException("BBDEFS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");
        }

        var staged = new List<(string Path, string Content)>();
        int edits = 0;
        foreach (var view in Files(game, admission))
        {
            var originalState = Examine(view);
            if (originalState.Candidates.Count == 0) continue;
            string effective = view.Content;
            State state = originalState;
            if (baseScripts is not null)
            {
                string stagedPath = Path.GetFullPath(Path.Combine(baseScripts,
                    view.Relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Within(stagedPath, baseScripts))
                    throw new InvalidDataException("BBDEFS_BASE_PREVIEW_PATH_ESCAPE");
                if (File.Exists(stagedPath))
                {
                    effective = File.ReadAllText(stagedPath, Encoding.UTF8);
                    state = Examine(new FileView(view.Relative, effective,
                        SourceAdmission.ReadTokens(effective)));

                    // Exact original candidate statement must survive all
                    // prior source-owned generic transforms. A changed line,
                    // reordered expression or removed candidate is a real
                    // conflict, never a reason to overwrite the earlier pass.
                    string[] before = originalState.Candidates.Select(c =>
                        c.Group + "\0" + view.Content[c.Begin..c.End])
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    string[] after = state.Candidates.Select(c =>
                        c.Group + "\0" + effective[c.Begin..c.End])
                        .OrderBy(x => x, StringComparer.Ordinal).ToArray();
                    if (state.Complete != originalState.Complete ||
                        !before.SequenceEqual(after, StringComparer.Ordinal))
                        throw new InvalidDataException(
                            "BBDEFS_COMPOSITION_STATEMENT_OVERLAP_OR_DRIFT");
                }
            }
            string updated = effective;
            string newline = updated.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            foreach (var c in state.Candidates.OrderByDescending(x => x.Begin))
            {
                string original = updated[c.Begin..c.End];
                string rewritten = original;
                foreach (var reference in new[] { c.Left, c.Right }.OrderByDescending(s => s.Start))
                    rewritten = rewritten[..(reference.Start - c.Begin)] + c.LocalName +
                        rewritten[(reference.End - c.Begin)..];
                int indentLength = original.Length - original.TrimStart(' ', '\t').Length;
                string indent = original[..indentLength];
                string declaration = indent + Marker + " group=" + c.Group +
                    " name=" + c.LocalName + newline + indent +
                    "let " + c.LocalName + " = GetAllBlackboardDefs()." + c.Group +
                    ";" + newline;
                updated = updated[..c.Begin] + declaration + rewritten + updated[c.End..];
                edits++;
            }
            // Verify the emitted result, not just the input predicate.
            var generated = Examine(new FileView(view.Relative, updated,
                SourceAdmission.ReadTokens(updated)));
            if (generated.Complete != state.Complete + state.Candidates.Count)
                throw new InvalidDataException("BBDEFS_GENERATED_POSTCONDITION_FAILED");
            staged.Add((view.Relative, updated));
        }
        AssertStable(admission);
        foreach (var file in staged)
        {
            string path = Path.GetFullPath(Path.Combine(output, "r6", "scripts",
                file.Path.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(path, output) || File.Exists(path))
                throw new InvalidDataException("BBDEFS_UNSAFE_OUTPUT_PATH");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, file.Content, new UTF8Encoding(false));
        }
        return edits;
    }

    private sealed record State(List<Candidate> Candidates, int Complete);
    private static State Examine(FileView view)
    {
        string source = view.Content;
        var tokens = view.Tokens;
        int already = VerifyGenerated(source);
        var positions = new List<(int Index, string Group)>();
        for (int i = 0; i + 5 < tokens.Count; i++)
            if (tokens[i].Value == "GetAllBlackboardDefs" &&
                tokens[i + 1].Value == "(" &&
                tokens[i + 2].Value == ")" &&
                tokens[i + 3].Value == "." &&
                Identifier(tokens[i + 4].Value))
                positions.Add((i, tokens[i + 4].Value));
        // The fifth token is the group; e.g. Group.Field is deliberately
        // outside the replacement span, and still reads its live value.
        var result = new List<Candidate>();
        foreach (var grouped in positions.GroupBy(x => tokens[x.Index].Line))
        {
            var sites = grouped.ToArray();
            if (sites.Length != 2 || sites[0].Group != sites[1].Group) continue;
            int line = grouped.Key;
            int start = source.LastIndexOf('\n', Math.Max(0, tokens[sites[0].Index].Start - 1)) + 1;
            int end = source.IndexOf('\n', start);
            if (end < 0) end = source.Length;
            string text = source[start..end].TrimEnd('\r');
            string trimmed = text.Trim();
            if (trimmed.Length == 0 || !WithinFunction(tokens, tokens[sites[0].Index].Start) ||
                trimmed.StartsWith("return ", StringComparison.Ordinal) ||
                trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("#", StringComparison.Ordinal) ||
                text.Contains("//", StringComparison.Ordinal) ||
                text.Contains("/*", StringComparison.Ordinal) ||
                text.Contains("*/", StringComparison.Ordinal) ||
                text.Contains('"') || text.Contains('?') ||
                text.Contains("&&", StringComparison.Ordinal) ||
                text.Contains("||", StringComparison.Ordinal) ||
                text.Contains("wrappedMethod", StringComparison.Ordinal) ||
                !(trimmed.EndsWith(';') || trimmed.EndsWith('{')) ||
                text.Count(c => c == ';') > 1)
                continue;

            string name = "__gcoBbDefL" + line;
            if (source.Contains(name, StringComparison.Ordinal)) continue;
            var a = sites[0].Index;
            var b = sites[1].Index;
            if (tokens[a].Line != tokens[a + 4].Line ||
                tokens[b].Line != tokens[b + 4].Line ||
                tokens[a + 4].End > tokens[b].Start ||
                tokens[a].Start < start || tokens[b + 4].End > end)
                continue;
            result.Add(new(view.Relative, line, sites[0].Group, name,
                start, end,
                new(tokens[a].Start, tokens[a + 4].End),
                new(tokens[b].Start, tokens[b + 4].End)));
        }
        return new(result, already);
    }

    private static int VerifyGenerated(string source)
    {
        int count = source.Split(Marker, StringSplitOptions.None).Length - 1;
        if (count == 0) return 0;
        string[] lines = source.Split('\n');
        int accepted = 0;
        var pattern = new Regex("^" + Regex.Escape(Marker) +
            @" group=(?<group>[A-Za-z_][A-Za-z0-9_]*) name=(?<var>__gcoBbDefL[0-9]+)$",
            RegexOptions.CultureInvariant);
        for (int i = 0; i < lines.Length; i++)
        {
            string trimmed = lines[i].Trim();
            if (!trimmed.Contains(Marker, StringComparison.Ordinal)) continue;
            var match = pattern.Match(trimmed);
            if (!match.Success || i + 2 >= lines.Length)
                throw new InvalidDataException("BBDEFS_PARTIAL_OR_DRIFTED_MARKER");
            string group = match.Groups["group"].Value;
            string variable = match.Groups["var"].Value;
            string indent = lines[i][..(lines[i].Length - lines[i].TrimStart(' ', '\t').Length)];
            string declaration = indent + "let " + variable +
                " = GetAllBlackboardDefs()." + group + ";";
            if (lines[i + 1].TrimEnd('\r') != declaration ||
                !lines[i + 2].StartsWith(indent, StringComparison.Ordinal))
                throw new InvalidDataException("BBDEFS_PARTIAL_OR_DRIFTED_DECLARATION");
            string use = lines[i + 2].TrimEnd('\r');
            int hits = Regex.Matches(use, @"\b" + Regex.Escape(variable) + @"\b").Count;
            if (hits != 2 || use.Contains("GetAllBlackboardDefs", StringComparison.Ordinal) ||
                use.Contains("//", StringComparison.Ordinal) ||
                use.Contains("/*", StringComparison.Ordinal) ||
                (!use.TrimEnd().EndsWith(';') && !use.TrimEnd().EndsWith('{')))
                throw new InvalidDataException("BBDEFS_PARTIAL_OR_DRIFTED_BODY");
            accepted++;
        }
        if (accepted != count)
            throw new InvalidDataException("BBDEFS_PARTIAL_OR_DRIFTED_MARKER_COUNT");
        return accepted;
    }

    private static bool WithinFunction(IReadOnlyList<SourceAdmission.Token> tokens, int charPos)
    {
        for (int i = 0; i + 3 < tokens.Count; i++)
        {
            if (tokens[i].Value != "func" || !Identifier(tokens[i + 1].Value) ||
                tokens[i + 2].Value != "(") continue;
            int parens = 0, close = -1;
            for (int j = i + 2; j < tokens.Count; j++)
            {
                if (tokens[j].Value == "(") parens++;
                if (tokens[j].Value == ")" && --parens == 0) { close = j; break; }
            }
            if (close < 0) continue;
            int opening = -1;
            for (int j = close + 1; j < Math.Min(tokens.Count, close + 40); j++)
            {
                if (tokens[j].Value == ";") break;
                if (tokens[j].Value == "{") { opening = j; break; }
            }
            if (opening < 0) continue;
            int depth = 0;
            for (int j = opening; j < tokens.Count; j++)
            {
                if (tokens[j].Value == "{") depth++;
                if (tokens[j].Value == "}" && --depth == 0)
                {
                    if (charPos > tokens[opening].Start && charPos < tokens[j].End)
                        return true;
                    break;
                }
            }
        }
        return false;
    }

    private static bool Identifier(string v) =>
        v.Length > 0 && (char.IsLetter(v[0]) || v[0] == '_') &&
        v.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static IEnumerable<FileView> Files(string game, SourceAdmission admission)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        foreach (string path in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!admission.IsFileIndexed(path)) continue;
            string text = Encoding.UTF8.GetString(File.ReadAllBytes(path));
            yield return new(Path.GetRelativePath(scripts, path).Replace('\\', '/'),
                text, SourceAdmission.ReadTokens(text));
        }
    }

    private static void AssertStable(SourceAdmission admission)
    {
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(), StringComparison.Ordinal))
            throw new InvalidDataException("BBDEFS_SOURCE_DRIFT");
    }

    private static bool Within(string path, string root) =>
        path.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
