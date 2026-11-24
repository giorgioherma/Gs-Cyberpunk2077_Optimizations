using System.Diagnostics;

namespace GTotalOptimizer.Core;

// Only installs GCO's own Python adapter; never touches game/mod/profile files.
public static class Mo2CompanionInstaller
{
    public static void Install(string applicationFolder, string packagedBridge)
    {
        var root = Path.GetFullPath(applicationFolder);
        var executable = Path.Combine(root, "ModOrganizer.exe");
        var plugins = Path.Combine(root, "plugins");
        if (!File.Exists(executable) || !Directory.Exists(plugins))
            throw new InvalidOperationException("Select the MO2 application folder containing its existing plugins directory.");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
            (File.GetAttributes(plugins) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidOperationException("MO2 plugin location is redirected; installation refused.");
        if (!File.Exists(packagedBridge))
            throw new FileNotFoundException("GCO companion is missing from this package.", packagedBridge);
        var bytes = File.ReadAllBytes(packagedBridge);
        if (bytes.Length == 0 || bytes.Length > 256 * 1024)
            throw new InvalidDataException("Invalid GCO companion payload.");
        var processes = Process.GetProcessesByName("ModOrganizer");
        try
        {
            if (processes.Length != 0)
                throw new InvalidOperationException("Close all running MO2 instances before adding the companion.");
        }
        finally { foreach (var process in processes) process.Dispose(); }
        var target = Path.Combine(plugins, "gco_mo2_bridge.py");
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 ||
                !File.ReadAllBytes(target).AsSpan().SequenceEqual(bytes))
                throw new InvalidOperationException("A different MO2 companion already exists. No files were overwritten.");
            return;
        }
        var temporary = Path.Combine(plugins, ".gco-bridge-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, target, overwrite: false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
