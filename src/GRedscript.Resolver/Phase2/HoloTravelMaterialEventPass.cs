using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 5 HoloTravel material-event fast path.
/// Standalone owner implementation for integration by the semantic stage owner.
/// It does not register a stage or modify the central plan.
/// </summary>
internal static class HoloTravelMaterialEventPass
{
    internal const string Rule = "HQ_HOLOTRAVEL_MATERIAL_EVENT_FASTPATH_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Span(int Open, int End);
    private static readonly Regex OldGuard = new(
        @"\bif\s*!\s*this\s*\.\s*IsValid\s*\(\s*e\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex EarlyEvent = new(
        @"\bif\s*!\s*IsDefined\s*\(\s*e\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex Material = new(
        @"\blet\s+mtl\s*=\s*e\s*\.\s*GetResource\s*\(\s*\)\s+as\s+CMaterialInstance\s*;");
    private static readonly Regex ResPath = new(
        @"\blet\s+resPath\s*=\s*ResourceRef\s*\.\s*GetPath\s*\(\s*mtl\s*\.\s*baseMaterial\s*\)\s*;");
    private static readonly Regex MaterialGuard = new(
        @"\bif\s*!\s*IsDefined\s*\(\s*mtl\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex OldFilter = new(
        @"\bif\s+resPath\s*==\s*this\s*\.\s*beam_baseRR\s*\|\|\s*resPath\s*==\s*this\s*\.\s*flare_baseRR\s*\{");
    private static readonly Regex NewFilter = new(
        @"\bif\s+resPath\s*!=\s*this\s*\.\s*beam_baseRR\s*&&\s*resPath\s*!=\s*this\s*\.\s*flare_baseRR\s*\{\s*return\s*;\s*\}");
    private static readonly Regex HoloGuard = new(
        @"\bif\s*!\s*IsDefined\s*\(\s*this\s*\.\s*svc\s*\.\s*holo\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex OldColor = new(
        @"\bthis\s*\.\s*ResolveColor\s*\(\s*this\s*\.\s*svc\s*\.\s*holo\s*\.\s*particleColor\s*\)");
    private static readonly Regex ColorLocal = new(
        @"\blet\s+color\s*=\s*this\s*\.\s*ResolveColor\s*\(\s*this\s*\.\s*svc\s*\.\s*holo\s*\.\s*particleColor\s*\)\s*;");
    private static readonly Regex LoopStart = new(
        @"\blet\s+i\s*=\s*0\s*;\s*while\s+i\s*<\s*ArraySize\s*\(\s*mtl\s*\.\s*params\s*\)");

    private static int Close(IReadOnlyList<SourceAdmission.Token> t, int open,
        string left = "{", string right = "}")
    {
        if (open < 0 || open >= t.Count || t[open].Value != left) return -1;
        int depth = 0;
        for (int i = open; i < t.Count; i++)
        {
            if (t[i].Value == left) depth++;
            else if (t[i].Value == right && --depth == 0) return i;
        }
        return -1;
    }

    private static Span? FindParticleCallback(string text)
    {
        var t = SourceAdmission.ReadTokens(text);
        int classOpen = -1, classClose = -1;
        for (int i = 0; i + 4 < t.Count; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != "ParticleHandler" ||
                t[i + 2].Value != "extends" || t[i + 3].Value != "BaseHandler")
                continue;
            if (classOpen >= 0) return null;
            int p = i + 4;
            while (p < t.Count && t[p].Value is not ("{" or ";")) p++;
            if (p == t.Count || t[p].Value != "{") return null;
            classOpen = p;
            classClose = Close(t, p);
            if (classClose < 0) return null;
        }
        if (classOpen < 0) return null;
        string cls = text[t[classOpen].Start..t[classClose].End];
        if (!Regex.IsMatch(cls,
                @"\bBindCallbackTRL\s*\(\s*self\s*,\s*n""OnUpdate""\s*,\s*n""CMaterialInstance""\s*\)") ||
            !Regex.IsMatch(cls,
                @"\bself\s*\.\s*beam_baseRR\s*=\s*HQRes\s*\(\s*""particle_beam_base\.mt""\s*\)") ||
            !Regex.IsMatch(cls,
                @"\bself\s*\.\s*flare_baseRR\s*=\s*HQRes\s*\(\s*""particle_flare_base\.remt""\s*\)"))
            return null;
        Span? callback = null;
        for (int i = classOpen + 1; i + 2 < classClose; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != "OnUpdate" ||
                t[i + 2].Value != "(") continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < classClose && t[open].Value is not ("{" or ";")) open++;
            if (open >= classClose || t[open].Value != "{") return null;
            int end = Close(t, open);
            if (end < 0 || end > classClose || callback is not null) return null;
            callback = new(t[open].End, t[end].Start);
            i = end;
        }
        return callback;
    }

    // The initializer is not a hot-loop call. Count only residual usages.
    private static int HotColorCalls(string source) =>
        OldColor.Matches(ColorLocal.Replace(source, "")).Count;

    private static (string Status, string? Reason, string? Source) Rewrite(string original)
    {
        var cb = FindParticleCallback(original);
        if (cb is null) return ("REFUSED", "HOLOTRAVEL_PARTICLE_CALLBACK_OR_BINDING_UNPROVEN", null);
        // This is the source-defined, side-effect-free IsValid predicate.
        if (!Regex.IsMatch(original,
                @"\bfunc\s+IsValid\s*\(\s*e\s*:\s*ref\s*<\s*ResourceEvent\s*>\s*\)\s*->\s*Bool\s*=\s*IsDefined\s*\(\s*this\s*\.\s*svc\s*\.\s*holo\s*\)\s*&&\s*IsDefined\s*\(\s*e\s*\)\s*;") ||
            !Regex.IsMatch(original,
                @"\bfunc\s+ResolveColor\s*\(\s*color\s*:\s*Color\s*\)\s*->\s*Color\b"))
            return ("REFUSED", "HOLOTRAVEL_VALIDATION_OR_COLOR_DEPENDENCY_CHANGED", null);

        string body = original[cb.Open..cb.End];
        string nl = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        bool oldGuard = OldGuard.IsMatch(body);
        bool early = EarlyEvent.IsMatch(body);
        bool oldFilter = OldFilter.IsMatch(body);
        bool newFilter = NewFilter.IsMatch(body);
        if ((oldGuard && early) || (oldFilter && newFilter) ||
            (!oldGuard && !early) || (!oldFilter && !newFilter) ||
            Material.Matches(body).Count != 1 ||
            ResPath.Matches(body).Count != 1 ||
            OldGuard.Matches(body).Count > 1 ||
            OldFilter.Matches(body).Count > 1 ||
            LoopStart.Matches(body).Count != 1)
            return ("REFUSED", "HOLOTRAVEL_PARTIAL_EVENT_FLOW_UNPROVEN", null);

        string result = body;
        if (oldGuard)
        {
            var g = OldGuard.Match(result);
            result = result[..g.Index] +
                "if !IsDefined(e) {" + nl +
                "            return;" + nl + "        }" +
                result[(g.Index + g.Length)..];
        }
        if (!MaterialGuard.IsMatch(result))
        {
            var m = Material.Match(result);
            result = result.Insert(m.Index + m.Length,
                nl + "        if !IsDefined(mtl) {" + nl +
                "            return;" + nl + "        }");
        }
        if (oldFilter)
        {
            // Do not duplicate or remove content inside the matched material branch.
            var m = OldFilter.Match(result);
            var tokens = SourceAdmission.ReadTokens(result);
            int opening = -1;
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Value == "{" &&
                    tokens[i].Start == m.Index + m.Length - 1)
                { opening = i; break; }
            int close = Close(tokens, opening);
            if (close < 0) return ("REFUSED", "HOLOTRAVEL_MATERIAL_BRANCH_UNBALANCED", null);
            string inner = result[tokens[opening].End..tokens[close].Start];
            if (!LoopStart.IsMatch(inner) ||
                !Regex.IsMatch(inner, @"\bmtl\s*\.\s*params\s*\[\s*i\s*\]\s*\.\s*data\s*="))
                return ("REFUSED", "HOLOTRAVEL_MATERIAL_CONTENT_UNPROVEN", null);
            string prefix = "if resPath != this.beam_baseRR && resPath != this.flare_baseRR {" +
                nl + "            return;" + nl + "        }" + nl +
                "        if !IsDefined(this.svc.holo) {" + nl +
                "            return;" + nl + "        }";
            result = result[..m.Index] + prefix + inner + result[tokens[close].End..];
        }
        else if (!HoloGuard.IsMatch(result))
        {
            var m = NewFilter.Match(result);
            result = result.Insert(m.Index + m.Length,
                nl + "        if !IsDefined(this.svc.holo) {" +
                nl + "            return;" + nl + "        }");
        }

        bool hasColor = ColorLocal.IsMatch(result);
        var declaration = ColorLocal.Match(result);
        int hotCalls = HotColorCalls(result);
        if (ColorLocal.Matches(result).Count > 1 ||
            (!hasColor && Regex.IsMatch(result, @"\blet\s+color\s*=")) ||
            hotCalls > 1)
            return ("REFUSED", "HOLOTRAVEL_COLOR_LOCAL_AMBIGUOUS", null);
        if (hotCalls == 1)
        {
            if (hasColor)
            {
                // A partial source can already have the declaration but still
                // call ResolveColor inside the loop: replace only that call.
                int afterDecl = declaration.Index + declaration.Length;
                string suffix = result[afterDecl..];
                if (!OldColor.IsMatch(suffix))
                    return ("REFUSED", "HOLOTRAVEL_COLOR_USE_ORDER_CHANGED", null);
                result = result[..afterDecl] +
                    OldColor.Replace(suffix, "color", 1);
            }
            else
            {
                result = OldColor.Replace(result, "color", 1);
                var loop = LoopStart.Match(result);
                result = result.Insert(loop.Index,
                    "let color = this.ResolveColor(this.svc.holo.particleColor);" +
                    nl + "        ");
            }
        }
        else if (!hasColor)
            return ("REFUSED", "HOLOTRAVEL_COLOR_USE_INCOMPATIBLE", null);

        if (!EarlyEvent.IsMatch(result) || !MaterialGuard.IsMatch(result) ||
            !NewFilter.IsMatch(result) || !HoloGuard.IsMatch(result) ||
            !ColorLocal.IsMatch(result) || HotColorCalls(result) != 0 ||
            OldFilter.IsMatch(result) || OldGuard.IsMatch(result))
            throw new InvalidDataException("HOLOTRAVEL_EVENT_POSTCONDITION");
        if (string.Equals(body, result, StringComparison.Ordinal))
            return ("COMPLETE", null, null);
        return ("APPLIED", null,
            original[..cb.Open] + result + original[cb.End..]);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("HOLOTRAVEL_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) || Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("HOLOTRAVEL_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("HOLOTRAVEL_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts, "HQ_HoloTravel");
        string status = "NOT_APPLICABLE";
        string? rel = null, reason = null, changed = null;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f, Encoding.UTF8),
                    @"\bclass\s+ParticleHandler\s+extends\s+BaseHandler\b"))
                .ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = "HOLOTRAVEL_OWNER_MISSING_OR_AMBIGUOUS";
            }
            else
            {
                string file = files[0];
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("HOLOTRAVEL_LINK_REFUSED");
                rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\', '/');
                string current = File.ReadAllText(file, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string earlier = Path.GetFullPath(Path.Combine(basePreviewRoot,
                        rel.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(earlier, basePreviewRoot))
                        throw new InvalidDataException("HOLOTRAVEL_OVERLAY_ESCAPE");
                    if (File.Exists(earlier))
                        current = File.ReadAllText(earlier, Encoding.UTF8);
                }
                var state = Rewrite(current);
                status = state.Status;
                reason = state.Reason;
                changed = state.Source;
            }
        }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("HOLOTRAVEL_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (changed is not null && rel is not null)
        {
            string path = Path.GetFullPath(Path.Combine(output,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(path, output) ||
                !path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HOLOTRAVEL_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, changed, new UTF8Encoding(false));
        }
        var report = new Result(Rule, status,
            status == "APPLIED" ? 1 : 0,
            status == "COMPLETE" ? 1 : 0,
            rel, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
