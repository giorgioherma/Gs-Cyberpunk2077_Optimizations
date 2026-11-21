using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.2 VehicleMileage: four independently admitted owner-local
/// repeating jobs. The original DelayCallbacks remain fallback paths, including
/// token/generation invalidation, high-frequency boot animation and dynamic
/// fast/slow wake behavior. Never copy historical full source files.
/// </summary>
internal static class VehicleMileageSchedulerPass
{
    internal const string Rule = "VEHICLE_MILEAGE_FOUR_SCHEDULER_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string[] Refusals,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Spec(string Owner, string ClassName, string OldCallback,
        string Arm, string Work, string Job, string RefField, string JobAction,
        string Period, string PlayerField, string Armed, bool Variable);
    private static readonly Spec[] Specs =
    [
        new("3d-odo","VM3DOdoService","VM3DOdoTick","ArmTick","Tick",
            "VMGCO3DOdoJob","svc","VM_3D_ODO","this.tickPeriod",
            "vm3dOdoSvc","tickArmed",false),
        new("fuel-available","VMFuelAvailableService","VMFuelAvailableTick","ArmTick","Tick",
            "VMGCOFuelAvailableJob","service","VM_FUEL_AVAILABLE","period",
            "vmFuelAvailableService","tickArmed",true),
        new("fuel-gauge","VM_FuelGauge","VM_FuelGaugeTick","ArmNextTick","Refresh",
            "VMGCOFuelGaugeJob","hud","VM_FUEL_GAUGE","this.period",
            "","__armed",false),
        new("price-sign","VMPriceSignService","VMPriceSignTick","ArmTick","Tick",
            "VMGCOPriceSignJob","svc","VM_PRICE_SIGN","period",
            "vmPriceSignSvc","tickArmed",true)
    ];
    private sealed record Block(int Start, int Open, int Close);
    private sealed record Edit(int Start, int End, string Replacement);
    private sealed record Decision(string Status, string? Reason, string? Changed, int Edits);
    private static bool Has(string s, string rx) =>
        Regex.IsMatch(s, rx, RegexOptions.CultureInvariant);
    private static string NL(string s) =>
        s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private static int Match(IReadOnlyList<SourceAdmission.Token> t, int start,
        string opening, string closing)
    {
        if(start<0||start>=t.Count||t[start].Value!=opening)return -1;
        int depth=0;
        for(int i=start;i<t.Count;i++)
        {
            if(t[i].Value==opening)depth++;
            else if(t[i].Value==closing&&--depth==0)return i;
        }
        return -1;
    }
    private static Block? FindClass(string s, string name)
    {
        var t=SourceAdmission.ReadTokens(s);
        Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="class"||t[i+1].Value!=name)continue;
            int open=i+2;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=Match(t,open,"{","}");
            if(close<0||found is not null)return null;
            found=new(t[i].Start,t[open].Start,t[close].Start);i=close;
        }
        return found;
    }
    private static Block? FindMethod(string s, Block owner, string name)
    {
        var t=SourceAdmission.ReadTokens(s);
        Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Start<=owner.Open||t[i].Start>=owner.Close||
               t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")continue;
            int args=Match(t,i+2,"(",")");if(args<0)return null;
            int open=args+1;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=Match(t,open,"{","}");
            if(close<0||t[close].Start>owner.Close||found is not null)return null;
            found=new(t[i].Start,t[open].Start,t[close].Start);i=close;
        }
        return found;
    }
    private static string GetBody(string source, Block b) =>
        source[(b.Open+1)..b.Close];

    private static Decision Rewrite(string s, Spec spec)
    {
        Block? owner=FindClass(s,spec.ClassName);
        if(owner is null)return new("REFUSED","VM_OWNER_CLASS_MISSING_OR_AMBIGUOUS",null,0);
        var arm=FindMethod(s,owner,spec.Arm);
        var work=FindMethod(s,owner,spec.Work);
        if(arm is null||work is null)return new("REFUSED","VM_ARM_OR_TICK_MISSING",null,0);
        string armBody=GetBody(s,arm), workBody=GetBody(s,work);
        if(!Has(s,@"\bclass\s+"+Regex.Escape(spec.OldCallback)+
              @"\s+extends\s+DelayCallback\b") ||
           !Has(armBody,@"\bDelayCallback\s*\(") ||
           !Has(armBody,Regex.Escape(spec.OldCallback)+@"\s*\.\s*Create\s*\(") ||
           !Has(workBody,@"\bthis\s*\.\s*"+Regex.Escape(spec.Arm)+@"\s*\(") &&
             !spec.Owner.Equals("fuel-gauge",StringComparison.Ordinal))
            return new("REFUSED","VM_ORIGINAL_DELAY_CALLBACK_FLOW_UNPROVEN",null,0);

        var edits=new List<Edit>();
        string nl=NL(s);
        void Add(int start,int end,string text)=>edits.Add(new(start,end,text));
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var header=Regex.Match(s,@"\bmodule\s+[A-Za-z0-9_.]+\s*\r?\n");
            if(!header.Success)return new("REFUSED","VM_MODULE_IMPORT_ANCHOR_MISSING",null,0);
            Add(header.Index+header.Length,header.Index+header.Length,
                "import GRedRuntime.*"+nl);
        }

        // New job class is separate and add-only. It never changes or deletes
        // the current owner DelayCallback class.
        bool jobPresent=Has(s,@"\bclass\s+"+Regex.Escape(spec.Job)+
            @"\s+extends\s+ScheduledJob\b");
        if(!jobPresent)
        {
            if(Has(s,@"\bclass\s+"+Regex.Escape(spec.Job)+@"\b"))
                return new("REFUSED","VM_JOB_NAME_COLLISION",null,0);
            var cls=Regex.Match(s,@"\b(?:public\s+)?class\s+"+
                Regex.Escape(spec.ClassName)+@"\b");
            if(!cls.Success)return new("REFUSED","VM_CLASS_INSERTION_ANCHOR_MISSING",null,0);
            string execution=spec.Owner=="fuel-gauge" ?
                "this.hud.Refresh(); this.hud.ArmNextTick();" :
                "this."+spec.RefField+"."+spec.Work+"();";
            string job=string.Join(nl,new[]
            {
                "public class "+spec.Job+" extends ScheduledJob {",
                "  public let "+spec.RefField+": wref<"+spec.ClassName+">;",
                "  public func Execute(runtime: ref<Runtime>) -> Void {",
                "    if IsDefined(this."+spec.RefField+") { "+execution+" };",
                "  }",
                "}",
                ""
            })+nl;
            Add(cls.Index,cls.Index,job);
        }
        string classBody=GetBody(s,owner);
        foreach((string name,string type) in new[]
        {
            ("m_gcoVMScheduler","wref<Scheduler>"),
            ("m_gcoVMJob","ref<"+spec.Job+">"),
            ("m_gcoVMJobID","Int32")
        })
        {
            var found=Regex.Matches(classBody,@"\blet\s+"+Regex.Escape(name)+
                @"\s*:\s*(?<type>[^;]+);");
            if(found.Count>1||(found.Count==1&&
               string.Concat(found[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
               string.Concat(type.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","VM_FIELD_COLLISION:"+name,null,0);
            if(found.Count==0)
                Add(owner.Open+1,owner.Open+1,nl+"  private let "+name+": "+type+";");
        }

        var stop=FindMethod(s,owner,"GCOStopVMJob");
        if(stop is null)
        {
            if(Has(classBody,@"\bfunc\s+GCOStopVMJob\b"))
                return new("REFUSED","VM_STOP_HELPER_COLLISION",null,0);
            string method=string.Join(nl,new[]
            {
                "  public func GCOStopVMJob() -> Void {",
                "    if IsDefined(this.m_gcoVMScheduler) && this.m_gcoVMJobID > 0 {",
                "      this.m_gcoVMScheduler.Unregister(this.m_gcoVMJobID);",
                "    };",
                "    this.m_gcoVMJobID = 0;",
                "    this.m_gcoVMJob = null;",
                "    this.m_gcoVMScheduler = null;",
                "    this."+spec.Armed+" = false;",
                spec.Variable?"    this.tickToken += 1;":"",
                spec.Owner=="fuel-gauge"?"    this.callbackGeneration += 1;":"",
                spec.Owner=="3d-odo"?"    this.HideLastWidget();":"",
                spec.Owner=="fuel-available"?"    ArrayClear(this.targets);":"",
                spec.Owner=="price-sign"?"    ArrayClear(this.targetWidgets);":"",
                spec.Owner=="fuel-gauge"?"": 
                    "    if this.registered && IsDefined(this.callbackSystem) {",
                spec.Owner=="fuel-gauge"?"": 
                    "      this.callbackSystem.UnregisterCallback(n\"Entity/Assemble\", this, n\""+
                    (spec.Owner=="3d-odo"?"OnVehicleAssemble":"OnEntityAssemble")+"\");",
                spec.Owner=="fuel-gauge"?"":"    };",
                spec.Owner=="fuel-gauge"?"":"    this.registered = false;",
                spec.Owner=="fuel-gauge"?"":"    this.callbackSystem = null;",
                "  }",
                ""
            }.Where(x=>x.Length>0))+nl;
            Add(arm.Close+1,arm.Close+1,nl+method);
        }
        else if(!Has(GetBody(s,stop),@"\bUnregister\s*\(\s*this\s*\.\s*m_gcoVMJobID\s*\)") ||
                !Has(GetBody(s,stop),@"\bm_gcoVMJobID\s*=\s*0"))
            return new("REFUSED","VM_STOP_HELPER_PARTIAL_UNPROVEN",null,0);

        // All scheduling edits are confined to the existing owner's arm
        // function. When Scheduler is absent or admission fails, continue
        // into the exact original DelaySystem branch unchanged.
        string registerKey="vmScheduler.Register(vmJob)";
        if(!armBody.Contains(registerKey,StringComparison.Ordinal))
        {
            if(Has(armBody,@"\bm_gcoVMJobID\b"))
                return new("REFUSED","VM_REGISTER_PARTIAL_UNPROVEN",null,0);
            string first=spec.Variable ?
                string.Join(nl,new[]
                {
                    "    if this.m_gcoVMJobID > 0 && IsDefined(this.m_gcoVMScheduler) {",
                    "      this.m_gcoVMScheduler.RescheduleJob(this.m_gcoVMJobID, period, period);",
                    "      return;",
                    "    };"
                }) :
                "    if this.m_gcoVMJobID > 0 { return; };";
            string injected=first+nl+string.Join(nl,new[]
            {
                "    let vmRuntime = Runtime.Get(GetGameInstance());",
                "    if IsDefined(vmRuntime) && vmRuntime.IsReady() {",
                "      let vmScheduler = vmRuntime.GetScheduler();",
                "      if IsDefined(vmScheduler) {",
                "        let vmJob = new "+spec.Job+"();",
                "        vmJob."+spec.RefField+" = this;",
                "        vmJob.Configure(n\""+spec.JobAction+"\", "+spec.Period+", true);",
                "        let vmID = vmScheduler.Register(vmJob);",
                "        if vmID > 0 {",
                "          this.m_gcoVMScheduler = vmScheduler;",
                "          this.m_gcoVMJob = vmJob;",
                "          this.m_gcoVMJobID = vmID;",
                "          this."+spec.Armed+" = true;",
                "          return;",
                "        };",
                "      };",
                "    };",
                ""
            })+nl;
            Add(arm.Open+1,arm.Open+1,nl+injected);
        }

        // Existing fast-wake code invalidates stale DelayCallback tokens.
        // For a registered Scheduler job, pull the same job forward instead
        // of invalidating the token and creating another delay callback.
        if(spec.Variable)
        {
            var request=FindMethod(s,owner,"RequestFastTicks");
            if(request is null)return new("REFUSED","VM_FAST_WAKE_METHOD_MISSING",null,0);
            string requestBody=GetBody(s,request);
            var tokenGuard=Regex.Match(requestBody,
                @"\bif\s+this\s*\.\s*tickArmed\s*\{");
            if(!Has(requestBody,@"\bthis\s*\.\s*ArmTick\s*\(\s*0\.01\s*\)") ||
               !Has(requestBody,@"\bthis\s*\.\s*tickToken\s*\+=\s*1") ||
               !tokenGuard.Success)
                return new("REFUSED","VM_FAST_WAKE_INVARIANT_MISSING",null,0);
            if(!Has(requestBody,@"\bm_gcoVMJobID\s*>\s*0"))
                Add(request.Open+1+tokenGuard.Index,
                    request.Open+1+tokenGuard.Index,
                    "if this.m_gcoVMJobID > 0 { this.ArmTick(0.05); return; };"+nl+"    ");
        }

        // Gauge's callbackGeneration is the existing world-invalidation
        // mechanism. Unregister the repeating job before generation rotates.
        if(spec.Owner=="fuel-gauge")
        {
            var world=FindMethod(s,owner,"OnNewWorld");
            if(world is null)return new("REFUSED","VM_GAUGE_WORLD_RESET_MISSING",null,0);
            string body=GetBody(s,world);
            var gen=Regex.Match(body,@"\bthis\s*\.\s*callbackGeneration\s*\+=\s*1\s*;");
            if(!gen.Success)return new("REFUSED","VM_GAUGE_GENERATION_GUARD_MISSING",null,0);
            if(!Has(body,@"\bthis\s*\.\s*GCOStopVMJob\s*\("))
                Add(world.Open+1+gen.Index,world.Open+1+gen.Index,
                    "this.GCOStopVMJob();"+nl+"    ");
        }

        // Existing player lifecycle always owns the service or the gauge
        // UISystem field. No new resident listeners or timers are installed.
        string detachWitness=spec.PlayerField.Length==0 ?
            "let vmUI = GameInstance.GetUISystem(this.GetGame());"+
            nl+"  if IsDefined(vmUI) && IsDefined(vmUI.vmFuelGauge) { vmUI.vmFuelGauge.GCOStopVMJob(); };":
            "if IsDefined(this."+spec.PlayerField+") { this."+
                spec.PlayerField+".GCOStopVMJob(); };";
        // Match the owner's actual field in the detach wrapper; a different
        // VehicleMileage owner's cleanup is not equivalent.
        if(!Has(s,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)[\s\S]{0,800}"+
            Regex.Escape(spec.PlayerField.Length==0?"vmFuelGauge":spec.PlayerField)+
            @"\s*\.\s*GCOStopVMJob\s*\("))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]
            {
                "@wrapMethod(PlayerPuppet)",
                "protected cb func OnDetach() -> Bool {",
                "  "+detachWitness,
                "  return wrappedMethod();",
                "}",
                ""
            }));
        }

        if(edits.Count==0)
            return new("COMPLETE",null,null,0);
        var sorted=edits.OrderByDescending(x=>x.Start).ThenByDescending(x=>x.End).ToArray();
        for(int i=1;i<sorted.Length;i++)
            if(sorted[i].End>sorted[i-1].Start)
                return new("REFUSED","VM_OVERLAPPING_EDITS",null,0);
        string output=s;
        foreach(var e in sorted)
            output=output[..e.Start]+e.Replacement+output[e.End..];
        if(!Has(output,@"\bclass\s+"+Regex.Escape(spec.Job)+
                @"\s+extends\s+ScheduledJob\b") ||
           !Has(output,@"\bthis\s*\.\s*m_gcoVMScheduler\s*\.\s*Unregister\s*\(") ||
           !Has(output,@"\bvmScheduler\s*\.\s*Register\s*\(\s*vmJob\s*\)") ||
           !Has(output,@"\bthis\s*\.\s*m_gcoVMJob\s*=\s*vmJob\s*;") ||
           !Has(output,@"\b"+Regex.Escape(spec.OldCallback)+@"\b"))
            throw new InvalidDataException("VM_SCHEDULER_POSTCONDITION_FAILED");
        foreach(Match marker in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(marker.Value,StringComparison.Ordinal))
                throw new InvalidDataException("VM_SCHEDULER_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||
           File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("VM_SCHEDULER_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("VM_SCHEDULER_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);_=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("VM_SCHEDULER_SOURCE_HASH_MISSING");
        var paths=new List<string>();var refused=new List<string>();
        int edits=0,complete=0,owners=0;
        string root=Path.Combine(scripts,"VehicleMileage");
        bool runtime=NightCityRemembersTickPass.RuntimeAbi(scripts);
        bool reschedule=Directory.EnumerateFiles(scripts,"*.reds",SearchOption.AllDirectories)
            .Any(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                @"\bmodule\s+GRedRuntime\b") &&
                Has(File.ReadAllText(f,Encoding.UTF8),
                @"\bfunc\s+RescheduleJob\s*\("));
        if(Directory.Exists(root))
        {
            var sources=Directory.EnumerateFiles(root,"*.reds",SearchOption.AllDirectories).ToArray();
            foreach(var spec in Specs)
            {
                var found=sources.Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+"+Regex.Escape(spec.ClassName)+@"\s+extends\s+IScriptable\b")).ToArray();
                if(found.Length==0)continue;
                owners++;
                if(found.Length!=1)
                {
                    refused.Add(spec.Owner+":VM_OWNER_AMBIGUOUS");continue;
                }
                if(!runtime||(spec.Variable&&!reschedule))
                {
                    refused.Add(spec.Owner+":VM_SCHEDULER_ABI_MISSING");continue;
                }
                string file=found[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("VM_SOURCE_LINK_UNSAFE");
                string rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string source=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("VM_PREVIEW_ESCAPE");
                    if(File.Exists(staged))source=File.ReadAllText(staged,Encoding.UTF8);
                }
                var decision=Rewrite(source,spec);
                if(decision.Status=="REFUSED")
                {
                    refused.Add(spec.Owner+":"+(decision.Reason??"VM_INCOMPATIBLE"));continue;
                }
                if(decision.Status=="COMPLETE"){complete++;continue;}
                if(decision.Changed is null)
                    throw new InvalidDataException("VM_APPLIED_WITHOUT_SOURCE");
                string target=Path.GetFullPath(Path.Combine(output,
                    rel.Replace('/',Path.DirectorySeparatorChar)));
                if(!Inside(target,output)||!target.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("VM_OUTPUT_ESCAPE");
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target,decision.Changed,new UTF8Encoding(false));
                paths.Add(rel);edits+=decision.Edits;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("VM_SCHEDULER_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        string status=paths.Count>0?"APPLIED":
            refused.Count>0?"REFUSED":
            owners>0&&complete>0?"COMPLETE":"NOT_APPLICABLE";
        var result=new Result(Rule,status,edits,complete,paths.ToArray(),
            refused.ToArray(),true,false);
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
