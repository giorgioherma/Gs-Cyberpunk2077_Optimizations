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
            // The ordinary stop path must NEVER enter ShellExecute(runas).
            // This exercises the shipped managed entrypoint, not a text scan.
            var extraElevationRefused = false;
            try
            {
                PresentMonCaptureController.RunAsync("unused", "unused", restart: false)
                    .GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex) when (
                ex.Message.Contains("no second elevation", StringComparison.OrdinalIgnoreCase))
            {
                extraElevationRefused = true;
            }
            if (!extraElevationRefused)
                throw new InvalidOperationException("PresentMon stop requested a second elevation.");
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

            // The source-SVG chamfer path is the hover's only shape.
            // Test actual perimeter coverage at normal, wide and intermediate
            // button slots, including horizontal letterboxing by Uniform.
            static void CheckShellAtSize(System.Windows.Size size, string context)
            {
                var shell = GTotalOptimizer.App.Visuals.ApprovedStageArtwork.RenderedShell(size);
                var band = GTotalOptimizer.App.Visuals.StageShellHover.OuterBand(size);
                var b = shell.Bounds;
                double x = b.Left + b.Width / 2;
                double y = b.Top + b.Height / 2;
                const double probe = 1.5;
                var sample = new[]
                {
                    new System.Windows.Point(x, b.Top - probe),
                    new System.Windows.Point(x, b.Bottom + probe),
                    new System.Windows.Point(b.Left - probe, y),
                    new System.Windows.Point(b.Right + probe, y)
                };
                if (b.Width < 30 || b.Height < 10 ||
                    sample.Any(point => !band.FillContains(point)) ||
                    band.FillContains(new System.Windows.Point(x, b.Top + probe)) ||
                    band.FillContains(new System.Windows.Point(x, y)) ||
                    // A rectangular hover would fill this empty corner;
                    // the actual source SVG has a 45-degree chamfer here.
                    band.FillContains(new System.Windows.Point(
                        b.Left - probe, b.Top - probe)))
                    throw new InvalidOperationException(
                        $"Hover no longer follows the owner SVG shell at {context}: " +
                        $"slot={size}, shell={b}, rim={band.Bounds}");
            }

            static void CheckLiveStageHover(System.Windows.Controls.Button button)
            {
                button.ApplyTemplate();
                button.UpdateLayout();
                var hover = button.Template.FindName("StageHover", button)
                    as GTotalOptimizer.App.Visuals.StageShellHover
                    ?? throw new InvalidOperationException(
                        "Source-derived SVG stage hover not found: " + button.Name);
                var origin = hover.TranslatePoint(new System.Windows.Point(0, 0), button);
                if (button.ActualWidth <= 0 || button.ActualHeight <= 0 ||
                    Math.Abs(hover.ActualWidth - button.ActualWidth) > 0.6 ||
                    Math.Abs(hover.ActualHeight - button.ActualHeight) > 0.6 ||
                    Math.Abs(origin.X) > 0.6 || Math.Abs(origin.Y) > 0.6)
                    throw new InvalidOperationException(
                        $"Hover must occupy the exact SVG viewport, never resize " +
                        $"independently: {button.Name}, button={button.ActualWidth:F2}x" +
                        $"{button.ActualHeight:F2}, hover={hover.ActualWidth:F2}x" +
                        $"{hover.ActualHeight:F2}, origin={origin}");
                CheckShellAtSize(new System.Windows.Size(hover.ActualWidth,
                    hover.ActualHeight), button.Name);
            }


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

                // Four native vector indicators use the same source preflight
                // as installation admission; no independently parsed versions.
                var frameworkIcons = new[]
                {
                    (Good: window.CetFrameworkSuccessIcon, Bad: window.CetFrameworkFailureIcon,
                        Text: window.CetFrameworkLabel, Name: "CET"),
                    (Good: window.ZeroEngineFrameworkSuccessIcon, Bad: window.ZeroEngineFrameworkFailureIcon,
                        Text: window.ZeroEngineFrameworkLabel, Name: "0-Engine"),
                    (Good: window.Red4extFrameworkSuccessIcon, Bad: window.Red4extFrameworkFailureIcon,
                        Text: window.Red4extFrameworkLabel, Name: "RED4ext"),
                    (Good: window.RedscriptFrameworkSuccessIcon, Bad: window.RedscriptFrameworkFailureIcon,
                        Text: window.RedscriptFrameworkLabel, Name: "redscript")
                };
                foreach (var gate in frameworkIcons)
                {
                    if (gate.Good.Width != 16 || gate.Good.Height != 16 ||
                        gate.Bad.Width != 16 || gate.Bad.Height != 16 ||
                        !ReferenceEquals(gate.Good.Source, window.FindResource("GcoStatusSuccessIcon")) ||
                        !ReferenceEquals(gate.Bad.Source, window.FindResource("GcoStatusFailureIcon")) ||
                        gate.Good.Parent is not System.Windows.Controls.Grid parent ||
                        !ReferenceEquals(gate.Bad.Parent, parent) ||
                        !ReferenceEquals(gate.Text.Parent, parent) ||
                        !ReferenceEquals(parent.Parent, window.FrameworkChecksRow))
                        throw new InvalidOperationException(
                            "Missing 16x16 native per-framework indicator or label: " + gate.Name);
                }
                void AssertFrameworkIcons(params bool?[] values)
                {
                    for (var k = 0; k < frameworkIcons.Length; k++)
                    {
                        var item = frameworkIcons[k];
                        var success = item.Good.Visibility == System.Windows.Visibility.Visible;
                        var failure = item.Bad.Visibility == System.Windows.Visibility.Visible;
                        if (success != (values[k] == true) || failure != (values[k] == false) ||
                            !item.Text.Text.StartsWith(item.Name, StringComparison.Ordinal))
                            throw new InvalidOperationException(
                                $"Incorrect {item.Name} status: expected={values[k]}, " +
                                $"success={success}, failure={failure}, text={item.Text.Text}");
                    }
                }

                static GTotalOptimizer.Core.FrameworkEvidence Evidence(
                    string? version, bool present = true) => new(present, version);
                window.SetFrameworkStatuses(null);
                AssertFrameworkIcons(null, null, null, null);
                var passing = GTotalOptimizer.Core.FrameworkPreflight.Evaluate(new(
                    Evidence("2.3.1"), Evidence("1.37.1"), Evidence(null),
                    Evidence("1.30.0"), Evidence("0.5.31")));
                window.SetFrameworkStatuses(passing);
                AssertFrameworkIcons(true, true, true, true);
                var failing = GTotalOptimizer.Core.FrameworkPreflight.Evaluate(new(
                    Evidence("2.5"), Evidence("1.38.1"), Evidence(null, false),
                    Evidence("1.30.0"), Evidence("0.5.32")));
                window.SetFrameworkStatuses(failing);
                AssertFrameworkIcons(false, false, true, false);
                if (failing.Ready ||
                    !window.CetFrameworkLabel.Text.Contains("1.37.1") ||
                    !window.RedscriptFrameworkLabel.Text.Contains("0.5.31") ||
                    !window.ZeroEngineFrameworkLabel.Text.Contains("MISSING"))
                    throw new InvalidOperationException(
                        "Framework icons misrepresent required/found versions or admission");
                window.SetFrameworkStatuses(
                    GTotalOptimizer.Core.FrameworkPreflightResult.FromVerifiedInstalledBaseline());
                AssertFrameworkIcons(true, true, true, true);
                window.SetFrameworkStatuses(null);

                // The short normal message and longer structured framework
                // guidance both use the full native ScrollViewer viewport.
                // Continue retains its own separate column.
                if (System.Windows.Data.BindingOperations.GetBindingExpression(
                        window.StatusText, System.Windows.FrameworkElement.WidthProperty)
                        ?.ParentBinding.Path?.Path != "ViewportWidth" ||
                    window.CaptureConsoleScroll.HorizontalAlignment !=
                        System.Windows.HorizontalAlignment.Stretch)
                    throw new InvalidOperationException(
                        "Capture console must wrap at its actual width, not at bitmap/short-line width");
                var viewportWidth = window.CaptureConsoleScroll.ViewportWidth;
                if (viewportWidth > 1 &&
                    Math.Abs(window.StatusText.ActualWidth - viewportWidth) > 2)
                    throw new InvalidOperationException(
                        $"Console does not fill available width: text={window.StatusText.ActualWidth:F1}, " +
                        $"viewport={viewportWidth:F1}");

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
                foreach (var viewport in new[]
                {
                    new System.Windows.Size(240, 32),
                    new System.Windows.Size(265, 36),
                    new System.Windows.Size(300, 36),
                    new System.Windows.Size(385, 38),
                    new System.Windows.Size(440, 42)
                })
                    CheckShellAtSize(viewport, "intermediate viewport");

                static void CheckCard(System.Windows.Controls.Button button,
                    System.Windows.Shapes.Rectangle card,
                    GTotalOptimizer.App.Visuals.ChamferOutline outline, bool active)
                {
                    button.UpdateLayout();
                    card.UpdateLayout();
                    var name = active ? "ActiveCardBitmap" : "InactiveCardBitmap";
                    var brushName = active ? "ActiveCardBrush" : "InactiveCardBrush";
                    var expected = (System.Windows.Media.Imaging.BitmapSource)card.FindResource(name);
                    var bitmapBrush = card.Fill as System.Windows.Media.ImageBrush;
                    var sameSource = ReferenceEquals(bitmapBrush?.ImageSource, expected) &&
                        ReferenceEquals(bitmapBrush, card.FindResource(brushName));
                    var sameButton = ReferenceEquals(card.Tag, button);
                    var crop = bitmapBrush?.Stretch == System.Windows.Media.Stretch.UniformToFill &&
                        bitmapBrush.AlignmentX == System.Windows.Media.AlignmentX.Center &&
                        bitmapBrush.AlignmentY == System.Windows.Media.AlignmentY.Center;
                    var fillsCard = card.ActualWidth > 0 && card.ActualHeight > 0 &&
                        Math.Abs(card.ActualWidth - outline.ActualWidth) < 1 &&
                        Math.Abs(card.ActualHeight - outline.ActualHeight) < 1 &&
                        Math.Abs(card.TranslatePoint(new System.Windows.Point(0, 0), outline).X) < 1 &&
                        Math.Abs(card.TranslatePoint(new System.Windows.Point(0, 0), outline).Y) < 1;
                    var clipped = card.Clip is not null &&
                        card.ActualWidth > 0 && card.ActualHeight > 0 &&
                        card.Clip.FillContains(new System.Windows.Point(card.ActualWidth / 2,
                            card.ActualHeight / 2)) &&
                        !card.Clip.FillContains(new System.Windows.Point(0, 0));
                    if (!sameButton || !sameSource || !crop || !clipped || !fillsCard)
                        throw new InvalidOperationException(
                            $"Card artwork state/crop/clip mismatch: {card.Name}, " +
                            $"button={sameButton}, bitmap={sameSource}, crop={crop}, clip={clipped}, fillsCard={fillsCard}, " +
                            $"brushType={card.Fill?.GetType().Name}, " +
                            $"expectedType={expected.GetType().Name}, " +
                            $"size={card.ActualWidth:F2}x{card.ActualHeight:F2}, " +
                            $"outline={outline.ActualWidth:F2}x{outline.ActualHeight:F2}, " +
                            $"imageOrigin={card.TranslatePoint(new System.Windows.Point(0, 0), outline)}, " +
                            $"imageBindingWidth={card.Width:F2}, imageBindingHeight={card.Height:F2}, state={name}");
                    CheckLiveStageHover(button);
                }
                var stages = new[] {
                    (Button: window.InstallButton, Card: window.StepOneCardArtwork, Outline: window.StepOneOutline),
                    (Button: window.LaunchButton, Card: window.StepTwoCardArtwork, Outline: window.StepTwoOutline),
                    (Button: window.CollectButton, Card: window.StepThreeCardArtwork, Outline: window.StepThreeOutline)
                };
                foreach (var stage in stages)
                {
                    // Normal and smaller windows retain the original row; wide
                    // windows must display virtually all of the SVG's shell,
                    // not a short uniformly-letterboxed outline.
                    if (width <= 1280.5 &&
                        (Math.Abs(stage.Button.ActualHeight - 38) > 0.2 ||
                         !double.IsPositiveInfinity(stage.Button.MaxWidth)))
                        throw new InvalidOperationException(
                            $"Default/small stage geometry changed: {stage.Button.Name}, " +
                            $"button={stage.Button.ActualWidth:F1}x{stage.Button.ActualHeight:F1}");
                    if (width > 1280.5 && stage.Button.ActualWidth > 260)
                    {
                        var shell = GTotalOptimizer.App.Visuals.ApprovedStageArtwork.RenderedShell(
                            new System.Windows.Size(stage.Button.ActualWidth,
                                stage.Button.ActualHeight));
                        if (shell.Bounds.Width < stage.Button.ActualWidth * 0.96 ||
                            stage.Button.ActualHeight < 38.5)
                            throw new InvalidOperationException(
                                $"Expanded stage button is wider than its SVG/hover: " +
                                $"{stage.Button.Name}, button={stage.Button.ActualWidth:F1}x" +
                                $"{stage.Button.ActualHeight:F1}, shell={shell.Bounds}");
                    }
                    stage.Button.IsEnabled = false;
                    CheckStage(stage.Button, "Unavailable");
                    CheckCard(stage.Button, stage.Card, stage.Outline, active: false);
                    stage.Button.IsEnabled = true;
                    CheckStage(stage.Button, "Yellow");
                    CheckCard(stage.Button, stage.Card, stage.Outline, active: true);
                    stage.Button.IsEnabled = false;
                }
                window.InstallButton.Style = (System.Windows.Style)window.FindResource("RestoreStageButton");
                window.InstallButton.IsEnabled = true;
                CheckStage(window.InstallButton, "Restore");
                CheckCard(window.InstallButton, window.StepOneCardArtwork, window.StepOneOutline, active: false);
                window.InstallButton.IsEnabled = false;
                CheckStage(window.InstallButton, "Unavailable");
                CheckCard(window.InstallButton, window.StepOneCardArtwork, window.StepOneOutline, active: false);

                // The Capture page is a general-status UI, not a hidden
                // hover-based diagnostics surface. Check the actual loaded
                // WPF controls, not merely source text.
                if (window.StatusText.ToolTip is not null ||
                    window.GamePathBox.ToolTip is not null ||
                    frameworkIcons.Any(gate => gate.Text.ToolTip is not null))
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
                    (structural.InstallButton.ActualWidth > 260 &&
                     structural.InstallButton.ActualHeight <= buttonHeight + .3) ||
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
                    cards.Height != cardHeight || cards.MinHeight != cardMinHeight ||
                    Math.Abs(structural.InstallButton.ActualHeight - buttonHeight) > .1 ||
                    !double.IsPositiveInfinity(structural.InstallButton.MaxWidth))
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
                // Verify identical SVG-bound hover at Normal, Maximized and
                // every restored Normal without modifying the shared UI tree.
                foreach (var button in new[] { shown.InstallButton, shown.LaunchButton, shown.CollectButton })
                    CheckLiveStageHover(button);
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
                    foreach (var button in new[] { shown.InstallButton, shown.LaunchButton, shown.CollectButton })
                        CheckLiveStageHover(button);
                    SnapshotShown(shown);

                    shown.WindowState = System.Windows.WindowState.Normal;
                    DrainDispatcher();
                    DrainDispatcher();
                    shown.ApplyMaximizedCaptureLayout();
                    foreach (var button in new[] { shown.InstallButton, shown.LaunchButton, shown.CollectButton })
                        CheckLiveStageHover(button);
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
