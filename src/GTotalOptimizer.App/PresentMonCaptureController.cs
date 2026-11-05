using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

// Elevated helper lives in the SAME shipped WinExe as GCO. A single UAC approval
// authorizes stop-old + start-new, without starting another admin-console shim.
internal static class PresentMonCaptureController
{
    public const string CliSwitch = "--presentmon-control";
    private const string ResultName = "PresentMon-controller.json";

    public static async Task RunAsync(string packageRoot, string csv, bool restart)
    {
        var safeCsv = PresentMonLifecyclePolicy.ValidateOutputCsv(packageRoot, csv);
        var resultPath = Path.Combine(Path.GetDirectoryName(safeCsv)!, ResultName);
        if (File.Exists(resultPath)) File.Delete(resultPath);

        var host = Path.Combine(packageRoot, "app", PortablePackagePaths.ManagedExecutableName);
        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.Combine(packageRoot, "app"),
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add(CliSwitch);
        psi.ArgumentList.Add(restart ? "restart" : "stop");
        psi.ArgumentList.Add(safeCsv);

        using var controller = Process.Start(psi)
            ?? throw new InvalidOperationException("Windows could not start the elevated PresentMon controller.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        try
        {
            await controller.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException("PresentMon controller did not finish; no new game launch was authorized.");
        }
        string? detail = null;
        if (File.Exists(resultPath))
        {
            using var result = JsonDocument.Parse(File.ReadAllText(resultPath));
            if (result.RootElement.TryGetProperty("detail", out var node))
                detail = node.GetString();
        }
        if (controller.ExitCode != 0)
            throw new InvalidOperationException("PresentMon controller failed: " +
                (detail ?? $"exit code {controller.ExitCode}. No second PresentMon was started."));
        if (!File.Exists(resultPath))
            throw new InvalidDataException("PresentMon controller ended without a verifiable status.");
    }

    public static int ExecuteElevated(string action, string outputCsv)
    {
        string? resultPath = null;
        try
        {
            var root = PortablePackagePaths.CurrentPackageRoot();
            var csv = PresentMonLifecyclePolicy.ValidateOutputCsv(root, outputCsv);
            resultPath = Path.Combine(Path.GetDirectoryName(csv)!, ResultName);
            Directory.CreateDirectory(Path.GetDirectoryName(csv)!);
            if (action is not ("restart" or "stop"))
                throw new ArgumentException("Unrecognized PresentMon operation.");
            var bundle = ComponentBundleVerifier.Inspect(root);
            if (!bundle.Ready)
                throw new InvalidDataException("GCO component identity verification failed: " + bundle.Detail);

            var presentMon = PresentMonLifecyclePolicy.Executable(root);
            var officialHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(presentMon)));
            var expectedPart = Path.Combine("components", "presentmon", "PresentMon.exe");

            foreach (var process in Process.GetProcessesByName("PresentMon"))
            {
                using (process)
                {
                    if (process.HasExited) continue;
                    string livePath;
                    try
                    {
                        livePath = process.MainModule?.FileName ??
                            throw new InvalidOperationException("Could not identify running PresentMon executable.");
                    }
                    catch (Win32Exception ex)
                    {
                        throw new InvalidOperationException(
                            "Cannot inspect existing PresentMon PID " + process.Id +
                            "; refusing to start a duplicate.", ex);
                    }
                    var currentOwner = PresentMonLifecyclePolicy.IsOwnedProcess(root, livePath);
                    // A previous version extracted in a different GCO folder is
                    // also ours if it has the same pinned bytes and package layout.
                    var previousGco = livePath.EndsWith(expectedPart, StringComparison.OrdinalIgnoreCase) &&
                        File.Exists(livePath) &&
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(livePath)))
                            .Equals(officialHash, StringComparison.OrdinalIgnoreCase);
                    if (!currentOwner && !previousGco)
                        throw new InvalidOperationException(
                            "Another non-GCO PresentMon is running (PID " + process.Id +
                            "). Close it before starting a GCO capture.");
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(10000))
                        throw new InvalidOperationException(
                            "Previous PresentMon PID " + process.Id +
                            " did not exit. No duplicate was launched.");
                }
            }

            int? pid = null;
            if (action == "restart")
            {
                var launch = new ProcessStartInfo(presentMon)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(presentMon)!
                };
                foreach (var argument in new[]
                {
                    "--process_name", "Cyberpunk2077.exe",
                    "--hotkey", "F11", "--session_name", "GCO",
                    "--output_file", csv
                }) launch.ArgumentList.Add(argument);
                // This controller already runs elevated. Do NOT also pass
                // --restart_as_admin or request a second UAC transition.
                using var spawned = Process.Start(launch)
                    ?? throw new InvalidOperationException("PresentMon did not launch.");
                if (spawned.WaitForExit(800))
                    throw new InvalidOperationException(
                        "PresentMon exited immediately (code " + spawned.ExitCode + ").");
                pid = spawned.Id;
            }

            File.WriteAllText(resultPath, JsonSerializer.Serialize(new
            {
                ok = true,
                action,
                pid,
                detail = action == "restart"
                    ? "Previous GCO PresentMon closed; one new process started."
                    : "GCO PresentMon stopped."
            }));
            return 0;
        }
        catch (Exception ex)
        {
            try
            {
                if (resultPath is not null)
                    File.WriteAllText(resultPath,
                        JsonSerializer.Serialize(new { ok = false, detail = ex.Message }));
            }
            catch { /* real failure is still the nonzero exit code */ }
            return 2;
        }
    }
}
