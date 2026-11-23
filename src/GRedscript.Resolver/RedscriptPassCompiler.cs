using System.Security.Cryptography;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Validates the generated effective REDscript tree with the existing
/// official SCC 0.5.31 native ABI. Every compiler write is external to the
/// game; unchanged vanilla cache bytes are copied, never edited in place.
/// </summary>
public static class RedscriptPassCompiler
{
    public const string NativeCompilerSha256 =
        "49EB60A3613C990CD13B6136E827A74F9F64AD68E2F911D8C93E4E3D3F53C100";

    public static void Verify(string gameRoot, string overlayRoot,
        string isolatedRoot, string officialNativeCompiler,
        string expectedSourceStackSha256)
    {
        string game = Path.GetFullPath(gameRoot);
        string overlay = Path.GetFullPath(overlayRoot);
        string isolated = Path.GetFullPath(isolatedRoot);
        string compiler = Path.GetFullPath(officialNativeCompiler);
        if (Directory.Exists(isolated) || File.Exists(isolated) ||
            Inside(isolated, game) || Inside(isolated, overlay) ||
            Inside(game, isolated) || Inside(overlay, isolated) ||
            !File.Exists(compiler) ||
            !Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(compiler)))
                .Equals(NativeCompilerSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GCO_SCC_ISOLATION_OR_NATIVE_COMPILER_INVALID");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        if (!string.Equals(admission.SourceStackSha256, expectedSourceStackSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GCO_SCC_SOURCE_DRIFT_BEFORE_COPY");

        string cache = Path.Combine(game, "r6", "cache", "final.redscripts");
        string originalScripts = Path.Combine(game, "r6", "scripts");
        string stagedScripts = Path.Combine(overlay, "r6", "scripts");
        if (!File.Exists(cache) || !Directory.Exists(originalScripts) ||
            !Directory.Exists(stagedScripts))
            throw new InvalidDataException("GCO_SCC_GAME_SOURCE_CACHE_OR_OVERLAY_MISSING");

        string workspace = Path.Combine(isolated, "source");
        string sandboxR6 = Path.Combine(workspace, "r6");
        string scripts = Path.Combine(sandboxR6, "scripts");
        string sandboxCache = Path.Combine(sandboxR6, "cache", "final.redscripts");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(Path.GetDirectoryName(sandboxCache)!);
        File.WriteAllText(Path.Combine(workspace, ".grsp-r6-workspace"),
            "GRSP_R6_ISOLATED_WORKSPACE");
        CopyTree(originalScripts, scripts, allowReplace: false);
        CopyTree(stagedScripts, scripts, allowReplace: true);
        File.Copy(cache, sandboxCache);

        string nativePluginRoot = Path.Combine(game, "red4ext", "plugins");
        string sandboxPluginRoot = Path.Combine(workspace, "red4ext", "plugins");
        if (Directory.Exists(nativePluginRoot))
        {
            CopyTree(nativePluginRoot, sandboxPluginRoot,
                allowReplace: false, onlyReds: true);
        }
        bool external = Directory.Exists(sandboxPluginRoot) &&
            Directory.EnumerateFiles(sandboxPluginRoot, "*.reds",
                SearchOption.AllDirectories).Any();

        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GCO_SCC_SOURCE_CHANGED_DURING_COPY");

        string result = Path.Combine(isolated, "scc-result.json");
        string log = Path.Combine(isolated, "scc-errors.txt");
        var options = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["--workspace"] = workspace, ["--r6"] = sandboxR6,
            ["--cache"] = sandboxCache, ["--compiled"] = sandboxCache + ".modded",
            ["--scc-native"] = compiler, ["--out"] = result, ["--log"] = log
        };
        if (external) options["--external-scripts"] = sandboxPluginRoot;
        int exit = HeadlessScc.Run(options);
        if (exit != 0 || !File.Exists(result))
            throw new InvalidDataException("GCO_REDSCRIPT_SCC_COMPILATION_FAILED");
        using var evidence = JsonDocument.Parse(File.ReadAllText(result));
        var root = evidence.RootElement;
        if (!root.GetProperty("success").GetBoolean() ||
            !root.GetProperty("originalCacheUnchanged").GetBoolean() ||
            !root.GetProperty("generatedOutputPresent").GetBoolean())
            throw new InvalidDataException("GCO_REDSCRIPT_SCC_COMPILER_PROOF_INVALID");

        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GCO_SCC_SOURCE_DRIFT_AFTER_COMPILE");
    }

    private static bool Inside(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith(root + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase);
    }

    private static void CopyTree(string source, string destination,
        bool allowReplace, bool onlyReds = false)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("GCO_SCC_LINKED_DIRECTORY_REFUSED");
        Directory.CreateDirectory(destination);
        foreach (var path in Directory.EnumerateFiles(source, "*",
                     SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_SCC_LINKED_FILE_REFUSED");
            if (onlyReds && !path.EndsWith(".reds",
                    StringComparison.OrdinalIgnoreCase))
                continue;
            string target = Path.Combine(destination, Path.GetFileName(path));
            File.Copy(path, target, overwrite: allowReplace);
        }
        foreach (var path in Directory.EnumerateDirectories(source, "*",
                     SearchOption.TopDirectoryOnly))
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("GCO_SCC_LINKED_DIRECTORY_REFUSED");
            CopyTree(path, Path.Combine(destination, Path.GetFileName(path)),
                allowReplace, onlyReds);
        }
    }
}
