using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Throttle only routine Dark Future buff ICON SetData drawing to 2.5 Hz.
/// New status records get a first draw; all actual effects, updates, icons,
/// timer inputs, callbacks, visibility, removal and stack data remain live.
/// A missing clock uses the original per-callback behavior.
/// </summary>
internal static class DarkFutureStatusIconCadencePass
{
    internal const string Rule = "DARK_FUTURE_STATUS_ICON_400MS_V1";
    private const string Flag = "m_gcoDfIconCadenceInitialized";
    private const string Last = "m_gcoDfIconCadenceLast";
    private const string Next = "m_gcoDfIconCadenceNext";
    private const string Due = "__gcoDfIconVisualDue";
    private const string Force = "__gcoDfIconRecordChanged";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Edit(int Start, int End, string Value);
    private sealed record Inspection(string Status, string? Reason,
        List<Edit> Edits);

    private static bool At(IReadOnlyList<SourceAdmission.Token> t,int i,string v)
        => i>=0 && i<t.Count && t[i].Value==v;
    private static bool Sequence(IReadOnlyList<SourceAdmission.Token> t,
        int i,params string[] values)
        => values.Select((v,j)=>At(t,i+j,v)).All(x=>x);
    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start,string open,string close)
    {
        if(!At(t,start,open))return -1;
        int depth=0;
        for(int i=start;i<t.Count;i++)
        {
            if(At(t,i,open))depth++;
            else if(At(t,i,close) && --depth==0)return i;
        }
        return -1;
    }
    private static string LineIndent(string source,int offset)
    {
        int start=source.LastIndexOf('\n',Math.Max(offset-1,0))+1;
        string prefix=source[start..offset];
        if(prefix.Any(c=>c!=' ' && c!='\t'))
            throw new InvalidDataException("DF_ICON_INSERTION_NOT_LINE_ALIGNED");
        return prefix;
    }
    // Compare the actual REDscript tokens and literal values, not the exact
    // whitespace/comments of a historical generated gate.
    private static bool EndsWithGuard(string source,
        IReadOnlyList<SourceAdmission.Token> tokens, int end,
        string guard, out int begin)
    {
        var candidates=new[]{
            guard,
            "if ("+guard[3..^2]+") {"
        };
        foreach(string expectedSource in candidates)
        {
            var expected=SourceAdmission.ReadTokens(expectedSource);
            if(end<expected.Count)continue;
            int start=end-expected.Count;
            bool same=true;
            for(int j=0;j<expected.Count;j++)
            {
                var actual=tokens[start+j];
                var pattern=expected[j];
                string actualValue=actual.Value=="<literal>"
                    ? source[actual.Start..actual.End]:actual.Value;
                string patternValue=pattern.Value=="<literal>"
                    ? expectedSource[pattern.Start..pattern.End]:pattern.Value;
                if(!string.Equals(actualValue,patternValue,
                    StringComparison.Ordinal)){same=false;break;}
            }
            if(!same)continue;
            begin=tokens[start].Start;
            return true;
        }
        begin=-1;
        return false;
    }

    private static string TargetVisualGate(string record)
    {
        // The broad DarkFutureCanHideOnBuffBar tag also belongs to addiction
        // withdrawal and injury effects. Only these four independently
        // identified needs penalties are approved for 400 ms icon redraw.
        string tags=record+".GameplayTags()";
        string[] needs=[
            "DarkFutureNeedNerve", "DarkFutureNeedHydration",
            "DarkFutureNeedNutrition", "DarkFutureNeedEnergy"
        ];
        string membership=string.Join(" || ",needs.Select(name=>
            "ArrayContains("+tags+", n\""+name+"\")"));
        return "if "+Due+" || "+Force+" || !("+membership+") {";
    }

    private static Inspection Inspect(string text)
    {
        static Inspection Refuse(string reason)=>new("REFUSED",reason,[]);
        var t=SourceAdmission.ReadTokens(text);
        var methods=new List<(int Annotation,int Body,int End)>();
        for(int i=0;i<t.Count-7;i++)
        {
            if(!Sequence(t,i,"@","replaceMethod","(",
                "buffListGameController",")"))continue;
            for(int j=i+5;j<t.Count && j<i+35;j++)
            {
                if(At(t,j,"@"))break;
                if(!Sequence(t,j,"func","UpdateBuffDebuffList","("))continue;
                int argsEnd=Close(t,j+2,"(",")");
                if(argsEnd<0)break;
                int open=argsEnd+1;
                while(open<t.Count && open<argsEnd+16 && !At(t,open,"{")
                    && !At(t,open,";"))open++;
                if(!At(t,open,"{"))break;
                int end=Close(t,open,"{","}");
                if(end<0)return Refuse("DF_ICON_METHOD_BODY_UNBALANCED");
                methods.Add((i,open,end));
                break;
            }
        }
        if(methods.Count!=1)
            return Refuse("DF_ICON_OWNER_METHOD_UNPROVEN");
        var method=methods[0];
        int setIndex=-1,setEnd=-1,count=0;
        string? receiver=null;
        for(int i=method.Body+1;i+3<method.End;i++)
        {
            if(!At(t,i+1,".") || !At(t,i+2,"SetData") ||
                !At(t,i+3,"("))continue;
            int end=Close(t,i+3,"(",")");
            if(end<0 || !At(t,end+1,";"))
                return Refuse("DF_ICON_SETDATA_EXPRESSION_UNPROVEN");
            count++;setIndex=i;setEnd=end+1;
            receiver=t[i].Value;
        }
        if(count!=1 || receiver is null)
            return Refuse("DF_ICON_SETDATA_NOT_UNIQUE");
        string body=text[t[method.Body].End..t[method.End].Start];
        if(!body.Contains("m_buffWidgets",StringComparison.Ordinal) ||
           !body.Contains("SetStatusEffectRecord",StringComparison.Ordinal) ||
           !body.Contains("onScreenBuffsCount",StringComparison.Ordinal))
            return Refuse("DF_ICON_WIDGET_POOL_FLOW_UNPROVEN");
        string recordPattern=@"\bif\s+(?<record>[a-zA-Z_]\w*)\s*!=\s*"+
            Regex.Escape(receiver)+
            @"\s*\.\s*GetStatusEffectRecord\s*\(\s*\)\s*\{";
        var matches=Regex.Matches(body,recordPattern);
        if(matches.Count!=1)
            return Refuse("DF_ICON_RECORD_IDENTITY_FLOW_UNPROVEN");
        var change=matches[0];
        int condition=text.IndexOf(body,StringComparison.Ordinal)+change.Index;
        int recordOpen=condition+change.Length-1;
        int recordClose=text.IndexOf('}',recordOpen+1);
        if(recordClose<0 || recordClose>=t[setIndex].Start ||
           !text[(recordOpen+1)..recordClose]
            .Contains("SetStatusEffectRecord",StringComparison.Ordinal))
            return Refuse("DF_ICON_RECORD_CHANGE_BRANCH_UNPROVEN");
        string nl=text.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var edits=new List<Edit>();

        // Add only missing state fields. Existing typed fields are preserved;
        // a differently typed declaration is an actual collision.
        string[] fields=[
            "@addField(buffListGameController)"+nl+
                "private let "+Flag+": Bool;",
            "@addField(buffListGameController)"+nl+
                "private let "+Last+": Float;",
            "@addField(buffListGameController)"+nl+
                "private let "+Next+": Float;"
        ];
        var missing=new List<string>();
        foreach(string field in fields)
        {
            var tokens=SourceAdmission.ReadTokens(field);
            bool found=false;
            for(int i=0;i+tokens.Count<=t.Count;i++)
                if(tokens.Select((x,j)=>At(t,i+j,x.Value)).All(x=>x))
                {found=true;break;}
            if(!found)
            {
                string name=field.Split("private let ")[1].Split(':')[0];
                for(int i=0;i<t.Count-2;i++)
                    if(Sequence(t,i,"let",name,":"))
                        return Refuse("DF_ICON_FIELD_DECLARATION_CONFLICT");
                missing.Add(field);
            }
        }
        if(missing.Count>0)
            edits.Add(new(t[method.Annotation].Start,t[method.Annotation].Start,
                string.Join(nl+nl,missing)+nl+nl));

        const string marker="let "+Due+": Bool = true;";
        bool hasPrelude=body.Contains(marker,StringComparison.Ordinal);
        string[] proof=[
            "GetPlayerControlledObject()",
            "GameInstance.GetTimeSystem(__gcoDfPlayer.GetGame())",
            "EngineTime.ToFloat",
            ".GetSimTime()",
            "now + 0.40",
            Flag+" = true",
            Last+" = now",
            Next+" = now + 0.40",
            Due+" = !this."+Flag
        ];
        bool partialCadence=!hasPrelude &&
            (body.Contains(Due,StringComparison.Ordinal) ||
             body.Contains("m_gcoDfIconCadence",StringComparison.Ordinal));
        if((hasPrelude || partialCadence) &&
            proof.Any(x=>!body.Contains(x,StringComparison.Ordinal)))
            return Refuse("DF_ICON_PARTIAL_CADENCE_BLOCK_UNPROVEN");
        // Validate clock fallback not only while repairing a partial
        // cadence, but also before declaring a custom source COMPLETE.
        // A reversed null guard would otherwise silently pass admission.
        if(hasPrelude || partialCadence)
        {
            string[] clockProof=[
                "let __gcoDfPlayer = this.GetPlayerControlledObject();",
                "this."+Flag+" = true;",
                "this."+Last+" = now;",
                "this."+Next+" = now + 0.40;"
            ];
            // Use the canonical source lexer for guards so comments and
            // whitespace in compatible custom versions never invalidate a
            // correctly proven positive null guard. Negated guards refuse.
            var clockTokens=SourceAdmission.ReadTokens(body);
            bool IsGuard(string name)=>Enumerable.Range(0,clockTokens.Count)
                .Any(i=>Sequence(clockTokens,i,"if","IsDefined","(",name,")","{")
                    || Sequence(clockTokens,i,"if","(","IsDefined","(",
                        name,")",")","{"));
            bool dueGuard=Enumerable.Range(0,clockTokens.Count)
                .Any(i=>Sequence(clockTokens,i,"if",Due,"{")
                    || Sequence(clockTokens,i,"if","(",Due,")","{"));
            if(clockProof.Any(x=>!body.Contains(x,StringComparison.Ordinal)) ||
                !IsGuard("__gcoDfPlayer") ||
                !IsGuard("__gcoDfTimeSystem") || !dueGuard)
                return Refuse("DF_ICON_PARTIAL_CLOCK_CONTROL_FLOW_UNPROVEN");
        }
        // A partial source can retain the entire proven clock, yet lack
        // only the cadence declaration. Add exactly that missing statement.
        if(partialCadence)
            edits.Add(new(t[method.Body].End,t[method.Body].End,
                nl+"    let "+Due+": Bool = true;"));
        if(!hasPrelude && !partialCadence)
        {
            string prelude=string.Join(nl,[
                "", "    let "+Due+": Bool = true;",
                "    let __gcoDfPlayer = this.GetPlayerControlledObject();",
                "    if IsDefined(__gcoDfPlayer) {",
                "        let __gcoDfTimeSystem: ref<TimeSystem> = "+
                    "GameInstance.GetTimeSystem(__gcoDfPlayer.GetGame());",
                "        if IsDefined(__gcoDfTimeSystem) {",
                "            let now: Float = EngineTime.ToFloat(__gcoDfTimeSystem.GetSimTime());",
                "            "+Due+" = !this."+Flag+
                    " || now < this."+Last+" || now >= this."+Next+";",
                "            if "+Due+" {",
                "                this."+Flag+" = true;",
                "                this."+Last+" = now;",
                "                this."+Next+" = now + 0.40;",
                "            };",
                "        };",
                "    };",
                "    let "+Force+": Bool;",
                ""
            ]);
            edits.Add(new(t[method.Body].End,t[method.Body].End,prelude));
        }
        if((hasPrelude || partialCadence) &&
            !body.Contains("let "+Force+": Bool;",StringComparison.Ordinal))
        {
            // Independent partial: existing cadence, missing per-record flag.
            edits.Add(new(t[method.Body].End,t[method.Body].End,
                nl+"    let "+Force+": Bool;"));
        }

        string prefix=text[t[method.Body].End..condition];
        bool reset=Regex.IsMatch(prefix,@"\b"+Force+@"\s*=\s*false\s*;");
        if(!reset)
        {
            string indent;
            try{indent=LineIndent(text,condition);}
            catch(InvalidDataException){return Refuse("DF_ICON_RECORD_BRANCH_NOT_ALIGNED");}
            edits.Add(new(condition,condition,Force+" = false;"+nl+indent));
        }
        bool forced=Regex.IsMatch(text[(recordOpen+1)..recordClose],
            @"\b"+Force+@"\s*=\s*true\s*;");
        if(!forced)
            edits.Add(new(recordOpen+1,recordOpen+1,
                nl+"        "+Force+" = true;"));

        // A single rendering-only gate. No original callback or record setter
        // is removed. The first draw after a new effect identity is immediate.
        string setLineIndent;
        try{setLineIndent=LineIndent(text,t[setIndex].Start);}
        catch(InvalidDataException){return Refuse("DF_ICON_SETDATA_NOT_ALIGNED");}
        // DarkFutureCanHideOnBuffBar is NOT specific to the four approved
        // needs icons. Do not throttle addictions, injuries or unrelated
        // effects that happen to share that broad tag.
        string record=change.Groups["record"].Value;
        string gate=TargetVisualGate(record);
        string legacyGate="if "+Due+" || "+Force+
            " || !ArrayContains("+record+
            ".GameplayTags(), n\"DarkFutureCanHideOnBuffBar\") {";
        bool guarded=EndsWithGuard(text,t,setIndex,gate,out int currentStart);
        bool legacy=EndsWithGuard(text,t,setIndex,legacyGate,out int legacyStart);
        if(!guarded && legacy)
        {
            // Upgrade a partially/already optimized prior generated guard
            // in-place. Replacing just the predicate preserves the owner's
            // original SetData, visual cadence and all unrelated source.
            edits.Add(new(legacyStart,t[setIndex-1].End,gate));
        }
        else if(!guarded)
        {
            if(At(t,setIndex-1,"{"))
                return Refuse("DF_ICON_UNKNOWN_SETDATA_GUARD");
            edits.Add(new(t[setIndex].Start,t[setIndex].Start,
                gate+nl+setLineIndent));
            edits.Add(new(t[setEnd].End,t[setEnd].End,
                nl+setLineIndent+"};"));
        }
        else
        {
            string nextText=text[t[setEnd].End..];
            if(!Regex.IsMatch(nextText,@"^\s*\}"))
                return Refuse("DF_ICON_EXISTING_SETDATA_GUARD_UNBALANCED");
        }

        return new(edits.Count==0?"COMPLETE":"ELIGIBLE",null,edits);
    }
    private static string Apply(string source,IEnumerable<Edit> changes)
    {
        foreach(var e in changes.OrderByDescending(x=>x.Start)
            .ThenByDescending(x=>x.End))
            source=source[..e.Start]+e.Value+source[e.End..];
        return source;
    }
    internal static Result Generate(string gameRoot,string outputRoot,
        string? priorRoot=null)
    {
        string game=Path.GetFullPath(gameRoot),output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts) || Directory.Exists(output) ||
           File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("DF_ICON_OUTPUT_NOT_ISOLATED");
        if(priorRoot is not null && (!Directory.Exists(priorRoot) ||
           Inside(priorRoot,game)||Inside(output,priorRoot)||
           Inside(priorRoot,output)))
            throw new InvalidDataException("DF_ICON_PRIOR_STAGE_INVALID");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        var hash=admission.SourceStackSha256 ??
            throw new InvalidDataException("DF_ICON_SOURCE_FINGERPRINT_MISSING");
        bool ownerPresent=Directory.Exists(Path.Combine(scripts,"Dark Future"));
        var found=new List<(string Path,string Source)>();
        foreach(string p in Directory.EnumerateFiles(scripts,"*.reds",
            SearchOption.AllDirectories))
        {
            if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("DF_ICON_LINKED_SOURCE");
            string s=File.ReadAllText(p,Encoding.UTF8);
            bool owner=Path.GetRelativePath(scripts,p)
                .Split(Path.DirectorySeparatorChar)
                .Any(x=>x.Equals("Dark Future",StringComparison.OrdinalIgnoreCase))
                || s.Contains("import DarkFuture.",StringComparison.Ordinal);
            if(!owner)continue;
            ownerPresent=true;
            if(s.Contains("UpdateBuffDebuffList",StringComparison.Ordinal) &&
                s.Contains("buffListGameController",StringComparison.Ordinal))
                found.Add((p,s));
        }
        Result outcome;string? relative=null;string? optimized=null;
        if(found.Count!=1)
            outcome=new(Rule,found.Count>1||ownerPresent?"REFUSED":"NOT_APPLICABLE",
                0,0,null,found.Count>1?"DF_ICON_MULTIPLE_TARGET_FILES":
                ownerPresent?"DF_ICON_TARGET_MISSING":"DF_NOT_INSTALLED",true,false);
        else
        {
            var (p,original)=found[0];
            relative="r6/scripts/"+
                Path.GetRelativePath(scripts,p).Replace('\\','/');
            string effective=original;
            if(priorRoot is not null)
            {
                var overlay=Path.GetFullPath(Path.Combine(priorRoot,
                    relative.Replace('/',Path.DirectorySeparatorChar)));
                if(!Inside(overlay,priorRoot))
                    throw new InvalidDataException("DF_ICON_OVERLAY_PATH_ESCAPE");
                if(File.Exists(overlay))
                    effective=File.ReadAllText(overlay,Encoding.UTF8);
            }
            var inspect=Inspect(effective);
            if(inspect.Status=="ELIGIBLE")
            {
                optimized=Apply(effective,inspect.Edits);
                var verify=Inspect(optimized);
                if(verify.Status!="COMPLETE")
                    throw new InvalidDataException("DF_ICON_GENERATED_INVALID:"+
                        verify.Reason);
                outcome=new(Rule,"APPLIED",1,0,relative,null,true,false);
            }
            else outcome=new(Rule,inspect.Status,0,
                inspect.Status=="COMPLETE"?1:0,relative,
                inspect.Reason,true,false);
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DF_ICON_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if(optimized is not null && relative is not null)
        {
            string dest=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dest,output))
                throw new InvalidDataException("DF_ICON_STAGE_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest,optimized,new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(outcome,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return outcome;
    }
    private static bool Inside(string path,string root)
    {
        path=Path.GetFullPath(path);
        root=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return path.StartsWith(root+Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }
}
