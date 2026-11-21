using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 8 ImmersiveCyberware bounded equipment/config reads and
/// retained ammo scale writes. Preserves existing state-machine/weapon roster
/// invocation cadence, original player getter, wrapped calls and user edits.
/// Separate branch-only generator: canonical order is owned by integration.
/// </summary>
public static class Pass8ImmersiveCyberwarePass
{
    public const string Rule = "IMMERSIVE_CYBERWARE_PASS8_HOTPATH_V1";
    public sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string[] Refusals,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Method(int Open, int Close);
    private sealed record Decision(string Status, string? Source, string? Reason,
        bool NeedsCache);

    // This is an additive owner-local helper, never a replacement for an
    // existing player class or a G-REDruntime service.
    private static readonly string CacheHelper = """
import ImmersiveCyberware.Settings.*

public class GRedICHotpathCache extends IScriptable {
  private let m_nextStatsRefresh: Float;
  private let m_lastStatsRefresh: Float;
  private let m_statsInitialized: Bool;
  private let m_nextSettingsRefresh: Float;
  private let m_lastSettingsRefresh: Float;
  private let m_settingsInitialized: Bool;
  private let m_statTypesInitialized: Bool;
  private let m_ammoCounterConnected: Bool;
  private let m_scanningFocusConnected: Bool;
  private let m_scanningFocusRequired: Bool;
  private let m_ammoCounterStat: gamedataStatType;
  private let m_scanningFocusStat: gamedataStatType;

  private func Now(game: GameInstance) -> Float {
    return EngineTime.ToFloat(GameInstance.GetEngineTime(game));
  }

  private func EnsureStatTypes() -> Void {
    if this.m_statTypesInitialized { return; }
    this.m_ammoCounterStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"AmmoCounterConnected"));
    this.m_scanningFocusStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"IMScanningFocus"));
    this.m_statTypesInitialized = true;
  }

  private func RefreshStats(player: ref<PlayerPuppet>) -> Void {
    if !IsDefined(player) { return; }
    let game: GameInstance = player.GetGame();
    let now: Float = this.Now(game);
    if this.m_statsInitialized && now >= this.m_lastStatsRefresh && now < this.m_nextStatsRefresh { return; }
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
    if !IsDefined(statsSystem) { return; }
    this.EnsureStatTypes();
    let statsID: StatsObjectID = Cast<StatsObjectID>(player.GetEntityID());
    this.m_ammoCounterConnected = statsSystem.GetStatValue(statsID, this.m_ammoCounterStat) > 0.0;
    this.m_scanningFocusConnected = statsSystem.GetStatValue(statsID, this.m_scanningFocusStat) > 0.0;
    this.m_lastStatsRefresh = now;
    this.m_nextStatsRefresh = now + 0.10;
    this.m_statsInitialized = true;
  }

  private func RefreshSettings(player: ref<PlayerPuppet>) -> Void {
    if !IsDefined(player) { return; }
    let now: Float = this.Now(player.GetGame());
    if this.m_settingsInitialized && now >= this.m_lastSettingsRefresh && now < this.m_nextSettingsRefresh { return; }
    let settings: ref<ICSettings> = new ICSettings();
    this.m_scanningFocusRequired = settings.scanningFocus;
    this.m_lastSettingsRefresh = now;
    this.m_nextSettingsRefresh = now + 0.50;
    this.m_settingsInitialized = true;
  }

  public func HasAmmoCounter(player: ref<PlayerPuppet>) -> Bool {
    this.RefreshStats(player);
    return this.m_ammoCounterConnected;
  }

  public func HasScanningFocus(player: ref<PlayerPuppet>) -> Bool {
    this.RefreshStats(player);
    return this.m_scanningFocusConnected;
  }

  public func RequiresScanningFocus(player: ref<PlayerPuppet>) -> Bool {
    this.RefreshSettings(player);
    return this.m_scanningFocusRequired;
  }
}

@addField(PlayerPuppet)
private let m_gredICHotpathCache: ref<GRedICHotpathCache>;

@addMethod(PlayerPuppet)
public final func GRedICHotpathCache() -> ref<GRedICHotpathCache> {
  if !IsDefined(this.m_gredICHotpathCache) {
    this.m_gredICHotpathCache = new GRedICHotpathCache();
  }
  return this.m_gredICHotpathCache;
}
""";

    private static readonly (string Type, string Name)[] AmmoFields =
    [
        ("wref<inkWidget>", "m_gredICAmmoWidget"),
        ("Bool", "m_gredICAmmoVisibilityKnown"),
        ("Bool", "m_gredICAmmoVisible")
    ];

    private static int Match(IReadOnlyList<SourceAdmission.Token> t, int at,
        string opening, string closing)
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
            int args = Match(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int close = Match(t, open, "{", "}");
            if (close < 0) continue;
            string prefix = source[Math.Max(0, t[i].Start - 280)..t[i].Start];
            if (!Regex.IsMatch(prefix,
                @"@wrapMethod\s*\(\s*" + Regex.Escape(decorator) +
                @"\s*\)\s*(?:(?:public|protected|private|final|static|const|cb)\s+)*$"))
                continue;
            found.Add(new(t[open].Start, t[close].Start));
            i = close;
        }
        return found;
    }

    private static string ReplaceBody(string source, Method m, string body) =>
        source[..(m.Open + 1)] + body + source[m.Close..];

    private static Decision Ammo(string source, Method m)
    {
        string body = source[(m.Open + 1)..m.Close];
        bool changed = false;
        bool cacheCall = body.Contains("HasAmmoCounter(", StringComparison.Ordinal);
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        if (!cacheCall)
        {
            var stats = Regex.Match(body,
                @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*game\s*\)\s*;");
            var statRead = Regex.Match(body,
                @"\blet\s+hasAimThing\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([\s\S]*?AmmoCounterConnected[\s\S]*?\)\s*;");
            var condition = Regex.Match(body,
                @"\blet\s+isWeaponTotalAmmoVisible\s*:\s*Bool\s*=\s*hasAimThing\s*>\s*0\.0\s*\|\|\s*this\s*\.\s*m_inVehicle\s*\|\|\s*this\s*\.\s*m_player\s*\.\s*IsReplacer\s*\(\s*\)\s*;");
            if (!stats.Success || !statRead.Success || !condition.Success ||
                stats.Index >= statRead.Index || statRead.Index >= condition.Index)
                return new("REFUSED", null, "PASS8_IC_AMMO_STAT_FLOW_UNPROVEN", false);
            body = body.Remove(statRead.Index, statRead.Length);
            body = body.Remove(stats.Index, stats.Length);
            body = Regex.Replace(body, @"\bhasAimThing\s*>\s*0\.0",
                "player.GRedICHotpathCache().HasAmmoCounter(player)", RegexOptions.None,
                TimeSpan.FromSeconds(1));
            changed = true;
        }
        bool setterGuard = body.Contains("m_gredICAmmoVisibilityKnown", StringComparison.Ordinal) &&
            body.Contains("m_gredICAmmoWidget", StringComparison.Ordinal);
        if (!setterGuard)
        {
            // Preserve the original widget lookup and scales. Track widget
            // identity as well as visibility so replacement rerenders.
            var scale = Regex.Match(body,
                @"\bif\s+isWeaponTotalAmmoVisible\s*\{\s*widget\s*\.\s*SetScale\s*\(\s*new\s+Vector2\s*\(\s*1\.0\s*,\s*1\.0\s*\)\s*\)\s*;\s*\}\s*else\s*\{\s*widget\s*\.\s*SetScale\s*\(\s*new\s+Vector2\s*\(\s*0\.0\s*,\s*0\.0\s*\)\s*\)\s*;\s*\}\s*;?");
            if (!scale.Success || !Regex.IsMatch(body,
                    @"\blet\s+widget\s*=\s*root\s*\.\s*GetWidget\s*\("))
                return new("REFUSED", null, "PASS8_IC_AMMO_WIDGET_LIFECYCLE_UNPROVEN", false);
            string wrapped = "if !this.m_gredICAmmoVisibilityKnown || " +
                "!IsDefined(this.m_gredICAmmoWidget) || " +
                "this.m_gredICAmmoWidget != widget || " +
                "NotEquals(this.m_gredICAmmoVisible, isWeaponTotalAmmoVisible) {" + nl +
                "    this.m_gredICAmmoWidget = widget;" + nl +
                "    this.m_gredICAmmoVisibilityKnown = true;" + nl +
                "    this.m_gredICAmmoVisible = isWeaponTotalAmmoVisible;" + nl +
                "    " + scale.Value + nl + "  }";
            body = body[..scale.Index] + wrapped +
                body[(scale.Index + scale.Length)..];
            changed = true;
        }
        string updated = changed ? ReplaceBody(source, m, body) : source;
        // A partial owner may already have one or two fields. Add only those
        // missing and never duplicate a manually applied field.
        if (body.Contains("m_gredICAmmoVisibilityKnown", StringComparison.Ordinal))
        {
            var missing = AmmoFields.Where(f => !Regex.IsMatch(updated,
                @"\blet\s+" + Regex.Escape(f.Name) + @"\s*:\s*" +
                Regex.Escape(f.Type).Replace("<", @"\s*<\s*")
                    .Replace(">", @"\s*>\s*") + @"\s*;")).ToArray();
            if (missing.Length > 0)
            {
                string declarations = string.Join(nl, missing.Select(f =>
                    "@addField(WeaponRosterGameController)" + nl +
                    "private let " + f.Name + ": " + f.Type + ";")) + nl + nl;
                updated = declarations + updated;
                changed = true;
            }
        }
        return new(changed ? "APPLIED" : "COMPLETE", changed ? updated : null,
            null, true);
    }

    private static Decision Focus(string source, Method m)
    {
        string body = source[(m.Open + 1)..m.Close];
        bool hasStat = body.Contains("HasScanningFocus(", StringComparison.Ordinal);
        bool hasSettings = body.Contains("RequiresScanningFocus(", StringComparison.Ordinal);
        if (hasStat && hasSettings)
            return new("COMPLETE", null, null, true);
        if (hasStat != hasSettings)
            return new("REFUSED", null, "PASS8_IC_FOCUS_PARTIAL_CACHE_CALL_UNPROVEN", true);
        var stats = Regex.Match(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*game\s*\)\s*;");
        var read = Regex.Match(body,
            @"\blet\s+hasScannerThing\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([\s\S]*?IMScanningFocus[\s\S]*?\)\s*;");
        var condition = Regex.Match(body,
            @"hasScannerThing\s*>\s*0\.0\s*\|\|\s*!\s*\(\s*new\s+ICSettings\s*\(\s*\)\s*\)\s*\.\s*scanningFocus");
        var player = Regex.Match(body,
            @"\blet\s+player\s*=\s*GetPlayer\s*\(\s*game\s*\)\s*;");
        if (!stats.Success || !read.Success || !condition.Success ||
            !player.Success || !(player.Index < stats.Index &&
            stats.Index < read.Index && read.Index < condition.Index))
            return new("REFUSED", null, "PASS8_IC_FOCUS_DECISION_FLOW_UNPROVEN", false);
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        body = body.Remove(read.Index, read.Length);
        body = body.Remove(stats.Index, stats.Length);
        body = Regex.Replace(body,
            @"hasScannerThing\s*>\s*0\.0\s*\|\|\s*!\s*\(\s*new\s+ICSettings\s*\(\s*\)\s*\)\s*\.\s*scanningFocus",
            "player.GRedICHotpathCache().HasScanningFocus(player) || " +
            "!player.GRedICHotpathCache().RequiresScanningFocus(player)");
        int afterPlayer = Regex.Match(body,
            @"\blet\s+player\s*=\s*GetPlayer\s*\(\s*game\s*\)\s*;").Index +
            Regex.Match(body,
                @"\blet\s+player\s*=\s*GetPlayer\s*\(\s*game\s*\)\s*;").Length;
        body = body.Insert(afterPlayer,
            nl + "    if !IsDefined(player) { return wrappedMethod(stateContext, scriptInterface); }");
        return new("APPLIED", ReplaceBody(source, m, body), null, true);
    }

    public static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("PASS8_IC_OUTPUT_UNSAFE");
        if (baseOverlayRoot is not null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("PASS8_IC_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256 ??
            throw new InvalidDataException("PASS8_IC_SOURCE_HASH_MISSING");
        var owner = Path.Combine(scripts, "ImmersiveCyberware");
        var composed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var touched = new SortedSet<string>(StringComparer.Ordinal);
        var refusals = new List<string>();
        int applied = 0, complete = 0;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).ToArray();
            var ops = new (string Name, string Decorator, string Func,
                Func<string, Method, Decision> Rewrite)[]
            {
                ("ammo-widget", "WeaponRosterGameController", "OnUpdate", Ammo),
                ("focus-scanner", "TimeDilationFocusModeDecisions", "EnterCondition", Focus)
            };
            bool needsHelper = false;
            foreach (var op in ops)
            {
                var matches = new List<(string Rel, Method Span, string Text)>();
                foreach (string file in files)
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("PASS8_IC_SOURCE_LINK_UNSAFE");
                    string rel = "r6/scripts/" +
                        Path.GetRelativePath(scripts, file).Replace('\\', '/');
                    string src = composed.TryGetValue(rel, out var pending)
                        ? pending : ReadEffective(file, rel, baseOverlayRoot);
                    foreach (var m in FindMethods(src, op.Decorator, op.Func))
                        matches.Add((rel, m, src));
                }
                if (matches.Count != 1)
                {
                    refusals.Add(op.Name + ":" + (matches.Count == 0
                        ? "PASS8_IC_TARGET_MISSING" : "PASS8_IC_TARGET_AMBIGUOUS"));
                    continue;
                }
                var target = matches[0];
                var decision = op.Rewrite(target.Text, target.Span);
                if (decision.Status == "REFUSED")
                {
                    refusals.Add(op.Name + ":" + decision.Reason);
                    continue;
                }
                needsHelper |= decision.NeedsCache;
                if (decision.Status == "COMPLETE") { complete++; continue; }
                string next = decision.Source ??
                    throw new InvalidDataException("PASS8_IC_NO_GENERATED_SOURCE");
                var markers = Regex.Matches(target.Text,
                    @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
                    .Select(m => m.Value).Distinct(StringComparer.Ordinal);
                if (markers.Any(m => !next.Contains(m, StringComparison.Ordinal)))
                    throw new InvalidDataException("PASS8_IC_GENERIC_WORK_LOST");
                composed[target.Rel] = next;
                touched.Add(target.Rel);
                applied++;
            }
            if (needsHelper)
            {
                var helperFiles = files.Where(f =>
                    ReadEffective(f, "r6/scripts/" +
                        Path.GetRelativePath(scripts, f).Replace('\\', '/'),
                        baseOverlayRoot).Contains("class GRedICHotpathCache",
                            StringComparison.Ordinal)).ToArray();
                if (helperFiles.Length > 1)
                    refusals.Add("hotpath-cache:PASS8_IC_DUPLICATE_CLASS");
                else if (helperFiles.Length == 1)
                {
                    string existing = File.ReadAllText(helperFiles[0], Encoding.UTF8);
                    foreach (string required in new[] {
                        "HasAmmoCounter(", "HasScanningFocus(",
                        "RequiresScanningFocus(", "GRedICHotpathCache()"
                    })
                        if (!existing.Contains(required, StringComparison.Ordinal))
                            refusals.Add("hotpath-cache:PASS8_IC_EXISTING_CACHE_ABI_UNPROVEN");
                }
                else
                {
                    string relative = "r6/scripts/ImmersiveCyberware/GRedHotpathCache.reds";
                    if (File.Exists(Path.Combine(scripts, "ImmersiveCyberware",
                            "GRedHotpathCache.reds")) || (baseOverlayRoot is not null &&
                            File.Exists(Path.Combine(baseOverlayRoot, relative.Replace('/',
                                Path.DirectorySeparatorChar)))))
                        refusals.Add("hotpath-cache:PASS8_IC_HELPER_FILENAME_OCCUPIED");
                    else
                    {
                        composed[relative] = CacheHelper;
                        touched.Add(relative);
                        applied++;
                    }
                }
                if (refusals.Any(r => r.StartsWith("hotpath-cache:", StringComparison.Ordinal)))
                {
                    // A missing/incompatible required helper must never leave
                    // new call sites pointing at an unproven class.
                    composed.Clear();
                    touched.Clear();
                    applied = 0;
                }
            }
        }
        if (!string.Equals(fingerprint, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PASS8_IC_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        foreach (var pair in composed)
        {
            string dst = Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dst, output) ||
                !dst.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PASS8_IC_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, pair.Value, new UTF8Encoding(false));
        }
        string status = applied > 0 ? "APPLIED" :
            complete > 0 && refusals.Count == 0 ? "COMPLETE" :
            refusals.Count > 0 ? "REFUSED" : "NOT_APPLICABLE";
        var result = new Result(Rule, status, applied, complete,
            touched.ToArray(), refusals.ToArray(), true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static string ReadEffective(string file, string rel, string? overlay)
    {
        if (overlay is null) return File.ReadAllText(file, Encoding.UTF8);
        string path = Path.GetFullPath(Path.Combine(overlay,
            rel.Replace('/', Path.DirectorySeparatorChar)));
        if (!Inside(path, overlay))
            throw new InvalidDataException("PASS8_IC_PREVIEW_PATH_ESCAPE");
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) :
            File.ReadAllText(file, Encoding.UTF8);
    }
    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
