using System.Diagnostics;

namespace GTotalOptimizer.Core;

// Only installs GCO's own Python adapter; never touches game/mod/profile files.
public static class Mo2CompanionInstaller
{
    // Only the exact previously shipped GCO-owned bridge is eligible for
    // replacement. Normalize checkout line endings; refuse user modifications.
    internal static bool IsKnownPreviousBridge(byte[] bytes)
    {
        if (bytes.Length == 0 || bytes.Length > 256 * 1024) return false;
        var normalized = System.Text.Encoding.UTF8.GetBytes(
            System.Text.Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n"));
        var sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(normalized));
        return sha.Equals("9d51d033f02d3c5462171bab08a2e7d63b2ae746eb3dc8677a713e7e925ecca7",
                   StringComparison.OrdinalIgnoreCase) ||
               sha.Equals("3005b331056bdd269621d9cc85ee021c1f785acab9eac3be374a2be920d01b71",
                   StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrent(string applicationFolder, string packagedBridge)
    {
        try
        {
            var target = Path.Combine(applicationFolder, "plugins", "gco_mo2_bridge.py");
            return File.Exists(target) && File.Exists(packagedBridge) &&
                (File.GetAttributes(target) & FileAttributes.ReparsePoint) == 0 &&
                File.ReadAllBytes(target).AsSpan().SequenceEqual(File.ReadAllBytes(packagedBridge));
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

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
        bool upgrade = false;
        if (File.Exists(target))
        {
            if ((File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("MO2 companion is redirected.");
            var previous = File.ReadAllBytes(target);
            if (previous.AsSpan().SequenceEqual(bytes)) return;
            if (!IsKnownPreviousBridge(previous))
                throw new InvalidOperationException("A different MO2 companion already exists. No files were overwritten.");
            upgrade = true;
        }
        var temporary = Path.Combine(plugins, ".gco-bridge-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (upgrade && (!File.Exists(target) ||
                (File.GetAttributes(target) & FileAttributes.ReparsePoint) != 0 ||
                !IsKnownPreviousBridge(File.ReadAllBytes(target))))
                throw new InvalidOperationException("MO2 companion changed during setup.");
            File.Move(temporary, target, overwrite: upgrade);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
