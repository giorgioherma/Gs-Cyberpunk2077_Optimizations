using GTotalOptimizer.Core;
using GRedscript.Resolver;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Mo2RealGenerationTests
{
    // Actual Page 2 services, historical measured capture, real owner
    // Resolver, representative generic REDscript source and original
    // 0-Engine. Not a claim that the replay source was the recorded stack.
    public static async Task RunAsync(string package, string game, string instance, string id)
    {
        var captured = CollectedCaptureStore.ReadRestored(package, game, id);
        if (captured.Mo2Source?.Profile != "Heavy")
            throw new InvalidDataException("MO2_REAL_GENERATION_PROFILE_BINDING_MISSING");
        const string relative = "r6/scripts/AuthorA/Original.reds";
        const string original = """
            module AuthorA
            public class A extends IScriptable {
              public func Join() -> String {
                let lines: array<String> = ["one", "two"];
                let text: String = "";
                let i: Int32 = 0;
                while i < ArraySize(lines) {
                  text += lines[i];
                  i += 1;
                };
                return text;
              }
            }
            """;
        var physical = Path.Combine(game, relative);
        var winner = Path.Combine(instance, "mods", "SimpleCETMod", relative);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        Directory.CreateDirectory(Path.GetDirectoryName(winner)!);
        File.WriteAllText(physical, "module Shadowed\npublic class Shadowed extends IScriptable {}");
        File.WriteAllText(winner, original);
        // MO2's current UI profile can be Other: the saved Heavy source
        // remains the exclusive optimization input.
        var ini = Path.Combine(instance, "ModOrganizer.ini");
        File.WriteAllText(ini, File.ReadAllText(ini).Replace("selected_profile=Heavy", "selected_profile=Other"));
        var indexPath = Path.Combine(CollectedCaptureStore.SessionFolder(package, id), "collection.json");
        var indexBefore = Hash(indexPath);
        var initialWinner = Hash(winner);
        var physicalBefore = Hash(physical);
        var planPath = Path.Combine(Path.GetDirectoryName(indexPath)!, "REDscript_GenerationPlan.json");
        var combined = new CombinedOptimizationPassService();
        string? generatedFolder = null;
        try
        {
            using (var view = Mo2AnalysisSource.Create(captured))
            {
                if (Hash(Path.Combine(view.Root, relative)) != initialWinner)
                    throw new InvalidDataException("MO2_REAL_GENERATION_WRONG_WINNER");
                await new CetReadOnlyAnalysisService().AnalyzeAsync(package, game, id, mo2Source: view);
                var plan = RedscriptGenerationPlan.Create(view.Root, planPath);
                if (!plan.Rules.Any(rule => rule.Id == "stable-array-bound" && rule.Eligible > 0))
                    throw new InvalidDataException("MO2_REAL_GENERATION_EXPECTED_GENERIC_CANDIDATE_MISSING");
                var availability = await combined.InspectAvailableAsync(package, game, id, mo2Source: view);
                if (!availability.HasNewOptimizations || availability.RedscriptEdits < 1)
                    throw new InvalidDataException("MO2_REAL_GENERATION_PREVIEW_MISSING");
            }
            // Generate independently rebuilds the view rather than relying
            // on the already disposed ANALYZE projection.
            var pass = await combined.GenerateAsync(package, game, id);
            generatedFolder = Path.GetDirectoryName(pass.ZipPath);
            if (pass.RedscriptEdits < 1 || pass.RedscriptFiles < 1 ||
                CombinedPassOutputInspector.Validate(pass.ZipPath) != pass.GeneratedFiles)
                throw new InvalidDataException("MO2_REAL_GENERATION_ZIP_INVALID");
            using (var zip = ZipFile.OpenRead(pass.ZipPath))
            {
                var entry = zip.GetEntry(relative) ??
                    throw new InvalidDataException("MO2_REAL_GENERATION_EXPECTED_REDSCRIPT_MISSING");
                using var reader = new StreamReader(entry.Open());
                var optimized = reader.ReadToEnd();
                if (!optimized.Contains("GCO-GENERIC:STABLE_ARRAY_BOUND_V1", StringComparison.Ordinal) ||
                    !optimized.Contains("while i < __gcoArrBoundL", StringComparison.Ordinal))
                    throw new InvalidDataException("MO2_REAL_GENERATION_EXPECTED_TRANSFORM_MISSING");
            }
            using (var sidecar = JsonDocument.Parse(File.ReadAllText(pass.ZipPath + ".ownership.json")))
            {
                var entry = sidecar.RootElement.GetProperty("files").EnumerateArray()
                    .Single(item => item.GetProperty("path").GetString() == relative);
                if (!string.Equals(entry.GetProperty("originalSha256").GetString(),
                    initialWinner, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("MO2_REAL_GENERATION_ROLLBACK_HASH_IS_NOT_WINNER");
            }
            _ = CollectedCaptureStore.ReadRestored(package, game, id);
            if (Hash(indexPath) != indexBefore || Hash(winner) != initialWinner ||
                Hash(physical) != physicalBefore)
                throw new InvalidDataException("MO2_REAL_GENERATION_CHANGED_ORIGINALS_OR_CAPTURE");
            Console.WriteLine("PASS MO2 actual combined ZIP through existing Page 2 services: CET transforms=" +
                pass.CetTransforms + ", REDscript edits=" + pass.RedscriptEdits +
                ", files=" + pass.GeneratedFiles + "; selected winner and rollback hash verified");
        }
        finally
        {
            // Generated test output must not leak into any release artifact.
            if (generatedFolder is not null && Directory.Exists(generatedFolder))
                Directory.Delete(generatedFolder, recursive: true);
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
