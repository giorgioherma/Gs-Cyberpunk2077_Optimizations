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
    private static string TrackingFile(string root) =>
        Path.Combine(root, "RESULTS", "GCO-current-PresentMon.txt");

    public static string? ReadTrackedCsv(string root)
    {
        var tracking = TrackingFile(root);
        if (!File.Exists(tracking)) return null;
        return PresentMonLifecyclePolicy.ValidateOutputCsv(root, File.ReadAllText(tracking).Trim());
    }

    public static void ClearTrackedCsv(string root)
    {
        var tracking = TrackingFile(root);
        if (File.Exists(tracking)) File.Delete(tracking);
    }

    public static async Task RunAsync(string packageRoot, string csv, bool restart)
    {
        var safeCsv = PresentMonLifecyclePolicy.ValidateOutputCsv(packageRoot, csv);
        var resultPath = Path.Combine(Path.GetDirectoryName(safeCsv)!, ResultName);
        if (File.Exists(resultPath)) File.Delete(resultPath);

        // Use the native GUI launcher, which prepares the private .NET 10
        // runtime even after Windows switches to the elevated token.
        var host = Path.Combine(packageRoot, PortablePackagePaths.RootLauncherName);
        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = packageRoot,
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
        if (controller.ExitCode != 0)
            throw new InvalidOperationException(
                "Elevated GCO launcher failed (exit " + controller.ExitCode + "). No new game was started.");

        // The native launcher returns once the private-runtime GUI host starts.
        // Wait for the elevated CHILD's atomic result, not the launcher's exit.
        var deadline = Stopwatch.StartNew();
        while (!File.Exists(resultPath) && deadline.Elapsed < TimeSpan.FromSeconds(20))
            await Task.Delay(100);
        if (!File.Exists(resultPath))
            throw new TimeoutException(
                "No result from elevated PresentMon controller; refusing an unverified second instance.");
        using (var result = JsonDocument.Parse(File.ReadAllText(resultPath)))
        {
            var item = result.RootElement;
            var success = item.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.True;
            if (!success)
            {
                var detail = item.TryGetProperty("detail", out var node)
                    ? node.GetString() : "Unknown elevated-controller failure.";
                throw new InvalidOperationException("PresentMon controller failed: " + detail);
            }
        }
        if (restart)
        {
            Directory.CreateDirectory(Path.Combine(packageRoot, "RESULTS"));
            File.WriteAllText(TrackingFile(packageRoot), safeCsv);
        }
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

            // Serialize all GCO windows (including after app restarts). Without
            // this gate two elevated helpers could both observe no PresentMon
            // and create two capture processes simultaneously.
            using var gate = new Mutex(false, @"Local\GCO_PresentMon_Single_Instance");
            bool gateHeld;
            try { gateHeld = gate.WaitOne(TimeSpan.FromSeconds(15)); }
            catch (AbandonedMutexException) { gateHeld = true; }
            if (!gateHeld)
                throw new TimeoutException("Another GCO PresentMon restart is still running.");
            try
            {
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

            WriteAtomicResult(resultPath, JsonSerializer.Serialize(new
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
            finally { gate.ReleaseMutex(); }
        }
        catch (Exception ex)
        {
            try
            {
                if (resultPath is not null)
                    WriteAtomicResult(resultPath,
                        JsonSerializer.Serialize(new { ok = false, detail = ex.Message }));
            }
            catch { /* real failure is still the nonzero exit code */ }
            return 2;
        }
    }

    private static void WriteAtomicResult(string resultPath, string content)
    {
        var temp = resultPath + ".tmp";
        File.WriteAllText(temp, content);
        File.Move(temp, resultPath, overwrite: true);
    }
}
