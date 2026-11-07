using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Keep exact original headless verification and elevated PresentMon
        // control BEFORE initializing any desktop UI.
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

        // Verify actual WPF border pixels from the shipped assets in CI,
        // rather than accepting a package that builds with an invisible UI.
        if (args.Length == 1 && args[0] == "--verify-border-ui")
            return VerifyBorderUi();

        if (args.Length == 3 && args[0] == PresentMonCaptureController.CliSwitch)
            return PresentMonCaptureController.ExecuteElevated(args[1], args[2]);

        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
        };
        app.Run(new MainWindow());
        return 0;
    }

    // Headless STA WPF regression: render the REAL interface root at regular
    // client size and prove yellow/cyan pixels reach the composed bitmap.
    // No game launch, no profiler or source installation, no art mutation.
    private static int VerifyBorderUi()
    {
        try
        {
            var app = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
            };
            var window = new MainWindow();
            var visual = window.Content as System.Windows.FrameworkElement
                ?? throw new InvalidOperationException("Missing root WPF visual");
            var first = window.OuterBorderArtwork.Source
                as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException(
                    "Border Source was null BEFORE layout (initial assignment regression)");
            if (first.PixelWidth != 1280 || first.PixelHeight != 760)
                throw new InvalidOperationException("Wrong initial regular border dimensions");

            const int width = 1280, height = 720;
            visual.Measure(new System.Windows.Size(width, height));
            visual.Arrange(new System.Windows.Rect(0, 0, width, height));
            visual.UpdateLayout();

            var regular = window.OuterBorderArtwork.Source
                as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Border Source disappeared after layout");
            if (regular.PixelWidth != 1280 || regular.PixelHeight != 760)
                throw new InvalidOperationException("Regular border wasn't selected");

            var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            screenshot.Render(visual);
            int[] RgbAt(int x, int y)
            {
                var bgra = new byte[4];
                screenshot.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1),
                    bgra, 4, 0);
                return [bgra[2], bgra[1], bgra[0]];
            }
            var yellow = RgbAt(307, 9);
            var cyan = RgbAt(1024, 9);
            Console.WriteLine($"Real WPF border pixels: yellow={string.Join(',', yellow)}, " +
                $"cyan={string.Join(',', cyan)}");
            if (yellow[0] < 140 || yellow[1] < 120 ||
                cyan[1] < 100 || cyan[2] < 110)
                throw new InvalidOperationException(
                    "Owner border PNG is NOT visible in the composed WPF window");

            var smallWindow = new MainWindow { Width = 1080, Height = 640 };
            var smallRoot = smallWindow.Content as System.Windows.FrameworkElement
                ?? throw new InvalidOperationException("Missing small WPF root");
            smallRoot.Measure(new System.Windows.Size(1080, 600));
            smallRoot.Arrange(new System.Windows.Rect(0, 0, 1080, 600));
            smallRoot.UpdateLayout();
            var small = smallWindow.OuterBorderArtwork.Source
                as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Small border Source was null");
            if (small.PixelWidth != 1080 || small.PixelHeight != 640)
                throw new InvalidOperationException(
                    $"Small border not selected: {small.PixelWidth}x{small.PixelHeight}");

            Console.WriteLine("PASS original border pixels rendered; regular and small image sources selected");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL actual WPF border rendering: " + ex);
            return 4;
        }
    }
}
