using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace GRedscript.Resolver;

/// <summary>
/// Source-only view of the G-REDruntime declarations supplied by the SAME
/// combined pass. The original game's r6/scripts fingerprint is unchanged.
/// A bundled file is visible only when CopyMissingFramework will actually
/// add it: installed files with the same relative path take precedence.
/// </summary>
internal static class ProvidedRuntimeSourceScope
{
    private sealed record View(string GameScripts, string[] MissingBundleFiles);
    private static readonly AsyncLocal<View?> Current = new();

    private sealed class Lease(View? previous) : IDisposable
    {
        public void Dispose() => Current.Value = previous;
    }

    internal static IDisposable Enter(string gameRoot, string? ownerRoot)
    {
        View? previous = Current.Value;
        if (ownerRoot is null)
        {
            Current.Value = null;
            return new Lease(previous);
        }

        string game = Path.GetFullPath(gameRoot);
        string scripts = Path.GetFullPath(Path.Combine(game, "r6", "scripts"));
        string owner = Path.GetFullPath(ownerRoot);
        string manifest = Path.Combine(Path.GetDirectoryName(owner) ??
            throw new InvalidDataException("GCO_RUNTIME_OWNER_ROOT_INVALID"),
            "source-sha256.json");
        if (!Directory.Exists(scripts) || !Directory.Exists(owner) ||
            !File.Exists(manifest) || IsInside(owner, game) ||
            IsInside(scripts, owner))
            throw new InvalidDataException("GCO_PROVIDED_RUNTIME_SOURCE_UNAVAILABLE");

        using var document = JsonDocument.Parse(File.ReadAllText(manifest));
        if (document.RootElement.ValueKind != JsonValueKind.Array ||
            document.RootElement.GetArrayLength() < 11)
            throw new InvalidDataException("GCO_PROVIDED_RUNTIME_MANIFEST_INVALID");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            string rel = entry.GetProperty("path").GetString() ?? "";
            string sha = entry.GetProperty("sha256").GetString() ?? "";
            if (!rel.StartsWith("r6/scripts/G-RedRuntime/",
                    StringComparison.OrdinalIgnoreCase) ||
                !rel.EndsWith(".reds", StringComparison.OrdinalIgnoreCase) ||
                rel.Contains('\\') ||
                rel.Split('/').Any(part => part is "" or "." or "..") ||
                sha.Length != 64 || !sha.All(Uri.IsHexDigit) ||
                !seen.Add(rel))
                throw new InvalidDataException("GCO_PROVIDED_RUNTIME_MANIFEST_PATH_INVALID");

            string file = Path.GetFullPath(Path.Combine(owner,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(file, owner) || !File.Exists(file) ||
                (File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0 ||
                !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)))
                    .Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("GCO_PROVIDED_RUNTIME_SOURCE_HASH_MISMATCH");

            // This is exactly the deployed-precedence rule in CopyMissingFramework.
            string live = Path.GetFullPath(Path.Combine(game,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsInside(live, scripts))
                throw new InvalidDataException("GCO_PROVIDED_RUNTIME_PATH_ESCAPE");
            if (File.Exists(live))
            {
                if ((File.GetAttributes(live) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("GCO_PROVIDED_RUNTIME_GAME_SOURCE_LINK");
                // The installed file wins. Do NOT claim the bundle will
                // replace an incompatible locally edited framework module.
                continue;
            }
            missing.Add(file);
        }

        // All present bundle files must be hash-manifested. A partial or
        // untracked package cannot silently stand in as a runtime dependency.
        string framework = Path.Combine(owner, "r6", "scripts", "G-RedRuntime");
        if (!Directory.Exists(framework) ||
            Directory.EnumerateFiles(framework, "*.reds", SearchOption.AllDirectories)
                .Any(file => !seen.Contains(
                    Path.GetRelativePath(owner, file).Replace('\\', '/'))))
            throw new InvalidDataException("GCO_PROVIDED_RUNTIME_UNMANIFESTED_SOURCE");

        Current.Value = new View(scripts, missing.ToArray());
        return new Lease(previous);
    }

    /// <summary>
    /// Used ONLY by runtime-ABI admission checks, never owner scanning,
    /// generic transforms, or source-stack hashing.
    /// </summary>
    internal static IEnumerable<string> Files(string scriptsRoot)
    {
        string scripts = Path.GetFullPath(scriptsRoot);
        var live = Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories);
        var view = Current.Value;
        return view is not null &&
               scripts.Equals(view.GameScripts, StringComparison.OrdinalIgnoreCase)
            ? live.Concat(view.MissingBundleFiles)
            : live;
    }

    private static bool IsInside(string child, string parent) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
