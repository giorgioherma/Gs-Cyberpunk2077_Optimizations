using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GTotalOptimizer.Core;

public sealed record Mo2BridgeReply(
    bool Ok, string Detail, string RequestId, int ProcessId);

// Narrow GCO <-> already-running MO2 Python plugin mailbox. Uses one
// owner-local random secret per MO2 process lifetime. The GCO UI has NOT yet
// wired this client; normal profiling remains on the existing Direct route.
public static class Mo2CompanionCommandClient
{
    private static readonly Regex StatusName = new(
        @"^instance-[a-f0-9]{24}\.json$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex Token = new(
        @"^[a-f0-9]{32}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly string[] SignedFields =
    [
        "schemaVersion", "bridgeEpoch", "requestId", "operation", "profile",
        "gameRoot", "stagingRoot", "sessionId", "createdUtcMs"
    ];

    public static Task<Mo2BridgeReply> ProbeAsync(
        Mo2CompanionStatus connected, CancellationToken cancellationToken = default) =>
        SendAsync(connected, "probe", "", "", cancellationToken);

    // The response has already passed epoch/request/HMAC validation. A
    // remote token query may be denied across integrity levels, so accept
    // the authenticated companion's own token result for that exact PID.
    // A confirmed non-elevated Windows token always wins over a reply.
    internal static bool ElevationVerified(int pid, bool? windowsElevation, Mo2BridgeReply reply)
    {
        if (pid <= 0 || !reply.Ok || windowsElevation == false) return false;
        if (windowsElevation == true) return true;
        return reply.ProcessId == 0 && string.Equals(reply.Detail,
            "MO2 token=elevated; pid=" + pid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            StringComparison.Ordinal);
    }

    public static bool ElevationVerified(Mo2CompanionStatus connected, Mo2BridgeReply reply) =>
        connected.Connected && ElevationVerified(connected.ProcessId,
            Mo2ProcessIdentity.InspectElevation(connected.ProcessId).Elevated, reply);

    public static async Task<bool> VerifyElevatedAsync(Mo2CompanionStatus connected,
        CancellationToken cancellationToken = default)
    {
        if (!connected.Connected) return false;
        var reply = await ProbeAsync(connected, cancellationToken);
        return ElevationVerified(connected, reply);
    }

    // DO NOT expose this through Page 1 until source-owner staging, recovery,
    // real MO2 process launch and native gameplay checks pass.
    public static Task<Mo2BridgeReply> LaunchAsync(
        Mo2CompanionStatus connected, string profile, string stageRoot,
        string sessionId, CancellationToken cancellationToken = default)
    {
        if (!Mo2FeatureGate.Enabled)
            throw new InvalidOperationException(
                "MO2 profiler deployment is still gated pending native and gameplay proof.");
        if (string.IsNullOrWhiteSpace(profile) || !Token.IsMatch(sessionId))
            throw new ArgumentException("Invalid MO2 selected profile or GCO session ID.");
        return SendAsync(connected, "launch", profile, stageRoot,
            cancellationToken, sessionId);
    }

    // Explicitly confirmed local test only. The normal production gate remains
    // disabled. Never offer a raw arbitrary-stage experimental launch.
    public static async Task<Mo2BridgeReply> LaunchPreparedTestAsync(
        Mo2CompanionStatus connected, Mo2ProfileSelection selection,
        Mo2OverlayPreparation prepared, bool userConfirmed,
        CancellationToken cancellationToken = default)
    {
        if (!userConfirmed || !connected.Connected || !selection.Recognized ||
            selection.CandidateSource is null ||
            !string.Equals(selection.GameDirectory, connected.GameDirectory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Explicit local MO2 test admission is required.");
        if (System.Diagnostics.Process.GetProcessesByName("Cyberpunk2077").Length != 0)
            throw new InvalidOperationException("Close Cyberpunk before launching the MO2 test.");
        if (Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore())
            throw new InvalidOperationException("Click VERIFY before starting Cyberpunk.");
        if (!await VerifyElevatedAsync(connected, cancellationToken))
            throw new InvalidOperationException("Click VERIFY to start MO2 with the required permissions.");
        Mo2ProfilerOverlayBuilder.VerifyPrepared(prepared, selection.CandidateSource);
        // Once the launch is attempted, even a signed failure can follow a
        // created process. Never classify a failed/timeout request as unlaunched.
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "prepared", "test-launch-pending");
        var reply = await SendAsync(connected, "launch", prepared.Profile,
            prepared.StagingDirectory, cancellationToken, prepared.SessionId);
        if (!reply.Ok || reply.ProcessId <= 0)
            throw new InvalidOperationException("MO2 launch failed: " + reply.Detail);
        Mo2ProfilerOverlayBuilder.RecordTestLaunchPhase(prepared, "test-launch-pending",
            "test-launched", reply.ProcessId);
        return reply;
    }

    private static async Task<Mo2BridgeReply> SendAsync(
        Mo2CompanionStatus connected, string operation, string profile,
        string stagingRoot, CancellationToken cancellationToken, string sessionId = "")
    {
        if (!connected.Connected ||
            !Token.IsMatch(connected.BridgeEpoch) ||
            string.IsNullOrWhiteSpace(connected.StatusFilePath))
            throw new InvalidOperationException("MO2 companion identity and epoch not verified.");
        var statusPath = Path.GetFullPath(connected.StatusFilePath);
        var folder = Path.GetDirectoryName(statusPath)!;
        var ownerDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GCO", "MO2Bridge");
        if (!Path.GetFullPath(folder).Equals(Path.GetFullPath(ownerDirectory),
                StringComparison.OrdinalIgnoreCase) ||
            !StatusName.IsMatch(Path.GetFileName(statusPath)) ||
            new FileInfo(statusPath).LinkTarget is not null)
            throw new InvalidOperationException("Unsafe MO2 bridge directory or status identity.");
        var keyPath = Path.ChangeExtension(statusPath, ".key");
        var requestPath = Path.ChangeExtension(statusPath, ".request.json");
        var responsePath = Path.ChangeExtension(statusPath, ".response.json");
        if (!File.Exists(keyPath) || new FileInfo(keyPath).LinkTarget is not null)
            throw new InvalidOperationException("MO2 bridge authentication key unavailable.");
        var secret = File.ReadAllBytes(keyPath);
        if (secret.Length != 32)
            throw new InvalidDataException("Unexpected MO2 bridge authentication key size.");
        var requestId = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        var request = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["bridgeEpoch"] = connected.BridgeEpoch,
            ["requestId"] = requestId,
            ["operation"] = operation,
            ["profile"] = profile,
            ["gameRoot"] = connected.GameDirectory,
            ["stagingRoot"] = stagingRoot,
            ["sessionId"] = sessionId,
            ["createdUtcMs"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        request["hmac"] = ToHex(HMACSHA256.HashData(secret,
            Encoding.UTF8.GetBytes(CanonicalRequest(request))));
        var temporary = Path.Combine(folder, ".gco-command-" + requestId + ".tmp");
        try
        {
            // Single outstanding request per MO2 instance. Atomic rename avoids
            // running a partially written command from the plugin's Qt timer.
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(request),
                cancellationToken);
            File.Move(temporary, requestPath);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(20);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(responsePath) && new FileInfo(responsePath).LinkTarget is null)
            {
                using var doc = JsonDocument.Parse(
                    await File.ReadAllTextAsync(responsePath, cancellationToken));
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object &&
                    ReadString(root, "requestId") == requestId)
                {
                    var epoch = ReadString(root, "bridgeEpoch");
                    var detail = ReadString(root, "detail");
                    var pid = root.TryGetProperty("processId", out var num) &&
                        num.TryGetInt32(out int n) ? n : 0;
                    var ok = root.TryGetProperty("ok", out var flag) &&
                        flag.ValueKind == JsonValueKind.True;
                    var signature = ReadString(root, "hmac");
                    var payload = string.Join("\n", "1", epoch, requestId,
                        ok ? "1" : "0", pid.ToString(
                            System.Globalization.CultureInfo.InvariantCulture), detail);
                    if (epoch != connected.BridgeEpoch ||
                        !Verify(secret, payload, signature))
                        throw new InvalidDataException("MO2 bridge response authentication failed.");
                    return new(ok, detail, requestId, pid);
                }
            }
            await Task.Delay(200, cancellationToken);
        }
        throw new TimeoutException(
            "MO2 did not respond. Check its window for an error or permission prompt.");
    }

    internal static string CanonicalRequest(IReadOnlyDictionary<string, object> payload)
    {
        var fields = new List<string>(SignedFields.Length);
        foreach (string key in SignedFields)
        {
            if (!payload.TryGetValue(key, out var value) || value is null)
                throw new InvalidDataException("Missing signed MO2 request field: " + key);
            var content = Convert.ToString(
                value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            if (content.IndexOfAny(['\n', '\r', '\0']) >= 0)
                throw new InvalidDataException("Unsafe newline or NUL in signed MO2 field");
            fields.Add(content);
        }
        return string.Join("\n", fields);
    }

    private static bool Verify(byte[] secret, string message, string hex)
    {
        if (hex.Length != 64 || !hex.All(char.IsAsciiHexDigit))
            return false;
        return CryptographicOperations.FixedTimeEquals(
            HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(message)),
            Convert.FromHexString(hex));
    }

    private static string ToHex(byte[] data) =>
        Convert.ToHexString(data).ToLowerInvariant();

    private static string ReadString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";
}
