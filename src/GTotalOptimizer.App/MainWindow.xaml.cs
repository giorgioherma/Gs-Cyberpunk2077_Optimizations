using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using System.Windows.Threading;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

// WPF presentation only. All native install/collect/restore transactions stay
// in the authoritative CET and GRSP owner managers via UnifiedProfilerWorkflow.
public partial class MainWindow : Window
{
    private enum PortraitStage { Black, Capturing, Collecting, Collected }
    private readonly string packageRoot = PortablePackagePaths.CurrentPackageRoot();
    private readonly DispatcherTimer glitchTimer = new();
    // Windows start notifications and process-handle exit events are idle
    // until the game transitions. Only start a slow timer if WMI is unavailable.
    private readonly GameProcessEvents gameProcesses = new();
    private readonly DispatcherTimer gameStateTimer = new()
        { Interval = TimeSpan.FromSeconds(10) };
    private readonly Random glitchRandom = new();
    private CancellationTokenSource? refreshCancellation;
    private GameInspection inspection = GameInspector.Inspect(null);
    private LiveProfilerState? liveState;
    private CollectedCaptureIndex? savedCapture;
    private string? resultsFolder;
    private string? completedSessionId;
    private string? completedGameRoot;
    private bool captureAndRestoreVerified;
    private bool captureLaunched;
    private DateTime lastLaunchRequestedUtc = DateTime.MinValue;
    private string? consolePinned;
    private string? consolePinnedDetail;
    private bool gameStateRefreshInFlight;
    private bool pendingExternalGameStart;
    private int? pendingExternalGamePid;
    private string? pendingGameExitSessionId;
    private int? observedGamePid;
    private int? gcoLaunchedGamePid;
    // The last CSV whose ETW stop completed successfully in this process.
    // Its tracking pointer remains until explicit Collect/abandonment.
    private string? stoppedPresentMonCsv;
    private bool closingInProgress;
    private bool closureCleanupVerified;
    private bool busy;
    private bool pathUpdate;
    private bool initialized;
    private int glitchTicks;
    private PortraitStage portraitStage = PortraitStage.Black;

    private UnifiedProfilerWorkflow Workflow => new(packageRoot);
    private static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length != 0;
    private static bool HasNative(CollectedCaptureIndex? capture) =>
        capture?.Cet is not null || capture?.Redscript is not null;
    private bool ClosedOrClosing => !IsLoaded && initialized;

    public MainWindow()
    {
        InitializeComponent();
        initialized = true;
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
            ?? throw new InvalidOperationException("GCO assembly version unavailable.");
        Title = $"G's Cyberpunk 2077 Optimizer — v{version}";
        BuildVersionText.Text = $"v{version}";
        glitchTimer.Tick += GlitchTick;
        gameStateTimer.Tick += GameStateTimer_Tick;
        gameProcesses.Started += pid => DispatchGameEvent(() => GameStarted(pid));
        gameProcesses.Exited += pid => DispatchGameEvent(() => GameExited(pid));
        LoadOriginalArtwork();
        pathUpdate = true;
        // Reopen on the original profiler installation, regardless of the
        // previous UI preference. The journal owns the recovery target.
        GamePathBox.Text = Workflow.ReadRecoveryGameRoot() ??
                           UserSettings.ReadGameRoot();
        pathUpdate = false;
        UpdatePathLock();
        SetPortraitStage(PortraitStage.Black);
    }

    private void LoadOriginalArtwork()
    {
        // Images must remain the two exact aligned originals supplied by the
        // user; there is deliberately no generated replacement/silhouette.
        BitmapImage? ImageFile(string folder, string name)
        {
            var full = Path.Combine(packageRoot, "assets", "ui", folder, name);
            if (!File.Exists(full)) return null;
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(full, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch { return null; }
        }
        var colored = ImageFile("images", "V_background_1_color.png");
        var black = ImageFile("images", "V_background_1_black.png");
        if (colored is not null && black is not null &&
            colored.PixelWidth == black.PixelWidth && colored.PixelHeight == black.PixelHeight)
        {
            VColorImage.Source = colored;
            VBlackImage.Source = black;
        }
        GcoHeaderLogo.Source = ImageFile("elements", "Logo_GCO_Official.png");
        if (GcoHeaderLogo.Source is null)
            Trace.TraceError("Missing/undecodable official GCO logo PNG in assets/ui/elements.");
        PuddleOverflowImage.Source = ImageFile("images", "The overflow.png");
        CityImage.Source = ImageFile("images", "TrueHeader_GCO_page1.png");
        // Use one immutable master for the lifetime of this window.
        // WPF continuously scales it; never swap BitmapSource on resize.
        OuterBorderArtwork.Source =
            ImageFile("elements", "GCO_Border_Regular_1280x760_FINAL.png");
        if (OuterBorderArtwork.Source is null)
            Trace.TraceError("Missing/undecodable Regular border PNG in assets/ui/elements.");
    }

    private void SetPortraitStage(PortraitStage stage)
    {
        portraitStage = stage;
        glitchTimer.Stop();
        VBlackImage.BeginAnimation(UIElement.OpacityProperty, null);
        if (stage == PortraitStage.Collected)
        {
            var fade = new DoubleAnimation(VBlackImage.Opacity, 0.0,
                TimeSpan.FromMilliseconds(850)) { FillBehavior = FillBehavior.HoldEnd };
            VBlackImage.BeginAnimation(UIElement.OpacityProperty, fade);
            return;
        }
        VBlackImage.Opacity = 1.0;
        if (stage is PortraitStage.Capturing or PortraitStage.Collecting)
            StartGlitchIfVisible();
    }

    private void StartGlitchIfVisible()
    {
        if (!IsActive ||
            VBlackImage.Source is null || VColorImage.Source is null)
            return;
        glitchTimer.Interval = TimeSpan.FromMilliseconds(350 + glitchRandom.Next(1100));
        glitchTimer.Start();
    }

    private void GlitchTick(object? sender, EventArgs e)
    {
        if (portraitStage is not (PortraitStage.Capturing or PortraitStage.Collecting) ||
            !IsActive)
        {
            glitchTimer.Stop();
            VBlackImage.Opacity = 1;
            return;
        }
        ++glitchTicks;
        var longBlack = glitchTicks % 8 == 0;
        var colored = VBlackImage.Opacity > 0.5 && !longBlack;
        VBlackImage.Opacity = colored ? 0 : 1;
        // Flashing does NOT indicate progress, capture success or validation.
        var next = longBlack ? 2400 + glitchRandom.Next(5000)
            : colored ? 30 + glitchRandom.Next(portraitStage == PortraitStage.Collecting ? 110 : 200)
            : portraitStage == PortraitStage.Collecting ? 90 + glitchRandom.Next(800)
              : 240 + glitchRandom.Next(2200);
        glitchTimer.Interval = TimeSpan.FromMilliseconds(next);
    }

    // Preserve the old narrow V content-column reservation. The new
    // 482:928 owner image pair retains the old top/left/height anchor and
    // extends its extra transition pixels rightwards without shifting UI.
    private const double PortraitBottomOverhang = 6.0;
    private const double ReservedVAspect = 743.0 / 2117.0;
    private const double NewVAspect = 482.0 / 928.0;
    private void Window_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        UpdatePortraitWidth();
        QueueHeaderTabAlignment();
    }

    // Keep the tab pair centered between the real, arranged logo and REFRESH,
    // rather than centering relative to the full window or right content area.
    // Loaded priority measures positions after WPF has arranged on resize.
    private void QueueHeaderTabAlignment()
    {
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(AlignTabsBetweenHeaderControls));
    }

    private void AlignTabsBetweenHeaderControls()
    {
        if (RootLayout.ActualWidth <= 0 || GcoHeaderLogo.ActualWidth <= 0 ||
            RefreshButton.ActualWidth <= 0)
            return;

        var logoRight = GcoHeaderLogo.TranslatePoint(
            new Point(GcoHeaderLogo.ActualWidth, 0), RootLayout).X;
        var refreshLeft = RefreshButton.TranslatePoint(new Point(0, 0), RootLayout).X;
        // The supported minimum window leaves enough room for both 246-DIP tabs
        // and their 12-DIP gap between these two header anchors.
        if (refreshLeft - logoRight < 504)
            return;

        var desired = new Thickness(logoRight, 0,
            RootLayout.ActualWidth - refreshLeft, 0);
        if (TopTabsHost.Margin != desired)
            TopTabsHost.Margin = desired;
    }

    private void ContentRegion_SizeChanged(object sender, System.Windows.SizeChangedEventArgs e)
    {
        UpdatePortraitWidth();
    }

    private void UpdatePortraitWidth()
    {
        if (ArtworkColumn is null || ContentRegion is null) return;
        var imageHeight = Math.Max(1.0, ContentRegion.ActualHeight
                                             + PortraitBottomOverhang);
        // Keep the old right-hand controls anchored to their original X.
        var reservedWidth = Math.Clamp(
            imageHeight * ReservedVAspect, 170.0, 245.0);
        ArtworkColumn.Width = new GridLength(reservedWidth);
        // Two registered 482:928 V layers stay at their previous height.
        // Moving the content-local margin -3 to -1 exposes the X27 frame
        // line, leaving V at X28 without moving the right-hand controls.
        if (VPortraitHost is not null)
            VPortraitHost.Width = imageHeight * NewVAspect + 3.0;
        // Crop x=0..365, y=879..928 of the 482x928 source: align using
        // the exact SAME height-derived scale and the same bottom/left.
        if (PuddleOverflowHost is not null)
        {
            PuddleOverflowHost.Width = imageHeight * 365.0 / 928.0;
            PuddleOverflowHost.Height = imageHeight * 49.0 / 928.0;
        }
    }

    private void SetActiveTab(bool results)
    {
        // Two genuinely illuminated surface states rather than one flat paint.
        // SlantedTabShape draws its own clipped specular lip and inset shadow.
        var active = new LinearGradientBrush
        {
            StartPoint = new Point(.08, 0), EndPoint = new Point(.86, 1)
        };
        active.GradientStops.Add(new GradientStop(Color.FromRgb(255, 255, 206), 0));
        active.GradientStops.Add(new GradientStop(Color.FromRgb(255, 246, 90), .24));
        active.GradientStops.Add(new GradientStop(Color.FromRgb(255, 240, 0), .58));
        active.GradientStops.Add(new GradientStop(Color.FromRgb(171, 138, 0), 1));
        active.Freeze();
        var inactive = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0), EndPoint = new Point(.56, 1)
        };
        inactive.GradientStops.Add(new GradientStop(Color.FromRgb(39, 69, 78), 0));
        inactive.GradientStops.Add(new GradientStop(Color.FromRgb(18, 37, 43), .28));
        inactive.GradientStops.Add(new GradientStop(Color.FromRgb(8, 19, 24), .75));
        inactive.GradientStops.Add(new GradientStop(Color.FromRgb(2, 9, 14), 1));
        inactive.Freeze();
        CaptureTabShape.Lit = !results;
        AnalyzeTabShape.Lit = results;
        CaptureTabShape.Fill = results ? inactive : active;
        CaptureTabShape.Stroke = results ? new SolidColorBrush(Color.FromRgb(145, 171, 180)) : active;
        CaptureTabLabel.Foreground = results ? Brushes.White : new SolidColorBrush(Color.FromRgb(6, 16, 20));
        CaptureTabMarks.Stroke = results ? new SolidColorBrush(Color.FromRgb(0, 226, 242)) :
            new SolidColorBrush(Color.FromRgb(6, 16, 20));
        AnalyzeTabShape.Fill = results ? active : inactive;
        AnalyzeTabShape.Stroke = results ? active : new SolidColorBrush(Color.FromRgb(145, 171, 180));
        AnalyzeTabLabel.Foreground = results ? new SolidColorBrush(Color.FromRgb(6, 16, 20)) : Brushes.White;
        AnalyzeTabMarks.Stroke = results ? new SolidColorBrush(Color.FromRgb(6, 16, 20)) :
            new SolidColorBrush(Color.FromRgb(0, 226, 242));
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdatePortraitWidth();
        QueueHeaderTabAlignment();
        // Subscribe before the one-time initial enumeration so no process
        // launch can fall between enumeration and notification registration.
        var notificationsAvailable = gameProcesses.TryStartNotifications(out var error);
        // Already-running game: track its exit, but do not fabricate a new
        // launch attempt or overwrite existing capture evidence.
        foreach (var process in Process.GetProcessesByName("Cyberpunk2077"))
        {
            using (process)
            {
                if (observedGamePid is null)
                {
                    observedGamePid = process.Id;
                    if (!gameProcesses.Track(process.Id)) observedGamePid = null;
                }
            }
        }
        if (!notificationsAvailable)
        {
            Trace.TraceWarning("Cyberpunk start notifications unavailable; 10-second fallback: " + error);
            gameStateTimer.Start();
        }
        ResumeLastRestoredWorkspace();
        _ = RefreshSourceAsync();
    }

    private void ResumeLastRestoredWorkspace()
    {
        // A pending install/capture journal always takes precedence. A
        // completed workspace may be resumed only after independent owner
        // restoration was recorded and every archived file hash verifies.
        if (Workflow.HasRecoveryJournal ||
            string.IsNullOrWhiteSpace(GamePathBox.Text)) return;
        try
        {
            var saved = CollectedCaptureStore.ReadLastRestored(
                packageRoot, GamePathBox.Text);
            if (saved is null) return;
            completedSessionId = saved.SessionId;
            completedGameRoot = saved.GameRoot;
            captureAndRestoreVerified = true;
            savedCapture = saved;
            UpdateResultTexts(saved);
            SetPortraitStage(PortraitStage.Collected);
            CapturePage.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            SetActiveTab(true);
            RefreshCompletedWorkspace();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Completed workspace not resumed: " + ex);
            PinStatus("Saved Analyze workspace unavailable. Capture files are unchanged.",
                ex.ToString());
        }
    }

    private void DispatchGameEvent(Action action)
    {
        if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        try
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (IsLoaded && !closingInProgress && !closureCleanupVerified)
                    action();
            }));
        }
        catch (InvalidOperationException) { /* window already shutting down */ }
    }

    private void QueueGameReconciliation()
    {
        DispatchGameEvent(() => _ = ReconcileGameProcessChangesAsync());
    }

    private void GameStarted(int pid)
    {
        if (observedGamePid == pid) return;
        if (observedGamePid is not null) return; // existing game is tracked
        observedGamePid = pid;
        if (!gameProcesses.Track(pid))
        {
            // The process may have failed at startup before we attached.
            GameExited(pid);
            return;
        }
        if (gcoLaunchedGamePid == pid)
        {
            gcoLaunchedGamePid = null;
        }
        else
        {
            pendingExternalGameStart = true;
            pendingExternalGamePid = pid;
        }
        QueueGameReconciliation();
    }

    private void GameExited(int pid)
    {
        if (observedGamePid != pid) return;
        gameProcesses.Untrack(pid);
        observedGamePid = null;
        gcoLaunchedGamePid = null;
        captureLaunched = false;
        // Match the exit to the still-active attempt, not a session that
        // might already have been superseded by another game launch.
        try
        {
            var game = Workflow.ReadRecoveryGameRoot();
            if (game is not null && Workflow.HasCaptureAttempt(game))
                pendingGameExitSessionId = Workflow.ReadRecoverySessionId(game);
        }
        catch (Exception ex) { Trace.TraceWarning("Game exit journal inspection: " + ex); }
        QueueGameReconciliation();
    }

    // Retained only as a 10-second safety fallback when Windows process-start
    // notifications cannot be subscribed. No polling occurs on normal systems.
    private void GameStateTimer_Tick(object? sender, EventArgs e)
    {
        if (!initialized || !IsLoaded || closingInProgress || closureCleanupVerified) return;
        var list = Process.GetProcessesByName("Cyberpunk2077");
        try
        {
            var pid = list.Length == 0 ? (int?)null : list[0].Id;
            if (pid is int running && observedGamePid is null)
                GameStarted(running);
            else if (pid is null && observedGamePid is int previous)
                GameExited(previous);
        }
        finally { foreach (var process in list) process.Dispose(); }
    }

    private async Task ReconcileGameProcessChangesAsync()
    {
        if (!initialized || !IsLoaded || closingInProgress ||
            closureCleanupVerified || busy || gameStateRefreshInFlight ||
            !inspection.IsValidGame) return;
        gameStateRefreshInFlight = true;
        try
        {
            // A new game run supersedes the previous exit: its restart
            // already stops old ETW before beginning a new capture.
            if (pendingExternalGameStart)
            {
                var pid = pendingExternalGamePid;
                pendingExternalGameStart = false;
                pendingExternalGamePid = null;
                if (pid == observedGamePid && GameRunning())
                {
                    pendingGameExitSessionId = null;
                    await HandleExternalGameLaunchAsync();
                }
            }

            if (pendingGameExitSessionId is string exitId)
            {
                pendingGameExitSessionId = null;
                await StopPresentMonAfterGameExitAsync(exitId);
            }
        }
        catch (Exception ex)
        {
            PinStatus("Game monitoring failed. Refresh GCO for a new status.", ex.ToString());
            Trace.TraceError("Game process reconciliation: " + ex);
        }
        finally
        {
            gameStateRefreshInFlight = false;
            await RefreshSourceAsync();
            if (pendingExternalGameStart || pendingGameExitSessionId is not null)
                QueueGameReconciliation();
        }
    }

    private async Task StopPresentMonAfterGameExitAsync(string exitedSessionId)
    {
        var game = Workflow.ReadRecoveryGameRoot();
        if (game is null || !Workflow.HasCaptureAttempt(game) ||
            !string.Equals(Workflow.ReadRecoverySessionId(game), exitedSessionId,
                StringComparison.OrdinalIgnoreCase))
            return;
        // Never clear GCO-current-PresentMon.txt here. Collect needs the
        // precise CSV path even after the writer has stopped and flushed.
        SetBusy(true);
        try
        {
            var stopped = await StopOwnedPresentMonAsync(exitedSessionId,
                clearTracking: false);
            PinStatus(stopped.Stopped
                ? stopped.Csv is not null
                    ? "Cyberpunk closed. PresentMon saved. Click COLLECT RESULTS."
                    : "Cyberpunk closed. Click COLLECT RESULTS."
                : "Cyberpunk closed. PresentMon stop failed; retry with COLLECT RESULTS.",
                stopped.Error);
        }
        finally { SetBusy(false); }
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (!initialized || busy ||
            WindowState == System.Windows.WindowState.Minimized) return;
        if (portraitStage is PortraitStage.Capturing or PortraitStage.Collecting)
            StartGlitchIfVisible();
        // Do not run synchronous pre-await inspection inside the taskbar's
        // native activation transition; schedule it once activation settles.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (IsLoaded && !busy &&
                WindowState != System.Windows.WindowState.Minimized)
                _ = RefreshSourceAsync();
        }));
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            glitchTimer.Stop();
            return;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded && IsVisible &&
                WindowState != System.Windows.WindowState.Minimized)
            {
                Activate();
                Focus();
            }
        }));
    }

    // Closing GCO never abandons the capture: its durable journal and native
    // source-owned results must survive reopening. Only uncollected PresentMon
    // scratch output is disposable, after its ETW writer has safely stopped.
    // Keep the window open if that graceful stop or targeted cleanup fails.
    private async void Window_Closing(object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (!initialized || closureCleanupVerified) return;
        e.Cancel = true;
        if (closingInProgress) return;
        if (busy)
        {
            PinStatus("Finish the active profiler operation before closing GCO.");
            return;
        }
        closingInProgress = true;
        SetBusy(true);
        SetStatus("Closing GCO. Keeping your profiler session.");
        try
        {
            var game = Workflow.ReadRecoveryGameRoot();
            var id = game is null ? Guid.NewGuid().ToString("N")
                : Workflow.ReadRecoverySessionId(game) ?? Guid.NewGuid().ToString("N");
            // Do not clear the tracking pointer until its known GCO-owned
            // temporary files have been deleted successfully.
            var stop = await StopOwnedPresentMonAsync(id, clearTracking: false);
            if (!stop.Stopped)
            {
                PinStatus("PresentMon did not stop. Retry closing GCO or use Restore.",
                    stop.Error);
                return;
            }
            if (game is not null && Workflow.HasCaptureAttempt(game))
                CollectedCaptureStore.DiscardUncollectedPresentMon(
                    packageRoot, game, id, stop.Csv);
            // The app-exit exception applies only to uncollected PresentMon.
            // CET/GRSP owner results, collected evidence and session journal
            // are never deleted or reset here.
            PresentMonCaptureController.ClearTrackedCsv(packageRoot);
            closureCleanupVerified = true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("GCO close cleanup: " + ex);
            PinStatus("Could not close safely. Capture data preserved; retry.", ex.ToString());
        }
        finally
        {
            closingInProgress = false;
            SetBusy(false);
            if (closureCleanupVerified)
                Dispatcher.BeginInvoke(DispatcherPriority.Background,
                    new Action(Close));
            else
                await RefreshSourceAsync();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        gameStateTimer.Stop();
        gameProcesses.Dispose();
        glitchTimer.Stop();
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
    }

    private async void GamePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized || pathUpdate || busy) return;
        var recoveryRoot = Workflow.ReadRecoveryGameRoot();
        if (recoveryRoot is not null &&
            !string.Equals(GamePathBox.Text, recoveryRoot,
                StringComparison.OrdinalIgnoreCase))
        {
            // Reject even programmatic source changes while a profiler session
            // remains installed. Keyboard edits are already read-only.
            pathUpdate = true;
            GamePathBox.Text = recoveryRoot;
            pathUpdate = false;
            PinStatus("Game folder locked. Use RESTORE PROFILERS.");
            return;
        }
        ClearStatusPin();
        await RefreshSourceAsync();
    }

    private void GamePath_LostKeyboardFocus(
        object sender, System.Windows.Input.KeyboardFocusChangedEventArgs e)
    {
        // WPF scrolls a single-line editable path towards its caret.
        // Once editing ends, show the recognizable drive/root prefix again;
        // never change the path string, installer state or a focused edit.
        GamePathBox.CaretIndex = 0;
        GamePathBox.ScrollToHorizontalOffset(0);
    }
    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        ClearStatusPin();
        await RefreshSourceAsync();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (Workflow.HasRecoveryJournal)
        {
            PinStatus("Game folder locked. Use RESTORE PROFILERS.");
            return;
        }
        using var browse = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the Cyberpunk 2077 game folder",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(GamePathBox.Text) ? GamePathBox.Text : ""
        };
        if (browse.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            GamePathBox.Text = browse.SelectedPath;
        else await RefreshSourceAsync();
    }

    private void UpdatePathLock()
    {
        // A journal is written before owner mutations, so interrupted installs
        // also lock the root. This survives an application restart.
        var locked = Workflow.HasRecoveryJournal;
        GamePathBox.IsReadOnly = locked;
        GamePathBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy && !locked;
        RefreshButton.IsEnabled = !busy;
    }

    private void SetBusy(bool isBusy)
    {
        busy = isBusy;
        UpdatePathLock();
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = false;
        if (!isBusy && (pendingExternalGameStart || pendingGameExitSessionId is not null))
            QueueGameReconciliation();
    }

    private void DisplayConsole(string message, string? detail = null)
    {
        StatusText.Text = UserFacingConsoleText.Compact(message);
        // The console stays readable; full owner diagnostics remain available
        // from hover and tracing without covering the artwork or controls.
        StatusText.ToolTip = detail ?? message;
    }
    private void SetStatus(string message) =>
        DisplayConsole(consolePinned ?? message, consolePinned is null ? null : consolePinnedDetail);
    private void PinStatus(string message, string? detail = null)
    {
        consolePinned = message;
        consolePinnedDetail = detail;
        DisplayConsole(message, detail);
    }
    private void ClearStatusPin()
    {
        consolePinned = null;
        consolePinnedDetail = null;
    }

    // Derive the label/surface directly from the actual, game-bound owner
    // journal. Verified restore deletes the journal; switching source roots
    // never inherits another game's recovery control.
    private bool UpdateProfilerAction(string? gameRoot)
    {
        // A durable journal always exposes recovery, even if damaged data
        // prevents identification of its game root. The click then reports
        // actionable recovery instructions in the console without writing.
        var recovery = Workflow.HasRecoveryJournal;
        InstallButton.Content = recovery ? "RESTORE PROFILERS" : "INSTALL PROFILERS";
        InstallButton.Style = (Style)FindResource(
            recovery ? "RestoreStageButton" : "StageActionButton");
        return recovery;
    }

    private async Task RefreshSourceAsync()
    {
        if (!initialized || busy) return;
        if (captureAndRestoreVerified && ResultsPage.Visibility == Visibility.Visible)
        {
            RefreshCompletedWorkspace();
            return;
        }
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        var cancel = refreshCancellation = new CancellationTokenSource();
        // If the saved root was changed outside GCO while closed, recover it
        // from the installation journal before inspecting any new sources.
        var ownedRoot = Workflow.ReadRecoveryGameRoot();
        if (ownedRoot is not null &&
            !string.Equals(GamePathBox.Text, ownedRoot, StringComparison.OrdinalIgnoreCase))
        {
            pathUpdate = true;
            GamePathBox.Text = ownedRoot;
            pathUpdate = false;
        }
        UpdatePathLock();
        var current = GameInspector.Inspect(GamePathBox.Text);
        if (current.SourceKind == SourceKind.LiveCetMods &&
            current.IsValidGame && current.GameRoot is not null &&
            !current.GameRoot.Equals(GamePathBox.Text, StringComparison.OrdinalIgnoreCase))
        {
            pathUpdate = true;
            GamePathBox.Text = current.GameRoot;
            GamePathBox.CaretIndex = 0;
            GamePathBox.ScrollToHorizontalOffset(0);
            pathUpdate = false;
        }
        inspection = current;
        liveState = null;
        savedCapture = null;
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = false;
        LaunchButton.Content = "START CYBERPUNK";
        CollectButton.Content = "COLLECT RESULTS";
        var recoveryMode = UpdateProfilerAction(
            current.IsValidGame ? current.GameRoot : GamePathBox.Text);
        // Recovery is always clickable; when the game is open its handler
        // gives a console instruction instead of touching the loaded files.
        if (recoveryMode) InstallButton.IsEnabled = true;

        UserSettings.WriteGameRoot(GamePathBox.Text);
        if (!current.IsValidGame)
        {
            ModCountText.Text = "Select the Cyberpunk 2077 game folder";
            DependencyText.Text = "CET, 0-Engine, r6/scripts and RED4ext have not been checked.";
            DependencyText.Foreground = (Brush)FindResource("Cyan");
            CetStatusText.Text = "G-CET Profiler  ·  NOT CHECKED";
            RedStatusText.Text = "G-REDscript Profiler  ·  NOT CHECKED";
            PresentStatusText.Text = "PresentMon  ·  AUTOMATIC";
            SetStatus(Workflow.HasRecoveryJournal
                ? "Game folder unavailable. Use RESTORE PROFILERS."
                : "Select a valid Cyberpunk 2077 game folder.");
            return;
        }
        var redOwners = RedscriptModCounter.CountPhysical(current.GameRoot);
        ModCountText.Text = $"Game found  ·  {current.CetModCount} CET mods  ·  {redOwners} REDscript groups";
        // Installer admission is based on actual framework versions, never
        // on mod counts or presence of an empty r6/scripts folder.
        // Owner status must be inspected before framework revalidation: GCO's
        // temporary CET native DLL is not the original pre-install binary.
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready)
        {
            SetStatus("GCO package verification failed. Re-extract the official build.");
            Trace.TraceError("GCO package verification: " + bundle.Detail);
            return;
        }
        try
        {
            var state = await Workflow.InspectAsync(current.GameRoot!, cancel.Token);
            if (cancel.IsCancellationRequested || !IsLoaded || busy ||
                (captureAndRestoreVerified && ResultsPage.Visibility == Visibility.Visible)) return;
            liveState = state;
            var running = GameRunning();
            var session = UpdateProfilerAction(current.GameRoot!);
            var foreignSession = Workflow.HasRecoveryJournal && !session;
            // A game-bound, verified owner installation reuses the original
            // admitted baseline until verified restore removes the journal.
            // No extra profiler version whitelist is needed.
            var pinnedFramework = Workflow.HasInstalledFrameworkBaseline(current.GameRoot!, state);
            var framework = pinnedFramework
                ? new FrameworkPreflightResult(Array.Empty<string>())
                : FrameworkPreflight.Inspect(current.GameRoot!);
            DependencyText.Text = framework.Ready
                ? "CET 1.37.1  ·  0-Engine present  ·  RED4ext 1.30.0  ·  redscript 0.5.31"
                : framework.Detail;
            DependencyText.Foreground = (Brush)FindResource(framework.Ready ? "Yellow" : "Cyan");
            var saved = session ? Workflow.ReadCollected(current.GameRoot!) : null;
            savedCapture = saved;
            var frame = PresentMonCaptureController.ReadTrackedCsv(packageRoot);

            // This row describes installed GCO profilers, not base CET/REDscript.
            // CET "OFFICIAL" is the original loader, not our managed profiler.
            var cetProfiler = state.CetManaged ? "INSTALLED" : "NOT INSTALLED";
            var redProfiler = state.GrspManaged ? "INSTALLED" : "NOT INSTALLED";
            CetStatusText.Text = "G-CET Profiler  ·  " + cetProfiler;
            RedStatusText.Text = "G-REDscript Profiler  ·  " + redProfiler;
            // PresentMon recording evidence is handled separately and remains
            // insufficient to authorize Continue without native capture.
            PresentStatusText.Text = "PresentMon  ·  AUTOMATIC";

            // Collect is available after a launch even if F11 was never used,
            // but is disabled once this attempt's PresentMon shutdown and
            // collection have been recorded. A failed stop remains retryable.
            var captureStarted = Workflow.HasCaptureAttempt(current.GameRoot!);
            var readyToCollect = CaptureAdmissionPolicy.CanCollect(session, captureStarted, saved);
            var recentLaunch = captureLaunched &&
                DateTime.UtcNow - lastLaunchRequestedUtc < TimeSpan.FromSeconds(10);
            InstallButton.IsEnabled = session ||
                (!foreignSession && framework.Ready && state.CleanForInstall && !running);
            // Do not start another game while the previous attempt still
            // needs collection; BeginCaptureAttempt creates a fresh identity
            // after collection, which re-enables Collect on the next launch.
            // A user may deliberately skip Collect: a later Start discards
            // ONLY GCO's uncollected attempt after stopping owned PresentMon.
            LaunchButton.IsEnabled = pinnedFramework &&
                !running && !recentLaunch;
            CollectButton.IsEnabled = !running && readyToCollect;
            ContinueButton.IsEnabled = !running &&
                CaptureAdmissionPolicy.CanContinue(saved, frame is null);
            // Adapt the captions to actual verified workflow state. Disabled
            // controls never suggest an unverified domain is installed.
            LaunchButton.Content = running ? "GAME RUNNING"
                : captureLaunched ? "CAPTURE STARTED" : "START CYBERPUNK";
            CollectButton.Content = "COLLECT RESULTS";
            resultsFolder = HasNative(saved) ? CollectedCaptureStore.SessionFolder(packageRoot, saved!.SessionId) : null;

            SetStatus(foreignSession
                ? "Another game session is pending. Restore it before switching games."
                : !framework.Ready ? "Framework check failed. See requirements above."
                : running ? "Cyberpunk running. Use F11 to start/stop profiling."
                : saved?.CollectionAttempted == true
                    ? CaptureAdmissionPolicy.CollectionStatus(saved)
                    : readyToCollect
                        ? "Game closed. Click COLLECT RESULTS."
                        : state.BothInstalled
                            ? "Profilers installed. Ready to start Cyberpunk."
                            : session
                                ? "Profiler recovery needed. Click RESTORE PROFILERS."
                                : $"CET: {cetProfiler} | REDscript: {redProfiler}.");
            if (HasNative(saved) && portraitStage != PortraitStage.Collected)
                SetPortraitStage(PortraitStage.Collected);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested && IsLoaded)
            {
                Trace.TraceWarning("Profiler status inspection: " + ex);
                SetStatus("Profiler status unavailable. Retry Refresh.");
            }
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        // No modal dialog and no admission gate may suppress a requested
        // recovery attempt. The owner journal supplies the only target.
        if (Workflow.HasRecoveryJournal)
            await RestoreIncompleteSessionAsync();
        else
            await InstallProfilersAsync();
    }
    private async Task InstallProfilersAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        ClearStatusPin();
        var frameworks = FrameworkPreflight.Inspect(inspection.GameRoot);
        if (!frameworks.Ready) { Warn(frameworks.Detail, "Framework preflight"); return; }
        SetBusy(true);
        SetStatus("Installing CET and REDscript profilers...");
        try
        {
            var result = await Workflow.InstallAsync(inspection.GameRoot);
            if (result.Ok)
            {
                captureLaunched = false;
                SetPortraitStage(PortraitStage.Black);
                // InstallAsync already re-inspects both source-owned managers.
                PinStatus("Profilers installed. Ready to start Cyberpunk.");
            }
            else Warn(result.Detail, "Profiler installation");
        }
        catch (Exception ex) { Warn(ex.Message, "Profiler installation"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    // A new GCO or directly observed Cyberpunk launch follows the exact same
    // rules: gracefully stop old owned ETW output; discard ONLY uncollected GCO
    // files; provision a new identity; start fresh PresentMon. Source-owner
    // native capture state remains untouched, and pre-existing native results
    // are quarantined from this attempt in collection.json.
    private async Task<string?> BeginFreshCaptureAsync(string game,
        LiveProfilerState state, bool gameAlreadyRunning)
    {
        if (!Workflow.HasInstalledFrameworkBaseline(game, state))
            throw new InvalidOperationException(
                "Verified, game-bound profiler installation is required. RESTORE PROFILERS remains available.");
        if (!ComponentBundleVerifier.Inspect(packageRoot).Ready)
            throw new InvalidOperationException("Bundled profiler provenance changed.");
        var oldId = Workflow.ReadRecoverySessionId(game) ??
                    Guid.NewGuid().ToString("N");
        var stopped = await StopOwnedPresentMonAsync(oldId);
        if (!stopped.Stopped)
            throw new InvalidOperationException("Previous GCO PresentMon must stop before starting a new attempt: " +
                stopped.Error);
        // An explicit Collect preserves its evidence and never gets discarded.
        // Stale native-owner archives are not copied into the next attempt.
        Workflow.AbandonCurrentUncollected(game);
        var attemptId = Workflow.BeginCaptureAttempt(game,
            observedGameAlreadyRunning: gameAlreadyRunning,
            ownerBeforeCapture: state);
        captureLaunched = true;
        lastLaunchRequestedUtc = DateTime.UtcNow;
        try
        {
            var csv = PresentMonLifecyclePolicy.NewSessionCsv(packageRoot, attemptId);
            await PresentMonCaptureController.RunAsync(packageRoot, csv, restart: true);
            stoppedPresentMonCsv = null;
            return null;
        }
        catch (Exception ex)
        {
            // A PresentMon-only failure does not invent native evidence or
            // silently reuse old CSVs. CET/GRSP F11 remain independently usable.
            return ex.Message;
        }
    }

    private async Task HandleExternalGameLaunchAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null ||
            !Workflow.HasSessionForGame(inspection.GameRoot)) return;
        var game = inspection.GameRoot;
        SetBusy(true);
        try
        {
            var state = await Workflow.InspectAsync(game);
            var warning = await BeginFreshCaptureAsync(game, state,
                gameAlreadyRunning: true);
            // The game may terminate during WMI notification / elevated ETW
            // startup (e.g. redscript compilation failure). Flush the NEW
            // attempt, never an old ID read during the earlier exit event.
            if (!GameRunning() && Workflow.HasCaptureAttempt(game))
                pendingGameExitSessionId = Workflow.ReadRecoverySessionId(game);
            SetPortraitStage(PortraitStage.Capturing);
            PinStatus(warning is null
                ? "New game detected. Fresh capture ready. Use F11."
                : "New game detected. Use F11; PresentMon unavailable.",
                warning);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Automatic capture restart: " + ex);
            PinStatus("Capture restart failed. Close Cyberpunk and use Restore.",
                ex.ToString());
        }
        finally { SetBusy(false); }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchCaptureAsync();
    private async Task LaunchCaptureAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        if (GameRunning()) { Warn("Cyberpunk is already running.", "Start"); return; }
        if (captureLaunched && DateTime.UtcNow - lastLaunchRequestedUtc < TimeSpan.FromSeconds(10))
        {
            Warn("The previous launch is still being processed.", "Start");
            return;
        }
        var game = inspection.GameRoot;
        var executable = Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe");
        if (!File.Exists(executable))
        {
            Warn("Cyberpunk2077.exe is missing.", "Start");
            return;
        }
        ClearStatusPin();
        SetBusy(true);
        try
        {
            var state = await Workflow.InspectAsync(game);
            var warning = await BeginFreshCaptureAsync(game, state,
                gameAlreadyRunning: false);
            try
            {
                using var started = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                }) ?? throw new InvalidOperationException("Cyberpunk did not start.");
                gcoLaunchedGamePid = started.Id;
                GameStarted(started.Id); // Process.Exited, no 2-second polling
            }
            catch
            {
                // A failed game launch is not a meaningful capture. Flush ETW,
                // then drop its own uncollected folder without touching CET /
                // REDscript native files or the recovery journal.
                var id = Workflow.ReadRecoverySessionId(game) ??
                         Guid.NewGuid().ToString("N");
                var stop = await StopOwnedPresentMonAsync(id);
                if (stop.Stopped) Workflow.AbandonCurrentUncollected(game);
                throw;
            }
            SetPortraitStage(PortraitStage.Capturing);
            PinStatus(warning is null
                ? "Cyberpunk started. Use F11 to start/stop profiling."
                : "Cyberpunk started. F11 ready; PresentMon unavailable.",
                warning);
        }
        catch (Exception ex) { Warn(ex.Message, "Launch and capture"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    // Collect and independent recovery share ONE PresentMon stop policy:
    // only our named ETW session/process can be stopped, and only a verified
    // graceful exit permits Continue. No F11 handler is installed by GCO.
    private async Task<(string? Csv, bool Stopped, string? Error)> StopOwnedPresentMonAsync(
        string sessionId, bool clearTracking = true)
    {
        string? tracked;
        try { tracked = PresentMonCaptureController.ReadTrackedCsv(packageRoot); }
        catch (Exception ex) { return (null, false, "PresentMon tracking invalid: " + ex.Message); }

        try
        {
            var ownedRunning = PresentMonCaptureController.HasOwnedRunning(packageRoot);
            if (tracked is null && !ownedRunning)
                return (null, true, null);
            // A completed process-exit stop already flushed this exact CSV.
            // Avoid a second unnecessary elevation on Collect, Restart or Exit.
            if (tracked is not null && !ownedRunning &&
                string.Equals(stoppedPresentMonCsv, tracked,
                    StringComparison.OrdinalIgnoreCase))
            {
                if (clearTracking)
                {
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                    stoppedPresentMonCsv = null;
                }
                return (tracked, true, null);
            }
        }
        catch (Exception ex)
        {
            return (tracked, false, "Could not verify PresentMon ownership: " + ex.Message);
        }

        try
        {
            var target = tracked ?? PresentMonLifecyclePolicy.NewSessionCsv(packageRoot, sessionId);
            await PresentMonCaptureController.RunAsync(packageRoot, target, restart: false);
            if (tracked is not null)
            {
                stoppedPresentMonCsv = tracked;
                if (clearTracking)
                {
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                    stoppedPresentMonCsv = null;
                }
            }
            return (tracked, true, null);
        }
        catch (Exception ex)
        {
            return (tracked, false, "PresentMon shutdown not verified: " + ex.Message);
        }
    }

    private async void Collect_Click(object sender, RoutedEventArgs e) => await CollectResultsAsync();
    private async Task CollectResultsAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        var game = inspection.GameRoot;
        if (GameRunning())
        {
            Warn("Close Cyberpunk before collecting results.", "Collection");
            return;
        }
        if (!Workflow.HasCaptureAttempt(game))
        {
            Warn("Start Cyberpunk before collecting results.", "Collection");
            return;
        }
        // Use the persisted current-attempt index, not a UI-only flag, so a
        // completed Collect stays disabled even after reopening GCO.
        CollectedCaptureIndex? existing;
        try { existing = Workflow.ReadCollected(game); }
        catch (Exception ex)
        {
            Warn("Cannot verify collection state: " + ex.Message, "Collection");
            return;
        }
        if (!CaptureAdmissionPolicy.CanCollect(
                Workflow.HasSessionForGame(game), true, existing))
        {
            Warn("Already collected. Start a new session or Restore.", "Collection");
            return;
        }
        ClearStatusPin();
        SetBusy(true);
        SetPortraitStage(PortraitStage.Collecting);
        SetStatus("Collecting profiler results...");
        try
        {
            var attempt = Workflow.ReadCollected(game) ??
                throw new InvalidDataException("No matching GCO capture attempt.");
            // The click is the trigger. Do not gate it on F11, a prior CSV,
            // or whether PresentMon has already stopped independently.
            var stop = await StopOwnedPresentMonAsync(attempt.SessionId);
            string? frameWarning = stop.Error;
            if (stop.Stopped && stop.Csv is not null)
            {
                var expectedRoot = CollectedCaptureStore.SessionFolder(packageRoot, attempt.SessionId) +
                    Path.DirectorySeparatorChar;
                if (!Path.GetFullPath(stop.Csv).StartsWith(expectedRoot,
                    StringComparison.OrdinalIgnoreCase))
                    frameWarning = "PresentMon CSV belongs to an earlier attempt; not reused.";
                else
                {
                    try
                    {
                        CollectedCaptureStore.CollectPresentMon(packageRoot, game,
                            attempt.SessionId, stop.Csv);
                    }
                    catch (Exception ex)
                    {
                        frameWarning = "CSV unavailable: " + ex.Message;
                    }
                }
            }
            var native = await Workflow.CollectResultsAsync(game);
            // This durable record is written only AFTER the user explicitly
            // clicks Collect. Even zero captures receive exact diagnostics.
            var saved = Workflow.RecordCollectionAttempt(game, stop.Stopped);
            saved = Workflow.ReadCollected(game, verifyHashes: true) ?? saved;
            savedCapture = saved;
            captureLaunched = false;
            if (HasNative(saved))
            {
                resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
                SetPortraitStage(PortraitStage.Collected);
                UpdateResultTexts(saved);
            }
            else SetPortraitStage(PortraitStage.Black);

            var nativeWarning = native.Ok &&
                (saved.CetReadyBeforeAttempt || saved.RedscriptReadyBeforeAttempt)
                    ? native.Detail : native.Ok ? null : native.Detail;
            PinStatus(CaptureAdmissionPolicy.CollectionStatus(saved, frameWarning,
                nativeWarning),
                "Collection: " + native.Detail +
                (frameWarning is null ? "" : " | PresentMon: " + frameWarning) +
                (nativeWarning is null ? "" : " | Native: " + nativeWarning));
        }
        catch (Exception ex)
        {
            SetPortraitStage(PortraitStage.Black);
            Warn(ex.Message, "Capture collection");
        }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    private void UpdateResultTexts(CollectedCaptureIndex saved)
    {
        CetReportText.Text = saved.Cet is null
            ? "CET native capture not available."
            : "CET capture verified: " + CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "CET");
        RedReportText.Text = saved.Redscript is null
            ? "REDscript native capture not available."
            : "REDscript capture verified: " + CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "REDscript");
        RedReportText.Text += saved.FrameTimeCsv is null
            ? Environment.NewLine + "PresentMon: no verified frame-time CSV."
            : Environment.NewLine + "PresentMon CSV: " + saved.FrameTimeCsv;
    }

    private async void Continue_Click(object sender, RoutedEventArgs e) => await ContinueToAnalyticsAsync();
    private async Task ContinueToAnalyticsAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        if (captureAndRestoreVerified)
        {
            RefreshCompletedWorkspace();
            CapturePage.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            SetActiveTab(true);
            return;
        }
        var game = inspection.GameRoot;
        if (GameRunning()) { Warn("Close Cyberpunk before restoring.", "Game running"); return; }
        CollectedCaptureIndex? saved;
        try { saved = Workflow.ReadCollected(game, verifyHashes: true); }
        catch (Exception ex) { Warn(ex.Message, "Evidence verification"); return; }
        bool monStopped;
        try { monStopped = PresentMonCaptureController.ReadTrackedCsv(packageRoot) is null; }
        catch (Exception ex) { Warn("PresentMon state invalid: " + ex.Message, "Collection"); return; }
        if (!CaptureAdmissionPolicy.CanContinue(saved, monStopped))
        {
            Warn("Collect CET and REDscript first, or use Restore.", "Continue");
            return;
        }
        SetBusy(true);
        try
        {
            // Only source-owner restoration can authorize Page 2. Missing
            // intentional domains are explicitly acknowledged above.
            var result = await Workflow.ContinueAsync(game,
                skipCet: false, skipRedscript: false,
                skipFrameTime: saved!.FrameTimeCsv is null);
            if (!result.RestoreVerified)
            {
                SetStatus(result.Detail);
                Warn(result.Detail, "Restoration blocked");
                return;
            }
            completedSessionId = saved.SessionId;
            completedGameRoot = game;
            captureAndRestoreVerified = true;
            refreshCancellation?.Cancel();
            string? workspaceWarning = null;
            try { CollectedCaptureStore.RememberRestored(packageRoot, game, saved.SessionId); }
            catch (Exception ex)
            {
                workspaceWarning = "Analyze workspace may not reopen. See details.";
                Trace.TraceWarning("Workspace reopening metadata: " + ex);
            }
            UpdateResultTexts(saved);
            CapturePage.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            SetActiveTab(true);
            RefreshCompletedWorkspace();
            if (workspaceWarning is not null) PinStatus(workspaceWarning);
        }
        catch (Exception ex) { Warn(ex.Message, "Continue failed"); }
        finally { SetBusy(false); }
    }

    private void RefreshCompletedWorkspace()
    {
        if (!captureAndRestoreVerified || completedSessionId is null || completedGameRoot is null)
            return;
        try
        {
            var saved = CollectedCaptureStore.ReadRestored(packageRoot, completedGameRoot, completedSessionId);
            if (!HasNative(saved))
                throw new InvalidDataException("The restored capture has no CET or REDscript native evidence.");
            UpdateResultTexts(saved);
            resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
            ResultsStatusText.Text = "Original game files restored. " +
                ((saved.Skipped?.Count ?? 0) > 0 ? "Skipped: " + string.Join(", ", saved.Skipped!) + ". " : "") +
                "Combined source analysis and generation are not yet integrated.";
            OpenResultsButton.IsEnabled = true;
        }
        catch (Exception ex)
        {
            ResultsStatusText.Text = "Saved capture verification failed: " + ex.Message;
            OpenResultsButton.IsEnabled = false;
        }
    }

    private async Task RestoreIncompleteSessionAsync()
    {
        if (busy) return;
        var game = Workflow.ReadRecoveryGameRoot();
        if (game is null || !Workflow.HasSessionForGame(game))
        {
            Warn("Recovery location invalid. Use the original profiler managers.", "Recovery required");
            return;
        }
        if (GameRunning())
        {
            Warn("Close Cyberpunk before restoring profilers.", "Recovery");
            return;
        }
        ClearStatusPin();
        SetBusy(true);
        SetStatus("Restoring original game files...");
        try
        {
            // No capture index/collection attempt is required. Recover even
            // directly after a partial install, including after GCO restarts.
            var sessionId = Workflow.ReadRecoverySessionId(game) ??
                            Guid.NewGuid().ToString("N");
            var stop = await StopOwnedPresentMonAsync(sessionId);
            string? cleanupWarning = stop.Stopped ? null :
                "PresentMon could not be stopped: " +
                (stop.Error ?? "Shutdown unverified.") +
                " Native owner restoration will still proceed.";
            // Recover original native files independently from ETW shutdown
            // and optional cleanup; neither can make RESTORE inaccessible.
            if (stop.Stopped)
            {
                try { Workflow.AbandonCurrentUncollected(game); }
                catch (Exception ex)
                {
                    cleanupWarning = "Uncollected GCO results preserved: " + ex.Message;
                }
            }
            var result = await Workflow.RestoreOwnedAsync(game);
            if (!result.RestoreVerified)
            {
                Warn(result.Detail, "Recovery required");
                return;
            }
            captureLaunched = false;
            SetPortraitStage(PortraitStage.Black);
            // Owner verification removed the journal. The same button changes
            // immediately from RESTORE to INSTALL, and the path unlocks.
            UpdateProfilerAction(game);
            UpdatePathLock();
            var fullDetails = result.Detail +
                (cleanupWarning is null ? "" : " | " + cleanupWarning);
            Trace.TraceInformation("Profiler restore: " + fullDetails);
            PinStatus(cleanupWarning is null
                    ? "Restore successful. Original game files restored."
                    : "Restore successful. Original files restored; see details.",
                fullDetails);
        }
        catch (Exception ex) { Warn(ex.Message, "Recovery required"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(resultsFolder) && Directory.Exists(resultsFolder))
            Process.Start(new ProcessStartInfo(resultsFolder) { UseShellExecute = true });
    }

    // The pinned console is the one normal diagnostic channel. No modal popup
    // can interrupt collection/recovery or bypass the verified Continue gate.
    private void Warn(string message, string title)
    {
        Trace.TraceWarning(title + ": " + message);
        PinStatus(UserFacingConsoleText.Failure(title, message), title + ": " + message);
    }
}
