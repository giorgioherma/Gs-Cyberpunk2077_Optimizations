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
    bool HasCetAsi = false, bool ProfileVerified = true, bool GameOnlyFallback = false)
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
                    .Select(folder => Path.GetFileName(folder))
                    .Where(name => !string.IsNullOrWhiteSpace(name))
                    .Select(name => name!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            // No modlist is not permission to read MO2 packages, but the live
            // game remains a valid source and the profile remains selectable.
            var detail = !Directory.Exists(profiles)
                ? "Profiles directory missing; live game remains available."
                : !Directory.Exists(mods)
                    ? "MO2 Mods directory missing; enabled packages cannot be resolved."
                    : $"MO2 paths found; {choices.Length} profile(s). Select the launched profile.";
            return new(true, detail, mods, profiles, overwrite, choices, selectedProfile);
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
        if (!IsValidGameRoot(gameRoot))
            return Refuse("Live Cyberpunk game root required.", profile);
        if (!instance.Recognized || string.IsNullOrWhiteSpace(profile) ||
            !instance.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            return ResolveGameOnly(gameRoot, "MO2 profile unverified; physical game scanned without MO2.", profile, token);

        var modlist = Path.Combine(instance.ProfilesDirectory, profile, "modlist.txt");
        if (!File.Exists(modlist))
            return ResolveGameOnly(gameRoot,
                "modlist.txt absent: no MO2 packages or Overwrite; only physical game files.", profile, token);
        try
        {
            var entries = ReadEnabledMods(modlist);
            if (entries.Count > 0 && !Directory.Exists(instance.ModsDirectory))
                return ResolveGameOnly(gameRoot, "Configured MO2 Mods directory missing; enabled MO2 files excluded.", profile, token);
            var winners = new Dictionary<string, Mo2WinningFile>(StringComparer.OrdinalIgnoreCase);
            var shadows = 0;
            var scanned = 0;
            var missingPackages = 0;

            // The physical game is always lowest priority, never discarded.
            IndexLayer(gameRoot!, "(physical game)", -1, false, winners, ref scanned, ref shadows, token);
            // MO2 modlist is highest-priority first; reverse so higher priority overwrites lower.
            for (var i = entries.Count - 1; i >= 0; i--)
            {
                token.ThrowIfCancellationRequested();
                var name = entries[i];
                var folder = Path.GetFullPath(Path.Combine(instance.ModsDirectory, name));
                if (!IsChildPath(instance.ModsDirectory, folder))
                    return ResolveGameOnly(gameRoot, "Unsafe enabled MO2 package path; no MO2 packages trusted.", profile, token);
                if (!Directory.Exists(folder))
                {
                    missingPackages++;
                    continue;
                }
                IndexLayer(folder, name, entries.Count - i, true,
                    winners, ref scanned, ref shadows, token);
            }
            if (missingPackages != 0)
                return ResolveGameOnly(gameRoot, $"{missingPackages} enabled MO2 package(s) not found; all MO2 layers excluded.", profile, token);

            // Overwrite is MO2-managed; include only after a real modlist was read.
            if (Directory.Exists(instance.OverwriteDirectory))
                IndexLayer(instance.OverwriteDirectory, "(MO2 Overwrite)", int.MaxValue,
                    true, winners, ref scanned, ref shadows, token);

            var caveat = "Loose-file precedence only; MO2 USVFS, RootBuilder, archive and file-exception loading remain unverified.";
            return Build(profile, entries.Count, winners, shadows, [caveat],
                profileVerified: true, gameOnlyFallback: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or InvalidDataException)
        {
            // Missing file is a special game-only state; corrupt/unreadable files
            // cannot be trusted to reconstruct the enabled stack.
            return ResolveGameOnly(gameRoot, "MO2 profile inventory unreadable or unsafe; all MO2 layers excluded: " + ex.Message, profile, token);
        }
    }

    public static Mo2Stack ResolveGameOnly(string? gameRoot, string reason, string? profile = null,
        CancellationToken token = default)
    {
        if (!IsValidGameRoot(gameRoot))
            return Refuse("Select the live Cyberpunk game root.", profile);
        try
        {
            var files = new Dictionary<string, Mo2WinningFile>(StringComparer.OrdinalIgnoreCase);
            var scanned = 0;
            var shadows = 0;
            IndexLayer(gameRoot!, "(physical game)", -1, false, files, ref scanned, ref shadows, token);
            return Build(profile ?? "", 0, files, shadows, [reason],
                profileVerified: false, gameOnlyFallback: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or InvalidDataException)
        {
            return Refuse("Cannot inspect live game: " + ex.Message, profile);
        }
    }

    private static bool IsValidGameRoot(string? root) => !string.IsNullOrWhiteSpace(root) &&
        File.Exists(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"));

    private static Mo2Stack Refuse(string detail, string? profile) =>
        new(false, detail, profile ?? "", 0, 0, 0, 0, 0,
            false, false, new Dictionary<string, Mo2WinningFile>(StringComparer.OrdinalIgnoreCase),
            [detail], ProfileVerified: false);

    private static Mo2Stack Build(string profile, int enabled,
        IReadOnlyDictionary<string, Mo2WinningFile> files, int shadowed,
        IReadOnlyList<string> notes, bool profileVerified, bool gameOnlyFallback)
    {
        var paths = files.Keys.ToArray();
        var cetOwners = paths.Where(IsCetInit).Select(p => p.Split('/')[5])
            .Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var reds = paths.Count(p => p.StartsWith("r6/scripts/", StringComparison.OrdinalIgnoreCase)
            && p.EndsWith(".reds", StringComparison.OrdinalIgnoreCase));
        var plugins = paths.Count(p => p.StartsWith("red4ext/plugins/", StringComparison.OrdinalIgnoreCase)
            && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
        var engine = files.ContainsKey("bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua");
        var asi = files.ContainsKey("bin/x64/plugins/cyber_engine_tweaks.asi") ||
            files.ContainsKey("bin/x64/plugins/cet.asi");
        var detail = gameOnlyFallback
            ? "Live game only; MO2 package selection unavailable. " + notes[0]
            : $"Live game + {enabled} enabled MO2 packages; {shadowed} loose-file overrides. VFS not proven.";
        return new(true, detail, profile, enabled, cetOwners, reds, plugins,
            shadowed, engine, reds > 0, files, notes, asi, profileVerified, gameOnlyFallback);
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
            if (!line.StartsWith('+')) continue;
            var name = line[1..].Trim();
            if (name.EndsWith("_separator", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Length == 0 || name.Contains('/') || name.Contains('\\') ||
                name == ".." || name == "." || name.Contains(':') || name.Contains('\0'))
                throw new InvalidDataException("Unsafe enabled MO2 package path in modlist.txt.");
            if (!seen.Add(name))
                throw new InvalidDataException("Duplicate enabled MO2 package in modlist.txt.");
            entries.Add(name);
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
