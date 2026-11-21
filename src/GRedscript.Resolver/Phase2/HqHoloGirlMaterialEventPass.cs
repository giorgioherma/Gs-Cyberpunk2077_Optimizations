using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// HQ_HoloGirl historical Pass 4/5 material event fast-path.
/// Filter the type-wide Resource/Loaded event before touching holo state,
/// cache holo_base.mi once per OnLoad, and hoist color/emissive calculations
/// out of the parameter loop. Preserve all other mod logic and callbacks.
/// </summary>
internal static class HqHoloGirlMaterialEventPass
{
    internal const string Rule = "HQ_HOLOGIRL_MATERIAL_EVENT_FASTPATH_V1";
    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);
    private sealed record Method(string Name, int Open, int End);
    private sealed record Edit(int Start, int End, string Text);
    private static readonly Regex BaseField = new(
        @"\b(?:private|protected|public)\s+let\s+holoBaseRR\s*:\s*(?<type>\w+)\s*;");
    private static readonly Regex BaseInit = new(
        @"\bthis\s*\.\s*holoBaseRR\s*=\s*HQRes\s*\(\s*""holo_base\.mi""\s*\)\s*;");
    private static readonly Regex OriginalGuard = new(
        @"\bif\s+!\s*this\s*\.\s*IsValid\s*\(\s*e\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex MatLocal = new(
        @"\blet\s+mtl\s*=\s*e\s*\.\s*GetResource\s*\(\s*\)\s+as\s+CMaterialInstance\s*;");
    private static readonly Regex OriginalOuter = new(
        @"\bif\s+ResourceRef\s*\.\s*GetPath\s*\(\s*mtl\s*\.\s*baseMaterial\s*\)\s*==\s*HQRes\s*\(\s*""holo_base\.mi""\s*\)\s*\{");
    private static readonly Regex FirstEvent = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*this\s*\.\s*evtHQHolo\s*\)\s*\{\s*this\s*\.\s*evtHQHolo\s*=\s*e\s*;\s*\}");
    private static readonly Regex EarlyEvent = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*e\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex EarlyMaterial = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*mtl\s*\)\s*\|\|\s*ResourceRef\s*\.\s*GetPath\s*\(\s*mtl\s*\.\s*baseMaterial\s*\)\s*!=\s*this\s*\.\s*holoBaseRR\s*\{\s*return\s*;\s*\}");
    private static readonly Regex EarlyHolo = new(
        @"\bif\s+!\s*IsDefined\s*\(\s*this\s*\.\s*holo\s*\)\s*\{\s*return\s*;\s*\}");
    private static readonly Regex ColorCalc = new(
        @"\bthis\s*\.\s*ResolveColor\s*\(\s*this\s*\.\s*holo\s*\.\s*txColor\s*\)");
    private static readonly Regex EmissiveCalc = new(
        @"\bthis\s*\.\s*GetEmissive\s*\(\s*\)");
    private static readonly Regex LocalColor = new(
        @"\blet\s+color\s*=\s*this\s*\.\s*ResolveColor\s*\(\s*this\s*\.\s*holo\s*\.\s*txColor\s*\)\s*;");
    private static readonly Regex LocalEmissive = new(
        @"\blet\s+emissive\s*=\s*this\s*\.\s*GetEmissive\s*\(\s*\)\s*;");

    private static int Closing(IReadOnlyList<SourceAdmission.Token> t,
        int begin, string open = "{", string close = "}")
    {
        if (begin < 0 || begin >= t.Count || t[begin].Value != open)
            return -1;
        int depth = 0;
        for (int i = begin; i < t.Count; i++)
        {
            if (t[i].Value == open) depth++;
            else if (t[i].Value == close && --depth == 0) return i;
        }
        return -1;
    }

    private static IReadOnlyList<Method> LocateMethods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var result = new List<Method>();
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" ||
                t[i + 1].Value is not ("OnLoad" or "UpdateHQHolo") ||
                t[i + 2].Value != "(") continue;
            int args = Closing(t, i + 2, "(", ")");
            if (args < 0) continue;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{") continue;
            int end = Closing(t, open);
            if (end < 0) continue;
            result.Add(new(t[i + 1].Value, t[open].Start, t[end].End));
            i = end;
        }
        return result;
    }

    private static (string Status, string? Reason, string? Changed) Rewrite(
        string original)
    {
        var methods = LocateMethods(original);
        if (methods.Count(x => x.Name == "OnLoad") != 1 ||
            methods.Count(x => x.Name == "UpdateHQHolo") != 1 ||
            !Regex.IsMatch(original,
                @"\bclass\s+HoloGirl\s+extends\s+ScriptableService\s*\{"))
            return ("REFUSED", "HQ_HOLOGIRL_METHOD_OWNERSHIP_UNPROVEN", null);
        var onLoad = methods.Single(x => x.Name == "OnLoad");
        var onEvent = methods.Single(x => x.Name == "UpdateHQHolo");
        string load = original[(onLoad.Open + 1)..(onLoad.End - 1)];
        string callback = original[(onEvent.Open + 1)..(onEvent.End - 1)];
        if (!Regex.IsMatch(load,
                @"\bthis\s*\.\s*holo_mi\s*\*=\s*HQRes\s*\(\s*""holo\.mi""\s*\)\s*;") ||
            !Regex.IsMatch(load,
                @"\bBindCallbackTRL\s*\(\s*this\s*,\s*n\s*""UpdateHQHolo""\s*,\s*n\s*""CMaterialInstance""\s*\)") ||
            FirstEvent.Matches(callback).Count != 1 ||
            MatLocal.Matches(callback).Count != 1)
            return ("REFUSED", "HQ_HOLOGIRL_CALLBACK_LIFECYCLE_UNPROVEN", null);

        var field = BaseField.Matches(original);
        if (field.Count > 1 ||
            (field.Count == 1 && field[0].Groups["type"].Value != "ResRef") ||
            (field.Count == 0 &&
             Regex.IsMatch(original, @"\blet\s+holoBaseRR\b")))
            return ("REFUSED", "HQ_HOLOGIRL_CACHE_FIELD_INCOMPATIBLE", null);
        var init = BaseInit.Matches(load);
        if (init.Count > 1 || (init.Count == 0 &&
            Regex.IsMatch(load, @"\bthis\s*\.\s*holoBaseRR\s*=")))
            return ("REFUSED", "HQ_HOLOGIRL_CACHE_INIT_INCOMPATIBLE", null);

        bool oldGuard = OriginalGuard.IsMatch(callback);
        bool newGuard = EarlyEvent.IsMatch(callback) &&
            EarlyMaterial.IsMatch(callback) && EarlyHolo.IsMatch(callback);
        var oldOuter = OriginalOuter.Matches(callback);
        if ((oldGuard && newGuard) || (!oldGuard && !newGuard) ||
            oldOuter.Count > 1 ||
            (newGuard && oldOuter.Count > 0))
            return ("REFUSED", "HQ_HOLOGIRL_CALLBACK_PARTIAL_FLOW_UNPROVEN", null);
        if (oldGuard && oldOuter.Count != 1)
            return ("REFUSED", "HQ_HOLOGIRL_OLD_FILTER_MISSING", null);
        int eventAt = FirstEvent.Match(callback).Index;
        int materialAt = MatLocal.Match(callback).Index;
        if (oldGuard &&
            !(OriginalGuard.Match(callback).Index < eventAt &&
              eventAt < materialAt && materialAt < oldOuter[0].Index))
            return ("REFUSED", "HQ_HOLOGIRL_EVENT_ORDER_CHANGED", null);
        if (newGuard &&
            !(EarlyEvent.Match(callback).Index < materialAt &&
              materialAt < EarlyMaterial.Match(callback).Index &&
              EarlyMaterial.Match(callback).Index < EarlyHolo.Match(callback).Index &&
              EarlyHolo.Match(callback).Index < eventAt))
            return ("REFUSED", "HQ_HOLOGIRL_NEW_EVENT_ORDER_UNPROVEN", null);

        string nl = original.Contains("\r\n", StringComparison.Ordinal) ?
            "\r\n" : "\n";
        var edits = new List<Edit>();
        if (field.Count == 0)
        {
            var anchor = Regex.Match(original,
                @"\bprivate\s+let\s+holo_mi\s*:\s*ResourceRef\s*;");
            if (!anchor.Success)
                return ("REFUSED", "HQ_HOLOGIRL_FIELD_ANCHOR_MISSING", null);
            edits.Add(new(anchor.Index + anchor.Length,
                anchor.Index + anchor.Length,
                nl + "    private let holoBaseRR: ResRef;"));
        }
        if (init.Count == 0)
        {
            var anchor = Regex.Match(load,
                @"\bthis\s*\.\s*holo_mi\s*\*=\s*HQRes\s*\(\s*""holo\.mi""\s*\)\s*;");
            edits.Add(new(onLoad.Open + 1 + anchor.Index + anchor.Length,
                onLoad.Open + 1 + anchor.Index + anchor.Length,
                nl + "        this.holoBaseRR = HQRes(\"holo_base.mi\");"));
        }

        string rebuilt = callback;
        if (oldGuard)
        {
            // The old outer condition contained only the material parameter
            // updates. Preserve that exact source, including comments and
            // custom statements inside the accepted branch.
            var m = oldOuter[0];
            var tokens = SourceAdmission.ReadTokens(rebuilt);
            int open = -1;
            for (int i = 0; i < tokens.Count; i++)
                if (tokens[i].Start == m.Index + m.Length - 1 &&
                    tokens[i].Value == "{") { open = i; break; }
            int end = Closing(tokens, open);
            if (end < 0)
                return ("REFUSED", "HQ_HOLOGIRL_MATERIAL_BRANCH_UNBALANCED", null);
            string inside = rebuilt[tokens[open].End..tokens[end].Start];
            if (!Regex.IsMatch(inside, @"\bwhile\s+i\s*<\s*ArraySize\s*\(\s*mtl\s*\.\s*params\s*\)") ||
                !Regex.IsMatch(inside, @"\bmtl\s*\.\s*params\s*\[\s*i\s*\]\s*\.\s*data\s*="))
                return ("REFUSED", "HQ_HOLOGIRL_MATERIAL_UPDATES_UNPROVEN", null);
            // Cut only the condition and its braces. Keep all interior
            // statements and the function's unrelated before/after code.
            rebuilt = rebuilt[..m.Index] + inside +
                rebuilt[tokens[end].End..];
            var first = OriginalGuard.Match(rebuilt);
            string early = "if !IsDefined(e) {" + nl +
                "            return;" + nl + "        }" + nl +
                "        let mtl = e.GetResource() as CMaterialInstance;" + nl +
                "        if !IsDefined(mtl) || ResourceRef.GetPath(mtl.baseMaterial) != this.holoBaseRR {" + nl +
                "            return;" + nl + "        }" + nl +
                "        if !IsDefined(this.holo) {" + nl +
                "            return;" + nl + "        }";
            rebuilt = rebuilt[..first.Index] + early +
                rebuilt[(first.Index + first.Length)..];
            // The old mtl declaration is after the event cache; remove it.
            // Resolve the declaration after inserting the new guard to
            // ensure the new early declaration cannot be mistakenly removed.
            var decls = MatLocal.Matches(rebuilt);
            if (decls.Count != 2)
                throw new InvalidDataException("HQ_HOLOGIRL_MATERIAL_DECL_COUNT");
            rebuilt = rebuilt[..decls[1].Index] +
                rebuilt[(decls[1].Index + decls[1].Length)..];
            // The event cache must follow the early filter.
            var cache = FirstEvent.Match(rebuilt);
            var mtlGuard = EarlyHolo.Match(rebuilt);
            if (!cache.Success || !mtlGuard.Success ||
                cache.Index <= mtlGuard.Index)
                throw new InvalidDataException("HQ_HOLOGIRL_FILTER_POSTCONDITION");
        }

        // Historical Pass 5 further avoids recalculating the same two
        // values for every material parameter. Preserve other call sites.
        var colorDecl = LocalColor.Matches(rebuilt);
        var emissiveDecl = LocalEmissive.Matches(rebuilt);
        if (colorDecl.Count > 1 || emissiveDecl.Count > 1 ||
            (colorDecl.Count == 0 &&
             Regex.IsMatch(rebuilt, @"\blet\s+color\b")) ||
            (emissiveDecl.Count == 0 &&
             Regex.IsMatch(rebuilt, @"\blet\s+emissive\b")))
            return ("REFUSED", "HQ_HOLOGIRL_HOIST_LOCAL_CONFLICT", null);
        string current = rebuilt;
        var loopAnchor = Regex.Match(current,
            @"\blet\s+i\s*=\s*0\s*;\s*while\s+i\s*<\s*ArraySize\s*\(\s*mtl\s*\.\s*params\s*\)");
        if (!loopAnchor.Success)
            return ("REFUSED", "HQ_HOLOGIRL_PARAMETER_LOOP_UNPROVEN", null);
        string preLoop = current[..loopAnchor.Index];
        string afterLoop = current[loopAnchor.Index..];
        if (!LocalColor.IsMatch(preLoop))
        {
            if (ColorCalc.Matches(afterLoop).Count != 1)
                return ("REFUSED", "HQ_HOLOGIRL_COLOR_HOIST_UNPROVEN", null);
            afterLoop = ColorCalc.Replace(afterLoop, "color", 1);
            preLoop += "let color = this.ResolveColor(this.holo.txColor);" + nl +
                "        ";
        }
        if (!LocalEmissive.IsMatch(preLoop))
        {
            if (EmissiveCalc.Matches(afterLoop).Count != 1)
                return ("REFUSED", "HQ_HOLOGIRL_EMISSIVE_HOIST_UNPROVEN", null);
            afterLoop = EmissiveCalc.Replace(afterLoop, "emissive", 1);
            preLoop += "let emissive = this.GetEmissive();" + nl +
                "        ";
        }
        rebuilt = preLoop + afterLoop;
        if (!EarlyEvent.IsMatch(rebuilt) ||
            !EarlyMaterial.IsMatch(rebuilt) ||
            !EarlyHolo.IsMatch(rebuilt) ||
            !LocalColor.IsMatch(rebuilt) ||
            !LocalEmissive.IsMatch(rebuilt))
            throw new InvalidDataException("HQ_HOLOGIRL_CALLBACK_POSTCONDITION");

        if (!string.Equals(callback, rebuilt, StringComparison.Ordinal))
            edits.Add(new(onEvent.Open + 1, onEvent.End - 1, rebuilt));
        if (edits.Count == 0)
            return ("COMPLETE", null, null);
        string result = original;
        foreach (var edit in edits.OrderByDescending(x => x.Start))
            result = result[..edit.Start] + edit.Text + result[edit.End..];
        if (!BaseField.IsMatch(result) ||
            !BaseInit.IsMatch(result) ||
            !EarlyMaterial.IsMatch(result))
            throw new InvalidDataException("HQ_HOLOGIRL_STAGE_POSTCONDITION");
        return ("APPLIED", null, result);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("HQ_HOLOGIRL_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) || Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("HQ_HOLOGIRL_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("HQ_HOLOGIRL_SOURCE_HASH_MISSING");
        string owner = Path.Combine(scripts, "HQ_HoloGirl");
        string status = "NOT_APPLICABLE";
        string? rel = null, reason = null, changed = null;
        if (Directory.Exists(owner))
        {
            var files = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories)
                .Where(f => Regex.IsMatch(File.ReadAllText(f,Encoding.UTF8),
                    @"\bclass\s+HoloGirl\s+extends\s+ScriptableService\b")).ToArray();
            if (files.Length != 1)
            {
                status = "REFUSED";
                reason = "HQ_HOLOGIRL_OWNER_MISSING_OR_AMBIGUOUS";
            }
            else
            {
                string file = files[0];
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("HQ_HOLOGIRL_LINK_REFUSED");
                rel = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\','/');
                string current = File.ReadAllText(file, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string earlier = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        rel.Replace('/',Path.DirectorySeparatorChar)));
                    if (!Inside(earlier,basePreviewRoot))
                        throw new InvalidDataException("HQ_HOLOGIRL_OVERLAY_ESCAPE");
                    if (File.Exists(earlier))
                        current = File.ReadAllText(earlier,Encoding.UTF8);
                }
                var state = Rewrite(current);
                status = state.Status; reason = state.Reason;
                changed = state.Changed;
            }
        }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("HQ_HOLOGIRL_SOURCE_CHANGED");
        Directory.CreateDirectory(output);
        if (changed is not null && rel is not null)
        {
            string file = Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(file,output) || !file.EndsWith(".reds",
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("HQ_HOLOGIRL_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, changed, new UTF8Encoding(false));
        }
        var report = new Result(Rule,status,
            status == "APPLIED" ? 1 : 0,
            status == "COMPLETE" ? 1 : 0,
            rel,reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }),new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
