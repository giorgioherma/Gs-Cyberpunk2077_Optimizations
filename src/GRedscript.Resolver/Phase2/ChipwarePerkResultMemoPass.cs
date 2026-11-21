using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Approved historical Pass 8 / final coverage: cache Chipware's already
/// source-recognized perk results for 100ms, without transplanting an old
/// chipPerkUnlock.reds or replacing original conditional gameplay logic.
/// Completes missing compatible work from effective post-generic sources.
/// </summary>
public static class ChipwarePerkResultMemoPass
{
    public const string Rule = "CHIPWARE_100MS_PERK_RESULT_MEMO_V1";
    public sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string[] Refusals,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Block(int Open, int Close);
    private sealed record Perk(string Name, int ReturnStart, int ReturnLength, string Level);
    private sealed record Edit(int Start, int Length, string Value);
    private sealed record Outcome(string Status, string? Text, int Edits, string? Reason);
    private const string HelperFile =
        "r6/scripts/zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds";

    private static readonly Regex PerkBranch = new(
        @"\bif\s*\(\s*Equals\s*\(\s*gamedataNewPerkType\s*\.\s*(?<name>[A-Za-z_]\w*)\s*,\s*perkType\s*\)\s*\)\s*\{",
        RegexOptions.CultureInvariant);
    private static readonly Regex ReturnLevel = new(
        @"\breturn\s+(?<level>\d+)\s*;", RegexOptions.CultureInvariant);
    private static readonly Regex VanillaReturn = new(
        @"\breturn\s+wrappedMethod\s*\(\s*perkType\s*\)\s*;",
        RegexOptions.CultureInvariant);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t, int start,
        string opening, string closing)
    {
        if (start < 0 || start >= t.Count || t[start].Value != opening) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? FindMethod(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? result = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != "IsNewPerkBought" ||
                t[i + 2].Value != "(") continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int close = Close(t, open, "{", "}");
            if (close < 0) return null;
            string prefix = source[Math.Max(0, t[i].Start - 320)..t[i].Start];
            if (!Regex.IsMatch(prefix,
                    @"@wrapMethod\s*\(\s*PlayerDevelopmentData\s*\)\s*(?:(?:public|private|protected|final|const|static)\s+)*$"))
                continue;
            if (result is not null) return null;
            result = new(t[open].Start, t[close].Start);
            i = close;
        }
        return result;
    }

    private static int ClosingBrace(string source, int open)
    {
        var t = SourceAdmission.ReadTokens(source);
        int at = -1;
        for (int i = 0; i < t.Count; i++)
            if (t[i].Start == open && t[i].Value == "{") { at = i; break; }
        int end = Close(t, at, "{", "}");
        return end >= 0 ? t[end].Start : -1;
    }

    private static string ApplyEdits(string original, List<Edit> edits)
    {
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            original = original[..edit.Start] + edit.Value +
                original[(edit.Start + edit.Length)..];
        return original;
    }

    private static Outcome RewriteOwner(string source, Block method)
    {
        string body = source[(method.Open + 1)..method.Close];
        if (Regex.IsMatch(body, @"\bTryGetPerkResult\s*\(") &&
            Regex.IsMatch(body, @"\bStorePerkResult\s*\("))
        {
            // Recognize the fully historical version (including its
            // switch-based dispatcher) without creating another cache.
            return new("COMPLETE", null, 0, null);
        }

        var guards = PerkBranch.Matches(body);
        if (guards.Count == 0)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_UNRECOGNIZED_PERK_DISPATCH");

        var perks = new List<Perk>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match guard in guards)
        {
            string name = guard.Groups["name"].Value;
            if (!seen.Add(name))
                return new("REFUSED", null, 0, "CHIPWARE_MEMO_DUPLICATE_PERK_BRANCH");
            int brace = body.IndexOf('{', guard.Index + guard.Length - 1);
            int end = ClosingBrace(body, brace);
            if (brace < 0 || end < 0)
                return new("REFUSED", null, 0, "CHIPWARE_MEMO_UNBALANCED_PERK_BRANCH");
            string branch = body[(brace + 1)..end];
            var returns = ReturnLevel.Matches(branch);
            if (returns.Count != 1 || Regex.Matches(branch, @"\breturn\b").Count != 1)
                return new("REFUSED", null, 0, "CHIPWARE_MEMO_PERK_RETURN_FLOW_CHANGED");
            Match ret = returns[0];
            perks.Add(new(name, brace + 1 + ret.Index, ret.Length,
                ret.Groups["level"].Value));
        }
        if (perks.Count < 2 ||
            ReturnLevel.Matches(body).Count != perks.Count)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_UNKNOWN_NUMERIC_RETURN");

        var game = Regex.Match(body,
            @"\blet\s+game\s*=\s*GetGameInstance\s*\(\s*\)\s*;");
        var player = Regex.Match(body,
            @"\blet\s+player\s*=\s*GetPlayer\s*\(\s*game\s*\)\s*;");
        var stats = Regex.Match(body,
            @"\blet\s+statsSystem\s*(?::\s*ref\s*<\s*StatsSystem\s*>)?\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*game\s*\)\s*;");
        if (!game.Success || !player.Success || !stats.Success ||
            !(game.Index < player.Index && player.Index < stats.Index) ||
            Regex.Matches(body, @"\bGetPlayer\s*\(\s*game\s*\)").Count != 1)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_PLAYER_SETUP_UNPROVEN");

        var vanilla = VanillaReturn.Matches(body);
        if (vanilla.Count is < 1 or > 2 ||
            vanilla[^1].Index < stats.Index)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_VANILLA_FALLBACK_UNPROVEN");

        var edits = new List<Edit>();
        bool preludePresent = Regex.IsMatch(body, @"\bTryGetPerkResult\s*\(");
        if (preludePresent &&
            (!Regex.IsMatch(body, @"\blet\s+cacheIndex\s*:\s*Int32") ||
             !Regex.IsMatch(body, @"\bGRedChipwareHotpathCache\s*\(")))
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_PARTIAL_PRELUDE_UNPROVEN");
        if (!preludePresent)
        {
            if (Regex.IsMatch(body, @"\blet\s+cacheIndex\s*:\s*Int32") ||
                Regex.IsMatch(body, @"\bStorePerkResult\s*\("))
                return new("REFUSED", null, 0, "CHIPWARE_MEMO_UNRECOGNIZED_PARTIAL_PRELUDE");
            string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            var dispatch = string.Join(nl, perks.Select((perk, i) =>
                "        case gamedataNewPerkType." + perk.Name +
                ": cacheIndex = " + i + "; break;"));
            string prefix = nl +
                "    // Historical approved 100ms Chipware result memoization." + nl +
                "    if !IsDefined(player) { return wrappedMethod(perkType); }" + nl +
                "    let cacheIndex: Int32 = -1;" + nl +
                "    switch perkType {" + nl + dispatch + nl +
                "        default: return wrappedMethod(perkType);" + nl +
                "    }" + nl +
                "    let cache: ref<GRedChipwareHotpathCache> = player.GRedChipwareHotpathCache();" + nl +
                "    let cachedResult: Int32;" + nl +
                "    if cache.TryGetPerkResult(cacheIndex, player, cachedResult) {" + nl +
                "        return cachedResult;" + nl +
                "    }" + nl;
            edits.Add(new(player.Index + player.Length, 0, prefix));
        }

        foreach (var perk in perks)
        {
            // Existing manual partial branch: do not insert duplicate stores.
            string before = body[Math.Max(0, perk.ReturnStart - 140)..perk.ReturnStart];
            if (Regex.IsMatch(before,
                @"cache\s*\.\s*StorePerkResult\s*\(\s*cacheIndex\s*,\s*player\s*,\s*" +
                Regex.Escape(perk.Level) + @"\s*\)\s*;\s*$"))
                continue;
            edits.Add(new(perk.ReturnStart, perk.ReturnLength,
                "cache.StorePerkResult(cacheIndex, player, " + perk.Level +
                "); return " + perk.Level + ";"));
        }

        Match last = vanilla[^1];
        string tail = body[(last.Index + last.Length)..];
        if (Regex.Replace(tail, @"//[^\r\n]*|/\*[\s\S]*?\*/", "").Trim().Length != 0)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_FINAL_FALLBACK_MOVED");
        edits.Add(new(last.Index, last.Length,
            "let vanillaPerkResult: Int32 = wrappedMethod(perkType); " +
            "cache.StorePerkResult(cacheIndex, player, vanillaPerkResult); " +
            "return vanillaPerkResult;"));

        string rewrite = ApplyEdits(body, edits);
        var markers = Regex.Matches(source,
            @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
            .Select(x => x.Value).Distinct(StringComparer.Ordinal);
        string full = source[..(method.Open + 1)] + rewrite + source[method.Close..];
        if (markers.Any(m => !full.Contains(m, StringComparison.Ordinal)))
            throw new InvalidDataException("CHIPWARE_MEMO_GENERIC_MARKER_LOST");
        return new("APPLIED", full, edits.Count, null);
    }

    private static Outcome ExtendHelper(string source, int perkCount)
    {
        const string className = "class GRedChipwareHotpathCache";
        int cls = source.IndexOf(className, StringComparison.Ordinal);
        if (cls < 0 || source.IndexOf(className, cls + 1, StringComparison.Ordinal) >= 0)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_HELPER_CLASS_UNPROVEN");
        int open = source.IndexOf('{', cls + className.Length);
        int close = ClosingBrace(source, open);
        if (open < 0 || close < 0)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_HELPER_BODY_UNPROVEN");
        string body = source[(open + 1)..close];
        string[] names = ["m_perkResultUntil", "m_perkResult",
            "EnsurePerkCache(", "TryGetPerkResult(", "StorePerkResult("];
        if (names.All(n => body.Contains(n, StringComparison.Ordinal)))
        {
            if (!Regex.IsMatch(body,
                    @"ArrayResize\s*\(\s*this\.m_perkResult\s*,\s*" + perkCount + @"\s*\)"))
                return new("REFUSED", null, 0, "CHIPWARE_MEMO_EXISTING_CACHE_SIZE_UNPROVEN");
            return new("COMPLETE", null, 0, null);
        }
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var pieces = new List<string>();
        if (!Regex.IsMatch(body, @"\bm_perkResultUntil\s*:\s*array<Float>"))
            pieces.Add("    private let m_perkResultUntil: array<Float>;");
        if (!Regex.IsMatch(body, @"\bm_perkResult\s*:\s*array<Int32>"))
            pieces.Add("    private let m_perkResult: array<Int32>;");
        if (!body.Contains("EnsurePerkCache(", StringComparison.Ordinal))
            pieces.Add($"""
    private func EnsurePerkCache() -> Void {{
        if ArraySize(this.m_perkResult) != {perkCount} {{
            ArrayResize(this.m_perkResult, {perkCount});
            ArrayResize(this.m_perkResultUntil, {perkCount});
        }}
    }}
""");
        if (!body.Contains("TryGetPerkResult(", StringComparison.Ordinal))
            pieces.Add($"""
    public func TryGetPerkResult(index: Int32, player: ref<PlayerPuppet>, out result: Int32) -> Bool {{
        if !IsDefined(player) || index < 0 || index >= {perkCount} {{ return false; }}
        this.EnsurePerkCache();
        let now: Float = EngineTime.ToFloat(GameInstance.GetEngineTime(player.GetGame()));
        if now < this.m_perkResultUntil[index] {{
            result = this.m_perkResult[index];
            return true;
        }}
        return false;
    }}
""");
        if (!body.Contains("StorePerkResult(", StringComparison.Ordinal))
            pieces.Add($"""
    public func StorePerkResult(index: Int32, player: ref<PlayerPuppet>, result: Int32) -> Void {{
        if !IsDefined(player) || index < 0 || index >= {perkCount} {{ return; }}
        this.EnsurePerkCache();
        this.m_perkResult[index] = result;
        this.m_perkResultUntil[index] =
            EngineTime.ToFloat(GameInstance.GetEngineTime(player.GetGame())) + 0.10;
    }}
""");
        if (pieces.Count == 0)
            return new("REFUSED", null, 0, "CHIPWARE_MEMO_UNRECOGNIZED_PARTIAL_HELPER");
        string updated = source[..close] + nl + string.Join(nl, pieces) + nl +
            source[close..];
        return new("APPLIED", updated, pieces.Count, null);
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static string Read(string path, string rel, string? overlay)
    {
        if (overlay is not null)
        {
            string candidate = Path.GetFullPath(Path.Combine(overlay,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(candidate, overlay))
                throw new InvalidDataException("CHIPWARE_MEMO_OVERLAY_ESCAPE");
            if (File.Exists(candidate)) return File.ReadAllText(candidate, Encoding.UTF8);
        }
        return File.ReadAllText(path, Encoding.UTF8);
    }

    public static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("CHIPWARE_MEMO_OUTPUT_UNSAFE");
        if (baseOverlayRoot is not null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("CHIPWARE_MEMO_INPUT_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256 ??
            throw new InvalidDataException("CHIPWARE_MEMO_MISSING_SOURCE_FINGERPRINT");
        var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refusals = new List<string>();
        int edits = 0, complete = 0;
        string owner = Path.Combine(scripts, "zzy_bChipwareExpansion");
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).ToArray();
            var methods = new List<(string Rel, string Text, Block Block)>();
            foreach (string file in files)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("CHIPWARE_MEMO_SYMLINK_SOURCE");
                string rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\', '/');
                string text = Read(file, rel, baseOverlayRoot);
                var method = FindMethod(text);
                if (method is not null) methods.Add((rel, text, method));
            }
            if (methods.Count == 1)
            {
                var target = methods[0];
                var ownerResult = RewriteOwner(target.Text, target.Block);
                if (ownerResult.Status == "REFUSED")
                    refusals.Add("chipware-perk:" + ownerResult.Reason);
                else
                {
                    if (ownerResult.Status == "APPLIED")
                    {
                        staged[target.Rel] = ownerResult.Text!;
                        edits += ownerResult.Edits;
                    }
                    else complete++;
                    var guards = PerkBranch.Matches(target.Text);
                    int count = guards.Count;
                    // Fully historical switch-based sources already own the
                    // cache; the declared helper is validated by SCC as-is.
                    if (count > 0)
                    {
                        string helperFile = Path.Combine(scripts, HelperFile["r6/scripts/".Length..]
                            .Replace('/', Path.DirectorySeparatorChar));
                        if (!File.Exists(helperFile) &&
                            (baseOverlayRoot is null ||
                             !File.Exists(Path.Combine(baseOverlayRoot,
                                HelperFile.Replace('/', Path.DirectorySeparatorChar)))))
                            refusals.Add("chipware-helper:CHIPWARE_MEMO_REQUIRED_HELPER_MISSING");
                        else
                        {
                            string helper = Read(helperFile, HelperFile, baseOverlayRoot);
                            var extended = ExtendHelper(helper, count);
                            if (extended.Status == "REFUSED")
                                refusals.Add("chipware-helper:" + extended.Reason);
                            else if (extended.Status == "APPLIED")
                            {
                                staged[HelperFile] = extended.Text!;
                                edits += extended.Edits;
                            }
                            else complete++;
                        }
                        if (refusals.Any(r => r.StartsWith("chipware-helper:",
                            StringComparison.Ordinal)))
                        {
                            // No callsites may reference an unproven helper.
                            staged.Clear();
                            edits = 0;
                        }
                    }
                }
            }
            else if (methods.Count > 1)
                refusals.Add("chipware-perk:CHIPWARE_MEMO_WRAPPER_AMBIGUOUS");
        }
        if (!string.Equals(fingerprint,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CHIPWARE_MEMO_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach (var pair in staged)
        {
            string dest = Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dest, output) ||
                !dest.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CHIPWARE_MEMO_WRONG_OUTPUT_PATH");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, pair.Value, new UTF8Encoding(false));
        }
        string status = edits > 0 ? "APPLIED" :
            complete > 0 && refusals.Count == 0 ? "COMPLETE" :
            refusals.Count > 0 ? "REFUSED" : "NOT_APPLICABLE";
        var report = new Result(Rule, status, edits, complete,
            staged.Keys.OrderBy(x => x, StringComparer.Ordinal).ToArray(),
            refusals.ToArray(), true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return report;
    }
}
