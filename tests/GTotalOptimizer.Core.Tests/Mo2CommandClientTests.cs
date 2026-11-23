using GTotalOptimizer.Core;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class Mo2CommandClientTests
{
    public static void Run()
    {
        var canonical = typeof(Mo2CompanionCommandClient).GetMethod(
            "CanonicalRequest", BindingFlags.NonPublic | BindingFlags.Static) ??
            throw new InvalidDataException("MO2 signing contract missing");
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["schemaVersion"] = 1,
            ["bridgeEpoch"] = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            ["requestId"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb",
            ["operation"] = "probe",
            ["profile"] = "",
            ["gameRoot"] = @"C:\Games\Cyberpunk 2077",
            ["stagingRoot"] = "",
            ["sessionId"] = "",
            ["createdUtcMs"] = 1795460000000L,
        };
        string actual = (string?)canonical.Invoke(null, [body]) ??
            throw new InvalidDataException("MO2 canonical request missing");
        string expected = string.Join("\n", "1", new string('a', 32),
            new string('b', 32), "probe", "", @"C:\Games\Cyberpunk 2077",
            "", "", "1795460000000");
        if (actual != expected)
            throw new InvalidDataException("MO2_CSHARP_PYTHON_SIGNATURE_CANONICAL_MISMATCH");
        byte[] secret = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
        string signature = Convert.ToHexString(
            HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes(actual))).ToLowerInvariant();
        if (signature != "525b9fa14a79e3417649ef7c99600e59dd278bf672968b478ec487d553c23ddd")
            throw new InvalidDataException("MO2_CSHARP_PYTHON_HMAC_SHA256_MISMATCH");

        body["profile"] = "unsafe\nprofile";
        try
        {
            canonical.Invoke(null, [body]);
            throw new InvalidDataException("MO2_SIGNED_FIELD_NEWLINE_ACCEPTED");
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException)
        {
            // Required rejection.
        }

        var status = new Mo2CompanionStatus(
            true, "Mocked", "", "Other", "", "", "", "", "",
            1, DateTimeOffset.UtcNow, new string('a', 32), "");
        try
        {
            _ = Mo2CompanionCommandClient.LaunchAsync(
                status, "Heavy", "untrusted-stage", new string('b', 32));
            throw new InvalidDataException("MO2_PROFILER_LAUNCH_ENABLED_BEFORE_RUNTIME_PROOF");
        }
        catch (InvalidOperationException)
        {
            // Source-owner/gameplay gate must stay off.
        }
        Console.WriteLine("PASS GCO MO2 signed command canonical parity, HMAC, rejection and gated launch");
    }
}
