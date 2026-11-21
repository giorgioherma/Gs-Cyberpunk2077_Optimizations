using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.2 GRS interaction poll: reuse framework Scheduler if
/// available, retaining the original one-shot DelayCallback as an exclusive
/// fallback. The original guard spawn, distance check, prompt and recovery
/// behavior are not moved or duplicated.
/// </summary>
internal static class GearRecoveryPollingPass
{
    internal const string Rule="GEAR_RECOVERY_POLL_SCHEDULER_FALLBACK_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Block(int Start,int Open,int Close);
    private sealed record Edit(int Start,int End,string Text);
    private sealed record Decision(string Status,string? Reason,string? Changed,int Edits);
    private static bool Has(string s,string p)=>Regex.IsMatch(s,p,RegexOptions.CultureInvariant);
    private static string NL(string s)=>s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static int End(IReadOnlyList<SourceAdmission.Token> t,int start,string a,string b)
    {
        if(start<0||start>=t.Count||t[start].Value!=a)return -1;
        int n=0;
        for(int i=start;i<t.Count;i++)
        {
            if(t[i].Value==a)n++;
            else if(t[i].Value==b&&--n==0)return i;
        }
        return -1;
    }
    private static Block? Class(string s,string name)
    {
        var t=SourceAdmission.ReadTokens(s);Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="class"||t[i+1].Value!=name)continue;
            int o=i+2;while(o<t.Count&&t[o].Value is not ("{" or ";"))o++;
            if(o==t.Count||t[o].Value!="{")return null;
            int e=End(t,o,"{","}");
            if(e<0||found is not null)return null;
            found=new(t[i].Start,t[o].Start,t[e].Start);i=e;
        }
        return found;
    }
    private static Block? Method(string s,Block owner,string name)
    {
        var t=SourceAdmission.ReadTokens(s);Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Start<=owner.Open||t[i].Start>=owner.Close||
               t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")continue;
            int a=End(t,i+2,"(",")");if(a<0)return null;
            int o=a+1;while(o<t.Count&&t[o].Value is not ("{" or ";"))o++;
            if(o==t.Count||t[o].Value!="{")return null;
            int e=End(t,o,"{","}");
            if(e<0||t[e].Start>owner.Close||found is not null)return null;
            found=new(t[i].Start,t[o].Start,t[e].Start);i=e;
        }
        return found;
    }

    private static Decision Rewrite(string s)
    {
        if(!Has(s,@"\bmodule\s+GearRecoverySystem\b"))
            return new("REFUSED","GRS_POLL_MODULE_MISSING",null,0);
        var owner=Class(s,"GRSInteractionController");
        if(owner is null)return new("REFUSED","GRS_POLL_OWNER_AMBIGUOUS",null,0);
        var queue=Method(s,owner,"QueuePoll");
        var tick=Method(s,owner,"OnPollTick");
        var stop=Method(s,owner,"StopPollingAndClearPrompt");
        var detach=Method(s,owner,"OnDetach");
        if(queue is null||tick is null||stop is null||detach is null)
            return new("REFUSED","GRS_POLL_METHODS_MISSING",null,0);
        string Body(Block b)=>s[(b.Open+1)..b.Close];
        string q=Body(queue),t=Body(tick),cleanup=Body(stop),d=Body(detach);
        if(Has(s,@"\bclass\s+GRSInteractionPollJob\s+extends\s+ScheduledJob\b") &&
           !Has(s,@"\bclass\s+GRSGCOInteractionPollJob\b"))
            return Has(q,@"\bpollJobID\b") && Has(cleanup,@"\bUnregister\s*\(")
                ?new("COMPLETE",null,null,0)
                :new("REFUSED","GRS_HISTORICAL_POLL_JOB_PARTIAL_UNPROVEN",null,0);
        if(!Has(s,@"\bclass\s+GRSInteractionPollCallback\s+extends\s+DelayCallback\b")||
           !Has(q,@"\bGRS_ConfigInteractionPollInterval\s*\(")||
           !Has(q,@"\bIsCacheReadyForInteraction\s*\(")||
           !Has(t,@"\bGRSRecoverySystem\s*\.\s*Get\s*\(")||
           !Has(t,@"\bTrySpawnRecoveryGuards\s*\(")||
           !Has(t,@"\bTryAlertRecoveryGuards\s*\(")||
           !Has(t,@"\bthis\s*\.\s*QueuePoll\s*\(")||
           !Has(t,@"\bthis\s*\.\s*StopPollingAndClearPrompt\s*\(")||
           !Has(cleanup,@"\bthis\s*\.\s*CancelVanillaBreach\s*\(")||
           !Has(d,@"\bStopVanillaBreachListener\s*\("))
            return new("REFUSED","GRS_POLL_OWNER_BEHAVIOR_UNPROVEN",null,0);
        string nl=NL(s);
        var edits=new List<Edit>();
        void Add(int a,int b,string value)=>edits.Add(new(a,b,value));
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var module=Regex.Match(s,@"\bmodule\s+GearRecoverySystem\s*\r?\n");
            if(!module.Success)return new("REFUSED","GRS_POLL_IMPORT_ANCHOR_MISSING",null,0);
            Add(module.Index+module.Length,module.Index+module.Length,"import GRedRuntime.*"+nl);
        }
        if(!Has(s,@"\bclass\s+GRSGCOInteractionPollJob\b"))
        {
            var cls=Regex.Match(s,@"\b(?:public\s+)?class\s+GRSInteractionController\b");
            if(!cls.Success)return new("REFUSED","GRS_POLL_CLASS_ANCHOR_MISSING",null,0);
            Add(cls.Index,cls.Index,string.Join(nl,new[]{
                "public class GRSGCOInteractionPollJob extends ScheduledJob {",
                "  public let controller: wref<GRSInteractionController>;",
                "  public func Execute(runtime: ref<Runtime>) -> Void {",
                "    if IsDefined(this.controller) { this.controller.OnPollTick(); };",
                "  }",
                "}",
                ""
            })+nl);
        }
        else if(!Has(s,@"\bclass\s+GRSGCOInteractionPollJob\s+extends\s+ScheduledJob\b"))
            return new("REFUSED","GRS_POLL_JOB_COLLISION",null,0);
        foreach(var field in new[]{
            ("m_gcoGrsPollScheduler","wref<Scheduler>"),
            ("m_gcoGrsPollJob","ref<GRSGCOInteractionPollJob>"),
            ("m_gcoGrsPollID","Int32")})
        {
            var existing=Regex.Matches(s,@"\blet\s+"+Regex.Escape(field.Item1)+
                @"\s*:\s*(?<type>[^;]+);");
            if(existing.Count>1||(existing.Count==1&&
               string.Concat(existing[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
               string.Concat(field.Item2.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","GRS_POLL_FIELD_COLLISION:"+field.Item1,null,0);
            if(existing.Count==0)
                Add(owner.Open+1,owner.Open+1,nl+"  private let "+field.Item1+": "+field.Item2+";");
        }

        var original=Regex.Match(q,
            @"\bthis\s*\.\s*pollQueued\s*=\s*true\s*;\s*let\s+(?<cb>[A-Za-z_]\w*)\s*=\s*new\s+GRSInteractionPollCallback\s*\(\s*\)\s*;\s*\k<cb>\s*\.\s*controller\s*=\s*this\s*;\s*GameInstance\s*\.\s*GetDelaySystem\s*\(\s*GetGameInstance\s*\(\s*\)\s*\)\s*\.\s*DelayCallback\s*\(\s*\k<cb>\s*,\s*GRS_ConfigInteractionPollInterval\s*\(\s*\)\s*,\s*false\s*\)\s*;",
            RegexOptions.Singleline);
        if(!Has(q,@"\bm_gcoGrsPollID\b"))
        {
            if(!original.Success)
                return new("REFUSED","GRS_ORIGINAL_CALLBACK_CHAIN_UNPROVEN",null,0);
            string registration=string.Join(nl,new[]{
                "let grsRuntime = Runtime.Get(GetGameInstance());",
                "        if IsDefined(grsRuntime) && grsRuntime.IsReady() {",
                "          let grsScheduler = grsRuntime.GetScheduler();",
                "          if IsDefined(grsScheduler) {",
                "            let grsJob = new GRSGCOInteractionPollJob();",
                "            grsJob.controller = this;",
                "            grsJob.Configure(n\"GRS_INTERACTION\", GRS_ConfigInteractionPollInterval(), true);",
                "            let grsID = grsScheduler.Register(grsJob);",
                "            if grsID > 0 {",
                "              this.m_gcoGrsPollScheduler = grsScheduler;",
                "              this.m_gcoGrsPollJob = grsJob;",
                "              this.m_gcoGrsPollID = grsID;",
                "              this.pollQueued = true;",
                "              return;",
                "            };",
                "          };",
                "        };",
                "        "+original.Value
            });
            Add(queue.Open+1+original.Index,queue.Open+1+original.Index+original.Length,registration);
        }
        else if(!original.Success ||
                !Has(q,@"\bgrsScheduler\s*\.\s*Register\s*\(") ||
                !Has(q,@"\bGRSGCOInteractionPollJob\b"))
            return new("REFUSED","GRS_POLL_REGISTRATION_PARTIAL_UNPROVEN",null,0);

        var guard=Regex.Match(q,@"\bif\s+this\s*\.\s*pollQueued\s*\{\s*return\s*;\s*\}");
        if(guard.Success)
            Add(queue.Open+1+guard.Index,queue.Open+1+guard.Index+guard.Length,
                "if this.pollQueued || this.m_gcoGrsPollID > 0 { return; }");
        else if(!Has(q,@"\bif\s+this\s*\.\s*pollQueued\s*\|\|\s*this\s*\.\s*m_gcoGrsPollID\s*>\s*0"))
            return new("REFUSED","GRS_POLL_QUEUE_GUARD_CHANGED",null,0);

        var reset=Regex.Match(t,@"\bthis\s*\.\s*pollQueued\s*=\s*false\s*;");
        var gatedReset=Regex.Match(t,@"\bif\s+this\s*\.\s*m_gcoGrsPollID\s*<=\s*0\s*\{\s*this\s*\.\s*pollQueued\s*=\s*false\s*;\s*\}\s*;");
        if(!gatedReset.Success && reset.Success)
            Add(tick.Open+1+reset.Index,tick.Open+1+reset.Index+reset.Length,
                "if this.m_gcoGrsPollID <= 0 { this.pollQueued = false; };");
        else if(!gatedReset.Success)
            return new("REFUSED","GRS_POLL_TICK_RESET_CHANGED",null,0);

        var requeue=Regex.Match(t,@"\bif\s+shouldContinue\s*\{\s*this\s*\.\s*QueuePoll\s*\(\s*\)\s*;");
        if(requeue.Success)
            Add(tick.Open+1+requeue.Index,tick.Open+1+requeue.Index+requeue.Length,
                "if shouldContinue { if this.m_gcoGrsPollID <= 0 { this.QueuePoll(); };");
        else if(!Has(t,@"\bif\s+shouldContinue\s*\{\s*if\s+this\s*\.\s*m_gcoGrsPollID\s*<=\s*0"))
            return new("REFUSED","GRS_POLL_REQUEUE_CHANGED",null,0);

        if(!Has(cleanup,@"\bthis\s*\.\s*m_gcoGrsPollScheduler\s*\.\s*Unregister\s*\("))
            Add(stop.Open+1,stop.Open+1,nl+string.Join(nl,new[]{
                "    if IsDefined(this.m_gcoGrsPollScheduler) && this.m_gcoGrsPollID > 0 {",
                "      this.m_gcoGrsPollScheduler.Unregister(this.m_gcoGrsPollID);",
                "    };",
                "    this.m_gcoGrsPollID = 0;",
                "    this.m_gcoGrsPollJob = null;",
                "    this.m_gcoGrsPollScheduler = null;",
                ""
            }));
        else if(!Has(cleanup,@"\bm_gcoGrsPollJob\s*=\s*null")||
                !Has(cleanup,@"\bm_gcoGrsPollID\s*=\s*0"))
            return new("REFUSED","GRS_POLL_CLEANUP_PARTIAL_UNPROVEN",null,0);
        if(!Has(d,@"\bthis\s*\.\s*StopPollingAndClearPrompt\s*\("))
            Add(detach.Open+1,detach.Open+1,nl+"    this.StopPollingAndClearPrompt();");

        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var ordered=edits.OrderByDescending(x=>x.Start).ThenByDescending(x=>x.End).ToArray();
        for(int i=1;i<ordered.Length;i++)
            if(ordered[i].End>ordered[i-1].Start)
                return new("REFUSED","GRS_POLL_EDIT_OVERLAP",null,0);
        string output=s;
        foreach(var e in ordered)output=output[..e.Start]+e.Text+output[e.End..];
        if(!Has(output,@"\bclass\s+GRSGCOInteractionPollJob\s+extends\s+ScheduledJob\b")||
           !Has(output,@"\bm_gcoGrsPollID\s*>\s*0")||
           !Has(output,@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoGrsPollID")||
           !Has(output,@"\bthis\s*\.\s*StopPollingAndClearPrompt\s*\(")||
           !Has(output,@"\bthis\s*\.\s*QueuePoll\s*\("))
            throw new InvalidDataException("GRS_POLL_POSTCONDITION_FAILED");
        foreach(Match m in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(m.Value,StringComparison.Ordinal))
                throw new InvalidDataException("GRS_POLL_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||
           File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("GRS_POLL_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null&&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("GRS_POLL_OVERLAY_UNSAFE");
        var admit=new SourceAdmission(game);_=admit.AuditAll();
        string hash=admit.SourceStackSha256??
            throw new InvalidDataException("GRS_POLL_HASH_MISSING");
        string status="NOT_APPLICABLE";string? reason=null,relative=null,rewritten=null;
        int edits=0;string owner=Path.Combine(scripts,"GearRecoverySystem");
        if(Directory.Exists(owner))
        {
            var found=Directory.EnumerateFiles(owner,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+GRSInteractionController\s+extends\s+ScriptableSystem\b"))
                .ToArray();
            if(found.Length!=1)
            {
                status="REFUSED";reason=found.Length==0?
                    "GRS_POLL_OWNER_MISSING":"GRS_POLL_OWNER_AMBIGUOUS";
            }
            else if(!NightCityRemembersTickPass.RuntimeAbi(scripts))
            {
                status="REFUSED";reason="GRS_POLL_RUNTIME_ABI_MISSING";
            }
            else
            {
                string file=found[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("GRS_POLL_SOURCE_LINK_UNSAFE");
                relative="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string src=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("GRS_POLL_PREVIEW_ESCAPE");
                    if(File.Exists(staged))src=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(src);status=d.Status;reason=d.Reason;
                rewritten=d.Changed;edits=d.Edits;
            }
        }
        if(!string.Equals(hash,admit.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GRS_POLL_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(rewritten is not null&&relative is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GRS_POLL_TARGET_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst,rewritten,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,edits,status=="COMPLETE"?1:0,
            relative,reason,true,false);
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
