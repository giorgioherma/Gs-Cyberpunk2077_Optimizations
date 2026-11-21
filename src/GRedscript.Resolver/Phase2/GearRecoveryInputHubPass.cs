using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.1 GearRecoverySystem: one GRS recover action routed by
/// InputHub when admitted, otherwise the original engine listener. Both
/// ingress paths call one owner-local recovery routine, preserving success
/// consumption and every original in-range/gameplay check.
/// </summary>
internal static class GearRecoveryInputHubPass
{
    internal const string Rule = "GEAR_RECOVERY_INPUT_HUB_FALLBACK_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Block(int Start,int Open,int Close);
    private sealed record Edit(int Start,int End,string Value);
    private sealed record Decision(string Status,string? Reason,string? Changed,int Edits);
    private static string NL(string s)=>s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static bool Has(string s,string regex)=>Regex.IsMatch(s,regex,RegexOptions.CultureInvariant);
    private static int End(IReadOnlyList<SourceAdmission.Token> t,int idx,string a,string b)
    {
        if(idx<0||idx>=t.Count||t[idx].Value!=a)return -1;
        int d=0;
        for(int i=idx;i<t.Count;i++){
            if(t[i].Value==a)d++;
            else if(t[i].Value==b && --d==0)return i;
        }
        return -1;
    }
    private static Block? Method(string s,string name,string witness,string? annotation=null)
    {
        var t=SourceAdmission.ReadTokens(s);Block? result=null;
        for(int i=0;i+2<t.Count;i++){
            if(t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")continue;
            int close=End(t,i+2,"(",")");if(close<0)return null;
            string sig=string.Join(" ",t.Skip(i+3).Take(close-i-3).Select(x=>x.Value));
            if(!sig.Contains(witness,StringComparison.Ordinal))continue;
            if(annotation is not null){
                string prev=s[Math.Max(0,t[i].Start-230)..t[i].Start];
                if(!Has(prev,@"@wrapMethod\s*\(\s*"+Regex.Escape(annotation)+
                    @"\s*\)\s*(?:(?:protected|public|private|cb|final)\s+)*$"))continue;
            }
            int open=close+1;while(open<t.Count&&t[open].Value is not("{" or ";"))open++;
            if(open==t.Count||t[open].Value!="{")return null;
            int end=End(t,open,"{","}");if(end<0||result is not null)return null;
            result=new(t[i].Start,t[open].Start,t[end].Start);i=end;
        }
        return result;
    }
    internal static bool RuntimeAbi(string scripts)
    {
        bool r=false,h=false,l=false,e=false;
        foreach(var f in ProvidedRuntimeSourceScope.Files(scripts)){
            string s=File.ReadAllText(f,Encoding.UTF8);
            if(!Has(s,@"\bmodule\s+GRedRuntime\b"))continue;
            if(Has(s,@"\bclass\s+Runtime\b") && Has(s,@"\bfunc\s+IsReady\s*\(") &&
               Has(s,@"\bfunc\s+GetInputHub\s*\(") && Has(s,@"\bfunc\s+Get\s*\(\s*game\s*:\s*GameInstance"))r=true;
            if(Has(s,@"\bclass\s+InputHub\b") && Has(s,@"\bfunc\s+Subscribe\s*\(")&&
               Has(s,@"\bfunc\s+Unsubscribe\s*\("))h=true;
            if(Has(s,@"\bclass\s+InputListener\b"))l=true;
            if(Has(s,@"\bclass\s+InputEvent\b"))e=true;
            if(r&&h&&l&&e)return true;
        }
        return false;
    }
    private static string HubAttach(string nl)=>string.Join(nl,new string[]{
        "this.m_grsInputSubscription = 0;",
        "    this.m_grsInputHub = null;",
        "    this.m_grsFallbackRegistered = false;",
        "    let grsRuntime = Runtime.Get(this.GetGame());",
        "    if IsDefined(grsRuntime) && grsRuntime.IsReady() {",
        "        let grsHub = grsRuntime.GetInputHub();",
        "        if IsDefined(grsHub) {",
        "            let grsId = grsHub.Subscribe(GRS_RecoverActionName(), this.m_grsRecoverInputListener);",
        "            if grsId > 0 {",
        "                this.m_grsInputSubscription = grsId;",
        "                this.m_grsInputHub = grsHub;",
        "            }",
        "        }",
        "    }",
        "    if this.m_grsInputSubscription <= 0 {",
        "        this.RegisterInputListener(this.m_grsRecoverInputListener);",
        "        this.m_grsFallbackRegistered = true;",
        "    }"
    });
    private static string HubDetach(string nl)=>string.Join(nl,new string[]{
        "if IsDefined(this.m_grsInputHub) && this.m_grsInputSubscription > 0 {",
        "        this.m_grsInputHub.Unsubscribe(this.m_grsInputSubscription);",
        "    }",
        "    this.m_grsInputSubscription = 0;",
        "    this.m_grsInputHub = null;",
        "    if this.m_grsFallbackRegistered {",
        "        this.UnregisterInputListener(this.m_grsRecoverInputListener);",
        "    }",
        "    this.m_grsFallbackRegistered = false;"
    });
    private static string HubCallback(string nl)=>string.Join(nl,new string[]{
        "public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
        "        if !IsDefined(evt) || !Equals(evt.actionName, GRS_RecoverActionName()) ||",
        "           !Equals(evt.actionType, gameinputActionType.BUTTON_HOLD_COMPLETE) { return; }",
        "        if this.GRSPerformRecover() { evt.consumed = true; }",
        "    }"
    });
    private static Decision Rewrite(string original)
    {
        string s=original,nl=NL(s);
        if(!Has(s,@"\bmodule\s+GearRecoverySystem\b")||
           !Has(s,@"\bclass\s+GRSRecoverInputListener\b"))
            return new("REFUSED","GRS_HUB_OWNER_MISSING",null,0);
        var action=Method(s,"OnAction","action : ListenerAction");
        var attach=Method(s,"OnGameAttached","", "PlayerPuppet");
        var detach=Method(s,"OnDetach","", "PlayerPuppet");
        if(action is null||attach is null||detach is null)
            return new("REFUSED","GRS_HUB_REQUIRED_METHOD_MISSING",null,0);
        string body=s[(action.Open+1)..action.Close];
        string attached=s[(attach.Open+1)..attach.Close];
        string detached=s[(detach.Open+1)..detach.Close];
        if(!Has(body,@"\bGRS_RecoverActionName\s*\(") ||
           !Has(body,@"\bBUTTON_HOLD_COMPLETE\b") ||
           !Has(attached,@"\bIsPreGame\s*\(")||
           !Has(attached,@"\bm_grsRecoverInputListener\s*=\s*new\s+GRSRecoverInputListener")||
           !Has(detached,@"\bwrappedMethod\s*\(")||
           !Has(detached,@"\bm_grsRecoverInputListener\s*=\s*null"))
            return new("REFUSED","GRS_HUB_OWNER_LIFECYCLE_UNPROVEN",null,0);
        var edits=new List<Edit>();
        void Add(int start,int end,string val)=>edits.Add(new(start,end,val));
        var gameplay=Method(s,"GRSPerformRecover","");
        if(gameplay is null){
            // Only the owner's original two name/type early returns may be
            // taken before the recovery body. Do not move any extra action.
            var checks=Regex.Matches(body,@"if\s+!Equals\s*\([^;]+?\)\s*\{\s*return\s+false\s*;\s*\}",RegexOptions.Singleline);
            if(checks.Count!=2||!checks[0].Value.Contains("GetName(")||
               !checks[1].Value.Contains("GetType("))
                return new("REFUSED","GRS_HUB_ACTION_GUARDS_UNPROVEN",null,0);
            int bodyStart=checks[1].Index+checks[1].Length;
            string work=body[bodyStart..];
            if(!Has(work,@"\bGRSInteractionController\s*\.\s*Get\s*\(")||
               !Has(work,@"\bTryRecoverGear\s*\(")||
               !Has(work,@"\breturn\s+true\s*;"))
                return new("REFUSED","GRS_HUB_RECOVERY_BODY_UNPROVEN",null,0);
            Add(action.Open+1+bodyStart,action.Close,
                nl+"        return this.GRSPerformRecover();"+nl+"    ");
            Add(action.Close+1,action.Close+1,nl+
                "    private func GRSPerformRecover() -> Bool {"+
                work.TrimEnd()+nl+"    }"+nl);
        }
        else if(!Has(body,@"\bthis\s*\.\s*GRSPerformRecover\s*\(") ||
                !Has(s[(gameplay.Open+1)..gameplay.Close],@"\bTryRecoverGear\s*\(") )
            return new("REFUSED","GRS_HUB_PARTIAL_GAMEPLAY_EXTRACTION_UNPROVEN",null,0);
        if(!Has(s,@"\bclass\s+GRSRecoverInputListener\s+extends\s+InputListener\b")){
            var cls=Regex.Match(s,@"\bpublic\s+class\s+GRSRecoverInputListener\s*\{");
            if(!cls.Success)return new("REFUSED","GRS_HUB_LISTENER_BASE_INCOMPATIBLE",null,0);
            Add(cls.Index,cls.Index+cls.Length,cls.Value.Replace("{","extends InputListener {"));
        }
        if(!Has(s,@"\bfunc\s+OnGRedInput\s*\(")){
            Add(action.Close+1,action.Close+1,nl+"    "+HubCallback(nl)+nl);
        }else if(!Has(s,@"\bevt\s*\.\s*consumed\s*=\s*true"))
            return new("REFUSED","GRS_HUB_HISTORICAL_CONSUMPTION_LOST",null,0);
        var reg=Regex.Match(attached,@"\bthis\s*\.\s*RegisterInputListener\s*\(\s*this\s*\.\s*m_grsRecoverInputListener\s*\)\s*;");
        if(reg.Success && !Has(attached,@"\bm_grsInputSubscription\b"))
            Add(attach.Open+1+reg.Index,attach.Open+1+reg.Index+reg.Length,HubAttach(nl));
        else if(!Has(attached,@"\bm_grsFallbackRegistered\s*=\s*true"))
            return new("REFUSED","GRS_HUB_ATTACH_PARTIAL_OR_CHANGED",null,0);
        var unreg=Regex.Match(detached,@"\bthis\s*\.\s*UnregisterInputListener\s*\(\s*this\s*\.\s*m_grsRecoverInputListener\s*\)\s*;");
        if(unreg.Success && !Has(detached,@"\bm_grsInputSubscription\b"))
            Add(detach.Open+1+unreg.Index,detach.Open+1+unreg.Index+unreg.Length,HubDetach(nl));
        else if(!Has(detached,@"\bm_grsFallbackRegistered\b") ||
                !Has(detached,@"\bUnsubscribe\s*\("))
            return new("REFUSED","GRS_HUB_DETACH_PARTIAL_OR_CHANGED",null,0);
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*")){
            var m=Regex.Match(s,@"\bmodule\s+GearRecoverySystem\s*\r?\n");
            if(!m.Success)return new("REFUSED","GRS_HUB_IMPORT_ANCHOR_MISSING",null,0);
            Add(m.Index+m.Length,m.Index+m.Length,"import GRedRuntime.*"+nl);
        }
        string[] fields={"m_grsInputHub: wref<InputHub>",
            "m_grsInputSubscription: Int32","m_grsFallbackRegistered: Bool"};
        var missing=new List<string>();
        foreach(var f in fields){
            string key=f[..f.IndexOf(':')],type=f[(f.IndexOf(':')+1)..].Trim();
            var found=Regex.Matches(s,@"\blet\s+"+Regex.Escape(key)+@"\s*:\s*(?<type>[^;]+);");
            if(found.Count>1||(found.Count==1&&
                string.Concat(found[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
                string.Concat(type.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","GRS_HUB_FIELD_COLLISION:"+key,null,0);
            if(found.Count==0)missing.Add(f);
        }
        if(missing.Count>0){
            var field=Regex.Match(s,@"@addField\s*\(\s*PlayerPuppet\s*\)\s*private\s+let\s+m_grsRecoverInputListener\s*:");
            if(!field.Success)return new("REFUSED","GRS_HUB_ORIGINAL_FIELD_MISSING",null,0);
            Add(field.Index,field.Index,string.Join(nl,missing.Select(f=>
                "@addField(PlayerPuppet)"+nl+"private let "+f+";"))+nl+nl);
        }
        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var sorted=edits.OrderByDescending(x=>x.Start).ThenByDescending(x=>x.End).ToArray();
        for(int i=1;i<sorted.Length;i++){
            if(sorted[i].End>sorted[i-1].Start)return new("REFUSED","GRS_HUB_OVERLAPPING_EDITS",null,0);
        }
        string output=s;
        foreach(var e in sorted)output=output[..e.Start]+e.Value+output[e.End..];
        if(!Has(output,@"\bclass\s+GRSRecoverInputListener\s+extends\s+InputListener\b")||
           !Has(output,@"\bfunc\s+OnGRedInput\s*\(")||
           !Has(output,@"\bfunc\s+GRSPerformRecover\s*\(")||
           !Has(output,@"\bevt\s*\.\s*consumed\s*=\s*true" )||
           !Has(output,@"\bm_grsFallbackRegistered\s*=\s*true"))
            throw new InvalidDataException("GRS_HUB_POSTCONDITION_FAILED");
        foreach(Match m in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(m.Value,StringComparison.Ordinal))
                throw new InvalidDataException("GRS_HUB_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }
    internal static Result Generate(string gameRoot,string outputRoot,string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
            Inside(output,game))throw new InvalidDataException("GRS_HUB_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
             Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("GRS_HUB_OVERLAY_UNSAFE");
        var admit=new SourceAdmission(game);_=admit.AuditAll();
        string hash=admit.SourceStackSha256??throw new InvalidDataException("GRS_HUB_HASH_MISSING");
        string status="NOT_APPLICABLE";string? rel=null,reason=null,rewritten=null;int edits=0;
        string owner=Path.Combine(scripts,"GearRecoverySystem");
        if(Directory.Exists(owner)){
            var found=Directory.EnumerateFiles(owner,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),@"\bclass\s+GRSRecoverInputListener\b"))
                .ToArray();
            if(found.Length!=1){status="REFUSED";reason=found.Length==0?
                "GRS_HUB_LISTENER_SOURCE_MISSING":"GRS_HUB_LISTENER_SOURCE_AMBIGUOUS";}
            else if(!RuntimeAbi(scripts)){
                status="REFUSED";reason="GRS_HUB_RUNTIME_API_MISSING";
            }else{
                string file=found[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("GRS_HUB_SOURCE_LINK_UNSAFE");
                rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string src=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null){
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,rel.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))throw new InvalidDataException("GRS_HUB_PREVIEW_ESCAPE");
                    if(File.Exists(staged))src=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(src);status=d.Status;reason=d.Reason;edits=d.Edits;rewritten=d.Changed;
            }
        }
        if(!string.Equals(hash,admit.FingerprintCurrentSources(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GRS_HUB_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(rewritten is not null&&rel is not null){
            string file=Path.GetFullPath(Path.Combine(output,rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(file,output)||!file.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GRS_HUB_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,rewritten,new UTF8Encoding(false));
        }
        Result result=new(Rule,status,edits,status=="COMPLETE"?1:0,rel,reason,true,false);
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
