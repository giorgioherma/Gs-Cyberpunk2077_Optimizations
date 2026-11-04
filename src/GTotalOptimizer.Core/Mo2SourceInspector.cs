namespace GTotalOptimizer.Core;

/// <summary>Read-only physical-file projection of one selected MO2 profile.
/// Does not claim that the running game's VFS or RootBuilder deployment has been verified.</summary>
public sealed record Mo2SourceFile(string GameRelativePath, string PhysicalPath, string Owner);
public sealed record Mo2Inspection(bool Valid, string Detail, string? ProfileName,
    int EnabledPackages, IReadOnlyList<Mo2SourceFile> EffectiveFiles,
    IReadOnlyList<string> Warnings)
{
    public int CetModCount => EffectiveFiles
        .Where(x => x.GameRelativePath.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/", StringComparison.OrdinalIgnoreCase)
                 && x.GameRelativePath.EndsWith("/init.lua", StringComparison.OrdinalIgnoreCase))
        .Select(x => x.GameRelativePath[..^"/init.lua".Length])
        .Distinct(StringComparer.OrdinalIgnoreCase).Count();
    public bool HasZeroEngine => EffectiveFiles.Any(x =>
        x.GameRelativePath.Equals("bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua", StringComparison.OrdinalIgnoreCase));
    public bool HasRedscript => EffectiveFiles.Any(x => x.GameRelativePath.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase));
    public bool HasRed4ext => EffectiveFiles.Any(x => x.GameRelativePath.StartsWith("red4ext/plugins/", StringComparison.OrdinalIgnoreCase));
}

public static class Mo2SourceInspector
{
    private static readonly string[] Prefixes =
    [
        "bin/x64/plugins/cyber_engine_tweaks/mods/",
        "r6/scripts/",
        "red4ext/plugins/"
    ];

    public static Mo2Inspection Inspect(string? instanceDirectory, string? profileOverride = null, string? gameRoot = null)
    {
        var warnings = new List<string>();
        var files = new Dictionary<string, Mo2SourceFile>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(instanceDirectory) || !Directory.Exists(instanceDirectory))
            return Fail("Select the MO2 instance folder that contains ModOrganizer.ini.");

        try
        {
            var instance = Path.GetFullPath(instanceDirectory);
            var iniFile = Path.Combine(instance, "ModOrganizer.ini");
            if (!File.Exists(iniFile))
                return Fail("ModOrganizer.ini not found in the selected MO2 instance folder.");

            var ini = ReadIni(iniFile);
            string? Value(params string[] keys)
            {
                foreach (var key in keys)
                    if (ini.TryGetValue(key, out var found) && !string.IsNullOrWhiteSpace(found)) return found;
                return null;
            }
            var baseDir = Resolve(Value("Settings/base_directory", "General/base_directory",
                "Settings/baseDirectory", "General/baseDirectory") ?? instance, instance, instance, gameRoot);
            if (baseDir is null) return Fail("MO2 base_directory contains unresolved variables.");
            string? Map(string? val, string fallback) => Resolve(val ?? fallback, instance, baseDir, gameRoot);
            var mods = Map(Value("Settings/mod_directory", "Settings/mods_directory",
                "General/mod_directory", "General/mods_directory"), "mods");
            var profiles = Map(Value("Settings/profiles_directory", "General/profiles_directory"), "profiles");
            var overwrite = Map(Value("Settings/overwrite_directory", "General/overwrite_directory"), "overwrite");
            if (mods is null || profiles is null || overwrite is null)
                return Fail("MO2 path variables could not be resolved. Check MO2 instance settings.");

            var chosen = profileOverride;
            if (string.IsNullOrWhiteSpace(chosen))
                chosen = Value("General/selected_profile", "Settings/selected_profile",
                    "General/current_profile", "Settings/current_profile");
            if (string.IsNullOrWhiteSpace(chosen))
            {
                if (!Directory.Exists(profiles)) return Fail("MO2 profiles directory not found.");
                var found = Directory.EnumerateDirectories(profiles)
                    .Where(p => File.Exists(Path.Combine(p, "modlist.txt"))).Take(2).ToArray();
                if (found.Length != 1)
                    return Fail("Select the active MO2 profile explicitly; automatic active-profile detection is ambiguous.");
                chosen = Path.GetFileName(found[0]);
            }
            if (Path.GetFileName(chosen) != chosen || chosen is "." or "..")
                return Fail("Invalid MO2 profile name.");

            var modlist = Path.Combine(profiles, chosen, "modlist.txt");
            if (!File.Exists(modlist)) return Fail("Selected MO2 profile has no modlist.txt.");
            if (!Directory.Exists(mods)) return Fail("MO2 configured mods directory is missing.");

            var enabled = new List<string>();
            foreach (var raw in File.ReadLines(modlist))
            {
                var line = raw.Trim().TrimStart('\uFEFF');
                if (!line.StartsWith("+", StringComparison.Ordinal)) continue;
                var name = line[1..].Trim();
                if (name.Length == 0 || Path.GetFileName(name) != name || name is "." or "..")
                    return Fail("MO2 modlist has an unsafe enabled package name.");
                enabled.Add(name);
            }
            if (enabled.Count == 0) warnings.Add("No enabled packages found in selected MO2 profile.");
            foreach (var package in enabled)
            {
                var root = Path.Combine(mods, package);
                if (!Directory.Exists(root))
                {
                    warnings.Add($"Enabled package missing: {package}");
                    continue;
                }
                Collect(root, package, files, warnings);
            }
            if (Directory.Exists(overwrite))
                Collect(overwrite, "MO2 Overwrite", files, warnings);

            warnings.Add("Physical MO2 projection only: VFS, separators, plugins, RootBuilder and live load order are not runtime-verified.");
            return new(true, $"MO2 profile '{chosen}': {enabled.Count} enabled packages; {files.Count} effective CET/REDscript/RED4ext files.", chosen,
                enabled.Count, files.Values.OrderBy(x => x.GameRelativePath, StringComparer.OrdinalIgnoreCase).ToArray(), warnings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Fail("Cannot read MO2 instance safely: " + ex.Message);
        }

        Mo2Inspection Fail(string reason) => new(false, reason, null, 0, [], [reason]);
    }

    private static void Collect(string root, string owner, Dictionary<string, Mo2SourceFile> files, List<string> warnings)
    {
        try
        {
            foreach (var path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (Prefixes.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                    files[relative] = new(relative, path, owner); // MO2 later priority and Overwrite win.
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            warnings.Add($"Could not fully inspect {owner}: {ex.Message}");
        }
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim().TrimStart('\uFEFF');
            if (line.StartsWith("[") && line.EndsWith("]"))
            {
                section = line[1..^1].Trim();
                continue;
            }
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            map[section + "/" + line[..eq].Trim()] = line[(eq + 1)..].Trim().Trim('"');
        }
        return map;
    }

    private static string? Resolve(string raw, string instance, string baseDir, string? gameRoot)
    {
        var value = raw.Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase)
            .Replace("%INSTANCE_DIR%", instance, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(gameRoot))
            value = value.Replace("%GAME_DIR%", gameRoot, StringComparison.OrdinalIgnoreCase);
        if (value.Contains('%')) return null; // Unrecognized macro: fail closed instead of guessing.
        value = value.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value));
    }
}
