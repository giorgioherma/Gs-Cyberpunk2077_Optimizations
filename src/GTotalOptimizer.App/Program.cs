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
            // Negative left margin enlarges the *host* by 3 DIPs, but the
            // Image's Uniform render preserves source pixels' 743:2117 ratio.
            var expectedWidth = window.VPortraitHost.ActualHeight * 743.0 / 2117.0;
            if (Math.Abs(window.VPortraitHost.ActualWidth - expectedWidth - 3) > 1.0)
                throw new InvalidOperationException("V proportional content width changed");
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
            var zPortrait = System.Windows.Controls.Panel.GetZIndex(window.VPortraitHost);
            var zInner = System.Windows.Controls.Panel.GetZIndex(chassis);
            if (!(zOuter > zPortrait && zPortrait > zInner))
                throw new InvalidOperationException(
                    $"Wrong WPF compositing priority: outer={zOuter}, V={zPortrait}, inner={zInner}");
            if (!rootGrid.Children.Contains(window.VPortraitHost))
                throw new InvalidOperationException(
                    "V must be a direct root overlay to cross the inner border");

            // No cloud image or generated texture. The two original V
            // images share ONE WPF mask, while a static gradient halo/vignette
            // bleeds behind the UI without moving portrait geometry.
            if (window.VImageLayers.OpacityMask is not System.Windows.Media.ImageBrush)
                throw new InvalidOperationException("V pair must share one opacity mask");
            var halos = window.VAtmosphereHost.Children
                .OfType<System.Windows.Shapes.Rectangle>().ToArray();
            if (halos.Length != 3 ||
                window.VUpperHalo.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VLowerHalo.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VDarkVignette.Fill is not System.Windows.Media.LinearGradientBrush)
                throw new InvalidOperationException(
                    "Original-concept lighting requires two WPF halos and one dark vignette");
            var haloLeft = window.VAtmosphereHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            double expectedHaloWidth = window.ArtworkColumn.Width.Value + 185.0;
            if (Math.Abs(haloLeft.X - rootTopLeft.X) > .75 ||
                Math.Abs(window.VAtmosphereHost.ActualWidth - expectedHaloWidth) > 1.5 ||
                window.VAtmosphereHost.ActualWidth <= window.VPortraitHost.ActualWidth + 160.0)
                throw new InvalidOperationException(
                    $"Halo placement invalid: left={haloLeft.X:0.#}, " +
                    $"width={window.VAtmosphereHost.ActualWidth:0.#}, " +
                    $"expected={expectedHaloWidth:0.#}");
            // The blue skyline background needs to be behind the halo,
            // while the live content panel must render above the atmosphere.
            var skylineZ = System.Windows.Controls.Panel.GetZIndex(window.SkylineHost);
            var haloZ = System.Windows.Controls.Panel.GetZIndex(window.VAtmosphereHost);
            var controls = (System.Windows.Controls.Grid)System.Windows.Media.VisualTreeHelper
                .GetParent(window.CapturePage);
            var contentZ = System.Windows.Controls.Panel.GetZIndex(controls);
            if (!(skylineZ < haloZ && haloZ < contentZ))
                throw new InvalidOperationException(
                    $"Incorrect skyline/halo/controls layering: {skylineZ}/{haloZ}/{contentZ}");
            Console.WriteLine("PASS skyline behind visible WPF halo; controls remain in front");
            Console.WriteLine("PASS shared V mask, static yellow halos and contained dark vignette");

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
            // Compare actual rendered pixels with the halo hidden. A gradient
            // object existing in the tree is not proof that it can be seen.
            // Probe just beyond V's right edge at upper and lower locations,
            // away from opaque card panels. This fails if another background
            // layer obscures the halo as in the owner screenshot.
            window.VAtmosphereHost.Visibility = System.Windows.Visibility.Hidden;
            var unlit = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            unlit.Render(visual);
            window.VAtmosphereHost.Visibility = System.Windows.Visibility.Visible;
            int RedWithoutHalo(int x, int y)
            {
                var px = new byte[4];
                unlit.CopyPixels(new System.Windows.Int32Rect(x, y, 1, 1), px, 4, 0);
                return px[2];
            }
            int probeX = Math.Clamp((int)Math.Round(portraitRight + 9), 0, width - 1);
            int probeTopY = Math.Clamp((int)Math.Round(rootTopLeft.Y +
                                        window.ContentRegion.ActualHeight * .24), 0, height - 1);
            int probeBottomY = Math.Clamp((int)Math.Round(rootTopLeft.Y +
                                           window.ContentRegion.ActualHeight * .87), 0, height - 1);
            int upperGain = RgbAt(probeX, probeTopY)[0] -
                            RedWithoutHalo(probeX, probeTopY);
            int lowerGain = RgbAt(probeX, probeBottomY)[0] -
                            RedWithoutHalo(probeX, probeBottomY);
            Console.WriteLine($"Actual WPF halo red gains upper={upperGain}, lower={lowerGain}");
            if (upperGain < 35 || lowerGain < 30)
                throw new InvalidOperationException(
                    $"Halo visually obscured at x={probeX}: " +
                    $"upper={upperGain} lower={lowerGain}. Check compositing.");
            Console.WriteLine("PASS halo visibly brightens pixels beyond V in both light zones");

            // Verify the ACTUAL painted bottom stroke instead of inspecting
            // an unfilled StreamGeometry through GetFlattenedPathGeometry:
            // WPF may expose zero filled PathFigures for an open outline.
            // A downward jog would place the central and right spans 3 DIPs
            // BELOW this common raised baseline, so these probes would fail.
            const int innerY = height - 30; // 27-DIP inset, 3-DIP raise
            foreach (var x in new[] { 330, 600, 950, 1110 })
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
            if (Math.Abs(smallWindow.VAtmosphereHost.ActualWidth -
                         smallWindow.ArtworkColumn.Width.Value - 185.0) > 1.5)
                throw new InvalidOperationException("Halo fails minimum-size layout");
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
            if (Math.Abs(smallWindow.VAtmosphereHost.ActualWidth -
                         smallWindow.ArtworkColumn.Width.Value - 185.0) > 1.5)
                throw new InvalidOperationException("Halo does not follow live window resize");
            Console.WriteLine("PASS gradient atmosphere follows portrait at normal, minimum and resized layouts");
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
