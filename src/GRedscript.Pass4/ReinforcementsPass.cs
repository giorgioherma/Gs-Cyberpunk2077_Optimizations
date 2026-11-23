using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Pass 4: pre-sort stable owner-owned restricted quadrilaterals at attach.
/// The public array remains mutable. Before any cached test compare all eight
/// live coordinates against the cached snapshot; on mismatch or absent cache
/// call the ORIGINAL generic-optimized IsPointInQuad. No RNG, eligibility,
/// quest, district, timing, polygon boundaries or generic source is replaced.
/// </summary>
internal static class ReinforcementsPass
{
    private const string Cache="gcoPreparedRestrictedCordinates";
    private const string Class="GcoPreparedReinforcementSquare";
    private static readonly Regex CacheField=new(
        @"\bprivate\s+let\s+gcoPreparedRestrictedCordinates\s*:\s*array<ref<GcoPreparedReinforcementSquare>>\s*;");
    private static readonly Regex FieldAnchor=new(
        @"\bpublic\s+let\s+restrictedCordinates\s*:\s*array<SquareArea>\s*;");
    private static readonly Regex OriginalTest=new(
        @"\bif\s*\(\s*IsPointInQuad\s*\(\s*p\s*,\s*this\s*\.\s*restrictedCordinates\s*\[\s*i\s*\]\s*\)\s*\)\s*\{");
    private static readonly Regex ChangedTest=new(
        @"\bGcoIsPointInPreparedReinforcementSquare\s*\(\s*p\s*,\s*this\s*\.\s*restrictedCordinates\s*\[\s*i\s*\]");
    private static readonly Regex PrepareCall=new(
        @"\bthis\s*\.\s*GcoPrepareRestrictedAreas\s*\(\s*\)\s*;");

    internal static Pass4Rewrite RewriteSystem(string original)
    {
        if(!Regex.IsMatch(original,@"\bclass\s+ReinforcementSystem\s+extends\s+ScriptableSystem\b"))
            return Pass4Rewrite.Refuse("RS_SYSTEM_OWNER_UNPROVEN");
        if(Regex.IsMatch(original,@"\bpreparedRestrictedCordinates\b|\bIsPointInPreparedQuad\b"))
            return Pass4Rewrite.Refuse("RS_LEGACY_UNGUARDED_CACHE_NEEDS_REVIEW");
        string source=original;
        int edits=0,done=0;
        var anchors=FieldAnchor.Matches(source);
        var fields=CacheField.Matches(source);
        if(anchors.Count!=1||fields.Count>1 ||
           (source.Contains(Cache,StringComparison.Ordinal)&&fields.Count==0))
            return Pass4Rewrite.Refuse("RS_AREA_CACHE_FIELD_CONFLICT");
        if(fields.Count==0) {
            var m=anchors[0];
            string nl=Pass4Source.NewLine(source);
            source=source.Insert(m.Index+m.Length,nl+
                "    private let gcoPreparedRestrictedCordinates: array<ref<GcoPreparedReinforcementSquare>>;");
            edits++;
        }else done++;

        var check=Pass4Source.FindMethod(source,"ReinforcementsChecksCall");
        var attach=Pass4Source.FindMethod(source,"OnPlayerAttach");
        if(check is null||attach is null||
           !Pass4Source.Body(source,check).Contains("this.restrictedCordinates",StringComparison.Ordinal)||
           !Pass4Source.Body(source,check).Contains("this.restrictedCharacters",StringComparison.Ordinal)||
           !Pass4Source.Body(source,attach).Contains("this.restrictedCordinates",StringComparison.Ordinal))
            return Pass4Rewrite.Refuse("RS_AREA_CALL_OR_ATTACH_UNPROVEN");

        string checkBody=Pass4Source.Body(source,check);
        if(ChangedTest.IsMatch(checkBody)) {
            if(!checkBody.Contains("IsPointInQuad(p, this.restrictedCordinates[i])",StringComparison.Ordinal))
                return Pass4Rewrite.Refuse("RS_CACHE_MUTATION_FALLBACK_MISSING");
            done++;
        }else {
            var m=OriginalTest.Matches(checkBody);
            if(m.Count!=1)
                return Pass4Rewrite.Refuse("RS_ORIGINAL_RESTRICTED_AREA_TEST_UNPROVEN");
            string nl=Pass4Source.NewLine(source);
            string replacement=
                "let gcoInsideRestricted: Bool = false;"+nl+
                "            if i < ArraySize(this.gcoPreparedRestrictedCordinates) {"+nl+
                "                gcoInsideRestricted = GcoIsPointInPreparedReinforcementSquare("+
                "p, this.restrictedCordinates[i], this.gcoPreparedRestrictedCordinates[i]);"+nl+
                "            } else {"+nl+
                "                gcoInsideRestricted = IsPointInQuad(p, this.restrictedCordinates[i]);"+nl+
                "            }"+nl+
                "            if (gcoInsideRestricted) {";
            checkBody=checkBody[..m[0].Index]+replacement+
                checkBody[(m[0].Index+m[0].Length)..];
            source=Pass4Source.ReplaceBody(source,check,checkBody);
            edits++;
        }
        attach=Pass4Source.FindMethod(source,"OnPlayerAttach")!;
        string attachBody=Pass4Source.Body(source,attach);
        if(PrepareCall.IsMatch(attachBody)) done++;
        else {
            var list=Regex.Matches(attachBody,
                @"\bthis\s*\.\s*restrictedCordinates\s*=\s*\[");
            if(list.Count!=1)
                return Pass4Rewrite.Refuse("RS_RESTRICTED_AREAS_INIT_AMBIGUOUS");
            int bracket=list[0].Index+list[0].Length-1;
            var toks=SourceAdmission.ReadTokens(attachBody);
            int i=Enumerable.Range(0,toks.Count)
                .FirstOrDefault(k=>toks[k].Start==bracket&&toks[k].Value=="[",-1);
            if(i<0) return Pass4Rewrite.Refuse("RS_INIT_ARRAY_TOKEN_NOT_FOUND");
            int last=Pass4Source.MatchClose(toks,i,"[","]");
            if(last<0) return Pass4Rewrite.Refuse("RS_INIT_ARRAY_UNBALANCED");
            int after=toks[last].End;
            while(after<attachBody.Length&&char.IsWhiteSpace(attachBody[after])) after++;
            if(after>=attachBody.Length||attachBody[after]!=';')
                return Pass4Rewrite.Refuse("RS_INIT_ARRAY_TERMINATOR_CHANGED");
            string nl=Pass4Source.NewLine(source);
            attachBody=attachBody.Insert(after+1,nl+
                "        this.GcoPrepareRestrictedAreas();");
            source=Pass4Source.ReplaceBody(source,attach,attachBody);
            edits++;
        }

        if(Pass4Source.FindMethod(source,"GcoPrepareRestrictedAreas") is not null) {
            var m=Pass4Source.FindMethod(source,"GcoPrepareRestrictedAreas")!;
            var method=Pass4Source.Body(source,m);
            if(!method.Contains("ArrayClear(this.gcoPreparedRestrictedCordinates)",StringComparison.Ordinal)||
               !method.Contains("GcoPrepareReinforcementSquare(",StringComparison.Ordinal))
                return Pass4Rewrite.Refuse("RS_PREPARE_HELPER_INCOMPATIBLE");
            done++;
        }else {
            if(Regex.IsMatch(source,@"\bGcoPrepareRestrictedAreas\s*\(")&&
                !PrepareCall.IsMatch(source))
                return Pass4Rewrite.Refuse("RS_PREPARE_HELPER_OTHER_DEFINITION");
            string nl=Pass4Source.NewLine(source);
            string method=nl+nl+
                "    private func GcoPrepareRestrictedAreas() -> Void {"+nl+
                "        ArrayClear(this.gcoPreparedRestrictedCordinates);"+nl+
                "        let i: Int32 = 0;"+nl+
                "        while i < ArraySize(this.restrictedCordinates) {"+nl+
                "            ArrayPush(this.gcoPreparedRestrictedCordinates, "+
                "GcoPrepareReinforcementSquare(this.restrictedCordinates[i]));"+nl+
                "            i += 1;"+nl+
                "        }"+nl+"    }"+nl;
            var anchor=Regex.Match(source,@"\bpublic\s+static\s+func\s+GetInstance\s*\(");
            if(!anchor.Success)
                return Pass4Rewrite.Refuse("RS_OWNER_METHOD_INSERTION_UNPROVEN");
            source=source.Insert(anchor.Index,method);
            edits++;
        }
        // OnPlayerDetach is a class event callback. Keep any existing custom
        // owner cleanup; clear only the private cache.
        var detach=Pass4Source.FindMethod(source,"OnPlayerDetach");
        if(detach is not null) {
            var body=Pass4Source.Body(source,detach);
            if(body.Contains("ArrayClear(this.gcoPreparedRestrictedCordinates)",StringComparison.Ordinal))
                done++;
            else {
                body=Pass4Source.NewLine(source)+
                    "        ArrayClear(this.gcoPreparedRestrictedCordinates);"+body;
                source=Pass4Source.ReplaceBody(source,detach,body);
                edits++;
            }
        }else {
            string nl=Pass4Source.NewLine(source);
            var anchor=Regex.Match(source,@"\bpublic\s+static\s+func\s+GetInstance\s*\(");
            if(!anchor.Success)
                return Pass4Rewrite.Refuse("RS_DETACH_ANCHOR_MISSING");
            source=source.Insert(anchor.Index,
                nl+"    private final func OnPlayerDetach(request: ref<PlayerDetachRequest>) -> Void {"+
                nl+"        ArrayClear(this.gcoPreparedRestrictedCordinates);"+
                nl+"    }"+nl+nl);
            edits++;
        }
        if(!CacheField.IsMatch(source)||
           !PrepareCall.IsMatch(source)||
           !source.Contains("GcoIsPointInPreparedReinforcementSquare(",StringComparison.Ordinal))
            throw new InvalidDataException("RS_SYSTEM_POSTCONDITION");
        return edits==0?Pass4Rewrite.Complete(done):
            Pass4Rewrite.Applied(source,edits,done);
    }

    // A marker is not a behavior proof. The existing (post-generic or
    // compatible custom) quad method must have the SAME two-triangle
    // decision, and its sort must depend only on current quad coordinates.
    // Otherwise caching could suppress extra custom restrictions or change
    // side effects. Refuse just the geometry operation.
    private static string? GeometryProof(string source)
    {
        static string WithoutComments(string s) =>
            Regex.Replace(s,@"//[^\r\n]*|/\*[\s\S]*?\*/","");
        static string Compact(string s) =>
            Regex.Replace(WithoutComments(s),@"\s+","");
        static bool CallsOnly(string body,params string[] allowed)
        {
            var tokens=SourceAdmission.ReadTokens(body);
            for(int i=0;i+1<tokens.Count;i++)
            {
                if(tokens[i+1].Value!="(" ||
                   !Regex.IsMatch(tokens[i].Value,@"^[A-Za-z_]\w*$") ||
                   tokens[i].Value is "if" or "while" or "switch")
                    continue;
                if(!allowed.Contains(tokens[i].Value,StringComparer.Ordinal))
                    return false;
            }
            return true;
        }
        var quad=Pass4Source.FindMethod(source,"IsPointInQuad");
        var sorter=Pass4Source.FindMethod(source,"SortSquare");
        if(quad is null||sorter is null)
            return "RS_QUAD_OR_SORT_METHOD_NOT_UNIQUE";
        string quadBody=WithoutComments(Pass4Source.Body(source,quad));
        var sorted=Regex.Matches(quadBody,
            @"\blet\s+pts\s*=\s*SortSquare\s*\(\s*sq\s*\)\s*;");
        if(sorted.Count!=1)
            return "RS_QUAD_SORT_CALL_FLOW_CHANGED";
        string tail=Compact(quadBody[(sorted[0].Index+sorted[0].Length)..]);
        const string expectedTail=
            "if(PointInTriangle(p,pts[0],pts[1],pts[2])){returntrue;}"+
            "if(PointInTriangle(p,pts[0],pts[2],pts[3])){returntrue;}"+
            "returnfalse;";
        if(tail!=expectedTail)
            return "RS_QUAD_CUSTOM_RESTRICTION_OR_TRIANGLE_FLOW_UNPROVEN";
        // Old compatible code may have no generic AABB at all. If the
        // generic broadphase is present, it must remain the known strict,
        // conservative finite/nondegenerate rejection, with no extra calls.
        string prefix=Compact(quadBody[..sorted[0].Index]);
        if(prefix.Length>0)
        {
            if(!prefix.StartsWith("letoutside=",StringComparison.Ordinal)||
               !prefix.Contains(";ifoutside&&",StringComparison.Ordinal)||
               !prefix.EndsWith("{returnfalse;}",StringComparison.Ordinal)||
               !prefix.Contains("AbsF(Sign(",StringComparison.Ordinal)||
               !prefix.Contains(")>1.0",StringComparison.Ordinal)||
               !prefix.Contains("-16.0",StringComparison.Ordinal)||
               !prefix.Contains("+16.0",StringComparison.Ordinal)||
               !prefix.Contains("-100000.0",StringComparison.Ordinal)||
               !prefix.Contains("<100000.0",StringComparison.Ordinal)||
               !CallsOnly(quadBody[..sorted[0].Index],"AbsF","Sign")||
               SourceAdmission.ReadTokens(quadBody[..sorted[0].Index]).Any(t =>
                   Regex.IsMatch(t.Value,@"^[A-Za-z_]\w*$") &&
                   !new[]{"p","sq","outside","a","b","c","d","X","Y",
                       "let","if","return","false","AbsF","Sign"}
                       .Contains(t.Value,StringComparer.Ordinal)))
                return "RS_QUAD_BROADPHASE_NOT_PROVEN_CONSERVATIVE";
            foreach(string v in new[]{"a","b","c","d"})
                if(!prefix.Contains("sq."+v+".X",StringComparison.Ordinal)||
                   !prefix.Contains("sq."+v+".Y",StringComparison.Ordinal))
                    return "RS_QUAD_BROADPHASE_VERTEX_INPUT_CHANGED";
        }
        string sortBody=WithoutComments(Pass4Source.Body(source,sorter));
        string compactSort=Compact(sortBody);
        const string direct="return[sq.a,sq.b,sq.c,sq.d];";
        if(compactSort==direct)
            return null; // A structurally proved pure alternate sorter.
        if(!Regex.IsMatch(sortBody,
                @"\blet\s+pts\s*:\s*array<Vector2>\s*=\s*\[\s*sq.a\s*,\s*sq.b\s*,\s*sq.c\s*,\s*sq.d\s*\]\s*;")||
           !Regex.IsMatch(sortBody,
                @"\blet\s+center\s*=\s*GetCenter\s*\(\s*sq\s*\)\s*;")||
           !compactSort.EndsWith("returnpts;",StringComparison.Ordinal)||
           !CallsOnly(sortBody,"GetCenter","AtanF","ArraySize")||
           Regex.IsMatch(sortBody,@"\bthis\b|\bGameInstance\b"))
            return "RS_SORT_NONLOCAL_OR_CHANGED_DEPENDENCIES";
        var center=Pass4Source.FindMethod(source,"GetCenter");
        if(center is null)
            return "RS_SORT_CENTER_HELPER_MISSING";
        string centerBody=WithoutComments(Pass4Source.Body(source,center));
        string centerText=Compact(centerBody);
        if(!centerText.StartsWith("returnnewVector2(",StringComparison.Ordinal)||
           !centerText.EndsWith(");",StringComparison.Ordinal)||
           !CallsOnly(centerBody,"Vector2")||
           Regex.IsMatch(centerBody,@"\bthis\b|\bGameInstance\b")||
           SourceAdmission.ReadTokens(centerBody).Any(t =>
               Regex.IsMatch(t.Value,@"^[A-Za-z_]\w*$") &&
               !new[]{"sq","a","b","c","d","X","Y","Vector2","new","return"}
                   .Contains(t.Value,StringComparer.Ordinal)))
            return "RS_SORT_CENTER_HELPER_NOT_PURE";
        foreach(string v in new[]{"a","b","c","d"})
            if(!centerText.Contains("sq."+v+".X",StringComparison.Ordinal)||
               !centerText.Contains("sq."+v+".Y",StringComparison.Ordinal))
                return "RS_SORT_CENTER_INPUT_CHANGED";
        // No stateful closure: all identifiers are argument/local/vector
        // components, accepted pure arithmetic primitives or syntax.
        var known=new HashSet<string>(StringComparer.Ordinal)
        {
            "sq","a","b","c","d","X","Y","center","pts","GetCenter",
            "ArraySize","AtanF","Vector2","Int32","array","let",
            "if","while","return","new","true","false"
        };
        foreach(Match local in Regex.Matches(sortBody,@"\blet\s+(\w+)"))
            known.Add(local.Groups[1].Value);
        foreach(var tok in SourceAdmission.ReadTokens(sortBody))
            if(Regex.IsMatch(tok.Value,@"^[A-Za-z_]\w*$")&&
               !known.Contains(tok.Value))
                return "RS_SORT_NONLOCAL_IDENTIFIER";
        return null;
    }

    internal static Pass4Rewrite RewriteUtils(string original)
    {
        if(!Regex.IsMatch(original,@"\bstruct\s+SquareArea\b")||
           !Regex.IsMatch(original,@"\bprivate\s+func\s+SortSquare\s*\(")||
           !Regex.IsMatch(original,@"\bprivate\s+func\s+PointInTriangle\s*\(")||
           Pass4Source.FindMethod(original,"IsPointInQuad") is null)
            return Pass4Rewrite.Refuse("RS_UTILS_ORIGINAL_GEOMETRY_UNPROVEN");
        string? geometryError=GeometryProof(original);
        if(geometryError is not null)
            return Pass4Rewrite.Refuse(geometryError);

        string nl=Pass4Source.NewLine(original),source=original;
        int edits=0,done=0;
        const string classSignature="public class GcoPreparedReinforcementSquare extends IScriptable";
        if(source.Contains("class GcoPreparedReinforcementSquare",StringComparison.Ordinal)) {
            if(!source.Contains(classSignature,StringComparison.Ordinal)||
               !source.Contains("public let original: SquareArea;",StringComparison.Ordinal)||
               !source.Contains("public let ordered: array<Vector2>;",StringComparison.Ordinal))
                return Pass4Rewrite.Refuse("RS_PREPARED_CLASS_INCOMPATIBLE");
            done++;
        }else {
            var anchor=Regex.Match(source,@"\bpublic\s+struct\s+SquareArea\s*\{");
            if(!anchor.Success) return Pass4Rewrite.Refuse("RS_SQUARE_STRUCT_NOT_UNIQUE");
            var tokens=SourceAdmission.ReadTokens(source);
            int open=Enumerable.Range(0,tokens.Count)
                .FirstOrDefault(i=>tokens[i].Start==anchor.Index+anchor.Length-1
                    &&tokens[i].Value=="{",-1);
            int end=Pass4Source.MatchClose(tokens,open);
            if(end<0) return Pass4Rewrite.Refuse("RS_SQUARE_STRUCT_UNBALANCED");
            string add=nl+nl+classSignature+" {"+nl+
                "    public let original: SquareArea;"+nl+
                "    public let ordered: array<Vector2>;"+nl+"}"+nl;
            source=source.Insert(tokens[end].End,add);
            edits++;
        }
        if(Pass4Source.FindMethod(source,"GcoPrepareReinforcementSquare") is not null) {
            var m=Pass4Source.FindMethod(source,"GcoPrepareReinforcementSquare")!;
            if(!Pass4Source.Body(source,m).Contains("SortSquare(sq)",StringComparison.Ordinal)||
               !Pass4Source.Body(source,m).Contains("prepared.original = sq",StringComparison.Ordinal))
                return Pass4Rewrite.Refuse("RS_PREPARE_SQUARE_HELPER_CHANGED");
            done++;
        }else {
            source+=nl+nl+
                "public func GcoPrepareReinforcementSquare(sq: SquareArea) -> ref<GcoPreparedReinforcementSquare> {"+nl+
                "    let prepared = new GcoPreparedReinforcementSquare();"+nl+
                "    prepared.original = sq;"+nl+
                "    prepared.ordered = SortSquare(sq);"+nl+
                "    return prepared;"+nl+"}"+nl;
            edits++;
        }
        if(Pass4Source.FindMethod(source,"GcoIsPointInPreparedReinforcementSquare") is not null) {
            var m=Pass4Source.FindMethod(source,"GcoIsPointInPreparedReinforcementSquare")!;
            var body=Pass4Source.Body(source,m);
            if(!body.Contains("IsPointInQuad(p, sq)",StringComparison.Ordinal)||
               !body.Contains("PointInTriangle(",StringComparison.Ordinal)||
               !body.Contains("sq.d.Y != prepared.original.d.Y",StringComparison.Ordinal))
                return Pass4Rewrite.Refuse("RS_PREPARED_TEST_FALLBACK_UNPROVEN");
            done++;
        }else {
            source+=nl+nl+
                "public func GcoIsPointInPreparedReinforcementSquare(p: Vector2, sq: SquareArea, prepared: ref<GcoPreparedReinforcementSquare>) -> Bool {"+nl+
                "    if !IsDefined(prepared) ||"+nl+
                "       sq.a.X != prepared.original.a.X || sq.a.Y != prepared.original.a.Y ||"+nl+
                "       sq.b.X != prepared.original.b.X || sq.b.Y != prepared.original.b.Y ||"+nl+
                "       sq.c.X != prepared.original.c.X || sq.c.Y != prepared.original.c.Y ||"+nl+
                "       sq.d.X != prepared.original.d.X || sq.d.Y != prepared.original.d.Y ||"+nl+
                "       ArraySize(prepared.ordered) != 4 {"+nl+
                "        return IsPointInQuad(p, sq);"+nl+
                "    }"+nl+
                "    if PointInTriangle(p, prepared.ordered[0], prepared.ordered[1], prepared.ordered[2]) {"+nl+
                "        return true;"+nl+
                "    }"+nl+
                "    return PointInTriangle(p, prepared.ordered[0], prepared.ordered[2], prepared.ordered[3]);"+nl+
                "}"+nl;
            edits++;
        }
        var originalMarkers=Regex.Matches(original,
            @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
            .Select(m=>m.Value).Distinct(StringComparer.Ordinal);
        if(!source.Contains("return IsPointInQuad(p, sq);",StringComparison.Ordinal)||
           originalMarkers.Any(m=>!source.Contains(m,StringComparison.Ordinal)))
            throw new InvalidDataException("RS_UTILS_POSTCONDITION");
        return edits==0?Pass4Rewrite.Complete(done):
            Pass4Rewrite.Applied(source,edits,done);
    }
}
