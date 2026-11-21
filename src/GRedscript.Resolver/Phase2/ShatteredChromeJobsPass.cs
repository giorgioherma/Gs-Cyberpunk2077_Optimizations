using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.2 ShatteredChrome main/HUD periodic jobs. The original
/// per-owner DelayCallbacks remain exclusive fallbacks when the shared
/// runtime/scheduler is unavailable or either registration fails. The original
/// OnTick/OnHUDTick bodies and their timing constants remain unchanged.
/// </summary>
internal static class ShatteredChromeJobsPass
{
    internal const string Rule = "SHATTERED_CHROME_JOBS_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Block(int Start, int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Reason,
        string? Changed, int Edits);

    private static bool Has(string s, string regex) =>
        Regex.IsMatch(s, regex, RegexOptions.CultureInvariant);
    private static string NL(string s) =>
        s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private static int End(IReadOnlyList<SourceAdmission.Token> t, int start,
        string a, string b)
    {
        if (start < 0 || start >= t.Count || t[start].Value != a) return -1;
        int depth = 0;
        for (int i=start; i<t.Count; i++)
        {
            if (t[i].Value == a) depth++;
            else if (t[i].Value == b && --depth == 0) return i;
        }
        return -1;
    }
    private static Block? Class(string s, string name)
    {
        var t=SourceAdmission.ReadTokens(s);
        Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="class" || t[i+1].Value!=name)continue;
            int open=i+2;
            while(open<t.Count && t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=End(t,open,"{","}");
            if(close<0||found is not null)return null;
            found=new(t[i].Start,t[open].Start,t[close].Start);
            i=close;
        }
        return found;
    }
    private static Block? Method(string s, Block owner, string name)
    {
        var t=SourceAdmission.ReadTokens(s);
        Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="func"||t[i+1].Value!=name||
               t[i+2].Value!="("||t[i].Start<=owner.Open||
               t[i].Start>=owner.Close)continue;
            int args=End(t,i+2,"(",")");if(args<0)return null;
            int open=args+1;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=End(t,open,"{","}");
            if(close<0||t[close].Start>owner.Close||found is not null)return null;
            found=new(t[i].Start,t[open].Start,t[close].Start);
            i=close;
        }
        return found;
    }

    private static Decision Rewrite(string s, string callbacks)
    {
        if(!Has(s,@"\bmodule\s+ShatteredChrome\b"))
            return new("REFUSED","SC_MODULE_MISSING",null,0);
        var owner=Class(s,"ShatteredChromeSystem");
        if(owner is null)return new("REFUSED","SC_SYSTEM_AMBIGUOUS",null,0);
        var ready=Method(s,owner,"OnSessionReady");
        var end=Method(s,owner,"OnSessionEnd");
        var detach=Method(s,owner,"OnDetach");
        var cancel=Method(s,owner,"CancelTick");
        var main=Method(s,owner,"OnTick");
        var hud=Method(s,owner,"OnHUDTick");
        var scheduleMain=Method(s,owner,"ScheduleNextTick");
        var scheduleHud=Method(s,owner,"ScheduleHUDTick");
        if(ready is null||end is null||detach is null||cancel is null||
           main is null||hud is null)
            return new("REFUSED","SC_REQUIRED_LIFECYCLE_MISSING",null,0);
        string Body(Block b)=>s[(b.Open+1)..b.Close];
        string readyBody=Body(ready), mainBody=Body(main),
            hudBody=Body(hud), cancelBody=Body(cancel);
        if(Has(callbacks,@"\bclass\s+SCUpdateJob\s+extends\s+ScheduledJob\b") ||
           Has(callbacks,@"\bclass\s+SCHUDTickJob\s+extends\s+ScheduledJob\b"))
        {
            return Has(callbacks,@"\bclass\s+SCUpdateJob\s+extends\s+ScheduledJob\b") &&
                   Has(callbacks,@"\bclass\s+SCHUDTickJob\s+extends\s+ScheduledJob\b") &&
                   Has(s,@"\bfunc\s+StartRuntimeJobs\s*\(") &&
                   Has(s,@"\bUnregister\s*\(")
                ?new("COMPLETE",null,null,0)
                :new("REFUSED","SC_HISTORICAL_JOBS_PARTIAL_UNPROVEN",null,0);
        }
        if(scheduleMain is null||scheduleHud is null ||
           !Has(Body(scheduleMain),@"\bSCUpdateCallback\b") ||
           !Has(Body(scheduleMain),@"\bDelayCallback\s*\(") ||
           !Has(Body(scheduleHud),@"\bSCHUDTickCallback\b") ||
           !Has(Body(scheduleHud),@"\bDelayCallback\s*\(") ||
           !Has(callbacks,@"\bclass\s+SCUpdateCallback\s+extends\s+DelayCallback\b") ||
           !Has(callbacks,@"\bclass\s+SCHUDTickCallback\s+extends\s+DelayCallback\b") ||
           !Has(readyBody,@"\bm_isSessionActive\s*=\s*true") ||
           !Has(Body(end),@"\bthis\s*\.\s*CancelTick\s*\(") ||
           !Has(Body(detach),@"\bthis\s*\.\s*CancelTick\s*\(") ||
           !Has(cancelBody,@"\bCancelCallback\s*\(\s*this\s*\.\s*m_updateDelayID") ||
           !Has(cancelBody,@"\bCancelCallback\s*\(\s*this\s*\.\s*m_hudDelayID") ||
           !Has(mainBody,@"\bGameInstance\s*\.\s*IsValid\s*\(") ||
           !Has(hudBody,@"\bSCHUD\s*\.\s*UpdateHUD\s*\("))
            return new("REFUSED","SC_SCHEDULING_FLOW_INCOMPATIBLE",null,0);

        var edits=new List<Edit>();
        void Add(int start,int endAt,string val)=>edits.Add(new(start,endAt,val));
        string nl=NL(s);
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var module=Regex.Match(s,@"\bmodule\s+ShatteredChrome\s*\r?\n");
            if(!module.Success)return new("REFUSED","SC_IMPORT_ANCHOR_MISSING",null,0);
            Add(module.Index+module.Length,module.Index+module.Length,
                "import GRedRuntime.*"+nl);
        }
        const string mainJob="SCGCOMaintenanceJob",hudJob="SCGCOHudJob";
        if(!Has(s,@"\bclass\s+SCGCOMaintenanceJob\b") &&
           !Has(s,@"\bclass\s+SCGCOHudJob\b"))
        {
            var classAnchor=Regex.Match(s,@"\b(?:public\s+)?class\s+ShatteredChromeSystem\b");
            if(!classAnchor.Success)return new("REFUSED","SC_CLASS_ANCHOR_MISSING",null,0);
            Add(classAnchor.Index,classAnchor.Index,string.Join(nl,new[]{
                "public class SCGCOMaintenanceJob extends ScheduledJob {",
                "  public let m_system: wref<ShatteredChromeSystem>;",
                "  public func Execute(runtime: ref<Runtime>) -> Void {",
                "    if IsDefined(this.m_system) { this.m_system.OnTick(); };",
                "  }",
                "}",
                "",
                "public class SCGCOHudJob extends ScheduledJob {",
                "  public let m_system: wref<ShatteredChromeSystem>;",
                "  public func Execute(runtime: ref<Runtime>) -> Void {",
                "    if IsDefined(this.m_system) { this.m_system.OnHUDTick(); };",
                "  }",
                "}",
                ""
            })+nl);
        }
        else if(!Has(s,@"\bclass\s+SCGCOMaintenanceJob\s+extends\s+ScheduledJob\b") ||
                !Has(s,@"\bclass\s+SCGCOHudJob\s+extends\s+ScheduledJob\b"))
            return new("REFUSED","SC_GCO_JOB_COLLISION",null,0);
        foreach(var field in new[]{
            ("m_gcoScheduler","wref<Scheduler>"),
            ("m_gcoMainJob","ref<SCGCOMaintenanceJob>"),
            ("m_gcoHudJob","ref<SCGCOHudJob>"),
            ("m_gcoMainID","Int32"),
            ("m_gcoHudID","Int32")})
        {
            var found=Regex.Matches(s,@"\blet\s+"+Regex.Escape(field.Item1)+
                @"\s*:\s*(?<type>[^;]+);");
            if(found.Count>1||(found.Count==1&&
                string.Concat(found[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
                string.Concat(field.Item2.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","SC_FIELD_COLLISION:"+field.Item1,null,0);
            if(found.Count==0)Add(owner.Open+1,owner.Open+1,
                nl+"  private let "+field.Item1+": "+field.Item2+";");
        }

        if(!Has(s,@"\bfunc\s+GCOStartJobs\s*\("))
        {
            string helper=string.Join(nl,new[]{
                "  private func GCOStartJobs() -> Void {",
                "    this.CancelTick();",
                "    let runtime = Runtime.Get(this.m_gameInstance);",
                "    if IsDefined(runtime) && runtime.IsReady() {",
                "      this.m_gcoScheduler = runtime.GetScheduler();",
                "      if IsDefined(this.m_gcoScheduler) {",
                "        this.m_gcoMainJob = new SCGCOMaintenanceJob();",
                "        this.m_gcoMainJob.m_system = this;",
                "        this.m_gcoMainJob.Configure(n\"SHATTERED_CHROME_MAIN\", SCTickInterval(), true);",
                "        this.m_gcoMainID = this.m_gcoScheduler.Register(this.m_gcoMainJob);",
                "        if this.m_gcoMainID <= 0 {",
                "          this.m_gcoMainJob = null;",
                "          this.ScheduleNextTick();",
                "        };",
                "        this.m_gcoHudJob = new SCGCOHudJob();",
                "        this.m_gcoHudJob.m_system = this;",
                "        this.m_gcoHudJob.Configure(n\"SHATTERED_CHROME_HUD\", SCHudTickInterval(), true);",
                "        this.m_gcoHudID = this.m_gcoScheduler.Register(this.m_gcoHudJob);",
                "        if this.m_gcoHudID <= 0 {",
                "          this.m_gcoHudJob = null;",
                "          this.ScheduleHUDTick();",
                "        };",
                "        return;",
                "      };",
                "    };",
                "    this.m_gcoScheduler = null;",
                "    this.m_gcoMainID = 0;",
                "    this.m_gcoHudID = 0;",
                "    this.ScheduleNextTick();",
                "    this.ScheduleHUDTick();",
                "  }",
                ""
            })+nl;
            Add(scheduleHud.Close+1,scheduleHud.Close+1,nl+helper);
        }
        else
        {
            var helper=Method(s,owner,"GCOStartJobs");
            if(helper is null ||
               !Has(Body(helper),@"\bthis\s*\.\s*ScheduleNextTick\s*\(") ||
               !Has(Body(helper),@"\bthis\s*\.\s*ScheduleHUDTick\s*\(") ||
               !Has(Body(helper),@"\bthis\s*\.\s*m_gcoScheduler\s*\.\s*Register\s*\("))
                return new("REFUSED","SC_START_JOBS_PARTIAL_UNPROVEN",null,0);
        }

        // Remove only original launch calls when replacing them with the
        // shared launcher; neither recurring callback body is removed.
        bool started=Has(readyBody,@"\bthis\s*\.\s*GCOStartJobs\s*\(");
        var originalMain=Regex.Matches(readyBody,@"\bthis\s*\.\s*ScheduleNextTick\s*\(\s*\)\s*;");
        var originalHud=Regex.Matches(readyBody,@"\bthis\s*\.\s*ScheduleHUDTick\s*\(\s*\)\s*;");
        if(!started && originalMain.Count==1 && originalHud.Count==1)
        {
            var a=originalMain[0],b=originalHud[0];
            Add(ready.Open+1+a.Index,ready.Open+1+a.Index+a.Length,
                "this.GCOStartJobs();");
            Add(ready.Open+1+b.Index,ready.Open+1+b.Index+b.Length,"");
        }
        else if(started && originalMain.Count==0 && originalHud.Count==0) { }
        else return new("REFUSED","SC_SESSION_LAUNCH_PARTIAL_OR_DRIFTED",null,0);

        // Add scheduler cleanup before original DelaySystem cancellation.
        if(!Has(cancelBody,@"\bthis\s*\.\s*m_gcoScheduler\s*\.\s*Unregister\s*\("))
        {
            Add(cancel.Open+1,cancel.Open+1,nl+string.Join(nl,new[]{
                "    if IsDefined(this.m_gcoScheduler) {",
                "      if this.m_gcoMainID > 0 { this.m_gcoScheduler.Unregister(this.m_gcoMainID); };",
                "      if this.m_gcoHudID > 0 { this.m_gcoScheduler.Unregister(this.m_gcoHudID); };",
                "    };",
                "    this.m_gcoMainID = 0;",
                "    this.m_gcoHudID = 0;",
                "    this.m_gcoMainJob = null;",
                "    this.m_gcoHudJob = null;",
                "    this.m_gcoScheduler = null;",
                ""
            }));
        }
        else if(!Has(cancelBody,@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoMainID")||
                !Has(cancelBody,@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoHudID")||
                !Has(cancelBody,@"\bm_gcoMainID\s*=\s*0")||
                !Has(cancelBody,@"\bm_gcoHudID\s*=\s*0"))
            return new("REFUSED","SC_JOBS_CLEANUP_PARTIAL_UNPROVEN",null,0);

        void Guard(Block method,string name,string id)
        {
            string body=Body(method);
            var all=Regex.Matches(body,@"\bthis\s*\.\s*"+Regex.Escape(name)+
                @"\s*\(\s*\)\s*;");
            var gated=Regex.Matches(body,@"if\s+this\s*\.\s*"+Regex.Escape(id)+
                @"\s*<=\s*0\s*\{\s*this\s*\.\s*"+Regex.Escape(name)+
                @"\s*\(\s*\)\s*;\s*\}\s*;");
            if(all.Count==0)throw new InvalidDataException("SC_EXPECTED_REQUEUE_MISSING:"+name);
            foreach(Match m in all)
            {
                bool hasGuard=gated.Cast<Match>().Any(g=>
                    m.Index>=g.Index&&m.Index+m.Length<=g.Index+g.Length);
                if(!hasGuard)Add(method.Open+1+m.Index,
                    method.Open+1+m.Index+m.Length,
                    "if this."+id+" <= 0 { this."+name+"(); };");
            }
        }
        Guard(main,"ScheduleNextTick","m_gcoMainID");
        Guard(hud,"ScheduleHUDTick","m_gcoHudID");

        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var ordered=edits.OrderByDescending(e=>e.Start).ThenByDescending(e=>e.End).ToArray();
        for(int i=1;i<ordered.Length;i++)
            if(ordered[i].End>ordered[i-1].Start)
                return new("REFUSED","SC_JOBS_OVERLAPPING_EDITS",null,0);
        string result=s;
        foreach(var e in ordered)result=result[..e.Start]+e.Text+result[e.End..];
        if(!Has(result,@"\bclass\s+SCGCOMaintenanceJob\s+extends\s+ScheduledJob\b")||
           !Has(result,@"\bclass\s+SCGCOHudJob\s+extends\s+ScheduledJob\b")||
           !Has(result,@"\bfunc\s+GCOStartJobs\s*\(")||
           !Has(result,@"\bm_gcoMainID\s*<=\s*0")||
           !Has(result,@"\bm_gcoHudID\s*<=\s*0")||
           !Has(result,@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoMainID")||
           !Has(result,@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoHudID"))
            throw new InvalidDataException("SC_JOBS_POSTCONDITION_FAILED");
        foreach(Match m in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!result.Contains(m.Value,StringComparison.Ordinal))
                throw new InvalidDataException("SC_JOBS_GENERIC_LOST");
        return new("APPLIED",null,result,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
           Inside(output,game))throw new InvalidDataException("SC_JOBS_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("SC_JOBS_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);_=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("SC_JOBS_HASH_MISSING");
        string status="NOT_APPLICABLE",reason=null!;
        string? relative=null,updated=null;
        int edits=0;
        string owner=Path.Combine(scripts,"ShatteredChrome");
        if(Directory.Exists(owner))
        {
            var files=Directory.EnumerateFiles(owner,"*.reds",SearchOption.AllDirectories).ToArray();
            var systems=files.Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                @"\bclass\s+ShatteredChromeSystem\s+extends\s+ScriptableSystem\b")).ToArray();
            var callbacks=files.Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                @"\bclass\s+SCUpdateCallback\b|\bclass\s+SCUpdateJob\b")).ToArray();
            if(systems.Length!=1||callbacks.Length!=1)
            {
                status="REFUSED";
                reason="SC_JOBS_SYSTEM_OR_CALLBACK_SOURCE_AMBIGUOUS";
            }
            else if(!NightCityRemembersTickPass.RuntimeAbi(scripts))
            {
                status="REFUSED";reason="SC_JOBS_RUNTIME_ABI_MISSING";
            }
            else
            {
                string file=systems[0],callbackFile=callbacks[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0||
                   (File.GetAttributes(callbackFile)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("SC_JOBS_SOURCE_LINK_UNSAFE");
                relative="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string src=File.ReadAllText(file,Encoding.UTF8);
                string cb=File.ReadAllText(callbackFile,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("SC_JOBS_PREVIEW_ESCAPE");
                    if(File.Exists(staged))src=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(src,cb);status=d.Status;reason=d.Reason!;
                updated=d.Changed;edits=d.Edits;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SC_JOBS_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(updated is not null&&relative is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SC_JOBS_TARGET_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst,updated,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,edits,status=="COMPLETE"?1:0,relative,
            reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}),
            new UTF8Encoding(false));
        return result;
    }
    private static bool Inside(string child,string root)=>
        Path.GetFullPath(child).StartsWith(Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
