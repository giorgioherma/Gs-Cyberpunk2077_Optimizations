using System.Diagnostics;
using System.Management;

namespace GTotalOptimizer.App;

// Win32_ProcessStartTrace emits an event only when a process starts. Once
// discovered, Process.Exited waits on that specific process handle without
// repeatedly enumerating game processes or interacting with the game.
internal sealed class GameProcessEvents : IDisposable
{
    private ManagementEventWatcher? starts;
    private readonly Dictionary<int, Process> tracked = new();
    private readonly string processName;
    private bool disposed;

    internal GameProcessEvents(string processName = "Cyberpunk2077.exe")
    {
        // A fixed executable basename also permits a real Windows contract
        // test with cmd.exe without starting the user's game in CI.
        if (string.IsNullOrWhiteSpace(processName) ||
            processName.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not ('.' or '_' or '-')) ||
            !processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Invalid game process event name.", nameof(processName));
        this.processName = processName;
    }

    public event Action<int>? Started;
    public event Action<int>? Exited;

    public bool TryStartNotifications(out string? error)
    {
        error = null;
        try
        {
            starts = new ManagementEventWatcher(new WqlEventQuery(
                "SELECT * FROM Win32_ProcessStartTrace WHERE ProcessName = '" +
                processName + "'"));
            starts.EventArrived += (_, e) =>
            {
                try
                {
                    if (disposed) return;
                    var raw = e.NewEvent.Properties["ProcessID"]?.Value;
                    if (raw is not null && Convert.ToInt64(raw) is var value &&
                        value > 0 && value <= int.MaxValue)
                        Started?.Invoke((int)value);
                }
                catch (Exception ex) { Trace.TraceWarning("Game start notification: " + ex); }
            };
            starts.Start();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            starts?.Dispose();
            starts = null;
            return false;
        }
    }

    public bool Track(int pid)
    {
        if (disposed) return false;
        if (tracked.ContainsKey(pid)) return true;
        Process? process = null;
        try
        {
            process = Process.GetProcessById(pid);
            process.Exited += (_, _) => Exited?.Invoke(pid);
            process.EnableRaisingEvents = true;
            tracked.Add(pid, process);
            if (process.HasExited)
                Exited?.Invoke(pid); // process died between StartTrace and handle attachment
            return true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Cannot track game process " + pid + ": " + ex);
            tracked.Remove(pid);
            process?.Dispose();
            return false; // process exited, inaccessible or already invalid
        }
    }

    public void Untrack(int pid)
    {
        if (tracked.Remove(pid, out var process))
        {
            try { process.EnableRaisingEvents = false; }
            catch (Exception ex) { Trace.TraceWarning("Process exit detach: " + ex); }
            process.Dispose();
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (starts is not null)
        {
            try { starts.Stop(); }
            catch (Exception ex) { Trace.TraceWarning("Stop process observer: " + ex); }
            starts.Dispose();
            starts = null;
        }
        foreach (var pid in tracked.Keys.ToArray()) Untrack(pid);
    }
}
