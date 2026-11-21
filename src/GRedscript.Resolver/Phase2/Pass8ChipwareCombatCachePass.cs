using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 8 bounded player equipment-state queries for chipware
/// movement/charge decisions. Source-owned callback bodies and return order
/// remain intact; only the enum/stat queries behind a recognized condition
/// change. Standalone pass for isolated branch validation, never a corrective
/// layer and never a replacement for the existing G-REDruntime services.
/// </summary>
public static class Pass8ChipwareCombatCachePass
{
    public const string Rule = "CHIPWARE_PASS8_COMBAT_STATS_CACHE_V1";
    public sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string[] SourcePaths,
        string[] Refusals, bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(int Open, int Close);
    private sealed record Operation(string OwnerClass, string Name, string Kind);
    private sealed record Decision(string Status, string? Updated, string? Reason);
    private static readonly Operation[] Operations =
    [
        new("DodgeDecisions", "EnterCondition", "keren"),
        new("KerenzikovDecisions", "ToTimeDilationReady", "keren"),
        new("KerenzikovDecisions", "EnterCondition", "keren"),
        new("SprintDecisions", "IsWreckingBallAllowed", "wreck"),
        new("MeleeTransition", "MeleeSprintStateCondition", "wreck"),
        new("MeleeBodySlamAttackDecisions", "EnterCondition", "wreck"),
        new("MeleeLeapEvents", "LeapToTarget", "flash"),
        new("MeleeLeapDecisions", "EnterCondition", "flash")
    ];

    private const string CacheFile = "r6/scripts/zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds";
    private const string CacheSource = """
public class GRedChipwareHotpathCache extends IScriptable {
    private let m_nextRefresh: Float;
    private let m_lastRefresh: Float;
    private let m_initialized: Bool;
    private let m_hasKerenzikov: Bool;
    private let m_hasWreckingBall: Bool;
    private let m_hasFlashAndThunderclap: Bool;
    private let m_statTypesInitialized: Bool;
    private let m_kerenzikovStat: gamedataStatType;
    private let m_kerenzikovCrackedStat: gamedataStatType;
    private let m_kerenzikovChargesStat: gamedataStatType;
    private let m_wreckingBallStat: gamedataStatType;
    private let m_flashAndThunderclapStat: gamedataStatType;

    private func EnsureStats() -> Void {
        if this.m_statTypesInitialized { return; }
        this.m_kerenzikovStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipEquipped"));
        this.m_kerenzikovCrackedStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipCrackedEquipped"));
        this.m_kerenzikovChargesStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipCharges"));
        this.m_wreckingBallStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"WreckingBallEquipped"));
        this.m_flashAndThunderclapStat = IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"FlashAndThunderclapEquipped"));
        this.m_statTypesInitialized = true;
    }

    private func RefreshFast(player: ref<PlayerPuppet>) -> Void {
        if !IsDefined(player) { return; }
        let game: GameInstance = player.GetGame();
        let now: Float = EngineTime.ToFloat(GameInstance.GetEngineTime(game));
        if this.m_initialized && now >= this.m_lastRefresh && now < this.m_nextRefresh { return; }
        let stats: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
        if !IsDefined(stats) { return; }
        this.EnsureStats();
        let id: StatsObjectID = Cast<StatsObjectID>(player.GetEntityID());
        let kerenzikov: Float = stats.GetStatValue(id, this.m_kerenzikovStat);
        let cracked: Float = stats.GetStatValue(id, this.m_kerenzikovCrackedStat);
        let charges: Float = stats.GetStatValue(id, this.m_kerenzikovChargesStat);
        this.m_hasKerenzikov = (kerenzikov > 0.0 && charges > 0.0) || cracked > 0.0;
        this.m_hasWreckingBall = stats.GetStatValue(id, this.m_wreckingBallStat) > 0.0;
        this.m_hasFlashAndThunderclap = stats.GetStatValue(id, this.m_flashAndThunderclapStat) > 0.0;
        this.m_lastRefresh = now;
        this.m_nextRefresh = now + 0.05;
        this.m_initialized = true;
    }

    public func HasKerenzikov(player: ref<PlayerPuppet>) -> Bool {
        this.RefreshFast(player);
        return this.m_hasKerenzikov;
    }
    public func HasWreckingBall(player: ref<PlayerPuppet>) -> Bool {
        this.RefreshFast(player);
        return this.m_hasWreckingBall;
    }
    public func HasFlashAndThunderclap(player: ref<PlayerPuppet>) -> Bool {
        this.RefreshFast(player);
        return this.m_hasFlashAndThunderclap;
    }
}

@addField(PlayerPuppet)
private let m_gredChipwareHotpathCache: ref<GRedChipwareHotpathCache>;

@addMethod(PlayerPuppet)
public final func GRedChipwareHotpathCache() -> ref<GRedChipwareHotpathCache> {
    if !IsDefined(this.m_gredChipwareHotpathCache) {
        this.m_gredChipwareHotpathCache = new GRedChipwareHotpathCache();
    }
    return this.m_gredChipwareHotpathCache;
}
""";

    private static int Match(IReadOnlyList<SourceAdmission.Token> tokens,
        int first, string opening, string closing)
    {
        if (first >= tokens.Count || first < 0 ||
            tokens[first].Value != opening) return -1;
        int depth = 0;
        for (int i = first; i < tokens.Count; i++)
        {
            if (tokens[i].Value == opening) depth++;
            else if (tokens[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static IReadOnlyList<Method> FindMethods(string source, Operation op)
    {
        var t = SourceAdmission.ReadTokens(source);
        var methods = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i+1].Value != op.Name ||
                t[i+2].Value != "(") continue;
            int args = Match(t, i+2, "(", ")");
            if (args < 0) continue;
            int open = args+1;
            while (open<t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open>=t.Count || t[open].Value != "{") continue;
            int close = Match(t, open, "{", "}");
            if (close < 0) continue;
            string prev = source[Math.Max(0,t[i].Start-300)..t[i].Start];
            if (!Regex.IsMatch(prev,
                @"@wrapMethod\s*\(\s*" + Regex.Escape(op.OwnerClass) +
                @"\s*\)\s*(?:(?:private|protected|public|const|static|final|cb)\s+)*$"))
                continue;
            methods.Add(new(t[open].Start, t[close].Start));
            i=close;
        }
        return methods;
    }

    private static string ReplaceBody(string source, Method method, string body) =>
        source[..(method.Open+1)] + body + source[method.Close..];

    private static readonly string[] KerenNames =
    ["kerenzikovChipEquipped","crackedKerenzikovChipEquipped","kerenzikovChipChargesEquipped"];
    private static readonly string[] KerenStats =
    ["KerenzikovChipEquipped","KerenzikovChipCrackedEquipped","KerenzikovChipCharges"];
    private static readonly Regex KerenCondition = new(
        @"\(\s*\(\s*kerenzikovChipEquipped\s*>\s*0\.0\s*&&\s*kerenzikovChipChargesEquipped\s*>\s*0\.0\s*\)\s*\|\|\s*crackedKerenzikovChipEquipped\s*>\s*0\.0\s*\)",
        RegexOptions.CultureInvariant);

    private static Decision Keren(string source, Method method)
    {
        string body = source[(method.Open+1)..method.Close];
        string marker = "GRedChipwareHotpathCache().HasKerenzikov(player)";
        if (body.Contains(marker,StringComparison.Ordinal))
        {
            if (KerenCondition.IsMatch(body) || KerenNames.Any(n =>
                Regex.IsMatch(body,@"\blet\s+"+n+@"\s*:")))
                return new("REFUSED",null,"PASS8_KEREN_MIXED_OLD_AND_CACHED_FLOW");
            return new("COMPLETE",null,null);
        }
        var stats = Regex.Match(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*GetGameInstance\s*\(\s*\)\s*\)\s*;");
        if (!stats.Success || KerenCondition.Matches(body).Count!=1 ||
            !body.Contains("wrappedMethod(stateContext, scriptInterface)",
                StringComparison.Ordinal))
            return new("REFUSED",null,"PASS8_KEREN_ELIGIBILITY_FLOW_CHANGED");
        var declarations = new List<Match>();
        for (int i=0;i<KerenNames.Length;i++)
        {
            string pattern=@"\blet\s+"+KerenNames[i]+
                @"\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([^;]*?"+
                Regex.Escape(KerenStats[i])+@"[^;]*?\)\s*;";
            var m=Regex.Match(body,pattern);
            if (!m.Success || Regex.Matches(body,pattern).Count!=1)
                return new("REFUSED",null,"PASS8_KEREN_STAT_GROUP_CHANGED");
            declarations.Add(m);
        }
        if (declarations.Any(x=>x.Index<stats.Index) ||
            Regex.Matches(body,@"\bstatsSystem\b").Count!=4 ||
            KerenNames.Any(name=>Regex.Matches(body,@"\b"+name+@"\b").Count!=2))
            return new("REFUSED",null,"PASS8_KEREN_OTHER_STAT_USE");
        string nl=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        // Transform in reverse offset order to protect the original statement
        // and condition positioning. All unrelated state-machine code is kept.
        var edits=new List<(int Start,int End,string Text)>();
        var condition=KerenCondition.Match(body);
        edits.Add((condition.Index,condition.Index+condition.Length,
            "IsDefined(player) && player."+marker));
        foreach(var m in declarations)
            edits.Add((m.Index,m.Index+m.Length,""));
        edits.Add((stats.Index,stats.Index+stats.Length,
            "let player: ref<PlayerPuppet> = scriptInterface.executionOwner as PlayerPuppet;"));
        foreach(var edit in edits.OrderByDescending(x=>x.Start))
            body=body[..edit.Start]+edit.Text+body[edit.End..];
        if (KerenCondition.IsMatch(body) || !body.Contains(marker,
                StringComparison.Ordinal))
            throw new InvalidDataException("PASS8_KEREN_REWRITE_POSTCONDITION");
        return new("APPLIED",ReplaceBody(source,method,body),null);
    }

    private static Decision Wreck(string source, Method method)
    {
        string body=source[(method.Open+1)..method.Close];
        string marker="player.GRedChipwareHotpathCache().HasWreckingBall(player)";
        string name="hasWreckingBallChip";
        if (body.Contains(marker,StringComparison.Ordinal))
        {
            if (Regex.IsMatch(body,@"\blet\s+hasWreckingBallChip\s*:\s*Float") ||
                Regex.IsMatch(body,@"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem"))
                return new("REFUSED",null,"PASS8_WRECK_PARTIAL_STAT_CACHE");
            return new("COMPLETE",null,null);
        }
        var stat=Regex.Match(body,
            @"\blet\s+hasWreckingBallChip\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([^;]*?WreckingBallEquipped[^;]*?\)\s*;");
        var system=Regex.Match(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*player\s*\.\s*GetGame\s*\(\s*\)\s*\)\s*;");
        if (!stat.Success || !system.Success || system.Index>stat.Index ||
            !body.Contains("let player = scriptInterface.executionOwner as PlayerPuppet;",
                StringComparison.Ordinal))
            return new("REFUSED",null,"PASS8_WRECK_STAT_FLOW_CHANGED");
        if (Regex.Matches(body,@"\bstatsSystem\b").Count!=2 ||
            Regex.Matches(body,@"\bhasWreckingBallChip\b").Count<2)
            return new("REFUSED",null,"PASS8_WRECK_OTHER_STAT_USE");
        string markerCondition = "player.GRedChipwareHotpathCache().HasWreckingBall(player)";
        body=body.Replace(stat.Value, "let hasWreckingBallChip: Bool = "+markerCondition+";",
            StringComparison.Ordinal);
        body=body.Replace(system.Value,"",StringComparison.Ordinal);
        body=Regex.Replace(body,@"\bhasWreckingBallChip\s*>\s*0\.0\b","hasWreckingBallChip");
        body=Regex.Replace(body,@"\bhasWreckingBallChip\s*==\s*0\.0\b","!hasWreckingBallChip");
        if (Regex.IsMatch(body,@"hasWreckingBallChip\s*(?:[=!<>]+)\s*\d"))
            return new("REFUSED",null,"PASS8_WRECK_UNSUPPORTED_COMPARISON");
        return new("APPLIED",ReplaceBody(source,method,body),null);
    }

    private static Decision Flash(string source, Method method)
    {
        string body=source[(method.Open+1)..method.Close];
        string marker="player.GRedChipwareHotpathCache().HasFlashAndThunderclap(player)";
        if(body.Contains(marker,StringComparison.Ordinal))
        {
            if(Regex.IsMatch(body,@"\blet\s+playerHasRelevantChip\s*:\s*Float"))
                return new("REFUSED",null,"PASS8_FLASH_MIXED_OLD_AND_NEW");
            return new("COMPLETE",null,null);
        }
        var stat=Regex.Match(body,
            @"\blet\s+playerHasRelevantChip\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([^;]*?FlashAndThunderclapEquipped[^;]*?\)\s*;");
        var system=Regex.Match(body,
            @"\blet\s+statsSystem\s*:\s*ref\s*<\s*StatsSystem\s*>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*GetGameInstance\s*\(\s*\)\s*\)\s*;");
        if (!stat.Success || !system.Success || system.Index>stat.Index ||
            Regex.Matches(body,@"\bstatsSystem\b").Count!=2 ||
            Regex.Matches(body,@"\bplayerHasRelevantChip\b").Count!=2)
            return new("REFUSED",null,"PASS8_FLASH_STAT_FLOW_CHANGED");
        string check="playerHasRelevantChip > 0.0";
        if(!body.Contains(check,StringComparison.Ordinal))
            return new("REFUSED",null,"PASS8_FLASH_CHIP_CONDITION_CHANGED");
        body=body.Replace(check,
            "IsDefined(player) && "+marker,StringComparison.Ordinal);
        body=body.Replace(stat.Value,"",StringComparison.Ordinal);
        body=body.Replace(system.Value,
            "let player: ref<PlayerPuppet> = scriptInterface.executionOwner as PlayerPuppet;",
            StringComparison.Ordinal);
        return new("APPLIED",ReplaceBody(source,method,body),null);
    }

    private static string ReadEffective(string file,string rel,string? baseOverlay)
    {
        if(baseOverlay is null) return File.ReadAllText(file,Encoding.UTF8);
        string staged=Path.GetFullPath(Path.Combine(baseOverlay,
            rel.Replace('/',Path.DirectorySeparatorChar)));
        if(!Inside(staged,baseOverlay))
            throw new InvalidDataException("PASS8_CHIPWARE_OVERLAY_ESCAPE");
        return File.Exists(staged)?File.ReadAllText(staged,Encoding.UTF8):
            File.ReadAllText(file,Encoding.UTF8);
    }

    public static Result Generate(string gameRoot,string outputRoot,
        string? baseOverlayRoot=null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||
            File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("PASS8_CHIPWARE_OUTPUT_UNSAFE");
        if(baseOverlayRoot is not null &&
            (!Directory.Exists(baseOverlayRoot)||Inside(baseOverlayRoot,game)||
             Inside(output,baseOverlayRoot)||Inside(baseOverlayRoot,output)))
            throw new InvalidDataException("PASS8_CHIPWARE_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string fingerprint=admission.SourceStackSha256 ??
            throw new InvalidDataException("PASS8_CHIPWARE_SOURCE_HASH_MISSING");
        var folder=Path.Combine(scripts,"zzy_bChipwareExpansion");
        var staged=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var changed=new SortedSet<string>(StringComparer.Ordinal);
        var refusals=new List<string>();
        int applied=0,complete=0;
        if(Directory.Exists(folder))
        {
            var files=Directory.EnumerateFiles(folder,"*.reds",SearchOption.AllDirectories).ToArray();
            foreach(var op in Operations)
            {
                var matches=new List<(string Rel,Method Method,string Text)>();
                foreach(string file in files)
                {
                    if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                        throw new InvalidDataException("PASS8_CHIPWARE_LINK_UNSAFE");
                    string rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                    string src=staged.TryGetValue(rel,out var prior)?prior:
                        ReadEffective(file,rel,baseOverlayRoot);
                    foreach(var method in FindMethods(src,op))
                        matches.Add((rel,method,src));
                }
                string label=op.OwnerClass+"."+op.Name;
                if(matches.Count!=1)
                {
                    refusals.Add(label+":"+(matches.Count==0?
                        "PASS8_CHIPWARE_METHOD_MISSING":"PASS8_CHIPWARE_METHOD_AMBIGUOUS"));
                    continue;
                }
                var target=matches[0];
                var result=op.Kind switch
                {
                    "keren"=>Keren(target.Text,target.Method),
                    "wreck"=>Wreck(target.Text,target.Method),
                    "flash"=>Flash(target.Text,target.Method),
                    _=>throw new InvalidDataException("PASS8_CHIPWARE_BAD_OPERATION")
                };
                if(result.Status=="REFUSED")
                {
                    refusals.Add(label+":"+result.Reason);
                    continue;
                }
                if(result.Status=="COMPLETE"){complete++;continue;}
                string rewrite=result.Updated ??
                    throw new InvalidDataException("PASS8_CHIPWARE_NO_BYTES");
                var markers=Regex.Matches(target.Text,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
                    .Select(x=>x.Value).Distinct(StringComparer.Ordinal);
                if(markers.Any(m=>!rewrite.Contains(m,StringComparison.Ordinal)))
                    throw new InvalidDataException("PASS8_CHIPWARE_GENERIC_LOSS");
                staged[target.Rel]=rewrite;
                changed.Add(target.Rel);
                applied++;
            }
            if(applied>0||complete>0)
            {
                var existing=files.Where(f=>ReadEffective(f,"r6/scripts/"+
                    Path.GetRelativePath(scripts,f).Replace('\\','/'),
                    baseOverlayRoot).Contains("class GRedChipwareHotpathCache",
                        StringComparison.Ordinal)).ToArray();
                if(existing.Length==0)
                {
                    if(File.Exists(Path.Combine(folder,"chipsScripts","GRedHotpathCache.reds")))
                        refusals.Add("chipware-helper:PASS8_CHIPWARE_HELPER_FILENAME_OCCUPIED");
                    else
                    {
                        staged[CacheFile]=CacheSource;
                        changed.Add(CacheFile);
                        applied++;
                    }
                }
                else if(existing.Length!=1)
                    refusals.Add("chipware-helper:PASS8_CHIPWARE_DUPLICATE_HELPERS");
                else
                {
                    string helper=ReadEffective(existing[0],"r6/scripts/"+
                        Path.GetRelativePath(scripts,existing[0]).Replace('\\','/'),
                        baseOverlayRoot);
                    if(new[]{"HasKerenzikov(","HasWreckingBall(",
                        "HasFlashAndThunderclap(","GRedChipwareHotpathCache()"}
                        .Any(name=>!helper.Contains(name,StringComparison.Ordinal)))
                        refusals.Add("chipware-helper:PASS8_CHIPWARE_HELPER_ABI_INCOMPATIBLE");
                }
                if(refusals.Any(r=>r.StartsWith("chipware-helper:",StringComparison.Ordinal)))
                {
                    // Required helper is atomic with all owner callsite rewrites.
                    staged.Clear();
                    changed.Clear();
                    applied=0;
                }
            }
        }
        if(!string.Equals(fingerprint,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PASS8_CHIPWARE_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach(var pair in staged)
        {
            string path=Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(path,output)||!path.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PASS8_CHIPWARE_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,pair.Value,new UTF8Encoding(false));
        }
        string status=applied>0?"APPLIED":
            complete>0&&refusals.Count==0?"COMPLETE":
            refusals.Count>0?"REFUSED":"NOT_APPLICABLE";
        var report=new Result(Rule,status,applied,complete,changed.ToArray(),
            refusals.ToArray(),true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true
            }),new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string child,string root)=>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
