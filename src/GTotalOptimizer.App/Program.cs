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
                    window.CityImage, window.PuddleOverflowImage,
                    window.StepOneCardArtwork, window.StepTwoCardArtwork,
                    window.StepThreeCardArtwork
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

                // Framework readiness indicators use the owner's native vector
                // resources directly under Game Root, never reconstructed icons.
                var successIcon = window.FrameworkStatusSuccessIcon;
                var failureIcon = window.FrameworkStatusFailureIcon;
                if (successIcon.Width != 16 || successIcon.Height != 16 ||
                    failureIcon.Width != 16 || failureIcon.Height != 16 ||
                    !ReferenceEquals(successIcon.Source, window.FindResource("GcoStatusSuccessIcon")) ||
                    !ReferenceEquals(failureIcon.Source, window.FindResource("GcoStatusFailureIcon")) ||
                    successIcon.Parent is not System.Windows.Controls.Grid iconRow ||
                    !ReferenceEquals(failureIcon.Parent, iconRow) ||
                    !ReferenceEquals(window.DependencyText.Parent, iconRow))
                    throw new InvalidOperationException(
                        "Native 16x16 success/failure icons not registered on the framework status line");
                window.SetFrameworkStatusIcon(null);
                if (successIcon.Visibility != System.Windows.Visibility.Collapsed ||
                    failureIcon.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Unknown framework must hide both icons");
                window.SetFrameworkStatusIcon(true);
                if (successIcon.Visibility != System.Windows.Visibility.Visible ||
                    failureIcon.Visibility != System.Windows.Visibility.Collapsed)
                    throw new InvalidOperationException("Compatible framework did not show green check");
                window.SetFrameworkStatusIcon(false);
                if (successIcon.Visibility != System.Windows.Visibility.Collapsed ||
                    failureIcon.Visibility != System.Windows.Visibility.Visible)
                    throw new InvalidOperationException("Incompatible framework did not show red X");
                window.SetFrameworkStatusIcon(null);

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
                static void CheckCard(System.Windows.Controls.Button button,
                    System.Windows.Controls.Image card, bool active)
                {
                    button.UpdateLayout();
                    card.UpdateLayout();
                    var name = active ? "ActiveCardBitmap" : "InactiveCardBitmap";
                    var expected = (System.Windows.Media.Imaging.BitmapSource)
                        card.FindResource(name);
                    var sameSource = ReferenceEquals(card.Source, expected);
                    var sameButton = ReferenceEquals(card.Tag, button);
                    var crop = card.Stretch == System.Windows.Media.Stretch.UniformToFill;
                    var clipped = card.Clip is not null &&
                        card.ActualWidth > 0 && card.ActualHeight > 0 &&
                        card.Clip.FillContains(new System.Windows.Point(card.ActualWidth / 2,
                            card.ActualHeight / 2)) &&
                        !card.Clip.FillContains(new System.Windows.Point(0, 0));
                    if (!sameButton || !sameSource || !crop || !clipped)
                        throw new InvalidOperationException(
                            $"Card artwork state/crop/clip mismatch: {card.Name}, " +
                            $"button={sameButton}, bitmap={sameSource}, crop={crop}, clip={clipped}, " +
                            $"imageType={card.Source?.GetType().Name}, " +
                            $"expectedType={expected.GetType().Name}, " +
                            $"size={card.ActualWidth:F1}x{card.ActualHeight:F1}, state={name}");
                    button.ApplyTemplate();
                    var hover = button.Template.FindName("StageHover", button)
                        as GTotalOptimizer.App.Visuals.ChamferOutline;
                    if (hover is null ||
                        hover.ActualWidth > button.ActualWidth + 0.5 ||
                        hover.ActualWidth > button.ActualHeight * (760.0 / 112.0) + 0.5)
                        throw new InvalidOperationException(
                            "Stage hover outline extends beyond the real scaled SVG shell: " + button.Name);
                }
                var stages = new[] {
                    (Button: window.InstallButton, Card: window.StepOneCardArtwork),
                    (Button: window.LaunchButton, Card: window.StepTwoCardArtwork),
                    (Button: window.CollectButton, Card: window.StepThreeCardArtwork)
                };
                foreach (var stage in stages)
                {
                    stage.Button.IsEnabled = false;
                    CheckStage(stage.Button, "Unavailable");
                    CheckCard(stage.Button, stage.Card, active: false);
                    stage.Button.IsEnabled = true;
                    CheckStage(stage.Button, "Yellow");
                    CheckCard(stage.Button, stage.Card, active: true);
                    stage.Button.IsEnabled = false;
                }
                window.InstallButton.Style = (System.Windows.Style)window.FindResource("RestoreStageButton");
                window.InstallButton.IsEnabled = true;
                CheckStage(window.InstallButton, "Restore");
                CheckCard(window.InstallButton, window.StepOneCardArtwork, active: false);
                window.InstallButton.IsEnabled = false;
                CheckStage(window.InstallButton, "Unavailable");
                CheckCard(window.InstallButton, window.StepOneCardArtwork, active: false);

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


            // Layout contract: maximize modifies existing section properties
            // only, without inserting/reparenting controls or transforming art.
            static void ArrangeOffscreen(MainWindow window, double width, double height)
            {
                var root = window.RootLayout;
                root.Measure(new System.Windows.Size(width, height));
                root.Arrange(new System.Windows.Rect(0, 0, width, height));
                root.UpdateLayout();
            }

            var structural = new MainWindow();
            ArrangeOffscreen(structural, 1280, 760);
            var originalPanel = structural.CapturePage.Content
                as System.Windows.Controls.StackPanel
                ?? throw new InvalidOperationException("Normal Capture StackPanel missing");
            var gameSection = (System.Windows.FrameworkElement)originalPanel.Children[1];
            var cards = (System.Windows.Controls.Grid)originalPanel.Children[3];
            var heroMargin = structural.HeroSection.Margin;
            var gameMargin = gameSection.Margin;
            var headingMargin = structural.CaptureWorkflowHeading.Margin;
            var statusMargin = structural.ConsoleTextHost.Margin;
            var cardHeight = cards.Height;
            var cardMinHeight = cards.MinHeight;
            var originalGameRootHeight = structural.GameRootPanel.Height;
            var cardPanels = cards.Children.Cast<System.Windows.Controls.Grid>().ToArray();
            var originalCardMargins = cardPanels.Select(card => card.Margin).ToArray();
            var iconHeight = structural.InstallStageIcon.ActualHeight;
            var buttonHeight = structural.InstallButton.ActualHeight;

            for (var cycle = 0; cycle < 2; cycle++)
            {
                structural.WindowState = System.Windows.WindowState.Maximized;
                structural.ApplyMaximizedCaptureLayout();
                ArrangeOffscreen(structural, 1700, 900);
                structural.ApplyMaximizedCaptureLayout();
                ArrangeOffscreen(structural, 1700, 900);
                if (!ReferenceEquals(structural.CapturePage.Content, originalPanel) ||
                    originalPanel.Children.Count != 4 ||
                    cards.Height <= cardHeight + 15 ||
                    cards.Height > cardHeight + 57 ||
                    structural.GameRootPanel.Height <= originalGameRootHeight + 10 ||
                    cardPanels.Any((card) => card.Margin.Left < originalCardMargins[Array.IndexOf(cardPanels, card)].Left + 18 ||
                        card.Margin.Right < originalCardMargins[Array.IndexOf(cardPanels, card)].Right + 18) ||
                    structural.CaptureWorkflowHeading.Margin.Bottom > 3 ||
                    Math.Abs(structural.InstallStageIcon.ActualHeight - iconHeight) > .1 ||
                    Math.Abs(structural.InstallButton.ActualHeight - buttonHeight) > .1 ||
                    !structural.StepOneOutline.SpatialStroke ||
                    !structural.StepTwoOutline.SpatialStroke ||
                    !structural.StepThreeOutline.SpatialStroke)
                    throw new InvalidOperationException(
                        $"Maximized must grow the same three cards with unscaled controls: " +
                        $"children={originalPanel.Children.Count}, " +
                        $"height={cards.Height:F1}/{cardHeight:F1}, " +
                        $"gameRoot={structural.GameRootPanel.Height:F1}/{originalGameRootHeight:F1}, " +
                        $"cardInset={cardPanels[0].Margin.Left:F1}/{originalCardMargins[0].Left:F1}, " +
                        $"workflowHeadingBottom={structural.CaptureWorkflowHeading.Margin.Bottom:F1}, " +
                        $"icon={structural.InstallStageIcon.ActualHeight:F1}/{iconHeight:F1}, " +
                        $"button={structural.InstallButton.ActualHeight:F1}/{buttonHeight:F1}");

                structural.WindowState = System.Windows.WindowState.Normal;
                structural.ApplyMaximizedCaptureLayout();
                ArrangeOffscreen(structural, 1280, 760);
                if (!ReferenceEquals(structural.CapturePage.Content, originalPanel) ||
                    originalPanel.Children.Count != 4 ||
                    structural.HeroSection.Margin != heroMargin ||
                    gameSection.Margin != gameMargin ||
                    structural.CaptureWorkflowHeading.Margin != headingMargin ||
                    structural.ConsoleTextHost.Margin != statusMargin ||
                    structural.GameRootPanel.Height != originalGameRootHeight ||
                    cardPanels.Where((card, index) => card.Margin != originalCardMargins[index]).Any() ||
                    cards.Height != cardHeight || cards.MinHeight != cardMinHeight)
                    throw new InvalidOperationException(
                        "Normal local XAML values or original Capture tree changed after maximize/restore cycle " +
                        (cycle + 1));
            }

            // An unshown WPF Window has ActualWidth=0 even if its root was
            // manually arranged at 1280x760. That produced false pixel
            // differences from retained scene-space neon drawings in #284-290.
            // For actual screenshot identity use a native shown window and
            // real WindowState transitions, preserving the same client size.
            static void DrainDispatcher()
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
            }

            static (byte[] Pixels, int Width, int Height) SnapshotShown(MainWindow window)
            {
                window.RootLayout.UpdateLayout();
                var root = window.RootLayout;
                int width = (int)Math.Round(root.ActualWidth);
                int height = (int)Math.Round(root.ActualHeight);
                if (width < 900 || height < 500 ||
                    window.ActualWidth <= 0 || window.ActualHeight <= 0)
                    throw new InvalidOperationException(
                        $"Native WPF viewport invalid: root={width}x{height}, " +
                        $"window={window.ActualWidth:F1}x{window.ActualHeight:F1}");
                var bmp = new System.Windows.Media.Imaging.RenderTargetBitmap(
                    width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bmp.Render(root);
                var pixels = new byte[checked(width * height * 4)];
                bmp.CopyPixels(pixels, width * 4, 0);
                return (pixels, width, height);
            }

            var shown = new MainWindow
            {
                WpfVerificationMode = true,
                ShowActivated = false,
                ShowInTaskbar = false,
                Left = 80,
                Top = 80
            };
            try
            {
                shown.Show();
                DrainDispatcher();
                DrainDispatcher();
                var baseline = SnapshotShown(shown);
                var stable = SnapshotShown(shown);
                if (!baseline.Pixels.AsSpan().SequenceEqual(stable.Pixels))
                    throw new InvalidOperationException(
                        "Native baseline WPF render unstable before maximizing");

                for (var cycle = 0; cycle < 2; cycle++)
                {
                    shown.WindowState = System.Windows.WindowState.Maximized;
                    DrainDispatcher();
                    DrainDispatcher();
                    shown.ApplyMaximizedCaptureLayout();
                    if (shown.WindowState != System.Windows.WindowState.Maximized ||
                        shown.CapturePage.Content is not System.Windows.Controls.StackPanel panel ||
                        panel.Children.Count != 4)
                        throw new InvalidOperationException(
                            "Real native WPF maximize did not preserve original Capture tree");
                    SnapshotShown(shown);

                    shown.WindowState = System.Windows.WindowState.Normal;
                    DrainDispatcher();
                    DrainDispatcher();
                    shown.ApplyMaximizedCaptureLayout();
                    var restored = SnapshotShown(shown);
                    if (restored.Width != baseline.Width ||
                        restored.Height != baseline.Height)
                        throw new InvalidOperationException(
                            $"Normal native window size drift after cycle {cycle + 1}: " +
                            $"{restored.Width}x{restored.Height} vs {baseline.Width}x{baseline.Height}");
                    if (!baseline.Pixels.AsSpan().SequenceEqual(restored.Pixels))
                    {
                        int different = 0, minX = baseline.Width, minY = baseline.Height,
                            maxX = -1, maxY = -1;
                        for (int px = 0; px < baseline.Pixels.Length; px += 4)
                        {
                            if (baseline.Pixels[px] == restored.Pixels[px] &&
                                baseline.Pixels[px + 1] == restored.Pixels[px + 1] &&
                                baseline.Pixels[px + 2] == restored.Pixels[px + 2] &&
                                baseline.Pixels[px + 3] == restored.Pixels[px + 3])
                                continue;
                            different++;
                            int x = (px / 4) % baseline.Width;
                            int y = (px / 4) / baseline.Width;
                            minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                            minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                        }
                        throw new InvalidOperationException(
                            $"Native Normal pixel difference after cycle {cycle + 1}: " +
                            $"{different} pixels, bounds=({minX},{minY})-({maxX},{maxY})");
                    }
                }
            }
            finally
            {
                shown.Close();
                DrainDispatcher();
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
                "PASS packaged native WPF Normal-Maximized-Normal pixel identity, maximized-only card growth and both pages");
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
