using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.0 Enhanced Vehicle System: one local VehicleComponentPS
/// handle per synchronous VehicleComponent.OnAction invocation. The historical
/// source reduces the same-object GetPS calls from many to one. Preserve all
/// generic source, dynamic PS field reads/writes, event timing and callbacks.
/// This does not import the historical action/player/vehicle code.
/// </summary>
internal static class EnhancedVehicleOnActionPsPass
{
    internal const string Rule = "EVS_ONACTION_PS_HANDLE_REUSE_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Block(int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Call(int Start, int End);
    private sealed record Decision(string Status, string? Reason, string? Changed,
        int ReplacedCalls);

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (start < 0 || start >= t.Count || t[start].Value != opening)
            return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? FindOwnerOnAction(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != "OnAction" ||
                t[i + 2].Value != "(") continue;
            int args = Match(t, i + 2, "(", ")");
            if (args < 0) return null;
            string signature = string.Join(" ", t.Skip(i + 3)
                .Take(args - (i + 3)).Select(x => x.Value));
            if (!signature.Contains("action : ListenerAction", StringComparison.Ordinal))
                continue;
            string pre = source[Math.Max(0, t[i].Start - 300)..t[i].Start];
            if (!Regex.IsMatch(pre,
                @"@replaceMethod\s*\(\s*VehicleComponent\s*\)\s*" +
                @"(?:(?:public|protected|private|final|cb)\s+)*$"))
                continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int end = Match(t, open, "{", "}");
            if (end < 0 || found is not null) return null;
            found = new(t[open].Start, t[end].Start);
            i = end;
        }
        return found;
    }

    private static List<Call> Calls(string source, Block method)
    {
        var t = SourceAdmission.ReadTokens(source);
        var calls = new List<Call>();
        for (int i = 0; i + 4 < t.Count; i++)
        {
            if (t[i].Start <= method.Open || t[i + 4].End > method.Close)
                continue;
            if (t[i].Value == "this" && t[i+1].Value == "." &&
                t[i+2].Value == "GetPS" && t[i+3].Value == "(" &&
                t[i+4].Value == ")")
            {
                calls.Add(new(t[i].Start, t[i+4].End));
                i += 4;
            }
        }
        return calls;
    }

    private static bool AtMethodLevel(string src, Block method, int offset)
    {
        // Match braces using the existing lexer (ignores comments/strings).
        // A local declared under a nested branch cannot serve every call.
        int depth = 0;
        foreach (var token in SourceAdmission.ReadTokens(src))
        {
            if (token.Start <= method.Open || token.Start >= offset) continue;
            if (token.Value == "{") depth++;
            else if (token.Value == "}") depth--;
        }
        return depth == 0;
    }

    private static Decision Rewrite(string source)
    {
        var method = FindOwnerOnAction(source);
        if (method is null)
            return new("REFUSED", "EVS_ONACTION_METHOD_MISSING_OR_AMBIGUOUS", null, 0);
        string body = source[(method.Open + 1)..method.Close];
        var calls = Calls(source, method);
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var decls = Regex.Matches(body,
            @"\blet\s+(?<name>[A-Za-z_]\w*)\s*(?::\s*(?:ref|wref)\s*<\s*VehicleComponentPS\s*>)?\s*=\s*this\s*\.\s*GetPS\s*\(\s*\)\s*;");
        if (decls.Count > 1)
            return new("REFUSED", "EVS_PS_HANDLE_AMBIGUOUS", null, 0);
        string? name = decls.Count == 1 ? decls[0].Groups["name"].Value : null;
        Call? declaredCall = null;
        if (decls.Count == 1)
        {
            int abs = method.Open + 1 + decls[0].Index;
            if (!AtMethodLevel(source, method, abs))
                return new("REFUSED", "EVS_PS_LOCAL_NESTED_SCOPE", null, 0);
            var inDeclaration = calls.Where(c => c.Start >= abs &&
                c.End <= abs + decls[0].Length).ToArray();
            if (inDeclaration.Length != 1)
                return new("REFUSED", "EVS_PS_DECLARATION_UNPROVEN", null, 0);
            declaredCall = inDeclaration[0];
        }
        else if (calls.Count == 0)
        {
            return new("REFUSED", "EVS_PS_GETTER_TARGET_ABSENT", null, 0);
        }
        else
        {
            var used = SourceAdmission.ReadTokens(body)
                .Select(x => x.Value).ToHashSet(StringComparer.Ordinal);
            name = "gcoOnActionPS";
            int suffix = 2;
            while (used.Contains(name)) name = "gcoOnActionPS" + suffix++;
        }

        string escaped = Regex.Escape(name!);
        // The original first-use destroyed-PS guard must survive. If there is
        // already a local handle, require an equivalent null/destroyed guard.
        var originalGuard = Regex.Match(body,
            @"\bif\s*\(?\s*this\s*\.\s*GetPS\s*\(\s*\)\s*\.\s*GetIsDestroyed\s*\(\s*\)\s*\)?\s*\{");
        var completeGuard = Regex.Match(body,
            @"\bif\s*\(?\s*!\s*IsDefined\s*\(\s*" + escaped +
            @"\s*\)\s*\|\|\s*" + escaped +
            @"\s*\.\s*GetIsDestroyed\s*\(\s*\)\s*\)?\s*\{");
        if ((originalGuard.Success && completeGuard.Success) ||
            (!originalGuard.Success && !completeGuard.Success))
            return new("REFUSED", "EVS_PS_EARLY_GUARD_UNPROVEN", null, 0);
        var guard = originalGuard.Success ? originalGuard : completeGuard;
        string afterGuard = body[(guard.Index + guard.Length)..];
        if (!Regex.IsMatch(afterGuard,
            @"^\s*return\s+false\s*;\s*\}"))
            return new("REFUSED", "EVS_PS_GUARD_RETURNS_CHANGED", null, 0);
        int guardAbs = method.Open + 1 + guard.Index;
        if (!AtMethodLevel(source, method, guardAbs))
            return new("REFUSED", "EVS_PS_GUARD_NOT_METHOD_LEVEL", null, 0);
        if (decls.Count == 1 &&
            method.Open + 1 + decls[0].Index >= guardAbs)
            return new("REFUSED", "EVS_PS_DECLARATION_AFTER_GUARD", null, 0);

        var edits = new List<Edit>();
        int changes = 0;
        if (originalGuard.Success)
        {
            // Original early return is the invariant. The new null check
            // prevents dereferencing a missing PS, as in game-tested Pass 2.0.
            int beg = method.Open + 1 + originalGuard.Index;
            int end = beg + originalGuard.Length;
            string old = source[beg..end];
            string rewritten = Regex.Replace(old,
                @"this\s*\.\s*GetPS\s*\(\s*\)\s*\.\s*GetIsDestroyed\s*\(\s*\)",
                "!IsDefined(" + name + ") || " + name + ".GetIsDestroyed()");
            edits.Add(new(beg, end, rewritten));
            changes++;
        }
        foreach (var c in calls)
        {
            if (declaredCall is not null && c.Start == declaredCall.Start) continue;
            if (originalGuard.Success && c.Start >= guardAbs &&
                c.End <= guardAbs + originalGuard.Length) continue;
            if (decls.Count == 0 && c.Start < guardAbs)
                return new("REFUSED", "EVS_PS_GETTER_BEFORE_EARLY_GUARD", null, 0);
            if (decls.Count == 1 && c.Start <= method.Open + 1 + decls[0].Index)
                return new("REFUSED", "EVS_PS_GETTER_BEFORE_LOCAL", null, 0);
            edits.Add(new(c.Start,c.End,name!));
            changes++;
        }
        if (decls.Count == 0)
        {
            string line = source[(source.LastIndexOf('\n',Math.Max(0,guardAbs-1))+1)..guardAbs];
            if (line.Any(c=>c!=' '&&c!='\t'))
                return new("REFUSED", "EVS_PS_GUARD_INDENT_UNPROVEN", null, 0);
            edits.Add(new(guardAbs,guardAbs,
                "let " + name + ": ref<VehicleComponentPS> = this.GetPS();" + nl + line));
            changes++;
        }
        if (edits.Count == 0)
            return new("COMPLETE", null, null, 0);
        string updated = source;
        foreach (var e in edits.OrderByDescending(x=>x.Start))
            updated = updated[..e.Start] + e.Text + updated[e.End..];
        var check = FindOwnerOnAction(updated);
        if (check is null)
            throw new InvalidDataException("EVS_PS_OWNER_METHOD_LOST");
        string result = updated[(check.Open+1)..check.Close];
        var remaining = Calls(updated,check);
        if (remaining.Count != 1 ||
            !Regex.IsMatch(result, @"\bif\s*\(?\s*!\s*IsDefined\s*\(\s*" + escaped) ||
            !result.Contains(name + ".GetIsDestroyed()",StringComparison.Ordinal))
            throw new InvalidDataException("EVS_PS_LOCAL_REUSE_POSTCONDITION_FAILED");
        return new("APPLIED",null,updated,changes);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if(!Directory.Exists(scripts)||Directory.Exists(output)||File.Exists(output)||
           Inside(output,game))
            throw new InvalidDataException("EVS_PS_OUTPUT_UNSAFE");
        if(basePreviewRoot is not null &&
           (!Directory.Exists(basePreviewRoot)||Inside(basePreviewRoot,game)||
            Inside(output,basePreviewRoot)||Inside(basePreviewRoot,output)))
            throw new InvalidDataException("EVS_PS_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string hash=admission.SourceStackSha256??
            throw new InvalidDataException("EVS_PS_SOURCE_HASH_MISSING");
        string? relative=null,rewritten=null,reason=null;
        string status="NOT_APPLICABLE";
        int edits=0;
        string owner=Path.Combine(scripts,"Enhanced Vehicle System");
        if(Directory.Exists(owner))
        {
            var candidates=Directory.EnumerateFiles(owner,"*.reds",
                SearchOption.AllDirectories).Where(f=>
                Regex.IsMatch(File.ReadAllText(f,Encoding.UTF8),
                    @"@replaceMethod\s*\(\s*VehicleComponent\s*\)[\s\S]{0,180}\bfunc\s+OnAction\s*\(")).ToArray();
            if(candidates.Length!=1)
            {
                status="REFUSED";
                reason=candidates.Length==0?
                    "EVS_ONACTION_TARGET_MISSING":"EVS_ONACTION_TARGET_AMBIGUOUS";
            }
            else
            {
                string path=candidates[0];
                if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)
                    throw new InvalidDataException("EVS_PS_SOURCE_LINK_UNSAFE");
                relative="r6/scripts/"+Path.GetRelativePath(scripts,path)
                    .Replace('\\','/');
                string source=File.ReadAllText(path,Encoding.UTF8);
                if(basePreviewRoot is not null)
                {
                    string staged=Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/',Path.DirectorySeparatorChar)));
                    if(!Inside(staged,basePreviewRoot))
                        throw new InvalidDataException("EVS_PS_PREVIEW_ESCAPE");
                    if(File.Exists(staged))source=File.ReadAllText(staged,Encoding.UTF8);
                }
                var decision=Rewrite(source);
                status=decision.Status;reason=decision.Reason;
                rewritten=decision.Changed;edits=decision.ReplacedCalls;
            }
        }
        if(!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("EVS_PS_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        if(rewritten is not null && relative is not null)
        {
            string dst=Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(dst,output)||!dst.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("EVS_PS_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst,rewritten,new UTF8Encoding(false));
        }
        var result=new Result(Rule,status,edits,status=="COMPLETE"?1:0,
            relative,reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return result;
    }
    private static bool Inside(string child,string root)=>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
