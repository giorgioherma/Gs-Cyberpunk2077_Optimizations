using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// G-InputEventReuse V1: native ListenerAction name/type are immutable for
/// the lifetime of one OnAction invocation. Reuse one read per accessor
/// from its FIRST unconditional root-level occurrence onward. Never move
/// access across a preceding early return, wrapper or lifecycle callback.
/// This is an action-local slice, NOT an InputHub registration migration.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    private const string InputSnapshotRule = "ACTION_EVENT_SNAPSHOT_V1";
    private const string InputSnapshotMarker = "GCO-GENERIC:" + InputSnapshotRule;

    private sealed record InputRead(int First, int Last, string Kind, int Depth);
    private sealed record InputRewrite(int Start, int End, string Value);
    private sealed record InputFileWork(List<InputRewrite> Rewrites,
        int AvoidedReads, int Completed, List<object> Rows, string[] Witnesses);

    internal sealed record InputSnapshotSummary(int SourceFiles, int Eligible,
        int EligibleFiles, int AlreadyComplete);

    internal static InputSnapshotSummary ScanActionEventSnapshots(
        string gameRoot, string outputPath)
    {
        string game = Path.GetFullPath(gameRoot),
            output = Path.GetFullPath(outputPath);
        if (Inside(output, game) || File.Exists(output) || Directory.Exists(output))
            throw new InvalidDataException("ACTION_SNAPSHOT_SCAN_OUTPUT_NOT_ISOLATED");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (files, methods) = ReadAllMethodSources(game, admission);
        bool nativeIntact = NativeActionAccessorsIntact(files);
        var rows = new List<object>();
        int eligible = 0, touched = 0, complete = 0;
        foreach (var file in files)
        {
            var work = InputSnapshotWork(file, methods, nativeIntact);
            eligible += work.AvoidedReads;
            complete += work.Completed;
            if (work.AvoidedReads > 0) touched++;
            rows.AddRange(work.Rows);
        }
        AssertConstantSourceStable(admission);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.action-snapshot.scan.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = admission.InspectedFileCount,
            rule = InputSnapshotRule,
            eligible = eligible,
            eligibleFiles = touched,
            alreadyComplete = complete,
            candidates = rows,
            sourceWrites = false,
            productionZipAuthorized = false,
            gameplayValidated = false,
            noModAllowlist = true,
            nativeAccessorPurityAssumption = "ListenerAction event fields immutable within callback",
            rawInputConsumerAndListenerUnchanged = true
        }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, eligible, touched, complete);
    }

    internal static int PreviewActionEventSnapshots(string gameRoot,
        string analysisPath, string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            analysis = Path.GetFullPath(analysisPath),
            output = Path.GetFullPath(outputRoot);
        if (Inside(output, game) || File.Exists(output) ||
            (Directory.Exists(output) &&
             Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("ACTION_SNAPSHOT_PREVIEW_OUTPUT_NOT_ISOLATED");
        if (!File.Exists(analysis))
            throw new InvalidDataException("ACTION_SNAPSHOT_SEALED_CAPTURE_MISSING");
        string? baseScripts = null;
        if (basePreviewRoot is not null)
        {
            string prior = Path.GetFullPath(basePreviewRoot);
            if (!Directory.Exists(prior) || Inside(prior, game) ||
                Inside(prior, output) || Inside(output, prior))
                throw new InvalidDataException("ACTION_SNAPSHOT_BASE_NOT_ISOLATED");
            baseScripts = Path.Combine(prior, "r6", "scripts");
            if (!Directory.Exists(baseScripts))
                throw new InvalidDataException("ACTION_SNAPSHOT_BASE_SCRIPTS_MISSING");
        }
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var document = JsonDocument.Parse(File.ReadAllText(analysis)))
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
                    "ACTION_SNAPSHOT_CAPTURE_OR_SOURCE_DRIFT");
        }
        var (files, methods) = ReadAllMethodSources(game, admission);
        bool nativeIntact = NativeActionAccessorsIntact(files);
        var staged = new List<(string Relative, string Text)>();
        int avoided = 0;
        foreach (var file in files)
        {
            var original = InputSnapshotWork(file, methods, nativeIntact);
            if (original.AvoidedReads == 0) continue;
            FileSource effective = file;
            if (baseScripts is not null)
            {
                string path = Path.GetFullPath(Path.Combine(baseScripts,
                    file.Relative["r6/scripts/".Length..]
                        .Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(path, baseScripts))
                    throw new InvalidDataException("ACTION_SNAPSHOT_BASE_PATH_ESCAPE");
                if (File.Exists(path))
                {
                    var text = File.ReadAllText(path, Encoding.UTF8);
                    effective = new FileSource(path, file.Relative, file.Module,
                        text, SourceAdmission.ReadTokens(text));
                    var check = InputSnapshotWork(effective, methods, nativeIntact);
                    if (!original.Witnesses.SequenceEqual(check.Witnesses,
                            StringComparer.Ordinal))
                        throw new InvalidDataException(
                            "ACTION_SNAPSHOT_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT");
                }
            }
            var work = InputSnapshotWork(effective, methods, nativeIntact);
            string transformed = effective.Text;
            foreach (var edit in work.Rewrites.OrderByDescending(x => x.Start)
                         .ThenByDescending(x => x.End))
                transformed = transformed[..edit.Start] + edit.Value +
                    transformed[edit.End..];
            var verified = new FileSource(effective.Path, file.Relative,
                file.Module, transformed,
                SourceAdmission.ReadTokens(transformed));
            var post = InputSnapshotWork(verified, methods, nativeIntact);
            if (post.AvoidedReads != 0 ||
                post.Completed != work.Completed + work.Rows.Count)
                throw new InvalidDataException("ACTION_SNAPSHOT_POSTCONDITION_FAILED");
            staged.Add((file.Relative, transformed));
            avoided += work.AvoidedReads;
        }
        AssertConstantSourceStable(admission);
        foreach (var item in staged)
        {
            string destination = Path.GetFullPath(Path.Combine(output,
                item.Relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(destination, output) || File.Exists(destination))
                throw new InvalidDataException("ACTION_SNAPSHOT_STAGE_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllText(destination, item.Text, new UTF8Encoding(false));
        }
        return avoided;
    }

    private static InputFileWork InputSnapshotWork(FileSource file,
        IReadOnlyList<MethodSource> originalMethods, bool nativeIntact)
    {
        var edits = new List<InputRewrite>();
        var rows = new List<object>();
        var witnesses = new List<string>();
        int saved = 0, complete = 0;
        if (!nativeIntact) return new(edits, saved, complete, rows, []);
        var methods = originalMethods.Where(x =>
            ReferenceEquals(x.File, file)).ToArray();
        if (methods.Length == 0 || methods[0].File.Text != file.Text)
            methods = ParseMethods(file).ToArray();
        var t = file.Tokens;
        foreach (var m in methods)
        {
            if (m.Name != "OnAction" ||
                !InputActionSignature(t, m)) continue;
            string body = file.Text[t[m.Open].End..t[m.Close].Start];
            // Changing the ListenerAction argument itself would invalidate
            // reuse. The native packet is immutable; the author may not
            // rebind the argument or explicitly mutate it through a method.
            if (ActionArgumentMutated(t, m)) continue;
            foreach (string kind in new[] { "GetName", "GetType" })
            {
                string label = kind == "GetName" ? "Name" : "Type";
                int ordinal = methods.Count(x => x.Name == "OnAction" &&
                    x.First <= m.First);
                string variable = "__gcoAction" + label + "N" + ordinal;
                var reads = ReadInputAccessors(t, m, kind);
                string tag = InputSnapshotMarker + " " + kind + "=" + variable;
                bool tagged = body.Contains(InputSnapshotMarker + " " + kind + "=",
                    StringComparison.Ordinal);
                if (tagged)
                {
                    // Lexical marker alone is insufficient. Verify the exact
                    // introduced declaration and ONE native call.
                    string expected = "// " + tag +
                        (file.Text.Contains("\r\n", StringComparison.Ordinal) ?
                            "\r\n" : "\n") +
                        Regex.Match(body, @"(?m)^([ \t]*)// " +
                            Regex.Escape(tag)).Groups[1].Value +
                        "let " + variable + " = ListenerAction." + kind +
                        "(action);";
                    if (reads.Count != 1 || !body.Contains(expected,
                            StringComparison.Ordinal) ||
                        t.Skip(m.Open + 1).Take(m.Close - m.Open - 1)
                            .Count(x => x.Value == variable) < 2)
                        throw new InvalidDataException(
                            "ACTION_SNAPSHOT_PARTIAL_OR_DRIFTED");
                    complete++;
                    continue;
                }
                if (reads.Count < 2) continue;
                int first = reads[0];
                int insertToken = InputRootStatementStart(t, m, first);
                if (insertToken < 0) continue;
                if (t.Skip(m.Open + 1).Take(m.Close - m.Open - 1)
                    .Any(x => x.Value == variable)) continue;
                int lineStart = file.Text.LastIndexOf('\n',
                    Math.Max(0, t[insertToken].Start - 1)) + 1;
                string indent = file.Text[lineStart..t[insertToken].Start];
                if (indent.Any(c => c is not (' ' or '\t')) ||
                    t[insertToken].Value is ("else" or "catch" or "finally"))
                    continue;
                string newline = file.Text.Contains("\r\n",
                    StringComparison.Ordinal) ? "\r\n" : "\n";
                string declaration = indent + "// " + tag + newline +
                    indent + "let " + variable + " = ListenerAction." +
                    kind + "(action);" + newline;
                edits.Add(new InputRewrite(lineStart, lineStart, declaration));
                foreach (int index in reads)
                    edits.Add(new InputRewrite(t[index].Start,
                        t[index + 5].End, variable));
                int prevented = reads.Count - 1;
                saved += prevented;
                rows.Add(new {
                    path = file.Relative,
                    line = m.Line,
                    method = m.Name,
                    rule = InputSnapshotRule,
                    accessor = kind,
                    originalGetterCallSites = reads.Count,
                    avoidedCallsUpperBoundPerCallback = prevented,
                    state = "PRISTINE_APPLICABLE",
                    listenerRegistrationUnchanged = true,
                    actionConsumptionUnchanged = true
                });
                witnesses.Add(m.Owner + "\0" + m.Name + "\0" + kind +
                    "\0" + reads.Count);
            }
        }
        return new(edits, saved, complete, rows,
            witnesses.OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    private static bool InputActionSignature(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource m)
    {
        string[] expected = { "(", "action", ":", "ListenerAction", ",",
            "consumer", ":", "ListenerActionConsumer", ")" };
        return t.Skip(m.SignatureOpen).Take(m.SignatureClose -
            m.SignatureOpen + 1).Select(x => x.Value).SequenceEqual(expected);
    }

    private static List<int> ReadInputAccessors(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource m, string kind)
    {
        var sites = new List<int>();
        for (int i = m.Open + 1; i + 5 < m.Close; i++)
        {
            if (t[i].Value == "ListenerAction" &&
                t[i + 1].Value == "." && t[i + 2].Value == kind &&
                t[i + 3].Value == "(" && t[i + 4].Value == "action" &&
                t[i + 5].Value == ")" &&
                (i == 0 || t[i - 1].Value != "."))
            {
                sites.Add(i);
                i += 5;
            }
        }
        return sites;
    }

    // First native read must occur in a root-level statement, not inside a
    // branch that may be skipped. Insert immediately before that statement,
    // after any earlier guards/returns; never hoist to callback entry.
    private static int InputRootStatementStart(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource m, int read)
    {
        int depth = 1, start = m.Open + 1;
        for (int i = m.Open + 1; i < read; i++)
        {
            if (t[i].Value == "{") depth++;
            else if (t[i].Value == "}")
            {
                depth--;
                if (depth == 1) start = i + 1;
            }
            else if (depth == 1 && t[i].Value == ";")
                start = i + 1;
        }
        return depth == 1 && start <= read ? start : -1;
    }

    private static bool ActionArgumentMutated(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource m)
    {
        for (int i = m.Open + 1; i + 1 < m.Close; i++)
            if (t[i].Value == "action" && t[i + 1].Value is
                ("=" or "+=" or "-=" or "++" or "--" or "."))
                return true;
        return false;
    }

    private static bool NativeActionAccessorsIntact(
        IReadOnlyList<FileSource> files)
    {
        // A script-level override of the native ListenerAction symbol would
        // invalidate the immutable-event assumption for every owner.
        foreach (var f in files)
        {
            var t = f.Tokens;
            for (int i = 0; i + 4 < t.Count; i++)
                if ((t[i].Value == "class" &&
                     t[i + 1].Value == "ListenerAction") ||
                    (t[i].Value == "@" &&
                     t[i + 1].Value is ("wrapMethod" or "replaceMethod" or "addMethod") &&
                     t[i + 2].Value == "(" &&
                     t[i + 3].Value == "ListenerAction" &&
                     t[i + 4].Value == ")"))
                    return false;
        }
        return true;
    }
}
