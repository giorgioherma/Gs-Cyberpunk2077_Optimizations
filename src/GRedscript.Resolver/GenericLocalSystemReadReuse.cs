using System.Text;

namespace GRedscript.Resolver;

/// <summary>
/// A source-local extension of the existing late-system-read generator.
/// It reuses the exact SourceAdmission tokens, not a new REDscript parser.
/// The only assumed invariant is the identity of a GameInstance.Get*System
/// handle for the same unmodified local game argument across consecutive
/// straight-line statements in one method/block. Live system values and
/// the original operations remain evaluated on every invocation.
/// </summary>
internal static class GenericLocalSystemReadReuse
{
    internal const string Rule = "LOCAL_SYSTEM_HANDLE_REUSE_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule + ":";

    internal sealed record Candidate(int Start, int Line, string System,
        string GameArgument, int AvoidedReads);
    internal sealed record Summary(IReadOnlyList<Candidate> Candidates,
        int AlreadyComplete);
    private sealed record Getter(int Start, int End, string Name,
        string Argument, string Expression);
    private sealed record Statement(int First, int Last, Getter Getter);
    private sealed record Group(int Start, int Line, string Name,
        string Arg, IReadOnlyList<Getter> Uses, string Declaration,
        string Variable);

    internal static Summary Examine(string source,
        IReadOnlyList<SourceAdmission.Token> tokens)
    {
        int complete = VerifyExistingMarkers(source, tokens);
        var groups = FindGroups(source, tokens);
        return new(groups.Select(g => new Candidate(g.Start, g.Line, g.Name,
            g.Arg, g.Uses.Count - 1)).ToArray(), complete);
    }

    internal static (string Source, int Edits) Rewrite(string source,
        IReadOnlyList<SourceAdmission.Token> tokens)
    {
        _ = VerifyExistingMarkers(source, tokens);
        var groups = FindGroups(source, tokens);
        if (groups.Count == 0) return (source, 0);

        string updated = source;
        foreach (var group in groups.OrderByDescending(g => g.Start))
        {
            foreach (var getter in group.Uses.OrderByDescending(g => g.Start))
                updated = updated[..getter.Start] + group.Variable +
                    updated[getter.End..];

            int startLine = source.LastIndexOf('\n', Math.Max(0, group.Start - 1)) + 1;
            string indent = source[startLine..group.Start];
            if (indent.Any(x => x != ' ' && x != '\t'))
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_INDENT_UNPROVEN");
            string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string inserted = Marker + group.Variable + ":" + group.Name +
                ":" + group.Arg + nl + indent +
                "let " + group.Variable + " = " + group.Declaration + ";" +
                nl + indent;
            updated = updated[..group.Start] + inserted + updated[group.Start..];
        }
        var after = SourceAdmission.ReadTokens(updated);
        int expected = Examine(source, tokens).AlreadyComplete + groups.Count;
        var verified = Examine(updated, after);
        if (verified.AlreadyComplete != expected ||
            verified.Candidates.Count != 0)
            throw new InvalidDataException(
                "LOCAL_SYSTEM_REUSE_GENERATED_POSTCONDITION_FAILED");
        return (updated, groups.Count);
    }

    private static List<Group> FindGroups(string source,
        IReadOnlyList<SourceAdmission.Token> t)
    {
        var results = new List<Group>();
        foreach (var (open, close) in MethodBodies(t))
        {
            int i = open + 1;
            while (i < close)
            {
                if (i == 0 || t[i - 1].Value is not ("{" or ";"))
                {
                    i++; continue;
                }
                Statement? first = ReadStatement(source, t, i, close);
                if (first is null) { i++; continue; }
                var uses = new List<Getter> { first.Getter };
                int last = first.Last;
                while (last + 1 < close)
                {
                    Statement? next = ReadStatement(source, t, last + 1, close);
                    if (next is null ||
                        next.Getter.Name != first.Getter.Name ||
                        next.Getter.Argument != first.Getter.Argument)
                        break;
                    uses.Add(next.Getter);
                    last = next.Last;
                }
                if (uses.Count >= 2)
                {
                    int start = t[first.First].Start;
                    int lineStart = source.LastIndexOf('\n',
                        Math.Max(0, start - 1)) + 1;
                    string indent = source[lineStart..start];
                    string variable = "__gcoLocalSystemL" + t[first.First].Line +
                        "_" + start;
                    // Never rename an author's local or reuse a generated name.
                    if (indent.All(c => c is ' ' or '\t') &&
                        !source.Contains(variable, StringComparison.Ordinal))
                        results.Add(new(start, t[first.First].Line,
                            first.Getter.Name, first.Getter.Argument, uses,
                            first.Getter.Expression, variable));
                }
                i = last + 1;
            }
        }
        return results;
    }

    private static Statement? ReadStatement(string text,
        IReadOnlyList<SourceAdmission.Token> t, int start, int limit)
    {
        if (start >= limit || t[start].Value is
            ("if" or "for" or "while" or "do" or "return" or "break"
                or "continue" or "switch" or "case" or "else" or "func"
                or "public" or "private" or "protected" or "native"))
            return null;
        int end = start;
        for (; end < limit; end++)
        {
            if (t[end].Value is "{" or "}") return null;
            if (t[end].Value == ";") break;
        }
        if (end >= limit) return null;
        if (t[start].Value != "let" && t[start].Value != "this" &&
            !IsIdentifier(t[start].Value))
            return null;
        int count = 0;
        Getter? found = null;
        for (int j = start; j + 6 < end; j++)
        {
            if (t[j].Value != "GameInstance" || t[j + 1].Value != "." ||
                !IsSystemGetter(t[j + 2].Value) ||
                t[j + 3].Value != "(" ||
                !IsIdentifier(t[j + 4].Value) ||
                t[j + 5].Value != ")")
                continue;
            // Require a plain stable local or method argument, never a
            // mutable field/property path or side-effectful GameInstance call.
            string arg = t[j + 4].Value;
            bool reassigned = false;
            for (int k = start; k + 1 < end; k++)
                if (t[k].Value == arg && t[k + 1].Value == "=")
                    reassigned = true;
            if (reassigned) return null;
            string exact = text[t[j].Start..t[j + 5].End];
            // Original comments and instrumentation inside the getter may
            // never be discarded by replacing an expression.
            if (exact.Contains("//", StringComparison.Ordinal) ||
                exact.Contains("/*", StringComparison.Ordinal))
                return null;
            count++;
            found = new(t[j].Start, t[j + 5].End,
                t[j + 2].Value, arg, exact);
        }
        return count == 1 && found is not null
            ? new(start, end, found) : null;
    }

    private static IEnumerable<(int Open, int Close)> MethodBodies(
        IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i + 3 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                !IsIdentifier(t[i + 1].Value)) continue;
            int argumentOpen = i + 2;
            if (t[argumentOpen].Value != "(") continue;
            int balance = 0, after = -1;
            for (int j = argumentOpen; j < t.Count; j++)
            {
                if (t[j].Value == "(") balance++;
                if (t[j].Value == ")" && --balance == 0)
                {
                    after = j + 1; break;
                }
            }
            if (after < 0) continue;
            int open = -1;
            for (int j = after; j < t.Count; j++)
            {
                if (t[j].Value == ";") break;
                if (t[j].Value == "{") { open = j; break; }
            }
            if (open < 0) continue;
            int braces = 0, close = -1;
            for (int j = open; j < t.Count; j++)
            {
                if (t[j].Value == "{") braces++;
                if (t[j].Value == "}" && --braces == 0)
                {
                    close = j; break;
                }
            }
            if (close >= 0) yield return (open, close);
            i = close >= 0 ? close : i;
        }
    }

    private static int VerifyExistingMarkers(string source,
        IReadOnlyList<SourceAdmission.Token> tokens)
    {
        int count = 0, at = 0;
        while ((at = source.IndexOf(Marker, at, StringComparison.Ordinal)) >= 0)
        {
            count++;
            int lineEnd = source.IndexOf('\n', at + Marker.Length);
            if (lineEnd < 0)
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");
            string[] annotation = source[(at + Marker.Length)..lineEnd]
                .Trim().Split(':');
            if (annotation.Length != 3)
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");
            string variable = annotation[0], getter = annotation[1],
                argument = annotation[2];
            if (!variable.StartsWith("__gcoLocalSystemL",
                    StringComparison.Ordinal) ||
                !variable.All(c => char.IsLetterOrDigit(c) || c == '_') ||
                !IsSystemGetter(getter) || !IsIdentifier(argument))
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");

            int definition = -1;
            for (int i = 0; i + 9 < tokens.Count; i++)
                if (tokens[i].Start > lineEnd &&
                    tokens[i].Value == "let" &&
                    tokens[i + 1].Value == variable)
                {
                    definition = i; break;
                }
            if (definition < 0 ||
                tokens[definition + 2].Value != "=" ||
                tokens[definition + 3].Value != "GameInstance" ||
                tokens[definition + 4].Value != "." ||
                tokens[definition + 5].Value != getter ||
                tokens[definition + 6].Value != "(" ||
                tokens[definition + 7].Value != argument ||
                tokens[definition + 8].Value != ")" ||
                tokens[definition + 9].Value != ";" ||
                !string.IsNullOrWhiteSpace(source[
                    (lineEnd + 1)..tokens[definition].Start]))
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");
            // The two consecutive statements following the declaration
            // must use the cached handle without a competing getter.
            int refs = 0, statements = 0;
            for (int j = definition + 10; j < tokens.Count && statements < 2; j++)
            {
                if (tokens[j].Value is "{" or "}")
                    throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");
                if (tokens[j].Value == variable) refs++;
                if (tokens[j].Value == ";") statements++;
            }
            if (statements != 2 || refs < 2)
                throw new InvalidDataException("LOCAL_SYSTEM_REUSE_PARTIAL_MARKER");
            at = lineEnd + 1;
        }
        return count;
    }

    private static bool IsSystemGetter(string value) =>
        value.StartsWith("Get", StringComparison.Ordinal) &&
        value.EndsWith("System", StringComparison.Ordinal) &&
        value.Length > "GetSystem".Length &&
        value.All(c => char.IsLetterOrDigit(c) || c == '_');

    private static bool IsIdentifier(string value) =>
        value.Length > 0 && (char.IsLetter(value[0]) || value[0] == '_') &&
        value.All(c => char.IsLetterOrDigit(c) || c == '_');
}
