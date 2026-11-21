using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 3.0: action-specific InputHub route for CyberwareEx pad
/// ToggleSprint release. Preserve the original exact Focus/Overclock gameplay,
/// original engine registration as fallback, and the original OnAction
/// wrapper's non-duplicated, non-wrapped pad-release path.
/// </summary>
internal static class CyberwareExInputHubPass
{
    internal const string Rule="CYBERWARE_EX_OVERLOAD_INPUT_HUB_FALLBACK_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Block(int Start,int Open,int Close);
    private sealed record Edit(int Start,int End,string Value);
    private sealed record Decision(string Status,string? Reason,string? Changed,int Edits);
    private static bool Has(string s,string rx)=>Regex.IsMatch(s,rx,RegexOptions.CultureInvariant);
    private static string NL(string s)=>s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int first,string open,string close)
    {
        if(first<0||first>=t.Count||t[first].Value!=open)return -1;
        int depth=0;
        for(int i=first;i<t.Count;i++)
        {
            if(t[i].Value==open)depth++;
            else if(t[i].Value==close&&--depth==0)return i;
        }
        return -1;
    }
    private static Block? Find(string s,string method,string witness)
    {
        var tokens=SourceAdmission.ReadTokens(s);
        Block? found=null;
        for(int i=0;i+2<tokens.Count;i++)
        {
            if(tokens[i].Value!="func"||tokens[i+1].Value!=method||
               tokens[i+2].Value!="(")continue;
            int args=Match(tokens,i+2,"(",")");if(args<0)return null;
            int open=args+1;
            while(open<tokens.Count&&tokens[open].Value is not ("{" or ";"))open++;
            if(open==tokens.Count||tokens[open].Value!="{")return null;
            int close=Match(tokens,open,"{","}");if(close<0)return null;
            string body=s[(tokens[open].Start+1)..tokens[close].Start];
            if(!body.Contains(witness,StringComparison.Ordinal)){i=close;continue;}
            string prior=s[Math.Max(0,tokens[i].Start-240)..tokens[i].Start];
            // CyberwareEx also wraps the quickhacks UI OnAction. It shares
            // TryToCycleOverclockedState but is not a PlayerPuppet hook.
            // Continue scanning rather than rejecting a valid sibling.
            if(!Has(prior,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*protected\s+cb\s*$"))
            {
                i=close;
                continue;
            }
            if(found is not null)return null;
            found=new(tokens[i].Start,tokens[open].Start,tokens[close].Start);
            i=close;
        }
        return found;
    }
    private static Decision Rewrite(string s)
    {
        string nl=NL(s);
        var attach=Find(s,"OnGameAttached","RegisterInputListener(");
        var action=Find(s,"OnAction","QuickHackableHelper.TryToCycleOverclockedState");
        if(attach is null||action is null)
            return new("REFUSED","CW_INPUT_OWNER_WRAPPERS_MISSING_OR_AMBIGUOUS",null,0);
        string attached=s[(attach.Open+1)..attach.Close];
        string onAction=s[(action.Open+1)..action.Close];
        if(!Has(s,@"\bimport\s+CyberwareEx\b") ||
           !Has(attached,@"\bIsControlledByLocalPeer\s*\(") ||
           !Has(attached,@"\bIsHost\s*\(") ||
           !Has(onAction,@"\bPlayerLastUsedPad\s*\(") ||
           !Has(onAction,@"\bListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)") ||
           !Has(onAction,@"\bListenerAction\s*\.\s*GetType\s*\(\s*action\s*\)") ||
           !Has(onAction,@"gameinputActionType\s*\.\s*BUTTON_RELEASED") ||
           !Has(onAction,@"\bQuickHackableHelper\s*\.\s*TryToCycleOverclockedState\s*\(") ||
           !Has(onAction,@"\bDPADActionPerformed\b") ||
           !Has(onAction,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)"))
            return new("REFUSED","CW_INPUT_GAMEPLAY_CONDITIONS_UNPROVEN",null,0);
        var edits=new List<Edit>();
        void Add(int start,int end,string val)=>edits.Add(new(start,end,val));
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
            Add(0,0,"import GRedRuntime.*"+nl);

        foreach((string name,string type) in new[]{
            ("m_gcoCwHub","wref<InputHub>"),
            ("m_gcoCwListener","ref<GCO_CyberwareExInputListener>"),
            ("m_gcoCwHubID","Int32")})
        {
            var f=Regex.Matches(s,@"\blet\s+"+Regex.Escape(name)+
                @"\s*:\s*(?<type>[^;]+);");
            if(f.Count>1||(f.Count==1&&
               string.Concat(f[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
               string.Concat(type.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","CW_INPUT_FIELD_COLLISION:"+name,null,0);
            if(f.Count==0)
                Add(s.Length,s.Length,nl+"@addField(PlayerPuppet)"+nl+
                    "private let "+name+": "+type+";"+nl);
        }
        if(!Has(s,@"\bclass\s+GCO_CyberwareExInputListener\b"))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]{
                "public class GCO_CyberwareExInputListener extends InputListener {",
                "  private let m_player: wref<PlayerPuppet>;",
                "  public func Init(player: wref<PlayerPuppet>) -> Void { this.m_player = player; }",
                "  public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
                "    if !IsDefined(evt) || !IsDefined(this.m_player) ||",
                "       !Equals(evt.actionName, n\"ToggleSprint\") ||",
                "       !Equals(evt.actionType, gameinputActionType.BUTTON_RELEASED) ||",
                "       !this.m_player.PlayerLastUsedPad() { return; }",
                "    evt.consumed = true;",
                "    let board = this.m_player.GetPlayerStateMachineBlackboard();",
                "    let isFocus = Equals(",
                "      IntEnum<gamePSMVision>(board.GetInt(GetAllBlackboardDefs().PlayerStateMachine.Vision)),",
                "      gamePSMVision.Focus);",
                "    if isFocus && QuickHackableHelper.TryToCycleOverclockedState(this.m_player) {",
                "      let event = new DPADActionPerformed();",
                "      event.action = EHotkey.LBRB;",
                "      event.state = EUIActionState.COMPLETED;",
                "      event.successful = true;",
                "      GameInstance.GetUISystem(this.m_player.GetGame()).QueueEvent(event);",
                "    }",
                "  }",
                "}",
                ""
            })+nl);
        }
        else if(!Has(s,@"\bclass\s+GCO_CyberwareExInputListener\s+extends\s+InputListener\b") ||
                !Has(s,@"\bfunc\s+OnGRedInput\s*\(") ||
                !Has(s,@"\bTryToCycleOverclockedState\s*\("))
            return new("REFUSED","CW_INPUT_LISTENER_PARTIAL_UNPROVEN",null,0);

        bool alreadyBound=Has(attached,@"\bm_gcoCwHubID\b");
        var originalRegister=Regex.Match(attached,
            @"\bthis\s*\.\s*RegisterInputListener\s*\(\s*this\s*,\s*n""ToggleSprint""\s*\)\s*;");
        if(!alreadyBound)
        {
            if(!originalRegister.Success)
                return new("REFUSED","CW_INPUT_ORIGINAL_REGISTER_MISSING",null,0);
            string replacement=string.Join(nl,new[]{
                "let gcoRuntime = Runtime.Get(this.GetGame());",
                "        if IsDefined(gcoRuntime) && gcoRuntime.IsReady() {",
                "            let gcoHub = gcoRuntime.GetInputHub();",
                "            if IsDefined(gcoHub) {",
                "                let gcoListener = new GCO_CyberwareExInputListener();",
                "                gcoListener.Init(this);",
                "                let gcoID = gcoHub.Subscribe(n\"ToggleSprint\", gcoListener);",
                "                if gcoID > 0 {",
                "                    this.m_gcoCwHub = gcoHub;",
                "                    this.m_gcoCwListener = gcoListener;",
                "                    this.m_gcoCwHubID = gcoID;",
                "                }",
                "            }",
                "        }",
                "        if this.m_gcoCwHubID <= 0 {",
                "            this.RegisterInputListener(this, n\"ToggleSprint\");",
                "        }"
            });
            Add(attach.Open+1+originalRegister.Index,
                attach.Open+1+originalRegister.Index+originalRegister.Length,
                replacement);
        }
        else if(!originalRegister.Success ||
                !Has(attached,@"\bSubscribe\s*\(\s*n""ToggleSprint""")||
                !Has(attached,@"\bm_gcoCwHubID\s*<=\s*0"))
            return new("REFUSED","CW_INPUT_HUB_OR_FALLBACK_PARTIAL_UNPROVEN",null,0);

        if(!Has(onAction,@"\bm_gcoCwHubID\s*>\s*0"))
        {
            Add(action.Open+1,action.Open+1,nl+string.Join(nl,new[]{
                "    // The action-specific Hub already ran CyberwareEx gameplay.",
                "    // Preserve the original wrapper's no-wrapped-method branch.",
                "    if this.m_gcoCwHubID > 0 && this.PlayerLastUsedPad() &&",
                "       Equals(ListenerAction.GetName(action), n\"ToggleSprint\") &&",
                "       Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) {",
                "        return false;",
                "    }",
                ""
            })+nl);
        }
        else if(!Has(onAction,@"\breturn\s+false\s*;"))
            return new("REFUSED","CW_INPUT_ACTION_GUARD_PARTIAL_UNPROVEN",null,0);

        if(!Has(s,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*protected\s+cb\s+func\s+OnDetach\s*\(\s*\)\s*->\s*Bool\s*\{[\s\S]{0,400}\bm_gcoCwHubID"))
        {
            Add(s.Length,s.Length,nl+string.Join(nl,new[]{
                "@wrapMethod(PlayerPuppet)",
                "protected cb func OnDetach() -> Bool {",
                "  if IsDefined(this.m_gcoCwHub) && this.m_gcoCwHubID > 0 {",
                "    this.m_gcoCwHub.Unsubscribe(this.m_gcoCwHubID);",
                "  }",
                "  this.m_gcoCwHubID = 0;",
                "  this.m_gcoCwListener = null;",
                "  this.m_gcoCwHub = null;",
                "  return wrappedMethod();",
                "}",
                ""
            })+nl);
        }

        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var ordered=edits.Select((edit,index)=>(edit,index))
            .OrderByDescending(x=>x.edit.Start)
            .ThenByDescending(x=>x.edit.End)
            .ThenByDescending(x=>x.index)
            .Select(x=>x.edit).ToArray();
        for(int i=1;i<ordered.Length;i++)
            if(ordered[i].End>ordered[i-1].Start)
                return new("REFUSED","CW_INPUT_EDITS_OVERLAP",null,0);
        string output=s;
        foreach(var e in ordered)output=output[..e.Start]+e.Value+output[e.End..];
        if(!Has(output,@"\bclass\s+GCO_CyberwareExInputListener\s+extends\s+InputListener\b")||
           !Has(output,@"\bthis\s*\.\s*m_gcoCwHub\s*\.\s*Unsubscribe\s*\(")||
           !Has(output,@"\bthis\s*\.\s*RegisterInputListener\s*\(\s*this\s*,\s*n""ToggleSprint""")||
           !Has(output,@"\bthis\s*\.\s*m_gcoCwHubID\s*>\s*0")||
           !Has(output,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)"))
            throw new InvalidDataException("CW_INPUT_POSTCONDITION_FAILED");
        foreach(Match generic in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(generic.Value,StringComparison.Ordinal))
                throw new InvalidDataException("CW_INPUT_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||
           File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("CW_INPUT_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null&&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("CW_INPUT_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);_=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("CW_INPUT_SOURCE_HASH_MISSING");
        string status="NOT_APPLICABLE";string? reason=null,rel=null,changed=null;int edits=0;
        string root=Path.Combine(scripts,"CyberwareEx");
        if(Directory.Exists(root))
        {
            var owners=Directory.EnumerateFiles(root,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bQuickHackableHelper\s*\.\s*TryToCycleOverclockedState\s*\(")&&
                    Has(File.ReadAllText(f,Encoding.UTF8),@"\bRegisterInputListener\s*\("))
                .ToArray();
            if(owners.Length!=1)
            {
                status="REFUSED";reason=owners.Length==0?
                    "CW_INPUT_OWNER_NOT_FOUND":"CW_INPUT_OWNER_AMBIGUOUS";
            }
            else if(!GearRecoveryInputHubPass.RuntimeAbi(scripts))
            {
                status="REFUSED";reason="CW_INPUT_RUNTIME_ABI_MISSING";
            }
            else
            {
                string file=owners[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("CW_INPUT_SOURCE_LINK_UNSAFE");
                rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string s=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("CW_INPUT_OVERLAY_ESCAPE");
                    if(File.Exists(staged))s=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(s);status=d.Status;reason=d.Reason;changed=d.Changed;edits=d.Edits;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CW_INPUT_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(changed is not null&&rel is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("CW_INPUT_TARGET_UNSAFE");
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
