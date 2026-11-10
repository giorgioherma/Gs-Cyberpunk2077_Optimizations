using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

// Elevated helper lives in the SAME shipped WinExe as GCO. A single UAC approval
// authorizes owned session start or graceful stop, without a hotkey handler.
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
        if (restart) PresentMonLifecyclePolicy.RequireWritableCapturePath(safeCsv);
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
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
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

    // Share process ownership classification with the elevated stop/restart
    // controller. A foreign PresentMon must not block a native-only capture
    // when GCO has no tracked or running collector of its own.
    private static bool IsOwnedExecutable(string root, string livePath, string officialHash)
    {
        if (PresentMonLifecyclePolicy.IsOwnedProcess(root, livePath)) return true;
        var suffix = Path.Combine("components", "presentmon", "PresentMon.exe");
        return livePath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(livePath) &&
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(livePath)))
                .Equals(officialHash, StringComparison.OrdinalIgnoreCase);
    }

    public static bool HasOwnedRunning(string root)
    {
        var exe = PresentMonLifecyclePolicy.Executable(root);
        if (!File.Exists(exe)) return false;
        var officialHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(exe)));
        foreach (var process in Process.GetProcessesByName("PresentMon"))
        {
            using (process)
            {
                if (process.HasExited) continue;
                var livePath = process.MainModule?.FileName ??
                    throw new InvalidOperationException("Cannot verify active PresentMon ownership.");
                if (IsOwnedExecutable(root, livePath, officialHash)) return true;
            }
        }
        return false;
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
            if (action == "restart") PresentMonLifecyclePolicy.RequireWritableCapturePath(csv);
            var bundle = ComponentBundleVerifier.Inspect(root);
            if (!bundle.Ready)
                throw new InvalidDataException("GCO component identity verification failed: " + bundle.Detail);

            var presentMon = PresentMonLifecyclePolicy.Executable(root);
            var officialHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(presentMon)));

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
            var ownedPids = new List<int>();
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
                            "Cannot inspect PresentMon PID " + process.Id +
                            "; refusing to manipulate an unverified process.", ex);
                    }
                    if (!IsOwnedExecutable(root, livePath, officialHash))
                        throw new InvalidOperationException(
                            "Non-GCO PresentMon PID " + process.Id +
                            " is running. Close it manually before GCO capture.");
                    ownedPids.Add(process.Id);
                }
            }

            // PresentMon 2.6.0 supports a graceful, source-owned session stop.
            // Never Process.Kill() the CSV writer before it has flushed.
            if (ownedPids.Count > 0 || action == "stop")
            {
                var stop = new ProcessStartInfo(presentMon)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(presentMon)!
                };
                foreach (var arg in PresentMonLifecyclePolicy.StopArguments())
                    stop.ArgumentList.Add(arg);
                using var stopping = Process.Start(stop) ??
                    throw new InvalidOperationException("PresentMon session terminator did not start.");
                if (!stopping.WaitForExit(15000))
                    throw new TimeoutException("PresentMon session termination did not complete.");
                if (!PresentMonLifecyclePolicy.StopExitAccepted(stopping.ExitCode, ownedPids.Count))
                    throw new InvalidOperationException(
                        "PresentMon session terminator exited " + stopping.ExitCode + ".");
                foreach (var ownedPid in ownedPids)
                {
                    try
                    {
                        using var previous = Process.GetProcessById(ownedPid);
                        if (!previous.WaitForExit(15000))
                            throw new TimeoutException(
                                "Previous PresentMon PID " + ownedPid + " did not flush and exit.");
                    }
                    catch (ArgumentException) { /* PID has exited */ }
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
                foreach (var argument in PresentMonLifecyclePolicy.StartArguments(csv))
                    launch.ArgumentList.Add(argument);
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
                    ? "GCO PresentMon started with no hotkey; CSV recording is continuous."
                    : "GCO PresentMon ETW session ended gracefully; CSV writer exited."
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
