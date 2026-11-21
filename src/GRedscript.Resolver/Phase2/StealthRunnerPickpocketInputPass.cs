using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 3.0 pickpocket navigation. Register both UI_MoveUp and
/// UI_MoveDown in the existing InputHub, and use the original OnAction
/// wrapper exclusively when the two subscriptions cannot both be established.
/// Preserve all custom loot UI state, weapon/phone/consumable wrappers and
/// original wrappedMethod behavior.
/// </summary>
internal static class StealthRunnerPickpocketInputPass
{
    internal const string Rule="STEALTH_RUNNER_PICKPOCKET_INPUT_FALLBACK_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Block(int Start,int Open,int Close);
    private sealed record Edit(int Start,int End,string Text);
    private sealed record Decision(string Status,string? Reason,string? Changed,int Edits);
    private static bool Has(string s,string rx)=>
        Regex.IsMatch(s,rx,RegexOptions.CultureInvariant);
    private static string NL(string s)=>s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static int Close(IReadOnlyList<SourceAdmission.Token> t,int index,string a,string b)
    {
        if(index<0||index>=t.Count||t[index].Value!=a)return -1;
        int n=0;
        for(int i=index;i<t.Count;i++)
        {
            if(t[i].Value==a)n++;
            else if(t[i].Value==b&&--n==0)return i;
        }
        return -1;
    }
    private static Block? PickpocketOnAction(string s)
    {
        var t=SourceAdmission.ReadTokens(s);Block? match=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="func"||t[i+1].Value!="OnAction"||t[i+2].Value!="(")continue;
            int args=Close(t,i+2,"(",")");
            if(args<0)return null;
            int open=args+1;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=Close(t,open,"{","}");
            if(close<0)return null;
            string body=s[(t[open].Start+1)..t[close].Start];
            if(Has(body,@"\bcustomLootInterface\s*\.\s*Up\s*\(") &&
               Has(body,@"\bcustomLootInterface\s*\.\s*Down\s*\("))
            {
                string prefix=s[Math.Max(0,t[i].Start-160)..t[i].Start];
                if(!Has(prefix,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*protected\s+cb\s*$"))
                    return null;
                if(match is not null)return null;
                match=new(t[i].Start,t[open].Start,t[close].Start);
            }
            i=close;
        }
        return match;
    }
    private static Decision Rewrite(string s)
    {
        string nl=NL(s);
        if(!Has(s,@"\bmodule\s+StealthRunner\b"))
            return new("REFUSED","SR_INPUT_MODULE_MISSING",null,0);
        var owner=PickpocketOnAction(s);
        if(owner is null)
            return new("REFUSED","SR_INPUT_ONACTION_MISSING_OR_AMBIGUOUS",null,0);
        string body=s[(owner.Open+1)..owner.Close];
        if(!Has(body,@"\bListenerAction\s*\.\s*IsButtonJustPressed\s*\(")||
           !Has(body,@"\bListenerAction\s*\.\s*IsAction\s*\(\s*action\s*,\s*n""UI_MoveUp""")||
           !Has(body,@"\bListenerAction\s*\.\s*IsAction\s*\(\s*action\s*,\s*n""UI_MoveDown""")||
           !Has(body,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)")||
           !Has(s,@"\bcustomLootInterface\b"))
            return new("REFUSED","SR_INPUT_OWNER_GAMEPLAY_UNPROVEN",null,0);
        var edits=new List<Edit>();
        void Add(int a,int b,string text)=>edits.Add(new(a,b,text));
        var module=Regex.Match(s,@"\bmodule\s+StealthRunner\s*\r?\n");
        if(!module.Success)return new("REFUSED","SR_INPUT_MODULE_ANCHOR_MISSING",null,0);
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
            Add(module.Index+module.Length,module.Index+module.Length,
                "import GRedRuntime.*"+nl);
        foreach((string name,string type) in new[]{
            ("m_gcoSrPickHub","wref<InputHub>"),
            ("m_gcoSrPickListener","ref<GCO_SRPickInputListener>"),
            ("m_gcoSrPickUpID","Int32"),
            ("m_gcoSrPickDownID","Int32"),
            ("m_gcoSrPickActive","Bool")})
        {
            var fields=Regex.Matches(s,@"\blet\s+"+Regex.Escape(name)+@"\s*:\s*(?<type>[^;]+);");
            if(fields.Count>1 || (fields.Count==1&&
                string.Concat(fields[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
                string.Concat(type.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","SR_INPUT_FIELD_COLLISION:"+name,null,0);
            if(fields.Count==0)
                Add(module.Index+module.Length,module.Index+module.Length,nl+
                    "@addField(PlayerPuppet)"+nl+"private let "+name+": "+type+";"+nl);
        }
        if(!Has(s,@"\bclass\s+GCO_SRPickInputListener\b"))
        {
            Add(module.Index+module.Length,module.Index+module.Length,nl+
                string.Join(nl,new[]{
                    "public class GCO_SRPickInputListener extends InputListener {",
                    "  private let m_player: wref<PlayerPuppet>;",
                    "  public func Init(player: wref<PlayerPuppet>) -> Void { this.m_player = player; }",
                    "  public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
                    "    if !IsDefined(evt) || !IsDefined(this.m_player) ||",
                    "       !IsDefined(this.m_player.customLootInterface) ||",
                    "       NotEquals(evt.actionType, gameinputActionType.BUTTON_PRESSED) { return; }",
                    "    if Equals(evt.actionName, n\"UI_MoveUp\") {",
                    "      this.m_player.customLootInterface.Up();",
                    "    } else {",
                    "      if Equals(evt.actionName, n\"UI_MoveDown\") {",
                    "        this.m_player.customLootInterface.Down();",
                    "      }",
                    "    }",
                    "  }",
                    "}",
                    ""
                })+nl);
        }
        else if(!Has(s,@"\bclass\s+GCO_SRPickInputListener\s+extends\s+InputListener\b") ||
                !Has(s,@"\bfunc\s+OnGRedInput\s*\("))
            return new("REFUSED","SR_INPUT_LISTENER_PARTIAL_UNPROVEN",null,0);
        var oldGuard=Regex.Match(body,
            @"\bif\s+IsDefined\s*\(\s*this\s*\.\s*customLootInterface\s*\)\s*\{");
        bool gated=Has(body,@"\bthis\s*\.\s*m_gcoSrPickActive\b");
        if(!gated && oldGuard.Success)
            Add(owner.Open+1+oldGuard.Index,owner.Open+1+oldGuard.Index+oldGuard.Length,
                "if IsDefined(this.customLootInterface) && !this.m_gcoSrPickActive {");
        else if(!gated || !Has(body,@"!\s*this\s*\.\s*m_gcoSrPickActive"))
            return new("REFUSED","SR_INPUT_ORIGINAL_ENGINE_FALLBACK_UNPROVEN",null,0);
        if(!Has(s,@"\bfunc\s+GCO_SRPickBind\s*\("))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]{
                "@addMethod(PlayerPuppet)",
                "private func GCO_SRPickBind() -> Void {",
                "  this.GCO_SRPickUnbind();",
                "  let runtime = Runtime.Get(this.GetGame());",
                "  if !IsDefined(runtime) || !runtime.IsReady() { return; }",
                "  let hub = runtime.GetInputHub();",
                "  if !IsDefined(hub) { return; }",
                "  let listener = new GCO_SRPickInputListener();",
                "  listener.Init(this);",
                "  let up = hub.Subscribe(n\"UI_MoveUp\", listener);",
                "  if up <= 0 { return; }",
                "  let down = hub.Subscribe(n\"UI_MoveDown\", listener);",
                "  if down <= 0 { hub.Unsubscribe(up); return; }",
                "  this.m_gcoSrPickHub = hub;",
                "  this.m_gcoSrPickListener = listener;",
                "  this.m_gcoSrPickUpID = up;",
                "  this.m_gcoSrPickDownID = down;",
                "  this.m_gcoSrPickActive = true;",
                "}",
                "",
                "@addMethod(PlayerPuppet)",
                "private func GCO_SRPickUnbind() -> Void {",
                "  if IsDefined(this.m_gcoSrPickHub) {",
                "    if this.m_gcoSrPickUpID > 0 { this.m_gcoSrPickHub.Unsubscribe(this.m_gcoSrPickUpID); }",
                "    if this.m_gcoSrPickDownID > 0 { this.m_gcoSrPickHub.Unsubscribe(this.m_gcoSrPickDownID); }",
                "  }",
                "  this.m_gcoSrPickUpID = 0;",
                "  this.m_gcoSrPickDownID = 0;",
                "  this.m_gcoSrPickActive = false;",
                "  this.m_gcoSrPickListener = null;",
                "  this.m_gcoSrPickHub = null;",
                "}",
                ""
            })+nl);
        }
        else if(!Has(s,@"\bfunc\s+GCO_SRPickUnbind\s*\(") ||
                !Has(s,@"\bSubscribe\s*\(\s*n""UI_MoveUp""")||
                !Has(s,@"\bSubscribe\s*\(\s*n""UI_MoveDown""")||
                !Has(s,@"\bUnsubscribe\s*\(\s*this\s*\.\s*m_gcoSrPickUpID")||
                !Has(s,@"\bUnsubscribe\s*\(\s*this\s*\.\s*m_gcoSrPickDownID"))
            return new("REFUSED","SR_INPUT_ATTACH_PARTIAL_UNPROVEN",null,0);
        if(!Has(s,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*protected\s+cb\s+func\s+OnGameAttached\s*\(\s*\)\s*->\s*Bool\s*\{[\s\S]{0,250}\bGCO_SRPickBind\s*\("))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]{
                "@wrapMethod(PlayerPuppet)",
                "protected cb func OnGameAttached() -> Bool {",
                "  let result = wrappedMethod();",
                "  this.GCO_SRPickBind();",
                "  return result;",
                "}",
                ""
            })+nl);
        }
        if(!Has(s,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*protected\s+cb\s+func\s+OnDetach\s*\(\s*\)\s*->\s*Bool\s*\{[\s\S]{0,250}\bGCO_SRPickUnbind\s*\("))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]{
                "@wrapMethod(PlayerPuppet)",
                "protected cb func OnDetach() -> Bool {",
                "  this.GCO_SRPickUnbind();",
                "  return wrappedMethod();",
                "}",
                ""
            })+nl);
        }
        if(edits.Count==0)return new("COMPLETE",null,null,0);
        // Multiple field/class/import insertions share the module anchor.
        // Apply equal-position edits in reverse creation order so imports
        // remain before all REDscript declarations in the effective file.
        var ordered=edits.Select((edit,index)=>(edit,index))
            .OrderByDescending(x=>x.edit.Start)
            .ThenByDescending(x=>x.edit.End)
            .ThenByDescending(x=>x.index)
            .Select(x=>x.edit).ToArray();
        for(int i=1;i<ordered.Length;i++)
            if(ordered[i].End>ordered[i-1].Start)
                return new("REFUSED","SR_INPUT_EDITS_OVERLAP",null,0);
        string output=s;
        foreach(var e in ordered)output=output[..e.Start]+e.Text+output[e.End..];
        if(!Has(output,@"\bGCO_SRPickBind\s*\(")||
           !Has(output,@"\bGCO_SRPickUnbind\s*\(")||
           !Has(output,@"\bthis\s*\.\s*m_gcoSrPickActive\b")||
           !Has(output,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)"))
            throw new InvalidDataException("SR_INPUT_POSTCONDITION_FAILED");
        foreach(Match m in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(m.Value,StringComparison.Ordinal))
                throw new InvalidDataException("SR_INPUT_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||
           File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("SR_INPUT_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("SR_INPUT_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);_=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("SR_INPUT_SOURCE_HASH_MISSING");
        string status="NOT_APPLICABLE";string? reason=null,rel=null,changed=null;
        int edits=0;string owner=Path.Combine(scripts,"StealthRunner");
        if(Directory.Exists(owner))
        {
            var files=Directory.EnumerateFiles(owner,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bcustomLootInterface\s*\.\s*Up\s*\(") &&
                    Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bcustomLootInterface\s*\.\s*Down\s*\(")).ToArray();
            if(files.Length!=1)
            {
                status="REFUSED";reason=files.Length==0?
                    "SR_PICKPOCKET_INPUT_OWNER_MISSING":"SR_PICKPOCKET_INPUT_OWNER_AMBIGUOUS";
            }
            else if(!GearRecoveryInputHubPass.RuntimeAbi(scripts))
            {
                status="REFUSED";reason="SR_INPUT_RUNTIME_ABI_MISSING";
            }
            else
            {
                string f=files[0];
                if((File.GetAttributes(f)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("SR_INPUT_SOURCE_LINK_UNSAFE");
                rel="r6/scripts/"+Path.GetRelativePath(scripts,f).Replace('\\','/');
                string s=File.ReadAllText(f,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("SR_INPUT_OVERLAY_ESCAPE");
                    if(File.Exists(staged))s=File.ReadAllText(staged,Encoding.UTF8);
                }
                var decision=Rewrite(s);
                status=decision.Status;reason=decision.Reason;
                changed=decision.Changed;edits=decision.Edits;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SR_INPUT_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(changed is not null && rel is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SR_INPUT_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst,changed,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,edits,status=="COMPLETE"?1:0,
            rel,reason,true,false);
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
