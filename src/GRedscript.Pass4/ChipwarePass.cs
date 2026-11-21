using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Pass 4: reject unrelated perk types before the expensive per-call player,
/// stats and memory read. The whitelist is recovered from the current owner's
/// actual guarded perk branches, never a historical enum list.
/// </summary>
internal static class ChipwarePass
{
    private static readonly Regex Guard=new(
        @"\bif\s*\(\s*Equals\s*\(\s*(?:gamedataNewPerkType\.(?<a>\w+)\s*,\s*perkType|perkType\s*,\s*gamedataNewPerkType\.(?<b>\w+))\s*\)\s*\)\s*\{");
    private static readonly Regex RelevantDecl=new(
        @"\blet\s+relevant\s*=\s*(?<expr>[\s\S]*?)\s*;");
    private static readonly Regex RelevantElement=new(
        @"Equals\s*\(\s*perkType\s*,\s*gamedataNewPerkType\.(?<name>\w+)\s*\)");
    private static readonly Regex EarlyReturn=new(
        @"\bif\s*!relevant\s*\{\s*return\s+wrappedMethod\s*\(\s*perkType\s*\)\s*;\s*\}");
    private static readonly Regex OriginalReturn=new(
        @"\breturn\s+wrappedMethod\s*\(\s*perkType\s*\)\s*;");
    private static readonly Regex Comment=new(
        @"//[^\r\n]*|/\*[\s\S]*?\*/");

    internal static Pass4Rewrite Rewrite(string original)
    {
        var method=Pass4Source.FindMethod(original,"IsNewPerkBought");
        if(method is null||
           !Pass4Source.HasDirective(original,method,"@wrapMethod(PlayerDevelopmentData)"))
            return Pass4Rewrite.Refuse("CHIP_PERK_WRAPPER_OWNERSHIP_UNPROVEN");
        string body=Pass4Source.Body(original,method);
        string clean=Comment.Replace(body,"");
        var guards=Guard.Matches(clean);
        if(guards.Count==0)
            return Pass4Rewrite.Refuse("CHIP_PERK_GUARDED_BRANCHES_MISSING");
        // A partially/fully optimized source already has its own relevant
        // predicate; that expression is NOT another owner gameplay branch.
        // Count only the original guarded comparisons after removing this
        // optional generated/handwritten predicate declaration.
        string originalGuardText=RelevantDecl.Replace(clean,"");
        if(Regex.Matches(originalGuardText,@"\bEquals\s*\([^)]*\bperkType\b").Count!=guards.Count)
            return Pass4Rewrite.Refuse("CHIP_PERK_UNRECOGNIZED_COMPARISON");
        var names=new List<string>();
        foreach(Match guard in guards) {
            string name=guard.Groups["a"].Success?
                guard.Groups["a"].Value:guard.Groups["b"].Value;
            if(!names.Contains(name,StringComparer.Ordinal))
                names.Add(name);
        }
        if(names.Count==0) return Pass4Rewrite.Refuse("CHIP_PERK_NO_ENUM_GUARDS");
        if(Regex.Matches(clean,@"\breturn\s+[0-9]+\s*;").Count!=guards.Count)
            return Pass4Rewrite.Refuse("CHIP_PERK_NONTRIVIAL_GUARD_RETURNS");
        if(OriginalReturn.Matches(clean).Count is not (1 or 2))
            return Pass4Rewrite.Refuse("CHIP_PERK_FALLBACK_COUNT_UNPROVEN");
        // All top-level side effects before guarded branches must be exactly
        // the original read-only game/player/stats/memory initialization.
        string prefix=clean[..guards[0].Index];
        prefix=RelevantDecl.Replace(prefix,"");
        prefix=EarlyReturn.Replace(prefix,"");
        var required=new [] {
            @"\blet\s+game\s*=\s*GetGameInstance\s*\(\s*\)\s*;",
            @"\blet\s+player\s*=\s*GetPlayer\s*\(\s*game\s*\)\s*;",
            @"\blet\s+statsSystem\s*:\s*ref<StatsSystem>\s*=\s*GameInstance\s*\.\s*GetStatsSystem\s*\(\s*game\s*\)\s*;",
            @"\blet\s+playerMemory\s*:\s*Float\s*=\s*statsSystem\s*\.\s*GetStatValue\s*\([^;]*\)\s*;"
        };
        foreach(var pattern in required) {
            var m=Regex.Match(prefix,pattern);
            if(!m.Success) return Pass4Rewrite.Refuse("CHIP_PERK_PREAMBLE_CHANGED");
            prefix=prefix.Remove(m.Index,m.Length);
        }
        if(!string.IsNullOrWhiteSpace(prefix))
            return Pass4Rewrite.Refuse("CHIP_PERK_PREAMBLE_SIDE_EFFECT_UNPROVEN");
        // No extra top-level actions can be omitted by the early gate.
        // Each source if block is matched with the existing REDscript lexer.
        var tokens=SourceAdmission.ReadTokens(clean);
        var guardEnds=new List<int>();
        foreach(Match guard in guards) {
            int open=guard.Index+guard.Length-1;
            int index=Enumerable.Range(0,tokens.Count)
                .FirstOrDefault(i=>tokens[i].Start==open&&tokens[i].Value=="{",-1);
            if(index<0) return Pass4Rewrite.Refuse("CHIP_PERK_GUARD_TOKEN_UNPROVEN");
            int end=Pass4Source.MatchClose(tokens,index);
            if(end<0) return Pass4Rewrite.Refuse("CHIP_PERK_GUARD_UNBALANCED");
            guardEnds.Add(tokens[end].End);
        }
        for(int i=0;i+1<guards.Count;i++)
            if(!string.IsNullOrWhiteSpace(clean[guardEnds[i]..guards[i+1].Index]))
                return Pass4Rewrite.Refuse("CHIP_PERK_OTHER_WORK_BETWEEN_GUARDS");
        string suffix=clean[guardEnds[^1]..];
        if(OriginalReturn.Matches(suffix).Count!=1||
            !string.IsNullOrWhiteSpace(OriginalReturn.Replace(suffix,"")))
            return Pass4Rewrite.Refuse("CHIP_PERK_FINAL_FALLBACK_CHANGED");

        int applied=0,done=0;
        var decl=RelevantDecl.Matches(body);
        var early=EarlyReturn.Matches(body);
        if(decl.Count>1||early.Count>1)
            return Pass4Rewrite.Refuse("CHIP_PERK_GATE_DUPLICATED");
        if(decl.Count==0&&early.Count>0)
            return Pass4Rewrite.Refuse("CHIP_PERK_GATE_WITHOUT_RELEVANCE");
        var existing=new List<string>();
        if(decl.Count==1) {
            string expr=decl[0].Groups["expr"].Value;
            foreach(Match m in RelevantElement.Matches(expr))
                if(!existing.Contains(m.Groups["name"].Value,StringComparer.Ordinal))
                    existing.Add(m.Groups["name"].Value);
            string residue=RelevantElement.Replace(expr,"");
            residue=Regex.Replace(residue,@"\|\||\s+","");
            if(residue.Length>0||existing.Count==0)
                return Pass4Rewrite.Refuse("CHIP_PERK_RELEVANCE_EXPRESSION_UNKNOWN");
        }
        string nl=Pass4Source.NewLine(original);
        var missing=names.Where(n=>!existing.Contains(n,StringComparer.Ordinal)).ToList();
        if(decl.Count==0) {
            string statement="let relevant = "+string.Join(" ||"+nl+"        ",
                names.Select(n=>"Equals(perkType, gamedataNewPerkType."+n+")"))+";";
            body=nl+"    "+statement+nl+
                "    if !relevant {"+nl+
                "        return wrappedMethod(perkType);"+nl+"    }"+nl+body;
            applied+=2;
        } else {
            if(missing.Count>0) {
                var match=decl[0];
                string extra=string.Join(" ||"+nl+"        ",
                    missing.Select(n=>"Equals(perkType, gamedataNewPerkType."+n+")"));
                string rewritten=match.Value.TrimEnd().TrimEnd(';')+
                    " ||"+nl+"        "+extra+";";
                body=body[..match.Index]+rewritten+
                    body[(match.Index+match.Length)..];
                applied++;
            }else done++;
            if(early.Count==0) {
                var match=RelevantDecl.Match(body);
                body=body.Insert(match.Index+match.Length,nl+
                    "    if !relevant {"+nl+
                    "        return wrappedMethod(perkType);"+nl+"    }");
                applied++;
            }else done++;
        }
        if(!RelevantDecl.IsMatch(body)||!EarlyReturn.IsMatch(body))
            throw new InvalidDataException("CHIP_PERK_POSTCONDITION");
        if(applied==0) return Pass4Rewrite.Complete(done);
        return Pass4Rewrite.Applied(
            Pass4Source.ReplaceBody(original,method,body),applied,done);
    }
}
