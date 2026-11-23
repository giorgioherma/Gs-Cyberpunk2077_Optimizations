using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.2 GRS StateCache/context getters. Reuse the existing
/// GRedRuntime service within the current owner, but always preserve the
/// original GameInstance player and blackboard lookups as fallbacks. Do not
/// require #36 or any historical pass as input. Never rewrite helper fallbacks.
/// </summary>
internal static class GearRecoveryStateCachePass
{
    internal const string Rule="GRS_STATE_CACHE_WITH_DIRECT_FALLBACK_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Scope(int Open,int Close);
    private sealed record Edit(int Start,int End,string Text);
    private sealed record Decision(string Status,string? Reason,string? Changed,int Edits);
    private static bool Has(string s,string rx)=>
        Regex.IsMatch(s,rx,RegexOptions.CultureInvariant);
    private static string NL(string s)=>
        s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static int End(IReadOnlyList<SourceAdmission.Token> t,int i,
        string open,string close)
    {
        if(i<0||i>=t.Count||t[i].Value!=open)return -1;
        int n=0;
        for(int p=i;p<t.Count;p++)
        {
            if(t[p].Value==open)n++;
            else if(t[p].Value==close&&--n==0)return p;
        }
        return -1;
    }
    private static Scope? Class(string s,string name)
    {
        var t=SourceAdmission.ReadTokens(s);
        Scope? match=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="class"||t[i+1].Value!=name)continue;
            int open=i+2;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=End(t,open,"{","}");
            if(close<0||match is not null)return null;
            match=new(t[open].Start,t[close].Start);
            i=close;
        }
        return match;
    }
    private static Scope? Method(string s,Scope owner,string name)
    {
        var t=SourceAdmission.ReadTokens(s);Scope? match=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Start<=owner.Open||t[i].Start>=owner.Close||
               t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")continue;
            int args=End(t,i+2,"(",")");if(args<0)return null;
            int open=args+1;while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=End(t,open,"{","}");
            if(close<0||t[close].Start>owner.Close||match is not null)return null;
            match=new(t[open].Start,t[close].Start);
            i=close;
        }
        return match;
    }
    private static bool Abi(string scripts)
    {
        bool runtime=false,state=false;
        foreach(string file in ProvidedRuntimeSourceScope.Files(scripts))
        {
            string s=File.ReadAllText(file,Encoding.UTF8);
            if(!Has(s,@"\bmodule\s+GRedRuntime\b"))continue;
            if(Has(s,@"\bclass\s+Runtime\b") &&
               Has(s,@"\bfunc\s+IsReady\s*\(") &&
               Has(s,@"\bfunc\s+GetStateCache\s*\("))runtime=true;
            if(Has(s,@"\bclass\s+StateCache\b") &&
               Has(s,@"\bfunc\s+GetPlayer\s*\(") &&
               Has(s,@"\bfunc\s+GetBlackboardSystem\s*\("))state=true;
            if(runtime&&state)return true;
        }
        return false;
    }
    private static Decision Rewrite(string s)
    {
        if(!Has(s,@"\bmodule\s+GearRecoverySystem\b"))
            return new("REFUSED","GRS_STATE_MODULE_MISSING",null,0);
        var owner=Class(s,"GRSInteractionController");
        if(owner is null)return new("REFUSED","GRS_STATE_OWNER_AMBIGUOUS",null,0);
        var stop=Method(s,owner,"StopPollingAndClearPrompt");
        var detach=Method(s,owner,"OnDetach");
        var poll=Method(s,owner,"OnPollTick");
        if(stop is null||detach is null||poll is null ||
           !Has(s[(poll.Open+1)..poll.Close],@"\bGRSRecoverySystem\s*\.\s*Get\s*\("))
            return new("REFUSED","GRS_STATE_LIFECYCLE_UNPROVEN",null,0);

        // Recognize the historical hand-optimized triplet as a separate
        // implementation, never introduce a second cache/adapter alongside it.
        bool historical=Method(s,owner,"GRTStateCache") is not null &&
            Method(s,owner,"GRTPlayer") is not null &&
            Method(s,owner,"GRTBlackboardSystem") is not null;
        bool historicalPartial=Has(s,@"\bfunc\s+GRT(?:StateCache|Player|BlackboardSystem)\s*\(") &&
            !historical;
        if(historicalPartial)
            return new("REFUSED","GRS_HISTORICAL_CACHE_PARTIAL_UNPROVEN",null,0);

        string cacheField=historical?"stateCache":"m_gcoGrsStateCache";
        string stateName=historical?"GRTStateCache":"GCOGrsStateCache";
        string playerName=historical?"GRTPlayer":"GCOGrsPlayer";
        string bbName=historical?"GRTBlackboardSystem":"GCOGrsBlackboardSystem";
        string nl=NL(s);
        var edits=new List<Edit>();
        void Add(int start,int end,string value)=>edits.Add(new(start,end,value));
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var module=Regex.Match(s,@"\bmodule\s+GearRecoverySystem\s*\r?\n");
            if(!module.Success)return new("REFUSED","GRS_STATE_MODULE_ANCHOR_MISSING",null,0);
            Add(module.Index+module.Length,module.Index+module.Length,
                "import GRedRuntime.*"+nl);
        }
        string classBody=s[(owner.Open+1)..owner.Close];
        var fields=Regex.Matches(classBody,
            @"\blet\s+"+Regex.Escape(cacheField)+@"\s*:\s*(?<type>[^;]+);");
        if(fields.Count>1 || (fields.Count==1&&
           string.Concat(fields[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
           "wref<StateCache>"))
            return new("REFUSED","GRS_STATE_FIELD_COLLISION",null,0);
        if(fields.Count==0)
            Add(owner.Open+1,owner.Open+1,nl+
                "    private let "+cacheField+": wref<StateCache>;");

        var state=Method(s,owner,stateName);
        var player=Method(s,owner,playerName);
        var bb=Method(s,owner,bbName);
        string Helpers()=>string.Join(nl,new[]{
            "    private func "+stateName+"() -> wref<StateCache> {",
            "        if IsDefined(this."+cacheField+") { return this."+cacheField+"; }",
            "        let runtime = Runtime.Get(GetGameInstance());",
            "        if IsDefined(runtime) && runtime.IsReady() {",
            "            this."+cacheField+" = runtime.GetStateCache();",
            "        }",
            "        return this."+cacheField+";",
            "    }",
            "",
            "    private func "+playerName+"() -> wref<PlayerPuppet> {",
            "        let state = this."+stateName+"();",
            "        if IsDefined(state) {",
            "            let cached = state.GetPlayer();",
            "            if IsDefined(cached) { return cached; }",
            "        }",
            "        return GameInstance.GetPlayerSystem(GetGameInstance()).GetLocalPlayerMainGameObject() as PlayerPuppet;",
            "    }",
            "",
            "    private func "+bbName+"() -> wref<BlackboardSystem> {",
            "        let state = this."+stateName+"();",
            "        if IsDefined(state) {",
            "            let cached = state.GetBlackboardSystem();",
            "            if IsDefined(cached) { return cached; }",
            "        }",
            "        return GameInstance.GetBlackboardSystem(GetGameInstance());",
            "    }",
            ""
        })+nl;
        if(state is null||player is null||bb is null)
        {
            if(state is not null||player is not null||bb is not null)
                return new("REFUSED","GRS_STATE_HELPER_PARTIAL_UNPROVEN",null,0);
            Add(owner.Close,owner.Close,nl+Helpers());
        }
        else
        {
            string st=s[(state.Open+1)..state.Close];
            if(!Has(st,@"\bRuntime\s*\.\s*Get\s*\(")||
               !Has(st,@"\bGetStateCache\s*\("))
                return new("REFUSED","GRS_STATE_GETTER_INCOMPATIBLE",null,0);
            string p=s[(player.Open+1)..player.Close];
            if(!Has(p,@"\bGetPlayer\s*\("))
                return new("REFUSED","GRS_STATE_PLAYER_HELPER_INCOMPATIBLE",null,0);
            if(!Has(p,@"\bGetLocalPlayerMainGameObject\s*\("))
            {
                // The original historical helper returned null if runtime
                // was absent. Restore its direct fallback in this owner.
                var older=Regex.Match(p,
                    @"return\s+IsDefined\s*\(\s*state\s*\)\s*\?\s*state\s*\.\s*GetPlayer\s*\(\s*\)\s*:\s*null\s*;");
                if(older.Success)
                    Add(player.Open+1+older.Index,player.Open+1+older.Index+older.Length,
                        "if IsDefined(state) { let cached = state.GetPlayer(); if IsDefined(cached) { return cached; } };"+
                        nl+"        return GameInstance.GetPlayerSystem(GetGameInstance()).GetLocalPlayerMainGameObject() as PlayerPuppet;");
                else return new("REFUSED","GRS_STATE_PLAYER_FALLBACK_UNPROVEN",null,0);
            }
            string b=s[(bb.Open+1)..bb.Close];
            if(!Has(b,@"\bGetBlackboardSystem\s*\("))
                return new("REFUSED","GRS_STATE_BB_HELPER_INCOMPATIBLE",null,0);
            if(!Has(b,@"\bGameInstance\s*\.\s*GetBlackboardSystem\s*\("))
                return new("REFUSED","GRS_STATE_BB_FALLBACK_MISSING",null,0);
        }

        // Never scan inside a cache helper: the original direct access there
        // is the only safe fallback and must not be optimized into recursion.
        var helpers=new[]{state,player,bb}.Where(x=>x is not null).Select(x=>x!).ToArray();
        bool InHelper(int position)=>helpers.Any(x=>position>x.Open&&position<x.Close);
        const string playerExpr=@"GameInstance\s*\.\s*GetPlayerSystem\s*\(\s*GetGameInstance\s*\(\s*\)\s*\)\s*\.\s*GetLocalPlayerMainGameObject\s*\(\s*\)\s*as\s+PlayerPuppet";
        const string bbExpr=@"GameInstance\s*\.\s*GetBlackboardSystem\s*\(\s*GetGameInstance\s*\(\s*\)\s*\)";
        int playerCalls=0,bbCalls=0;
        foreach(Match m in Regex.Matches(s,playerExpr))
        {
            if(m.Index<=owner.Open||m.Index>=owner.Close||InHelper(m.Index))continue;
            Add(m.Index,m.Index+m.Length,"this."+playerName+"()");
            playerCalls++;
        }
        foreach(Match m in Regex.Matches(s,bbExpr))
        {
            if(m.Index<=owner.Open||m.Index>=owner.Close||InHelper(m.Index))continue;
            Add(m.Index,m.Index+m.Length,"this."+bbName+"()");
            bbCalls++;
        }
        if(playerCalls==0&&bbCalls==0 &&
           !Has(classBody,@"\bthis\s*\.\s*"+Regex.Escape(playerName)+@"\s*\(") &&
           !Has(classBody,@"\bthis\s*\.\s*"+Regex.Escape(bbName)+@"\s*\("))
            return new("REFUSED","GRS_STATE_NO_GETTER_TARGETS",null,0);

        foreach(var method in new[]{stop,detach})
        {
            string body=s[(method.Open+1)..method.Close];
            if(!Has(body,@"\bthis\s*\.\s*"+Regex.Escape(cacheField)+@"\s*=\s*null\s*;"))
                Add(method.Open+1,method.Open+1,nl+
                    "        this."+cacheField+" = null;");
        }
        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var sorted=edits.Select((edit,index)=>(edit,index))
            .OrderByDescending(x=>x.edit.Start)
            .ThenByDescending(x=>x.edit.End)
            .ThenByDescending(x=>x.index)
            .Select(x=>x.edit).ToArray();
        for(int i=1;i<sorted.Length;i++)
            if(sorted[i].End>sorted[i-1].Start)
                return new("REFUSED","GRS_STATE_EDITS_OVERLAP",null,0);
        string output=s;
        foreach(var e in sorted)
            output=output[..e.Start]+e.Text+output[e.End..];
        if(!Has(output,@"\bfunc\s+"+Regex.Escape(stateName)+@"\s*\(") ||
           !Has(output,@"\bfunc\s+"+Regex.Escape(playerName)+@"\s*\(") ||
           !Has(output,@"\bfunc\s+"+Regex.Escape(bbName)+@"\s*\(") ||
           !Has(output,@"\bthis\s*\.\s*"+Regex.Escape(cacheField)+@"\s*=\s*null\s*;") ||
           !Has(output,@"\bGameInstance\s*\.\s*GetPlayerSystem\s*\(") ||
           !Has(output,@"\bGameInstance\s*\.\s*GetBlackboardSystem\s*\("))
            throw new InvalidDataException("GRS_STATE_POSTCONDITION_FAILED");
        foreach(Match generic in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(generic.Value,StringComparison.Ordinal))
                throw new InvalidDataException("GRS_STATE_GENERIC_LOST");
        return new("APPLIED",null,output,edits.Count);
    }
    internal static Result Generate(string gameRoot,string outputRoot,string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
           Inside(output,game))throw new InvalidDataException("GRS_STATE_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null&&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("GRS_STATE_OVERLAY_UNSAFE");
        var admit=new SourceAdmission(game);_=admit.AuditAll();
        string hash=admit.SourceStackSha256??
            throw new InvalidDataException("GRS_STATE_HASH_MISSING");
        string status="NOT_APPLICABLE";string? reason=null,relative=null,changed=null;
        int edits=0;
        string root=Path.Combine(scripts,"GearRecoverySystem");
        if(Directory.Exists(root))
        {
            var found=Directory.EnumerateFiles(root,"*.reds",SearchOption.AllDirectories)
                .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+GRSInteractionController\s+extends\s+ScriptableSystem\b"))
                .ToArray();
            if(found.Length!=1)
            {
                status="REFUSED";reason=found.Length==0?
                    "GRS_STATE_OWNER_MISSING":"GRS_STATE_OWNER_AMBIGUOUS";
            }
            else if(!Abi(scripts))
            {
                status="REFUSED";reason="GRS_STATE_RUNTIME_ABI_MISSING";
            }
            else
            {
                string file=found[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("GRS_STATE_SOURCE_LINK_UNSAFE");
                relative="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string source=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("GRS_STATE_PREVIEW_ESCAPE");
                    if(File.Exists(staged))source=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(source);status=d.Status;reason=d.Reason;
                changed=d.Changed;edits=d.Edits;
            }
        }
        if(!string.Equals(hash,admit.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GRS_STATE_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(changed is not null&&relative is not null)
        {
            string target=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(target,output)||!target.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GRS_STATE_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,changed,new UTF8Encoding(false));
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
