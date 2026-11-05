using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

// Read-only, source-owner-scoped preservation BEFORE incomplete native profiling
// state is removed by CET / GRSP owner restorers. Never copies arbitrary mods.
public sealed record ProfilerCrashEvidenceResult(string Folder, int FileCount);

public static class ProfilerCrashEvidence
{
    private static readonly HashSet<string> DiagnosticExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".csv", ".json", ".txt", ".log", ".tmp" };

    public static ProfilerCrashEvidenceResult Preserve(string packageRoot, string gameRoot)
    {
        var resultsRoot = Path.Combine(Path.GetFullPath(packageRoot), "RESULTS");
        var folder = Path.Combine(resultsRoot,
            "GCO-Incomplete-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") +
            "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var copied = new List<object>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Save(string owner, string source, string relative)
        {
            if (!File.Exists(source) || !seen.Add(Path.GetFullPath(source))) return;
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Refusing to follow a linked profiler diagnostic: " + source);
            var output = Path.Combine(folder, owner, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Copy(source, output, overwrite: false);
            // Ensure interrupted copies do not count as recoverable evidence.
            var inputHash = Hash(source);
            var outputHash = Hash(output);
            if (!inputHash.Equals(outputHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Profiler evidence copy did not verify: " + source);
            copied.Add(new
            {
                owner, relative,
                bytes = new FileInfo(output).Length,
                sha256 = outputHash
            });
        }

        var game = Path.GetFullPath(gameRoot);
        var cet = Path.Combine(game, "bin", "x64", "plugins", "cyber_engine_tweaks");
        if (Directory.Exists(cet))
        {
            foreach (var file in Directory.EnumerateFiles(cet, "CET_Runtime_Profile_*",
                SearchOption.TopDirectoryOnly))
            {
                if (DiagnosticExtensions.Contains(Path.GetExtension(file)))
                    Save("CET", file, Path.GetFileName(file));
            }
            foreach (var filename in new[] { "bindings.json", "cyber_engine_tweaks.log", "scripting.log" })
                Save("CET", Path.Combine(cet, filename), filename);
        }

        var grsp = Path.Combine(game, "red4ext", "plugins", "G-REDscript-Profiler");
        foreach (var subdir in new[] { "RESULTS", "Data" })
        {
            var sourceRoot = Path.Combine(grsp, subdir);
            if (!Directory.Exists(sourceRoot)) continue;
            // Skip directory junctions/reparse points; never traverse into mods.
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            foreach (var file in Directory.EnumerateFiles(sourceRoot, "*", options))
            {
                if (!DiagnosticExtensions.Contains(Path.GetExtension(file))) continue;
                var relative = Path.Combine(subdir, Path.GetRelativePath(sourceRoot, file));
                Save("GRSP", file, relative);
            }
        }

        // The GCO Results directory is not an owner source and is not deleted
        // during native restoration. PresentMon CSVs therefore stay in place.
        File.WriteAllText(Path.Combine(folder, "evidence.json"),
            JsonSerializer.Serialize(new
            {
                schema = 1,
                gameRoot = game,
                savedUtc = DateTimeOffset.UtcNow,
                purpose = "Incomplete CET / GRSP crash evidence preserved before owner restore",
                fileCount = copied.Count,
                files = copied
            }, new JsonSerializerOptions { WriteIndented = true }));
        return new ProfilerCrashEvidenceResult(folder, copied.Count);
    }

    private static string Hash(string file)
    {
        using var stream = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
