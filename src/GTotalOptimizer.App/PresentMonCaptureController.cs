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

    // A fresh 128-bit capture identity is also the control capability for
    // its elevated, native PresentMon observer. No polling or elevated .NET
    // process stays resident during gameplay.
    private static string ObserverEvent(string sessionId)
    {
        if (sessionId.Length != 32 || !sessionId.All(Uri.IsHexDigit))
            throw new InvalidDataException("Invalid PresentMon capture identity.");
        return @"Local\GCO_PM_Stop_" + sessionId;
    }

    private static bool ObserverReady(string sessionId)
    {
        try
        {
            using var control = EventWaitHandle.OpenExisting(ObserverEvent(sessionId));
            return true;
        }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }

    private static void RequestObserverStop(string sessionId)
    {
        using var stop = EventWaitHandle.OpenExisting(ObserverEvent(sessionId));
        if (!stop.Set())
            throw new InvalidOperationException("GCO PresentMon observer did not accept stop.");
    }

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

    public static async Task RunAsync(string packageRoot, string csv, bool restart,
        string? captureSessionId = null)
    {
        if (!restart)
            throw new InvalidOperationException(
                "PresentMon stop must use the previously authorized observer; no second elevation.");
        if (captureSessionId is null)
            throw new InvalidDataException("PresentMon capture identity missing.");
        ObserverEvent(captureSessionId);
        var safeCsv = PresentMonLifecyclePolicy.ValidateOutputCsv(packageRoot, csv);
        var folder = CollectedCaptureStore.SessionFolder(packageRoot, captureSessionId);
        if (!Path.GetFullPath(safeCsv).StartsWith(
                Path.GetFullPath(folder) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PresentMon CSV does not belong to this capture identity.");
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
        psi.ArgumentList.Add(captureSessionId);

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
            if (!item.TryGetProperty("sessionId", out var sid) ||
                !string.Equals(sid.GetString(), captureSessionId, StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("observerPid", out var observerPid) ||
                !observerPid.TryGetInt32(out var observerProcessId) || observerProcessId <= 0)
                throw new InvalidDataException("PresentMon observer startup proof is incomplete.");
        }
        // Check the control channel from the NON-elevated GUI token too.
        // A start is not accepted if GCO cannot later request ETW shutdown.
        if (!ObserverReady(captureSessionId))
            throw new InvalidDataException("PresentMon observer control is unavailable to GCO.");
        if (restart)
        {
            Directory.CreateDirectory(Path.Combine(packageRoot, "RESULTS"));
            File.WriteAllText(TrackingFile(packageRoot), safeCsv);
        }
    }

    // The native observer holds the initial elevation and owns the ETW stop.
    // WPF only signals its per-attempt event; neither Collect nor Restore
    // requests another UAC prompt. A stopped/missing writer is harmless.
    public static async Task WaitForAutoStopAsync(string root, string csv,
        CancellationToken token = default)
    {
        var ownedCsv = PresentMonLifecyclePolicy.ValidateOutputCsv(root, csv);
        var resultPath = Path.Combine(Path.GetDirectoryName(ownedCsv)!, ResultName);
        // A crashed/missing controller report must not strand Restore when
        // no PresentMon process exists anywhere to keep the CSV open.
        // If a writer is still present, fail closed without owner proof.
        static bool AnyPresentMon()
        {
            var found = Process.GetProcessesByName("PresentMon");
            try { return found.Length != 0; }
            finally { foreach (var process in found) process.Dispose(); }
        }
        if (!File.Exists(resultPath))
        {
            if (!AnyPresentMon()) return;
            throw new InvalidDataException("PresentMon startup proof is missing; stop remains unverified.");
        }
        int pid;
        try
        {
            using var proof = JsonDocument.Parse(File.ReadAllText(resultPath));
            var item = proof.RootElement;
            if (!item.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True ||
                !item.TryGetProperty("action", out var action) || action.GetString() != "restart" ||
                !item.TryGetProperty("pid", out var pidNode) ||
                pidNode.ValueKind != JsonValueKind.Number ||
                !pidNode.TryGetInt32(out pid) || pid <= 0)
                throw new InvalidDataException("PresentMon startup proof is incomplete.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
            if (!AnyPresentMon()) return;
            throw;
        }
        // A stopped game or failed launch requests an authorized graceful
        // ETW flush. Older captures without an observer identity are accepted
        // only if their writer has already exited.
        string? observerSession = null;
        using (var proof = JsonDocument.Parse(File.ReadAllText(resultPath)))
        {
            var item = proof.RootElement;
            if (item.TryGetProperty("sessionId", out var session) &&
                session.ValueKind == JsonValueKind.String)
                observerSession = session.GetString();
        }
        var live = Process.GetProcessesByName("PresentMon");
        bool ownedLive;
        try { ownedLive = live.Any(p => p.Id == pid); }
        finally { foreach (var p in live) p.Dispose(); }
        if (ownedLive && observerSession is not null)
        {
            // Missing/denied observer events cannot justify assuming an ETW
            // stop succeeded. Preserve its CSV/journal for the next START.
            RequestObserverStop(observerSession);
        }

        // Enumerate PIDs instead of opening an elevated process handle.
        // PROCESS_SYNCHRONIZE/MainModule can be denied to non-admin WPF;
        // enumeration of the exact startup-reported PID does not require
        // a second elevation and has no dependency on other PresentMon PIDs.
        for (var attempt = 0; attempt < 80; attempt++)
        {
            token.ThrowIfCancellationRequested();
            var processes = Process.GetProcessesByName("PresentMon");
            bool ownedStillRunning;
            try { ownedStillRunning = processes.Any(p => p.Id == pid); }
            finally { foreach (var p in processes) p.Dispose(); }
            if (!ownedStillRunning) return;
            await Task.Delay(250, token);
        }
        throw new TimeoutException("PresentMon is still recording. Close Cyberpunk and retry.");
    }

    // Share process ownership classification with the elevated start
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

    public static int ExecuteElevated(string action, string outputCsv,
        string captureSessionId)
    {
        string? resultPath = null;
        try
        {
            var root = PortablePackagePaths.CurrentPackageRoot();
            var csv = PresentMonLifecyclePolicy.ValidateOutputCsv(root, outputCsv);
            ObserverEvent(captureSessionId);
            var expectedFolder = CollectedCaptureStore.SessionFolder(root, captureSessionId);
            if (!csv.StartsWith(Path.GetFullPath(expectedFolder) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PresentMon session identity does not match CSV.");
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
            int? observerPid = null;
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
                // Spawn the tiny native observer from THIS elevated token.
                // It has the privilege to gracefully stop the same named ETW
                // session later, without another UAC or a resident managed UI.
                try
                {
                    var native = Path.Combine(root, PortablePackagePaths.RootLauncherName);
                    var monitorStart = new ProcessStartInfo(native)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = root
                    };
                    monitorStart.ArgumentList.Add("--presentmon-observe");
                    monitorStart.ArgumentList.Add(spawned.Id.ToString(
                        System.Globalization.CultureInfo.InvariantCulture));
                    monitorStart.ArgumentList.Add(captureSessionId);
                    using var monitor = Process.Start(monitorStart) ??
                        throw new InvalidOperationException("Native PresentMon observer failed to launch.");
                    var ready = false;
                    for (var i = 0; i < 100; i++)
                    {
                        if (monitor.HasExited)
                            throw new InvalidOperationException(
                                "Native PresentMon observer exited " + monitor.ExitCode + ".");
                        if (ObserverReady(captureSessionId))
                        {
                            ready = true;
                            break;
                        }
                        Thread.Sleep(50);
                    }
                    if (!ready)
                        throw new TimeoutException("Native PresentMon observer did not initialize.");
                    observerPid = monitor.Id;
                }
                catch
                {
                    // Startup is not admitted if no authorized stop path was
                    // established. Gracefully end this writer before failure.
                    try
                    {
                        var stop = new ProcessStartInfo(presentMon)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            WorkingDirectory = Path.GetDirectoryName(presentMon)!
                        };
                        foreach (var arg in PresentMonLifecyclePolicy.StopArguments())
                            stop.ArgumentList.Add(arg);
                        using var ending = Process.Start(stop);
                        if (ending is not null && ending.WaitForExit(15000))
                            spawned.WaitForExit(15000);
                    }
                    catch (Exception cleanup)
                    {
                        Trace.TraceWarning("PresentMon startup rollback: " + cleanup);
                    }
                    throw;
                }
            }

            WriteAtomicResult(resultPath, JsonSerializer.Serialize(new
            {
                ok = true,
                action,
                pid,
                observerPid,
                sessionId = captureSessionId,
                detail = action == "restart"
                    ? "GCO PresentMon recording and authorized native observer started."
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
