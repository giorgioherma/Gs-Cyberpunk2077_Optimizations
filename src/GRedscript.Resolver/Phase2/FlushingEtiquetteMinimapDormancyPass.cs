using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical FE pass 9 remainder: eliminate an FE-owned wrapper from the
/// global per-minimap-mappin Update path. The author's existing targeted
/// MinimapPOIMappinController.UpdateIcon hook must already own washroom icon
/// and tint styling. Other mappin callbacks and the original engine method
/// remain untouched; no replacement, listener, timer or runtime is created.
/// </summary>
internal static class FlushingEtiquetteMinimapDormancyPass
{
    internal const string Rule = "FLUSHING_ETIQUETTE_MINIMAP_UPDATE_DORMANCY_V1";

    internal sealed record Result(string RuleId, string Status,
        int AppliedEdits, int AlreadyComplete, string? SourcePath,
        string? Reason, bool SourceUnchanged, bool GameplayValidated);

    private sealed record Hook(int Start, int End, int BodyOpen, int BodyClose);
    private sealed record Candidate(string Path, string Text, Hook Hook);

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t,
        int i, string value) =>
        i >= 0 && i < t.Count && t[i].Value == value;

    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t,
        int index, params string[] expected) =>
        expected.Select((s, i) => Is(t, index + i, s)).All(v => v);

    private static int Match(IReadOnlyList<SourceAdmission.Token> t,
        int index, string opening, string closing)
    {
        if (!Is(t, index, opening)) return -1;
        int depth = 0;
        for (int i = index; i < t.Count; i++)
        {
            if (Is(t, i, opening)) depth++;
            else if (Is(t, i, closing) && --depth == 0) return i;
        }
        return -1;
    }

    private static bool OwnsModule(IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i < t.Count - 1; i++)
            if (Seq(t, i, "module", "FlushingEtiquette"))
                return true;
        return false;
    }

    private static List<Hook> Hooks(IReadOnlyList<SourceAdmission.Token> t,
        string type, string name)
    {
        var found = new List<Hook>();
        for (int i = 0; i + 5 < t.Count; i++)
        {
            if (!Seq(t, i, "@", "wrapMethod", "(", type, ")"))
                continue;
            int method = i + 5;
            while (method < t.Count && !Is(t, method, "func") &&
                   !Is(t, method, "@") && !Is(t, method, "{"))
                method++;
            if (!Seq(t, method, "func", name, "("))
                continue;
            int argsEnd = Match(t, method + 2, "(", ")");
            if (argsEnd < 0) continue;
            int open = argsEnd + 1;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";") && !Is(t, open, "@"))
                open++;
            if (!Is(t, open, "{")) continue;
            int close = Match(t, open, "{", "}");
            if (close < 0) continue;
            found.Add(new(t[i].Start, t[close].End, open, close));
            i = close;
        }
        return found;
    }

    private static bool Exact(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, params string[] values)
    {
        if (last - first != values.Length) return false;
        for (int i = 0; i < values.Length; i++)
            if (t[first + i].Value != values[i]) return false;
        return true;
    }

    private static bool IsRedundantUpdate(
        IReadOnlyList<SourceAdmission.Token> t, Hook hook) =>
        Exact(t, hook.BodyOpen + 1, hook.BodyClose,
            "wrappedMethod", "(", ")", ";",
            "if", "IsDefined", "(", "FEPlaces", ".", "PinData", "(",
            "this", ".", "GetMappin", "(", ")", ")", ")", "{",
            "inkWidgetRef", ".", "SetTintColor", "(",
            "this", ".", "iconWidget", ",",
            "FEPlaces", ".", "PinTint", "(", ")", ")", ";", "}");

    private static bool HasTargetedIconHook(
        IReadOnlyList<SourceAdmission.Token> t)
    {
        foreach (var hook in Hooks(t, "MinimapPOIMappinController", "UpdateIcon"))
        {
            // Require the real wrapped icon callback and the final FE-specific
            // styling operation; do not guess from a method name alone.
            if (Exact(t, hook.BodyOpen + 1, hook.BodyClose,
                "wrappedMethod", "(", ")", ";",
                "if", "IsDefined", "(", "FEPlaces", ".", "PinData", "(",
                "this", ".", "GetMappin", "(", ")", ")", ")", "{",
                "this", ".", "FEApplyWashroomIcon", "(", ")", ";", "}"))
                return true;
        }
        return false;
    }

    private static bool HasTintImplementation(
        IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i + 3 < t.Count; i++)
        {
            if (!Seq(t, i, "func", "FEApplyWashroomIcon", "(")) continue;
            int argsEnd = Match(t, i + 2, "(", ")");
            if (argsEnd < 0) continue;
            int open = argsEnd + 1;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int close = Match(t, open, "{", "}");
            if (close < 0) continue;
            for (int k = open + 1; k + 13 < close; k++)
                if (Seq(t, k, "inkWidgetRef", ".", "SetTintColor", "(",
                    "this", ".", "iconWidget", ",",
                    "FEPlaces", ".", "PinTint", "(", ")", ")", ";"))
                    return true;
        }
        return false;
    }

    private static bool HasPinIdentity(
        IReadOnlyList<SourceAdmission.Token> t)
    {
        bool pin = false, data = false;
        for (int i = 0; i + 3 < t.Count; i++)
        {
            if (Seq(t, i, "class", "FEWashroomPinData", "extends",
                "MappinScriptData")) pin = true;
            if (Seq(t, i, "GetScriptData", "(", ")", "as",
                "FEWashroomPinData")) data = true;
        }
        return pin && data;
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_MINIMAP_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) ||
            Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("FE_MINIMAP_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("FE_MINIMAP_SOURCE_HASH_MISSING");

        bool installed = false, targetIconHook = false, tint = false;
        bool pinClass = false, pinAccessor = false;
        var targets = new List<Candidate>();
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("FE_MINIMAP_SOURCE_LINK_REFUSED");
            string original = File.ReadAllText(file, Encoding.UTF8);
            if (!original.Contains("FlushingEtiquette", StringComparison.Ordinal))
                continue;
            var originalTokens = SourceAdmission.ReadTokens(original);
            if (!OwnsModule(originalTokens)) continue;
            installed = true;
            string rel = Path.GetRelativePath(scripts, file);
            string effective = original;
            if (basePreviewRoot is not null)
            {
                string prior = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, "r6", "scripts", rel));
                if (!Inside(prior, basePreviewRoot))
                    throw new InvalidDataException("FE_MINIMAP_OVERLAY_PATH_ESCAPE");
                if (File.Exists(prior))
                    effective = File.ReadAllText(prior, Encoding.UTF8);
            }
            var t = SourceAdmission.ReadTokens(effective);
            if (!OwnsModule(t))
                throw new InvalidDataException("FE_MINIMAP_OVERLAY_LOST_OWNER");
            targetIconHook |= HasTargetedIconHook(t);
            tint |= HasTintImplementation(t);
            for (int i = 0; i + 3 < t.Count; i++)
            {
                if (Seq(t, i, "class", "FEWashroomPinData", "extends",
                    "MappinScriptData")) pinClass = true;
                if (Seq(t, i, "GetScriptData", "(", ")", "as",
                    "FEWashroomPinData")) pinAccessor = true;
            }
            foreach (var hook in Hooks(t, "BaseMinimapMappinController", "Update"))
                targets.Add(new(rel, effective, hook));
        }

        Result result;
        string? rewritten = null, relPath = null;
        if (!installed)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "FE_MINIMAP_OWNER_NOT_INSTALLED", true, false);
        else if (targets.Count > 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                "FE_MINIMAP_UPDATE_WRAPPER_AMBIGUOUS", true, false);
        else if (!targetIconHook || !tint || !pinClass || !pinAccessor)
            result = new(Rule, targets.Count == 0 ? "NOT_APPLICABLE" : "REFUSED",
                0, 0, null, "FE_MINIMAP_TARGETED_STYLING_UNPROVEN", true, false);
        else if (targets.Count == 0)
            result = new(Rule, "COMPLETE", 0, 1, null, null, true, false);
        else
        {
            var candidate = targets[0];
            var t = SourceAdmission.ReadTokens(candidate.Text);
            if (!IsRedundantUpdate(t, candidate.Hook))
                result = new(Rule, "REFUSED", 0, 0, null,
                    "FE_MINIMAP_UPDATE_HAS_OTHER_EFFECTS", true, false);
            else
            {
                string old = candidate.Text[candidate.Hook.Start..candidate.Hook.End];
                if (old.Contains("GCO-GENERIC:", StringComparison.Ordinal) ||
                    old.Contains("GRSP-GENERIC:", StringComparison.Ordinal))
                    result = new(Rule, "REFUSED", 0, 0, null,
                        "FE_MINIMAP_UPDATE_CONTAINS_GENERIC_WORK", true, false);
                else
                {
                    rewritten = candidate.Text[..candidate.Hook.Start] +
                        candidate.Text[candidate.Hook.End..];
                    if (Hooks(SourceAdmission.ReadTokens(rewritten),
                        "BaseMinimapMappinController", "Update").Count != 0)
                        throw new InvalidDataException(
                            "FE_MINIMAP_GENERATOR_POSTCONDITION_FAILED");
                    relPath = candidate.Path;
                    result = new(Rule, "APPLIED", 1, 0,
                        "r6/scripts/" + relPath.Replace('\\', '/'),
                        null, true, false);
                }
            }
        }

        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_MINIMAP_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        if (rewritten is not null && relPath is not null)
        {
            string dest = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", relPath));
            if (!Inside(dest, output))
                throw new InvalidDataException("FE_MINIMAP_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, rewritten, new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
