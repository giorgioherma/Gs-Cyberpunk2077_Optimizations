using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

// One explicit VERIFY authorizes this narrow setup operation. The normal
// desktop app stays unelevated; MO2 and its game child inherit one admin token.
internal static class Mo2StartupController
{
    internal const string CliSwitch = "--mo2-verify-start";

    internal static async Task StartAsync(string packageRoot, string folder, string instanceFolder)
    {
        var host = Path.Combine(packageRoot, PortablePackagePaths.RootLauncherName);
        var psi = new ProcessStartInfo(host)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = packageRoot,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        psi.ArgumentList.Add(CliSwitch);
        psi.ArgumentList.Add(folder);
        psi.ArgumentList.Add(instanceFolder);
        using var controller = Process.Start(psi) ??
            throw new InvalidOperationException("Windows could not start MO2 setup.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await controller.WaitForExitAsync(timeout.Token);
        if (controller.ExitCode != 0)
            throw new InvalidOperationException("MO2 setup failed (code " + controller.ExitCode +
                "). Check that MO2 can close normally and no game is running.");
    }

    internal static int ExecuteElevated(string folder, string instanceFolder)
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                return 10;
            var location = Mo2InstallLocator.Inspect(folder, instanceFolder);
            if (!location.HasConfiguration || location.InstanceFolder is null ||
                !string.Equals(Path.GetFullPath(instanceFolder), Path.GetFullPath(location.InstanceFolder),
                    StringComparison.OrdinalIgnoreCase))
                return 11;
            var exe = Path.GetFullPath(Path.Combine(folder, "ModOrganizer.exe"));
            if (!File.Exists(exe)) return 11;
            if (!location.IsPortable)
            {
                // Upstream -i accepts a global instance NAME, not a custom path.
                var parent = Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData), "ModOrganizer");
                if (!string.Equals(Path.GetDirectoryName(location.InstanceFolder), parent,
                        StringComparison.OrdinalIgnoreCase)) return 11;
            }
            if (GameRunning()) return 12;
            var processes = Process.GetProcessesByName("ModOrganizer");
            try
            {
                // Do not close an unrelated installation, force kill, or
                // interrupt a running game. Refuse if normal shutdown fails.
                foreach (var process in processes)
                {
                    if (!string.Equals(Mo2ProcessIdentity.Image(process.Id), exe,
                            StringComparison.OrdinalIgnoreCase)) return 13;
                }
                foreach (var process in processes)
                {
                    if (process.HasExited) continue;
                    if (!process.CloseMainWindow() || !process.WaitForExit(10000)) return 14;
                }
            }
            finally { foreach (var process in processes) process.Dispose(); }
            if (GameRunning()) return 12;
            Mo2CompanionInstaller.Install(folder,
                Path.Combine(AppContext.BaseDirectory, "mo2", "gco_mo2_bridge.py"));
            var start = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                WorkingDirectory = folder
            };
            if (!location.IsPortable)
            {
                start.ArgumentList.Add("-i");
                start.ArgumentList.Add(Path.GetFileName(location.InstanceFolder));
            }
            using var launched = Process.Start(start);
            return launched is null ? 15 : 0;
        }
        catch (Exception ex)
        {
            Trace.TraceError("MO2 startup: " + ex);
            return 16;
        }
    }

    private static bool GameRunning()
    {
        var games = Process.GetProcessesByName("Cyberpunk2077");
        try { return games.Length != 0; }
        finally { foreach (var game in games) game.Dispose(); }
    }
}
