using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

internal sealed record Pass4Rewrite(string Status,int AppliedEdits,
    int AlreadyComplete,string? Reason,string? Changed)
{
    internal static Pass4Rewrite Refuse(string why) =>
        new("REFUSED",0,0,why,null);
    internal static Pass4Rewrite Complete(int done=1) =>
        new("COMPLETE",0,done,null,null);
    internal static Pass4Rewrite Applied(string changed,int edits,int done=0) =>
        new("APPLIED",edits,done,null,changed);
}

internal static class Pass4Source
{
    internal sealed record Method(int Open,int Close,int Start,string Name);
    internal static int MatchClose(IReadOnlyList<SourceAdmission.Token> tokens,
        int open,string opening="{",string closing="}")
    {
        if(open<0||open>=tokens.Count||tokens[open].Value!=opening) return -1;
        int depth=0;
        for(int i=open;i<tokens.Count;i++) {
            if(tokens[i].Value==opening) depth++;
            if(tokens[i].Value==closing && --depth==0) return i;
        }
        return -1;
    }
    internal static Method? FindMethod(string source,string name)
    {
        var t=SourceAdmission.ReadTokens(source);
        var matches=new List<Method>();
        for(int i=0;i+2<t.Count;i++) {
            if(t[i].Value!="func"||t[i+1].Value!=name||t[i+2].Value!="(")
                continue;
            int endArgs=MatchClose(t,i+2,"(",")");
            if(endArgs<0) return null;
            int open=endArgs+1;
            while(open<t.Count&&t[open].Value is not ("{" or ";")) open++;
            if(open>=t.Count||t[open].Value!="{") return null;
            int close=MatchClose(t,open);
            if(close<0) return null;
            matches.Add(new(t[open].Start,t[close].Start,t[i].Start,name));
            i=close;
        }
        return matches.Count==1?matches[0]:null;
    }
    internal static string Body(string source,Method m) =>
        source[(m.Open+1)..m.Close];
    internal static string ReplaceBody(string source,Method m,string replacement) =>
        source[..(m.Open+1)]+replacement+source[m.Close..];

    internal static bool HasDirective(string source,Method m,string target)
    {
        var before=source[Math.Max(0,m.Start-150)..m.Start];
        return before.Contains(target,StringComparison.Ordinal);
    }
    internal static bool ContainsOne(string s,string literal) =>
        s.Split(literal,StringSplitOptions.None).Length==2;

    internal static string NewLine(string s) =>
        s.Contains("\r\n",StringComparison.Ordinal)?"\r\n":"\n";

    internal static bool IsValidCurrent(string source) {
        try { _=SourceAdmission.ReadTokens(source); return true; }
        catch { return false; }
    }
    internal static Match? UniqueMatch(string source,string pattern,
        RegexOptions options=RegexOptions.None)
    {
        var matches=Regex.Matches(source,pattern,options);
        return matches.Count==1?matches[0]:null;
    }
}
