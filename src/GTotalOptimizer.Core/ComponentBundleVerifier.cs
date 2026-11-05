using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

public sealed record VerifiedBundle(bool Ready, string Detail, string? CetExecutable = null,
    string? GrspExecutable = null);

public static class ComponentBundleVerifier
{
    // The CI packaging step must author this manifest from exact component builds.
    // A filename or a stale release title is never sufficient for game mutation.
    public static VerifiedBundle Inspect(string packageRoot)
    {
        var manifestFile = Path.Combine(packageRoot, "components", "manifest.json");
        if (!File.Exists(manifestFile))
            return new(false, "Incomplete GCO installation: components/manifest.json not found. " +
                "Extract the entire v1.1.3 Integrated artifact into a new empty folder. " +
                "Expected: " + manifestFile);

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
            var root = doc.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 2 ||
                !root.TryGetProperty("components", out var components) || components.ValueKind != JsonValueKind.Array)
                return new(false, "Component manifest schema invalid.");

            var verified = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var normalizedRoot = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var item in components.EnumerateArray())
            {
                var id = item.GetProperty("id").GetString();
                var relative = item.GetProperty("executable").GetString();
                var hash = item.GetProperty("sha256").GetString();
                var source = item.GetProperty("sourceCommit").GetString();
                var runId = item.TryGetProperty("ciRun", out var run) ? run.GetInt64() : 0;
                var artifactId = item.TryGetProperty("artifactId", out var artifact) ? artifact.GetInt64() : 0;

                if (id is not ("cet" or "grsp") || string.IsNullOrWhiteSpace(relative) ||
                    string.IsNullOrWhiteSpace(hash) || hash.Length != 64 ||
                    source?.Length != 40 ||
                    (id == "cet" && (runId <= 0 || artifactId <= 0)) ||
                    (id == "grsp" && (!item.TryGetProperty("releaseSha256", out var release) ||
                                      release.GetString()?.Length != 64)) ||
                    !hash.All(Uri.IsHexDigit))
                    return new(false, "Component manifest identity, source or CI provenance is incomplete.");

                var full = Path.GetFullPath(Path.Combine(packageRoot, relative));
                if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                    !File.Exists(full) || verified.ContainsKey(id))
                    return new(false, "Component is missing, duplicated or outside verified package root.");

                // A public installer must also verify all DLL/payload assets and their
                // license/provenance before producing this manifest; EXE hash is necessary, not sufficient.
                using var stream = File.OpenRead(full);
                var actual = Convert.ToHexString(SHA256.HashData(stream));
                if (!actual.Equals(hash, StringComparison.OrdinalIgnoreCase))
                    return new(false, $"Component {id} executable differs from its declared hash.");
                verified[id] = full;
            }

            if (!verified.TryGetValue("cet", out var cet) || !verified.TryGetValue("grsp", out var grsp))
                return new(false, "Both CET and GRSP are required for a combined install; no unowned partial mutation.");

            if (!root.TryGetProperty("payloads", out var payloads) ||
                payloads.ValueKind != JsonValueKind.Array || payloads.GetArrayLength() != 5)
                return new(false, "Required native-payload inventory absent.");
            var nativePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in payloads.EnumerateArray())
            {
                var relative = item.GetProperty("path").GetString();
                var expected = item.GetProperty("sha256").GetString();
                if (string.IsNullOrWhiteSpace(relative) || expected?.Length != 64 ||
                    !expected.All(Uri.IsHexDigit))
                    return new(false, "Native payload identity incomplete.");
                var full = Path.GetFullPath(Path.Combine(packageRoot, relative));
                if (!full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
                    !nativePaths.Add(relative) || !File.Exists(full))
                    return new(false, "Native payload missing, duplicated or outside verified package.");
                using var stream = File.OpenRead(full);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(expected, StringComparison.OrdinalIgnoreCase))
                    return new(false, "Native payload changed: " + relative);
            }
            foreach (var required in new[]
            {
                "components/cet/payload/cyber_engine_tweaks.PROFILER.dll",
                "components/cet/payload/0-Engine/modules/G-CET/Scheduler.lua",
                "components/cet/payload/CETProfilerControls/init.lua",
                "components/grsp/payload/G-REDscript-Profiler.dll",
                "components/presentmon/PresentMon.exe"
            })
                if (!nativePaths.Contains(required))
                    return new(false, "Required native payload absent from manifest: " + required);
            return new(true, "Both component launchers and five native/payload files verified.",
                cet, grsp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return new(false, "Invalid or unreadable component manifest: " + ex.Message);
        }
    }
}
