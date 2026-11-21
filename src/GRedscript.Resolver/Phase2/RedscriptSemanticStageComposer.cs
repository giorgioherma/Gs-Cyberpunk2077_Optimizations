using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Composes independently verified Chipware/ImmersiveCyberware operations into the
/// one production REDscript Generate. Runs after the existing ten generic and
/// earlier semantic stages, using their effective-preview as the only overlay.
/// This is composition/verification, never a corrective source rewrite.
/// </summary>
internal static class RedscriptSemanticStageComposer
{
    internal sealed record Outcome(
        string RuleId, string Status, int AppliedEdits,
        string[] SourcePaths, string[] Refusals);

    public static void ApplyChipwareAndImmersive(
        string game, string output, string final,
        List<RedscriptGenerationPlan.OutputItem> completed,
        List<RedscriptGenerationPlan.SkippedRule> skipped,
        ref int files, ref int expressions, int firstOrder = 37)
    {
        Run(firstOrder, "pass8-chipware-throughput", "zzy_bChipwareExpansion",
            game, output, final, completed, skipped, ref files, ref expressions,
            (g, o, b) =>
            {
                var r = Pass8ThroughputPass.Generate(g, o, b);
                return new(r.RuleId, r.Status, r.AppliedEdits, r.SourcePaths, r.Refusals);
            });
        Run(firstOrder + 1, "pass8-chipware-combat-cache", "zzy_bChipwareExpansion",
            game, output, final, completed, skipped, ref files, ref expressions,
            (g, o, b) =>
            {
                var r = Pass8ChipwareCombatCachePass.Generate(g, o, b);
                return new(r.RuleId, r.Status, r.AppliedEdits, r.SourcePaths, r.Refusals);
            });
        Run(firstOrder + 2, "pass8-immersive-cyberware", "ImmersiveCyberware",
            game, output, final, completed, skipped, ref files, ref expressions,
            (g, o, b) =>
            {
                var r = Pass8ImmersiveCyberwarePass.Generate(g, o, b);
                return new(r.RuleId, r.Status, r.AppliedEdits, r.SourcePaths, r.Refusals);
            });
    }

    internal static void Run(
        int order, string id, string owner,
        string game, string output, string final,
        List<RedscriptGenerationPlan.OutputItem> completed,
        List<RedscriptGenerationPlan.SkippedRule> skipped,
        ref int files, ref int expressions,
        Func<string, string, string?, Outcome> generate)
    {
        string stage = Path.Combine(output, "_stages", order + "-" + id);
        string? prior = Directory.Exists(final) ? final : null;
        Outcome report = generate(game, stage, prior);

        if (report.Status is not ("APPLIED" or "COMPLETE" or "REFUSED" or "NOT_APPLICABLE") ||
            report.AppliedEdits < 0 || report.SourcePaths is null ||
            report.Refusals is null)
            throw new InvalidDataException("PASS8_CANONICAL_REPORT_INVALID:" + id);

        // A partially compatible mod is not a whole-owner refusal: apply
        // independent safe methods AND preserve every incompatible operation.
        if (report.Refusals.Length > 0)
            skipped.Add(new(order, id, report.Refusals.Length,
                string.Join(" | ", report.Refusals)));

        string scriptsRoot = Path.Combine(stage, "r6", "scripts");
        var staged = Directory.Exists(scriptsRoot)
            ? Directory.EnumerateFiles(scriptsRoot, "*.reds",
                SearchOption.AllDirectories).ToArray()
            : [];
        var paths = report.SourcePaths.Distinct(StringComparer.Ordinal).ToArray();

        if (report.Status != "APPLIED")
        {
            if (report.AppliedEdits != 0 || paths.Length != 0 || staged.Length != 0)
                throw new InvalidDataException("PASS8_CANONICAL_NONAPPLIED_OUTPUT:" + id);
            return;
        }
        if (report.AppliedEdits <= 0 || paths.Length == 0 ||
            staged.Length != paths.Length || paths.Length != report.SourcePaths.Length)
            throw new InvalidDataException("PASS8_CANONICAL_APPLIED_OUTPUT_INCOMPLETE:" + id);

        var produced = new HashSet<string>(StringComparer.Ordinal);
        int newFiles = 0, composedFiles = 0;
        foreach (string file in staged)
        {
            string rel = Path.GetRelativePath(stage, file).Replace('\\', '/');
            string[] parts = rel.Split('/');
            // The first three segments must be r6/scripts/<owner or nested
            // owner>. Do not permit output from an unrelated mod.
            if (parts.Length < 4 || parts[0] != "r6" || parts[1] != "scripts" ||
                !parts.Skip(2).Take(parts.Length - 3).Any(x =>
                    owner.Split('|').Contains(x, StringComparer.OrdinalIgnoreCase)) ||
                !rel.EndsWith(".reds", StringComparison.OrdinalIgnoreCase) ||
                !paths.Contains(rel, StringComparer.Ordinal) ||
                !produced.Add(rel))
                throw new InvalidDataException("PASS8_CANONICAL_FOREIGN_OR_UNREPORTED_SOURCE:" + rel);

            string src = Path.GetFullPath(file);
            string dst = Path.GetFullPath(Path.Combine(final,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(src, stage) || !IsInside(dst, final) ||
                (File.GetAttributes(src) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("PASS8_CANONICAL_PATH_ESCAPE:" + rel);

            bool exists = File.Exists(dst);
            if (exists)
            {
                string before = File.ReadAllText(dst);
                string after = File.ReadAllText(src);
                if (string.Equals(before, after, StringComparison.Ordinal))
                    throw new InvalidDataException("PASS8_CANONICAL_UNCHANGED_OUTPUT:" + rel);
                var generic = Regex.Matches(before,
                        @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+")
                    .Select(m => m.Value).Distinct(StringComparer.Ordinal);
                if (generic.Any(marker => !after.Contains(marker,
                        StringComparison.Ordinal)))
                    throw new InvalidDataException("PASS8_CANONICAL_GENERIC_WORK_LOST:" + rel);
                composedFiles++;
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                newFiles++;
            }
            File.Copy(src, dst, overwrite: exists);
        }

        files += newFiles;
        expressions += report.AppliedEdits;
        completed.Add(new(order, id, report.AppliedEdits,
            newFiles, composedFiles, []));
    }

    private static bool IsInside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
