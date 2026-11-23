using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 6.0: remove four redundant camera-axis registrations
/// from EVS's own VehicleComponent.RegisterInputListener wrapper. The wrapper's
/// original engine registration and all other actions survive. #30's existing
/// VehicleComponent.OnAction PS optimization remains intact.
/// </summary>
internal static class EnhancedVehicleCameraRegistrationPass
{
    internal const string Rule = "EVS_UNUSED_CAMERA_INPUT_REGISTRATIONS_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Block(int Open, int Close);
    private sealed record Decision(string Status, string? Reason,
        string? Updated, int Edits);
    private static readonly string[] Names =
        ["CameraX", "CameraY", "CameraMouseX", "CameraMouseY"];

    private static int Matching(IReadOnlyList<SourceAdmission.Token> t,
        int index, string opening, string closing)
    {
        if (index < 0 || index >= t.Count || t[index].Value != opening)
            return -1;
        int level = 0;
        for (int i = index; i < t.Count; i++)
        {
            if (t[i].Value == opening) level++;
            else if (t[i].Value == closing && --level == 0) return i;
        }
        return -1;
    }

    private static Block? Wrapper(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != "RegisterInputListener" ||
                t[i + 2].Value != "(") continue;
            int argsEnd = Matching(t, i + 2, "(", ")");
            if (argsEnd < 0 || argsEnd != i + 3) continue;
            string prefix = source[Math.Max(0, t[i].Start - 250)..t[i].Start];
            if (!Regex.IsMatch(prefix,
                @"@wrapMethod\s*\(\s*VehicleComponent\s*\)\s*" +
                @"(?:(?:private|public|protected|final|cb)\s+)*$"))
                continue;
            int open = argsEnd + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int end = Matching(t, open, "{", "}");
            if (end < 0 || found != null) return null;
            found = new(t[open].Start, t[end].Start);
            i = end;
        }
        return found;
    }

    private static Decision Rewrite(string source)
    {
        if (!Regex.IsMatch(source,
            @"\bmodule\s+Hgyi56\s*\.\s*Enhanced_Vehicle_System\b"))
            return new("REFUSED", "EVS_CAMERA_MODULE_MISSING", null, 0);
        var wrapper = Wrapper(source);
        if (wrapper is null)
            return new("REFUSED", "EVS_CAMERA_WRAPPER_MISSING_OR_AMBIGUOUS", null, 0);
        string body = source[(wrapper.Open + 1)..wrapper.Close];
        var wrappedCalls = Regex.Matches(body, @"\bwrappedMethod\s*\(\s*\)\s*;");
        // The stock EVS wrapper correctly calls the original registration
        // once for unsupported vehicle types and once for supported ones.
        // Preserve both and require camera subscriptions to follow both.
        if (wrappedCalls.Count is < 1 or > 2)
            return new("REFUSED", "EVS_CAMERA_NATIVE_WRAPPER_UNPROVEN", null, 0);
        var wrapped = wrappedCalls[^1];

        var edits = new List<(int Start, int End)>();
        foreach (string name in Names)
        {
            var lines = Regex.Matches(body,
                @"(?m)^[ \t]*player\s*\.\s*RegisterInputListener\s*\(\s*this\s*,\s*n""" +
                Regex.Escape(name) + @"""\s*\)\s*;[ \t]*(?:\r?\n|\r|$)");
            int references = Regex.Matches(source,
                @"\b" + Regex.Escape(name) + @"\b").Count;
            if (references != lines.Count || lines.Count > 1)
                return new("REFUSED", "EVS_CAMERA_ACTION_USED_OR_REGISTRATION_DRIFT:" + name, null, 0);
            if (lines.Count == 0) continue;
            var m = lines[0];
            if (m.Index < wrapped.Index + wrapped.Length)
                return new("REFUSED", "EVS_CAMERA_REGISTRATION_BEFORE_NATIVE_WRAPPER", null, 0);
            edits.Add((wrapper.Open + 1 + m.Index,
                wrapper.Open + 1 + m.Index + m.Length));
        }
        if (edits.Count == 0)
            return new("COMPLETE", null, null, 0);
        string updated = source;
        foreach (var edit in edits.OrderByDescending(e => e.Start))
            updated = updated[..edit.Start] + updated[edit.End..];
        if (Wrapper(updated) is null ||
            Names.Any(name => Regex.IsMatch(updated,
                @"\b" + Regex.Escape(name) + @"\b")) ||
            !updated.Contains("wrappedMethod();", StringComparison.Ordinal))
            throw new InvalidDataException("EVS_CAMERA_POSTCONDITION_FAILED");
        return new("APPLIED", null, updated, edits.Count);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("EVS_CAMERA_OUTPUT_UNSAFE");
        if (baseOverlayRoot != null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("EVS_CAMERA_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sha = admission.SourceStackSha256 ??
            throw new InvalidDataException("EVS_CAMERA_NO_SOURCE_SHA");
        var candidates = Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories).Where(path =>
        {
            string src = File.ReadAllText(path, Encoding.UTF8);
            return Regex.IsMatch(src,
                       @"\bmodule\s+Hgyi56\s*\.\s*Enhanced_Vehicle_System\b") &&
                   src.Contains("RegisterInputListener", StringComparison.Ordinal) &&
                   Regex.IsMatch(src, @"@wrapMethod\s*\(\s*VehicleComponent\s*\)");
        }).ToArray();
        string status = "NOT_APPLICABLE";
        string? reason = null, relative = null, rewritten = null;
        int edits = 0;
        if (candidates.Length > 1)
        {
            status = "REFUSED";
            reason = "EVS_CAMERA_MULTIPLE_OWNER_SOURCES";
        }
        else if (candidates.Length == 1)
        {
            string path = candidates[0];
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("EVS_CAMERA_SOURCE_LINK_UNSAFE");
            relative = "r6/scripts/" +
                Path.GetRelativePath(scripts, path).Replace('\\', '/');
            string src = File.ReadAllText(path, Encoding.UTF8);
            if (baseOverlayRoot != null)
            {
                string staged = Path.GetFullPath(Path.Combine(baseOverlayRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(staged, baseOverlayRoot))
                    throw new InvalidDataException("EVS_CAMERA_OVERLAY_PATH_ESCAPE");
                if (File.Exists(staged))
                    src = File.ReadAllText(staged, Encoding.UTF8);
            }
            var decision = Rewrite(src);
            status = decision.Status;
            reason = decision.Reason;
            edits = decision.Edits;
            rewritten = decision.Updated;
        }
        if (!string.Equals(sha, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("EVS_CAMERA_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        if (rewritten != null && relative != null)
        {
            string dest = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dest, output) ||
                !dest.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("EVS_CAMERA_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, rewritten, new UTF8Encoding(false));
        }
        var result = new Result(Rule, status, edits,
            status == "COMPLETE" ? 1 : 0, relative, reason, true, false);
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
