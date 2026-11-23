using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// CET-inspired REDscript callback inventory and the first callback-local
/// source-shape rewrite. Uses SourceAdmission's lexer, not Lua regexes.
/// No event registration, order, cadence, argument or consumer is changed.
/// </summary>
internal static class GenericCallbackRules
{
    private const string Rule = "CNAME_FIXED_INDEX_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule + ":";
    private sealed record Method(
        string Name, string Owner, bool IsStatic,
        int ArgsOpen, int ArgsClose, int BodyOpen, int BodyClose,
        int ReturnStart, int ReturnEnd);
    private sealed record Candidate(Method Method, string Parameter,
        string Helper, string[] Literals, string State);

    internal sealed record Summary(int SourceFiles, int Callbacks,
        int IndexCandidates, int AlreadyComplete, int Rejected);

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        var game = Path.GetFullPath(gameRoot);
        var output = Path.GetFullPath(outputFile);
        if (Within(output, game))
            throw new InvalidDataException("Callback inventory cannot write inside the installed game");
        if (File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("Callback scan refuses an existing output");
        var source = new SourceAdmission(game);
        _ = source.AuditAll();
        var scripts = Path.Combine(game, "r6", "scripts");
        var rows = new List<object>();
        var candidates = new List<object>();
        int callbacks = 0, eligible = 0, already = 0, rejected = 0;
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!source.IsFileIndexed(file)) continue;
            string text = File.ReadAllText(file, Encoding.UTF8);
            var tokens = SourceAdmission.ReadTokens(text);
            var methods = ReadMethods(tokens);
            string relative = Path.GetRelativePath(scripts, file).Replace('\\', '/');
            foreach (var method in methods)
            {
                var kind = Family(method.Name);
                if (kind is not null)
                {
                    callbacks++;
                    var body = Body(tokens, method);
                    rows.Add(new
                    {
                        path = "r6/scripts/" + relative, ownerType = method.Owner,
                        method = method.Name, family = kind,
                        sourceLine = tokens[method.BodyOpen].Line,
                        usesWrappedMethod = body.Any(t => t.Value == "wrappedMethod"),
                        readsActionName = HasSequence(body, "ListenerAction", ".", "GetName", "("),
                        consumesInput = body.Any(t => t.Value is "Consume" or "ConsumeSingleAction"),
                        sourceStatus = "CALLBACK_FAMILY_RECOGNIZED_ONLY",
                        rewriteAuthorized = false
                    });
                }
                if (!IsIndexSignature(tokens, method)) continue;
                var candidate = InspectIndex(text, tokens, methods, method);
                if (candidate is null) continue;
                if (candidate.State == "PRISTINE_APPLICABLE" &&
                    HasOtherSourceMethodOverride(scripts, method.Owner, candidate.Helper))
                    candidate = candidate with { State = "UNPROVEN_OR_DRIFTED" };
                if (candidate.State == "PRISTINE_APPLICABLE") eligible++;
                else if (candidate.State == "ALREADY_COMPLETE") already++;
                else rejected++;
                candidates.Add(new
                {
                    path = "r6/scripts/" + relative, ownerType = method.Owner,
                    method = method.Name, sourceLine = tokens[method.BodyOpen].Line,
                    rule = Rule, state = candidate.State,
                    fixedNameCount = candidate.Literals.Length,
                    rewriteAuthorized = false,
                    measuredCallbackEntryCount = (long?)null
                });
            }
        }
        AssertStable(source);
        object report = new
        {
            schemaVersion = "gco.redscript.callback.scan.v1",
            sourceStackSha256 = source.SourceStackSha256,
            scope = "ALL_CURRENT_R6_SCRIPTS",
            sourceFiles = source.InspectedFileCount,
            callbackMethods = callbacks,
            fixedCnameIndexEligible = eligible,
            fixedCnameIndexAlreadyComplete = already,
            fixedCnameIndexRejected = rejected,
            sourceWrites = false,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            callbackEntryCountsProven = false,
            callbacks = rows,
            fixedIndexCandidates = candidates
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
        return new(source.InspectedFileCount, callbacks, eligible, already, rejected);
    }

    internal static int Preview(string gameRoot, string analysisFile, string outputRoot)
    {
        var game = Path.GetFullPath(gameRoot);
        var output = Path.GetFullPath(outputRoot);
        var analysis = Path.GetFullPath(analysisFile);
        if (Within(output, game) || Within(output, analysis))
            throw new InvalidDataException("Callback preview output must be separate from installed game/evidence");
        if (Directory.Exists(output) &&
            Directory.EnumerateFileSystemEntries(output).Any())
            throw new InvalidDataException("Callback preview refuses nonempty output");
        if (File.Exists(output) || !File.Exists(analysis))
            throw new InvalidDataException("Missing analysis or output is a file");
        var source = new SourceAdmission(game);
        _ = source.AuditAll();
        using var document = JsonDocument.Parse(File.ReadAllText(analysis));
        var root = document.RootElement;
        if (root.GetProperty("schemaVersion").GetString() != "grsp.resolver.input.v1" ||
            root.GetProperty("generationAuthorized").GetBoolean() ||
            root.GetProperty("capture").GetProperty("collectionProvenance").GetString() !=
                "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
            !string.Equals(root.GetProperty("source").GetProperty("sourceStackSha256").GetString(),
                source.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");
        var scripts = Path.Combine(game, "r6", "scripts");
        var staged = new List<(string Relative, string Text)>();
        int eligible = 0;
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!source.IsFileIndexed(file)) continue;
            string text = File.ReadAllText(file, Encoding.UTF8);
            var tokens = SourceAdmission.ReadTokens(text);
            var methods = ReadMethods(tokens);
            var edits = new List<(int Start, int End, string Replacement)>();
            foreach (var method in methods)
            {
                if (!IsIndexSignature(tokens, method)) continue;
                var candidate = InspectIndex(text, tokens, methods, method);
                if (candidate?.State != "PRISTINE_APPLICABLE" ||
                    HasOtherSourceMethodOverride(scripts, method.Owner, candidate.Helper))
                    continue;
                var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
                var lines = new List<string> { "    " + Marker + candidate.Helper };
                for (int i = 0; i < candidate.Literals.Length; i++)
                    lines.Add("    if Equals(" + candidate.Literals[i] + ", " +
                        candidate.Parameter + ") { return " + i + "; }");
                lines.Add("    return -1;");
                edits.Add((tokens[method.BodyOpen].End, tokens[method.BodyClose].Start,
                    newline + string.Join(newline, lines) + newline + "  "));
            }
            if (edits.Count == 0) continue;
            string modified = text;
            foreach (var edit in edits.OrderByDescending(x => x.Start))
                modified = modified[..edit.Start] + edit.Replacement + modified[edit.End..];
            string relative = Path.GetRelativePath(scripts, file).Replace('\\', '/');
            staged.Add((relative, modified));
            eligible += edits.Count;
        }
        AssertStable(source);
        foreach (var entry in staged)
        {
            string destination = Path.GetFullPath(Path.Combine(output, "r6", "scripts",
                entry.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(destination, output) || File.Exists(destination))
                throw new InvalidDataException("Unsafe or existing callback preview destination");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, entry.Text, new UTF8Encoding(false));
        }
        // Preview is intentionally NOT accepted into a final ZIP or marked deployable.
        return eligible;
    }

    private static string? Family(string name) =>
        name switch
        {
            "OnAction" => "ONACTION",
            "OnUpdate" => "ONUPDATE",
            "OnDraw" => "ONDRAW",
            "OnTick" => "ONTICK",
            _ when name.StartsWith("On", StringComparison.Ordinal) &&
                name.Length > 2 => "ON_EVENT",
            _ => null
        };

    private static bool HasSequence(IReadOnlyList<SourceAdmission.Token> body, params string[] sequence)
    {
        for (int i = 0; i <= body.Count - sequence.Length; i++)
            if (sequence.Select((value, k) => body[i + k].Value == value).All(x => x))
                return true;
        return false;
    }

    private static List<SourceAdmission.Token> Body(
        IReadOnlyList<SourceAdmission.Token> tokens, Method m) =>
        tokens.Skip(m.BodyOpen + 1).Take(m.BodyClose - m.BodyOpen - 1)
            .Where(t => t.Value != ";").ToList();

    private static IReadOnlyList<Method> ReadMethods(IReadOnlyList<SourceAdmission.Token> t)
    {
        var owners = new List<(string Name, int Open, int Close)>();
        for (int i = 0; i < t.Count - 2; i++)
        {
            if (t[i].Value != "class") continue;
            string name = t[i + 1].Value;
            int open = i + 2;
            while (open < t.Count && open < i + 40 && t[open].Value is not ("{" or ";"))
                open++;
            if (open < t.Count && t[open].Value == "{")
                owners.Add((name, open, Matching(t, open, "{", "}")));
        }
        var methods = new List<Method>();
        for (int i = 1; i < t.Count - 3; i++)
        {
            if (t[i].Value != "func" || t[i + 2].Value != "(") continue;
            int argsClose = Matching(t, i + 2, "(", ")");
            int p = argsClose + 1;
            if (p + 2 >= t.Count || t[p].Value != "-" ||
                t[p + 1].Value != ">") continue;
            int resultStart = p + 2;
            int open = resultStart;
            while (open < t.Count && open < resultStart + 20 &&
                   t[open].Value is not ("{" or ";" or "="))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int close = Matching(t, open, "{", "}");
            var owner = owners.Where(x => x.Open < i && x.Close > close)
                .OrderByDescending(x => x.Open).FirstOrDefault();
            methods.Add(new Method(t[i + 1].Value, owner.Name ?? "",
                t[i - 1].Value == "static", i + 2, argsClose,
                open, close, resultStart, open));
        }
        return methods;
    }

    private static int Matching(
        IReadOnlyList<SourceAdmission.Token> t, int start, string open, string close)
    {
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        throw new InvalidDataException("Unbalanced REDscript method");
    }

    private static bool IsIndexSignature(
        IReadOnlyList<SourceAdmission.Token> t, Method m)
    {
        if (!m.IsStatic || string.IsNullOrWhiteSpace(m.Owner)) return false;
        var args = t.Skip(m.ArgsOpen + 1).Take(m.ArgsClose - m.ArgsOpen - 1)
            .Select(x => x.Value).ToArray();
        var result = t.Skip(m.ReturnStart).Take(m.ReturnEnd - m.ReturnStart)
            .Select(x => x.Value).ToArray();
        return args.Length == 3 && args[1] == ":" && args[2] == "CName" &&
            result.SequenceEqual(new[] { "Int32" });
    }

    private static Candidate? InspectIndex(string source,
        IReadOnlyList<SourceAdmission.Token> t, IReadOnlyList<Method> methods, Method m)
    {
        var body = Body(t, m);
        var words = body.Select(x => x.Value).ToArray();
        string param = t[m.ArgsOpen + 1].Value;
        if (words.Length >= 7 && words[0] == "let" && words[2] == "=" &&
            words[4] == "." && words[6] == "(")
        {
            string arr = words[1], owner = words[3], helper = words[5];
            if (owner != m.Owner || words.Length < 12) return null;
            string index = words[9];
            string[] shape = {
                "let",arr,"=",owner,".",helper,"(",")",
                "let",index,"=","0",
                "while",index,"<","ArraySize","(",arr,")","{",
                "if","Equals","(",arr,"[",index,"]",",",param,")","{",
                "return",index,"}",index,"+","=","1","}",
                "return","-","1"
            };
            if (words.SequenceEqual(shape) &&
                TryHelper(source, t, methods, m.Owner, helper, out var literals))
                return new(m, param, helper, literals, "PRISTINE_APPLICABLE");
        }
        // A marker is never trusted alone: only the exact canonical output
        // with the original proven helper is ALREADY_COMPLETE.
        string bodySource = source[t[m.BodyOpen].End..t[m.BodyClose].Start];
        int markerPos = bodySource.IndexOf(Marker, StringComparison.Ordinal);
        if (markerPos < 0) return null;
        string markerText = bodySource[(markerPos + Marker.Length)..].Split('\n')[0].Trim();
        if (string.IsNullOrWhiteSpace(markerText) ||
            !markerText.All(c => char.IsLetterOrDigit(c) || c == '_') ||
            !TryHelper(source, t, methods, m.Owner, markerText, out var existing))
            return new(m, param, markerText, Array.Empty<string>(), "UNPROVEN_OR_DRIFTED");
        var expected = new List<string>();
        for (int i = 0; i < existing.Length; i++)
        {
            expected.AddRange(SourceAdmission.ReadTokens(
                "if Equals(" + existing[i] + ", " + param + ") { return " + i + "; }")
                .Where(x => x.Value != ";").Select(x => x.Value));
        }
        expected.AddRange(new[] { "return", "-", "1" });
        return new(m, param, markerText, existing,
            words.SequenceEqual(expected) ? "ALREADY_COMPLETE" : "UNPROVEN_OR_DRIFTED");
    }

    private static bool TryHelper(string source,
        IReadOnlyList<SourceAdmission.Token> t, IReadOnlyList<Method> methods,
        string owner, string helperName, out string[] literals)
    {
        literals = Array.Empty<string>();
        var choices = methods.Where(x => x.Owner == owner && x.Name == helperName &&
            x.IsStatic && x.ArgsClose == x.ArgsOpen + 1).ToArray();
        if (choices.Length != 1) return false;
        var m = choices[0];
        string[] result = t.Skip(m.ReturnStart).Take(m.ReturnEnd - m.ReturnStart)
            .Select(x => x.Value).ToArray();
        if (!result.SequenceEqual(new[] { "array", "<", "CName", ">" })) return false;
        var b = Body(t, m);
        string[] shape = b.Select(x => x.Value).ToArray();
        if (shape.Length < 16 || shape[0] != "let" || shape[2] != ":" ||
            shape[3] != "array" || shape[4] != "<" || shape[5] != "CName" ||
            shape[6] != ">" || shape[7] != "=" || shape[8] != "[")
            return false;
        string arrayName = shape[1];
        var parsed = new List<string>();
        int i = 9;
        while (i + 1 < shape.Length && shape[i] == "n" &&
               shape[i + 1] == "<literal>")
        {
            string literal = source[b[i].Start..b[i + 1].End];
            if (!literal.StartsWith("n\"", StringComparison.Ordinal) ||
                !literal.EndsWith("\"", StringComparison.Ordinal) ||
                literal.Contains('\\') || literal.Contains('\n')) return false;
            parsed.Add(literal);
            i += 2;
            if (i < shape.Length && shape[i] == ",") i++;
            else break;
        }
        if (parsed.Count is < 2 or > 32 ||
            !shape.Skip(i).SequenceEqual(new[] { "]", "return", arrayName }))
            return false;
        literals = parsed.ToArray();
        return true;
    }

    // Denial-only whole-stack check: a same-class REDscript @wrapMethod or
    // @replaceMethod can change the helper's supposedly fixed return value.
    // Do not treat a local literal-return body as authoritative in that case.
    // Uses the same validated lexer to ignore comments and quoted text.
    private static bool HasOtherSourceMethodOverride(
        string scripts, string owner, string helper)
    {
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories))
        {
            IReadOnlyList<SourceAdmission.Token> tokens;
            try { tokens = SourceAdmission.ReadTokens(
                File.ReadAllText(file, Encoding.UTF8)); }
            catch (InvalidDataException) { return true; } // UNKNOWN -> refuse
            for (int i = 0; i + 4 < tokens.Count; i++)
            {
                if (tokens[i].Value != "@" ||
                    tokens[i + 1].Value is not ("wrapMethod" or "replaceMethod") ||
                    tokens[i + 2].Value != "(") continue;
                int end = i + 3;
                while (end < tokens.Count && end < i + 24 &&
                       tokens[end].Value != ")") end++;
                if (end >= tokens.Count || end >= i + 24 ||
                    !tokens.Skip(i + 3).Take(end - i - 3)
                        .Any(t => t.Value == owner))
                    continue;
                int next = end + 1;
                while (next + 1 < tokens.Count && next < end + 30 &&
                       tokens[next].Value is not ("func" or "{" or "@"))
                    next++;
                if (next + 1 < tokens.Count &&
                    tokens[next].Value == "func" &&
                    tokens[next + 1].Value == helper)
                    return true;
            }
        }
        return false;
    }

    private static void AssertStable(SourceAdmission source)
    {
        if (!string.Equals(source.SourceStackSha256,
                source.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SOURCE_DRIFT_DETECTED");
    }

    private static bool Within(string child, string parent)
    {
        parent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        child = Path.GetFullPath(child);
        return child.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            child.StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }
}
