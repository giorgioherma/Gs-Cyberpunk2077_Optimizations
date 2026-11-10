using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace GTotalOptimizer.Core;

public sealed record FrameworkEvidence(bool Present, string? Version, bool StructureCompatible = true,
    string? Diagnostic = null);
public sealed record GameFrameworkEvidence(
    FrameworkEvidence Game, FrameworkEvidence Cet, FrameworkEvidence ZeroEngine,
    FrameworkEvidence Red4ext, FrameworkEvidence Redscript);
public sealed record FrameworkPreflightResult(IReadOnlyList<string> Issues)
{
    public bool Ready => Issues.Count == 0;
    public string Detail => Ready ? "Cyberpunk 2.31 frameworks verified." : string.Join("  |  ", Issues);
}

// This deliberately small list is for reporting known redscript scc.exe builds,
// not admitting older/newer compiler versions. Hashes of modified/self-built
// binaries remain UNKNOWN rather than being guessed from metadata or filenames.
public static class RedscriptCompilerFingerprints
{
    public const string Official031Exe =
        "DA2C0DE580173010C907A05FDD2F3CD2430D6E62BF748339D65060118AE32EDD";
    public const string Official031Library =
        "49EB60A3613C990CD13B6136E827A74F9F64AD68E2F911D8C93E4E3D3F53C100";

    public static string? IdentifyExecutable(string? sha256) => sha256?.ToUpperInvariant() switch
    {
        "CE40C4663E3E20DDD0BF8173DEC993699C597832D09F87636EDAADEAEB6D344B" => "0.5.28",
        "8FE63C150ABCDD04E67419F3B144CC0652761C3B636D2978438721609ABB93F7" => "0.5.30",
        Official031Exe => "0.5.31",
        "F7BF0A5A0986AEC61D360AE79A3E957B8E21C0781309193A2A056C7BDE0E90BC" => "0.5.32",
        _ => null
    };
}

// Source-based admission: a folder alone is never proof of framework compatibility.
public static class FrameworkPreflight
{
    private static readonly Regex Token = new(
        @"(?<![0-9])v?(?<number>[0-9]+\.[0-9]+(?:\.[0-9]+){0,2})(?![0-9])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));
    private static readonly Regex ModVersion = new(
        @"(?im)(?:\bversion\b|\bmodVersion\b)\s*(?:=|:)\s*['""]?v?(?<number>[0-9]+\.[0-9]+\.[0-9]+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(200));

    private static int[]? Parts(string? version)
    {
        if (string.IsNullOrWhiteSpace(version)) return null;
        Match match;
        try { match = Token.Match(version); }
        catch (RegexMatchTimeoutException) { return null; }
        if (!match.Success) return null;
        var raw = match.Groups["number"].Value.Split('.');
        if (raw.Length < 2 || raw.Length > 4) return null;
        var p = new int[4];
        for (var i = 0; i < raw.Length; i++)
            if (!int.TryParse(raw[i], out p[i])) return null;
        return p;
    }
    private static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < 4; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        return 0;
    }
    private static void Check(FrameworkEvidence evidence, string name, string target,
        IList<string> failures, bool minimum = false, bool game = false,
        bool checkStructure = true)
    {
        if (!evidence.Present)
        {
            failures.Add(game ? "Cyberpunk 2077 2.31 is required" : name + " " + target + " is missing");
            return;
        }
        if (checkStructure && !evidence.StructureCompatible)
        {
            failures.Add(name + " required profiling interfaces are missing or incompatible");
            return;
        }
        if (!string.IsNullOrWhiteSpace(evidence.Diagnostic))
        {
            failures.Add(evidence.Diagnostic);
            return;
        }
        var found = Parts(evidence.Version);
        if (found is null)
        {
            failures.Add(name + " compatibility could not be verified");
            return;
        }
        var required = minimum ? new[] { 0, 18, 0, 0 } :
            game ? new[] { 2, 3, 1, 0 } : Parts(target)!;
        var valid = minimum ? Compare(found, required) >= 0 : Compare(found, required) == 0;
        if (game && evidence.Version?.Trim() == "2.31") valid = true;
        if (!valid)
            failures.Add(game ? "Cyberpunk 2077 2.31 is required; found " + evidence.Version :
                name + " requires " + target + "; found " + evidence.Version);
    }

    public static FrameworkPreflightResult Evaluate(GameFrameworkEvidence installed)
    {
        var problems = new List<string>();
        Check(installed.Game, "Cyberpunk 2077", "2.31", problems, game: true);
        Check(installed.Cet, "CET", "1.37.1", problems);
        // GCO checks 0-Engine admission by installed version only. CET's
        // Resolver/Generator exclusively owns source/injection compatibility
        // and must decide whether it can generate an optimization.
        Check(installed.ZeroEngine, "0-Engine", "0.18.0 or greater", problems,
            minimum: true, checkStructure: false);
        Check(installed.Red4ext, "RED4ext", "1.30.0", problems);
        Check(installed.Redscript, "redscript", "0.5.31", problems);
        return new(problems);
    }

    private static string? PeVersion(string path, bool productOnly = false)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            if (Parts(info.ProductVersion) is not null) return info.ProductVersion;
            if (!productOnly && Parts(info.FileVersion) is not null) return info.FileVersion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return null;
    }

    // Bounded, read-only SHA-256 fingerprinting; never invokes the compiler.
    private static string? FileSha256(string path)
    {
        try
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (file.Length == 0 || file.Length > 16 * 1024 * 1024) return null;
            return Convert.ToHexString(SHA256.HashData(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? EngineVersion(string folder)
    {
        foreach (var file in new[] { "init.lua", "version.lua", "manifest.json", "mod.json" })
        {
            var path = Path.Combine(folder, file);
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length > 262144) continue;
                var m = ModVersion.Match(File.ReadAllText(path));
                if (m.Success) return m.Groups["number"].Value;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or RegexMatchTimeoutException) { }
        }
        return null;
    }

    // redscript is the standalone script compiler, NOT a RED4ext plugin.
    // Upstream 0.5.31 package: engine/tools/scc.exe + scc_lib.dll, and
    // engine/config/base/scripts.ini. Read-only: do not execute the compiler
    // to determine its version (that could compile/change the user's game).
    public static FrameworkEvidence InspectRedscript(string gameRoot)
    {
        var tools = Path.Combine(gameRoot, "engine", "tools");
        var compiler = Path.Combine(tools, "scc.exe");
        var library = Path.Combine(tools, "scc_lib.dll");
        var alternateLibrary = Path.Combine(tools, "scc.dll");
        var config = Path.Combine(gameRoot, "engine", "config", "base", "scripts.ini");

        var hasExe = File.Exists(compiler);
        var hasLibrary = File.Exists(library);
        var hasAlternate = File.Exists(alternateLibrary);
        var hasConfig = File.Exists(config);
        // Recognize only known executable fingerprints for console diagnostics.
        // Installation acceptance still requires the exact official 0.5.31
        // executable AND matching official companion library and scripts.ini.
        var shape = hasExe && hasLibrary && hasConfig;
        var version = hasExe
            ? RedscriptCompilerFingerprints.IdentifyExecutable(FileSha256(compiler))
            : null;
        var officialPair = version == "0.5.31" &&
            FileSha256(library) == RedscriptCompilerFingerprints.Official031Library;
        var mismatch = shape && version == "0.5.31" && !officialPair;
        return new(
            hasExe || hasLibrary || hasAlternate || hasConfig,
            mismatch ? null : version,
            shape,
            mismatch ? "redscript 0.5.31 scc.exe recognized, but scc_lib.dll does not match the official 0.5.31 release. Reinstall matching redscript files." : null);
    }

    public static FrameworkPreflightResult Inspect(string gameRoot)
    {
        var exe = Path.Combine(gameRoot, "bin", "x64", "Cyberpunk2077.exe");
        var cet = Path.Combine(gameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks.asi");
        var red4 = Path.Combine(gameRoot, "red4ext", "RED4ext.dll");
        var engine = Path.Combine(gameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "0-Engine");
        return Evaluate(new(
            new(File.Exists(exe), PeVersion(exe, productOnly: true)),
            new(File.Exists(cet), PeVersion(cet)),
            new(File.Exists(Path.Combine(engine, "init.lua")), EngineVersion(engine)),
            new(File.Exists(red4), PeVersion(red4)),
            InspectRedscript(gameRoot)));
    }
}
