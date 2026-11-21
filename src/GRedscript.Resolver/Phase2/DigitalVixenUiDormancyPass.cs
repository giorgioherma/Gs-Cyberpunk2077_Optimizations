using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Port the game-tested historical DigitalVixenCore Pass 9 UI dormancy from
/// current post-generic source. BodyMap, ItemEditor and StatusWindow are
/// independently eligible and preserve original RedWindows callbacks.
/// StatusWindow retains pinned updates with the overlay closed.
/// </summary>
internal static class DigitalVixenUiDormancyPass
{
    internal const string Rule="DIGITAL_VIXEN_HISTORICAL_UI_DORMANCY_V1";
    internal sealed record Result(string RuleId,string Status,int AppliedEdits,
        int AlreadyComplete,string[] SourcePaths,string? Reason,
        bool SourceUnchanged,bool GameplayValidated);
    private sealed record Method(int Open,int Close);
    private sealed record Edit(int Start,int End,string Text);
    private sealed record Decision(string Status,string? Reason,string? Updated,
        int Applied,int Complete);

    private const string EditorPrelude = """
        let shouldUpdate: Bool = !this.initialized;
        let delay: Float = 2.0;
        let mgr: ref<RWManager> = RWManager.Get(this.gi);
        if this.initialized && IsDefined(mgr) && mgr.IsOverlay() {
          delay = 0.5;
          let win: ref<RWWindow> = mgr.FindWindow(n"DigitalVixenCore.ItemEditor");
          shouldUpdate = IsDefined(win) && win.IsRendered();
          if shouldUpdate { delay = 0.25; }
        }
        """;

    // Unlike the historical status loop, this preserves status updates
    // for a PINNED window while the overlay is closed. A normal hidden
    // status window still polls every two seconds.
    private const string StatusPrelude = """
        let shouldUpdate: Bool = !this.initialized;
        let delay: Float = 2.0;
        let mgr: ref<RWManager> = RWManager.Get(this.gi);
        if this.initialized && IsDefined(mgr) {
          if mgr.IsOverlay() { delay = 0.5; }
          let win: ref<RWWindow> = mgr.FindWindow(n"DigitalVixenCore.Status");
          shouldUpdate = IsDefined(win) && win.IsRendered();
          if shouldUpdate { delay = 0.25; }
        }
        """;

    private const string BodyGate = """
        if !overlay && !cfgPinned {
          win.SetOverlayOnly(true);
          return false;
        }
        """;

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start,string opening,string closing)
    {
        if(start<0 || start>=t.Count || t[start].Value!=opening)return -1;
        int depth=0;
        for(int i=start;i<t.Count;i++){
            if(t[i].Value==opening)depth++;
            else if(t[i].Value==closing && --depth==0)return i;
        }
        return -1;
    }

    private static Method? FindMethod(string source,string className,string name)
    {
        var t=SourceAdmission.ReadTokens(source);
        var classes=new List<(int Start,int End)>();
        for(int i=0;i+2<t.Count;i++){
            if(t[i].Value!="class" || t[i+1].Value!=className)continue;
            int open=i+2;
            while(open<t.Count && t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count || t[open].Value!="{")return null;
            int end=Close(t,open,"{","}");
            if(end<0)return null;
            classes.Add((t[open].End,t[end].Start));
        }
        if(classes.Count!=1)return null;
        Method? result=null;
        for(int i=0;i+2<t.Count;i++){
            if(t[i].Value!="func" || t[i+1].Value!=name ||
               t[i+2].Value!="(" ||
               t[i].Start<classes[0].Start ||
               t[i].Start>=classes[0].End)continue;
            int args=Close(t,i+2,"(",")");
            if(args<0)return null;
            int open=args+1;
            while(open<t.Count && t[open].Value is not ("{" or ";"))open++;
            if(open>=t.Count || t[open].Value!="{")return null;
            int end=Close(t,open,"{","}");
            if(end<0 || t[end].Start>classes[0].End || result is not null)
                return null;
            result=new(t[open].End,t[end].Start);
            i=end;
        }
        return result;
    }

    private static string Shape(string s)=>string.Join("\u001f",
        SourceAdmission.ReadTokens(s).Select(x=>x.Value));

    private static bool ContainsSequence(string body,string statement)
    {
        var ts=SourceAdmission.ReadTokens(body);
        var pattern=SourceAdmission.ReadTokens(statement);
        if(pattern.Count==0)return false;
        for(int i=0;i+pattern.Count<=ts.Count;i++){
            bool all=true;
            for(int j=0;j<pattern.Count;j++)
                if(ts[i+j].Value!=pattern[j].Value){all=false;break;}
            if(all)return true;
        }
        return false;
    }

    private static string Apply(string text,IEnumerable<Edit> edits)
    {
        foreach(var e in edits.OrderByDescending(x=>x.Start))
            text=text[..e.Start]+e.Text+text[e.End..];
        return text;
    }

    private static Decision Loop(string text,bool status)
    {
        string className=status?"DVCStatusWindowLoop":"DVCItemEditorLoop";
        string update=status?"DVCStatusWindow":"DVCItemEditor";
        var call=FindMethod(text,className,"Call");
        if(call is null || !Regex.IsMatch(text,
            @"\bclass\s+"+className+@"\s+extends\s+DelayCallback\b"))
            return new("REFUSED","DVC_UI_LOOP_OWNER_UNPROVEN",null,0,0);
        var cl=Regex.Match(text,@"\bclass\s+"+className+
            @"\s+extends\s+DelayCallback\s*\{");
        if(!cl.Success)return new("REFUSED","DVC_UI_LOOP_CLASS_UNPROVEN",
            null,0,0);
        string body=text[call.Open..call.Close];
        string newline=text.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        string prelude=status?StatusPrelude:EditorPrelude;
        var updates=Regex.Matches(body,@"\b"+update+
            @"\s*\.\s*Update\s*\(\s*this\s*\.\s*gi\s*\)\s*;");
        var conditional=Regex.Matches(body,
            @"\bif\s+shouldUpdate\s*\{\s*"+update+
            @"\s*\.\s*Update\s*\(\s*this\s*\.\s*gi\s*\)\s*;\s*\}");
        var schedule=Regex.Matches(body,
            @"\bGameInstance\s*\.\s*GetDelaySystem\s*\(\s*this\s*\.\s*gi\s*\)"+
            @"\s*\.\s*DelayCallback\s*\(\s*cb\s*,\s*(?<delay>0\.5|delay)\s*,\s*false\s*\)\s*;");
        var constructor=Regex.Matches(body,
            @"\blet\s+cb\s*:\s*ref\s*<\s*"+className+@"\s*>\s*=\s*new\s+"+
            className+@"\s*\(\s*\)\s*;");
        var carry=Regex.Matches(body,
            @"\bcb\s*\.\s*gi\s*=\s*this\s*\.\s*gi\s*;");
        var initialized=Regex.Matches(body,
            @"\bcb\s*\.\s*initialized\s*=\s*true\s*;");
        if(updates.Count!=1 || schedule.Count!=1 ||
            constructor.Count!=1 || carry.Count!=1 || initialized.Count>1 ||
            updates[0].Index>=constructor[0].Index ||
            constructor[0].Index>=carry[0].Index ||
            carry[0].Index>=schedule[0].Index)
            return new("REFUSED","DVC_UI_LOOP_CALLBACK_LIFECYCLE_CHANGED",
                null,0,0);
        string fieldPattern=@"\b(?:public|private|protected)\s+let\s+initialized\s*:\s*(?<type>\w+)\s*;";
        var field=Regex.Matches(text,fieldPattern);
        if(field.Count>1 ||
            (field.Count==1 && field[0].Groups["type"].Value!="Bool"))
            return new("REFUSED","DVC_UI_LOOP_INIT_FIELD_CONFLICT",null,0,0);
        string expectedTail="cb.initialized = true;";
        bool hasPrelude=ContainsSequence(body,prelude);
        bool preludeReference=Regex.IsMatch(body,
            @"\blet\s+shouldUpdate\s*:\s*Bool") ||
            Regex.IsMatch(body,@"\blet\s+delay\s*:\s*Float") ||
            Regex.IsMatch(body,@"\bshouldUpdate\s*=\s*IsDefined\s*\(\s*win");
        if(preludeReference && !hasPrelude)
            return new("REFUSED","DVC_UI_PARTIAL_CADENCE_CONTROL_FLOW_UNPROVEN",
                null,0,0);
        bool isConditional=conditional.Count==1;
        if(!isConditional && hasPrelude && Regex.IsMatch(body,
            @"\bif\s+shouldUpdate\s*\{"))
            return new("REFUSED","DVC_UI_UPDATE_BRANCH_CHANGED",null,0,0);
        if(schedule[0].Groups["delay"].Value=="delay" && !hasPrelude)
            return new("REFUSED","DVC_UI_DELAY_VARIABLE_UNPROVEN",null,0,0);
        if(initialized.Count==1 && 
           initialized[0].Index<carry[0].Index)
            return new("REFUSED","DVC_UI_INITIALIZED_BEFORE_GI",null,0,0);
        if(field.Count==0 &&
            SourceAdmission.ReadTokens(text).Any(t=>t.Value=="initialized"))
        {
            // Existing callbacks may already carry initialized uses.
            // An otherwise eligible known partial implementation is still
            // repairable when the only uses are inside the owned Call.
            string outside=text[..call.Open]+text[call.Close..];
            if(Regex.IsMatch(outside,@"\binitialized\b"))
                return new("REFUSED","DVC_UI_UNOWNED_INIT_STATE",null,0,0);
        }
        var edits=new List<Edit>();
        if(field.Count==0){
            var gi=Regex.Match(text,@"\bpublic\s+let\s+gi\s*:\s*GameInstance\s*;");
            if(!gi.Success || gi.Index<cl.Index || gi.Index>call.Open)
                return new("REFUSED","DVC_UI_INIT_FIELD_ANCHOR_UNPROVEN",
                    null,0,0);
            edits.Add(new(gi.Index+gi.Length,gi.Index+gi.Length,
                newline+"  public let initialized: Bool;"));
        }
        if(!hasPrelude){
            string prefix=newline+"    "+
                prelude.Replace("\n",newline+"    ").TrimEnd()+newline+"    ";
            edits.Add(new(call.Open+updates[0].Index,
                call.Open+updates[0].Index,prefix));
        }
        if(!isConditional){
            string old=updates[0].Value;
            edits.Add(new(call.Open+updates[0].Index,
                call.Open+updates[0].Index+updates[0].Length,
                "if shouldUpdate {"+newline+"      "+old+newline+"    }"));
        }
        if(initialized.Count==0)
            edits.Add(new(call.Open+schedule[0].Index,
                call.Open+schedule[0].Index,
                "cb.initialized = true;"+newline+"    "));
        if(schedule[0].Groups["delay"].Value=="0.5"){
            var p=schedule[0].Groups["delay"];
            edits.Add(new(call.Open+p.Index,call.Open+p.Index+p.Length,
                "delay"));
        }
        if(edits.Count==0)return new("COMPLETE",null,null,0,1);
        // Multiple insertions at the same source index are composed
        // explicitly, so a fresh prelude never nests inside its new guard.
        if(!hasPrelude && !isConditional){
            edits.RemoveAll(x=>x.Start==call.Open+updates[0].Index &&
                x.End==x.Start);
            string old=updates[0].Value;
            string prefix=newline+"    "+
                prelude.Replace("\n",newline+"    ").TrimEnd()+newline+"    ";
            edits.RemoveAll(x=>x.Start==call.Open+updates[0].Index &&
                x.End==call.Open+updates[0].Index+updates[0].Length);
            edits.Add(new(call.Open+updates[0].Index,
                call.Open+updates[0].Index+updates[0].Length,
                prefix+"if shouldUpdate {"+newline+"      "+old+newline+"    }"));
        }
        string updated=Apply(text,edits);
        var next=LoopComplete(updated,status);
        if(!next)
            throw new InvalidDataException("DVC_UI_LOOP_POSTCONDITION:"+className);
        return new("APPLIED",null,updated,1,0);
    }

    private static bool LoopComplete(string source,bool status)
    {
        string cls=status?"DVCStatusWindowLoop":"DVCItemEditorLoop";
        string callee=status?"DVCStatusWindow":"DVCItemEditor";
        var m=FindMethod(source,cls,"Call");
        if(m is null)return false;
        string body=source[m.Open..m.Close];
        return Regex.IsMatch(source,@"\bpublic\s+let\s+initialized\s*:\s*Bool\s*;")
            && ContainsSequence(body,status?StatusPrelude:EditorPrelude)
            && Regex.IsMatch(body,@"\bif\s+shouldUpdate\s*\{\s*"+
                callee+@"\s*\.\s*Update\s*\(\s*this\s*\.\s*gi\s*\)\s*;\s*\}")
            && Regex.IsMatch(body,@"\bcb\s*\.\s*initialized\s*=\s*true\s*;")
            && Regex.IsMatch(body,@"\bDelayCallback\s*\(\s*cb\s*,\s*delay\s*,\s*false\s*\)");
    }

    private static Decision UpdateVisibility(string source,bool status)
    {
        string cls=status?"DVCStatusWindow":"DVCItemEditor";
        var m=FindMethod(source,cls,"Update");
        if(m is null)return new("REFUSED","DVC_UI_UPDATE_OWNER_UNPROVEN",null,0,0);
        string body=source[m.Open..m.Close];
        if(!Regex.IsMatch(body,
            @"\bRedWindowsAPI\s*\.\s*Window\s*\(\s*gi\s*,\s*n\s*""DigitalVixenCore\."+
            (status?"Status":"ItemEditor")+@"""") ||
            !Regex.IsMatch(body,@"\blet\s+win\s*:\s*ref\s*<\s*RWWindow\s*>") ||
            !body.Contains("win.SetOverlayOnly(",StringComparison.Ordinal))
            return new("REFUSED","DVC_UI_WINDOW_LIFECYCLE_UNPROVEN",null,0,0);
        var old=Regex.Matches(body,
            @"\bif\s*!\s*win\s*\.\s*IsVisible\s*\(\s*\)\s*\{\s*return\s*;\s*\}");
        var newer=Regex.Matches(body,
            @"\bif\s*!\s*win\s*\.\s*IsRendered\s*\(\s*\)\s*\{\s*return\s*;\s*\}");
        if(newer.Count==1 && old.Count==0)
            return new("COMPLETE",null,null,0,1);
        if(old.Count!=1 || newer.Count!=0)
            return new("REFUSED","DVC_UI_VISIBILITY_BRANCH_AMBIGUOUS",null,0,0);
        string edited=source[..(m.Open+old[0].Index)] +
            old[0].Value.Replace("IsVisible","IsRendered",StringComparison.Ordinal) +
            source[(m.Open+old[0].Index+old[0].Length)..];
        return new("APPLIED",null,edited,1,0);
    }

    private static Decision BodyMap(string source)
    {
        var m=FindMethod(source,"DVBodyMapWindow","Update");
        if(m is null)return new("REFUSED","DVC_BODYMAP_UPDATE_MISSING",null,0,0);
        string body=source[m.Open..m.Close];
        if(!Regex.IsMatch(body,
            @"\bRedWindowsAPI\s*\.\s*Window\s*\(\s*gi\s*,\s*n\s*""DigitalVixenCore\.HitIndicator""") ||
            !body.Contains("HideRuleActive(",StringComparison.Ordinal) ||
            !body.Contains("hitIndicatorPinned",StringComparison.Ordinal) ||
            !body.Contains("win.SetOverlayOnly(",StringComparison.Ordinal))
            return new("REFUSED","DVC_BODYMAP_HIT_LIFECYCLE_UNPROVEN",null,0,0);
        int applied=0,complete=0;
        var refusals=new List<string>();
        string nl=source.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";
        var overlay=Regex.Match(body,
            @"\blet\s+overlay\s*:\s*Bool\s*=\s*RedWindowsAPI\s*\.\s*IsOverlay\s*\(\s*gi\s*\)\s*;");
        var pin=Regex.Matches(body,
            @"\blet\s+cfgPinned\s*:\s*Bool\s*=\s*IsDefined\s*\(\s*cfg\s*\)\s*\?\s*cfg\s*\.\s*hitIndicatorPinned\s*:\s*false\s*;");
        var gate=Regex.Match(body,
            @"\bif\s*!\s*overlay\s*&&\s*!\s*cfgPinned\s*\{\s*win\s*\.\s*SetOverlayOnly\s*\(\s*true\s*\)\s*;\s*return\s+false\s*;\s*\}");
        var style=Regex.Match(body,
            @"\blet\s+clean\s*:\s*Bool\s*=");
        var checks=Regex.Match(body,
            @"\bwin\s*\.\s*Checkbox\s*\(\s*n\s*""hiPin""");
        if(!overlay.Success || pin.Count!=1 || !style.Success ||
            !checks.Success || overlay.Index>=checks.Index)
            return new("REFUSED","DVC_BODYMAP_PIN_FLOW_UNPROVEN",null,0,0);
        if(!gate.Success){
            if(pin[0].Index>=checks.Index ||
                gate.Success || body.Contains("if !overlay && !cfgPinned",
                    StringComparison.Ordinal))
                refusals.Add("DVC_BODYMAP_GATE_CUSTOM_FLOW_UNPROVEN");
            else{
                // Relocate the existing pin read ahead of style changes,
                // then insert the tested early exit before widget work.
                int at=overlay.Index+overlay.Length;
                string prefix=nl+"    "+pin[0].Value+nl+"    "+
                    BodyGate.Replace("\n",nl+"    ").TrimEnd();
                if(pin[0].Index>at){
                    body=body[..pin[0].Index]+body[(pin[0].Index+pin[0].Length)..];
                    body=body[..at]+prefix+body[at..];
                }else{
                    body=body[..(pin[0].Index+pin[0].Length)]+
                        nl+"    "+BodyGate.Replace("\n",nl+"    ").TrimEnd()+
                        body[(pin[0].Index+pin[0].Length)..];
                }
                applied++;
            }
        }else{
            if(pin[0].Index>gate.Index || gate.Index>style.Index)
                refusals.Add("DVC_BODYMAP_EXISTING_GATE_ORDER_UNPROVEN");
            else complete++;
        }
        var supOld=Regex.Matches(body,
            @"\bif\s+suppressed\s*\{\s*return\s+win\s*\.\s*IsVisible\s*\(\s*\)\s*;\s*\}");
        var supNew=Regex.Matches(body,
            @"\bif\s+suppressed\s*\{\s*return\s+win\s*\.\s*IsRendered\s*\(\s*\)\s*;\s*\}");
        if(supOld.Count==1 && supNew.Count==0){
            var old=supOld[0];body=body[..old.Index]+
                old.Value.Replace("IsVisible","IsRendered",StringComparison.Ordinal)+
                body[(old.Index+old.Length)..];
            applied++;
        }else if(supNew.Count==1 && supOld.Count==0)complete++;
        else refusals.Add("DVC_BODYMAP_SUPPRESSED_RETURN_UNPROVEN");

        var visOld=Regex.Matches(body,
            @"\bif\s*!\s*win\s*\.\s*IsVisible\s*\(\s*\)\s*\{\s*return\s+false\s*;\s*\}");
        var visNew=Regex.Matches(body,
            @"\bif\s*!\s*win\s*\.\s*IsRendered\s*\(\s*\)\s*\{\s*return\s+false\s*;\s*\}");
        if(visOld.Count==1 && visNew.Count==0){
            var old=visOld[0];body=body[..old.Index]+
                old.Value.Replace("IsVisible","IsRendered",StringComparison.Ordinal)+
                body[(old.Index+old.Length)..];
            applied++;
        }else if(visNew.Count==1 && visOld.Count==0)complete++;
        else refusals.Add("DVC_BODYMAP_RENDER_VISIBILITY_UNPROVEN");
        if(applied==0)
            return new(refusals.Count>0?"REFUSED":"COMPLETE",
                refusals.Count>0?string.Join(";",refusals):null,null,0,complete);
        string rewritten=source[..m.Open]+body+source[m.Close..];
        return new("APPLIED",refusals.Count>0?string.Join(";",refusals):null,
            rewritten,applied,complete);
    }

    private static Decision TransformFile(string source,string kind)
    {
        if(kind=="BodyMap")return BodyMap(source);
        bool status=kind=="Status";
        var loop=Loop(source,status);
        string next=loop.Updated??source;
        var visibility=UpdateVisibility(next,status);
        var refusals=new List<string>();
        if(loop.Reason is not null)refusals.Add("loop:"+loop.Reason);
        if(visibility.Reason is not null)refusals.Add("visibility:"+visibility.Reason);
        int applied=loop.Applied+visibility.Applied;
        int complete=loop.Complete+visibility.Complete;
        return new(applied>0?"APPLIED":
            refusals.Count>0?"REFUSED":"COMPLETE",
            refusals.Count>0?string.Join(";",refusals):null,
            applied>0?(visibility.Updated??next):null,applied,complete);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts) ||
           Directory.Exists(output) || File.Exists(output) ||
           Inside(output,game))
            throw new InvalidDataException("DVC_UI_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot,game) ||
            Inside(output,basePreviewRoot) ||
            Inside(basePreviewRoot,output)))
            throw new InvalidDataException("DVC_UI_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string originalFingerprint=admission.SourceStackSha256 ??
            throw new InvalidDataException("DVC_UI_HASH_MISSING");
        string owner=Path.Combine(scripts,"DigitalVixenCore");
        var staged=new Dictionary<string,string>(
            StringComparer.OrdinalIgnoreCase);
        var refusals=new List<string>();
        int applied=0,complete=0;
        string status="NOT_APPLICABLE";
        if(Directory.Exists(owner))
        {
            string redWindows=Path.Combine(scripts,"RedWindows");
            bool renderAvailable=Directory.Exists(redWindows) &&
                Directory.EnumerateFiles(redWindows,"*.reds",
                    SearchOption.AllDirectories).Any(f=>Regex.IsMatch(
                        File.ReadAllText(f,Encoding.UTF8),
                        @"\bfunc\s+IsRendered\s*\(\s*\)\s*->\s*Bool\b"));
            if(!renderAvailable && basePreviewRoot is not null)
            {
                string priorRw=Path.Combine(basePreviewRoot,
                    "r6","scripts","RedWindows");
                renderAvailable=Directory.Exists(priorRw) &&
                    Directory.EnumerateFiles(priorRw,"*.reds",
                        SearchOption.AllDirectories).Any(f=>Regex.IsMatch(
                            File.ReadAllText(f,Encoding.UTF8),
                            @"\bfunc\s+IsRendered\s*\(\s*\)\s*->\s*Bool\b"));
            }
            if(!renderAvailable)
            {
                status="REFUSED";
                refusals.Add("DVC_UI_REQUIRES_REDWINDOWS_ISRENDERED");
            }
            else
            {
                var definitions=new[]
                {
                    (Kind:"BodyMap",Class:"DVBodyMapWindow"),
                    (Kind:"ItemEditor",Class:"DVCItemEditorLoop"),
                    (Kind:"Status",Class:"DVCStatusWindowLoop")
                };
                var files=Directory.EnumerateFiles(owner,"*.reds",
                    SearchOption.AllDirectories).ToArray();
                foreach(var def in definitions)
                {
                    var matches=files.Where(f=>Regex.IsMatch(
                        File.ReadAllText(f,Encoding.UTF8),
                        @"\bclass\s+"+def.Class+@"\b")).ToArray();
                    if(matches.Length!=1)
                    {
                        refusals.Add(def.Kind+":DVC_UI_OWNER_MISSING_OR_AMBIGUOUS");
                        continue;
                    }
                    string file=matches[0];
                    if((File.GetAttributes(file)&
                        FileAttributes.ReparsePoint)!=0)
                        throw new InvalidDataException("DVC_UI_LINKED_SOURCE");
                    string rel="r6/scripts/"+
                        Path.GetRelativePath(scripts,file).Replace('\\','/');
                    string effective=File.ReadAllText(file,Encoding.UTF8);
                    if(basePreviewRoot is not null)
                    {
                        string prior=Path.GetFullPath(Path.Combine(
                            basePreviewRoot,
                            rel.Replace('/',Path.DirectorySeparatorChar)));
                        if(!Inside(prior,basePreviewRoot))
                            throw new InvalidDataException("DVC_UI_OVERLAY_ESCAPE");
                        if(File.Exists(prior))
                            effective=File.ReadAllText(prior,Encoding.UTF8);
                    }
                    var decision=TransformFile(effective,def.Kind);
                    applied+=decision.Applied;
                    complete+=decision.Complete;
                    if(decision.Reason is not null)
                        refusals.Add(def.Kind+":"+decision.Reason);
                    if(decision.Updated is not null)
                    {
                        if(!rel.StartsWith("r6/scripts/DigitalVixenCore/",
                                StringComparison.Ordinal) ||
                            staged.ContainsKey(rel))
                            throw new InvalidDataException(
                                "DVC_UI_DUPLICATE_OWNER_FILE");
                        staged.Add(rel,decision.Updated);
                    }
                }
                status=applied>0?"APPLIED":
                    refusals.Count>0?"REFUSED":"COMPLETE";
            }
        }
        if(!string.Equals(originalFingerprint,
            admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DVC_UI_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach(var file in staged)
        {
            string dest=Path.GetFullPath(Path.Combine(output,
                file.Key.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dest,output) || !dest.EndsWith(".reds",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DVC_UI_STAGE_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest,file.Value,new UTF8Encoding(false));
        }
        var report=new Result(Rule,status,applied,complete,
            staged.Keys.Order(StringComparer.Ordinal).ToArray(),
            refusals.Count>0?string.Join("; ",refusals):null,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string child,string root)=>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
