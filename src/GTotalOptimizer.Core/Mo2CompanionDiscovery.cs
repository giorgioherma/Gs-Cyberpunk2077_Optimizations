using System.Diagnostics;
using System.Text.Json;

namespace GTotalOptimizer.Core;

// A live read-only MO2 companion report. These bytes are *discovery only*:
// they cannot authorize changing a profile, native overrides, capture or generation.
public sealed record Mo2CompanionStatus(
    bool Connected, string Detail, string GameDirectory, string CurrentProfile,
    string Mo2Executable, string InstanceBaseDirectory, string ModsDirectory,
    string OverwriteDirectory, string CurrentProfileDirectory,
    int ProcessId, DateTimeOffset UpdatedUtc)
{
    public bool CanMutateSelectedProfile => false;
    public bool CanInstallProfilers => false;
}

public static class Mo2CompanionDiscovery
{
    private const int MaximumStatusFiles = 32;
    private static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(30);

    public static Mo2CompanionStatus Inspect(
        string? mo2ApplicationFolder, Mo2Instance instance,
        string? localAppData = null, DateTimeOffset? currentUtc = null)
    {
        var disconnected = Empty("MO2 companion not connected or not yet loaded.");
        if (!instance.Recognized || string.IsNullOrWhiteSpace(mo2ApplicationFolder))
            return Empty("MO2 application and instance must be identified first.");
        string application;
        try { application = Path.GetFullPath(mo2ApplicationFolder.Trim().Trim('"')); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        { return Empty("Invalid MO2 application directory."); }

        string dataRoot = localAppData ??
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(dataRoot))
            return disconnected;
        string bridgeDirectory = Path.Combine(dataRoot, "GCO", "MO2Bridge");
        if (!Directory.Exists(bridgeDirectory)) return disconnected;
        var now = currentUtc ?? DateTimeOffset.UtcNow;
        try
        {
            var files = Directory.EnumerateFiles(bridgeDirectory, "instance-*.json",
                    SearchOption.TopDirectoryOnly)
                .Take(MaximumStatusFiles + 1).ToArray();
            if (files.Length > MaximumStatusFiles)
                return Empty("Too many MO2 bridge status records; cannot identify the instance.");
            var accepted = new List<Mo2CompanionStatus>();
            foreach (var file in files)
            {
                // Do not parse attacker-controlled symlink targets.
                if (new FileInfo(file).LinkTarget is not null) continue;
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length == 0 || stream.Length > 16 * 1024) continue;
                using var json = JsonDocument.Parse(stream);
                var root = json.RootElement;
                if (ReadInt(root, "schemaVersion") != 1 ||
                    Read(root, "kind") != "gco.mo2.readonly.status" ||
                    ReadBool(root, "installationEnabled")) continue;

                var exe = Read(root, "mo2Executable");
                var game = Read(root, "gameRoot");
                var profile = Read(root, "currentProfile");
                var profilePath = Read(root, "currentProfilePath");
                var mods = Read(root, "modsRoot");
                var overwrite = Read(root, "overwriteRoot");
                var baseDirectory = Read(root, "instanceRoot");
                int pid = ReadInt(root, "processId");
                if (!SameFolder(Path.GetDirectoryName(exe), application) ||
                    !SameFolder(mods, instance.ModsDirectory) ||
                    !SameFolder(overwrite, instance.OverwriteDirectory) ||
                    !SameFolder(Path.GetDirectoryName(profilePath), instance.ProfilesDirectory) ||
                    !File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")) ||
                    pid <= 0) continue;
                if (!DateTimeOffset.TryParse(Read(root, "updatedUtc"), out var heartbeat) ||
                    now < heartbeat || now - heartbeat > MaximumAge) continue;
                try
                {
                    using var process = Process.GetProcessById(pid);
                    if (process.HasExited) continue;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                { continue; }
                accepted.Add(new(true, "MO2 companion read-only identity confirmed; selected-profile deployment not yet proven.",
                    game, profile, exe, baseDirectory, mods, overwrite,
                    profilePath, pid, heartbeat));
            }
            return accepted.Count == 1 ? accepted[0] :
                Empty(accepted.Count > 1
                    ? "Multiple matching MO2 bridge processes; select a unique running instance."
                    : disconnected.Detail);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            return Empty("Cannot verify MO2 bridge status: " + ex.Message);
        }
    }

    private static bool SameFolder(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(JsonElement root, string property) =>
        root.TryGetProperty(property, out var token) && token.ValueKind == JsonValueKind.String
            ? token.GetString() ?? "" : "";

    private static int ReadInt(JsonElement root, string property) =>
        root.TryGetProperty(property, out var token) && token.ValueKind == JsonValueKind.Number &&
        token.TryGetInt32(out int value) ? value : 0;

    private static bool ReadBool(JsonElement root, string property) =>
        root.TryGetProperty(property, out var token) && token.ValueKind == JsonValueKind.True;

    private static Mo2CompanionStatus Empty(string detail) =>
        new(false, detail, "", "", "", "", "", "", "", 0, DateTimeOffset.MinValue);
}
