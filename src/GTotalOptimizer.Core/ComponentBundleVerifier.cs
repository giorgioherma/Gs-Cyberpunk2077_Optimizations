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
            return new(false, "No CI-proven component manifest. Profiler installation is disabled.");

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestFile));
            var root = doc.RootElement;
            if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 ||
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
                var runId = item.GetProperty("ciRun").GetInt64();
                var artifactId = item.GetProperty("artifactId").GetInt64();

                if (id is not ("cet" or "grsp") || string.IsNullOrWhiteSpace(relative) ||
                    string.IsNullOrWhiteSpace(hash) || hash.Length != 64 ||
                    source?.Length != 40 || runId <= 0 || artifactId <= 0 ||
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

            return new(true, "Both executable identities match staged CI manifest; runtime payload/status checks still required.",
                cet, grsp);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or KeyNotFoundException or FormatException or InvalidOperationException)
        {
            return new(false, "Invalid or unreadable component manifest: " + ex.Message);
        }
    }
}
