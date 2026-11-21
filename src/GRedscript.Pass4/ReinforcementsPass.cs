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

    internal static Pass4Rewrite RewriteUtils(string original)
    {
        if(!Regex.IsMatch(original,@"\bstruct\s+SquareArea\b")||
           !Regex.IsMatch(original,@"\bprivate\s+func\s+SortSquare\s*\(")||
           !Regex.IsMatch(original,@"\bprivate\s+func\s+PointInTriangle\s*\(")||
           Pass4Source.FindMethod(original,"IsPointInQuad") is null)
            return Pass4Rewrite.Refuse("RS_UTILS_ORIGINAL_GEOMETRY_UNPROVEN");
        if(!original.Contains("GCO-GENERIC:ANGLE_SORT_INVARIANT_V1",StringComparison.Ordinal)||
           !original.Contains("GCO-GENERIC:QUAD_SAFE_AABB_BROADPHASE_V1",StringComparison.Ordinal))
            return Pass4Rewrite.Refuse("RS_EXPECTED_GENERIC_GEOMETRY_MISSING");

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
        if(!source.Contains("return IsPointInQuad(p, sq);",StringComparison.Ordinal)||
           !source.Contains("GCO-GENERIC:ANGLE_SORT_INVARIANT_V1",StringComparison.Ordinal)||
           !source.Contains("GCO-GENERIC:QUAD_SAFE_AABB_BROADPHASE_V1",StringComparison.Ordinal))
            throw new InvalidDataException("RS_UTILS_POSTCONDITION");
        return edits==0?Pass4Rewrite.Complete(done):
            Pass4Rewrite.Applied(source,edits,done);
    }
}
