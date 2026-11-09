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
            // The owner ICO must be present in the actual WPF window, not
            // merely in the EXE metadata: titlebar/Alt-Tab use Window.Icon.
            if (window.Icon is not System.Windows.Media.Imaging.BitmapSource appIcon ||
                appIcon.PixelWidth < 16 || appIcon.PixelHeight < 16)
                throw new InvalidOperationException(
                    "Canonical GOT LAG WPF window icon not embedded or decodable");
            Console.WriteLine("PASS canonical GOT LAG icon loaded by actual WPF window");
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

            // V remains 3 DIPs left and pinned to the same top. After the
            // corrected outer border was uploaded, V now ends 6 DIPs
            // below ContentRegion, with the inner frame unchanged.
            var rootTopLeft = window.ContentRegion.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            var portraitTopLeft = window.VPortraitHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            if (Math.Abs(rootTopLeft.X - portraitTopLeft.X - 3) > 0.5 ||
                Math.Abs(rootTopLeft.Y - portraitTopLeft.Y) > 0.5)
                throw new InvalidOperationException(
                    $"V left/top anchor incorrect: region={rootTopLeft}, V={portraitTopLeft}");
            var extension = window.VPortraitHost.ActualHeight -
                            window.ContentRegion.ActualHeight;
            if (Math.Abs(extension - 6.0) > 0.5)
                throw new InvalidOperationException(
                    $"V must stop at upper outer-border line; host delta={extension:0.###}");
            var imageHeight = window.VPortraitHost.ActualHeight;
            var expectedWidth = imageHeight * 482.0 / 928.0 + 3.0;
            var reservedWidth = Math.Clamp(imageHeight * 743.0 / 2117.0, 170.0, 245.0);
            if (Math.Abs(window.VPortraitHost.ActualWidth - expectedWidth) > 1.0 ||
                Math.Abs(window.ArtworkColumn.Width.Value - reservedWidth) > 1.0 ||
                window.VPortraitHost.ActualWidth < reservedWidth + 65.0)
                throw new InvalidOperationException(
                    "V must keep original left column while the new art flows right");
            var photoClip = window.VPortraitHost.Clip ??
                throw new InvalidOperationException("V portrait has no clipping geometry");
            if (photoClip.FillContains(new System.Windows.Point(
                    1, window.VPortraitHost.ActualHeight - 1)) ||
                photoClip.FillContains(new System.Windows.Point(1, 1)) ||
                !photoClip.FillContains(new System.Windows.Point(
                    16, window.VPortraitHost.ActualHeight - 1)))
                throw new InvalidOperationException(
                    "V must crop only its two true corner wedges after uniform resize");
            var rootGrid = (System.Windows.Controls.Grid)visual;
            var chassis = rootGrid.Children
                .OfType<GTotalOptimizer.App.Visuals.ContentChassisOutline>().Single();
            var zOuter = System.Windows.Controls.Panel.GetZIndex(window.OuterBorderArtwork);
            var zInner = System.Windows.Controls.Panel.GetZIndex(chassis);
            var zSky = System.Windows.Controls.Panel.GetZIndex(window.SkylineHost);
            var zPortrait = System.Windows.Controls.Panel.GetZIndex(window.VPortraitHost);
            var contentGrid = (System.Windows.Controls.Grid)System.Windows.Media.VisualTreeHelper
                .GetParent(window.CapturePage);
            var zControls = System.Windows.Controls.Panel.GetZIndex(contentGrid);
            if (!(zOuter > zInner && zSky < zPortrait && zPortrait < zControls) ||
                !window.ContentRegion.Children.Contains(window.VPortraitHost))
                throw new InvalidOperationException(
                    $"V must extend behind controls and above skyline: " +
                    $"outer={zOuter}, inner={zInner}, sky={zSky}, V={zPortrait}, UI={zControls}");

            var colored = window.VColorImage.Source as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("New V color art missing");
            var black = window.VBlackImage.Source as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("New V black art missing");
            if (colored.PixelWidth != 482 || colored.PixelHeight != 928 ||
                black.PixelWidth != 482 || black.PixelHeight != 928 ||
                window.VImageLayers.OpacityMask is not null ||
                window.VColorImage.Stretch != System.Windows.Media.Stretch.Uniform ||
                window.VBlackImage.Stretch != System.Windows.Media.Stretch.Uniform)
                throw new InvalidOperationException("V owner PNG pair/mask/scale mismatch");
            var sky = window.CityImage.Source as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Owner header missing");
            if (sky.PixelWidth != 1870 || sky.PixelHeight != 652 ||
                Math.Abs(window.SkylineHost.ActualHeight - 174) > 0.5)
                throw new InvalidOperationException("Updated skyline pixel geometry mismatch");
            if (window.ContentRegion.Children.OfType<System.Windows.FrameworkElement>()
                .Any(child => child.Name == "VAtmosphereHost"))
                throw new InvalidOperationException("Obsolete WPF halo still present");
            var pageBrush = rootGrid.Background as System.Windows.Media.SolidColorBrush;
            var gameBrush = window.GameRootPanel.Background as System.Windows.Media.SolidColorBrush;
            if (pageBrush?.Color != System.Windows.Media.Colors.Black ||
                gameBrush?.Color != System.Windows.Media.Colors.Black)
                throw new InvalidOperationException("App canvas and Game Root must be true black");
            var skyFade = window.SkylineHost.Children.OfType<System.Windows.Shapes.Rectangle>()
                .Single().Fill as System.Windows.Media.LinearGradientBrush;
            if (skyFade?.GradientStops.Last().Color != System.Windows.Media.Colors.Black)
                throw new InvalidOperationException("Header fade must end in true black");
            Console.WriteLine("PASS new 482x928 V pair flows right over reserved column, true-black background and owner header");

            var regular = window.OuterBorderArtwork.Source
                as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Border Source disappeared after layout");
            if (regular.PixelWidth != 1280 || regular.PixelHeight != 760 ||
                !ReferenceEquals(regular, first))
                throw new InvalidOperationException("Regular border must remain the same bitmap after layout");

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
            // Verify the ACTUAL painted bottom stroke instead of inspecting
            // an unfilled StreamGeometry through GetFlattenedPathGeometry:
            // WPF may expose zero filled PathFigures for an open outline.
            // A downward jog would place the central and right spans 3 DIPs
            // BELOW this common raised baseline, so these probes would fail.
            const int innerY = height - 30; // 27-DIP inset, 3-DIP raise
            foreach (var x in new[] { 420, 600, 950, 1110 })
            {
                var lineVisible = false;
                for (var y = innerY - 1; y <= innerY + 1; y++)
                {
                    var rgb = RgbAt(x, y);
                    var yellowLine = rgb[0] > 90 && rgb[1] > 90 &&
                                     rgb[0] - rgb[2] > 25;
                    var cyanLine = rgb[1] > 85 && rgb[2] > 90 &&
                                   rgb[2] - rgb[0] > 25;
                    if (yellowLine || cyanLine)
                        lineVisible = true;
                }
                if (!lineVisible)
                    throw new InvalidOperationException(
                        $"Raised inner border has a gap/step at x={x}, y={innerY}");
            }
            Console.WriteLine("PASS WPF raised inner border has one straight painted baseline");

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
            if (Math.Abs(smallWindow.VPortraitHost.ActualWidth -
                         (smallWindow.VPortraitHost.ActualHeight * 482.0 / 928.0 + 3.0)) > 1.5 ||
                smallWindow.VPortraitHost.ActualWidth < smallWindow.ArtworkColumn.Width.Value + 60.0)
                throw new InvalidOperationException("New owner art fails minimum-size layout");
            var small = smallWindow.OuterBorderArtwork.Source
                as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Small-window border Source is null");
            if (small.PixelWidth != 1280 || small.PixelHeight != 760)
                throw new InvalidOperationException(
                    $"Small-window raster must be Regular 1280x760, got {small.PixelWidth}x{small.PixelHeight}");
            // Re-layout an existing window at a different size: never swap Source.
            smallRoot.Measure(new System.Windows.Size(1190, 680));
            smallRoot.Arrange(new System.Windows.Rect(0, 0, 1190, 680));
            smallRoot.UpdateLayout();
            if (Math.Abs(smallWindow.VPortraitHost.ActualWidth -
                         (smallWindow.VPortraitHost.ActualHeight * 482.0 / 928.0 + 3.0)) > 1.5 ||
                smallWindow.VPortraitHost.ActualWidth < smallWindow.ArtworkColumn.Width.Value + 60.0)
                throw new InvalidOperationException("New owner art fails live window resize");
            Console.WriteLine("PASS new V painted transition follows normal, minimum and resized layouts");
            if (!ReferenceEquals(smallWindow.OuterBorderArtwork.Source, small))
                throw new InvalidOperationException("Regular border swapped on live window resize");

            Console.WriteLine("PASS Regular border bitmap rendered at both sizes without source switching");
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
