using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
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
    // Game exit is not guaranteed to re-activate the WPF window (e.g. a
    // second monitor). A cheap transition-only timer refreshes owner status.
    private readonly DispatcherTimer gameStateTimer = new()
        { Interval = TimeSpan.FromSeconds(2) };
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
    private bool? lastGameRunning;
    private bool pendingGameStateRefresh;
    private bool gameStateRefreshInFlight;
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
        LoadOriginalArtwork();
        pathUpdate = true;
        GamePathBox.Text = UserSettings.ReadGameRoot();
        pathUpdate = false;
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
        lastGameRunning = GameRunning();
        gameStateTimer.Start();
        _ = RefreshSourceAsync();
    }

    // Trigger CLI status checks only on Cyberpunk process transitions, never
    // every timer tick. Preserve the pending refresh if an install/collect/
    // restore transaction owns the UI when the process closes.
    private async void GameStateTimer_Tick(object? sender, EventArgs e)
    {
        if (!initialized || !IsLoaded) return;
        var running = GameRunning();
        if (lastGameRunning is bool prior && prior != running)
        {
            pendingGameStateRefresh = true;
            if (prior && !running) captureLaunched = false;
        }
        lastGameRunning = running;

        if (!pendingGameStateRefresh || busy || gameStateRefreshInFlight ||
            !inspection.IsValidGame) return;
        pendingGameStateRefresh = false;
        gameStateRefreshInFlight = true;
        try { await RefreshSourceAsync(); }
        catch (Exception ex) { Trace.TraceError("Process transition refresh: " + ex); }
        finally { gameStateRefreshInFlight = false; }
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (!initialized || busy) return;
        if (portraitStage is PortraitStage.Capturing or PortraitStage.Collecting)
            StartGlitchIfVisible();
        if (IsLoaded) _ = RefreshSourceAsync();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        gameStateTimer.Stop();
        glitchTimer.Stop();
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
    }

    private async void GamePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (initialized && !pathUpdate && !busy) await RefreshSourceAsync();
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
        consolePinned = null;
        await RefreshSourceAsync();
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
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

    private void SetBusy(bool isBusy)
    {
        busy = isBusy;
        GamePathBox.IsEnabled = BrowseButton.IsEnabled = RefreshButton.IsEnabled = !isBusy;
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = !isBusy && false;
        RestoreButton.IsEnabled = !isBusy;
    }

    private void SetStatus(string message) => StatusText.Text = consolePinned ?? message;
    private void PinStatus(string message)
    {
        consolePinned = message;
        StatusText.Text = message;
    }
    private void ClearStatusPin() => consolePinned = null;

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
        InstallButton.Content = "INSTALL PROFILERS";
        LaunchButton.Content = "START CYBERPUNK";
        CollectButton.Content = "COLLECT RESULTS";
        RestoreButton.Visibility = Visibility.Collapsed;

        UserSettings.WriteGameRoot(GamePathBox.Text);
        if (!current.IsValidGame)
        {
            ModCountText.Text = "Select the Cyberpunk 2077 game folder";
            DependencyText.Text = "CET, 0-Engine, r6/scripts and RED4ext have not been checked.";
            CetStatusText.Text = "CET Profiler  ·  NOT CHECKED";
            RedStatusText.Text = "REDscript Profiler  ·  NOT CHECKED";
            PresentStatusText.Text = "PresentMon  ·  AUTOMATIC";
            SetStatus(current.Summary);
            return;
        }
        var redOwners = RedscriptModCounter.CountPhysical(current.GameRoot);
        ModCountText.Text = $"Game found  ·  {current.CetModCount} CET mods  ·  {redOwners} REDscript groups";
        var cetSource = current.Checks.Any(c => c.Name == "CET source folder" &&
            c.State != GTotalOptimizer.Core.CheckState.Missing);
        var zeroEngine = current.Checks.Any(c => c.Name == "0-Engine source" &&
            c.State != GTotalOptimizer.Core.CheckState.Missing);
        var scripts = current.Checks.Any(c => c.Name == "REDscript source folder" &&
            c.State != GTotalOptimizer.Core.CheckState.Missing);
        var red4ext = current.Checks.Any(c => c.Name == "RED4ext plugin folder" &&
            c.State != GTotalOptimizer.Core.CheckState.Missing);
        // Installer admission is based on actual framework versions, never
        // on mod counts or presence of an empty r6/scripts folder.
        var framework = FrameworkPreflight.Inspect(current.GameRoot!);
        DependencyText.Text = framework.Ready
            ? "CET 1.37.1  ·  0-Engine >= 0.18.0  ·  RED4ext 1.30.0  ·  redscript 0.5.31"
            : framework.Detail;
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready)
        {
            SetStatus("Bundle provenance failed: " + bundle.Detail);
            return;
        }
        try
        {
            var state = await Workflow.InspectAsync(current.GameRoot!, cancel.Token);
            if (cancel.IsCancellationRequested || !IsLoaded || busy ||
                (captureAndRestoreVerified && ResultsPage.Visibility == Visibility.Visible)) return;
            liveState = state;
            var running = GameRunning();
            var session = Workflow.HasRecoveryJournal;
            var saved = session ? Workflow.ReadCollected(current.GameRoot!) : null;
            savedCapture = saved;
            var frame = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            var sameFrameSession = saved is not null && frame is not null &&
                Path.GetFullPath(frame).StartsWith(
                    CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

            // This row describes installed GCO profilers, not base CET/REDscript.
            // CET "OFFICIAL" is the original loader, not our managed profiler.
            var cetProfiler = state.CetManaged ? "INSTALLED" : "NOT INSTALLED";
            var redProfiler = state.GrspManaged ? "INSTALLED" : "NOT INSTALLED";
            CetStatusText.Text = "CET Profiler  ·  " + cetProfiler;
            RedStatusText.Text = "REDscript Profiler  ·  " + redProfiler;
            // PresentMon recording evidence is handled separately and remains
            // insufficient to authorize Continue without native capture.
            PresentStatusText.Text = "PresentMon  ·  AUTOMATIC";

            // Collect is the next action AFTER launch, even if F11 failed.
            // It is the Collect click itself that stops PresentMon.
            var captureStarted = Workflow.HasCaptureAttempt(current.GameRoot!);
            var readyToCollect = CaptureAdmissionPolicy.CanCollect(session, captureStarted);
            var recentLaunch = captureLaunched &&
                DateTime.UtcNow - lastLaunchRequestedUtc < TimeSpan.FromSeconds(10);
            InstallButton.IsEnabled = framework.Ready && state.CleanForInstall && !session && !running;
            LaunchButton.IsEnabled = framework.Ready && state.BothInstalled && session &&
                !running && !recentLaunch && (!state.BothCaptured || saved?.CollectionAttempted == true);
            CollectButton.IsEnabled = readyToCollect;
            ContinueButton.IsEnabled = !running &&
                CaptureAdmissionPolicy.CanContinue(saved, frame is null);
            // Adapt the captions to actual verified workflow state. Disabled
            // controls never suggest an unverified domain is installed.
            InstallButton.Content = state.CetManaged && state.GrspManaged
                ? "PROFILERS INSTALLED" : "INSTALL PROFILERS";
            LaunchButton.Content = running ? "GAME RUNNING"
                : captureLaunched ? "CAPTURE STARTED" : "START CYBERPUNK";
            CollectButton.Content = HasNative(saved) ? "RESULTS VERIFIED" : "COLLECT RESULTS";
            RestoreButton.Visibility = session ? Visibility.Visible : Visibility.Collapsed;
            resultsFolder = HasNative(saved) ? CollectedCaptureStore.SessionFolder(packageRoot, saved!.SessionId) : null;

            SetStatus(!framework.Ready ? framework.Detail
                : running ? "Cyberpunk running. F11 controls CET + REDscript. Click COLLECT RESULTS after closing the game."
                : saved?.CollectionAttempted == true
                    ? CaptureAdmissionPolicy.CollectionStatus(saved)
                    : readyToCollect
                        ? "Click COLLECT RESULTS to stop PresentMon and inspect all three captures."
                        : state.BothInstalled
                            ? "G-CET and G-REDscript profilers installed. Ready to start Cyberpunk 2077."
                            : session
                                ? "Profiler session needs recovery; use RESTORE PROFILERS."
                                : $"G-CET Profiler: {cetProfiler}  /  G-REDscript Profiler: {redProfiler}.");
            if (HasNative(saved) && portraitStage != PortraitStage.Collected)
                SetPortraitStage(PortraitStage.Collected);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested && IsLoaded)
                SetStatus("Owner status unavailable: " + ex.Message);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e) => await InstallProfilersAsync();
    private async Task InstallProfilersAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        SetBusy(true);
        SetStatus("Installing native profilers through their authoritative owners...");
        try
        {
            var result = await Workflow.InstallAsync(inspection.GameRoot);
            if (result.Ok) { captureLaunched = false; SetPortraitStage(PortraitStage.Black); }
            SetStatus(result.Detail);
            if (!result.Ok) Warn(result.Detail, "Profiler installation");
        }
        catch (Exception ex) { Warn(ex.Message, "Profiler installation"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e) => await LaunchCaptureAsync();
    private async Task LaunchCaptureAsync()
    {
        if (busy || captureLaunched || GameRunning() ||
            !inspection.IsValidGame || inspection.GameRoot is null) return;
        var game = inspection.GameRoot;
        SetBusy(true);
        try
        {
            var state = await Workflow.InspectAsync(game);
            if (!state.BothInstalled || !Workflow.HasRecoveryJournal)
                throw new InvalidOperationException("The current backend requires both verified profilers and a GCO session. Independent-domain support is pending.");
            var executable = Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe");
            if (!File.Exists(executable))
                throw new FileNotFoundException("Cyberpunk2077.exe is missing.", executable);
            if (!ComponentBundleVerifier.Inspect(packageRoot).Ready)
                throw new InvalidOperationException("Bundled profiler provenance changed.");
            var capture = Workflow.ReadCollected(game) ??
                throw new InvalidDataException("Capture session identity is missing.");
            string? frameWarning = null;
            try
            {
                var csv = PresentMonLifecyclePolicy.NewSessionCsv(packageRoot, capture.SessionId);
                await PresentMonCaptureController.RunAsync(packageRoot, csv, restart: true);
            }
            catch (Exception ex) { frameWarning = ex.Message; }
            try
            {
                _ = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                }) ?? throw new InvalidOperationException("Cyberpunk did not start.");
            }
            catch
            {
                var active = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
                if (active is not null)
                {
                    await PresentMonCaptureController.RunAsync(packageRoot, active, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                }
                throw;
            }
            captureLaunched = true;
            SetPortraitStage(PortraitStage.Capturing);
            SetStatus(frameWarning is null
                ? "Game launched. PresentMon recording; F11 starts/stops CET and REDscript."
                : "Game launched with native profilers; optional PresentMon failed.");
            if (frameWarning is not null) Warn("PresentMon unavailable: " + frameWarning, "Optional frame-time capture");
        }
        catch (Exception ex) { Warn(ex.Message, "Launch and capture"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    private async void Collect_Click(object sender, RoutedEventArgs e) => await CollectResultsAsync();
    private async Task CollectResultsAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        var game = inspection.GameRoot;
        if (GameRunning()) { Warn("Close Cyberpunk before collecting.", "Game running"); return; }
        SetBusy(true);
        SetPortraitStage(PortraitStage.Collecting);
        SetStatus("Stopping PresentMon and verifying/copying current-session evidence...");
        try
        {
            var capture = Workflow.ReadCollected(game) ??
                throw new InvalidDataException("No matching GCO session.");
            var tracked = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            string? frameWarning = null;
            if (tracked is not null)
            {
                try
                {
                    var expectedRoot = CollectedCaptureStore.SessionFolder(packageRoot, capture.SessionId) +
                        Path.DirectorySeparatorChar;
                    if (!Path.GetFullPath(tracked).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("PresentMon CSV is from another capture session.");
                    await PresentMonCaptureController.RunAsync(packageRoot, tracked, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                    try { CollectedCaptureStore.CollectPresentMon(packageRoot, game, capture.SessionId, tracked); }
                    catch (Exception ex) { frameWarning = "PresentMon stopped, CSV unusable: " + ex.Message; }
                }
                catch (Exception ex)
                {
                    frameWarning = "PresentMon stop failed; retry Collect: " + ex.Message;
                }
            }
            var result = await Workflow.CollectResultsAsync(game);
            var saved = Workflow.ReadCollected(game, verifyHashes: true);
            savedCapture = saved;
            if (HasNative(saved))
            {
                resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved!.SessionId);
                SetPortraitStage(PortraitStage.Collected);
                UpdateResultTexts(saved);
            }
            else
                SetPortraitStage(PortraitStage.Black);
            SetStatus(result.Detail + (frameWarning is null ? "" : " " + frameWarning));
            // Deliberately no missing-domain popup here; Continue owns the prompt.
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

    private bool ConfirmPartialResults(CollectedCaptureIndex saved)
    {
        var missing = new List<string>();
        if (liveState?.CetManaged == true && saved.Cet is null) missing.Add("CET");
        if (liveState?.GrspManaged == true && saved.Redscript is null) missing.Add("REDscript");
        if (saved.FrameTimeCsv is null) missing.Add("PresentMon (optional frame timing)");
        if (missing.Count == 0) return true;

        var dlg = new Window
        {
            Title = "Incomplete capture evidence",
            Icon = Icon,
            Owner = this,
            Width = 520,
            Height = 250,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = new SolidColorBrush(Color.FromRgb(12, 21, 25)),
            Foreground = Brushes.White,
            ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = "Missing expected captures: " + string.Join(", ", missing),
            FontSize = 15, TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(255, 240, 0))
        });
        panel.Children.Add(new TextBlock
        {
            Text = "RETURN keeps your verified results and lets you try again. CONTINUE acknowledges missing evidence, then restores the native profilers.",
            Margin = new Thickness(0, 14, 0, 20),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var back = new System.Windows.Controls.Button { Content = "RETURN", MinWidth = 112, Padding = new Thickness(13, 7, 13, 7), Margin = new Thickness(0, 0, 12, 0) };
        var proceed = new System.Windows.Controls.Button { Content = "CONTINUE", MinWidth = 112, Padding = new Thickness(13, 7, 13, 7) };
        back.Click += (_, _) => dlg.DialogResult = false;
        proceed.Click += (_, _) => dlg.DialogResult = true;
        buttons.Children.Add(back); buttons.Children.Add(proceed);
        panel.Children.Add(buttons);
        dlg.Content = panel;
        return dlg.ShowDialog() == true;
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
        if (!HasNative(saved)) return;
        if (PresentMonCaptureController.ReadTrackedCsv(packageRoot) is not null)
        {
            Warn("PresentMon is still active. Use COLLECT RESULTS to stop it.", "Frame capture active");
            return;
        }
        if (!ConfirmPartialResults(saved!)) return;
        SetBusy(true);
        try
        {
            // Only source-owner restoration can authorize Page 2. Missing
            // intentional domains are explicitly acknowledged above.
            var result = await Workflow.ContinueAsync(game,
                skipCet: saved!.Cet is null, skipRedscript: saved.Redscript is null,
                skipFrameTime: saved.FrameTimeCsv is null);
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
            UpdateResultTexts(saved);
            CapturePage.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            SetActiveTab(true);
            RefreshCompletedWorkspace();
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

    private async void Restore_Click(object sender, RoutedEventArgs e) => await RestoreIncompleteSessionAsync();
    private async Task RestoreIncompleteSessionAsync()
    {
        if (busy || !inspection.IsValidGame || inspection.GameRoot is null ||
            !Workflow.HasRecoveryJournal) return;
        if (GameRunning()) { Warn("Close Cyberpunk before restoring.", "Game running"); return; }
        var game = inspection.GameRoot;
        try { _ = await Workflow.InspectAsync(game); }
        catch (Exception ex) { Warn("Owner status unavailable: " + ex.Message, "Recovery"); return; }
        var choice = System.Windows.MessageBox.Show(this,
            "End this profiling session and restore originals now? Already archived results remain intact. Incomplete diagnostics will be preserved.",
            "Restore original files", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (choice != MessageBoxResult.Yes) return;
        SetBusy(true);
        try
        {
            var result = await Workflow.RestoreOwnedAsync(game);
            var csv = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            string? frameError = null;
            if (csv is not null)
            {
                try
                {
                    await PresentMonCaptureController.RunAsync(packageRoot, csv, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                }
                catch (Exception ex) { frameError = ex.Message; }
            }
            captureLaunched = false;
            SetPortraitStage(PortraitStage.Black);
            Warn(result.Detail + (frameError is null ? "" : "\nPresentMon: " + frameError),
                result.RestoreVerified ? "Restored" : "Recovery required");
        }
        catch (Exception ex) { Warn(ex.Message, "Recovery"); }
        finally { SetBusy(false); await RefreshSourceAsync(); }
    }

    private void OpenResults_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(resultsFolder) && Directory.Exists(resultsFolder))
            Process.Start(new ProcessStartInfo(resultsFolder) { UseShellExecute = true });
    }

    private void Warn(string message, string title) =>
        System.Windows.MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
