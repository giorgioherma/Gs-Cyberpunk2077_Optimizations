using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 8, independent chipware throughput operations. The generator
/// works against the actual post-generic effective source; it never transplants
/// historical files and never caches mutable player stats across callbacks.
/// Integration into the canonical stage order is reserved for the handoff owner.
/// </summary>
public static class Pass8ThroughputPass
{
    public const string Rule = "CHIPWARE_PASS8_SOURCE_LOCAL_THROUGHPUT_V1";
    public sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string[] Refusals,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Method(int Open, int Close);
    private sealed record Outcome(string Status, string? Source, string? Reason);
    private sealed record Operation(string Name, string Owner, string Decorator,
        string MethodName, Func<string, Method, Outcome> Apply);

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int at, string opening, string closing)
    {
        if (at < 0 || at >= t.Count || t[at].Value != opening) return -1;
        int depth = 0;
        for (int i = at; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static IReadOnlyList<Method> FindMethods(string source,
        string decorator, string methodName)
    {
        var t = SourceAdmission.ReadTokens(source);
        var found = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != methodName ||
                t[i + 2].Value != "(") continue;
            int endArgs = Match(t, i + 2, "(", ")");
            if (endArgs < 0) continue;
            int open = endArgs + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Match(t, open, "{", "}");
            if (end < 0) continue;
            // Inspect the decoration immediately preceding this function, not
            // another method or a historical file path/line number.
            int before = Math.Max(0, t[i].Start - 280);
            string prefix = source[before..t[i].Start];
            if (!Regex.IsMatch(prefix,
                @"@wrapMethod\s*\(\s*" + Regex.Escape(decorator) +
                @"\s*\)\s*(?:(?:public|protected|private|final|static|const|cb)\s+)*$"))
                continue;
            found.Add(new(t[open].Start, t[end].Start));
            i = end;
        }
        return found;
    }

    private static readonly Regex PerkBranch = new(
        @"\bif\s*\(?\s*Equals\s*\(\s*gamedataNewPerkType\s*\.\s*(?<perk>[A-Za-z_]\w*)\s*,\s*perkType\s*\)\s*\)?\s*\{",
        RegexOptions.CultureInvariant);
    private static readonly Regex FastGuard = new(
        @"\bif\s*!\s*\((?<expr>[\s\S]*?)\)\s*\{\s*return\s+wrappedMethod\s*\(\s*perkType\s*\)\s*;\s*\}",
        RegexOptions.CultureInvariant);
    private static readonly Regex GuardMember = new(
        @"Equals\s*\(\s*gamedataNewPerkType\s*\.\s*(?<perk>[A-Za-z_]\w*)\s*,\s*perkType\s*\)",
        RegexOptions.CultureInvariant);

    private static Outcome PerkAdmission(string source, Method method)
    {
        string body = source[(method.Open + 1)..method.Close];
        var branches = PerkBranch.Matches(body);
        if (branches.Count == 0)
        {
            // Historical/manual switch dispatch is already independently
            // classified before mutable stats are read.
            if (Regex.IsMatch(body, @"\bswitch\s+perkType\b") &&
                Regex.IsMatch(body, @"\bdefault\s*:\s*return\s+wrappedMethod\s*\(\s*perkType\s*\)"))
                return new("COMPLETE", null, null);
            return new("REFUSED", null, "PASS8_PERK_CLASSIFICATION_MISSING");
        }
        var names = branches.Select(m => m.Groups["perk"].Value)
            .Distinct(StringComparer.Ordinal).ToArray();
        if (names.Length < 2 || !Regex.IsMatch(body,
                @"\breturn\s+wrappedMethod\s*\(\s*perkType\s*\)\s*;\s*$"))
            return new("REFUSED", null, "PASS8_PERK_FALLBACK_UNPROVEN");

        var firstSetup = Regex.Match(body, @"\blet\s+game\s*=\s*GetGameInstance\s*\(\s*\)\s*;");
        if (firstSetup.Success)
        {
            // A pure getter setup followed by source-proven enum conditions
            // means we can skip that work for every unrelated perk query.
            string prefix = body[..firstSetup.Index];
            var existing = FastGuard.Match(prefix);
            if (existing.Success)
            {
                string expr = existing.Groups["expr"].Value;
                var members = GuardMember.Matches(expr)
                    .Select(m => m.Groups["perk"].Value).Distinct(StringComparer.Ordinal).ToArray();
                string cleaned = GuardMember.Replace(expr, "");
                if (Regex.IsMatch(cleaned, @"[^\s|()]") ||
                    members.Except(names, StringComparer.Ordinal).Any())
                    return new("REFUSED", null, "PASS8_PERK_CUSTOM_GUARD_UNPROVEN");
                if (names.All(x => members.Contains(x, StringComparer.Ordinal)))
                    return new("COMPLETE", null, null);
                string joined = string.Join(" || ", names.Select(n =>
                    "Equals(gamedataNewPerkType." + n + ", perkType)"));
                string replacement = "if !(" + joined +
                    ") { return wrappedMethod(perkType); }";
                string updated = body[..existing.Index] + replacement +
                    body[(existing.Index + existing.Length)..];
                return new("APPLIED", ReplaceBody(source, method, updated), null);
            }
            // Comments and generic markers are inert and remain in place.
            string commentFree = Regex.Replace(prefix,
                @"//[^\r\n]*|/\*[\s\S]*?\*/", "");
            if (commentFree.Trim().Length != 0)
                return new("REFUSED", null, "PASS8_PERK_SETUP_NOT_FIRST");
            // Refuse a newer/custom body that has any other perkType-dependent
            // behavior not represented by the recognized 24-style branches.
            string withoutConditions = PerkBranch.Replace(body, "");
            withoutConditions = Regex.Replace(withoutConditions,
                @"wrappedMethod\s*\(\s*perkType\s*\)", "");
            if (Regex.IsMatch(withoutConditions, @"\bperkType\b"))
                return new("REFUSED", null, "PASS8_PERK_OTHER_PERK_CONSUMER");
            string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string guard = "    if !(" +
                string.Join(" || ", names.Select(n =>
                    "Equals(gamedataNewPerkType." + n + ", perkType)")) +
                ") { return wrappedMethod(perkType); }" + nl;
            return new("APPLIED", ReplaceBody(source, method, nl + guard + body), null);
        }
        // Historical/manual complete sources can use a switch dispatcher and
        // have no stock player setup. Do not append a second dispatcher.
        if (Regex.IsMatch(body, @"\bswitch\s+perkType\b") &&
            Regex.IsMatch(body, @"\bdefault\s*:\s*return\s+wrappedMethod\s*\(\s*perkType\s*\)"))
            return new("COMPLETE", null, null);
        return new("REFUSED", null, "PASS8_PERK_SETUP_OR_DISPATCH_UNPROVEN");
    }

    private static Outcome WreckingBallMeleeGate(string source, Method method)
    {
        string body = source[(method.Open + 1)..method.Close];
        var melee = Regex.Match(body,
            @"\blet\s+isMelee\s*=\s*WeaponObject\s*\.\s*IsMelee\s*\(\s*GameObject\s*\.\s*GetActiveWeapon\s*\(\s*scriptInterface\s*\.\s*executionOwner\s*\)\s*\.\s*GetItemID\s*\(\s*\)\s*\)\s*;");
        var guard = Regex.Match(body,
            @"\bif\s+!\s*isMelee\s*\{\s*return\s+false\s*;\s*\}");
        if (guard.Success && melee.Success && melee.Index < guard.Index)
            return new("COMPLETE", null, null);
        if (!melee.Success || guard.Success)
            return new("REFUSED", null, "PASS8_WRECKING_MELEE_GATE_UNPROVEN");
        var system = Regex.Match(body,
            @"\blet\s+neuralwareSystem\s*=\s*NeuralWareSystem\s*\.\s*GetInstance\s*\(\s*player\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*;");
        var stats = Regex.Match(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*player\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*;");
        if (!system.Success || !stats.Success || !(system.Index < stats.Index &&
            stats.Index < melee.Index) || !Regex.IsMatch(body,
            @"\bif\s*\(\s*isMelee\s*&&\s*neuralwareSystem\s*\.\s*CanSpendNeuralwareCapacity"))
            return new("REFUSED", null, "PASS8_WRECKING_DEPENDENCY_CHANGED");
        // Only move the original boolean-only melee predicate; preserve all
        // eligibility, capacity checks, player stats and fallback results.
        string declaration = melee.Value;
        string remaining = body.Remove(melee.Index, melee.Length);
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        remaining = remaining.Insert(system.Index, declaration + nl +
            "    if !isMelee { return false; }" + nl);
        return new("APPLIED", ReplaceBody(source, method, remaining), null);
    }

    private static Outcome FlashGorillaGate(string source, Method method)
    {
        string body = source[(method.Open + 1)..method.Close];
        var first = Regex.Match(body,
            @"\blet\s+gorillaArms\s*:\s*Bool\s*=\s*Equals\s*\(\s*MeleeTransition\s*\.\s*GetWeaponType\s*\(\s*scriptInterface\s*\)\s*,\s*gamedataItemType\s*\.\s*Cyb_StrongArms\s*\)\s*;");
        if (!first.Success)
            return new("REFUSED", null, "PASS8_FLASH_GORILLA_TARGET_MISSING");
        var guard = Regex.Match(body,
            @"\bif\s+!\s*gorillaArms\s*\{\s*return\s+wrappedMethod\s*\(\s*stateContext\s*,\s*scriptInterface\s*\)\s*;\s*\}");
        if (guard.Success && first.Index < guard.Index)
            return new("COMPLETE", null, null);
        if (guard.Success || !Regex.IsMatch(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>") ||
            !Regex.IsMatch(body,
                @"\bif\s*\(\s*playerHasRelevantChip\s*>\s*0\.0\s*\)"))
            return new("REFUSED", null, "PASS8_FLASH_DECISION_FLOW_CHANGED");
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        string updated = body.Insert(first.Index + first.Length,
            nl + "    if !gorillaArms { return wrappedMethod(stateContext, scriptInterface); }");
        return new("APPLIED", ReplaceBody(source, method, updated), null);
    }

    private static string ReplaceBody(string source, Method m, string body) =>
        source[..(m.Open + 1)] + body + source[m.Close..];

    private static readonly Operation[] Operations =
    [
        new("chip-perk-early-admission", "zzy_bChipwareExpansion",
            "PlayerDevelopmentData", "IsNewPerkBought", PerkAdmission),
        new("wrecking-ball-melee-gate", "zzy_bChipwareExpansion",
            "SprintDecisions", "IsWreckingBallAllowed", WreckingBallMeleeGate),
        new("flash-gorilla-early-admission", "zzy_bChipwareExpansion",
            "MeleeLeapDecisions", "EnterCondition", FlashGorillaGate)
    ];

    public static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("PASS8_OUTPUT_UNSAFE");
        if (baseOverlayRoot is not null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("PASS8_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256 ??
            throw new InvalidDataException("PASS8_SOURCE_HASH_MISSING");
        var files = Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories).ToArray();
        var composed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var refusals = new List<string>();
        var touched = new SortedSet<string>(StringComparer.Ordinal);
        int applied = 0, complete = 0;

        foreach (var op in Operations)
        {
            string[] ownerFiles = files.Where(f =>
                Path.GetRelativePath(scripts, f)
                    .Split(Path.DirectorySeparatorChar)
                    .Contains(op.Owner, StringComparer.OrdinalIgnoreCase)).ToArray();
            if (ownerFiles.Length == 0) continue; // absent mods: no ZIP file
            var candidates = new List<(string File, Method Span, string Source)>();
            foreach (string file in ownerFiles)
            {
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("PASS8_SOURCE_LINK_UNSAFE");
                string relative = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\', '/');
                string source = composed.TryGetValue(relative, out var pending)
                    ? pending : ReadEffective(file, relative, baseOverlayRoot);
                foreach (var m in FindMethods(source, op.Decorator, op.MethodName))
                    candidates.Add((file, m, source));
            }
            if (candidates.Count != 1)
            {
                refusals.Add(op.Name + ":" + (candidates.Count == 0
                    ? "PASS8_TARGET_NOT_FOUND" : "PASS8_TARGET_AMBIGUOUS"));
                continue;
            }
            var target = candidates[0];
            string rel = "r6/scripts/" +
                Path.GetRelativePath(scripts, target.File).Replace('\\', '/');
            var result = op.Apply(target.Source, target.Span);
            if (result.Status == "REFUSED")
            {
                refusals.Add(op.Name + ":" + result.Reason);
                continue;
            }
            if (result.Status == "COMPLETE") { complete++; continue; }
            if (result.Source is null)
                throw new InvalidDataException("PASS8_APPLIED_WITHOUT_BYTES");
            // No generic markers or custom source sections may be discarded.
            var genericMarkers = Regex.Matches(target.Source,
                @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
                .Select(m => m.Value).Distinct(StringComparer.Ordinal);
            if (genericMarkers.Any(m => !result.Source.Contains(m,
                StringComparison.Ordinal)))
                throw new InvalidDataException("PASS8_DROPPED_GENERIC_WORK");
            composed[rel] = result.Source;
            touched.Add(rel);
            applied++;
        }
        if (!string.Equals(fingerprint, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PASS8_SOURCE_CHANGED_DURING_GENERATE");

        Directory.CreateDirectory(output);
        foreach (var pair in composed)
        {
            string dst = Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dst, output) ||
                !dst.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PASS8_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, pair.Value, new UTF8Encoding(false));
        }
        string status = applied > 0 ? "APPLIED" :
            complete > 0 && refusals.Count == 0 ? "COMPLETE" :
            refusals.Count > 0 ? "REFUSED" : "NOT_APPLICABLE";
        var report = new Result(Rule, status, applied, complete,
            touched.ToArray(), refusals.ToArray(), true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return report;
    }

    private static string ReadEffective(string file, string relative,
        string? baseOverlayRoot)
    {
        if (baseOverlayRoot is null) return File.ReadAllText(file, Encoding.UTF8);
        string staged = Path.GetFullPath(Path.Combine(baseOverlayRoot,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!Inside(staged, baseOverlayRoot))
            throw new InvalidDataException("PASS8_OVERLAY_PATH_ESCAPE");
        return File.Exists(staged) ? File.ReadAllText(staged, Encoding.UTF8) :
            File.ReadAllText(file, Encoding.UTF8);
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
