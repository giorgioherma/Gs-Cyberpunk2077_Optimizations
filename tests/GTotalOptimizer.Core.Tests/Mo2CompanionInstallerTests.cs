using GTotalOptimizer.Core;

internal static class Mo2CompanionInstallerTests
{
    public static void Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "gco-mo2-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            string app = Path.Combine(root, "app");
            string plugins = Path.Combine(app, "plugins");
            Directory.CreateDirectory(plugins);
            string exe = Path.Combine(app, "ModOrganizer.exe");
            string source = Path.Combine(root, "bridge.py");
            string target = Path.Combine(plugins, "gco_mo2_bridge.py");
            string foreign = Path.Combine(plugins, "other.py");
            File.WriteAllText(exe, "test executable");
            File.WriteAllText(source, "test bridge");
            File.WriteAllText(foreign, "foreign plugin");
            Mo2CompanionInstaller.Install(app, source);
            Mo2CompanionInstaller.Install(app, source);
            if (File.ReadAllText(target) != "test bridge" || File.ReadAllText(foreign) != "foreign plugin" ||
                File.ReadAllText(exe) != "test executable" || Directory.GetFiles(plugins).Length != 2)
                throw new InvalidDataException("MO2 companion copy changed unrelated files or left staging");
            File.WriteAllText(target, "custom bridge");
            bool refused = false;
            try { Mo2CompanionInstaller.Install(app, source); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused || File.ReadAllText(target) != "custom bridge")
                throw new InvalidDataException("MO2 companion overwritten");
            refused = false;
            try { Mo2CompanionInstaller.Install(root, source); }
            catch (InvalidOperationException) { refused = true; }
            if (!refused) throw new InvalidDataException("Foreign folder accepted");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
        Console.WriteLine("PASS explicit companion install, idempotence and foreign-file preservation");
    }
}
