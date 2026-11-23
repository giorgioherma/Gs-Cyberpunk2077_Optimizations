using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.1 DrugDealer input routing. Five independent owner-local
/// migrations. Original generic action filter and engine OnAction remain as
/// exclusive fallback; the hub shares the original gameplay, including the
/// different per-owner success-consumption semantics. No wholesale old files.
/// </summary>
internal static class DrugDealerInputHubPass
{
    internal const string Rule = "DRUG_DEALER_INPUT_HUB_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string[] Refusals,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Owner(string Class, string PlayerField, int Actions, bool Consumes);
    private sealed record Block(int Start, int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Reason, string? Updated, int Edits);

    private static readonly Owner[] Owners = [
        new("CrackhouseInputListener", "crackhouseInputListener", 2, true),
        new("DDInputListener", "ddInputListener", 1, false),
        new("LabInputListener", "labInputListener", 2, false),
        new("NightMarketInputListener", "nightMarketInputListener", 1, false),
        new("OperationInputListener", "operationInputListener", 2, false)
    ];
    private static readonly RegexOptions RX=RegexOptions.CultureInvariant;
    private static bool Has(string s,string p)=>Regex.IsMatch(s,p,RX);
    private static string NL(string s)=>s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
    private static bool Inside(string a,string b)=>Path.GetFullPath(a).StartsWith(
        Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,int at,string a,string b)
    {
        if(at<0||at>=t.Count||t[at].Value!=a)return -1;
        int d=0;
        for(int i=at;i<t.Count;i++)
        {
            if(t[i].Value==a)d++;
            else if(t[i].Value==b&&--d==0)return i;
        }
        return -1;
    }
    private static Block? FindMethod(string s,string name,string parameterWitness,
        string? ownerClass=null, bool wrapper=false)
    {
        var t=SourceAdmission.ReadTokens(s);
        int? clsStart=null,clsEnd=null;
        if(ownerClass is not null)
        {
            for(int i=0;i+2<t.Count;i++)
            {
                if(t[i].Value!="class"||t[i+1].Value!=ownerClass)continue;
                int open=i+2;
                while(open<t.Count&&t[open].Value is not("{" or ";"))open++;
                if(open>=t.Count||t[open].Value!="{")return null;
                int end=Close(t,open,"{","}");
                if(end<0||clsStart is not null)return null;
                clsStart=t[open].Start;clsEnd=t[end].Start;
            }
            if(clsStart is null)return null;
        }
        Block? found=null;
        for(int i=0;i+2<t.Count;i++)
        {
            if(t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")continue;
            if(clsStart is not null &&
               (t[i].Start<=clsStart.Value||t[i].Start>=clsEnd!.Value))continue;
            int argEnd=Close(t,i+2,"(",")");if(argEnd<0)return null;
            string args=string.Join(" ",t.Skip(i+3).Take(argEnd-i-3).Select(x=>x.Value));
            if(!args.Contains(parameterWitness,StringComparison.Ordinal))continue;
            if(wrapper)
            {
                string prev=s[Math.Max(0,t[i].Start-240)..t[i].Start];
                if(!Has(prev,@"@wrapMethod\s*\(\s*PlayerPuppet\s*\)\s*(?:(?:protected|private|public|cb|final)\s+)*$"))continue;
            }
            int body=argEnd+1;
            while(body<t.Count&&t[body].Value is not("{" or ";"))body++;
            if(body==t.Count||t[body].Value!="{")return null;
            int end=Close(t,body,"{","}");
            if(end<0||found is not null)return null;
            found=new(t[i].Start,t[body].Start,t[end].Start);
            i=end;
        }
        return found;
    }
    private static bool RuntimeAbi(string root)
    {
        bool r=false,h=false,l=false,e=false;
        foreach(string file in ProvidedRuntimeSourceScope.Files(root))
        {
            string s=File.ReadAllText(file,Encoding.UTF8);
            if(!Has(s,@"\bmodule\s+GRedRuntime\b"))continue;
            if(Has(s,@"\bclass\s+Runtime\b")&&Has(s,@"\bfunc\s+GetInputHub\s*\(")&&
               Has(s,@"\bfunc\s+IsReady\s*\(")&&Has(s,@"\bfunc\s+Get\s*\(\s*game\s*:\s*GameInstance"))r=true;
            if(Has(s,@"\bclass\s+InputHub\b")&&Has(s,@"\bfunc\s+Subscribe\s*\(")&&
               Has(s,@"\bfunc\s+Unsubscribe\s*\("))h=true;
            if(Has(s,@"\bclass\s+InputListener\b"))l=true;
            if(Has(s,@"\bclass\s+InputEvent\b"))e=true;
            if(r&&h&&l&&e)return true;
        }
        return false;
    }
    private static bool ProveActionHelpers(string root)
    {
        bool foundHelpers=false,foundMapping=false;
        foreach(string f in Directory.EnumerateFiles(root,"*.reds",SearchOption.AllDirectories))
        {
            if(!f.Replace('\\','/').Contains("/DrugDealer/",StringComparison.OrdinalIgnoreCase))continue;
            string s=File.ReadAllText(f,Encoding.UTF8);
            if(Has(s,@"\bfunc\s+IsMainAction\s*\(")&&Has(s,@"\bfunc\s+IsSecondaryAction\s*\("))
            {
                // Prove action selectors use the same index and HOLD_COMPLETE
                // invariant as the original engine callback.
                if(!Has(s,@"IsMainAction\s*\([^)]*\)[^{]*\{[^}]*registeredActions\s*\[\s*0\s*\]")||
                   !Has(s,@"IsSecondaryAction\s*\([^)]*\)[^{]*\{[^}]*registeredActions\s*\[\s*1\s*\]")||
                   !Has(s,@"BUTTON_HOLD_COMPLETE"))return false;
                foundHelpers=true;
            }
            if(Has(s,@"\bfunc\s+InputRegisteredActions\s*\("))
            {
                // The static current CName array is the source of truth,
                // not an assumed mod version or historical hardcoded file.
                if(!Has(s,"InputRegisteredActions\\s*\\(\\s*\\)\\s*->\\s*array\\s*<\\s*CName\\s*>\\s*=\\s*\\[\\s*n\"DDInteractionActionMain\"\\s*,\\s*n\"DDInteractionActionSecondary\"\\s*\\]"))
                    return false;
                foundMapping=true;
            }
        }
        return foundHelpers&&foundMapping;
    }
    private static string FieldName(Owner o,string suffix)=>"gco"+o.Class+suffix;

    private static string HubOnAttached(Owner o,string nl)
    {
        string listener="this."+o.PlayerField;
        string hub=FieldName(o,"Hub"), main=FieldName(o,"MainId"),
            secondary=FieldName(o,"SecondaryId"),fallback=FieldName(o,"EngineFallback");
        var rows=new List<string>{
            "this."+hub+" = null;",
            "this."+main+" = 0;"
        };
        if(o.Actions==2)rows.Add("this."+secondary+" = 0;");
        rows.AddRange([
            "this."+fallback+" = false;",
            "let gcoDdr = Runtime.Get(this.GetGame());",
            "if IsDefined(gcoDdr) && gcoDdr.IsReady() {",
            "  let gcoHub = gcoDdr.GetInputHub();",
            "  if IsDefined(gcoHub) {",
            "    this."+main+" = gcoHub.Subscribe(n\"DDInteractionActionMain\", "+listener+");"
        ]);
        if(o.Actions==2)rows.Add("    this."+secondary+" = gcoHub.Subscribe(n\"DDInteractionActionSecondary\", "+listener+");");
        string ok="this."+main+">0"+(o.Actions==2?" && this."+secondary+">0":"");
        rows.Add("    if "+ok+" {");
        rows.Add("      this."+hub+" = gcoHub;");
        rows.Add("    } else {");
        rows.Add("      if this."+main+">0 { gcoHub.Unsubscribe(this."+main+"); }");
        if(o.Actions==2)rows.Add("      if this."+secondary+">0 { gcoHub.Unsubscribe(this."+secondary+"); }");
        rows.Add("      this."+main+" = 0;");
        if(o.Actions==2)rows.Add("      this."+secondary+" = 0;");
        rows.AddRange([
            "    }",
            "  }",
            "}",
            "if !IsDefined(this."+hub+") {",
            "  this.RegisterInputListener("+listener+");",
            "  this."+fallback+" = true;",
            "}"
        ]);
        return string.Join(nl+"    ",rows);
    }
    private static string HubOnDetached(Owner o,string nl)
    {
        string hub=FieldName(o,"Hub"),main=FieldName(o,"MainId"),
            secondary=FieldName(o,"SecondaryId"),fallback=FieldName(o,"EngineFallback");
        var rows=new List<string>{
            "if IsDefined(this."+hub+") {",
            "  if this."+main+">0 { this."+hub+".Unsubscribe(this."+main+"); }"
        };
        if(o.Actions==2)rows.Add("  if this."+secondary+">0 { this."+hub+".Unsubscribe(this."+secondary+"); }");
        rows.AddRange([
            "}",
            "this."+hub+" = null;",
            "this."+main+" = 0;"
        ]);
        if(o.Actions==2)rows.Add("this."+secondary+" = 0;");
        rows.AddRange([
            "if this."+fallback+" { this.UnregisterInputListener(this."+o.PlayerField+"); }",
            "this."+fallback+" = false;"
        ]);
        return string.Join(nl+"    ",rows);
    }
    private static string HubCallback(Owner o,string nl)
    {
        string helper=FieldName(o,"Handle");
        string actions="!Equals(evt.actionName,n\"DDInteractionActionMain\")"+
            (o.Actions==2?" && !Equals(evt.actionName,n\"DDInteractionActionSecondary\")":"");
        string call="this."+helper+"(evt.actionName)";
        string[] lines=[
            "public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
            "    if !IsDefined(evt) || !Equals(evt.actionType,gameinputActionType.BUTTON_HOLD_COMPLETE) { return; }",
            "    if "+actions+" { return; }",
            o.Consumes?"    if "+call+" { evt.consumed = true; }":"    "+call+";",
            "}"
        ];
        return string.Join(nl+"    ",lines);
    }
    private static Decision Rewrite(string s,Owner o)
    {
        string nl=NL(s);
        if(!Has(s,@"\bmodule\s+DrugDealer\b")||!Has(s,@"\bclass\s+"+Regex.Escape(o.Class)+@"\b"))
            return new("REFUSED","DD_HUB_OWNER_CLASS_OR_MODULE_MISSING",null,0);
        Block? action=FindMethod(s,"OnAction","action : ListenerAction",o.Class);
        Block? attach=FindMethod(s,"OnGameAttached","",wrapper:true);
        Block? detach=FindMethod(s,"OnDetach","",wrapper:true);
        if(action is null||attach is null||detach is null)
            return new("REFUSED","DD_HUB_REQUIRED_CALLBACK_MISSING",null,0);
        string body=s[(action.Open+1)..action.Close],att=s[(attach.Open+1)..attach.Close],det=s[(detach.Open+1)..detach.Close];
        if(!Has(body,@"\bListenerAction\s*\.\s*GetName\s*\(\s*action\s*\)")||
           !Has(body,@"\bBUTTON_HOLD_COMPLETE\b")||
           !Has(body,@"\bACTION_LITERAL_MEMBERSHIP_V1\b")||
           !Has(att,@"\bIsPreGame\s*\(")||
           !Has(att,@"\bthis\s*\.\s*"+Regex.Escape(o.PlayerField)+@"\s*=\s*new\s+"+Regex.Escape(o.Class))||
           !Has(det,@"\bthis\s*\.\s*"+Regex.Escape(o.PlayerField)+@"\s*=\s*null"))
            return new("REFUSED","DD_HUB_ORIGINAL_GENERIC_OR_LIFECYCLE_UNPROVEN",null,0);
        var edits=new List<Edit>();
        void Add(int a,int b,string text)=>edits.Add(new(a,b,text));
        string helper=FieldName(o,"Handle");
        var helperMethod=FindMethod(s,helper,"",o.Class);
        if(helperMethod is null)
        {
            var kind=Regex.Match(body,@"if\s+!Equals\s*\(\s*ListenerAction\s*\.\s*GetType\s*\(\s*action\s*\)\s*,\s*gameinputActionType\.BUTTON_HOLD_COMPLETE\s*\)\s*\{\s*return\s+false\s*;\s*\}",RX);
            if(!kind.Success)return new("REFUSED","DD_HUB_ACTION_TYPE_GUARD_UNPROVEN",null,0);
            string work=body[(kind.Index+kind.Length)..];
            if(!Has(work,@"\bIsMainAction\s*\(\s*action\s*\)"))
                return new("REFUSED","DD_HUB_MAIN_ACTION_GUARD_MISSING",null,0);
            work=Regex.Replace(work,@"\bIsMainAction\s*\(\s*action\s*\)","Equals(name, n\"DDInteractionActionMain\")",RX);
            work=Regex.Replace(work,@"\bIsSecondaryAction\s*\(\s*action\s*\)","Equals(name, n\"DDInteractionActionSecondary\")",RX);
            if(SourceAdmission.ReadTokens(work).Any(t=>t.Value=="action")||Has(work,@"\bListenerAction\s*\."))
                return new("REFUSED","DD_HUB_ACTION_REFERENCE_ESCAPES_SHARED_HANDLER",null,0);
            if(o.Consumes && !Has(work,@"\breturn\s+true\s*;"))
                return new("REFUSED","DD_HUB_TRUE_CONSUMPTION_NOT_FOUND",null,0);
            if(!o.Consumes && Has(work,@"\breturn\s+true\s*;"))
                return new("REFUSED","DD_HUB_CHANGED_CONSUMPTION_BEHAVIOR",null,0);
            if(!o.Consumes)work=Regex.Replace(work,@"\breturn\s+false\s*;","return;",RX);
            string call="this."+helper+"(actionName)";
            string replacement=nl+"        "+(o.Consumes?"return "+call+";":call+";")+nl+"    ";
            Add(action.Open+1+kind.Index+kind.Length,action.Close,replacement);
            string helperText=nl+"    private func "+helper+"(name: CName) -> "+(o.Consumes?"Bool":"Void")+" {"+
                work.TrimEnd()+nl+"    }"+nl;
            Add(action.Close+1,action.Close+1,helperText);
        }
        else if(!Has(body,Regex.Escape(helper)+@"\s*\("))
            return new("REFUSED","DD_HUB_EXISTING_HANDLER_UNRELATED",null,0);
        if(!Has(s,@"\bclass\s+"+Regex.Escape(o.Class)+@"\s+extends\s+InputListener\b"))
        {
            var cls=Regex.Match(s,@"\bpublic\s+class\s+"+Regex.Escape(o.Class)+@"\s*\{",RX);
            if(!cls.Success)return new("REFUSED","DD_HUB_LISTENER_BASE_INCOMPATIBLE",null,0);
            Add(cls.Index,cls.Index+cls.Length,cls.Value.Replace("{","extends InputListener {"));
        }
        if(!Has(s,@"\bfunc\s+OnGRedInput\s*\("))
            Add(action.Close+1,action.Close+1,nl+"    "+HubCallback(o,nl)+nl);
        else if(o.Consumes&&!Has(s,@"\bevt\s*\.\s*consumed\s*=\s*true"))
            return new("REFUSED","DD_HUB_EXISTING_EVENT_CONSUMPTION_UNPROVEN",null,0);
        string hub=FieldName(o,"Hub"),main=FieldName(o,"MainId"),
            second=FieldName(o,"SecondaryId"),fallback=FieldName(o,"EngineFallback");
        var fields=new List<string>{hub+": wref<InputHub>",main+": Int32",fallback+": Bool"};
        if(o.Actions==2)fields.Add(second+": Int32");
        var missing=new List<string>();
        foreach(string field in fields)
        {
            string key=field[..field.IndexOf(':')],type=field[(field.IndexOf(':')+1)..].Trim();
            var found=Regex.Matches(s,@"\blet\s+"+Regex.Escape(key)+@"\s*:\s*(?<type>[^;]+);",RX);
            if(found.Count>1 ||(found.Count==1 &&
                string.Concat(found[0].Groups["type"].Value.Where(c=>!char.IsWhiteSpace(c)))!=
                string.Concat(type.Where(c=>!char.IsWhiteSpace(c)))))
                return new("REFUSED","DD_HUB_FIELD_COLLISION:"+key,null,0);
            if(found.Count==0)missing.Add(field);
        }
        if(missing.Count>0)
        {
            var original=Regex.Match(s,@"@addField\s*\(\s*PlayerPuppet\s*\)\s*private\s+let\s+"+Regex.Escape(o.PlayerField)+@"\s*:",RX);
            if(!original.Success)return new("REFUSED","DD_HUB_ORIGINAL_PLAYER_FIELD_MISSING",null,0);
            Add(original.Index,original.Index,string.Join(nl,missing.Select(f=>
                "@addField(PlayerPuppet)"+nl+"private let "+f+";"))+nl+nl);
        }
        var r=Regex.Match(att,@"\bthis\s*\.\s*RegisterInputListener\s*\(\s*this\s*\.\s*"+Regex.Escape(o.PlayerField)+@"\s*\)\s*;",RX);
        if(r.Success&&!Has(att,Regex.Escape(main)))
            Add(attach.Open+1+r.Index,attach.Open+1+r.Index+r.Length,HubOnAttached(o,nl));
        else if(!Has(att,Regex.Escape(fallback)+@"\s*=\s*true"))
            return new("REFUSED","DD_HUB_ATTACH_STATE_UNPROVEN",null,0);
        var d=Regex.Match(det,@"\bthis\s*\.\s*UnregisterInputListener\s*\(\s*this\s*\.\s*"+Regex.Escape(o.PlayerField)+@"\s*\)\s*;",RX);
        if(d.Success&&!Has(det,Regex.Escape(main)))
            Add(detach.Open+1+d.Index,detach.Open+1+d.Index+d.Length,HubOnDetached(o,nl));
        else if(!Has(det,Regex.Escape(fallback))||!Has(det,@"\bUnsubscribe\s*\("))
            return new("REFUSED","DD_HUB_DETACH_STATE_UNPROVEN",null,0);
        if(!Has(s,@"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var mod=Regex.Match(s,@"\bmodule\s+DrugDealer\.[\w\.]+\s*\r?\n",RX);
            if(!mod.Success)return new("REFUSED","DD_HUB_MODULE_IMPORT_ANCHOR_MISSING",null,0);
            Add(mod.Index+mod.Length,mod.Index+mod.Length,"import GRedRuntime.*"+nl);
        }
        if(edits.Count==0)return new("COMPLETE",null,null,0);
        var ordered=edits.OrderByDescending(x=>x.Start).ThenByDescending(x=>x.End).ToArray();
        for(int i=1;i<ordered.Length;i++)if(ordered[i].End>ordered[i-1].Start)
            return new("REFUSED","DD_HUB_EDIT_OVERLAP",null,0);
        string updated=s;
        foreach(var e in ordered)updated=updated[..e.Start]+e.Text+updated[e.End..];
        if(!Has(updated,@"\bclass\s+"+Regex.Escape(o.Class)+@"\s+extends\s+InputListener")||
           !Has(updated,@"\bfunc\s+OnGRedInput\s*\(")||
           !Has(updated,Regex.Escape(helper)+@"\s*\(")||
           !Has(updated,Regex.Escape(fallback)+@"\s*=\s*true"))
            throw new InvalidDataException("DD_HUB_POSTCONDITION_FAILED:"+o.Class);
        foreach(Match marker in Regex.Matches(s,@"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+",RX))
            if(!updated.Contains(marker.Value,StringComparison.Ordinal))
                throw new InvalidDataException("DD_HUB_GENERIC_LOST:"+o.Class);
        return new("APPLIED",null,updated,edits.Count);
    }

    internal static Result Generate(string gameRoot,string outputRoot,string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot),
            root=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(root)||Directory.Exists(output)||File.Exists(output)||Inside(output,game))
            throw new InvalidDataException("DD_HUB_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
             Inside(basePreviewRoot,output)||Inside(output,basePreviewRoot)))
            throw new InvalidDataException("DD_HUB_OVERLAY_UNSAFE");
        var admit=new SourceAdmission(game);_=admit.AuditAll();
        string initial=admit.SourceStackSha256??throw new InvalidDataException("DD_HUB_HASH_MISSING");
        string ownerPath=Path.Combine(root,"DrugDealer");
        var paths=new List<string>();var problems=new List<string>();
        int edits=0,complete=0,found=0;
        var staged=new List<(string Relative,string Content)>();
        if(Directory.Exists(ownerPath))
        {
            bool runtime=RuntimeAbi(root),helpers=ProveActionHelpers(root);
            foreach(var owner in Owners)
            {
                var files=Directory.EnumerateFiles(ownerPath,"*.reds",SearchOption.AllDirectories)
                    .Where(f=>Has(File.ReadAllText(f,Encoding.UTF8),@"\bclass\s+"+Regex.Escape(owner.Class)+@"\b"))
                    .ToArray();
                if(files.Length==0)continue;
                found++;
                if(files.Length!=1){problems.Add(owner.Class+":SOURCE_AMBIGUOUS");continue;}
                if(!runtime){problems.Add(owner.Class+":RUNTIME_API_MISSING");continue;}
                if(!helpers){problems.Add(owner.Class+":ACTION_HELPERS_UNPROVEN");continue;}
                string path=files[0];
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("DD_HUB_SOURCE_LINK_UNSAFE");
                string relative="r6/scripts/"+Path.GetRelativePath(root,path).Replace('\\','/');
                string source=File.ReadAllText(path,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string effective=Path.GetFullPath(Path.Combine(basePreviewRoot,relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(effective,basePreviewRoot))throw new InvalidDataException("DD_HUB_PREVIEW_ESCAPE");
                    if(File.Exists(effective))source=File.ReadAllText(effective,Encoding.UTF8);
                }
                Decision d=Rewrite(source,owner);
                if(d.Status=="APPLIED"&&d.Updated is not null)
                {
                    edits+=d.Edits;paths.Add(relative);staged.Add((relative,d.Updated));
                }
                else if(d.Status=="COMPLETE")complete++;
                else problems.Add(owner.Class+":"+(d.Reason??"UNPROVEN"));
            }
        }
        if(!string.Equals(initial,admit.FingerprintCurrentSources(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DD_HUB_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        foreach(var f in staged)
        {
            string file=Path.GetFullPath(Path.Combine(output,f.Relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(file,output)||!file.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DD_HUB_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file,f.Content,new UTF8Encoding(false));
        }
        string status=staged.Count>0?"APPLIED":problems.Count>0?"REFUSED":
            complete>0?"COMPLETE":"NOT_APPLICABLE";
        Result result=new(Rule,status,edits,complete,paths.ToArray(),problems.ToArray(),true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true}),
            new UTF8Encoding(false));
        return result;
    }
}
