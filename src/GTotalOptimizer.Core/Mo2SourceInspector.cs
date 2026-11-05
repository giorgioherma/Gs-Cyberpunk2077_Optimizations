namespace GTotalOptimizer.Core;

// Reads MO2's physical loose-file layers without copying or modifying installed mods.
// MO2 launch/USVFS, archive precedence and RootBuilder mappings require separate runtime proof.
public sealed record Mo2Instance(
    bool Recognized, string Detail, string ModsDirectory, string ProfilesDirectory,
    string OverwriteDirectory, IReadOnlyList<string> Profiles,
    string? PreferredProfile = null);

public sealed record Mo2WinningFile(string GameRelativePath, string PhysicalPath, string Owner,
    int Priority, bool FromMo2);

public sealed record Mo2Stack(
    bool Ready, string Detail, string Profile,
    int EnabledPackages, int CetOwners, int RedscriptFiles, int Red4extFiles,
    int ShadowedFiles, bool HasZeroEngine, bool HasRedscript,
    IReadOnlyDictionary<string, Mo2WinningFile> EffectiveFiles,
    IReadOnlyList<string> Warnings,
    bool HasCetAsi = false)
{
    public string? Winner(string relative) =>
        EffectiveFiles.TryGetValue(relative.Replace('\\', '/'), out var file) ? file.PhysicalPath : null;
}

public static class Mo2SourceInspector
{
    private const int MaximumScannedFiles = 250_000;

    public static Mo2Instance Discover(string? selectedInstance, string? modsDirectoryOverride = null)
    {
        if (string.IsNullOrWhiteSpace(selectedInstance))
            return Failed("Select an MO2 instance containing ModOrganizer.ini.");
        try
        {
            var instance = Path.GetFullPath(selectedInstance.Trim().Trim('"'));
            var ini = Path.Combine(instance, "ModOrganizer.ini");
            if (!File.Exists(ini)) return Failed("ModOrganizer.ini not found in the selected MO2 instance.");
            var values = ReadIni(ini);
            var baseDir = ResolveSetting(instance, instance, values, "base_directory", ".");
            var mods = string.IsNullOrWhiteSpace(modsDirectoryOverride)
                ? ResolveSetting(instance, baseDir, values, "mod_directory", "mods")
                : Path.GetFullPath(modsDirectoryOverride.Trim().Trim('"'));
            var profiles = ResolveSetting(instance, baseDir, values, "profiles_directory", "profiles");
            var overwrite = ResolveSetting(instance, baseDir, values, "overwrite_directory", "overwrite");
            var selectedProfile = ReadProfileSelection(ini);
            var choices = Directory.Exists(profiles)
                ? Directory.EnumerateDirectories(profiles)
                    .Where(folder => File.Exists(Path.Combine(folder, "modlist.txt")))
                    .Select(folder => Path.GetFileName(folder))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            if (!Directory.Exists(mods))
                return new(false, "MO2 mods directory missing: " + mods, mods, profiles, overwrite, choices, selectedProfile);
            if (choices.Length == 0)
                return new(false, "No profiles with modlist.txt found under " + profiles, mods, profiles, overwrite, choices, selectedProfile);
            return new(true, $"MO2 paths found; {choices.Length} profile(s). Select the actual launched profile.",
                mods, profiles, overwrite, choices, selectedProfile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return Failed("Cannot read MO2 paths: " + ex.Message);
        }
    }

    // A profile is explicit, never inferred from the mere presence of a mod in MO2 storage.
    // The source map is a *candidate loose-file view*, NOT proof that Cyberpunk launched
    // under the selected MO2 profile or that RootBuilder/virtual paths match this layout.
    public static Mo2Stack Resolve(string? gameRoot, Mo2Instance instance, string? profile, CancellationToken token = default)
    {
        var empty = new Dictionary<string, Mo2WinningFile>(StringComparer.OrdinalIgnoreCase);
        Mo2Stack Refuse(string reason) => new(false, reason, profile ?? "", 0, 0, 0, 0, 0,
            false, false, empty, [reason]);

        if (!instance.Recognized) return Refuse(instance.Detail);
        if (string.IsNullOrWhiteSpace(profile) || !instance.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            return Refuse("Select a profile found in the MO2 instance.");
        if (string.IsNullOrWhiteSpace(gameRoot) ||
            !File.Exists(Path.Combine(gameRoot, "bin", "x64", "Cyberpunk2077.exe")))
            return Refuse("MO2 needs the actual Cyberpunk game root for mixed game/MO2 source resolution.");

        try
        {
            var profileDir = Path.Combine(instance.ProfilesDirectory, profile);
            var entries = ReadEnabledMods(Path.Combine(profileDir, "modlist.txt"));
            if (entries.Count == 0)
                return Refuse("Profile has no enabled package entries in modlist.txt.");
            var warnings = new List<string>();
            var winners = new Dictionary<string, Mo2WinningFile>(StringComparer.OrdinalIgnoreCase);
            var shadows = 0;
            var scanned = 0;

            // Lowest precedence: game's physical files, including separately installed CET.
            IndexLayer(gameRoot, "(physical game)", -1, false, winners, ref scanned, ref shadows, token);
            // MO2 modlist.txt is highest-to-lowest. Reverse to let highest priority win last.
            var missingPackages = 0;
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                var modName = entries[i];
                var folder = Path.GetFullPath(Path.Combine(instance.ModsDirectory, modName));
                if (!IsChildPath(instance.ModsDirectory, folder))
                {
                    warnings.Add("Rejected unsafe modlist path outside MO2 mods root: " + modName);
                    continue;
                }
                if (!Directory.Exists(folder))
                {
                    warnings.Add("Enabled MO2 package not found: " + modName);
                    missingPackages++;
                    continue;
                }
                IndexLayer(folder, modName, entries.Count - i, true, winners, ref scanned, ref shadows, token);
            }

            // Never claim a usable stack if enabled package inventory is incomplete.
            if (missingPackages > 0)
                return Refuse($"{missingPackages} enabled MO2 package(s) could not be located. Check configured Mods directory.");

            // MO2 Overwrite loose files have the highest nominal precedence.
            if (Directory.Exists(instance.OverwriteDirectory))
                IndexLayer(instance.OverwriteDirectory, "(MO2 Overwrite)", int.MaxValue,
                    true, winners, ref scanned, ref shadows, token);

            var paths = winners.Keys.ToArray();
            var cetNames = paths
                .Where(IsCetInit)
                .Select(s => s.Split('/')[5])
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var redCount = paths.Count(p => p.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase)
                                          && p.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));
            var nativeCount = paths.Count(p => p.StartsWith("red4ext/plugins/", StringComparison.OrdinalIgnoreCase)
                                            && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
            var engine = winners.ContainsKey("bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua");
            var hasRed = redCount > 0;
            var asi = winners.ContainsKey("bin/x64/plugins/cyber_engine_tweaks.asi") ||
                winners.ContainsKey("bin/x64/plugins/cet.asi");
            warnings.Add("Loose-file priority only: verify MO2 launch, archive mapping, hidden file overrides, and RootBuilder/virtualized root plugins before generation.");
            return new(true, $"Resolved physical loose-file candidates from {entries.Count} enabled MO2 packages. Runtime VFS unverified.",
                profile, entries.Count, cetNames, redCount, nativeCount, shadows, engine,
                hasRed, winners, warnings, asi);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidDataException)
        {
            return Refuse("MO2 source resolution blocked: " + ex.Message);
        }
    }

    private static void IndexLayer(string root, string owner, int priority, bool fromMo2,
        Dictionary<string, Mo2WinningFile> winners, ref int scanned, ref int shadows,
        CancellationToken token)
    {
        var paths = new[]
        {
            Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks", "mods"),
            Path.Combine("r6", "scripts"),
            Path.Combine("red4ext", "plugins")
        };
        // CET's native ASI may be game-installed or in an enabled MO2 package.
        // RootBuilder-specific mappings remain unproven and never get inferred.
        var nativeRoot = Path.Combine(root, "bin", "x64", "plugins");
        foreach (var asiName in new[] { "cyber_engine_tweaks.asi", "cet.asi" })
        {
            token.ThrowIfCancellationRequested();
            var asiFile = Path.Combine(nativeRoot, asiName);
            if (!File.Exists(asiFile)) continue;
            if (++scanned > MaximumScannedFiles)
                throw new InvalidDataException("Too many files for bounded MO2 source inventory.");
            var nativeRel = Path.GetRelativePath(root, asiFile).Replace('\\', '/');
            if (winners.ContainsKey(nativeRel)) shadows++;
            winners[nativeRel] = new(nativeRel, asiFile, owner, priority, fromMo2);
        }
        foreach (var path in paths)
        {
            var directory = Path.Combine(root, path);
            if (!Directory.Exists(directory)) continue;
            foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
            {
                token.ThrowIfCancellationRequested();
                if (++scanned > MaximumScannedFiles)
                    throw new InvalidDataException("Too many files for bounded MO2 source inventory.");
                if (file.EndsWith(".mohidden", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (!IsSafeRelative(relative)) continue;
                if (winners.ContainsKey(relative)) shadows++;
                winners[relative] = new(relative, file, owner, priority, fromMo2);
            }
        }
    }

    private static bool IsCetInit(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 7 &&
            parts[0].Equals("bin", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Equals("x64", StringComparison.OrdinalIgnoreCase) &&
            parts[2].Equals("plugins", StringComparison.OrdinalIgnoreCase) &&
            parts[3].Equals("cyber_engine_tweaks", StringComparison.OrdinalIgnoreCase) &&
            parts[4].Equals("mods", StringComparison.OrdinalIgnoreCase) &&
            parts[6].Equals("init.lua", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSafeRelative(string relative) =>
        !Path.IsPathRooted(relative) && !relative.Split('/').Any(x => x is ".." or ".");

    private static bool IsChildPath(string parent, string child) =>
        child.StartsWith(Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) +
                         Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static List<string> ReadEnabledMods(string modlist)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var entries = new List<string>();
        foreach (var raw in File.ReadLines(modlist))
        {
            var line = raw.Trim();
            if (!line.StartsWith('+') || line.Length < 2) continue;
            var name = line[1..].Trim();
            if (name.Length == 0 || name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase) ||
                name.Contains('/') || name.Contains('\\') || name == ".." || name == "." ||
                name.Contains(':') || name.Contains('\0')) continue;
            if (seen.Add(name)) entries.Add(name);
        }
        return entries;
    }

    private static string? ReadProfileSelection(string ini)
    {
        var section = "";
        foreach (var raw in File.ReadLines(ini))
        {
            var line = raw.Trim();
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                section = line[1..^1];
                continue;
            }
            if (section is not ("General" or "Settings")) continue;
            var equal = line.IndexOf('=');
            if (equal <= 0 || !line[..equal].Trim()
                .Equals("selected_profile", StringComparison.OrdinalIgnoreCase)) continue;
            return line[(equal + 1)..].Trim().Trim('"');
        }
        return null;
    }

    private static Dictionary<string, string> ReadIni(string path)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is ';' or '#') continue;
            if (line.StartsWith('[') && line.EndsWith(']')) { section = line[1..^1]; continue; }
            var equal = line.IndexOf('=');
            if (equal <= 0 || !section.Equals("Settings", StringComparison.OrdinalIgnoreCase)) continue;
            values[line[..equal].Trim()] = line[(equal + 1)..].Trim().Trim('"');
        }
        return values;
    }

    private static string ResolveSetting(string instance, string baseDir,
        IReadOnlyDictionary<string, string> values, string key, string fallback)
    {
        var value = values.TryGetValue(key, out var chosen) && !string.IsNullOrWhiteSpace(chosen)
            ? chosen : fallback;
        value = value.Replace("%BASE_DIR%", baseDir, StringComparison.OrdinalIgnoreCase)
                     .Replace("%INSTANCE_DIR%", instance, StringComparison.OrdinalIgnoreCase);
        value = Environment.ExpandEnvironmentVariables(value);
        if (value.Contains('%')) throw new InvalidDataException("Unresolved MO2 path variable for " + key);
        return Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDir, value));
    }

    private static Mo2Instance Failed(string detail) =>
        new(false, detail, "", "", "", []);
}
