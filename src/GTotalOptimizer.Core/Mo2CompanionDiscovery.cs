using System.Diagnostics;
using System.Text.Json;

namespace GTotalOptimizer.Core;

// A live read-only MO2 companion report. These bytes are *discovery only*:
// they cannot authorize changing a profile, native overrides, capture or generation.
public sealed record Mo2CompanionStatus(
    bool Connected, string Detail, string GameDirectory, string CurrentProfile,
    string Mo2Executable, string InstanceBaseDirectory, string ModsDirectory,
    string OverwriteDirectory, string CurrentProfileDirectory,
    int ProcessId, DateTimeOffset UpdatedUtc,
    string BridgeEpoch = "", string StatusFilePath = "")
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
        string? localAppData = null, DateTimeOffset? currentUtc = null,
        Func<int, string?>? processImageForTesting = null)
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
                .Where(file => System.Text.RegularExpressions.Regex.IsMatch(
                    Path.GetFileName(file), @"^instance-[a-f0-9]{24}\.json$",
                    System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                .Take(MaximumStatusFiles + 1).ToArray();
            if (files.Length > MaximumStatusFiles)
                return Empty("Too many MO2 bridge status records; cannot identify the instance.");
            var accepted = new List<Mo2CompanionStatus>();
            string refusal = disconnected.Detail;
            foreach (var file in files)
            {
                // Do not parse attacker-controlled symlink targets.
                if (new FileInfo(file).LinkTarget is not null) continue;
                // Python publishes by atomic replace; allow deletion/rename
                // of the previous heartbeat while it is being read.
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length == 0 || stream.Length > 16 * 1024) continue;
                using var json = JsonDocument.Parse(stream);
                var root = json.RootElement;
                if (root.ValueKind != JsonValueKind.Object) continue;
                var epoch = Read(root, "bridgeEpoch");
                if (ReadInt(root, "schemaVersion") != 1 ||
                    Read(root, "kind") != "gco.mo2.readonly.status" ||
                    ReadBool(root, "installationEnabled") ||
                    epoch.Length != 32 || !epoch.All(char.IsAsciiHexDigit)) continue;

                var exe = Read(root, "mo2Executable");
                var game = Read(root, "gameRoot");
                var profile = Read(root, "currentProfile");
                var profilePath = Read(root, "currentProfilePath");
                var mods = Read(root, "modsRoot");
                var overwrite = Read(root, "overwriteRoot");
                var baseDirectory = Read(root, "instanceRoot");
                int pid = ReadInt(root, "processId");
                if (!SameFolder(Path.GetDirectoryName(exe), application)) continue;
                if (!SameFolder(mods, instance.ModsDirectory) ||
                    !SameFolder(overwrite, instance.OverwriteDirectory) ||
                    !SameFolder(Path.GetDirectoryName(profilePath), instance.ProfilesDirectory))
                {
                    refusal = "MO2 is open, but its instance paths do not match the selected instance.";
                    continue;
                }
                if (!File.Exists(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe")) || pid <= 0)
                {
                    refusal = "MO2 game directory or process identity is invalid.";
                    continue;
                }
                if (!DateTimeOffset.TryParse(Read(root, "updatedUtc"), out var heartbeat) ||
                    now < heartbeat || now - heartbeat > MaximumAge)
                {
                    refusal = "MO2 bridge heartbeat is stale.";
                    continue;
                }
                // A live PID can be reused by another process. Verify the actual
                // running image, not just a self-reported executable or live PID.
                var actualImage = processImageForTesting is null
                    ? LiveProcessImage(pid) : processImageForTesting(pid);
                if (!SameFile(actualImage, exe))
                {
                    refusal = actualImage is null
                        ? "Could not read the running MO2 process image."
                        : "MO2 process image does not match the selected installation.";
                    continue;
                }
                accepted.Add(new(true, "MO2 companion read-only identity confirmed; selected-profile deployment not yet proven.",
                    game, profile, exe, baseDirectory, mods, overwrite,
                    profilePath, pid, heartbeat, epoch, file));
            }
            return accepted.Count == 1 ? accepted[0] :
                Empty(accepted.Count > 1
                    ? "Multiple matching MO2 bridge processes; select a unique running instance."
                    : refusal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
            or ArgumentException or NotSupportedException)
        {
            return Empty("Cannot verify MO2 bridge status: " + ex.Message);
        }
    }

    private static string? LiveProcessImage(int pid) => Mo2ProcessIdentity.Image(pid);

    private static bool SameFile(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
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
