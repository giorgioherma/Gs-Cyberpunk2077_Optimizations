namespace GTotalOptimizer.Core;

public sealed record Mo2Location(
    bool HasInstallation, bool HasConfiguration, string? InstanceFolder,
    bool IsPortable, IReadOnlyList<string> InstanceChoices, string Detail);

// The user picks one MO2 installation folder. A portable INI lives there;
// instance-mode INIs normally live under %LOCALAPPDATA%\ModOrganizer\<instance>.
// Do not guess among multiple distinct instances: the user selects one.
public static class Mo2InstallLocator
{
    public static Mo2Location Inspect(string? installation, string? chosenInstance = null,
        string? localAppData = null)
    {
        if (string.IsNullOrWhiteSpace(installation))
            return Empty("Select the MO2 folder (containing ModOrganizer.exe).");
        try
        {
            var folder = Path.GetFullPath(installation.Trim().Trim('"'));
            if (!Directory.Exists(folder))
                return Empty("Selected MO2 folder does not exist.");

            if (File.Exists(Path.Combine(folder, "ModOrganizer.ini")))
                return new(true, true, folder, true, [folder],
                    "Portable MO2 configuration found.");

            if (!File.Exists(Path.Combine(folder, "ModOrganizer.exe")))
                return Empty("Select the MO2 application directory or a portable MO2 folder.");

            var appData = localAppData ??
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var store = Path.Combine(appData, "ModOrganizer");
            var candidates = Directory.Exists(store)
                ? Directory.EnumerateDirectories(store)
                    .Where(path => File.Exists(Path.Combine(path, "ModOrganizer.ini")))
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
                : [];
            if (!string.IsNullOrWhiteSpace(chosenInstance))
            {
                var specified = Path.GetFullPath(chosenInstance.Trim().Trim('"'));
                if (File.Exists(Path.Combine(specified, "ModOrganizer.ini")) &&
                    !candidates.Contains(specified, StringComparer.OrdinalIgnoreCase))
                    candidates = candidates.Append(specified).ToArray();
            }
            if (candidates.Length == 0)
                return new(true, false, null, false, [],
                    "No MO2 instance configuration found. Locate the instance folder manually.");

            // A previously chosen instance is valid only if still present in the candidate set.
            var selected = candidates.FirstOrDefault(path => string.Equals(
                path, chosenInstance, StringComparison.OrdinalIgnoreCase));
            if (selected is null && candidates.Length == 1)
                selected = candidates[0];

            return selected is null
                ? new(true, false, null, false, candidates,
                    "Multiple MO2 instances found. Select the instance used for Cyberpunk.")
                : new(true, true, selected, false, candidates,
                    "MO2 instance configuration located: " + Path.GetFileName(selected));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Empty("Could not inspect the MO2 folder: " + ex.Message);
        }
    }

    private static Mo2Location Empty(string detail) =>
        new(false, false, null, false, [], detail);
}
