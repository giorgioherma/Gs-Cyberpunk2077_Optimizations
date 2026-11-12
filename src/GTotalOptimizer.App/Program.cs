using System.Collections.Concurrent;
using System.Diagnostics;
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

        // Render the packaged UI, not owner-selected aesthetic coordinates.
        if (args.Length == 1 &&
            (args[0] == "--verify-wpf-ui" || args[0] == "--verify-border-ui"))
            return VerifyWpfUi();

        if (args.Length == 1 && args[0] == "--verify-game-process-events")
            return VerifyGameProcessEvents();

        if (args.Length == 3 && args[0] == PresentMonCaptureController.CliSwitch)
            return PresentMonCaptureController.ExecuteElevated(args[1], args[2]);

        var app = new System.Windows.Application
        {
            ShutdownMode = System.Windows.ShutdownMode.OnMainWindowClose
        };
        app.Run(new MainWindow());
        return 0;
    }

    // Windows CI: exercise the real OS notification and process-handle exit
    // semantics without Cyberpunk or elevation. A missing WMI service is
    // supported: only the StartTrace portion falls back to slow polling.
    private static int VerifyGameProcessEvents()
    {
        try
        {
            using var events = new GameProcessEvents("cmd.exe");
            var starts = new ConcurrentQueue<int>();
            var exits = new ConcurrentQueue<int>();
            events.Started += starts.Enqueue;
            events.Exited += exits.Enqueue;
            var subscribed = events.TryStartNotifications(out var issue);
            using var child = Process.Start(new ProcessStartInfo(
                "cmd.exe", "/D /C ping -n 4 127.0.0.1 >NUL")
            {
                UseShellExecute = false,
                CreateNoWindow = true
            }) ?? throw new InvalidOperationException("Could not start Windows child fixture.");
            var pid = child.Id;
            if (!events.Track(pid))
                throw new InvalidOperationException("Could not attach process-exit notification.");
            if (!child.WaitForExit(15000))
            {
                child.Kill(entireProcessTree: true);
                throw new TimeoutException("Windows child fixture did not exit.");
            }
            if (!SpinWait.SpinUntil(() => exits.Contains(pid),
                    TimeSpan.FromSeconds(8)))
                throw new InvalidOperationException("Windows process exit notification was missed.");
            if (subscribed && !SpinWait.SpinUntil(() => starts.Contains(pid),
                    TimeSpan.FromSeconds(10)))
                throw new InvalidOperationException("WMI process-start notification was missed.");
            Console.WriteLine("PASS Windows process-exit event" +
                (subscribed ? " and WMI start trace" :
                    " (WMI service unavailable; 10-second fallback applies: " + issue + ")"));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL Windows game process notifications: " + ex);
            return 5;
        }
    }

    // Verify real packaged WPF operation without policing the owner's art.
    // The old --verify-border-ui alias stays for existing developer scripts.
    private static int VerifyWpfUi()
    {
        try
        {
            var app = new System.Windows.Application
            {
                ShutdownMode = System.Windows.ShutdownMode.OnExplicitShutdown
            };

            static void VerifyAtSize(MainWindow window, double width, double height)
            {
                var root = window.Content as System.Windows.FrameworkElement
                    ?? throw new InvalidOperationException("Missing WPF root");

                if (!double.IsFinite(width) || !double.IsFinite(height) ||
                    width <= 0 || height <= 0)
                    throw new InvalidOperationException("Invalid WPF window dimensions");

                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new System.Windows.Rect(0, 0, width, height));
                root.UpdateLayout();

                // Scrollable panels must not paint a dotted focus rectangle over
                // the owner artwork; keyboard focus and scrolling stay enabled.
                foreach (var scroll in new[]
                         {
                             window.CapturePage, window.CaptureConsoleScroll,
                             window.ResultsScrollViewer
                         })
                {
                    if (scroll.FocusVisualStyle is not null)
                        throw new InvalidOperationException(
                            "Unexpected scroll-pane focus adorner: " + scroll.Name);
                }

                // Buttons and the editable game path must retain Tab focus.
                foreach (var control in new System.Windows.Controls.Control[]
                         {
                             window.RefreshButton, window.BrowseButton,
                             window.GamePathBox, window.InstallButton,
                             window.LaunchButton, window.CollectButton,
                             window.ContinueButton, window.OpenResultsButton
                         })
                {
                    if (!control.Focusable || !control.IsTabStop)
                        throw new InvalidOperationException(
                            "Keyboard Tab navigation disabled: " + control.Name);
                }

                // Prevent any later layout change from shrinking reference
                // cards or independently transforming the shared neon gradient.
                var outlines = new[] {
                    window.StepOneOutline, window.StepTwoOutline, window.StepThreeOutline
                };
                System.Windows.Controls.Grid? stageRow = null;
                foreach (var outline in outlines)
                {
                    if (!outline.SpatialStroke ||
                        outline.Parent is not System.Windows.Controls.Grid card ||
                        card.Parent is not System.Windows.Controls.Grid parent ||
                        parent.ColumnDefinitions.Count != 3 ||
                        Math.Abs(parent.Height - 228.0) > .1)
                        throw new InvalidOperationException(
                            "Original direct-card layout or spatial neon gradient lost: " + outline.Name);
                    stageRow ??= parent;
                    if (!ReferenceEquals(stageRow, parent))
                        throw new InvalidOperationException(
                            "Workflow cards no longer share a single full-width row");
                }

                // Check user-owned assets are present and decodable, never
                // hardcode their size, location or decorative composition.
                var art = new System.Windows.Controls.Image[]
                {
                    window.OuterBorderArtwork, window.GcoHeaderLogo,
                    window.VColorImage, window.VBlackImage,
                    window.CityImage, window.PuddleOverflowImage
                };
                foreach (var image in art)
                {
                    if (image.Source is not System.Windows.Media.Imaging.BitmapSource bitmap ||
                        bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
                        throw new InvalidOperationException(
                            "GCO artwork missing/undecodable: " + image.Name);
                }

                // Verify core affordances exist in the real visual tree.
                // Their enabled state is gated by verified profiler sessions.
                var controls = new System.Windows.FrameworkElement[]
                {
                    window.GamePathBox, window.BrowseButton, window.RefreshButton,
                    window.InstallButton, window.LaunchButton, window.CollectButton,
                    window.ContinueButton, window.TopTabsHost, window.StepTwoHotkey
                };
                foreach (var control in controls)
                {
                    if (control.ActualWidth <= 0 || control.ActualHeight <= 0)
                        throw new InvalidOperationException(
                            "Essential WPF control failed layout: " + control.Name);
                }

                // The taller multiline-console footer must not enlarge Continue
                // or pull it into the workflow cards. Its intrinsic height stays
                // compact, but enabled/disabled gating and keyboard focus remain.
                if (window.ContinueButton.VerticalAlignment != System.Windows.VerticalAlignment.Bottom ||
                    window.ContinueButton.ActualHeight > 54)
                    throw new InvalidOperationException(
                        "Continue enlarged by the console footer instead of retaining compact size");

                if (Math.Abs(width - 1280) < 1 && Math.Abs(height - 760) < 1)
                {
                    var cardBottom = window.StepThreeOutline.TranslatePoint(
                        new System.Windows.Point(0, window.StepThreeOutline.ActualHeight), root).Y;
                    var continueTop = window.ContinueButton.TranslatePoint(
                        new System.Windows.Point(0, 0), root).Y;
                    if (continueTop - cardBottom < 10)
                        throw new InvalidOperationException(
                            "Continue overlaps or crowds the original Capture workflow cards");
                }

                if (window.StepTwoHotkey.Text != "STOP / START — F11")
                    throw new InvalidOperationException(
                        "Capture card is missing the explicit F11 start/stop instructions");

                // Render the actual three owner-approved SVG surfaces and verify
                // disabled, available, and journal-recovery transitions. WPF,
                // not the SVG, must own the changing accessible labels.
                static void CheckStage(System.Windows.Controls.Button button, string variant)
                {
                    button.ApplyTemplate();
                    var skin = button.Template.FindName("StageArtwork", button)
                        as GTotalOptimizer.App.Visuals.ApprovedStageArtwork;
                    if (skin is null || skin.Variant != variant ||
                        string.IsNullOrWhiteSpace(skin.SvgSource) ||
                        skin.SvgSource.Contains("<text", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "Incorrect approved stage artwork or baked label: " + button.Name + "/" + variant);
                }
                foreach (var button in new[] {
                    window.InstallButton, window.LaunchButton, window.CollectButton
                })
                {
                    button.IsEnabled = false;
                    CheckStage(button, "Unavailable");
                    button.IsEnabled = true;
                    CheckStage(button, "Yellow");
                    button.IsEnabled = false;
                }
                window.InstallButton.Style = (System.Windows.Style)window.FindResource("RestoreStageButton");
                window.InstallButton.IsEnabled = true;
                CheckStage(window.InstallButton, "Restore");
                window.InstallButton.IsEnabled = false;
                CheckStage(window.InstallButton, "Unavailable");

                // The Capture page is a general-status UI, not a hidden
                // hover-based diagnostics surface. Check the actual loaded
                // WPF controls, not merely source text.
                if (window.StatusText.ToolTip is not null ||
                    window.GamePathBox.ToolTip is not null ||
                    window.DependencyText.ToolTip is not null)
                    throw new InvalidOperationException(
                        "Capture UI must not display hover tooltips");

                void Render()
                {
                    var frame = new System.Windows.Media.Imaging.RenderTargetBitmap(
                        (int)Math.Ceiling(width), (int)Math.Ceiling(height),
                        96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    frame.Render(root);
                }

                Render();
                window.ResultsPage.Visibility = System.Windows.Visibility.Visible;
                root.UpdateLayout();
                Render();
                window.ResultsPage.Visibility = System.Windows.Visibility.Collapsed;
                root.UpdateLayout();
            }


            // WPF-rendered 96-DPI round-trip proof: the original 1280x760
            // Normal pixels must survive two fullscreen cycles byte-for-byte.
            // This checks functionality, not owner-selected decorative colors.
            static byte[] Snapshot(MainWindow window, double width, double height)
            {
                var root = window.Content as System.Windows.FrameworkElement
                    ?? throw new InvalidOperationException("Missing WPF root");
                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new System.Windows.Rect(0, 0, width, height));
                root.UpdateLayout();
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    (int)width, (int)height, 96, 96,
                    System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(root);
                var pixels = new byte[(int)width * (int)height * 4];
                bmp.CopyPixels(pixels, (int)width * 4, 0);
                return pixels;
            }

            var roundTrip = new MainWindow();
            var originalTree = roundTrip.CapturePage.Content;
            var originalConsoleMargin = roundTrip.ConsoleTextHost.Margin;
            static System.Windows.Point Origin(MainWindow w, System.Windows.FrameworkElement element)
            {
                var root = (System.Windows.FrameworkElement)w.Content;
                return element.TranslatePoint(new System.Windows.Point(0, 0), root);
            }
            var referenceCardOrigin = Origin(roundTrip, roundTrip.StepOneOutline);
            var referenceRootOrigin = Origin(roundTrip, roundTrip.GameRootOutline);
            // First rendering can initialize WPF's embedded SVG brushes.
            // Require a stable second Normal render before round-trip testing.
            var warmPixels = Snapshot(roundTrip, 1280, 760);
            var baselinePixels = Snapshot(roundTrip, 1280, 760);
            if (!warmPixels.AsSpan().SequenceEqual(baselinePixels))
                throw new InvalidOperationException(
                    "Offscreen Normal rendering is not stable even before maximizing");
            var baselineCardHeight = roundTrip.StepOneOutline.ActualHeight;
            var baselineIconHeight = roundTrip.InstallStageIcon.ActualHeight;
            var baselineButtonHeight = roundTrip.InstallButton.ActualHeight;
            for (var cycle = 0; cycle < 2; cycle++)
            {
                // Offscreen windows do not always deliver native StateChanged.
                roundTrip.WindowState = System.Windows.WindowState.Maximized;
                roundTrip.ApplyMaximizedCaptureLayout();
                Snapshot(roundTrip, 1700, 900);
                if (roundTrip.CapturePage.Content is not System.Windows.Controls.Grid ||
                    roundTrip.StepOneOutline.ActualHeight <= baselineCardHeight + 15 ||
                    Math.Abs(roundTrip.InstallStageIcon.ActualHeight - baselineIconHeight) > .1 ||
                    Math.Abs(roundTrip.InstallButton.ActualHeight - baselineButtonHeight) > .1 ||
                    !roundTrip.StepOneOutline.SpatialStroke ||
                    !roundTrip.StepTwoOutline.SpatialStroke ||
                    !roundTrip.StepThreeOutline.SpatialStroke)
                    throw new InvalidOperationException(
                        "Maximized layout did not grow cards while preserving native icon/button sizes and shared neon: " +
                        $"container={roundTrip.CapturePage.Content?.GetType().Name}, " +
                        $"viewport={roundTrip.CapturePage.ActualHeight:F1}, " +
                        $"gridHeight={(roundTrip.CapturePage.Content as System.Windows.Controls.Grid)?.Height:F1}, " +
                        $"card={roundTrip.StepOneOutline.ActualHeight:F1}/{baselineCardHeight:F1}, " +
                        $"icon={roundTrip.InstallStageIcon.ActualHeight:F1}/{baselineIconHeight:F1}, " +
                        $"button={roundTrip.InstallButton.ActualHeight:F1}/{baselineButtonHeight:F1}, " +
                        $"spatial={roundTrip.StepOneOutline.SpatialStroke}/" +
                        $"{roundTrip.StepTwoOutline.SpatialStroke}/{roundTrip.StepThreeOutline.SpatialStroke}");

                roundTrip.WindowState = System.Windows.WindowState.Normal;
                roundTrip.ApplyMaximizedCaptureLayout();
                var restoredPixels = Snapshot(roundTrip, 1280, 760);
                var sameTree = ReferenceEquals(originalTree, roundTrip.CapturePage.Content);
                var sameConsole = roundTrip.ConsoleTextHost.Margin == originalConsoleMargin;
                var sameCard = Math.Abs(roundTrip.StepOneOutline.ActualHeight - baselineCardHeight) <= .1;
                var samePixels = baselinePixels.AsSpan().SequenceEqual(restoredPixels);
                if (!sameTree || !sameConsole || !sameCard || !samePixels)
                {
                    var changed = 0;
                    var minX = 1280; var maxX = -1; var minY = 760; var maxY = -1;
                    for (var offset = 0; offset < baselinePixels.Length; offset += 4)
                    {
                        if (baselinePixels[offset] == restoredPixels[offset] &&
                            baselinePixels[offset + 1] == restoredPixels[offset + 1] &&
                            baselinePixels[offset + 2] == restoredPixels[offset + 2] &&
                            baselinePixels[offset + 3] == restoredPixels[offset + 3])
                            continue;
                        changed++;
                        var px = offset / 4 % 1280;
                        var py = offset / 4 / 1280;
                        minX = Math.Min(minX, px); maxX = Math.Max(maxX, px);
                        minY = Math.Min(minY, py); maxY = Math.Max(maxY, py);
                    }
                    throw new InvalidOperationException(
                        $"Normal restore cycle {cycle + 1}: tree={sameTree}, " +
                        $"console={sameConsole}, card={sameCard}, pixels={samePixels}, " +
                        $"changed={changed}, bounds=({minX},{minY})-({maxX},{maxY}), " +
                        $"scroll={roundTrip.CapturePage.VerticalOffset:F1}, " +
                        $"cardHeight={roundTrip.StepOneOutline.ActualHeight:F1}/{baselineCardHeight:F1}, " +
                        $"cardOrigin={Origin(roundTrip, roundTrip.StepOneOutline)}/{referenceCardOrigin}, " +
                        $"gameOrigin={Origin(roundTrip, roundTrip.GameRootOutline)}/{referenceRootOrigin}");
                }
            }

            var normal = new MainWindow();
            VerifyAtSize(normal, normal.Width, normal.Height);

            var minimum = new MainWindow();
            VerifyAtSize(minimum, minimum.MinWidth, minimum.MinHeight);
            // Larger sizes should still render and remain navigable, even
            // though responsive card scaling was intentionally reverted.
            VerifyAtSize(new MainWindow(), 1700, 900);
            VerifyAtSize(new MainWindow(), 1920, 1080);

            Console.WriteLine(
                "PASS packaged WPF Normal-Maximized-Normal pixel identity, expanded cards, and both pages");
            app.Shutdown();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL packaged WPF UI smoke: " + ex);
            return 4;
        }
    }
}
