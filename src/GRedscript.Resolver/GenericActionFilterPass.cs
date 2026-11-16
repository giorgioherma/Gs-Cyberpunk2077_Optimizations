using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Second independent cross-mod REDscript generic. Converts the exact
/// "literal-only static CName array -> local ArrayContains -> return false"
/// OnAction prefix into ordered direct CName equality comparisons.
/// No listener registration, callback cadence, downstream handler, state,
/// wrappedMethod, or event-consumer behavior is changed.
/// </summary>
internal static class GenericActionFilterPass
{
    internal const string Rule = "ACTION_LITERAL_MEMBERSHIP_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule + ":";
    private sealed record LiteralProvider(
        string Owner, string Function, string Module, string[] Names, string File);
    private sealed record Candidate(
        int Start, int End, string[] Names, string Owner, string Function,
        string NameVariable, string State, int SourceLine);

    internal sealed record Summary(int SourceFiles, int OnActionMethods,
        int Eligible, int AlreadyComplete, int Refused);

    private sealed record SourceFile(string Path, string Text,
        IReadOnlyList<SourceAdmission.Token> Tokens, string Module);

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        string game = Path.GetFullPath(gameRoot), output = Path.GetFullPath(outputFile);
        if (Within(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("Action scan refuses unsafe or existing output");
        var source = new SourceAdmission(game);
        _ = source.AuditAll();
        var files = ReadStack(game, source);
        var providers = FindProviders(files);
        var rows = new List<object>();
        int methods = 0, eligible = 0, complete = 0, refused = 0;
        foreach (var file in files)
        {
            foreach (int bodyOpen in ActionBodies(file.Tokens))
            {
                methods++;
                var candidate = Inspect(file, bodyOpen, providers, files);
                if (candidate is null) continue;
                if (candidate.State == "PRISTINE_APPLICABLE") eligible++;
                else if (candidate.State == "ALREADY_COMPLETE") complete++;
                else refused++;
                rows.Add(new
                {
                    path = FileRelative(game, file.Path),
                    sourceLine = candidate.SourceLine,
                    rule = Rule,
                    state = candidate.State,
                    fixedNameCount = candidate.Names.Length,
                    originalProvider = candidate.Owner + "." + candidate.Function,
                    measuredEntryCount = (long?)null,
                    rewriteAuthorized = false
                });
            }
        }
        AssertStable(source);
        var report = new
        {
            schemaVersion = "gco.redscript.action-filter.scan.v1",
            sourceStackSha256 = source.SourceStackSha256,
            sourceFiles = files.Count,
            onActionMethods = methods,
            eligible,
            alreadyComplete = complete,
            refused,
            generationAuthorized = false,
            compilerValidated = false,
            gameplayValidated = false,
            sourceModified = false,
            callbackEntriesProven = false,
            measuredSavingsClaimed = false,
            candidates = rows
        };
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return new(files.Count, methods, eligible, complete, refused);
    }

    internal static int Preview(string gameRoot, string analysisFile, string outputRoot)
    {
        string game = Path.GetFullPath(gameRoot),
            output = Path.GetFullPath(outputRoot),
            analysis = Path.GetFullPath(analysisFile);
        if (Within(output, game) || Within(output, analysis) || File.Exists(output) ||
            (Directory.Exists(output) &&
             Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("Action preview must be isolated and empty");
        if (!File.Exists(analysis))
            throw new InvalidDataException("Action preview requires owner analysis");
        var source = new SourceAdmission(game);
        _ = source.AuditAll();
        using (var doc = JsonDocument.Parse(File.ReadAllText(analysis)))
        {
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetString() != "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance").GetString() !=
                    "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source")
                        .GetProperty("sourceStackSha256").GetString(),
                    source.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("ACTION_FILTER_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT");
        }

        var files = ReadStack(game, source);
        var providers = FindProviders(files);
        var staged = new List<(string Relative, string Text)>();
        int matches = 0;
        foreach (var file in files)
        {
            var edits = new List<(int Start, int End, string Text)>();
            foreach (int bodyOpen in ActionBodies(file.Tokens))
            {
                var match = Inspect(file, bodyOpen, providers, files);
                if (match is null || match.State != "PRISTINE_APPLICABLE") continue;
                var newline = file.Text.Contains("\r\n", StringComparison.Ordinal)
                    ? "\r\n" : "\n";
                // The name lookup and the entire rest of the callback remain
                // byte-for-byte intact. Only the pure literal array creation
                // and its immediate membership guard are replaced.
                string indent = "        ";
                string replacement = Marker + match.Owner + "." + match.Function +
                    newline + indent + "if " +
                    string.Join(" && ", match.Names.Select(n =>
                        "!Equals(" + match.NameVariable + ", " + n + ")")) +
                    " { return false; }";
                edits.Add((match.Start, match.End, replacement));
                matches++;
            }
            if (edits.Count == 0) continue;
            string modified = file.Text;
            foreach (var edit in edits.OrderByDescending(x => x.Start))
                modified = modified[..edit.Start] + edit.Text + modified[edit.End..];
            if (modified == file.Text)
                throw new InvalidDataException("Action rewrite produced no source difference");
            staged.Add((FileRelative(game, file.Path), modified));
        }
        AssertStable(source);
        foreach (var (relative, text) in staged)
        {
            string destination = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Within(destination, output) || File.Exists(destination))
                throw new InvalidDataException("Action preview escaped isolated output");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, text, new UTF8Encoding(false));
        }
        return matches;
    }

    private static IReadOnlyList<SourceFile> ReadStack(
        string root, SourceAdmission admission)
    {
        string scripts = Path.Combine(root, "r6", "scripts");
        var files = new List<SourceFile>();
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
                     SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            if (!admission.IsFileIndexed(file)) continue;
            string content = File.ReadAllText(file, Encoding.UTF8);
            var tokens = SourceAdmission.ReadTokens(content);
            files.Add(new SourceFile(file, content, tokens, ReadModule(tokens)));
        }
        return files;
    }

    private static string ReadModule(IReadOnlyList<SourceAdmission.Token> tokens)
    {
        for (int i = 0; i < tokens.Count - 1 && i < 100; i++)
            if (tokens[i].Value == "module")
            {
                int line = tokens[i].Line;
                var parts = new List<string>();
                for (int j = i + 1; j < tokens.Count && tokens[j].Line == line; j++)
                {
                    if (!Identifier(tokens[j].Value) && tokens[j].Value != ".")
                        return "";
                    parts.Add(tokens[j].Value);
                }
                return string.Concat(parts);
            }
        return "";
    }

    private static Dictionary<string, List<LiteralProvider>> FindProviders(
        IReadOnlyList<SourceFile> files)
    {
        var providers = new Dictionary<string, List<LiteralProvider>>(
            StringComparer.Ordinal);
        foreach (var file in files)
        {
            var t = file.Tokens;
            for (int i = 0; i + 3 < t.Count; i++)
            {
                if (t[i].Value != "class" || !Identifier(t[i + 1].Value))
                    continue;
                string owner = t[i + 1].Value;
                int brace = i + 2;
                while (brace < t.Count && brace < i + 30 && t[brace].Value != "{")
                    brace++;
                if (brace >= t.Count || brace >= i + 30) continue;
                int close = Match(t, brace, "{", "}");
                for (int j = brace + 1; j + 10 < close; j++)
                {
                    if (t[j].Value != "func" || !Identifier(t[j + 1].Value) ||
                        t[j + 2].Value != "(" || t[j + 3].Value != ")") continue;
                    string fn = t[j + 1].Value;
                    var key = owner + "\0" + fn;
                    // Include unknown/dynamic same-name definitions as blockers.
                    if (!providers.TryGetValue(key, out var methods))
                        providers.Add(key, methods = new List<LiteralProvider>());
                    var values = j > 0 && t[j - 1].Value == "static"
                        ? LiteralArray(file.Text, t, j + 4, close)
                        : Array.Empty<string>();
                    methods.Add(new LiteralProvider(owner, fn, file.Module, values,
                        file.Path));
                }
                i = close;
            }
        }
        return providers;
    }

    private static string[] LiteralArray(string text,
        IReadOnlyList<SourceAdmission.Token> t, int i, int classEnd)
    {
        // Exact expression-bodied method:
        // () -> array<CName> = [n"first", n"second"]; No dynamic reads/writes.
        string[] start = { "-", ">", "array", "<", "CName", ">", "=", "[" };
        if (i + start.Length >= classEnd ||
            !start.Select((v, k) => t[i + k].Value == v).All(x => x))
            return [];
        int p = i + start.Length;
        var values = new List<string>();
        while (p + 1 < classEnd && t[p].Value == "n" &&
               t[p + 1].Value == "<literal>")
        {
            string literal = text[t[p].Start..t[p + 1].End];
            if (!literal.StartsWith("n\"", StringComparison.Ordinal) ||
                !literal.EndsWith("\"", StringComparison.Ordinal) ||
                literal.Contains('\\') || literal.Contains('\n') ||
                literal.Contains('\r')) return [];
            values.Add(literal);
            p += 2;
            if (t[p].Value == ",") p++;
            else break;
        }
        if (values.Count is < 2 or > 16 ||
            p + 1 >= classEnd || t[p].Value != "]" ||
            t[p + 1].Value != ";")
            return [];
        return values.ToArray();
    }

    private static IEnumerable<int> ActionBodies(IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i + 13 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != "OnAction" ||
                t[i + 2].Value != "(") continue;
            int end = Match(t, i + 2, "(", ")");
            var parameters = t.Skip(i + 3).Take(end - i - 3)
                .Select(x => x.Value).ToArray();
            if (!parameters.SequenceEqual(new[]
                { "action", ":", "ListenerAction", ",", "consumer", ":", "ListenerActionConsumer" }))
                continue;
            int p = end + 1;
            if (p + 3 < t.Count && t[p].Value == "-" &&
                t[p + 1].Value == ">" && t[p + 2].Value == "Bool" &&
                t[p + 3].Value == "{")
                yield return p + 3;
        }
    }

    private static Candidate? Inspect(SourceFile file, int bodyOpen,
        Dictionary<string, List<LiteralProvider>> providers,
        IReadOnlyList<SourceFile> all)
    {
        var t = file.Tokens;
        int end = Match(t, bodyOpen, "{", "}");
        int p = bodyOpen + 1;
        string[] namePrefix = { "let", "", "=", "ListenerAction", ".",
            "GetName", "(", "action", ")", ";" };
        if (p + 30 >= end || t[p].Value != "let" ||
            !Identifier(t[p + 1].Value)) return null;
        string name = t[p + 1].Value;
        namePrefix[1] = name;
        if (!MatchWords(t, p, namePrefix)) return null;
        p += namePrefix.Length;
        int start = p;
        // Distinguish fully complete generated source from broken/partial
        // generated source using exact token + source-owned helper proof.
        // A marker alone never authorizes a second rewrite.
        string methodText = file.Text[t[bodyOpen].End..t[end].Start];
        bool hasMarker = methodText.Contains(Marker, StringComparison.Ordinal);
        if (hasMarker)
        {
            var recognized = AlreadyComplete(file, p, providers, all, name);
            return recognized ?? new Candidate(0, 0, [], "", "",
                name, "UNPROVEN_OR_DRIFTED", t[bodyOpen].Line);
        }
        if (t[p].Value != "let" || !Identifier(t[p + 1].Value) ||
            t[p + 2].Value != "=" || !Identifier(t[p + 3].Value) ||
            t[p + 4].Value != "." || !Identifier(t[p + 5].Value) ||
            t[p + 6].Value != "(" || t[p + 7].Value != ")" ||
            t[p + 8].Value != ";") return null;
        string local = t[p + 1].Value, owner = t[p + 3].Value,
            method = t[p + 5].Value;
        if (local == name) return null;
        p += 9;
        string[] guard = { "if", "!", "ArrayContains", "(", local, ",",
            name, ")", "{", "return", "false", ";", "}" };
        if (!MatchWords(t, p, guard)) return null;
        int guardEnd = p + guard.Length - 1;

        string key = owner + "\0" + method;
        var options = providers.GetValueOrDefault(key);
        bool valid = options?.Count == 1 && options[0].Names.Length >= 2 &&
            IsImported(file, options[0]) &&
            !HasProviderOverride(all, owner, method);
        if (!valid)
            return new Candidate(t[start].Start, t[guardEnd].End, [], owner,
                method, name, "UNPROVEN_PROVIDER_OR_IMPORT", t[bodyOpen].Line);
        return new Candidate(t[start].Start, t[guardEnd].End,
            options![0].Names, owner, method, name, "PRISTINE_APPLICABLE",
            t[bodyOpen].Line);
    }

    private static Candidate? AlreadyComplete(SourceFile file, int p,
        Dictionary<string, List<LiteralProvider>> providers,
        IReadOnlyList<SourceFile> all, string name)
    {
        var t = file.Tokens;
        if (p < 1 || p >= t.Count) return null;
        string interlude = file.Text[t[p - 1].End..t[p].Start].Trim();
        if (!interlude.StartsWith(Marker, StringComparison.Ordinal) ||
            interlude.Contains('\n') || interlude.Contains('\r')) return null;
        string identity = interlude[Marker.Length..];
        int dot = identity.IndexOf('.');
        if (dot <= 0 || dot == identity.Length - 1 ||
            identity.LastIndexOf('.') != dot) return null;
        string owner = identity[..dot], function = identity[(dot + 1)..];
        if (!Identifier(owner) || !Identifier(function)) return null;
        var options = providers.GetValueOrDefault(owner + "\0" + function);
        if (options?.Count != 1 || options[0].Names.Length < 2 ||
            !IsImported(file, options[0]) ||
            HasProviderOverride(all, owner, function)) return null;
        string statement = "if " + string.Join(" && ",
            options[0].Names.Select(n => "!Equals(" + name + ", " + n + ")")) +
            " { return false; }";
        var expected = SourceAdmission.ReadTokens(statement);
        if (!MatchWords(t, p, expected.Select(x => x.Value).ToArray()))
            return null;
        return new Candidate(0, 0, options[0].Names, owner, function,
            name, "ALREADY_COMPLETE", t[p].Line);
    }

    private static bool IsImported(SourceFile consumer, LiteralProvider provider)
    {
        if (provider.Module.Length == 0) return false;
        if (consumer.Module == provider.Module) return true;
        // A specific class import makes the static name binding explicit.
        // No wildcard, alias, inferred cross-module lookup or mod name.
        var t = consumer.Tokens;
        for (int i = 0; i < t.Count - 4; i++)
        {
            if (t[i].Value != "import") continue;
            int line = t[i].Line, end = i + 1;
            while (end < t.Count && t[end].Line == line) end++;
            var values = t.Skip(i + 1).Take(end - i - 1)
                .Select(x => x.Value).ToArray();
            string modulePrefix = provider.Module + ".";
            string merged = string.Concat(values);
            if (merged.StartsWith(modulePrefix + "{", StringComparison.Ordinal) &&
                values.Contains(provider.Owner))
                return true;
            if (merged.Equals(modulePrefix + provider.Owner, StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    private static bool HasProviderOverride(
        IReadOnlyList<SourceFile> all, string owner, string method)
    {
        foreach (var file in all)
        {
            var t = file.Tokens;
            for (int i = 0; i + 5 < t.Count; i++)
            {
                if (t[i].Value != "@" ||
                    t[i + 1].Value is not ("wrapMethod" or "replaceMethod" or "addMethod") ||
                    t[i + 2].Value != "(" || t[i + 3].Value != owner ||
                    t[i + 4].Value != ")") continue;
                int p = i + 5;
                while (p + 1 < t.Count && p < i + 40 &&
                       t[p].Value is not ("func" or "class" or "@")) p++;
                if (p + 1 < t.Count && t[p].Value == "func" &&
                    t[p + 1].Value == method)
                    return true;
            }
        }
        return false;
    }

    private static bool MatchWords(IReadOnlyList<SourceAdmission.Token> t,
        int start, IReadOnlyList<string> words)
    {
        if (start + words.Count > t.Count) return false;
        for (int i = 0; i < words.Count; i++)
            if (t[start + i].Value != words[i]) return false;
        return true;
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
        throw new InvalidDataException("Unbalanced REDscript source block");
    }

    private static string FileRelative(string game, string file) =>
        "r6/scripts/" + Path.GetRelativePath(Path.Combine(game, "r6", "scripts"), file)
            .Replace('\\', '/');

    private static bool Identifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(x => char.IsLetterOrDigit(x) || x == '_');

    private static void AssertStable(SourceAdmission source)
    {
        if (!string.Equals(source.SourceStackSha256,
                source.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("ACTION_FILTER_SOURCE_DRIFT");
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
