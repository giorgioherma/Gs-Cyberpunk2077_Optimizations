using System.Text.Json;

namespace GTotalOptimizer.App;

// Retained user-preference format from the original WinForms shell.
// Preferences do not touch Cyberpunk files.
internal static class UserSettings
{
    internal sealed record Mo2Setup(string InstallationFolder, string InstanceFolder, string Profile);

    private sealed class Settings
    {
        public string GameRoot { get; set; } = "";
        public string Mo2InstallationFolder { get; set; } = "";
        public string Mo2InstanceFolder { get; set; } = "";
        // Old versions stored a manual Mods-directory override. Retained for
        // backward deserialization only; the INI always supplies this path.
        public string Mo2ModsOverride { get; set; } = "";
        public string Mo2Profile { get; set; } = "";
        // Old per-source-folder settings are retained only for backward
        // deserialization; they never grant MO2 enabled-mod verification.
        public string[] Mo2SourceFolders { get; set; } = [];
    }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G-Total-Optimizer", "settings.json");

    private static Settings Read()
    {
        try { return File.Exists(SettingsFile)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings()
            : new Settings(); }
        catch { return new Settings(); }
    }

    internal static string ReadGameRoot() => Read().GameRoot;
    internal static Mo2Setup ReadMo2Setup()
    {
        var s = Read();
        return new(s.Mo2InstallationFolder, s.Mo2InstanceFolder, s.Mo2Profile);
    }
    internal static void WriteGameRoot(string value)
    {
        var s = Read(); s.GameRoot = value; Write(s);
    }
    internal static void WriteMo2Setup(string installation, string instance, string profile)
    {
        var s = Read();
        s.Mo2InstallationFolder = installation;
        s.Mo2InstanceFolder = instance;
        s.Mo2Profile = profile;
        Write(s);
    }

    private static void Write(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings));
            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch { /* Preferences must never modify game data. */ }
    }
}
