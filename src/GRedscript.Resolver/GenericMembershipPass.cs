using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// R4/R5 first generic candidate: replace a proven pure CName membership
/// loop with REDscript's built-in ArrayContains. This is strictly a preview,
/// NOT an output-generation authorization: SCC/full stack and gameplay remain
/// independent required gates.
/// </summary>
internal static class GenericMembershipPass
{
    internal const string Rule = "CNAME_ARRAY_MEMBERSHIP_V1";
    private const string Marker = "// GRSP-GENERIC:" + Rule;

    private sealed record Match(
        int Start, int End, string Replacement, int Line);
    internal sealed record Summary(int SourceFiles, int Eligible,
        int AlreadyComplete);
    private sealed record Inspected(string Relative, string Text,
        IReadOnlyList<Match> Matches, int Complete);

    // Source structure decides eligibility, not whether this exact loop
    // executed in the user's F11 capture. The sealed capture is still
    // mandatory before generation and must match the entire installed stack.
    internal static Summary Scan(string gameRoot, string outputFile)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputFile);
        if (Within(output, game) || File.Exists(output) ||
            Directory.Exists(output))
            throw new InvalidDataException("CNAME_MEMBERSHIP_SCAN_OUTPUT_INVALID");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256!;
        var files = Inspect(game, admission);
        if (!string.Equals(fingerprint, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CNAME_MEMBERSHIP_SOURCE_CHANGED");

        int eligible = files.Sum(f => f.Matches.Count);
        int complete = files.Sum(f => f.Complete);
        var rows = files.SelectMany(f => f.Matches.Select(m => new {
            path = "r6/scripts/" + f.Relative, line = m.Line,
            rule = Rule, state = "PRISTINE_APPLICABLE",
            sourceOnlyEligibility = true
        })).ToArray();
        var report = new {
            schemaVersion = "gco.redscript.cname-membership.scan.v1",
            sourceStackSha256 = fingerprint,
            sourceFiles = admission.InspectedFileCount,
            eligible, alreadyComplete = complete,
            candidates = rows, sourceWrites = false,
            productionZipAuthorized = false
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, complete);
    }

    // Preserve the old CLI's preview.json and overlay layout. Both entrypoints
    // use ONE source-driven implementation and the same exact TryMatch proof.
    internal static object Preview(string gameRoot, string analysisFile,
        string outputRoot)
    {
        var result = Generate(gameRoot, analysisFile, outputRoot, null, true);
        return new {
            schemaVersion = "grsp.generic.preview.v1",
            rule = Rule, sourceStackSha256 = result.Fingerprint,
            sourceCount = result.SourceFiles,
            sourceShapeDriven = true,
            sourceSitesGeneratedWithoutIndividualCaptureHit = true,
            applicableFileCount = result.Files,
            alreadyCompleteFileCount = result.AlreadyComplete,
            stagedFiles = result.Files,
            transformedLoops = result.Edits,
            compilerValidated = false, gameplayValidated = false,
            generationAuthorized = false, deployable = false,
            outputKind = "ISOLATED_SOURCE_PREVIEW_NOT_GAME_ZIP"
        };
    }

    internal static int PreviewOrdered(string gameRoot, string analysisFile,
        string outputRoot, string? basePreview = null) =>
        Generate(gameRoot, analysisFile, outputRoot, basePreview, false).Edits;

    private sealed record Generated(int Edits, int Files,
        int AlreadyComplete, int SourceFiles, string Fingerprint);

    private static Generated Generate(string gameRoot, string analysisFile,
        string outputRoot, string? basePreview, bool legacyOverlay)
    {
        string game = Path.GetFullPath(gameRoot);
        string evidence = Path.GetFullPath(analysisFile);
        string output = Path.GetFullPath(outputRoot);
        if (Within(output, game) || output.Equals(evidence, StringComparison.OrdinalIgnoreCase) ||
            File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("CNAME_MEMBERSHIP_PREVIEW_OUTPUT_INVALID");
        if (basePreview is not null &&
            (Within(Path.GetFullPath(basePreview), game) ||
             Within(output, Path.GetFullPath(basePreview)) ||
             Within(Path.GetFullPath(basePreview), output)))
            throw new InvalidDataException("CNAME_MEMBERSHIP_BASE_PREVIEW_INVALID");

        using var capture = JsonDocument.Parse(File.ReadAllText(evidence));
        var root = capture.RootElement;
        if (root.GetProperty("schemaVersion").GetString() !=
                "grsp.resolver.input.v1" ||
            root.GetProperty("generationAuthorized").GetBoolean() ||
            root.GetProperty("capture").GetProperty("collectionProvenance")
                .GetString() != "GCO_INDEXED_COLLECTED_BYTES_VERIFIED")
            throw new InvalidDataException("CNAME_MEMBERSHIP_CAPTURE_UNVERIFIED");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256!;
        if (!string.Equals(fingerprint, root.GetProperty("source")
                .GetProperty("sourceStackSha256").GetString(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CNAME_MEMBERSHIP_SOURCE_DRIFT");
        var inspected = Inspect(game, admission);
        var staged = new List<(string Relative, string Text)>();
        int edits = 0;
        foreach (var file in inspected.Where(f => f.Matches.Count > 0))
        {
            string effective = file.Text;
            if (basePreview is not null)
            {
                string stagedPath = Path.Combine(basePreview, "r6", "scripts",
                    file.Relative.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(stagedPath))
                    effective = File.ReadAllText(stagedPath, Encoding.UTF8);
            }

            // The prior generator may have inserted earlier source edits.
            // Rebase only exact original loop bytes, uniquely, never by a
            // changed line number or by trusting an unverified marker.
            foreach (var match in file.Matches.Reverse())
            {
                string originalLoop = file.Text[match.Start..match.End];
                int at = effective.IndexOf(originalLoop, StringComparison.Ordinal);
                if (at < 0 || effective.IndexOf(originalLoop,
                        at + originalLoop.Length, StringComparison.Ordinal) >= 0)
                    throw new InvalidDataException(
                        "CNAME_MEMBERSHIP_COMPOSITION_DRIFT_OR_AMBIGUITY: " +
                        file.Relative);
                effective = effective[..at] + match.Replacement +
                    effective[(at + originalLoop.Length)..];
                edits++;
            }
            var tokens = SourceAdmission.ReadTokens(effective);
            int expected = file.Complete + file.Matches.Count;
            int actual = CountMarkers(effective);
            if (actual != expected ||
                !HasCompletedStructuralState(tokens, effective))
                throw new InvalidDataException(
                    "CNAME_MEMBERSHIP_GENERATED_POSTCONDITION: " + file.Relative);
            staged.Add((file.Relative, effective));
        }

        if (!string.Equals(fingerprint, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CNAME_MEMBERSHIP_SOURCE_CHANGED");

        Directory.CreateDirectory(output);
        foreach (var file in staged)
        {
            string relative = legacyOverlay ?
                Path.Combine("overlay", "r6", "scripts", file.Relative) :
                Path.Combine("r6", "scripts", file.Relative);
            string target = Path.GetFullPath(Path.Combine(output, relative));
            if (!Within(target, output))
                throw new InvalidDataException("CNAME_MEMBERSHIP_PATH_INVALID");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, file.Text, new UTF8Encoding(false));
        }

        if (legacyOverlay)
        {
            var report = new {
                schemaVersion = "grsp.generic.preview.v1",
                rule = Rule, sourceStackSha256 = fingerprint,
                sourceCount = admission.InspectedFileCount,
                sourceShapeDriven = true, transformedLoops = edits,
                stagedFiles = staged.Count,
                alreadyCompleteFileCount = inspected.Sum(f => f.Complete),
                compilerValidated = false, gameplayValidated = false,
                generationAuthorized = false, deployable = false,
                outputKind = "ISOLATED_SOURCE_PREVIEW_NOT_GAME_ZIP"
            };
            File.WriteAllText(Path.Combine(output, "preview.json"),
                JsonSerializer.Serialize(report,
                    new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        return new(edits, staged.Count, inspected.Sum(f => f.Complete),
            admission.InspectedFileCount, fingerprint);
    }

    private static int CountMarkers(string text) =>
        text.Split(Marker, StringSplitOptions.None).Length - 1;

    private static IReadOnlyList<Inspected> Inspect(string game,
        SourceAdmission admission)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts))
            throw new InvalidDataException("CNAME_MEMBERSHIP_SCRIPTS_MISSING");
        var result = new List<Inspected>();
        foreach (string full in Directory.EnumerateFiles(
                     scripts, "*.reds", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CNAME_MEMBERSHIP_SYMLINK_REFUSED");
            if (!admission.IsFileIndexed(full)) continue;
            string text = File.ReadAllText(full, Encoding.UTF8);
            var tokens = SourceAdmission.ReadTokens(text);
            int complete = CountMarkers(text);
            if (complete > 0 && !HasCompletedStructuralState(tokens, text))
                throw new InvalidDataException(
                    "CNAME_MEMBERSHIP_PARTIAL_OR_DRIFTED_MARKER: " + full);
            var lines = tokens.Where(t => t.Value == "for")
                .Select(t => t.Line).Distinct().ToArray();
            var matches = new List<Match>();
            foreach (int line in lines)
            {
                var candidate = TryMatch(text, tokens, line, null);
                if (candidate is not null) matches.Add(candidate);
            }
            result.Add(new(Path.GetRelativePath(scripts, full)
                .Replace('\\', '/'), text, matches, complete));
        }
        return result;
    }

    // The source-wide scanner reuses the exact same structural admission.
    // A missing captured function permits inspection, never rewrite authority.
    internal static bool HasSourceShape(
        string text, IReadOnlyList<SourceAdmission.Token> tokens, int line) =>
        TryMatch(text, tokens, line, null, out _) is not null;

    // Diagnostic is derived from the SAME authoritative admission function.
    // This never expands rewrite eligibility; "ELIGIBLE" only proves syntax shape.
    internal static string SourceShapeReason(
        string text, IReadOnlyList<SourceAdmission.Token> tokens, int line)
    {
        _ = TryMatch(text, tokens, line, null, out string reason);
        return reason;
    }

    private static Match? TryMatch(
        string text, IReadOnlyList<SourceAdmission.Token> t,
        int line, string? function) => TryMatch(text, t, line, function, out _);

    private static Match? TryMatch(
        string text, IReadOnlyList<SourceAdmission.Token> t,
        int line, string? function, out string rejection)
    {
        rejection = "ELIGIBLE";
        var eligible = Enumerable.Range(0, t.Count)
            .Where(i => t[i].Value == "for" && t[i].Line == line).ToArray();
        if (eligible.Length != 1) { rejection = "FOR_LINE_NOT_UNIQUE"; return null; }
        int i = eligible[0];
        if (i + 22 >= t.Count) { rejection = "LOOP_TOKENS_TRUNCATED"; return null; }
        static bool Ident(string value) =>
            value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
            value.All(c => char.IsLetterOrDigit(c) || c == '_');

        string member = t[i + 1].Value;
        string field = t[i + 5].Value;
        string value = t[i + 12].Value;
        string flag = t[i + 15].Value;
        // Diagnose the FIRST syntactic deviation without admitting any
        // additional rewrites. These checks are subsets of the exact loop
        // predicate below, and share this one authoritative matcher.
        // In particular, an offset is not a "needle" until the left argument
        // and comma have been proven: otherwise we would mislabel expressions.
        if (!Ident(member))
        { rejection = "LOOP_MEMBER_NOT_IDENTIFIER"; return null; }
        if (!Ident(field))
        { rejection = "ARRAY_FIELD_NOT_IDENTIFIER"; return null; }
        if (t[i + 10].Value != member)
        {
            // Swapped *syntax* is not type or equality-purity proof.
            // Keep it rejected until the enclosing declarations and
            // ArrayContains equivalence can be established at R4/R5.
            bool bareReverse = Ident(t[i + 10].Value) &&
                t[i + 11].Value == "," &&
                t[i + 12].Value == member &&
                t[i + 13].Value == ")";
            rejection = bareReverse ? "EQUALS_OPERANDS_REVERSED_SHAPE_UNPROVEN"
                : "EQUALS_LEFT_NOT_ITERATED_VALUE";
            return null;
        }
        if (t[i + 11].Value != ",")
        {
            // First non-comma token distinguishes common source structures.
            // Never infer element type, purity or value semantics from it.
            rejection = t[i + 11].Value switch
            {
                "." => "EQUALS_LEFT_MEMBER_ACCESS",
                "[" => "EQUALS_LEFT_INDEXED_EXPRESSION",
                "(" => "EQUALS_LEFT_CALL_EXPRESSION",
                _ => "EQUALS_LEFT_NOT_SINGLE_IDENTIFIER"
            };
            return null;
        }
        if (!Ident(value))
        { rejection = "EQUALS_RIGHT_NOT_BARE_IDENTIFIER"; return null; }
        if (t[i + 13].Value != ")")
        { rejection = "EQUALS_RIGHT_NOT_SINGLE_IDENTIFIER"; return null; }
        if (t[i + 14].Value != "{")
        { rejection = "EQUALS_CONDITION_NOT_DIRECT_BLOCK"; return null; }
        if (t[i + 15].Value == "return")
        { rejection = "MATCH_BODY_RETURNS_INSTEAD_OF_FLAG"; return null; }
        if (!Ident(flag))
        { rejection = "MATCH_BODY_RESULT_NOT_IDENTIFIER"; return null; }
        if (member == flag || member == value || value == flag)
        { rejection = "LOOP_IDENTIFIERS_COLLIDE"; return null; }
        if (t[i + 16].Value == "(")
        { rejection = "MATCH_BODY_CALL_BEFORE_FLAG_ASSIGNMENT"; return null; }
        if (t[i + 16].Value != "=")
        { rejection = "MATCH_BODY_NOT_FLAG_ASSIGNMENT"; return null; }
        if (t[i + 17].Value != "true")
        { rejection = "MATCH_BODY_NOT_TRUE_ASSIGNMENT"; return null; }
        bool hasExactLoop = t[i + 2].Value == "in" &&
            t[i + 3].Value == "this" && t[i + 4].Value == "." &&
            t[i + 6].Value == "{" && t[i + 7].Value == "if" &&
            t[i + 8].Value == "Equals" && t[i + 9].Value == "(" &&
            t[i + 10].Value == member && t[i + 11].Value == "," &&
            t[i + 13].Value == ")" && t[i + 14].Value == "{" &&
            t[i + 16].Value == "=" && t[i + 17].Value == "true" &&
            t[i + 18].Value == ";" && t[i + 19].Value == "break" &&
            t[i + 20].Value == ";" && t[i + 21].Value == "}" &&
            t[i + 22].Value == "}";
        if (!hasExactLoop) { rejection = "LOOP_BODY_NOT_EXACT_MEMBERSHIP"; return null; }

        // Require the loop inside precisely the captured function body.
        int methodStart = -1;
        for (int j = 0; j < i - 3; j++)
        {
            if (t[j].Value != "func" || t[j + 2].Value != "(") continue;
            if (function is not null)
            {
                string capturedName = function.Contains("::", StringComparison.Ordinal)
                    ? function.Split("::").Last()
                    : function.Split('.').Last();
                if (!t[j + 1].Value.Equals(capturedName, StringComparison.Ordinal))
                    continue;
            }
            int open = FindOpeningMethodBrace(t, j + 2);
            if (open < 0) continue;
            int close = Closing(t, open);
            if (open < i && close > i + 22) methodStart = open;
        }
        if (methodStart < 0) { rejection = "METHOD_SCOPE_NOT_PROVEN"; return null; }

        // Field must be declared exactly once as array<CName> in the
        // enclosing source class. Scope-local bool must begin false and
        // remain unmodified before the loop; searched CName is a local
        // value, never a side-effectful expression.
        int classStart = -1;
        for (int j = 0; j < methodStart - 2; j++)
        {
            if (t[j].Value != "class" || !Ident(t[j + 1].Value)) continue;
            int open = j + 2;
            while (open < methodStart && t[open].Value != "{") open++;
            if (open < methodStart && Closing(t, open) > i)
                classStart = open;
        }
        if (classStart < 0) { rejection = "CLASS_SCOPE_NOT_PROVEN"; return null; }
        int fields = 0;
        for (int j = classStart + 1; j + 7 < methodStart; j++)
            if (Sequence(t, j, "let", field, ":", "array", "<", "CName", ">", ";"))
                fields++;
        if (fields != 1) { rejection = "ARRAY_CNAME_FIELD_NOT_PROVEN"; return null; }

        int flagDeclaration = -1, valueDeclaration = -1;
        for (int j = methodStart + 1; j + 5 < i; j++)
        {
            if (Sequence(t, j, "let", flag, ":", "Bool", "=", "false") &&
                j + 6 < i && t[j + 6].Value == ";")
            {
                if (flagDeclaration >= 0)
                { rejection = "BOOL_DECLARATION_AMBIGUOUS"; return null; }
                flagDeclaration = j + 6;
            }
            if (Sequence(t, j, "let", value, ":", "CName", "="))
            {
                if (valueDeclaration >= 0)
                { rejection = "CNAME_NEEDLE_DECLARATION_AMBIGUOUS"; return null; }
                valueDeclaration = j + 4;
            }
        }
        if (flagDeclaration < 0 || valueDeclaration < 0)
        {
            rejection = flagDeclaration < 0 ?
                "FALSE_BOOL_DECLARATION_MISSING" : "CNAME_NEEDLE_DECLARATION_MISSING";
            return null;
        }
        for (int j = flagDeclaration + 1; j < i; j++)
            if (t[j].Value == flag)
            { rejection = "BOOL_USED_BEFORE_LOOP"; return null; }

        // The loop body has to be source-only punctuation/identifiers,
        // not comments or string interpolations which might be lost.
        int start = t[i].Start, end = t[i + 22].End;
        string body = text[start..end];
        if (body.Contains("//", StringComparison.Ordinal) ||
            body.Contains("/*", StringComparison.Ordinal) ||
            body.Contains("<literal>", StringComparison.Ordinal))
        { rejection = "LOOP_TEXT_CONTAINS_COMMENT_OR_LITERAL"; return null; }
        int newline = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
        string indent = text[newline..start];
        if (!indent.All(c => c is ' ' or '\t'))
        { rejection = "LOOP_INDENTATION_NOT_PROVEN"; return null; }
        string eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string replacement = Marker + eol + indent +
            flag + " = ArrayContains(this." + field + ", " + value + ");";
        return new Match(start, end, replacement, line);
    }

    private static bool HasCompletedStructuralState(
        IReadOnlyList<SourceAdmission.Token> t, string text)
    {
        // Every generated marker must be adjacent to EXACTLY the expected
        // typed built-in assignment; do not trust a marker elsewhere in source.
        int markers = 0;
        int after = 0;
        while (true)
        {
            int at = text.IndexOf(Marker, after, StringComparison.Ordinal);
            if (at < 0) break;
            markers++;
            int markerEnd = at + Marker.Length;
            int eol = text.IndexOf('\n', markerEnd);
            if (eol < 0 || !text[markerEnd..eol].Trim().Equals("",
                    StringComparison.Ordinal))
                return false;
            int lineEnd = text.IndexOf('\n', eol + 1);
            if (lineEnd < 0) lineEnd = text.Length;
            string line = text[(eol + 1)..lineEnd].Trim();
            var assignment = SourceAdmission.ReadTokens(line);
            if (assignment.Count != 11 ||
                assignment[1].Value != "=" ||
                assignment[2].Value != "ArrayContains" ||
                assignment[3].Value != "(" ||
                assignment[4].Value != "this" ||
                assignment[5].Value != "." ||
                assignment[7].Value != "," ||
                assignment[9].Value != ")" ||
                assignment[10].Value != ";")
                return false;
            string flag = assignment[0].Value;
            string field = assignment[6].Value;
            string needle = assignment[8].Value;
            bool hasFlag = false, hasField = false, hasNeedle = false;
            for (int i = 0; i + 7 < t.Count; i++)
            {
                if (Sequence(t, i, "let", flag, ":", "Bool", "=", "false", ";"))
                    hasFlag = true;
                if (Sequence(t, i, "let", field, ":", "array", "<", "CName", ">", ";"))
                    hasField = true;
                if (Sequence(t, i, "let", needle, ":", "CName", "="))
                    hasNeedle = true;
            }
            if (!hasFlag || !hasField || !hasNeedle)
                return false;
            after = lineEnd;
        }
        return markers > 0;
    }

    private static bool Sequence(
        IReadOnlyList<SourceAdmission.Token> t, int offset,
        params string[] expected)
    {
        if (offset + expected.Length > t.Count) return false;
        for (int j = 0; j < expected.Length; j++)
            if (t[offset + j].Value != expected[j]) return false;
        return true;
    }

    private static int FindOpeningMethodBrace(
        IReadOnlyList<SourceAdmission.Token> t, int parentheses)
    {
        int parens = 0;
        for (int i = parentheses; i < t.Count; i++)
        {
            if (t[i].Value == "(") parens++;
            else if (t[i].Value == ")") parens--;
            if (parens == 0 && t[i].Value == "{") return i;
            if (parens == 0 && t[i].Value == ";") return -1;
        }
        return -1;
    }

    private static int Closing(
        IReadOnlyList<SourceAdmission.Token> t, int open)
    {
        if (t[open].Value != "{") return -1;
        int depth = 0;
        for (int i = open; i < t.Count; i++)
        {
            if (t[i].Value == "{") depth++;
            else if (t[i].Value == "}" && --depth == 0) return i;
        }
        return -1;
    }

    private static bool Within(string path, string root)
    {
        string candidate = Path.GetFullPath(path);
        string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return candidate.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            candidate.StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
