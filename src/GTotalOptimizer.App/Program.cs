using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Real packaged app self-check. CI executes THIS win-x64 apphost from an
        // unrelated working directory; no GUI is created and no game is touched.
        if (args.Length == 1 && args[0] == "--verify-package")
        {
            var root = PortablePackagePaths.CurrentPackageRoot();
            var verification = ComponentBundleVerifier.Inspect(root);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = verification.Ready,
                root,
                manifest = Path.Combine(root, "components", "manifest.json"),
                process = Environment.ProcessPath,
                details = verification.Detail
            }));
            return verification.Ready ? 0 : 2;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
        return 0;
    }
}
