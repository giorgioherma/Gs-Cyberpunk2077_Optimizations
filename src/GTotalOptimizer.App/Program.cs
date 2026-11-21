using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using GTotalOptimizer.Core;
using GRedscript.Resolver;

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
            // REDscript must use the SAME single GCO self-contained .NET 10
            // runtime: Resolver's managed assembly sits beside the GUI and
            // is invoked in-process. Do not ship a second Resolver runtime.
            string resolverAssembly = typeof(RedscriptGenerationPlan).Assembly.Location;
            bool resolverInBundledApp = File.Exists(resolverAssembly) &&
                string.Equals(Path.GetDirectoryName(resolverAssembly),
                    AppContext.BaseDirectory.TrimEnd(
                        Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase);
            bool sharedNet10 = System.Runtime.InteropServices.RuntimeInformation
                .FrameworkDescription.StartsWith(".NET 10.", StringComparison.Ordinal);
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                ok = verification.Ready && resolverInBundledApp && sharedNet10,
                root,
                manifest = Path.Combine(root, "components", "manifest.json"),
                process = Environment.ProcessPath,
                details = verification.Detail,
                redscriptResolverInSameProcess = resolverInBundledApp,
                sameBundledNet10Runtime = sharedNet10,
                redscriptResolverAssembly = Path.GetFileName(resolverAssembly)
            }));
            return verification.Ready && resolverInBundledApp && sharedNet10 ? 0 : 2;
        }

        // Render the packaged UI, not owner-selected aesthetic coordinates.
        if (args.Length == 1 &&
            (args[0] == "--verify-wpf-ui" || args[0] == "--verify-border-ui"))
            return VerifyWpfUi();

        if (args.Length == 1 && args[0] == "--verify-game-process-events")
            return VerifyGameProcessEvents();

        if (args.Length == 4 && args[0] == PresentMonCaptureController.CliSwitch)
            return PresentMonCaptureController.ExecuteElevated(args[1], args[2], args[3]);

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
            VerifyAbsentPresentMonRecovery();
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

    // A game-exited PresentMon must never keep RESTORE locked just because
    // the controller record or observer event remains in RESULTS. Preserve
    // original CSV evidence and refuse to modify any source-owner files.
    private static void VerifyAbsentPresentMonRecovery()
    {
        var processes = Process.GetProcessesByName("PresentMon");
        try
        {
            if (processes.Length != 0)
                throw new InvalidOperationException(
                    "PresentMon recovery smoke requires no active writer.");
        }
        finally { foreach (var process in processes) process.Dispose(); }

        var root = Path.Combine(Path.GetTempPath(), "GCO-stop-test-" +
            Guid.NewGuid().ToString("N"));
        var id = Guid.NewGuid().ToString("N");
        var directory = Path.Combine(root, "RESULTS", "GCO-Capture-" + id, "PresentMon");
        var csv = Path.Combine(directory, "PresentMon.csv");
        var controller = Path.Combine(directory, "PresentMon-controller.json");
        var tracking = Path.Combine(root, "RESULTS", "GCO-current-PresentMon.txt");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(csv, "Application,CPUStartQPC\nCyberpunk2077.exe,12345678\n");
            var csvProof = File.ReadAllText(csv);

            // Stale fully formed controller: its old process and observer no
            // longer exist. The same tracked path must still be eligible for
            // collection when clearTracking=false.
            File.WriteAllText(tracking, csv);
            File.WriteAllText(controller, JsonSerializer.Serialize(new
            {
                ok = true, action = "restart", pid = int.MaxValue, sessionId = id
            }));
            var kept = PresentMonCaptureController.StopTrackedAsync(
                root, clearTracking: false).GetAwaiter().GetResult();
            if (!kept.Stopped || kept.Csv != csv || !File.Exists(tracking))
                throw new InvalidOperationException(
                    "Already-exited writer did not preserve Collect's CSV pointer.");

            // RESTORE retires only its stale pointer, never the original CSV.
            var stopped = PresentMonCaptureController.StopTrackedAsync(
                root).GetAwaiter().GetResult();
            if (!stopped.Stopped || stopped.Csv != csv || File.Exists(tracking))
                throw new InvalidOperationException(
                    "Already-exited writer kept RESTORE in a pending state.");

            // Missing or corrupt controller records also cannot hold recovery
            // pending when the operating system has no PresentMon process.
            foreach (var broken in new[] { "not-json", "" })
            {
                File.WriteAllText(tracking, csv);
                if (broken.Length == 0) File.Delete(controller);
                else File.WriteAllText(controller, broken);
                stopped = PresentMonCaptureController.StopTrackedAsync(
                    root).GetAwaiter().GetResult();
                if (!stopped.Stopped || File.Exists(tracking))
                    throw new InvalidOperationException(
                        "Missing/corrupt PresentMon controller blocked completed cleanup.");
            }

            // Invalid stale tracking metadata must clear ONLY after proving
            // no writer remains, without deleting any preserved frame data.
            File.WriteAllText(tracking, Path.Combine(root, "outside", "PresentMon.csv"));
            stopped = PresentMonCaptureController.StopTrackedAsync(
                root).GetAwaiter().GetResult();
            if (!stopped.Stopped || File.Exists(tracking) ||
                File.ReadAllText(csv) != csvProof)
                throw new InvalidOperationException(
                    "Stale PresentMon tracking either blocked RESTORE or damaged the CSV.");
            Console.WriteLine("PASS stopped PresentMon recovers Collect/RESTORE after stale PID, missing or invalid controller, and bad tracking without losing CSV");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
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


            // Missing, incomplete, stale and corrupt derived reports must
            // never unlock CONTINUE, even when F11 captures are sealed.
            var reportFixtureRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(), "GCO-Report-Gate-" + Guid.NewGuid().ToString("N"));
            const string reportFixtureSession = "0123456789abcdef0123456789abcdef";
            try
            {
                var html = GcoProfilerReportService.ReportFile(reportFixtureRoot,
                    reportFixtureSession);
                var json = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(html)!,
                    GcoProfilerReportService.JsonFile);
                if (MainWindow.HasCompletedProfilerReport(reportFixtureRoot, reportFixtureSession))
                    throw new InvalidOperationException("Missing report unlocked CONTINUE.");
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(html)!);
                System.IO.File.WriteAllText(html, "<html>ready</html>");
                if (MainWindow.HasCompletedProfilerReport(reportFixtureRoot, reportFixtureSession))
                    throw new InvalidOperationException("HTML-only report unlocked CONTINUE.");
                System.IO.File.WriteAllText(json, "{\"sessionId\":\"another-session\"}");
                if (MainWindow.HasCompletedProfilerReport(reportFixtureRoot, reportFixtureSession))
                    throw new InvalidOperationException("Wrong-session report unlocked CONTINUE.");
                System.IO.File.WriteAllText(json, JsonSerializer.Serialize(new { sessionId = reportFixtureSession }));
                if (!MainWindow.HasCompletedProfilerReport(reportFixtureRoot, reportFixtureSession))
                    throw new InvalidOperationException("Complete report did not unlock CONTINUE.");
                System.IO.File.WriteAllText(json, "{broken");
                if (MainWindow.HasCompletedProfilerReport(reportFixtureRoot, reportFixtureSession))
                    throw new InvalidOperationException("Corrupt report unlocked CONTINUE.");
            }
            finally
            {
                if (System.IO.Directory.Exists(reportFixtureRoot))
                    System.IO.Directory.Delete(reportFixtureRoot, recursive: true);
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
                             window.ResultsScrollViewer, window.ResultsConsoleScroll
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
                             window.ContinueButton, window.OpenFolderButton
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

                // Saturated console content must stay above the shared rail;
                // use measured WPF bounds rather than hardcoded pixel positions.
                var priorCaptureText = window.StatusText.Text;
                window.StatusText.Text = string.Join(Environment.NewLine,
                    Enumerable.Repeat("Lengthy profiler guidance remains scrollable.", 8));
                window.WorkflowProgressHost.Visibility = System.Windows.Visibility.Visible;
                root.UpdateLayout();
                var captureRailTop = window.WorkflowProgressHost.TranslatePoint(
                    new System.Windows.Point(0, 0), root).Y;
                var captureConsoleBottom = window.CaptureConsoleScroll.TranslatePoint(
                    new System.Windows.Point(0, window.CaptureConsoleScroll.ActualHeight), root).Y;
                var railBottom = window.WorkflowProgressHost.TranslatePoint(
                    new System.Windows.Point(0, window.WorkflowProgressHost.ActualHeight), root).Y;
                var contentBottom = window.ContentRegion.TranslatePoint(
                    new System.Windows.Point(0, window.ContentRegion.ActualHeight), root).Y;
                if (captureConsoleBottom > captureRailTop ||
                    railBottom >= contentBottom)
                    throw new InvalidOperationException(
                        "Capture console overlaps progress or progress reaches the outer frame.");
                window.StatusText.Text = priorCaptureText;
                window.WorkflowProgressHost.Visibility = System.Windows.Visibility.Collapsed;
                root.UpdateLayout();

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
                window.CapturePage.Visibility = System.Windows.Visibility.Collapsed;
                window.CaptureFooterHost.Visibility = System.Windows.Visibility.Collapsed;
                window.ResultsPage.Visibility = System.Windows.Visibility.Visible;
                window.BackToCaptureButton.Visibility = System.Windows.Visibility.Visible;
                root.UpdateLayout();
                if (window.CaptureFooterHost.Visibility != System.Windows.Visibility.Collapsed ||
                    window.ContinueButton.Visibility != System.Windows.Visibility.Visible ||
                    window.ConsoleTextHost.Visibility != System.Windows.Visibility.Visible ||
                    window.ResultsStatusText.Visibility != System.Windows.Visibility.Visible ||
                    window.CaptureFooterHost.ActualHeight > 0)
                    throw new InvalidOperationException(
                        "Analyze must hide the entire Capture footer, including stale status and CONTINUE, while retaining its own bottom console.");
                // Restored/capture health stays at the top. Operational
                // messages must be INSIDE the scrollable console BELOW actions.
                if (window.AnalyzeButton.ActualWidth <= 0 ||
                    window.ResultsRestoreStatusText.ActualWidth <= 0 ||
                    window.CetCaptureStatusText.ActualWidth <= 0 ||
                    window.RedCaptureStatusText.ActualWidth <= 0 ||
                    window.PresentCaptureStatusText.ActualWidth <= 0 ||
                    window.ResultsConsoleScroll.ActualWidth <= 0 ||
                    !ReferenceEquals(window.ResultsConsoleScroll.Content,
                        window.ResultsStatusText) ||
                    ReferenceEquals(window.ResultsRestoreStatusText.Parent,
                        window.ResultsStatusText.Parent) ||
                    window.ResultsConsoleScroll.TranslatePoint(
                        new System.Windows.Point(0, 0), window.ResultsPage).Y <=
                    window.AnalyzeButton.TranslatePoint(
                        new System.Windows.Point(0, 0), window.ResultsPage).Y)
                    throw new InvalidOperationException(
                        "Analyze console must be visible below buttons; restoration and capture indicators stay above panels.");

                if (window.BackToCaptureButton.Visibility !=
                        System.Windows.Visibility.Visible ||
                    window.BackToCaptureButton.ActualHeight < 24 ||
                    window.BackToCaptureButton.Content?.ToString() != "← BACK" ||
                    window.BackToCaptureButton.Style != window.RefreshButton.Style ||
                    window.BackToCaptureButton.TranslatePoint(
                        new System.Windows.Point(0, 0), window.TopHeaderHost).X >=
                    window.RefreshButton.TranslatePoint(
                        new System.Windows.Point(0, 0), window.TopHeaderHost).X ||
                    window.CloseAfterGenerationButton.Content?.ToString() != "DONE" ||
                    window.CloseAfterGenerationButton.Visibility !=
                        System.Windows.Visibility.Collapsed ||
                    window.OpenFolderButton.IsEnabled)
                    throw new InvalidOperationException(
                        "Analyze BACK must match REFRESH; DONE is hidden and OPEN FOLDER disabled before generation.");

                // Long Analyze messages scroll inside the footer and never
                // extend into the dedicated shared progress strip.
                var priorAnalyzeText = window.ResultsStatusText.Text;
                window.ResultsStatusText.Text = string.Join(Environment.NewLine,
                    Enumerable.Repeat("Lengthy REDscript source diagnostics remain scrollable.", 8));
                window.WorkflowProgressHost.Visibility = System.Windows.Visibility.Visible;
                root.UpdateLayout();
                var analyzeRailTop = window.WorkflowProgressHost.TranslatePoint(
                    new System.Windows.Point(0, 0), root).Y;
                var analyzeConsoleBottom = window.ResultsConsoleScroll.TranslatePoint(
                    new System.Windows.Point(0, window.ResultsConsoleScroll.ActualHeight), root).Y;
                if (analyzeConsoleBottom > analyzeRailTop)
                    throw new InvalidOperationException(
                        "Analyze console overlaps the dedicated progress strip.");
                window.ResultsStatusText.Text = priorAnalyzeText;
                window.WorkflowProgressHost.Visibility = System.Windows.Visibility.Collapsed;
                root.UpdateLayout();

                // DONE shares the footer row with the console. Simulating its
                // visibility must not shift status messages or overlap them.
                // The user intentionally placed the console ABOVE the button
                // center, so do not assert arbitrary visual center alignment.
                var originalConsoleWidth = window.ResultsConsoleScroll.ActualWidth;
                var originalConsoleTop = window.ResultsConsoleScroll.TranslatePoint(
                    new System.Windows.Point(0, 0), window.ResultsPage).Y;
                window.CloseAfterGenerationButton.Visibility =
                    System.Windows.Visibility.Visible;
                root.UpdateLayout();
                var doneLeft = window.CloseAfterGenerationButton.TranslatePoint(
                    new System.Windows.Point(0, 0), window.ResultsPage).X;
                var consoleRight = window.ResultsConsoleScroll.TranslatePoint(
                    new System.Windows.Point(window.ResultsConsoleScroll.ActualWidth, 0),
                    window.ResultsPage).X;
                var consoleTop = window.ResultsConsoleScroll.TranslatePoint(
                    new System.Windows.Point(0, 0), window.ResultsPage).Y;
                if (consoleRight > doneLeft ||
                    Math.Abs(consoleTop - originalConsoleTop) > 1 ||
                    window.ResultsConsoleScroll.ActualWidth >= originalConsoleWidth ||
                    window.CloseAfterGenerationButton.ActualWidth <= 0 ||
                    window.OpenFolderButton.Content?.ToString() != "OPEN FOLDER" ||
                    window.OpenFolderButton.IsEnabled)
                    throw new InvalidOperationException(
                        "DONE must not overlap or shift the console; OPEN FOLDER requires a generated ZIP.");
                window.CloseAfterGenerationButton.Visibility =
                    System.Windows.Visibility.Collapsed;
                root.UpdateLayout();
                if (Math.Abs(window.ResultsConsoleScroll.ActualWidth - originalConsoleWidth) > 1)
                    throw new InvalidOperationException(
                        "Hidden DONE must release the full Analyze console width.");

                if (Math.Abs(width - 1280) < 1 && Math.Abs(height - 760) < 1 &&
                    window.ResultsScrollViewer.ComputedVerticalScrollBarVisibility ==
                        System.Windows.Visibility.Visible)
                    throw new InvalidOperationException(
                        "Default Analyze layout requires vertical scrolling before any expanded analysis results.");

                // The Page 2 overlay must not hide the original shared
                // skyline and V art; Capture layout must remain unchanged.
                static bool Transparent(System.Windows.Media.Brush? value) =>
                    value is System.Windows.Media.SolidColorBrush brush &&
                    brush.Color.A == 0;
                if (!Transparent(window.ResultsPage.Background) ||
                    !Transparent(window.ResultsScrollViewer.Background) ||
                    window.ResultsPage.Margin.Left < 35 ||
                    window.ResultsPage.ActualWidth < 650 ||
                    window.SkylineHost.Visibility != System.Windows.Visibility.Visible ||
                    window.CityImage.Source is null)
                    throw new InvalidOperationException(
                        "Analyze page hides the existing skyline, covers V, or clips the shifted content.");

                // Hotscenes English-only removes the language system from
                // generated Lua, so it must always be an explicit default-OFF
                // user choice inside the CET (not REDscript) summary.
                var englishCheck = window.HotscenesEnglishOnlyCheckBox;
                if (englishCheck.IsChecked != false ||
                    englishCheck.IsThreeState ||
                    englishCheck.Content?.ToString() != "Hotscenes ENG only" ||
                    englishCheck.Parent is not System.Windows.Controls.StackPanel cetPanel ||
                    !ReferenceEquals(window.CetReportText.Parent, cetPanel) ||
                    window.HotscenesEnglishOnlyDescription.Parent != cetPanel ||
                    !window.HotscenesEnglishOnlyDescription.Text.Contains(
                        "Removes Hotscenes localization", StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Hotscenes ENG-only must be clearly explained and unchecked in the CET box.");
                englishCheck.IsChecked = true;
                if (englishCheck.IsChecked != true)
                    throw new InvalidOperationException(
                        "Hotscenes ENG-only explicit opt-in could not be selected.");
                englishCheck.IsChecked = false;

                var yellow = System.Windows.Media.Color.FromRgb(255, 240, 0);
                var green = System.Windows.Media.Color.FromRgb(101, 220, 162);
                static bool ColorIs(System.Windows.Media.Brush? value,
                    System.Windows.Media.Color expected) =>
                    value is System.Windows.Media.SolidColorBrush b && b.Color == expected;
                if (!ColorIs(window.ResultsRestoreStatusText.Foreground, yellow) ||
                    !ColorIs(window.CetCaptureStatusText.Foreground, yellow) ||
                    !ColorIs(window.RedCaptureStatusText.Foreground, yellow) ||
                    !ColorIs(window.PresentCaptureStatusText.Foreground, yellow))
                    throw new InvalidOperationException(
                        "Analyze restoration and domain labels must use owner yellow.");

                // Test the actual formatter used by successful and missing
                // capture results: only verified OK may be green.
                GTotalOptimizer.App.MainWindow.SetAnalyzeCaptureStatus(
                    window.CetCaptureStatusText, "CET", "OK");
                var good = window.CetCaptureStatusText.Inlines
                    .OfType<System.Windows.Documents.Run>().ToArray();
                if (good.Length != 2 || good[0].Text != "CET · " ||
                    !ColorIs(good[0].Foreground, yellow) ||
                    good[1].Text != "OK" || !ColorIs(good[1].Foreground, green))
                    throw new InvalidOperationException(
                        "Only the verified CET OK word must render green.");
                GTotalOptimizer.App.MainWindow.SetAnalyzeCaptureStatus(
                    window.CetCaptureStatusText, "CET", "MISSING");
                var missing = window.CetCaptureStatusText.Inlines
                    .OfType<System.Windows.Documents.Run>().ToArray();
                if (missing.Length != 2 || missing[1].Text != "MISSING" ||
                    !ColorIs(missing[0].Foreground, yellow) ||
                    !ColorIs(missing[1].Foreground, yellow))
                    throw new InvalidOperationException(
                        "Missing capture status must never be green.");
                GTotalOptimizer.App.MainWindow.SetAnalyzeCaptureStatus(
                    window.CetCaptureStatusText, "CET", "NOT VERIFIED");

                var backup = string.Concat(window.OptimizationFooterBackup.Inlines
                    .OfType<System.Windows.Documents.Run>().Select(run => run.Text));
                static string FlattenWhitespace(string value) =>
                    System.Text.RegularExpressions.Regex.Replace(
                        value, @"\s+", " ").Trim();
                if (window.CetOptimizationHeading.Text != "CET mod Optimizations:" ||
                    window.RedOptimizationHeading.Text != "REDscript mod Optimizations:")
                    throw new InvalidOperationException("Incorrect Analyze headings: CET=" +
                        window.CetOptimizationHeading.Text + " REDscript=" +
                        window.RedOptimizationHeading.Text);
                // Only assert meaningful guidance, not an exact UI sentence:
                // wording and spacing are presentation decisions, not a
                // release-blocking CI contract.
                var zipGuide = FlattenWhitespace(window.OptimizationFooterFirstLine.Text);
                var managerGuide = FlattenWhitespace(window.OptimizationFooterVortex.Text);
                if (!zipGuide.Contains("CET + REDscript", StringComparison.Ordinal) ||
                    !zipGuide.Contains("zip", StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(managerGuide))
                    throw new InvalidOperationException(
                        "Analyze footer must identify the combined ZIP and file-management guidance.");
                // WPF adds optional whitespace-only inline runs around XAML
                // Run elements. Validate visible words and both exact game
                // paths, not parser-generated whitespace between those runs.
                if (!FlattenWhitespace(backup).Contains(
                        "For manual install BACKUP YOUR ", StringComparison.Ordinal) ||
                    !backup.Contains(@"\Cyberpunk 2077\bin\x64\plugins\cyber_engine_tweaks\mods",
                        StringComparison.Ordinal) ||
                    !backup.Contains(@"\Cyberpunk 2077\r6\scripts",
                        StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        "Incorrect Analyze backup guidance: " + backup);
                // Exercise the exact timer state machine deterministically:
                // one, two, three dots, then repeat, and restore prior
                // completed console text without replacing real diagnostics.
                const string baseProgress = "Analyzing";
                string prior = window.ResultsStatusText.Text;
                long activity = window.BeginConsoleActivity(baseProgress);
                if (window.ResultsStatusText.Text != "Analyzing.")
                    throw new InvalidOperationException("Console spinner initial dot missing.");
                window.AdvanceConsoleActivity();
                if (window.ResultsStatusText.Text != "Analyzing..")
                    throw new InvalidOperationException("Console spinner second dot missing.");
                window.AdvanceConsoleActivity();
                if (window.ResultsStatusText.Text != "Analyzing...")
                    throw new InvalidOperationException("Console spinner third dot missing.");
                window.AdvanceConsoleActivity();
                if (window.ResultsStatusText.Text != "Analyzing.")
                    throw new InvalidOperationException("Console spinner cycle failed.");
                window.EndConsoleActivity(activity);
                if (window.ResultsStatusText.Text != prior)
                    throw new InvalidOperationException(
                        "Console spinner damaged the prior status after work ended.");

                // A completed error can arrive while another async cleanup
                // awaits and a timer tick is still possible. Never let the
                // final dots erase the meaningful outcome.
                long failedActivity = window.BeginConsoleActivity("Analyzing");
                window.SetResultsConsoleStatus("Cannot analyze: source unavailable.");
                window.AdvanceConsoleActivity();
                window.EndConsoleActivity(failedActivity);
                if (window.ResultsStatusText.Text != "Cannot analyze: source unavailable.")
                    throw new InvalidOperationException(
                        "Activity tick erased an actual Analyze failure message.");

                Render();
                window.ResultsPage.Visibility = System.Windows.Visibility.Collapsed;
                window.CapturePage.Visibility = System.Windows.Visibility.Visible;
                window.CaptureFooterHost.Visibility = System.Windows.Visibility.Visible;
                root.UpdateLayout();
                if (window.CaptureFooterHost.Visibility != System.Windows.Visibility.Visible ||
                    window.ConsoleTextHost.Visibility != System.Windows.Visibility.Visible ||
                    window.ContinueButton.Visibility != System.Windows.Visibility.Visible ||
                    window.CaptureFooterHost.ActualHeight <= 0)
                    throw new InvalidOperationException(
                        "Capture return must restore the original footer and CONTINUE control.");

                // The same activity animation must work in the older Page 1
                // console without changing the Capture image/card structure.
                string capturePrevious = window.StatusText.Text;
                long collecting = window.BeginConsoleActivity("Collecting results");
                if (window.StatusText.Text != "Collecting results.")
                    throw new InvalidOperationException(
                        "Capture console spinner initial dot missing.");
                window.AdvanceConsoleActivity();
                window.AdvanceConsoleActivity();
                if (window.StatusText.Text != "Collecting results...")
                    throw new InvalidOperationException(
                        "Capture console spinner three-dot step missing.");
                window.EndConsoleActivity(collecting);
                if (window.StatusText.Text != capturePrevious)
                    throw new InvalidOperationException(
                        "Capture spinner did not restore previous console status.");
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

            // Exercise the actual START-to-tray WPF unload while NO game
            // process was launched. Waiting for Cyberpunk here would be a
            // regression: this packaged test has no Cyberpunk executable.
            var handoff = new MainWindow
            {
                WpfVerificationMode = true,
                ShowActivated = false,
                ShowInTaskbar = false
            };
            handoff.Show();
            DrainDispatcher();
            if (!handoff.HibernateAfterLaunch() || handoff.IsVisible)
                throw new InvalidOperationException(
                    "Successful START must close the visible WPF window immediately, without a Cyberpunk PID.");
            DrainDispatcher();
            Console.WriteLine("PASS START capture handoff unloads WPF before Cyberpunk is observed");

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
