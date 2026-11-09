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

            // V starts one DIP right of the thin inner border; same top and height.
            var rootTopLeft = window.ContentRegion.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            var portraitTopLeft = window.VPortraitHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            if (Math.Abs(rootTopLeft.X - portraitTopLeft.X - 1) > 0.5 ||
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
            var zContent = System.Windows.Controls.Panel.GetZIndex(window.ContentRegion);
            var zTabs = System.Windows.Controls.Panel.GetZIndex(window.TopTabsHost);
            var zSpill = System.Windows.Controls.Panel.GetZIndex(window.PuddleOverflowHost);
            if (!(zOuter > zTabs && zTabs > zSpill && zSpill > zInner &&
                  zInner > zContent && zSky < zPortrait && zPortrait < zControls) ||
                !window.ContentRegion.Children.Contains(window.VPortraitHost) ||
                !rootGrid.Children.Contains(window.PuddleOverflowHost) ||
                window.PuddleOverflowHost.IsHitTestVisible)
                throw new InvalidOperationException(
                    $"Only puddle crop can cross inner frame: outer={zOuter}, " +
                    $"tabs={zTabs}, spill={zSpill}, inner={zInner}, " +
                    $"content={zContent}, sky={zSky}, V={zPortrait}, UI={zControls}");
            var spillTopLeft = window.PuddleOverflowHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            double scale = window.VPortraitHost.ActualHeight / 928.0;
            if (Math.Abs(spillTopLeft.X - portraitTopLeft.X) > 0.5 ||
                Math.Abs(spillTopLeft.Y + window.PuddleOverflowHost.ActualHeight -
                         portraitTopLeft.Y - window.VPortraitHost.ActualHeight) > 0.75 ||
                Math.Abs(window.PuddleOverflowHost.ActualWidth - 365.0 * scale) > 0.75 ||
                Math.Abs(window.PuddleOverflowHost.ActualHeight - 49.0 * scale) > 0.75)
                throw new InvalidOperationException(
                    $"Owner crop offset/scale differs from V: base={portraitTopLeft}, spill={spillTopLeft}");
            Console.WriteLine("PASS V starts after left inner line; only owner puddle crop covers thin frame");

            // Tabs are in the upper band, outside the inner content frame.
            var tabTop = window.TopTabsHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            if (tabTop.Y < 7 || tabTop.Y + window.TopTabsHost.ActualHeight > 65 ||
                tabTop.X < 220 || window.TopTabsHost.ActualWidth < 800)
                throw new InvalidOperationException(
                    $"Tabs are not inside header margin: {tabTop} " +
                    $"size={window.TopTabsHost.ActualWidth}x{window.TopTabsHost.ActualHeight}");
            if (System.Windows.Controls.Grid.GetRow(window.CapturePage) != 0 ||
                System.Windows.Controls.Grid.GetRow(window.ResultsPage) != 0)
                throw new InvalidOperationException("GOT LAG content must start directly at inner frame");
            if (window.ConsoleTextHost.Children.OfType<GTotalOptimizer.App.Visuals.ChamferOutline>().Any() ||
                window.ConsoleTextHost.Children.OfType<System.Windows.Shapes.Path>().Any() ||
                window.ConsoleTextHost.Background is not System.Windows.Media.SolidColorBrush consoleBrush ||
                consoleBrush.Color.A != 0)
                throw new InvalidOperationException("Status console must be unboxed and slash-free");
            var rootLeft = window.GameRootPanel.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0)).X;
            var rightStart = window.SkylineHost.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0)).X;
            if (Math.Abs(rootLeft - rightStart - 15.0) > 1.5)
                throw new InvalidOperationException(
                    $"Game Root must return to original content alignment: " +
                    $"root={rootLeft}, rightStart={rightStart}");
            var heroLeft = window.HeroTitle.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0)).X;
            if (Math.Abs(heroLeft - rootLeft - 22.0) > 1.5)
                throw new InvalidOperationException(
                    $"GOT LAG headline not independently offset right: hero={heroLeft}, root={rootLeft}");
            var pathFrame = window.GamePathBox.Template.FindName("PathFrame", window.GamePathBox);
            if (pathFrame is not System.Windows.Controls.Border frame ||
                frame.CornerRadius != new System.Windows.CornerRadius(0) ||
                Math.Abs(frame.BorderThickness.Left - 1) > .2)
                throw new InvalidOperationException("Game path must use a normal rectangular TextBox frame");
            var fieldLeft = window.GamePathBox.TransformToAncestor(window.GameRootPanel)
                .Transform(new System.Windows.Point(0, 0)).X;
            var openLeft = window.BrowseButton.TransformToAncestor(window.GameRootPanel)
                .Transform(new System.Windows.Point(0, 0)).X;
            var openRight = openLeft + window.BrowseButton.ActualWidth;
            if (fieldLeft < 20 || openLeft - fieldLeft - window.GamePathBox.ActualWidth < 15 ||
                openRight > window.GameRootPanel.ActualWidth - 20)
                throw new InvalidOperationException(
                    $"Path field must clear chamfer and OPEN button: " +
                    $"fieldX={fieldLeft}, width={window.GamePathBox.ActualWidth}, " +
                    $"buttonX={openLeft}, buttonRight={openRight}");
            var refreshTop = window.RefreshButton.TransformToAncestor(visual)
                .Transform(new System.Windows.Point(0, 0));
            if (refreshTop.Y < 5 || refreshTop.Y + window.RefreshButton.ActualHeight > 66 ||
                refreshTop.X < 1020)
                throw new InvalidOperationException("REFRESH must be in the top-right header band");
            if (window.ModCountText.Margin.Right != 0 ||
                window.DependencyText.Text.Contains("MO2", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("MO2 placeholder must not occupy Page 1");
            Console.WriteLine("PASS independent GOT LAG title offset; original Game Root X; rectangular inset path; upper REFRESH; no MO2 placeholder");

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
            var spill = window.PuddleOverflowImage.Source as System.Windows.Media.Imaging.BitmapSource
                ?? throw new InvalidOperationException("Owner bottom puddle strip missing");
            if (spill.PixelWidth != 365 || spill.PixelHeight != 49 ||
                window.PuddleOverflowImage.Stretch != System.Windows.Media.Stretch.Uniform)
                throw new InvalidOperationException("Owner 365x49 RGBA strip incorrectly loaded");
            // This is not a rectangular photo pasted over the border:
            // validate the actual owner PNG alpha, not just its dimensions.
            var stripPixels = new byte[365 * 49 * 4];
            var rgbaStrip = new System.Windows.Media.Imaging.FormatConvertedBitmap(
                spill, System.Windows.Media.PixelFormats.Bgra32, null, 0);
            rgbaStrip.CopyPixels(stripPixels, 365 * 4, 0);
            int transparentPixels = 0, blackPixels = 0;
            for (int k = 0; k < stripPixels.Length; k += 4)
            {
                if (stripPixels[k + 3] == 0)
                    transparentPixels++;
                else if (stripPixels[k] < 75 &&
                         stripPixels[k + 1] < 75 &&
                         stripPixels[k + 2] < 75)
                    blackPixels++;
            }
            if (transparentPixels < 3000 || blackPixels < 7000)
                throw new InvalidOperationException(
                    $"Owner strip must be actual transparent black puddle art: " +
                    $"transparent={transparentPixels}, black={blackPixels}");
            Console.WriteLine($"PASS owner RGBA puddle crop transparent={transparentPixels}, black={blackPixels}");

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
            // Real rendered frame: the new strip must darken PART of the
            // yellow inner bottom line; elsewhere the line remains visible.
            // Compare one frame with and one without just the owner crop.
            var ownerStripVisible = window.PuddleOverflowHost.Visibility;
            window.PuddleOverflowHost.Visibility = System.Windows.Visibility.Hidden;
            var noSpillBitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            noSpillBitmap.Render(visual);
            window.PuddleOverflowHost.Visibility = ownerStripVisible;
            int obscuredLinePixels = 0;
            var baseline = new byte[4];
            for (int xx = 44; xx <= 270; xx++)
                for (int yy = 689; yy <= 691; yy++)
                {
                    noSpillBitmap.CopyPixels(new System.Windows.Int32Rect(xx, yy, 1, 1),
                        baseline, 4, 0);
                    var visible = RgbAt(xx, yy);
                    int baselineBrightness = baseline[2] + baseline[1] + baseline[0];
                    int currentBrightness = visible[0] + visible[1] + visible[2];
                    if (baselineBrightness - currentBrightness > 90)
                        obscuredLinePixels++;
                }
            if (obscuredLinePixels < 18)
                throw new InvalidOperationException(
                    $"Owner puddle crop does not actually overpaint the yellow inner line: " +
                    $"darkened border pixels={obscuredLinePixels}");
            var exposedLeftLine = RgbAt(27, 360);
            if (exposedLeftLine[0] < 100 || exposedLeftLine[1] < 100)
                throw new InvalidOperationException(
                    $"V still hides the left inner line at x27: " +
                    $"{string.Join(',', exposedLeftLine)}");
            Console.WriteLine(
                $"PASS real puddle-only overpaint darkens {obscuredLinePixels} inner-line pixels; " +
                $"x27 left line remains yellow");

            // Verify the ACTUAL painted bottom stroke instead of inspecting
            // an unfilled StreamGeometry through GetFlattenedPathGeometry:
            // WPF may expose zero filled PathFigures for an open outline.
            // A downward jog would place the central and right spans 3 DIPs
            // BELOW this common raised baseline, so these probes would fail.
            const int innerY = height - 30; // 27-DIP inset, 3-DIP raise
            foreach (var x in new[] { 310, 420, 600, 950, 1110 })
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

            // Raising V above the thin inner chassis must not make Page 2's
            // opaque results panel erase the remaining horizontal frame.
            if (window.ResultsPage.Margin.Top < 2 || window.ResultsPage.Margin.Bottom < 2)
                throw new InvalidOperationException(
                    "Opaque Analyze page must be inset from the thin inner outline");
            window.ResultsPage.Visibility = System.Windows.Visibility.Visible;
            visual.UpdateLayout();
            var resultFrame = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            resultFrame.Render(visual);
            bool upperLineVisible = false;
            var buffer = new byte[4];
            foreach (var tx in new[] { 450, 600, 780, 1000 })
            {
                for (var ty = 67; ty <= 69; ++ty)
                {
                    resultFrame.CopyPixels(new System.Windows.Int32Rect(tx, ty, 1, 1),
                        buffer, 4, 0);
                    if (buffer[0] + buffer[1] + buffer[2] > 290)
                        upperLineVisible = true;
                }
            }
            if (!upperLineVisible)
                throw new InvalidOperationException(
                    "Analyze page erased the upper inner chassis line");
            window.ResultsPage.Visibility = System.Windows.Visibility.Collapsed;
            visual.UpdateLayout();
            Console.WriteLine("PASS Analyze page retains thin inner chassis above its opaque content");

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
            var smallTabTop = smallWindow.TopTabsHost.TransformToAncestor(smallRoot)
                .Transform(new System.Windows.Point(0, 0));
            if (smallTabTop.Y + smallWindow.TopTabsHost.ActualHeight > 65 ||
                smallTabTop.X < 310)
                throw new InvalidOperationException("Responsive tabs overlap small window title");
            var smallV = smallWindow.VPortraitHost.TransformToAncestor(smallRoot)
                .Transform(new System.Windows.Point(0, 0));
            var smallSpill = smallWindow.PuddleOverflowHost.TransformToAncestor(smallRoot)
                .Transform(new System.Windows.Point(0, 0));
            if (Math.Abs(smallV.X - smallSpill.X) > 0.75 ||
                Math.Abs(smallWindow.PuddleOverflowHost.ActualWidth -
                         smallWindow.VPortraitHost.ActualHeight * 365.0 / 928.0) > 0.75 ||
                Math.Abs(smallWindow.PuddleOverflowHost.ActualHeight -
                         smallWindow.VPortraitHost.ActualHeight * 49.0 / 928.0) > 0.75)
                throw new InvalidOperationException("Owner crop must follow V at minimum size");
            var minFieldLeft = smallWindow.GamePathBox.TransformToAncestor(
                smallWindow.GameRootPanel).Transform(new System.Windows.Point(0, 0)).X;
            var minOpenLeft = smallWindow.BrowseButton.TransformToAncestor(
                smallWindow.GameRootPanel).Transform(new System.Windows.Point(0, 0)).X;
            if (minFieldLeft < 20 ||
                minOpenLeft - minFieldLeft - smallWindow.GamePathBox.ActualWidth < 15)
                throw new InvalidOperationException("Small-window path field crosses chamfer or OPEN gap");
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
            if (Math.Abs(smallWindow.PuddleOverflowHost.ActualWidth -
                         smallWindow.VPortraitHost.ActualHeight * 365.0 / 928.0) > 0.75 ||
                Math.Abs(smallWindow.PuddleOverflowHost.ActualHeight -
                         smallWindow.VPortraitHost.ActualHeight * 49.0 / 928.0) > 0.75)
                throw new InvalidOperationException("Owner crop did not follow live resize");
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
