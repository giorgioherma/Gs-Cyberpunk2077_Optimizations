using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical RunningMan Pass 7 + Pass 9 in ONE source-aware operation.
/// The per-sprint perk cache, OnUpdate evaluator gate and StandEvents
/// first-eligible-frame invalidation are recovered directly from the live
/// post-generic owner. Original sprint, stamina and state transitions stay.
/// This class is intentionally NOT registered in the shared stage plan:
/// the integration owner assigns its final stage/order after branch merge.
/// </summary>
public static class RunningManDoubleSprintPass
{
    public const string Rule = "RUNNINGMAN_DOUBLE_SPRINT_STAND_DORMANCY_V1";
    public sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(string Owner, string Name, string Attribute,
        int Header, int BodyStart, int BodyEnd);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Reason, List<Edit> Edits);

    private const string SprintField = "m_gredRunningManDoubleRunPerk";
    private const string StandField = "m_gredRunningManResumeChecked";
    private static readonly RegexOptions Opt = RegexOptions.CultureInvariant |
        RegexOptions.Singleline;

    private static string NL(string s) =>
        s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
    private static string Shape(string source) =>
        string.Join("|", SourceAdmission.ReadTokens(source).Select(t => t.Value));

    private static int Close(IReadOnlyList<SourceAdmission.Token> tokens, int start,
        string op, string cl)
    {
        if (start < 0 || start >= tokens.Count || tokens[start].Value != op)
            return -1;
        int depth = 0;
        for (int j = start; j < tokens.Count; j++)
        {
            if (tokens[j].Value == op) depth++;
            else if (tokens[j].Value == cl && --depth == 0) return j;
        }
        return -1;
    }

    private static IReadOnlyList<Method> Methods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var found = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 2].Value != "(") continue;
            int closeArgs = Close(t, i + 2, "(", ")");
            if (closeArgs < 0) continue;
            int open = closeArgs + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Close(t, open, "{", "}");
            if (end < 0) continue;
            int begin = Math.Max(0, t[i].Start - 420);
            string prefix = source[begin..t[i].Start];
            // Comments do not modify the owner annotation; the source lexer
            // supplies the actual function/braces and offsets.
            string stripped = Regex.Replace(prefix, @"/\*[\s\S]*?\*/|//[^\r\n]*", "");
            var attr = Regex.Match(stripped,
                @"@\s*(?<kind>wrapMethod|replaceMethod)\s*\(\s*" +
                @"(?<owner>SprintEvents|StandEvents)\s*\)\s*" +
                @"(?:(?:public|private|protected|final|static|cb|const)\s+)*$",
                RegexOptions.CultureInvariant);
            if (attr.Success)
            {
                string owner = attr.Groups["owner"].Value;
                string name = t[i + 1].Value;
                // This is only used for inserting the standalone Stand OnEnter.
                // Work backwards to the last annotation token to get true
                // offsets even when comments preceded the method declaration.
                int header = t[i].Start;
                for (int j = i - 1; j >= 0 && j > i - 22; j--)
                {
                    if (t[j].Value == "@" &&
                        j + 4 < t.Count &&
                        t[j + 2].Value == "(" &&
                        t[j + 3].Value == owner &&
                        t[j + 4].Value == ")")
                    {
                        header = t[j].Start;
                        break;
                    }
                }
                found.Add(new(owner, name, attr.Groups["kind"].Value,
                    header, t[open].End, t[end].Start));
            }
            i = end;
        }
        return found;
    }

    private static Method? Exactly(IReadOnlyList<Method> methods,
        string owner, string name, string kind)
    {
        var matching = methods.Where(m => m.Owner == owner &&
            m.Name == name && m.Attribute == kind).ToArray();
        return matching.Length == 1 ? matching[0] : null;
    }

    private static string Body(string s, Method m) =>
        s[m.BodyStart..m.BodyEnd];

    private static readonly Regex PerkLocal = new(
        @"\blet\s+hasDoubleRunPerk\s*:\s*Bool\s*=\s*" +
        @"(?<expr>PlayerDevelopmentSystem\s*\.\s*GetInstance\s*" +
        @"\(\s*scriptInterface\s*\.\s*executionOwner\s*\)\s*" +
        @"\.\s*IsNewPerkBought\s*\([\s\S]*?\)\s*>\s*\d+)\s*;",
        Opt);
    private static readonly Regex PerkAssignment = new(
        @"\bthis\s*\.\s*" + SprintField + @"\s*=\s*" +
        @"(?<expr>PlayerDevelopmentSystem\s*\.\s*GetInstance\s*" +
        @"\(\s*scriptInterface\s*\.\s*executionOwner\s*\)\s*" +
        @"\.\s*IsNewPerkBought\s*\([\s\S]*?\)\s*>\s*\d+)\s*;",
        Opt);
    private static readonly Regex SprintEvaluate = new(
        @"\bthis\s*\.\s*EvaluateTwoStepSprint\s*" +
        @"\(\s*stateContext\s*,\s*scriptInterface\s*\)\s*;", Opt);
    private static readonly Regex Wrapped = new(
        @"\bwrappedMethod\s*\(\s*stateContext\s*,\s*scriptInterface\s*\)\s*;", Opt);
    private static readonly Regex UpdateGuard = new(
        @"\bif\s+this\s*\.\s*" + SprintField + @"\s*\|\|\s*" +
        @"this\s*\.\s*m_isInSecondSprint\s*\{\s*" +
        @"this\s*\.\s*EvaluateTwoStepSprint\s*" +
        @"\(\s*stateContext\s*,\s*scriptInterface\s*\)\s*;\s*\}", Opt);

    private static bool HasField(string source, string owner, string name) =>
        Regex.IsMatch(source,
            @"@\s*addField\s*\(\s*" + owner + @"\s*\)\s*" +
            @"(?:(?:public|private|protected)\s+)?let\s+" +
            Regex.Escape(name) + @"\s*:\s*Bool\s*;",
            RegexOptions.CultureInvariant);

    private static bool ContainsReservedCollision(string source,
        string name, string owner) =>
        Regex.IsMatch(source, @"\b" + name + @"\b",
            RegexOptions.CultureInvariant) &&
        !HasField(source, owner, name) &&
        !Regex.IsMatch(source, @"\bthis\s*\.\s*" + name + @"\b",
            RegexOptions.CultureInvariant);

    private static Decision Sprint(string src, IReadOnlyList<Method> methods)
    {
        var result = new List<Edit>();
        var update = Exactly(methods, "SprintEvents", "OnUpdate", "wrapMethod");
        var enter = Exactly(methods, "SprintEvents", "OnEnter", "wrapMethod");
        var evaluate = Exactly(methods, "SprintEvents",
            "EvaluateTwoStepSprint", "replaceMethod");
        if (update is null || enter is null || evaluate is null)
            return new("REFUSED", "RM_SPRINT_OWNER_METHODS_NOT_UNIQUE", result);
        string ub = Body(src, update), eb = Body(src, enter), vb = Body(src, evaluate);
        if (Wrapped.Matches(ub).Count != 1 ||
            Wrapped.Matches(eb).Count != 1 ||
            SprintEvaluate.Matches(ub).Count != 1 ||
            !Regex.IsMatch(vb,
                @"this\s*\.\s*ShouldEnterSecondSprint\s*\(") ||
            !Regex.IsMatch(vb,
                @"this\s*\.\s*RemoveMaxSpeedModifier\s*\(") ||
            !Regex.IsMatch(vb, @"RunningMan_ResumeSecondSprint") ||
            !Regex.IsMatch(eb, @"RunningMan_ResumeSecondSprint"))
            return new("REFUSED", "RM_SPRINT_TRANSITION_OR_CALLBACK_UNPROVEN", result);

        bool hasField = HasField(src, "SprintEvents", SprintField);
        if (!hasField && ContainsReservedCollision(src, SprintField, "SprintEvents"))
            return new("REFUSED", "RM_SPRINT_FIELD_COLLISION", result);

        var initializers = PerkAssignment.Matches(eb);
        if (initializers.Count > 1)
            return new("REFUSED", "RM_SPRINT_DUPLICATE_CACHE_INIT", result);
        var evalLocals = PerkLocal.Matches(vb);
        var enterLocals = PerkLocal.Matches(eb);
        if (evalLocals.Count > 1 || enterLocals.Count > 1)
            return new("REFUSED", "RM_SPRINT_PERK_QUERY_AMBIGUOUS", result);
        string? expr = evalLocals.Count == 1 ?
            evalLocals[0].Groups["expr"].Value :
            enterLocals.Count == 1 ?
                enterLocals[0].Groups["expr"].Value :
                initializers.Count == 1 ?
                    initializers[0].Groups["expr"].Value : null;
        if (expr is null ||
            !expr.Contains("Body_Left_Milestone_1", StringComparison.Ordinal) ||
            !expr.Contains("scriptInterface.executionOwner", StringComparison.Ordinal))
            return new("REFUSED", "RM_SPRINT_PERK_SOURCE_QUERY_MISSING", result);
        if (evalLocals.Count == 1 && enterLocals.Count == 1 &&
            Shape(evalLocals[0].Groups["expr"].Value) !=
            Shape(enterLocals[0].Groups["expr"].Value))
            return new("REFUSED", "RM_SPRINT_PERK_QUERIES_NOT_EQUIVALENT", result);
        if (initializers.Count == 1 &&
            Shape(initializers[0].Groups["expr"].Value) != Shape(expr))
            return new("REFUSED", "RM_SPRINT_PERK_INIT_DIFFERENT", result);

        bool alreadyGuarded = UpdateGuard.Matches(ub).Count == 1;
        if (!alreadyGuarded &&
            Regex.IsMatch(ub, @"\bif\s+this\s*\.\s*" + SprintField))
            return new("REFUSED", "RM_SPRINT_PARTIAL_UPDATE_GUARD_UNPROVEN", result);
        if (!alreadyGuarded)
        {
            var call = SprintEvaluate.Match(ub);
            result.Add(new(update.BodyStart + call.Index,
                update.BodyStart + call.Index + call.Length,
                "if this." + SprintField + " || this.m_isInSecondSprint {" +
                NL(src) + "    " + call.Value + NL(src) + "  }"));
        }

        if (evalLocals.Count == 1)
        {
            if (Regex.Matches(vb, @"\bhasDoubleRunPerk\b").Count != 2 ||
                !Regex.IsMatch(vb, @"\bif\s+hasDoubleRunPerk\s*\{"))
                return new("REFUSED", "RM_SPRINT_EVALUATOR_PERK_FLOW_CHANGED",
                    new List<Edit>());
            var local = evalLocals[0];
            result.Add(new(evaluate.BodyStart + local.Index,
                evaluate.BodyStart + local.Index + local.Length, ""));
            var condition = Regex.Match(vb, @"\bif\s+(?<ref>hasDoubleRunPerk)\s*\{");
            var g = condition.Groups["ref"];
            result.Add(new(evaluate.BodyStart + g.Index,
                evaluate.BodyStart + g.Index + g.Length,
                "this." + SprintField));
        }
        else if (!Regex.IsMatch(vb,
                     @"\bif\s+this\s*\.\s*" + SprintField + @"\s*\{"))
            return new("REFUSED", "RM_SPRINT_EVALUATOR_CACHE_NOT_PROVEN",
                new List<Edit>());

        if (enterLocals.Count == 1)
        {
            if (Regex.Matches(eb, @"\bhasDoubleRunPerk\b").Count != 2 ||
                !Regex.IsMatch(eb,
                    @"\bif\s+hasDoubleRunPerk\s*&&\s*isStaminaPositive"))
                return new("REFUSED", "RM_SPRINT_RESUME_PERK_FLOW_CHANGED",
                    new List<Edit>());
            var local = enterLocals[0];
            result.Add(new(enter.BodyStart + local.Index,
                enter.BodyStart + local.Index + local.Length, ""));
            var condition = Regex.Match(eb,
                @"\bif\s+(?<ref>hasDoubleRunPerk)\s*&&\s*isStaminaPositive");
            var g = condition.Groups["ref"];
            result.Add(new(enter.BodyStart + g.Index,
                enter.BodyStart + g.Index + g.Length,
                "this." + SprintField));
        }
        else if (!Regex.IsMatch(eb,
            @"\bif\s+this\s*\.\s*" + SprintField +
            @"\s*&&\s*isStaminaPositive"))
            return new("REFUSED", "RM_SPRINT_RESUME_CACHE_NOT_PROVEN",
                new List<Edit>());

        if (initializers.Count == 0)
        {
            var w = Wrapped.Match(eb);
            result.Add(new(enter.BodyStart + w.Index + w.Length,
                enter.BodyStart + w.Index + w.Length,
                NL(src) + "  this." + SprintField + " = " + expr + ";"));
        }
        if (!hasField)
            result.Add(new(0, 0, "@addField(SprintEvents)" + NL(src) +
                "private let " + SprintField + ": Bool;" + NL(src) + NL(src)));
        return new(result.Count > 0 ? "APPLIED" : "COMPLETE", null, result);
    }

    private static readonly Regex StandGuard = new(
        @"\bif\s+(?<cached>!\s*this\s*\.\s*" + StandField +
        @"\s*&&\s*)?this\s*\.\s*GetInStateTime\s*\(\s*\)\s*>=\s*0\.10\s*\{",
        Opt);
    private static readonly Regex StandReset = new(
        @"\bthis\s*\.\s*" + StandField + @"\s*=\s*false\s*;", Opt);
    private static readonly Regex StandChecked = new(
        @"\bthis\s*\.\s*" + StandField + @"\s*=\s*true\s*;", Opt);

    private static Decision Stand(string src, IReadOnlyList<Method> methods)
    {
        var result = new List<Edit>();
        var update = Exactly(methods,"StandEvents","OnUpdate","wrapMethod");
        var enter = Exactly(methods,"StandEvents","OnEnter","wrapMethod");
        if (update is null)
            return new("REFUSED", "RM_STAND_UPDATE_METHOD_NOT_UNIQUE", result);
        string ub = Body(src, update);
        if (Wrapped.Matches(ub).Count != 1 ||
            !ub.Contains("RunningMan_ResumeSecondSprint", StringComparison.Ordinal) ||
            !Regex.IsMatch(ub,
                @"stateContext\s*\.\s*SetPermanentBoolParameter\s*\(\s*" +
                @"n""RunningMan_ResumeSecondSprint""\s*,\s*false\s*,\s*true\s*\)"))
            return new("REFUSED", "RM_STAND_ORIGINAL_RESUME_TRANSITION_UNPROVEN", result);
        bool field = HasField(src,"StandEvents",StandField);
        if (!field && ContainsReservedCollision(src,StandField,"StandEvents"))
            return new("REFUSED","RM_STAND_FIELD_COLLISION",result);
        var guards = StandGuard.Matches(ub);
        if (guards.Count != 1)
            return new("REFUSED","RM_STAND_CADENCE_GUARD_UNPROVEN",result);
        var guard = guards[0];
        bool guarded = guard.Groups["cached"].Success;
        var tk = SourceAdmission.ReadTokens(ub);
        int tokenIndex = -1;
        for (int i=0;i<tk.Count;i++)
            if (tk[i].Start==guard.Index+guard.Length-1 &&
                tk[i].Value=="{") { tokenIndex=i; break; }
        int end = Close(tk,tokenIndex,"{","}");
        if (end < 0)
            return new("REFUSED","RM_STAND_RESUME_BLOCK_UNBALANCED",result);
        int endOffset = tk[end].Start;
        string block = ub[(guard.Index+guard.Length)..endOffset];
        if (Regex.Matches(block,
            @"GetPermanentBoolParameter\s*\(\s*n""RunningMan_ResumeSecondSprint""").Count!=1 ||
            Regex.IsMatch(block, @"\breturn\s*;"))
            return new("REFUSED","RM_STAND_RESUME_BLOCK_CHANGED",result);
        var checks = StandChecked.Matches(block);
        if (checks.Count > 1)
            return new("REFUSED","RM_STAND_DUPLICATE_COMPLETION",result);
        if (!guarded)
        {
            result.Add(new(update.BodyStart+guard.Index+3,
                update.BodyStart+guard.Index+3,
                "!this." + StandField + " && "));
        }
        if (checks.Count == 0)
            result.Add(new(update.BodyStart+endOffset,
                update.BodyStart+endOffset,
                NL(src) + "    this." + StandField + " = true;" + NL(src) + "  "));
        else if (!Regex.IsMatch(block,
            @"this\s*\.\s*" + StandField + @"\s*=\s*true\s*;\s*$"))
            return new("REFUSED","RM_STAND_COMPLETION_POSITION_CHANGED",
                new List<Edit>());

        if (enter is null)
        {
            if (Regex.IsMatch(src,
                @"@\s*wrapMethod\s*\(\s*StandEvents\s*\)\s*" +
                @"(?:public|private|protected)?\s*func\s+OnEnter"))
                return new("REFUSED","RM_STAND_ENTER_AMBIGUOUS",new List<Edit>());
            string nl=NL(src);
            string method="@wrapMethod(StandEvents)" + nl +
                "public func OnEnter(stateContext: ref<StateContext>, " +
                "scriptInterface: ref<StateGameScriptInterface>) -> Void {" + nl +
                "  wrappedMethod(stateContext, scriptInterface);" + nl +
                "  this." + StandField + " = false;" + nl + "}" + nl + nl;
            result.Add(new(update.Header,update.Header,method));
        }
        else
        {
            string eb = Body(src, enter);
            if (Wrapped.Matches(eb).Count != 1)
                return new("REFUSED","RM_STAND_ENTER_FALLBACK_UNPROVEN",
                    new List<Edit>());
            var resets = StandReset.Matches(eb);
            if (resets.Count > 1)
                return new("REFUSED","RM_STAND_DUPLICATE_RESET",
                    new List<Edit>());
            if (resets.Count == 0)
            {
                var w=Wrapped.Match(eb);
                result.Add(new(enter.BodyStart+w.Index+w.Length,
                    enter.BodyStart+w.Index+w.Length,
                    NL(src)+"  this."+StandField+" = false;"));
            }
            else if (resets[0].Index < Wrapped.Match(eb).Index)
                return new("REFUSED","RM_STAND_RESET_PRECEDES_FALLBACK",
                    new List<Edit>());
        }
        if (!field)
            result.Add(new(0,0,"@addField(StandEvents)"+NL(src)+
                "private let "+StandField+": Bool;"+NL(src)+NL(src)));
        return new(result.Count>0?"APPLIED":"COMPLETE",null,result);
    }

    private static string Apply(string source,IEnumerable<Edit> edits)
    {
        var all=edits.OrderByDescending(e=>e.Start).ThenByDescending(e=>e.End).ToArray();
        if (all.Any(e=>e.Start<0 || e.End>source.Length || e.Start>e.End))
            throw new InvalidDataException("RM_INVALID_SOURCE_SPAN");
        for (int i=1;i<all.Length;i++)
            if (all[i].End>all[i-1].Start)
                throw new InvalidDataException("RM_OVERLAPPING_SOURCE_EDITS");
        // Several independent addField edits may share offset 0.
        string updated=source;
        foreach (var e in all)
            updated=updated[..e.Start]+e.Text+updated[e.End..];
        return updated;
    }

    public static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("RM_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot,game) || Inside(output,basePreviewRoot) ||
            Inside(basePreviewRoot,output)))
            throw new InvalidDataException("RM_OVERLAY_UNSAFE");

        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string sourceHash=admission.SourceStackSha256 ??
            throw new InvalidDataException("RM_SOURCE_HASH_MISSING");
        var candidates=Directory.EnumerateFiles(scripts,"*.reds",
            SearchOption.AllDirectories)
            .Where(f=>{
                string s=File.ReadAllText(f,Encoding.UTF8);
                return s.Contains("EvaluateTwoStepSprint",StringComparison.Ordinal) &&
                    s.Contains("RunningMan_ResumeSecondSprint",StringComparison.Ordinal) &&
                    s.Contains("StandEvents",StringComparison.Ordinal) &&
                    s.Contains("SprintEvents",StringComparison.Ordinal);
            }).ToArray();
        string? reason=null, status="NOT_APPLICABLE";
        int applied=0,complete=0;
        var changed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        if (candidates.Length > 1)
        {
            status="REFUSED";
            reason="RM_OWNER_SOURCE_AMBIGUOUS";
        }
        else if (candidates.Length==1)
        {
            string file=candidates[0];
            string rel="r6/scripts/"+Path.GetRelativePath(scripts,file).Replace('\\','/');
            string source=File.ReadAllText(file,Encoding.UTF8);
            if (basePreviewRoot is not null)
            {
                string overlayFile=Path.GetFullPath(Path.Combine(basePreviewRoot,
                    rel.Replace('/',Path.DirectorySeparatorChar)));
                if (!Inside(overlayFile,basePreviewRoot))
                    throw new InvalidDataException("RM_OVERLAY_PATH_ESCAPE");
                if (File.Exists(overlayFile))
                    source=File.ReadAllText(overlayFile,Encoding.UTF8);
            }
            var methods=Methods(source);
            var a=Sprint(source,methods);
            var b=Stand(source,methods);
            var accepted=new List<Edit>();
            if (a.Status=="APPLIED") {accepted.AddRange(a.Edits);applied++;}
            else if (a.Status=="COMPLETE") complete++;
            if (b.Status=="APPLIED") {accepted.AddRange(b.Edits);applied++;}
            else if (b.Status=="COMPLETE") complete++;
            reason=string.Join(";",
                new[]{a.Reason,b.Reason}.Where(x=>x is not null));
            if (string.IsNullOrEmpty(reason)) reason=null;
            if (accepted.Count>0)
            {
                string updated=Apply(source,accepted);
                var verified=Methods(updated);
                var sprintProof=Sprint(updated,verified);
                var standProof=Stand(updated,verified);
                if ((a.Status=="APPLIED" && sprintProof.Status!="COMPLETE") ||
                    (b.Status=="APPLIED" && standProof.Status!="COMPLETE"))
                    throw new InvalidDataException(
                        "RM_GENERATOR_POSTCONDITION_INCOMPLETE:"+
                        sprintProof.Reason+":"+standProof.Reason);
                changed.Add(rel,updated);
                status="APPLIED";
            }
            else status=reason is not null?"REFUSED":"COMPLETE";
        }
        else if(Directory.EnumerateDirectories(scripts,"RunningMan",
                    SearchOption.AllDirectories).Any())
        {
            status="REFUSED";reason="RM_OWNER_TARGET_REMOVED_OR_INCOMPATIBLE";
        }
        if (!string.Equals(sourceHash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RM_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach(var pair in changed)
        {
            string target=Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(target,output) ||
                !target.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RM_OWNER_OUTPUT_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,pair.Value,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,applied,complete,
            changed.Keys.Order(StringComparer.Ordinal).ToArray(),
            reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true}),new UTF8Encoding(false));
        return result;
    }
    private static bool Inside(string path,string root)=>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
