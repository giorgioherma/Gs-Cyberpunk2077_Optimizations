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

                // Regression: keep the original three direct stage-card grids.
                // Viewbox-wrapping their spatial gradient brushes changed every
                // card to a yellow-left/cyan-right split and shrank the default.
                // The stage shells must share one scene-anchored color field.
                var stageBorders = new[]
                {
                    window.StepOneOutline, window.StepTwoOutline,
                    window.StepThreeOutline
                };
                foreach (var outline in stageBorders)
                {
                    if (!outline.SpatialStroke ||
                        outline.Parent is not System.Windows.Controls.Grid card ||
                        !ReferenceEquals(card.Parent, window.WorkflowCardsHost) ||
                        Math.Abs(card.ActualHeight - window.WorkflowCardsHost.ActualHeight) > 1.0)
                        throw new InvalidOperationException(
                            "Stage layout must preserve direct scene-space outline: " +
                            outline.Name);
                }
                // The plain default is the approved reference design, not a
                // reduced viewbox. Larger windows may distribute extra height.
                if (window.WorkflowCardsHost.Height < 228.0)
                    throw new InvalidOperationException(
                        "Workflow cards unexpectedly smaller than the approved baseline");

                // The console should stay in the right content column rather
                // than acquiring portrait-width margin compensation.
                if (window.ConsoleTextHost.Margin.Left != -7.0)
                    throw new InvalidOperationException(
                        "Capture console shifted from its base column alignment");

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

            var normal = new MainWindow();
            VerifyAtSize(normal, normal.Width, normal.Height);

            var minimum = new MainWindow();
            VerifyAtSize(minimum, minimum.MinWidth, minimum.MinHeight);

            // Fullscreen-like and widescreen client viewports: test the real
            // arranged cards/console, not only the compact initial window.
            VerifyAtSize(new MainWindow(), 1700, 900);
            VerifyAtSize(new MainWindow(), 1920, 1080);

            Console.WriteLine(
                "PASS packaged WPF assets, workflow proportions, console clearance and both pages at multiple sizes");
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
