using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// RedWindows historical Pass 9 background-producer dormancy.
/// IsVisible retains logical open state; IsRendered mirrors actual
/// RefreshVisibility conditions. Builder and Logs polling are allowed to
/// sleep while overlay-only windows cannot render. When m_mgr is absent,
/// honor overlayOnly (historical reference erroneously returned true).
/// </summary>
internal static class RedWindowsRenderDormancyPass
{
    internal const string Rule = "REDWINDOWS_RENDER_AWARE_DORMANCY_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(int Start, int Open, int End, string Name);
    private sealed record Edit(int Start, int End, string Text);

    private const string Canonical = """
        public func IsRendered() -> Bool {
          if !this.visible { return false; }
          if !IsDefined(this.m_mgr) { return !this.overlayOnly; }
          let overlay: Bool = this.m_mgr.IsOverlay();
          let allowed: Bool = !overlay || this.m_mgr.SoloAllows(this);
          return (!this.overlayOnly || overlay) && allowed;
        }
        """;
    private const string Historical = """
        public func IsRendered() -> Bool {
          if !this.visible { return false; }
          if !IsDefined(this.m_mgr) { return true; }
          let overlay: Bool = this.m_mgr.IsOverlay();
          let allowed: Bool = !overlay || this.m_mgr.SoloAllows(this);
          return (!this.overlayOnly || overlay) && allowed;
        }
        """;
    private static string Shape(string s) =>
        string.Join("|", SourceAdmission.ReadTokens(s).Select(t => t.Value));

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int first, string open = "{", string close = "}")
    {
        if (first < 0 || first >= t.Count || t[first].Value != open)
            return -1;
        int depth = 0;
        for (int i = first; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }
    private static IReadOnlyList<Method> Methods(string source, string name)
    {
        var t = SourceAdmission.ReadTokens(source);
        var matches = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != name ||
                t[i + 2].Value != "(") continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Close(t, open);
            if (end < 0) continue;
            matches.Add(new(t[i].Start, t[open].Start, t[end].End,name));
            i = end;
        }
        return matches;
    }

    private static (string Status, string? Reason, string? Changed)
        Window(string source)
    {
        if (!Regex.IsMatch(source, @"\bclass\s+RWWindow\s*\{") ||
            Methods(source, "RefreshVisibility").Count != 1)
            return ("REFUSED", "RW_RENDER_WINDOW_OWNER_UNPROVEN", null);
        var refresh = Methods(source,"RefreshVisibility")[0];
        string refreshBody = source[(refresh.Open + 1)..(refresh.End - 1)];
        bool baseLogic = Regex.IsMatch(refreshBody,
            @"\blet\s+overlay\s*:\s*Bool\s*=\s*IsDefined\s*\(\s*this\s*\.\s*m_mgr\s*\)\s*&&\s*this\s*\.\s*m_mgr\s*\.\s*IsOverlay\s*\(\s*\)\s*;") &&
            Regex.IsMatch(refreshBody,
            @"\blet\s+allowed\s*:\s*Bool\s*=\s*!\s*overlay\s*\|\|\s*this\s*\.\s*m_mgr\s*\.\s*SoloAllows\s*\(\s*this\s*\)\s*;") &&
            Regex.IsMatch(refreshBody,
            @"this\s*\.\s*m_canvas\s*\.\s*SetVisible\s*\(\s*this\s*\.\s*visible\s*&&\s*\(\s*!\s*this\s*\.\s*overlayOnly\s*\|\|\s*overlay\s*\)\s*&&\s*allowed\s*\)");
        bool delegated = Regex.IsMatch(refreshBody,
            @"this\s*\.\s*m_canvas\s*\.\s*SetVisible\s*\(\s*this\s*\.\s*IsRendered\s*\(\s*\)\s*\)");
        if (!baseLogic && !delegated)
            return ("REFUSED", "RW_RENDER_EFFECTIVE_VISIBILITY_UNPROVEN", null);

        var visible = Methods(source,"IsVisible");
        if (visible.Count != 1 ||
            !Regex.IsMatch(source[visible[0].Start..visible[0].End],
                @"\breturn\s+this\s*\.\s*visible\s*;"))
            return ("REFUSED", "RW_RENDER_LOGICAL_VISIBILITY_UNPROVEN", null);
        var rendered = Methods(source,"IsRendered");
        if (rendered.Count > 1)
            return ("REFUSED", "RW_RENDER_DUPLICATE_HELPER", null);
        if (rendered.Count == 1)
        {
            var method=rendered[0];
            string own = source[method.Start..method.End];
            if (Shape(own) == Shape(Canonical[Canonical.IndexOf("func IsRendered", StringComparison.Ordinal)..]))
                return ("COMPLETE", null, null);
            if (Shape(own) != Shape(Historical[Historical.IndexOf("func IsRendered", StringComparison.Ordinal)..]))
                return ("REFUSED", "RW_RENDER_EXISTING_HELPER_INCOMPATIBLE", null);
            // Fix the historical no-manager overlay-only visibility edge,
            // without modifying the current UI lifecycle.
            var legacy = Regex.Match(own,
                @"\bif\s+!\s*IsDefined\s*\(\s*this\s*\.\s*m_mgr\s*\)\s*\{\s*return\s+true\s*;");
            if (!legacy.Success)
                return ("REFUSED", "RW_RENDER_MANAGER_FALLBACK_UNPROVEN", null);
            int at = own.IndexOf("return true;", legacy.Index,
                StringComparison.Ordinal);
            if (at < 0 || at >= legacy.Index + legacy.Length)
                return ("REFUSED", "RW_RENDER_MANAGER_FALLBACK_AMBIGUOUS", null);
            return ("APPLIED", null,
                source[..(method.Start + at)] +
                "return !this.overlayOnly;" +
                source[(method.Start + at + "return true;".Length)..]);
        }
        string nl = source.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        string helper = nl + nl + "  " +
            Canonical.Replace("\n", nl + "  ").TrimEnd();
        return ("APPLIED", null,
            source[..visible[0].End] + helper +
            source[visible[0].End..]);
    }

    private static (string Status, string? Reason, string? Changed)
        Builder(string source)
    {
        if (!Regex.IsMatch(source,
                @"\bclass\s+RWBuilderApp\s+extends\s+ScriptableSystem\s*\{"))
            return ("REFUSED", "RW_RENDER_BUILDER_OWNER_UNPROVEN",null);
        var methods = Methods(source,"OnTick");
        if (methods.Count != 1)
            return ("REFUSED","RW_RENDER_BUILDER_TICK_UNPROVEN",null);
        var method = methods[0];
        string body = source[(method.Open + 1)..(method.End - 1)];
        if (!body.Contains("this.UpdateBuilder(builder)",StringComparison.Ordinal) ||
            !body.Contains("this.UpdateGallery(gallery)",StringComparison.Ordinal) ||
            !body.Contains("this.ScheduleTick(0.1)",StringComparison.Ordinal) ||
            !body.Contains("this.ScheduleTick(1.0)",StringComparison.Ordinal))
            return ("REFUSED","RW_RENDER_BUILDER_CADENCE_UNPROVEN",null);
        var edits = new List<Edit>();
        foreach (string name in new[] { "builder", "gallery" })
        {
            string cap = name == "builder" ? "builderUp" : "galleryUp";
            var predicate = Regex.Matches(body,
                @"\blet\s+" + cap + @"\s*:\s*Bool\s*=\s*IsDefined\s*" +
                @"\(\s*" + name + @"\s*\)\s*&&\s*" + name +
                @"\s*\.\s*(?<method>IsVisible|IsRendered)\s*\(\s*\)\s*;");
            if (predicate.Count != 1)
                return ("REFUSED","RW_RENDER_BUILDER_GUARD_UNPROVEN:"+cap,null);
            string existing = predicate[0].Groups["method"].Value;
            if (existing == "IsVisible")
            {
                var at=predicate[0].Groups["method"];
                edits.Add(new(method.Open + 1 + at.Index,
                    method.Open + 1 + at.Index + at.Length,
                    "IsRendered"));
            }
        }
        if (edits.Count == 0) return ("COMPLETE",null,null);
        string updated=source;
        foreach (var e in edits.OrderByDescending(e=>e.Start))
            updated=updated[..e.Start]+e.Text+updated[e.End..];
        return ("APPLIED",null,updated);
    }

    private static (string Status, string? Reason, string? Changed)
        Logs(string source)
    {
        if (!Regex.IsMatch(source,
                @"\bclass\s+RWLogsApp\s+extends\s+ScriptableSystem\s*\{"))
            return ("REFUSED","RW_RENDER_LOGS_OWNER_UNPROVEN",null);
        var methods=Methods(source,"OnTick");
        if (methods.Count != 1)
            return ("REFUSED","RW_RENDER_LOGS_TICK_UNPROVEN",null);
        var method=methods[0];
        string body=source[(method.Open+1)..(method.End-1)];
        if (!body.Contains("RedLogger.Available()",StringComparison.Ordinal) ||
            !body.Contains("this.Schedule(delay)",StringComparison.Ordinal) ||
            !body.Contains("this.PollLive()",StringComparison.Ordinal))
            return ("REFUSED","RW_RENDER_LOGS_CADENCE_UNPROVEN",null);
        var guard=Regex.Matches(body,
            @"\bif\s+win\s*\.\s*(?<method>IsVisible|IsRendered)\s*\(\s*\)\s*&&\s*RedLogger\s*\.\s*Available\s*\(\s*\)\s*\{");
        if (guard.Count != 1)
            return ("REFUSED","RW_RENDER_LOGS_GUARD_UNPROVEN",null);
        var m=guard[0].Groups["method"];
        if (m.Value=="IsRendered")return ("COMPLETE",null,null);
        int at=method.Open+1+m.Index;
        return ("APPLIED",null,source[..at]+"IsRendered"+source[(at+m.Length)..]);
    }

    internal static Result Generate(string gameRoot,string outputRoot,
        string? basePreviewRoot=null)
    {
        string game=Path.GetFullPath(gameRoot);
        string output=Path.GetFullPath(outputRoot);
        string scripts=Path.Combine(game,"r6","scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("RW_RENDER_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot,game) || Inside(output,basePreviewRoot) ||
            Inside(basePreviewRoot,output)))
            throw new InvalidDataException("RW_RENDER_OVERLAY_UNSAFE");
        var admission=new SourceAdmission(game);
        _=admission.AuditAll();
        string hash=admission.SourceStackSha256 ??
            throw new InvalidDataException("RW_RENDER_SOURCE_HASH_MISSING");
        string owner=Path.Combine(scripts,"RedWindows");
        var changed=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var refusals=new List<string>();
        string status="NOT_APPLICABLE";
        if (Directory.Exists(owner))
        {
            var definitions=new[]
            {
                (Kind:"Window", Class:"RWWindow",
                    Pattern:@"\bclass\s+RWWindow\s*\{"),
                (Kind:"Builder", Class:"RWBuilderApp",
                    Pattern:@"\bclass\s+RWBuilderApp\s+extends\s+ScriptableSystem\s*\{"),
                (Kind:"Logs", Class:"RWLogsApp",
                    Pattern:@"\bclass\s+RWLogsApp\s+extends\s+ScriptableSystem\s*\{")
            };
            var candidates=new Dictionary<string,string>(
                StringComparer.Ordinal);
            var sourcePaths=Directory.EnumerateFiles(owner,"*.reds",
                SearchOption.AllDirectories).ToArray();
            foreach (var item in definitions)
            {
                string[] matching=sourcePaths.Where(f=>Regex.IsMatch(
                    File.ReadAllText(f,Encoding.UTF8),item.Pattern)).ToArray();
                if (matching.Length!=1)
                    refusals.Add("RW_RENDER_OWNER_NOT_UNIQUE:"+item.Kind);
                else candidates.Add(item.Kind,matching[0]);
            }
            if (candidates.TryGetValue("Window",out var windowFile))
            {
                string Effective(string full)
                {
                    string rel="r6/scripts/"+Path.GetRelativePath(scripts,full)
                        .Replace('\\','/');
                    string current=File.ReadAllText(full,Encoding.UTF8);
                    if (basePreviewRoot is not null)
                    {
                        string previous=Path.GetFullPath(Path.Combine(
                            basePreviewRoot,
                            rel.Replace('/',Path.DirectorySeparatorChar)));
                        if (!Inside(previous,basePreviewRoot))
                            throw new InvalidDataException(
                                "RW_RENDER_PREVIEW_PATH_ESCAPE");
                        if (File.Exists(previous))
                            current=File.ReadAllText(previous,Encoding.UTF8);
                    }
                    return current;
                }
                string windowSource=Effective(windowFile);
                var windowState=Window(windowSource);
                if (windowState.Status=="REFUSED")
                    refusals.Add(windowState.Reason ?? "RW_RENDER_WINDOW_UNPROVEN");
                else
                {
                    var accepted=new List<(string Full,string Content)>();
                    if (windowState.Changed is not null)
                        accepted.Add((windowFile,windowState.Changed));
                    foreach (string consumer in new[] { "Builder","Logs" })
                    {
                        if (!candidates.TryGetValue(consumer,out string? file))
                            continue;
                        string current=Effective(file);
                        var result=consumer=="Builder" ?
                            Builder(current): Logs(current);
                        if (result.Status=="REFUSED")
                            refusals.Add(result.Reason ??
                                "RW_RENDER_CONSUMER_UNPROVEN:"+consumer);
                        else if (result.Changed is not null)
                            accepted.Add((file,result.Changed));
                    }
                    // Do not insert a helper with no provable caller.
                    if (accepted.Count>0 && candidates.Count>=2)
                    {
                        foreach (var item in accepted)
                        {
                            string rel="r6/scripts/"+
                                Path.GetRelativePath(scripts,item.Full)
                                    .Replace('\\','/');
                            if (!rel.StartsWith("r6/scripts/RedWindows/",
                                    StringComparison.Ordinal) ||
                                changed.ContainsKey(rel))
                                throw new InvalidDataException(
                                    "RW_RENDER_DUPLICATE_OWNER_OUTPUT");
                            changed.Add(rel,item.Content);
                        }
                    }
                    status=changed.Count>0?"APPLIED":
                        refusals.Count>0?"REFUSED":"COMPLETE";
                }
            }
            else status="REFUSED";
        }
        if (!string.Equals(hash,admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("RW_RENDER_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        foreach (var pair in changed)
        {
            string path=Path.GetFullPath(Path.Combine(output,
                pair.Key.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(path,output) ||
                !path.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("RW_RENDER_OUTPUT_PATH_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path,pair.Value,new UTF8Encoding(false));
        }
        var report=new Result(Rule,status,
            status=="APPLIED"?1:0,status=="COMPLETE"?1:0,
            changed.Keys.Order(StringComparer.Ordinal).ToArray(),
            refusals.Count>0?string.Join(";",refusals):null,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions{
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return report;
    }
    private static bool Inside(string path,string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
