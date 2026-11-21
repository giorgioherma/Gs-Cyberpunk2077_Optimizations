using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 3.0 VCP hot-path elimination, source-safe subset:
/// three independent quest-system read pairs are reduced to one synchronous
/// owner-local handle per callback. No InputHub listener or gameplay branch
/// is added; every original wrapper, fact, order and runtime fallback remains.
/// </summary>
internal static class VehicleCallQuestReusePass
{
    internal const string Rule="VEHICLE_CALL_QUEST_SYSTEM_LOCAL_REUSE_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string? SourcePath,string[] Refusals,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Method(int Start,int Open,int Close,string Name);
    private sealed record Edit(int Start,int End,string Value);
    private sealed record Decision(string Status,string? Changed,int Edits,string[] Refusals,int Complete);
    private static bool Has(string s,string rx)=>
        Regex.IsMatch(s,rx,RegexOptions.CultureInvariant);
    private static string NL(string s)=>
        s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static int Match(IReadOnlyList<SourceAdmission.Token> t,int i,
        string a,string b)
    {
        if(i<0||i>=t.Count||t[i].Value!=a)return -1;
        int d=0;
        for(int j=i;j<t.Count;j++)
        {
            if(t[j].Value==a)d++;
            else if(t[j].Value==b&&--d==0)return j;
        }
        return -1;
    }
    private static Method? Find(string s,string method,string firstFact,
        string secondFact)
    {
        var t=SourceAdmission.ReadTokens(s);Method? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="func"||t[i+1].Value!=method||t[i+2].Value!="(")continue;
            int args=Match(t,i+2,"(",")");if(args<0)return null;
            int open=args+1;
            while(open<t.Count&&t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count||t[open].Value!="{")return null;
            int close=Match(t,open,"{","}");if(close<0)return null;
            string body=s[(t[open].Start+1)..t[close].Start];
            if(!body.Contains(firstFact,StringComparison.Ordinal)||
               !body.Contains(secondFact,StringComparison.Ordinal))
            {
                i=close;continue;
            }
            if(found is not null)return null;
            found=new(t[i].Start,t[open].Start,t[close].Start,method);
            i=close;
        }
        return found;
    }
    private static readonly string[] Targets=
        ["OnAction","OnQuickSlotButtonHoldStartEvent","IsDefaultConditionMet"];
    private static Decision Rewrite(string source)
    {
        const string story="q001_ripperdoc_done",standalone="ep1_standalone";
        string nl=NL(source);
        var edits=new List<Edit>();
        var refused=new List<string>();
        int already=0;
        foreach(string name in Targets)
        {
            var method=Find(source,name,story,standalone);
            if(method is null)
            {
                refused.Add(name+":VCP_QUEST_OWNER_MISSING_OR_AMBIGUOUS");
                continue;
            }
            string body=source[(method.Open+1)..method.Close];
            var facts=Regex.Matches(body,
                @"(?<prefix>(?:let\s+[A-Za-z_]\w*(?:\s*:\s*Bool)?\s*=\s*))"+
                @"(?<receiver>(?:GameInstance\s*\.\s*GetQuestsSystem\s*\("+
                    @"(?<arg>(?:this\s*\.\s*GetGame\s*\(\s*\)|this\s*\.\s*m_playerPuppet\s*\.\s*GetGame\s*\(\s*\)|device\s*\.\s*GetGameInstance\s*\(\s*\)))"+
                    @"\s*\)|[A-Za-z_]\w*))"+
                @"\s*\.\s*GetFact\s*\(\s*n""(?<fact>q001_ripperdoc_done|ep1_standalone)""\s*\)",
                RegexOptions.CultureInvariant);
            if(facts.Count!=2 ||
               facts[0].Groups["fact"].Value==facts[1].Groups["fact"].Value)
            {
                refused.Add(name+":VCP_QUEST_FACT_FLOW_UNPROVEN");
                continue;
            }
            var direct=facts.Cast<Match>().Where(x=>
                x.Groups["receiver"].Value.Contains("GameInstance",StringComparison.Ordinal)).ToArray();
            var cached=facts.Cast<Match>().Where(x=>
                !x.Groups["receiver"].Value.Contains("GameInstance",StringComparison.Ordinal)).ToArray();
            if(direct.Length==0&&cached.Length==2)
            {
                string receiver=cached[0].Groups["receiver"].Value;
                if(receiver!=cached[1].Groups["receiver"].Value ||
                   !Has(body,@"\blet\s+"+Regex.Escape(receiver)+
                     @"\s*=\s*GameInstance\s*\.\s*GetQuestsSystem\s*\("))
                    refused.Add(name+":VCP_MANUAL_CACHE_NOT_PROVEN");
                else already++;
                continue;
            }
            string variable;
            string expression;
            if(direct.Length==2)
            {
                string a=direct[0].Groups["arg"].Value;
                string b=direct[1].Groups["arg"].Value;
                if(string.Concat(SourceAdmission.ReadTokens(a).Select(x=>x.Value))!=
                   string.Concat(SourceAdmission.ReadTokens(b).Select(x=>x.Value)))
                {
                    refused.Add(name+":VCP_QUEST_DIFFERENT_GAME_CONTEXT");
                    continue;
                }
                variable="gcoVcpQuests";
                if(Has(body,@"\b"+variable+@"\b"))
                {
                    refused.Add(name+":VCP_LOCAL_NAME_COLLISION");
                    continue;
                }
                expression=direct[0].Groups["receiver"].Value;
                // Insert at the start of the FIRST direct read's statement;
                // only accept two adjacent assignments in the same block.
                int before=direct[0].Index;
                int lineStart=body.LastIndexOf('\n',Math.Max(0,before-1))+1;
                string leading=body[lineStart..before];
                if(!Regex.IsMatch(leading,@"^\s*let\s+[A-Za-z_]\w*(?:\s*:\s*Bool)?\s*=\s*$"))
                {
                    refused.Add(name+":VCP_QUEST_STATEMENT_SHAPE_CHANGED");
                    continue;
                }
                // Derive the leading indentation from the original line.
                string indent=Regex.Match(leading,@"^\s*").Value;
                // The first assignment remains after the inserted local.
                Add(method.Open+1+lineStart,method.Open+1+lineStart,
                    indent+"let "+variable+" = "+expression+";"+nl);
            }
            else if(direct.Length==1&&cached.Length==1)
            {
                variable=cached[0].Groups["receiver"].Value;
                expression="";
                if(!Has(body,@"\blet\s+"+Regex.Escape(variable)+
                   @"\s*=\s*GameInstance\s*\.\s*GetQuestsSystem\s*\("))
                {
                    refused.Add(name+":VCP_PARTIAL_QUEST_LOCAL_UNPROVEN");
                    continue;
                }
            }
            else
            {
                refused.Add(name+":VCP_QUEST_PAIR_PARTIAL_UNPROVEN");continue;
            }
            foreach(var directRead in direct)
            {
                var receiver=directRead.Groups["receiver"];
                Add(method.Open+1+receiver.Index,
                    method.Open+1+receiver.Index+receiver.Length,variable);
            }
        }
        if(edits.Count==0)
            return new(refused.Count>0?"REFUSED":"COMPLETE",
                null,0,refused.ToArray(),already);
        var sorted=edits.Select((edit,index)=>(edit,index))
            .OrderByDescending(x=>x.edit.Start)
            .ThenByDescending(x=>x.edit.End)
            .ThenByDescending(x=>x.index)
            .Select(x=>x.edit).ToArray();
        for(int i=1;i<sorted.Length;i++)
            if(sorted[i].End>sorted[i-1].Start)
                throw new InvalidDataException("VCP_QUEST_EDITS_OVERLAP");
        string output=source;
        foreach(var e in sorted)
            output=output[..e.Start]+e.Value+output[e.End..];
        foreach(Match m in Regex.Matches(source,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if(!output.Contains(m.Value,StringComparison.Ordinal))
                throw new InvalidDataException("VCP_QUEST_GENERIC_LOST");
        if(!Has(output,@"\bwrappedMethod\s*\(\s*action\s*,\s*consumer\s*\)") ||
           !Has(output,@"\bwrappedMethod\s*\(\s*evt\s*\)") ||
           !output.Contains(story,StringComparison.Ordinal) ||
           !output.Contains(standalone,StringComparison.Ordinal))
            throw new InvalidDataException("VCP_QUEST_WRAPPERS_OR_FACTS_LOST");
        return new("APPLIED",output,edits.Count,refused.ToArray(),already);
    }
    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
           Inside(output,game))
            throw new InvalidDataException("VCP_QUEST_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("VCP_QUEST_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);_=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("VCP_QUEST_SOURCE_HASH_MISSING");
        string status="NOT_APPLICABLE",relative=null!;
        string? changed=null;
        int edits=0,complete=0;string[] refused=[];
        string root=Path.Combine(scripts,"Vehicle Call Plus");
        if(Directory.Exists(root))
        {
            var candidates=Directory.EnumerateFiles(root,"*.reds",
                SearchOption.AllDirectories).Where(f=>
                Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+VehicleFastTravelTracking\s+extends\s+ScriptedPuppetPS\b") &&
                Has(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+ActionVehicleServices\s+extends\s+ActionBool\b")).ToArray();
            if(candidates.Length!=1)
            {
                status="REFUSED";
                refused=[candidates.Length==0?"VCP_QUEST_OWNER_MISSING":
                    "VCP_QUEST_OWNER_AMBIGUOUS"];
            }
            else
            {
                string file=candidates[0];
                if((File.GetAttributes(file)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("VCP_QUEST_SOURCE_LINK_UNSAFE");
                relative="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
                string s=File.ReadAllText(file,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("VCP_QUEST_PREVIEW_ESCAPE");
                    if(File.Exists(staged))s=File.ReadAllText(staged,Encoding.UTF8);
                }
                var d=Rewrite(s);
                status=d.Status;changed=d.Changed;
                edits=d.Edits;refused=d.Refusals;complete=d.Complete;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("VCP_QUEST_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(changed is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("VCP_QUEST_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst,changed,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,edits,complete,
            relative,refused,true,false);
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
