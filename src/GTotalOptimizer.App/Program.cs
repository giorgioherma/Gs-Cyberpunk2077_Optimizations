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

            // Round 6 keeps the exact shared portrait mask and uses five
            // radial/elliptical WPF lighting layers + one gentle right fade.
            if (window.VImageLayers.OpacityMask is not System.Windows.Media.ImageBrush)
                throw new InvalidOperationException("V pair must share one opacity mask");
            var lightingLayers = window.VAtmosphereHost.Children
                .OfType<System.Windows.Shapes.Rectangle>().ToArray();
            if (lightingLayers.Length != 6 ||
                window.VMainGlow.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VUpperHalo.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VLowerHalo.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VTopArch.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VBottomSkirt.Fill is not System.Windows.Media.RadialGradientBrush ||
                window.VRightFade.Fill is not System.Windows.Media.LinearGradientBrush)
                throw new InvalidOperationException(
                    "Round-6 WPF atmosphere requires outward light, top arch, lower skirt and subtle side fade");
            var haloLeft = window.VAtmosphereHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            double expectedHaloWidth = window.ArtworkColumn.Width.Value + 245.0;
            if (Math.Abs(haloLeft.X - rootTopLeft.X) > .75 ||
                Math.Abs(window.VAtmosphereHost.ActualWidth - expectedHaloWidth) > 1.5 ||
                window.VAtmosphereHost.ActualWidth <= window.VPortraitHost.ActualWidth + 220.0)
                throw new InvalidOperationException(
                    $"Halo placement invalid: left={haloLeft.X:0.#}, " +
                    $"width={window.VAtmosphereHost.ActualWidth:0.#}, " +
                    $"expected={expectedHaloWidth:0.#}");
            // Skyline behind illumination, controls in front. The portrait
            // pair's own host geometry remains unchanged.
            var skylineZ = System.Windows.Controls.Panel.GetZIndex(window.SkylineHost);
            var haloZ = System.Windows.Controls.Panel.GetZIndex(window.VAtmosphereHost);
            var controls = (System.Windows.Controls.Grid)System.Windows.Media.VisualTreeHelper
                .GetParent(window.CapturePage);
            var contentZ = System.Windows.Controls.Panel.GetZIndex(controls);
            if (!(skylineZ < haloZ && haloZ < contentZ))
                throw new InvalidOperationException(
                    $"Incorrect skyline/halo/controls layering: {skylineZ}/{haloZ}/{contentZ}");
            Console.WriteLine("PASS skyline behind V-origin WPF glow; controls remain in front");
            Console.WriteLine("PASS shared V mask and separate top arch / bottom skirt shaping");


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
            // Prove the light is actually composited into the real window,
            // not simply present as XAML objects hidden by skyline/control art.
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
            double portraitRight = portraitTopLeft.X + window.VPortraitHost.ActualWidth;
            int probeX = Math.Clamp((int)Math.Round(portraitRight + 9), 0, width - 1);
            int midY = Math.Clamp((int)Math.Round(rootTopLeft.Y +
                                  window.ContentRegion.ActualHeight * .50), 0, height - 1);
            int midGain = RgbAt(probeX, midY)[0] - RedWithoutHalo(probeX, midY);
            if (midGain < 25)
                throw new InvalidOperationException(
                    $"Round-6 V-origin light isn't visible outside V: red gain={midGain}");
            Console.WriteLine($"PASS outward V-origin glow visible in real WPF UI, middle red gain={midGain}");

            // Separately render the light field to prove its spatial SHAPE:
            // left-to-right illumination, dark arch UP TOP and skirt BELOW.
            // This avoids opaque card panels corrupting the contour probes.
            int fieldW = Math.Max(1, (int)Math.Round(window.VAtmosphereHost.ActualWidth));
            int fieldH = Math.Max(1, (int)Math.Round(window.VAtmosphereHost.ActualHeight));
            var field = new System.Windows.Media.Imaging.RenderTargetBitmap(
                fieldW, fieldH, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            field.Render(window.VAtmosphereHost);
            int FieldRed(double rx, double ry)
            {
                var px = new byte[4];
                int xx = Math.Clamp((int)Math.Round((fieldW - 1) * rx), 0, fieldW - 1);
                int yy = Math.Clamp((int)Math.Round((fieldH - 1) * ry), 0, fieldH - 1);
                field.CopyPixels(new System.Windows.Int32Rect(xx, yy, 1, 1), px, 4, 0);
                return px[2];
            }
            int near = FieldRed(.42, .52);
            int far = FieldRed(.86, .52);
            int archedTop = FieldRed(.69, .07);
            int openCenter = FieldRed(.69, .49);
            int skirtBottom = FieldRed(.69, .94);
            Console.WriteLine($"Round-6 shape red: near={near}, far={far}, " +
                              $"topArch={archedTop}, center={openCenter}, lowerSkirt={skirtBottom}");
            if (near < far + 20 || openCenter < archedTop + 15 ||
                openCenter < skirtBottom + 15)
                throw new InvalidOperationException(
                    "Round-6 light field lost its V-origin falloff, top arch or bottom skirt");
            Console.WriteLine("PASS shaped V-outward light, dark top arch and lower skirt");


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
