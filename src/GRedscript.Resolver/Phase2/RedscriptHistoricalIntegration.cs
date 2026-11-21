using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// One production generation from post-generic stock. Reuses the independently
/// validated source-owner generators from Passes 4–10; no archived REDscript
/// files, follow-up repair runs, second runtime or parallel mod installers.
/// Overlapping operations are applied once by their strongest compatible owner.
/// </summary>
internal static class RedscriptHistoricalIntegration
{
    private static Pass8CanonicalComposition.Outcome Single(
        string rule, string status, int edits, string? path, string? reason)
    {
        return new(rule, status, edits,
            status == "APPLIED" && !string.IsNullOrWhiteSpace(path) ? [path] : [],
            status == "REFUSED" && !string.IsNullOrWhiteSpace(reason) ? [reason] : []);
    }

    public static void Apply(
        string game, string output, string final,
        List<RedscriptGenerationPlan.OutputItem> completed,
        List<RedscriptGenerationPlan.SkippedRule> skipped,
        ref int files, ref int expressions)
    {
        // Pass 4's StealthRunner cache is covered by Pass 7, and its chipware
        // relevance gate by Pass 8. Generate only the unique quad-geometry
        // operation; the standalone Pass 4 contract still tests all owners.
        Pass8CanonicalComposition.Run(42, "pass4-reinforcements-geometry",
            "ReinforcementsSystem", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                Pass4Program.Generate(g, o, b, uniqueGeometryOnly: true);
                using var doc = JsonDocument.Parse(File.ReadAllText(
                    Path.Combine(o, "pass4-result.json")));
                var root = doc.RootElement;
                if (root.GetProperty("schemaVersion").GetString() !=
                    "gco.phase2.pass4.isolated.v1" ||
                    !root.GetProperty("sourceUnchanged").GetBoolean())
                    throw new InvalidDataException("INTEGRATION_PASS4_REPORT_INVALID");
                var ops = root.GetProperty("operations").EnumerateArray().ToArray();
                if (ops.Length != 1 ||
                    ops[0].GetProperty("ruleId").GetString() !=
                    "REINFORCEMENTS_PREPARED_QUAD_CACHE_V1")
                    throw new InvalidDataException("INTEGRATION_PASS4_OWNER_DRIFT");
                var op = ops[0];
                string status = op.GetProperty("status").GetString() ??
                    throw new InvalidDataException("INTEGRATION_PASS4_MISSING_STATUS");
                var paths = root.GetProperty("sourcePaths").EnumerateArray()
                    .Select(x => x.GetString()!).ToArray();
                string? reason = op.GetProperty("reason").GetString();
                return new Pass8CanonicalComposition.Outcome(
                    "REINFORCEMENTS_PREPARED_QUAD_CACHE_V1", status,
                    root.GetProperty("appliedEdits").GetInt32(), paths,
                    status == "REFUSED" && !string.IsNullOrEmpty(reason)
                        ? [reason] : []);
            });

        Pass8CanonicalComposition.Run(43, "pass5-holotravel-material",
            "HQ_HoloTravel", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = HoloTravelMaterialEventPass.Generate(g, o, b);
                return Single(r.RuleId, r.Status, r.AppliedEdits,
                    r.SourcePath, r.Reason);
            });

        Pass8CanonicalComposition.Run(44, "pass6-virtual-atelier-pacing",
            "VirtualAtelierDelivery", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = VirtualAtelierSpawnPacingPass.Generate(g, o, b);
                return Single(r.RuleId, r.Status, r.AppliedEdits,
                    r.SourcePath, r.Reason);
            });

        Pass8CanonicalComposition.Run(45, "pass6-evs-camera-admission",
            "Enhanced Vehicle System", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = EnhancedVehicleCameraRegistrationPass.Generate(g, o, b);
                return Single(r.RuleId, r.Status, r.AppliedEdits,
                    r.SourcePath, r.Reason);
            });

        Pass8CanonicalComposition.Run(46, "pass7-chase-frame-floor",
            "NightCityRemembers|StealthRunner|RunningMan|LizziesBDs|ReinforcementsSystem|zzy_bChipwareExpansion",
            game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = Pass7ChaseFrameFloorPass.Generate(g, o, b);
                if (r.Operations.Length != 11 ||
                    r.AppliedEdits != r.Operations.Sum(x => x.AppliedEdits))
                    throw new InvalidDataException("INTEGRATION_PASS7_OPERATION_REPORT_DRIFT");
                var invalid = r.Operations.Where(x =>
                    x.Status is not ("APPLIED" or "COMPLETE" or
                        "NOT_APPLICABLE" or "REFUSED")).ToArray();
                if (invalid.Length > 0)
                    throw new InvalidDataException("INTEGRATION_PASS7_UNKNOWN_STATUS");

                // Historical Pass 7 deliberately refused this cache pending
                // approval. It is now explicitly approved and implemented by
                // Pass 10. Its actual result and refusal belong to stage 51,
                // not a stale earlier hypothetical refusal.
                var refusals = r.Operations.Where(x =>
                        x.Status == "REFUSED" &&
                        x.Id != "CHIPWARE_24_PERK_RESULT_MEMO_V1")
                    .Select(x => x.Id + ":" + (x.Reason ?? "SOURCE_INCOMPATIBLE"))
                    .ToArray();
                var paths = r.Operations.Where(x =>
                        x.Status == "APPLIED" &&
                        !string.IsNullOrWhiteSpace(x.SourcePath))
                    .Select(x => x.SourcePath!).Distinct(StringComparer.Ordinal).ToArray();
                string status = r.AppliedEdits > 0 ? "APPLIED" :
                    refusals.Length > 0 ? "REFUSED" :
                    r.Operations.Any(x => x.Status == "COMPLETE") ?
                        "COMPLETE" : "NOT_APPLICABLE";
                return new Pass8CanonicalComposition.Outcome(
                    r.RuleId, status, r.AppliedEdits, paths, refusals);
            });

        // The three cached-chipware/Immersive operations now follow both
        // Pass 4 geometry and the complete Pass 7 chase/frame-floor family.
        Pass8CanonicalComposition.Apply(game, output, final, completed,
            skipped, ref files, ref expressions, firstOrder: 47);

        Pass8CanonicalComposition.Run(50, "pass9-runningman-dormancy",
            "RunningMan", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = RunningManDoubleSprintPass.Generate(g, o, b);
                return new Pass8CanonicalComposition.Outcome(
                    r.RuleId, r.Status, r.AppliedEdits, r.SourcePaths,
                    r.Status == "REFUSED" && !string.IsNullOrWhiteSpace(r.Reason)
                        ? [r.Reason] : []);
            });

        Pass8CanonicalComposition.Run(51, "pass10-chipware-perk-memo",
            "zzy_bChipwareExpansion", game, output, final, completed, skipped,
            ref files, ref expressions, (g, o, b) =>
            {
                var r = ChipwarePerkResultMemoPass.Generate(g, o, b);
                return new Pass8CanonicalComposition.Outcome(
                    r.RuleId, r.Status, r.AppliedEdits, r.SourcePaths, r.Refusals);
            });
    }
}
