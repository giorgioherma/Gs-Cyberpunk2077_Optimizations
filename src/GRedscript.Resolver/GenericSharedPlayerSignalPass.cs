using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Generic first REDscript consumer of existing event-backed
/// G-REDruntime.StateCache. Converts only the exact typed player-main-object
/// expression with an explicit PlayerPuppet cast. The shared provider uses
/// authoritative attach state and the ORIGINAL live getter when UNKNOWN.
/// No new observer, listener, timer or alternate player-state machine.
/// </summary>
internal static class GenericSharedPlayerSignalPass
{
    internal const string Rule = "SHARED_PLAYER_SIGNAL_V1";
    private const string Marker = "/* GCO-GENERIC:" + Rule + " */";
    private const string ReplacementClass = "SharedProviders.Player";
    private static readonly Regex Identifier = new(
        @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant);
    private static readonly string[] Prefix = [
        "GameInstance", ".", "GetPlayerSystem", "(" ];
    private static readonly string[] Suffix = [
        ")", ".", "GetLocalPlayerMainGameObject", "(", ")",
        "as", "PlayerPuppet" ];
    private sealed record Candidate(int Start, int End, int Line, string Argument);
    internal sealed record Summary(int SourceFiles, int Eligible,
        int EligibleFiles, int AlreadyComplete);
    private static readonly Regex ModuleHeader = new(
        @"(?m)^[ \\t]*module[ \\t]+[A-Za-z_][A-Za-z0-9_.]*[ \\t]*$",
        RegexOptions.CultureInvariant);
    private static readonly Regex RuntimeImport = new(
        @"(?m)^[ \\t]*import[ \\t]+GRedRuntime\\.\\*[ \\t]*;?[ \\t]*$",
        RegexOptions.CultureInvariant);

    private sealed record Script(string Relative, string Text,
        List<Candidate> Candidates, int Complete);

    private static bool Inside(string child, string parent)
    {
        string p = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar);
        string c = Path.GetFullPath(child);
        return c.Equals(p, StringComparison.OrdinalIgnoreCase) ||
            c.StartsWith(p + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<Script> Inspect(string game)
    {
        var scripts = Path.Combine(game, "r6", "scripts");
        foreach (string file in Directory.EnumerateFiles(scripts,
            "*.reds", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var relative = Path.GetRelativePath(scripts, file).Replace('\\', '/');
            if (relative.StartsWith("G-RedRuntime/", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = File.ReadAllText(file, Encoding.UTF8);
            var candidates = Find(text);
            int complete = Regex.Matches(text, Regex.Escape(Marker)).Count;
            if (candidates.Count > 0 || complete > 0)
                yield return new(relative, text, candidates, complete);
        }
    }

    private static List<Candidate> Find(string text)
    {
        if (!ModuleHeader.IsMatch(text)) return new List<Candidate>();
        var tokens = SourceAdmission.ReadTokens(text);
        var rows = new List<Candidate>();
        for (int i = 0; i + Prefix.Length + 1 + Suffix.Length <= tokens.Count; i++)
        {
            bool first = true;
            for (int k = 0; k < Prefix.Length; k++)
                if (tokens[i + k].Value != Prefix[k]) { first = false; break; }
            if (!first) continue;
            int argument = i + Prefix.Length;
            string id = tokens[argument].Value;
            if (!Identifier.IsMatch(id)) continue;
            bool rest = true;
            for (int k = 0; k < Suffix.Length; k++)
                if (tokens[argument + k + 1].Value != Suffix[k])
                { rest = false; break; }
            if (!rest) continue;
            // Replacing a native/foreign static class shadow would be unsafe.
            if (Regex.IsMatch(text,
                @"\b(?:let|var|const)\s+GameInstance\b",
                RegexOptions.CultureInvariant)) continue;
            int end = argument + Suffix.Length;
            rows.Add(new(tokens[i].Start, tokens[end].End,
                tokens[i].Line, id));
            i = end;
        }
        return rows;
    }

    internal static Summary Scan(string gameRoot, string outputFile)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputFile);
        if (Inside(output, game) || File.Exists(output) ||
            Directory.Exists(output))
            throw new InvalidDataException("SHARED_PLAYER_SCAN_OUTPUT_COLLISION");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var source = Inspect(game).ToArray();
        var rows = source.SelectMany(x => x.Candidates.Select(c => new {
            path = "r6/scripts/" + x.Relative, c.Line,
            rule = Rule, state = "PRISTINE_APPLICABLE",
            originalGetterFallback = true,
            authoritativeAttachOnly = true,
            noAdditionalEngineCallbacks = true
        })).ToArray();
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_PLAYER_SOURCE_CHANGED");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.shared-player.scan.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = admission.InspectedFileCount,
            eligible = rows.Length,
            eligibleFiles = source.Count(x => x.Candidates.Count > 0),
            alreadyComplete = source.Sum(x => x.Complete),
            candidates = rows,
            generated = false, productionZipAuthorized = false,
            gameplayValidated = false
        }), new UTF8Encoding(false));
        return new(admission.InspectedFileCount, rows.Length,
            source.Count(x => x.Candidates.Count > 0),
            source.Sum(x => x.Complete));
    }

    internal static int Preview(string gameRoot, string analysisPath,
        string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        if (Inside(output, game) || File.Exists(output) ||
            (Directory.Exists(output) &&
             Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("SHARED_PLAYER_PREVIEW_OUTPUT_COLLISION");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        using (var doc = JsonDocument.Parse(File.ReadAllText(analysisPath)))
        {
            var root = doc.RootElement;
            if (root.GetProperty("schemaVersion").GetString() !=
                    "grsp.resolver.input.v1" ||
                root.GetProperty("generationAuthorized").GetBoolean() ||
                root.GetProperty("capture").GetProperty("collectionProvenance")
                    .GetString() != "GCO_INDEXED_COLLECTED_BYTES_VERIFIED" ||
                !string.Equals(root.GetProperty("source")
                        .GetProperty("sourceStackSha256").GetString(),
                    admission.SourceStackSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHARED_PLAYER_CAPTURE_OR_SOURCE_MISMATCH");
        }
        string? baseScripts = basePreviewRoot is null ? null :
            Path.Combine(Path.GetFullPath(basePreviewRoot), "r6", "scripts");
        if (baseScripts is not null &&
            (!Directory.Exists(baseScripts) || Inside(baseScripts, game) ||
             Inside(output, baseScripts) || Inside(baseScripts, output)))
            throw new InvalidDataException("SHARED_PLAYER_INVALID_BASE_STAGE");
        var staged = new List<(string Relative, string Text)>();
        int edits = 0;
        foreach (var item in Inspect(game))
        {
            if (item.Candidates.Count == 0) continue;
            string effective = item.Text;
            if (baseScripts is not null)
            {
                string prev = Path.GetFullPath(Path.Combine(baseScripts,
                    item.Relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(prev, baseScripts))
                    throw new InvalidDataException("SHARED_PLAYER_BASE_PATH_ESCAPE");
                if (File.Exists(prev)) effective = File.ReadAllText(prev, Encoding.UTF8);
            }
            var found = Find(effective);
            if (found.Count != item.Candidates.Count ||
                !found.Select(x => x.Argument).SequenceEqual(
                    item.Candidates.Select(x => x.Argument), StringComparer.Ordinal))
                throw new InvalidDataException("SHARED_PLAYER_EFFECTIVE_SOURCE_DRIFT");
            foreach (var match in found.OrderByDescending(x => x.Start))
                effective = effective[..match.Start] + Marker + " " +
                    ReplacementClass + "(" + match.Argument + ")" +
                    effective[match.End..];
            if (!RuntimeImport.IsMatch(effective))
            {
                var header = ModuleHeader.Match(effective);
                if (!header.Success)
                    throw new InvalidDataException("SHARED_PLAYER_MODULE_HEADER_MISSING");
                string nl = effective.Contains("\\r\\n", StringComparison.Ordinal)
                    ? "\\r\\n" : "\\n";
                effective = effective.Insert(header.Index + header.Length,
                    nl + "import GRedRuntime.*");
            }
            if (Find(effective).Count != 0 ||
                Regex.Matches(effective, Regex.Escape(Marker)).Count <
                item.Complete + found.Count)
                throw new InvalidDataException("SHARED_PLAYER_POSTCONDITION");
            staged.Add((item.Relative, effective));
            edits += found.Count;
        }
        if (!string.Equals(admission.SourceStackSha256,
            admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_PLAYER_SOURCE_CHANGED");
        foreach (var item in staged)
        {
            string target = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", item.Relative.Replace('/',
                    Path.DirectorySeparatorChar)));
            if (!Inside(target, output) || File.Exists(target))
                throw new InvalidDataException("SHARED_PLAYER_STAGE_COLLISION");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, item.Text, new UTF8Encoding(false));
        }
        return edits;
    }
}
