using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.0 BrowserExtension: reuse the journal manager and the two
/// immutable journal-entry handles, while still evaluating GetEntryState on
/// every call. Does not cache quest state or replace unrelated browser logic.
/// Each entry reader can be completed independently from current post-generic,
/// partially optimized, or markerless historical source.
/// </summary>
internal static class BrowserExtensionJournalCachePass
{
    internal const string Rule = "BROWSER_EXTENSION_JOURNAL_HANDLES_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private static readonly (string Method, string Field)[] Readers =
    [
        ("IsCorpoStartActive", "m_corpoStartQuest"),
        ("IsEP1EpilogueActive", "m_ep1EpilogueQuest")
    ];

    private static int Match(IReadOnlyList<SourceAdmission.Token> tokens,
        int first, string opening, string closing)
    {
        if (first < 0 || first >= tokens.Count || tokens[first].Value != opening)
            return -1;
        int depth = 0;
        for (int i = first; i < tokens.Count; i++)
        {
            if (tokens[i].Value == opening) depth++;
            else if (tokens[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? FindClass(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" ||
                t[i + 1].Value != "BrowserExtensionSystem") continue;
            int open = i + 2;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int close = Match(t, open, "{", "}");
            if (close < 0 || found is not null) return null;
            found = new(t[open].Start, t[close].Start);
        }
        return found;
    }

    private static Block? FindMethod(string source, Block cls, string name)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Start <= cls.Open || t[i].Start >= cls.Close ||
                t[i].Value != "func" || t[i + 1].Value != name ||
                t[i + 2].Value != "(") continue;
            int endArgs = Match(t, i + 2, "(", ")");
            if (endArgs < 0) return null;
            int open = endArgs + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int close = Match(t, open, "{", "}");
            if (close < 0 || t[close].Start > cls.Close || found is not null)
                return null;
            found = new(t[open].Start, t[close].Start);
            i = close;
        }
        return found;
    }

    private static readonly Regex LegacyJournal = new(
        @"\blet\s+(?<name>[A-Za-z_]\w*)\s*:\s*wref\s*<\s*JournalManager\s*>\s*=\s*GameInstance\s*\.\s*GetJournalManager\s*\(\s*this\s*\.\s*GetGameInstance\s*\(\s*\)\s*\)\s*;",
        RegexOptions.CultureInvariant);
    private static readonly Regex CachedJournal = new(
        @"\blet\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*wref\s*<\s*JournalManager\s*>)?\s*=\s*this\s*\.\s*GetCachedJournal\s*\(\s*\)\s*;",
        RegexOptions.CultureInvariant);

    private static string Shape(string s) => string.Join("|",
        SourceAdmission.ReadTokens(s).Select(t => t.Value));

    private static (string Status, string? Reason, string? Changed, int Count)
        Rewrite(string source)
    {
        var cls = FindClass(source);
        if (cls is null)
            return ("REFUSED", "BEX_JOURNAL_CLASS_UNPROVEN", null, 0);
        string body = source[(cls.Open + 1)..cls.Close];
        var methodEdits = new List<Edit>();
        var missingFields = new List<string>();
        int already = 0, changed = 0;

        var helper = FindMethod(source, cls, "GetCachedJournal");
        if (helper is not null)
        {
            string hb = source[(helper.Open + 1)..helper.Close];
            if (!Regex.IsMatch(hb,
                    @"\bGameInstance\s*\.\s*GetJournalManager\s*\(\s*this\s*\.\s*GetGameInstance\s*\(\s*\)\s*\)") ||
                !Regex.IsMatch(hb, @"\breturn\s+this\.m_journal\s*;"))
                return ("REFUSED", "BEX_JOURNAL_HELPER_INCOMPATIBLE", null, 0);
        }

        var fields = new[]
        {
            ("m_journal", "JournalManager"),
            ("m_corpoStartQuest", "JournalQuest"),
            ("m_ep1EpilogueQuest", "JournalQuest")
        };
        foreach (var (name, type) in fields)
        {
            var matches = Regex.Matches(body,
                @"\b(?:private|public|protected)?\s*let\s+" + name +
                @"\s*:\s*(?<ref>wref|ref)\s*<\s*(?<type>[A-Za-z_]\w*)\s*>\s*;");
            if (matches.Count > 1 ||
                (matches.Count == 1 &&
                 (matches[0].Groups["ref"].Value != "wref" ||
                  matches[0].Groups["type"].Value != type)))
                return ("REFUSED", "BEX_JOURNAL_FIELD_INCOMPATIBLE:" + name, null, 0);
            if (matches.Count == 0)
            {
                if (Regex.IsMatch(body, @"\blet\s+" + name + @"\s*:"))
                    return ("REFUSED", "BEX_JOURNAL_FIELD_COLLISION:" + name, null, 0);
                missingFields.Add("  private let " + name + ": wref<" + type + ">;");
            }
        }

        foreach (var (name, field) in Readers)
        {
            var method = FindMethod(source, cls, name);
            if (method is null)
                return ("REFUSED", "BEX_JOURNAL_READER_MISSING:" + name, null, 0);
            string original = source[(method.Open + 1)..method.Close];
            string current = original;
            var cached = CachedJournal.Matches(current);
            var legacy = LegacyJournal.Matches(current);
            if (cached.Count + legacy.Count != 1)
                return ("REFUSED", "BEX_JOURNAL_MANAGER_SITE_UNPROVEN:" + name, null, 0);
            string journalName = cached.Count == 1 ?
                cached[0].Groups["name"].Value : legacy[0].Groups["name"].Value;
            string journalPattern = Regex.Escape(journalName);
            string questQuery = @"\b" + journalPattern +
                @"\s*\.\s*GetEntryByString\s*\(\s*(?<path>s\s*""[^""]+"")\s*,\s*""gameJournalQuest""\s*\)\s+as\s+JournalQuest";
            var legacyQuest = Regex.Matches(current,
                @"\blet\s+(?<local>[A-Za-z_]\w*)\s*:\s*wref\s*<\s*JournalQuest\s*>\s*=\s*" +
                questQuery + @"\s*;");
            var cachedQuest = Regex.Matches(current,
                @"\bthis\." + field + @"\s*=\s*" + questQuery + @"\s*;");
            if (legacyQuest.Count + cachedQuest.Count != 1)
                return ("REFUSED", "BEX_JOURNAL_ENTRY_SITE_UNPROVEN:" + name, null, 0);
            if (legacyQuest.Count == 1 &&
                SourceAdmission.ReadTokens(current).Count(t =>
                    t.Value == legacyQuest[0].Groups["local"].Value) != 2)
                return ("REFUSED", "BEX_JOURNAL_ENTRY_HAS_OTHER_READERS:" + name, null, 0);
            if (cachedQuest.Count == 1 &&
                !Regex.IsMatch(current,
                    @"\bif\s+!\s*IsDefined\s*\(\s*this\." + field + @"\s*\)\s*\{"))
                return ("REFUSED", "BEX_JOURNAL_ENTRY_CACHE_GUARD_MISSING:" + name, null, 0);

            string arg = legacyQuest.Count == 1 ?
                legacyQuest[0].Groups["local"].Value : "this." + field;
            var stateRead = Regex.Matches(current,
                @"\b" + journalPattern + @"\s*\.\s*GetEntryState\s*\(\s*" +
                Regex.Escape(arg) + @"\s*\)");
            if (stateRead.Count != 1 ||
                !Regex.IsMatch(current,
                    @"\breturn\s+Equals\s*\(\s*[A-Za-z_]\w*\s*,\s*gameJournalEntryState\.Active\s*\)\s*;"))
                return ("REFUSED", "BEX_JOURNAL_STATE_QUERY_UNPROVEN:" + name, null, 0);

            bool journalGuard = Regex.IsMatch(current,
                @"\bif\s+!\s*IsDefined\s*\(\s*" + journalPattern +
                @"\s*\)\s*\{\s*return\s+false\s*;\s*\}");
            bool questGuard = Regex.IsMatch(current,
                @"\bif\s+!\s*IsDefined\s*\(\s*this\." + field +
                @"\s*\)\s*\{\s*return\s+false\s*;\s*\}");
            if (legacy.Count == 1)
            {
                var m = legacy[0];
                current = current.Remove(m.Index, m.Length).Insert(m.Index,
                    "let " + journalName + " = this.GetCachedJournal();");
            }
            if (!journalGuard)
            {
                var m = CachedJournal.Match(current);
                if (!m.Success)
                    return ("REFUSED", "BEX_JOURNAL_MANAGER_GUARD_LOCATION_UNPROVEN:" + name, null, 0);
                current = current.Insert(m.Index + m.Length,
                    "\n    if !IsDefined(" + journalName + ") { return false; }");
            }
            if (legacyQuest.Count == 1)
            {
                var m = Regex.Match(current,
                    @"\blet\s+(?<local>[A-Za-z_]\w*)\s*:\s*wref\s*<\s*JournalQuest\s*>\s*=\s*" + questQuery + @"\s*;");
                if (!m.Success)
                    return ("REFUSED", "BEX_JOURNAL_ENTRY_DRIFT:" + name, null, 0);
                string query = current[m.Groups["path"].Index..(m.Groups["path"].Index + m.Groups["path"].Length)];
                string replacement = "if !IsDefined(this." + field + ") {\n" +
                    "      this." + field + " = " + journalName +
                    ".GetEntryByString(" + query + ", \"gameJournalQuest\") as JournalQuest;\n" +
                    "    }";
                current = current.Remove(m.Index, m.Length).Insert(m.Index, replacement);
                current = Regex.Replace(current,
                    @"\b" + journalPattern + @"\s*\.\s*GetEntryState\s*\(\s*" +
                    Regex.Escape(arg) + @"\s*\)",
                    journalName + ".GetEntryState(this." + field + ")");
            }
            if (!questGuard)
            {
                var m = Regex.Match(current,
                    @"\bthis\." + field + @"\s*=\s*" + questQuery + @"\s*;");
                if (!m.Success)
                    return ("REFUSED", "BEX_JOURNAL_ENTRY_GUARD_LOCATION_UNPROVEN:" + name, null, 0);
                // Guard belongs after the original source-owned lookup block.
                int after = current.IndexOf('}', m.Index + m.Length);
                if (after < 0) return ("REFUSED", "BEX_JOURNAL_ENTRY_BLOCK_UNBALANCED:" + name, null, 0);
                current = current.Insert(after + 1,
                    "\n    if !IsDefined(this." + field + ") { return false; }");
            }
            if (!Regex.IsMatch(current,
                    @"\b" + journalPattern + @"\s*\.\s*GetEntryState\s*\(\s*this\." +
                    field + @"\s*\)") ||
                !CachedJournal.IsMatch(current) ||
                !Regex.IsMatch(current,
                    @"\bthis\." + field + @"\s*=\s*" + questQuery + @"\s*;"))
                throw new InvalidDataException("BEX_JOURNAL_POSTCONDITION:" + name);
            if (current != original)
            {
                methodEdits.Add(new(method.Open + 1, method.Close, current));
                changed++;
            }
            else already++;
        }

        if (changed == 0 && missingFields.Count == 0 && helper is not null)
            return ("COMPLETE", null, null, already);

        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertion = new StringBuilder();
        foreach (string field in missingFields)
            insertion.Append(nl).Append(field);
        if (helper is null)
        {
            insertion.Append(nl).Append(nl).Append("  private func GetCachedJournal() -> wref<JournalManager> {")
                .Append(nl).Append("    if !IsDefined(this.m_journal) {")
                .Append(nl).Append("      this.m_journal = GameInstance.GetJournalManager(this.GetGameInstance());")
                .Append(nl).Append("    }")
                .Append(nl).Append("    return this.m_journal;")
                .Append(nl).Append("  }");
        }
        if (insertion.Length > 0)
            methodEdits.Add(new(cls.Open + 1, cls.Open + 1,
                insertion.ToString() + nl));
        string result = source;
        foreach (Edit edit in methodEdits.OrderByDescending(e => e.Start))
            result = result[..edit.Start] + edit.Text + result[edit.End..];
        if (FindClass(result) is null ||
            Readers.Any(reader =>
                FindMethod(result, FindClass(result)!, reader.Method) is null))
            throw new InvalidDataException("BEX_JOURNAL_POSTCONDITION_PARSE");
        return ("APPLIED", null, result, changed);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            output = Path.GetFullPath(outputRoot),
            scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("BEX_JOURNAL_OUTPUT_UNSAFE");
        if (baseOverlayRoot is not null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("BEX_JOURNAL_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256 ??
            throw new InvalidDataException("BEX_JOURNAL_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts, "BrowserExtension");
        string status = "NOT_APPLICABLE";
        string? reason = null, relative = null, rewritten = null;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).Where(p =>
                File.ReadAllText(p, Encoding.UTF8).Contains(
                    "class BrowserExtensionSystem", StringComparison.Ordinal)).ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = files.Length == 0 ? "BEX_JOURNAL_SYSTEM_MISSING" :
                    "BEX_JOURNAL_SYSTEM_AMBIGUOUS";
            }
            else
            {
                if ((File.GetAttributes(files[0]) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("BEX_JOURNAL_SOURCE_LINK_UNSAFE");
                relative = "r6/scripts/" +
                    Path.GetRelativePath(scripts, files[0]).Replace('\\', '/');
                string source = File.ReadAllText(files[0], Encoding.UTF8);
                if (baseOverlayRoot is not null)
                {
                    string over = Path.GetFullPath(Path.Combine(
                        baseOverlayRoot,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(over, baseOverlayRoot))
                        throw new InvalidDataException("BEX_JOURNAL_OVERLAY_ESCAPE");
                    if (File.Exists(over))
                        source = File.ReadAllText(over, Encoding.UTF8);
                }
                var decision = Rewrite(source);
                status = decision.Status;
                reason = decision.Reason;
                rewritten = decision.Changed;
            }
        }
        if (!string.Equals(fingerprint,
            admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BEX_JOURNAL_SOURCE_CHANGED_DURING_GENERATION");
        Directory.CreateDirectory(output);
        if (rewritten is not null && relative is not null)
        {
            string full = Path.GetFullPath(Path.Combine(
                output, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(full, output) ||
                !full.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("BEX_JOURNAL_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, rewritten, new UTF8Encoding(false));
        }
        var result = new Result(Rule, status, status == "APPLIED" ? 1 : 0,
            status == "COMPLETE" ? 1 : 0, relative, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
