namespace GTotalOptimizer.Core;

/// <summary>
/// Read-only, explicitly selected MO2 instance/profile source identity.
/// A candidate loose-file map is never proof of actual USVFS behavior.
/// </summary>
public sealed record Mo2ProfileSelection(
    bool Recognized,
    string Detail,
    string ApplicationDirectory,
    string InstanceDirectory,
    string GameDirectory,
    string Profile,
    IReadOnlyList<string> AvailableProfiles,
    Mo2Stack? CandidateSource)
{
    // The source map does not authorize capture or optimization.
    public bool RequiresRuntimeVerification => Recognized;
    public bool AllowsProfilerInstallation => false;
    public bool AllowsGeneration => false;
}

public static class Mo2ProfileSelectionService
{
    public static Mo2ProfileSelection Inspect(
        string? mo2Folder,
        string? chosenInstance,
        string? chosenProfile,
        string? apiReportedGameDirectory,
        string? localAppData = null,
        CancellationToken cancellationToken = default)
    {
        var application = Normalize(mo2Folder);
        var location = Mo2InstallLocator.Inspect(mo2Folder, chosenInstance, localAppData);
        if (!location.HasConfiguration || string.IsNullOrEmpty(location.InstanceFolder))
            return Refuse(location.Detail, application);

        var instance = Mo2SourceInspector.Discover(location.InstanceFolder);
        if (!instance.Recognized)
            return Refuse(instance.Detail, application, location.InstanceFolder);

        // Game directory comes from MO2's managed-game API, not an assumed install path.
        var game = Normalize(apiReportedGameDirectory);
        if (string.IsNullOrWhiteSpace(game) ||
            !File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")))
            return Refuse("MO2 has not confirmed a valid Cyberpunk 2077 game root.",
                application, location.InstanceFolder, game, instance.Profiles);

        var profile = string.IsNullOrWhiteSpace(chosenProfile)
            ? instance.PreferredProfile ?? string.Empty
            : chosenProfile.Trim();
        if (!instance.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase))
            return Refuse("Selected MO2 profile does not belong to this instance.",
                application, location.InstanceFolder, game, instance.Profiles);

        // Existing authoritative read-only loose-file inspector; includes physical game.
        var candidates = Mo2SourceInspector.Resolve(game, instance, profile, cancellationToken);
        if (!candidates.Ready || !candidates.ProfileVerified || candidates.GameOnlyFallback)
            return new(false, "MO2 selected-profile inventory is unverified: " + candidates.Detail,
                application, location.InstanceFolder, game, profile,
                instance.Profiles, candidates);

        return new(true, "MO2 profile and candidate loose-file winners resolved; runtime VFS not yet verified.",
            application, location.InstanceFolder, game, profile,
            instance.Profiles, candidates);
    }

    private static Mo2ProfileSelection Refuse(
        string detail, string application, string instance = "", string game = "",
        IReadOnlyList<string>? choices = null) =>
        new(false, detail, application, instance, game, string.Empty,
            choices ?? [], null);

    private static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try { return Path.GetFullPath(path.Trim().Trim('"')); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException)
        { return string.Empty; }
    }
}
