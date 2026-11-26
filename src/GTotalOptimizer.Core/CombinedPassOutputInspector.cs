using System.IO.Compression;

namespace GTotalOptimizer.Core;

/// <summary>Final independently enforced ZIP contract for Vortex overlay use.</summary>
public static class CombinedPassOutputInspector
{
    internal const string LegacyNativeDll = "red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll";
    internal const string LegacyNativeManifest = "red4ext/plugins/G-CET-StaticLocations/G-CET.locations";
    internal const string NativeDll = "red4ext/plugins/G-NativeRuntime/G-Engine.dll";
    internal const string NativeManifest = "red4ext/plugins/G-NativeRuntime/G-Native.locations";

    internal static bool IsCanonicalNativePath(string path) =>
        path.Equals(NativeDll,StringComparison.OrdinalIgnoreCase) ||
        path.Equals(NativeManifest,StringComparison.OrdinalIgnoreCase);

    internal static bool IsNativeRuntimePath(string path) =>
        IsCanonicalNativePath(path) ||
        path.Equals(LegacyNativeDll,StringComparison.OrdinalIgnoreCase) ||
        path.Equals(LegacyNativeManifest,StringComparison.OrdinalIgnoreCase);

    // Either complete native owner pair or neither. Never accept a mixed
    // historical/canonical layout or a DLL with a mismatched companion file.
    internal static void VerifyNativePair(IReadOnlySet<string> paths)
    {
        bool modernDll=paths.Contains(NativeDll), modernData=paths.Contains(NativeManifest);
        bool oldDll=paths.Contains(LegacyNativeDll), oldData=paths.Contains(LegacyNativeManifest);
        if(modernDll!=modernData || oldDll!=oldData || (modernDll&&oldDll))
            throw new InvalidDataException("GCO_PASS_NATIVE_OWNER_LAYOUT_CONFLICT");
    }

    // The same game-only allowlist is used while staging source-owner output
    // and after writing the ZIP. No separate corrective packaging pass.
    public static bool AllowedRuntimePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') ||
            path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(x => x is "" or "." or ".."))
            return false;
        if (IsNativeRuntimePath(path)) return true;
        string file = Path.GetFileName(path);
        // Preserve optional experimental native-location source in GitHub,
        // never leak its generated adapters/backend into the default ZIP.
        if (file.StartsWith("GCO_NativeLocation_", StringComparison.OrdinalIgnoreCase) ||
            file.Equals("GCETNativeLocationBackend.reds", StringComparison.OrdinalIgnoreCase))
            return false;
        if (new[] { "README", "LICENSE", "LICENCE", "CHANGELOG",
                    "HANDOFF", "CONTRIBUTORS", "NOTICE" }
            .Any(prefix => file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) ||
            new[] { ".exe", ".pdb", ".dll", ".md", ".csv", ".html",
                    ".log", ".ps1", ".zip", ".bak", ".vortex_backup" }
            .Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            return false;

        if (path.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase))
            return path.EndsWith(".reds", StringComparison.OrdinalIgnoreCase);
        return path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/",
                StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/CETProfiler", StringComparison.OrdinalIgnoreCase) &&
               !path.Contains("/G-REDscript-Profiler", StringComparison.OrdinalIgnoreCase);
    }

    public static int Validate(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (var item in zip.Entries)
        {
            string path = item.FullName;
            if (item.Length < 1 || !AllowedRuntimePath(path) || !seen.Add(path))
                throw new InvalidDataException("GCO_PASS_ARCHIVE_UNSAFE_OR_NONRUNTIME_ENTRY");
            files++;
        }
        VerifyNativePair(seen);
        if (files == 0)
            throw new InvalidDataException("GCO_PASS_ARCHIVE_EMPTY");
        return files;
    }
}
