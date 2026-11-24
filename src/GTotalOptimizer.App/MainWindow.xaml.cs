using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using System.Windows.Threading;
using GTotalOptimizer.Core;
using GRedscript.Resolver;

namespace GTotalOptimizer.App;

// WPF presentation only. All native install/collect/restore transactions stay
// in the authoritative CET and GRSP owner managers via UnifiedProfilerWorkflow.
public partial class MainWindow : Window
{
    private enum PortraitStage { Black, Installed, Capturing, Collecting, Results, Analyzing, Generating, Completed }
    private static readonly Brush AnalyzeYellow = new SolidColorBrush(Color.FromRgb(255, 240, 0));
    private static readonly Brush AnalyzeOkGreen = new SolidColorBrush(Color.FromRgb(101, 220, 162));
    private readonly string packageRoot = PortablePackagePaths.CurrentPackageRoot();
    private readonly DispatcherTimer glitchTimer = new();
    // One WPF-only progress indicator shared by Capture and Analyze consoles.
    // Tokens keep independent operations (for example report generation)
    // from stopping another operation's indicator prematurely.
    private readonly DispatcherTimer consoleActivityTimer = new()
        { Interval = TimeSpan.FromMilliseconds(350) };
    // A UI-only interpolator, active only while Collect/Analyze/Generate runs.
    private readonly DispatcherTimer workProgressTimer = new()
        { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Stopwatch workProgressClock = new();
    private readonly CheckpointProgressSmoother progressSmoother = new();
    private sealed record ConsoleActivity(
        string Message, bool Results, string PreviousText, long StatusRevision);
    private long captureConsoleRevision;
    private long resultsConsoleRevision;
    private string latestCaptureConsoleText = "";
    private string latestResultsConsoleText = "";
    private readonly Dictionary<long, ConsoleActivity> consoleActivities = new();
    private long nextConsoleActivityId;
    private int consoleActivityDots;
    private string? lastActivityRender;
    // Windows start notifications and process-handle exit events are idle
    // until the game transitions. Only start a slow timer if WMI is unavailable.
    private readonly GameProcessEvents gameProcesses = new();
    private readonly DispatcherTimer gameStateTimer = new()
        { Interval = TimeSpan.FromSeconds(10) };
    private readonly PortraitSignal portraitSignal = new();
    private readonly Stopwatch portraitClock = new();
    private CancellationTokenSource? refreshCancellation;
    private GameInspection inspection = GameInspector.Inspect(null);
    private LiveProfilerState? liveState;
    private CollectedCaptureIndex? savedCapture;
    private string? completedSessionId;
    private string? completedGameRoot;
    private bool captureAndRestoreVerified;
    private readonly CetReadOnlyAnalysisService readOnlyAnalyzer = new();
    private readonly CombinedOptimizationPassService combinedPassGenerator = new();
    private bool redscriptPlanReady;
    private CombinedPassAvailability? analyzedAvailability;
    private CetAnalysisOutcome? analyzedCetCounts;
    private RedscriptGenerationPlan.Document? analyzedRedscriptCounts;
    private bool hotscenesOptInRefused;
    // This session's successfully published combined optimization ZIP only.
    // OPEN FOLDER selects the actual file, not the GCO application root.
    private string? generatedPassZipPath;
    private CancellationTokenSource? analysisCancellation;
    private bool analysisRunning;
    private bool generationRunning;
    private bool profilerReportRunning;
    private string? analyzedSessionId;
    // WMI can report our game process before Process.Start returns.
    // Classify that event as GCO-owned; the tray handoff itself never waits.
    private bool gcoLaunchInProgress;
    // Only protects the short gap between clicking START and observing
    // Cyberpunk2077.exe. An observed process uses the ordinary running gate.
    private DateTime launchProcessGuardUntilUtc = DateTime.MinValue;
    private int launchProcessGuardGeneration;
    private string? consolePinned;
    private bool gameStateRefreshInFlight;
    private bool pendingExternalGameStart;
    private int? pendingExternalGamePid;
    // Process exit schedules UI refresh, never PresentMon shutdown.
    private bool pendingGameExitRefresh;
    private int? observedGamePid;
    private int? gcoLaunchedGamePid;
    private bool closingInProgress;
    private bool closureCleanupVerified;
    // Only a successful START CYBERPUNK hibernates WPF while retaining the tray.
    private bool captureHibernationClose;
    private bool busy;
    private bool pathUpdate;
    private bool initialized;
    private bool mo2Mode;
    private bool mo2SelectionUpdating;
    private bool mo2RefreshRunning;
    private bool mo2StartupRunning;
    private Mo2CompanionStatus? mo2Connected;
    private Mo2ProfileSelection? mo2SelectedSource;
    private FrameworkPreflightResult? mo2Framework;
    private Mo2OverlayPreparation? mo2Prepared;
    private bool mo2LaunchAttempted;
    private bool mo2RecoveryBlocked;
    private bool mo2VerifyRequired;
    // The normal XAML tree is permanent. Maximize only overrides values on
    // existing sections and restores the exact saved local values afterward.
    private StackPanel? maximizedGameRootSection;
    private Grid? maximizedWorkflowCards;
    private Thickness normalHeroMargin;
    private Thickness normalGameRootMargin;
    private Thickness normalHeadingMargin;
    private double normalWorkflowHeight;
    private double normalWorkflowMinHeight;
    private double normalCaptureScrollOffset;
    private double normalCaptureContentHeight;
    private double normalGameRootPanelHeight;
    private Grid[]? maximizedCardColumns;
    private Thickness[]? normalCardMargins;
    private readonly Thickness originalConsoleMargin;
    private readonly Thickness originalResultsConsoleMargin;

    // Fullscreen-only visual tuning: fixed normal XAML values are authoritative.
    // Width/gap/height knobs intentionally do not scale child text or SVGs.
    private const double ExpandedGameRootHeight = 18.0;
    private const double ExpandedCardSideInset = 20.0;
    private const double ExpandedCardGrowthRatio = 0.20;
    private const double ExpandedCardMaxGrowth = 56.0;
    private const double ExpandedHeroGapRatio = 0.25;
    private const double ExpandedRootGapRatio = 0.28;
    private const double ExpandedWorkflowHeadingBottom = 2.0;
    private const double ExpandedConsolePortraitClearance = 112.0;
    private const double ExpandedConsoleRaise = 19.0;
    internal bool WpfVerificationMode { get; set; }
    private PortraitStage portraitStage = PortraitStage.Black;

    private UnifiedProfilerWorkflow Workflow => new(packageRoot);
    private static bool GameRunning() => Process.GetProcessesByName("Cyberpunk2077").Length != 0;
    private static bool HasNative(CollectedCaptureIndex? capture) =>
        capture?.Cet is not null || capture?.Redscript is not null;

    private void SetHotscenesOptionVisible(bool installed)
    {
        if (!installed)
            HotscenesEnglishOnlyCheckBox.IsChecked = false;
        var visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        HotscenesEnglishOnlyCheckBox.Visibility = visibility;
        HotscenesEnglishOnlyDescription.Visibility = visibility;
    }

    private void RefreshHotscenesOption()
    {
        // Check the CURRENT physical game source, not the sealed capture.
        // Removing/changing the mod revokes a previously checked option.
        bool installed = captureAndRestoreVerified &&
            ResultsPage.Visibility == Visibility.Visible &&
            GameInspector.HasInstalledHotscenes(completedGameRoot);
        SetHotscenesOptionVisible(installed);
    }

    // G-CET owner opt-in marker: completion needs absent removed feature hooks.
    private static bool HasCompletedHotscenesEnglishOnly(string game)
    {
        try
        {
            string file = Path.Combine(game, "bin", "x64", "plugins",
                "cyber_engine_tweaks", "mods", "hotscenes", "init.lua");
            if (!File.Exists(file)) return false;
            string source = File.ReadAllText(file);
            return source.Contains("-- G-CET personal:hotscenes-english-only-v1",
                       StringComparison.Ordinal) &&
                !source.Contains("function loadAllLanguageFiles()", StringComparison.Ordinal) &&
                !source.Contains("function getGameUILanguage(", StringComparison.Ordinal) &&
                !source.Contains("function loadLocalizedStrings(", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool HasPendingHotscenesOptIn()
    {
        if (hotscenesOptInRefused || completedGameRoot is null ||
            HotscenesEnglishOnlyCheckBox.Visibility != Visibility.Visible ||
            HotscenesEnglishOnlyCheckBox.IsChecked != true) return false;
        try
        {
            string file = Path.Combine(completedGameRoot, "bin", "x64",
                "plugins", "cyber_engine_tweaks", "mods", "hotscenes", "init.lua");
            if (!File.Exists(file)) return false;
            // Incomplete generated markers are not a new optimization:
            // the CET owner must refuse them rather than reapply a partial pass.
            return !File.ReadAllText(file).Contains(
                "-- G-CET personal:hotscenes-english-only-v1", StringComparison.Ordinal);
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private bool HasNewOptimizationAvailable() =>
        analyzedAvailability?.HasNewOptimizations == true ||
        HasPendingHotscenesOptIn();

    private string NoNewOptimizationStatus()
    {
        int refused = analyzedAvailability?.RedscriptSkipped.Length ?? 0;
        return "NO NEW OPTIMIZATIONS AVAILABLE. All proven compatible work is already installed." +
            (refused > 0
                ? Environment.NewLine + refused +
                  " REDscript operation(s) remain source-refused; not counted as new work."
                : "");
    }

    private bool HasPublishedOptimizationZip() =>
        generatedPassZipPath is not null && File.Exists(generatedPassZipPath);

    // The same DONE action closes the verified workflow after a real pass or
    // after an analyzed no-op. Unproven source refusals do not force a ZIP.
    internal static bool ShouldShowDone(bool publishedZip, bool verifiedAnalysis,
        bool hasNewWork, bool operationRunning) =>
        !operationRunning && (publishedZip || (verifiedAnalysis && !hasNewWork));

    private void RefreshDoneAvailability()
    {
        bool verifiedAnalysis = captureAndRestoreVerified &&
            analyzedSessionId is not null && analyzedSessionId == completedSessionId &&
            redscriptPlanReady && analyzedAvailability is not null;
        bool published = HasPublishedOptimizationZip();
        bool hasNewWork = HasNewOptimizationAvailable();
        bool done = ShouldShowDone(published, verifiedAnalysis, hasNewWork,
            analysisRunning || generationRunning);
        CloseAfterGenerationButton.Visibility =
            done ? Visibility.Visible : Visibility.Collapsed;
        // The terminal V state is also reached by a verified no-op Analyze,
        // where DONE is available without ever running Generate.
        if (ResultsPage.Visibility == Visibility.Visible &&
            verifiedAnalysis && !analysisRunning && !generationRunning)
        {
            if (done) SetPortraitStage(PortraitStage.Completed);
            else if (portraitStage == PortraitStage.Completed)
                SetPortraitStage(PortraitStage.Analyzing);
        }
    }

    // Both panels show equivalent units and three consistent rows.
    // Mod counts come from actual staged source edits, not ZIP support files.
    private void RefreshPlannedWork()
    {
        if (analyzedAvailability is null || analyzedCetCounts is null ||
            analyzedRedscriptCounts is null || !redscriptPlanReady ||
            analyzedSessionId is null || analyzedSessionId != completedSessionId)
            return;

        var cet = analyzedCetCounts;
        var red = analyzedRedscriptCounts;
        var preview = analyzedAvailability;
        int cetChangedMods = preview.CetOptimizedModNames.Length;
        // English-only is an explicit Generate option. Count the still-pending
        // Hotscenes source owner once; do not show an "additional option" item.
        if (HasPendingHotscenesOptIn() &&
            !preview.CetOptimizedModNames.Contains("hotscenes",
                StringComparer.OrdinalIgnoreCase))
            cetChangedMods++;

        CetReportText.Text =
            "Source mods: " + cet.InstalledCetMods +
            " | Source files: " + cet.LiveLuaSourceFiles +
            " | Callback families: " + cet.MeasuredCallbackFamilies +
            " | Generic candidates: " + cet.GenericCandidates +
            " | Semantic rules: " + cet.SemanticCandidates + Environment.NewLine +
            "Already optimized: " + cet.AlreadySatisfied +
            " | Other issues: " + cet.MaterialRemaining + Environment.NewLine +
            "Mods with optimizations: " + cetChangedMods;

        int alreadyComplete = red.Rules.Where(rule => rule.Order is not null)
            .Sum(rule => rule.AlreadyComplete ?? 0);
        RedReportText.Text =
            "Source mods: " + preview.RedscriptSourceModCount +
            " | Source files: " + red.SourceFiles +
            " | Callback families: " +
                (preview.RedscriptCallbackFamilyCount?.ToString() ?? "N/A") +
            " | Generic candidates: " + red.EligibleSourceEdits +
            " | Semantic rules: " + preview.RedscriptSemanticRuleCount +
            Environment.NewLine +
            "Already optimized: " + alreadyComplete +
            " | Other issues: " + preview.RedscriptSkipped.Length +
            Environment.NewLine +
            "Mods with optimizations: " + preview.RedscriptOptimizedModCount;
    }

    private void RefreshGenerateEligibility()
    {
        if (analyzedSessionId is null || analyzedSessionId != completedSessionId ||
            !redscriptPlanReady || analyzedAvailability is null) return;
        RefreshPlannedWork();
        bool ready = HasNewOptimizationAvailable();
        RefreshDoneAvailability();
        GenerateButton.IsEnabled = !analysisRunning && !generationRunning &&
            !HasPublishedOptimizationZip() && ready;
        if (!analysisRunning && !generationRunning && !HasPublishedOptimizationZip())
            SetResultsConsoleStatus(ready
                ? "CET + REDscript analysis ready. GENERATE creates one ZIP containing all optimizations."
                : NoNewOptimizationStatus());
    }

    private bool ClosedOrClosing => !IsLoaded && initialized;

    public MainWindow()
    {
        InitializeComponent();
        HotscenesEnglishOnlyCheckBox.Checked += (_, _) =>
        {
            hotscenesOptInRefused = false;
            RefreshGenerateEligibility();
        };
        HotscenesEnglishOnlyCheckBox.Unchecked += (_, _) =>
        {
            hotscenesOptInRefused = false;
            RefreshGenerateEligibility();
        };
        originalConsoleMargin = ConsoleTextHost.Margin;
        originalResultsConsoleMargin = ResultsConsoleScroll.Margin;
        CapturePage.SizeChanged += (_, _) =>
        {
            UpdateMaximizedCaptureGeometry();
            UpdateExpandedStageButtons();
        };
        // Only the existing bottom-row buttons grow; the card shells, margins
        // and normal/small layout remain authored by the original XAML.
        foreach (var button in new[] { InstallButton, LaunchButton, CollectButton })
        {
            if (button.Parent is Grid cardContent)
                cardContent.SizeChanged += (_, _) => UpdateExpandedStageButton(button);
        }
        initialized = true;
        var version = typeof(MainWindow).Assembly.GetName().Version?.ToString(3)
            ?? throw new InvalidOperationException("GCO assembly version unavailable.");
        Title = $"G's Cyberpunk 2077 Optimizer — v{version}";
        BuildVersionText.Text = $"v{version}";
        glitchTimer.Tick += GlitchTick;
        Deactivated += (_, _) => PausePortrait();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) StartGlitchIfVisible();
            else PausePortrait();
        };
        consoleActivityTimer.Tick += (_, _) => AdvanceConsoleActivity();
        workProgressTimer.Tick += (_, _) =>
            WorkflowProgressBar.Value = progressSmoother.Advance(
                workProgressClock.Elapsed.TotalSeconds);
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
        Mo2PathBox.Text = UserSettings.ReadMo2Setup().InstallationFolder;
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

    // The same aligned owner images serve both pages. Only their signal
    // intensity changes; this effect is never workflow/completion evidence.
    private void SetPortraitStage(PortraitStage stage)
    {
        if (portraitStage != stage)
        {
            portraitStage = stage;
            portraitSignal.SetStage(stage);
        }
        if (stage == PortraitStage.Black)
        {
            PausePortrait();
            RenderPortrait(0);
        }
        else if (stage == PortraitStage.Completed)
        {
            // Only verified completion is permanently full color. No waiting
            // for the next glitch tick after ZIP publication / no-op DONE.
            PausePortrait();
            RenderPortrait(1);
        }
        else StartGlitchIfVisible();
    }

    private void RenderPortrait(double color)
    {
        // Explicit endpoints prevent color leaking through the black PNG's
        // transparent perimeter. Interior weak pulses use the original pair.
        if (VColorImage.Opacity != color) VColorImage.Opacity = color;
        if (VBlackImage.Opacity != 1 - color) VBlackImage.Opacity = 1 - color;
    }

    private void PausePortrait()
    {
        glitchTimer.Stop();
        portraitClock.Reset();
    }

    private void StartGlitchIfVisible()
    {
        if (WpfVerificationMode || !IsActive || !IsVisible ||
            WindowState == System.Windows.WindowState.Minimized ||
            VBlackImage.Source is null || VColorImage.Source is null ||
            !portraitSignal.NeedsTicks || glitchTimer.IsEnabled) return;
        portraitClock.Restart();
        glitchTimer.Interval = TimeSpan.FromMilliseconds(portraitSignal.NextTickMilliseconds);
        glitchTimer.Start();
    }

    private void GlitchTick(object? sender, EventArgs e)
    {
        if (WpfVerificationMode || !IsActive || !IsVisible ||
            WindowState == System.Windows.WindowState.Minimized)
        {
            PausePortrait();
            return;
        }
        double seconds = Math.Min(0.35, portraitClock.Elapsed.TotalSeconds);
        portraitClock.Restart();
        RenderPortrait(portraitSignal.Advance(seconds));
        if (!portraitSignal.NeedsTicks) PausePortrait();
        else glitchTimer.Interval =
            TimeSpan.FromMilliseconds(portraitSignal.NextTickMilliseconds);
    }

    // Variable-length pulses and pauses, not a uniform alternating blink.
    // Interpolate the requested density without resetting an in-flight burst.
    private sealed class PortraitSignal
    {
        private readonly Random random = new();
        private readonly Queue<(double Color, double Seconds)> pulses = new();
        private double presence;
        private double from;
        private double target;
        private double transitionElapsed;
        private double transitionDuration = 1;
        private double color;
        private double remaining;
        private double restAfterBurst;
        private double burstMajority;
        private double burstPresence;
        private bool inBurst;
        private bool stable;
        public bool NeedsTicks => target > 0 && !stable;
        public double NextTickMilliseconds =>
            transitionElapsed < transitionDuration ? 33 :
            Math.Clamp(remaining * 1000, 20, 250);

        private double Range(double min, double max) => min + random.NextDouble() * (max - min);

        public void SetStage(PortraitStage stage)
        {
            if (stage == PortraitStage.Completed)
            {
                // DONE always settles instantly. Generating is deliberately
                // nonterminal and continues to glitch until this transition.
                pulses.Clear();
                presence = from = target = color = 1;
                remaining = restAfterBurst = 0;
                inBurst = false;
                stable = true;
                transitionElapsed = transitionDuration = 1;
                return;
            }

            double next = stage switch
            {
                PortraitStage.Installed => .025,
                PortraitStage.Capturing => .055,
                PortraitStage.Collecting => .16,
                PortraitStage.Results => .75,
                PortraitStage.Analyzing => .90,
                PortraitStage.Generating => .985,
                PortraitStage.Completed => 1,
                _ => 0
            };
            if (next == target) return;
            if (next == 0)
            {
                pulses.Clear();
                presence = from = target = color = remaining = 0;
                inBurst = stable = false;
                transitionElapsed = transitionDuration;
                return;
            }
            from = presence;
            target = next;
            stable = false;
            transitionElapsed = 0;
            transitionDuration = stage switch
            {
                PortraitStage.Generating => 1.2,
                PortraitStage.Analyzing => 1.6,
                PortraitStage.Results => 2.4,
                _ => 2.8
            };
            // Keep an ongoing cluster intact, but do not wait through the
            // previous profile's entire quiet hold before the new one emerges.
            if (!inBurst)
            {
                double firstWait = stage switch
                {
                    PortraitStage.Installed => Range(12, 25),
                    PortraitStage.Capturing => Range(7, 15),
                    PortraitStage.Collecting => Range(3, 7),
                    _ => Range(.4, 1.2)
                };
                remaining = remaining <= 0 ? firstWait :
                    Math.Min(remaining, firstWait);
            }
        }

        public double Advance(double seconds)
        {
            if (!NeedsTicks) return color;
            transitionElapsed = Math.Min(transitionDuration, transitionElapsed + seconds);
            double t = transitionElapsed / transitionDuration;
            double blend = t * t * (3 - 2 * t);
            presence = from + (target - from) * blend;
            if (target == 1 && t >= 1)
            {
                pulses.Clear();
                inBurst = false;
                stable = true;
                color = 1;
                return color;
            }

            remaining -= seconds;
            if (remaining <= 0)
            {
                if (pulses.Count != 0)
                    (color, remaining) = pulses.Dequeue();
                else if (inBurst)
                {
                    inBurst = false;
                    color = burstMajority;
                    // Never inherit a long quiet period from a much less
                    // active stage after CONTINUE or another state change.
                    remaining = Math.Abs(presence - burstPresence) > .10
                        ? Math.Min(restAfterBurst, Range(.45, 1.1))
                        : restAfterBurst;
                }
                else
                    BeginBurst();
            }
            // Only the terminal ramp settles the last interruptions away;
            // all earlier state transitions change frequency/duty, not fade.
            if (target == 1)
            {
                double settle = Math.Clamp((t - .5) * 2, 0, 1);
                return color + (1 - color) * settle * settle;
            }
            return color;
        }

        private void BeginBurst()
        {
            double p = Math.Clamp(presence, .005, .975);
            burstPresence = p;
            burstMajority = p >= .5 ? 1 : 0;
            if (burstMajority == 0)
            {
                // Install/Start/Collect must remain black-dominant. Quick
                // irregular flicker clusters only: never hold the full image
                // for seconds during any of the first three stages.
                BeginEarlyFlickers(p);
                return;
            }

            // GENERATING stays nearly fully colored, with one very brief
            // irregular black signal drop every ~13-27 seconds. Only DONE
            // locks the terminal full-color image without further ticks.
            if (target >= .98 && target < 1)
            {
                pulses.Enqueue((random.NextDouble() < .30
                    ? Range(.20, .45) : 0, Range(.05, .11)));
                restAfterBurst = Range(13, 27);
                inBurst = true;
                (color, remaining) = pulses.Dequeue();
                return;
            }

            // On Page 2 the full color image remains dominant, interrupted
            // by uneven black glitches. Analyze remains almost stable.
            double minority = 1 - p;
            int count = random.Next(1, 6);
            double minoritySeconds = 0;
            double gapSeconds = 0;
            int longBlackIndex = target == .75 && p >= .62 &&
                random.NextDouble() < .16 ? random.Next(count) : -1;
            for (int i = 0; i < count; ++i)
            {
                // An occasional 1.5–3.5 second blackout only on Continue,
                // never while analyzing or in the first three stages.
                double duration = i == longBlackIndex
                    ? Range(1.5, 3.5)
                    : random.NextDouble() < .23
                        ? Range(.18, .36) : Range(.045, .14);
                double strength = i == longBlackIndex ? 1 :
                    duration < .14 && random.NextDouble() < .55
                        ? Range(.25, .65) : 1;
                pulses.Enqueue((1 - strength, duration));
                minoritySeconds += duration;
                if (i + 1 < count)
                {
                    double gap = random.NextDouble() < .30 ?
                        Range(.16, .40) : Range(.04, .13);
                    pulses.Enqueue((1, gap));
                    gapSeconds += gap;
                }
            }
            restAfterBurst = Math.Clamp(
                (minoritySeconds * (1 - minority) / minority - gapSeconds) *
                    Range(.72, 1.28), .18, 18);
            inBurst = true;
            (color, remaining) = pulses.Dequeue();
        }

        private void BeginEarlyFlickers(double p)
        {
            // Gradually reduce the quiet intervals from rare Install flashes
            // to more frequent Collect glitches, without introducing slow
            // full-image holds. All individual flashes stay under 110 ms.
            double captureMix = Math.Clamp((p - .025) / .030, 0, 1);
            double collectMix = Math.Clamp((p - .055) / .105, 0, 1);
            double minQuiet = 23 - 10 * captureMix - 9 * collectMix;
            double maxQuiet = 47 - 19 * captureMix - 19 * collectMix;
            double minFlash = .028 + .009 * captureMix + .006 * collectMix;
            double maxFlash = .065 + .020 * captureMix + .025 * collectMix;
            int count = random.Next(2, 6);
            for (int i = 0; i < count; ++i)
            {
                // Rapid pair/triplet, an occasionally weak flicker, uneven
                // gaps, then a long black rest. Not a repeated blink loop.
                double strength = random.NextDouble() < .30
                    ? Range(.20, .55) : 1;
                pulses.Enqueue((strength, Range(minFlash, maxFlash)));
                if (i + 1 < count)
                {
                    double gap = random.NextDouble() < .25
                        ? Range(.10, .22) : Range(.025, .09);
                    pulses.Enqueue((0, gap));
                }
            }
            restAfterBurst = Range(minQuiet, maxQuiet);
            // During CONTINUE the emerging color-dominant signal must not
            // inherit a full early-stage black interval.
            if (target >= .75)
                restAfterBurst = Math.Min(restAfterBurst, Range(.35, .85));
            inBurst = true;
            (color, remaining) = pulses.Dequeue();
        }
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
        UpdateMaximizedCaptureGeometry();
        UpdateExpandedStageButtons();
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
        UpdateMaximizedCaptureGeometry();
        UpdateExpandedStageButtons();
        if (WindowState == System.Windows.WindowState.Normal &&
            maximizedWorkflowCards is null)
            InvalidateSceneNeon();
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



    // Keep the approved 38-DIP stage row bit-for-bit at default/smaller widths.
    // Above 1280 DIPs, the card's fixed-height row used to make a wide button
    // while its 760:112 SVG and hover both stopped at ~258 DIPs. Give the
    // EXISTING bottom row enough height to scale the entire author SVG instead
    // of independently stretching its hover or moving the card.
    private const double StageBaseHeight = 38.0;
    private const double StageSvgWidth = 760.0;
    private const double StageSvgHeight = 112.0;

    private void UpdateExpandedStageButtons()
    {
        UpdateExpandedStageButton(InstallButton);
        UpdateExpandedStageButton(LaunchButton);
        UpdateExpandedStageButton(CollectButton);
    }

    private void UpdateExpandedStageButton(System.Windows.Controls.Button button)
    {
        if (button.Parent is not Grid content || content.RowDefinitions.Count != 4 ||
            content.ActualWidth <= 0 || content.ActualHeight <= 0)
            return;

        var bottom = content.RowDefinitions[3];
        var desiredHeight = StageBaseHeight;
        var maxWidth = double.PositiveInfinity;
        if (RootLayout.ActualWidth > 1280.5)
        {
            // Preserve room for the existing emblem, heading and instructions.
            // At very wide resolutions, constrain the BUTTON together with its
            // SVG/hover instead of allowing the aspect ratio to break again.
            var availableHeight = Math.Max(StageBaseHeight,
                content.ActualHeight - content.RowDefinitions[0].ActualHeight
                - content.RowDefinitions[1].ActualHeight - 44.0);
            var naturalHeight = Math.Max(StageBaseHeight,
                content.ActualWidth * StageSvgHeight / StageSvgWidth);
            desiredHeight = Math.Min(naturalHeight, availableHeight);
            if (naturalHeight > availableHeight + 0.1)
                maxWidth = desiredHeight * StageSvgWidth / StageSvgHeight;
        }

        if (button.MaxWidth != maxWidth)
            button.MaxWidth = maxWidth;
        if (bottom.Height.GridUnitType != GridUnitType.Pixel ||
            Math.Abs(bottom.Height.Value - desiredHeight) > 0.1)
            bottom.Height = new GridLength(desiredHeight);
    }

    // The four original Capture sections keep their exact XAML parent and
    // ordering in both window states. Only Height and Margin local values are
    // different while maximized: no reparenting, spacer controls or Viewboxes.
    internal void ApplyMaximizedCaptureLayout()
    {
        if (WindowState == System.Windows.WindowState.Maximized)
        {
            if (maximizedWorkflowCards is not null)
            {
                UpdateMaximizedCaptureGeometry();
                return;
            }

            if (CapturePage.Content is not StackPanel baseline ||
                baseline.Children.Count != 4 ||
                !ReferenceEquals(baseline.Children[0], HeroSection) ||
                baseline.Children[1] is not StackPanel gameRootSection ||
                !ReferenceEquals(baseline.Children[2], CaptureWorkflowHeading) ||
                baseline.Children[3] is not Grid cards ||
                !double.IsFinite(cards.Height) ||
                cards.Children.Count != 3 ||
                cards.Children.Cast<UIElement>().Any(child => child is not Grid))
                return;

            normalHeroMargin = HeroSection.Margin;
            normalGameRootMargin = gameRootSection.Margin;
            normalHeadingMargin = CaptureWorkflowHeading.Margin;
            normalWorkflowHeight = cards.Height;
            normalWorkflowMinHeight = cards.MinHeight;
            normalGameRootPanelHeight = GameRootPanel.Height;
            maximizedCardColumns = cards.Children.Cast<Grid>().ToArray();
            normalCardMargins = maximizedCardColumns.Select(card => card.Margin).ToArray();
            normalCaptureScrollOffset = CapturePage.VerticalOffset;
            // DesiredSize describes the unmodified normal sections. Never
            // recalculate the baseline from already-expanded measurements.
            normalCaptureContentHeight = baseline.DesiredSize.Height;
            maximizedGameRootSection = gameRootSection;
            maximizedWorkflowCards = cards;
            UpdateMaximizedCaptureGeometry();
            return;
        }

        if (maximizedWorkflowCards is null || maximizedGameRootSection is null ||
            maximizedCardColumns is null || normalCardMargins is null)
            return;

        for (var i = 0; i < maximizedCardColumns.Length; i++)
            maximizedCardColumns[i].Margin = normalCardMargins[i];
        GameRootPanel.Height = normalGameRootPanelHeight;
        HeroSection.Margin = normalHeroMargin;
        maximizedGameRootSection.Margin = normalGameRootMargin;
        CaptureWorkflowHeading.Margin = normalHeadingMargin;
        maximizedWorkflowCards.Height = normalWorkflowHeight;
        maximizedWorkflowCards.MinHeight = normalWorkflowMinHeight;
        ConsoleTextHost.Margin = originalConsoleMargin;
        ResultsConsoleScroll.Margin = originalResultsConsoleMargin;
        var scrollOffset = normalCaptureScrollOffset;
        maximizedGameRootSection = null;
        maximizedWorkflowCards = null;
        maximizedCardColumns = null;
        normalCardMargins = null;
        InvalidateSceneNeon();
        CapturePage.ScrollToVerticalOffset(scrollOffset);
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (WindowState == System.Windows.WindowState.Normal &&
                maximizedWorkflowCards is null)
                CapturePage.ScrollToVerticalOffset(scrollOffset);
        }));
    }

    private void InvalidateSceneNeon()
    {
        GameRootOutline.InvalidateVisual();
        StepOneOutline.InvalidateVisual();
        StepTwoOutline.InvalidateVisual();
        StepThreeOutline.InvalidateVisual();
        foreach (UIElement child in RootLayout.Children)
        {
            if (child is GTotalOptimizer.App.Visuals.ContentChassisOutline frame)
                frame.InvalidateVisual();
        }
    }

    private void UpdateMaximizedCaptureGeometry()
    {
        if (WindowState != System.Windows.WindowState.Maximized) return;

        // Both console text origins must move by the SAME portrait clearance
        // in fullscreen. Shift only the Analyze console's scroll viewport,
        // leaving its DONE button and the rest of Page 2 untouched.
        var overhang = VPortraitHost.Width - ArtworkColumn.Width.Value;
        var shift = Math.Max(0, overhang - ExpandedConsolePortraitClearance);
        var resultsMargin = new Thickness(originalResultsConsoleMargin.Left + shift,
            originalResultsConsoleMargin.Top, originalResultsConsoleMargin.Right,
            originalResultsConsoleMargin.Bottom);
        if (ResultsConsoleScroll.Margin != resultsMargin)
            ResultsConsoleScroll.Margin = resultsMargin;

        if (maximizedGameRootSection is null || maximizedWorkflowCards is null ||
            CapturePage.ActualHeight <= 0)
            return;

        // The original 228-DIP cards should not fill the entire window.
        // Grow them modestly; spend most of the surplus on section separation,
        // leaving the workflow title close to its own cards. The Game Root
        // heading and its box always move as a single original StackPanel.
        var surplus = Math.Max(0, CapturePage.ActualHeight - normalCaptureContentHeight);
        var rootGrowth = Math.Min(ExpandedGameRootHeight, surplus);
        var remaining = Math.Max(0, surplus - rootGrowth);
        static Thickness WithBottom(Thickness original, double added) =>
            new(original.Left, original.Top, original.Right, original.Bottom + added);

        var hero = WithBottom(normalHeroMargin, remaining * ExpandedHeroGapRatio);
        var root = WithBottom(normalGameRootMargin, remaining * ExpandedRootGapRatio);
        var heading = new Thickness(normalHeadingMargin.Left,
            normalHeadingMargin.Top, normalHeadingMargin.Right,
            Math.Min(normalHeadingMargin.Bottom, ExpandedWorkflowHeadingBottom));
        if (HeroSection.Margin != hero) HeroSection.Margin = hero;
        if (maximizedGameRootSection.Margin != root) maximizedGameRootSection.Margin = root;
        if (CaptureWorkflowHeading.Margin != heading) CaptureWorkflowHeading.Margin = heading;

        var rootHeight = normalGameRootPanelHeight + rootGrowth;
        if (Math.Abs(GameRootPanel.Height - rootHeight) > .1)
            GameRootPanel.Height = rootHeight;
        var cardGrowth = Math.Min(ExpandedCardMaxGrowth, remaining * ExpandedCardGrowthRatio);
        var cardHeight = normalWorkflowHeight + cardGrowth;
        if (Math.Abs(maximizedWorkflowCards.Height - cardHeight) > .1)
            maximizedWorkflowCards.Height = cardHeight;

        // Make each of the three equal-column cards narrower without changing
        // their inner controls: wider inter-card gaps and more outer breathing room.
        if (maximizedCardColumns is not null && normalCardMargins is not null)
            for (var i = 0; i < maximizedCardColumns.Length; i++)
            {
                var margin = normalCardMargins[i];
                var expanded = new Thickness(margin.Left + ExpandedCardSideInset,
                    margin.Top, margin.Right + ExpandedCardSideInset, margin.Bottom);
                if (maximizedCardColumns[i].Margin != expanded)
                    maximizedCardColumns[i].Margin = expanded;
            }

        // Preserve the existing Capture clearance but apply its same
        // horizontal shift to Analyze above, avoiding per-page visual drift.
        var console = new Thickness(originalConsoleMargin.Left + shift,
            originalConsoleMargin.Top - ExpandedConsoleRaise, originalConsoleMargin.Right,
            originalConsoleMargin.Bottom + ExpandedConsoleRaise);
        if (ConsoleTextHost.Margin != console)
            ConsoleTextHost.Margin = console;
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
        ApplyMaximizedCaptureLayout();
        QueueHeaderTabAlignment();
        if (WpfVerificationMode) return; // Visual-only native CI window.
        // A fresh WPF child reopened by the native tray should be active,
        // not remain behind ordinary applications. Never use Topmost.
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsVisible && WindowState != System.Windows.WindowState.Minimized)
            {
                Activate();
                Focus();
            }
        }));
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
        // Direct recovery remains authoritative when present. Otherwise resume
        // interrupted MO2 recovery even if its companion is no longer running.
        if (!Workflow.HasRecoveryJournal && !Workflow.RestoreActionAvailable(GamePathBox.Text))
        {
            bool needsMo2Restore;
            try { needsMo2Restore = Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession() ||
                Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore(); }
            catch { needsMo2Restore = true; }
            if (needsMo2Restore)
            {
                mo2Mode = true;
                mo2RecoveryBlocked = true;
                GameRootContents.Visibility = Visibility.Collapsed;
                Mo2SourceContents.Visibility = Visibility.Visible;
                GameRootContents.Children.Remove(FrameworkChecksRow);
                Mo2FrameworkSlot.Children.Add(FrameworkChecksRow);
                GameSourceHeading.Text = "MOD ORGANIZER 2";
                SetFrameworkStatuses(null);
                UpdatePathLock();
                UpdateMo2TestButtons();
                _ = RefreshMo2SourceAsync();
                return;
            }
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
            SetPortraitStage(PortraitStage.Results);
            CapturePage.Visibility = Visibility.Collapsed;
            CaptureFooterHost.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            BackToCaptureButton.Visibility = Visibility.Visible;
            ClearWorkProgress();
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

    private void ArmProcessStartGuard()
    {
        var generation = ++launchProcessGuardGeneration;
        launchProcessGuardUntilUtc = DateTime.UtcNow.AddSeconds(12);
        _ = ExpireProcessStartGuardAsync(generation);
    }

    private async Task ExpireProcessStartGuardAsync(int generation)
    {
        await Task.Delay(TimeSpan.FromSeconds(12));
        if (generation != launchProcessGuardGeneration ||
            Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        // A deadline is not proof of failure: recheck the real process first.
        if (GameRunning())
        {
            if (observedGamePid is null)
            {
                var games = Process.GetProcessesByName("Cyberpunk2077");
                try
                {
                    if (games.Length != 0) GameStarted(games[0].Id);
                }
                finally { foreach (var p in games) p.Dispose(); }
            }
            launchProcessGuardUntilUtc = DateTime.MinValue;
            return;
        }
        // No Cyberpunk PID was ever observed; do not keep START locked merely
        // because a previous attempt launched PresentMon successfully.
        launchProcessGuardUntilUtc = DateTime.MinValue;
        gcoLaunchedGamePid = null;
        if (IsLoaded && !busy) await RefreshSourceAsync();
    }

    // Process.Start returning successfully finishes the START UI workflow.
    // The native tray remains resident, while Cyberpunk may still be loading
    // or may crash early. Neither condition should keep the managed UI open.
    // Also exercised by the packaged Windows WPF smoke with no game process.
    internal bool HibernateAfterLaunch()
    {
        if (busy || !IsLoaded || closingInProgress || closureCleanupVerified)
            return false;
        captureHibernationClose = true;
        Close();
        // WPF IsLoaded is sticky after a window has been shown, even when
        // Close() has disposed it. Visibility is the correct closure check.
        return !IsVisible;
    }

    // Arm the native tray with this attempt's existing 128-bit identity
    // BEFORE unloading WPF. No new elevation or resident .NET process.
    private bool ArmNativeGameReopen(string game)
    {
        try
        {
            var id = Workflow.ReadRecoverySessionId(game)
                ?? throw new InvalidOperationException("Capture identity unavailable.");
            var launcher = Path.Combine(packageRoot, PortablePackagePaths.RootLauncherName);
            using var control = Process.Start(new ProcessStartInfo(launcher)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = packageRoot,
                ArgumentList = { "--watch-game", id }
            }) ?? throw new InvalidOperationException("Native game observer unavailable.");
            if (!control.WaitForExit(2500) || control.ExitCode != 0)
                throw new InvalidOperationException("Native tray rejected the game watcher.");
            return true;
        }
        catch (Exception ex)
        {
            // Keep WPF usable if the native tray has unexpectedly vanished.
            // Never claim automatic reopening when no watcher was armed.
            Trace.TraceWarning("GCO native game-exit watcher: " + ex);
            PinStatus("Cyberpunk started. Automatic return unavailable; GCO remains open.");
            return false;
        }
    }

    private void GameStarted(int pid)
    {
        launchProcessGuardUntilUtc = DateTime.MinValue;
        ++launchProcessGuardGeneration;
        if (observedGamePid == pid) return;
        if (observedGamePid is not null) return; // existing game is tracked
        observedGamePid = pid;
        if (!gameProcesses.Track(pid))
        {
            // The process may have failed at startup before we attached.
            GameExited(pid);
            return;
        }
        if (gcoLaunchInProgress || gcoLaunchedGamePid == pid)
        {
            // This launch already provisioned its capture before Process.Start.
            // Never run the external-launch reset or postpone WPF unload.
            gcoLaunchedGamePid = null;
        }
        else
        {
            pendingExternalGameStart = true;
            pendingExternalGamePid = pid;
            QueueGameReconciliation();
        }
    }

    private void GameExited(int pid)
    {
        if (observedGamePid != pid) return;
        gameProcesses.Untrack(pid);
        observedGamePid = null;
        gcoLaunchedGamePid = null;
        gcoLaunchInProgress = false;
        launchProcessGuardUntilUtc = DateTime.MinValue;
        ++launchProcessGuardGeneration;
        // Native elevated observer alone stops PresentMon on game exit.
        pendingGameExitRefresh = true;
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
            closureCleanupVerified || busy || gameStateRefreshInFlight) return;
        if (mo2Mode)
        {
            // The experimental source mode never starts Direct capture or ETW.
            pendingExternalGameStart = pendingGameExitRefresh = false;
            pendingExternalGamePid = null;
            UpdatePathLock();
            UpdateMo2TestButtons();
            return;
        }
        if (!inspection.IsValidGame) return;
        gameStateRefreshInFlight = true;
        try
        {
            // External launches still provision fresh attempts; process-exit
            // notifications only refresh UI and never signal ETW shutdown.
            if (pendingExternalGameStart)
            {
                var pid = pendingExternalGamePid;
                pendingExternalGameStart = false;
                pendingExternalGamePid = null;
                if (pid == observedGamePid && GameRunning())
                {
                    pendingGameExitRefresh = false;
                    await HandleExternalGameLaunchAsync();
                }
            }

            // Consume UI-only pending refresh; no PresentMon stop requested.
            pendingGameExitRefresh = false;
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
            if (pendingExternalGameStart || pendingGameExitRefresh)
                QueueGameReconciliation();
        }
    }

    private void Window_Activated(object sender, EventArgs e)
    {
        if (WpfVerificationMode) return;
        if (!initialized ||
            WindowState == System.Windows.WindowState.Minimized) return;
        StartGlitchIfVisible();
        // Activation is presentation-only. Initial load, user REFRESH,
        // process transitions and workflow actions already re-inspect sources.
        // Repeating owner CLIs and package hashing on Alt-Tab was redundant.
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        ApplyMaximizedCaptureLayout();
        if (WpfVerificationMode) return;
        if (WindowState == System.Windows.WindowState.Minimized)
        {
            PausePortrait();
            return;
        }
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (IsLoaded && IsVisible &&
                WindowState != System.Windows.WindowState.Minimized)
            {
                Activate();
                Focus();
                StartGlitchIfVisible();
            }
        }));
    }

    // Window X fully exits GCO; only the explicit capture-hibernation close
    // keeps the native tray. Neither action mutates owned results or journals.
    private void Window_Closing(object? sender,
        System.ComponentModel.CancelEventArgs e)
    {
        if (WpfVerificationMode || !initialized) return;
        if (busy)
        {
            e.Cancel = true;
            PinStatus("Finish the active profiler operation before closing GCO.");
            return;
        }
        closureCleanupVerified = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        gameStateTimer.Stop();
        gameProcesses.Dispose();
        PausePortrait();
        consoleActivityTimer.Stop();
        workProgressTimer.Stop();
        workProgressClock.Stop();
        progressSmoother.Stop();
        consoleActivities.Clear();
        refreshCancellation?.Cancel();
        refreshCancellation?.Dispose();
        analysisCancellation?.Cancel();
        analysisCancellation?.Dispose();
        if (!WpfVerificationMode && !captureHibernationClose)
        {
            // User X/Alt+F4 terminates the package-bound native host as well.
            // The existing capture and owner restore state are never modified.
            try
            {
                var launcher = Path.Combine(packageRoot, PortablePackagePaths.RootLauncherName);
                if (File.Exists(launcher))
                {
                    using var control = Process.Start(new ProcessStartInfo(launcher, "--ui-closed")
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WorkingDirectory = packageRoot
                    });
                }
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("GCO tray shutdown after window close: " + ex);
            }
        }
    }

    private async void GamePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (!initialized || pathUpdate || busy || mo2Mode) return;
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
        if (ResultsPage.Visibility == Visibility.Visible)
        {
            // On Page 2 REFRESH means repaint the existing UI, not restore,
            // re-analyze, regenerate or advance the V/progress/Done state.
            RefreshResultsPresentationOnly();
            return;
        }
        ClearStatusPin();
        await RefreshSourceAsync();
    }

    private void RefreshResultsPresentationOnly()
    {
        // No owner inspection, file reads, session updates, status overrides,
        // progress activities, action enablement or portrait stage changes.
        // Preserve the current in-memory presentation, including a currently
        // running Analyze/Generate operation, while invalidating its layout.
        ResultsPage.InvalidateMeasure();
        ResultsPage.InvalidateArrange();
        ResultsPage.InvalidateVisual();
        RootLayout.InvalidateVisual();
        ResultsPage.UpdateLayout();
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

    private bool CanSwitchSource() =>
        !busy && !mo2StartupRunning && mo2Prepared is null && !mo2RecoveryBlocked && !gameStateRefreshInFlight && !Workflow.HasRecoveryJournal &&
        !Workflow.RestoreActionAvailable(GamePathBox.Text) && !GameRunning();

    private async void DirectSource_Click(object sender, RoutedEventArgs e)
    {
        if (!initialized || !mo2Mode || !CanSwitchSource() || mo2RefreshRunning || mo2VerifyRequired) return;
        mo2Mode = false;
        mo2Connected = null;
        pendingExternalGameStart = pendingGameExitRefresh = false;
        pendingExternalGamePid = null;
        GameRootContents.Visibility = Visibility.Visible;
        Mo2SourceContents.Visibility = Visibility.Collapsed;
        Mo2FrameworkSlot.Children.Remove(FrameworkChecksRow);
        GameRootContents.Children.Insert(2, FrameworkChecksRow);
        SetFrameworkStatuses(null);
        GameSourceHeading.Text = "GAME ROOT / CYBERPUNK 2077";
        UpdatePathLock();
        ClearStatusPin();
        await RefreshSourceAsync();
    }

    private async void Mo2Source_Click(object sender, RoutedEventArgs e)
    {
        if (!initialized || mo2Mode || !CanSwitchSource()) return;
        // Revoke an in-flight Direct refresh before showing MO2 controls.
        refreshCancellation?.Cancel();
        mo2Mode = true;
        inspection = GameInspector.Inspect(null);
        liveState = null;
        savedCapture = null;
        pendingExternalGameStart = pendingGameExitRefresh = false;
        pendingExternalGamePid = null;
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = false;
        SetPortraitStage(PortraitStage.Black);
        GameRootContents.Visibility = Visibility.Collapsed;
        Mo2SourceContents.Visibility = Visibility.Visible;
        GameRootContents.Children.Remove(FrameworkChecksRow);
        Mo2FrameworkSlot.Children.Add(FrameworkChecksRow);
        SetFrameworkStatuses(null);
        GameSourceHeading.Text = "MOD ORGANIZER 2";
        UpdatePathLock();
        ClearStatusPin();
        await RefreshMo2SourceAsync();
    }

    private async void Mo2Browse_Click(object sender, RoutedEventArgs e)
    {
        if (!mo2Mode || !CanSwitchSource() || mo2RefreshRunning) return;
        using var browse = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select the MO2 folder containing ModOrganizer.exe",
            ShowNewFolderButton = false,
            SelectedPath = Directory.Exists(Mo2PathBox.Text) ? Mo2PathBox.Text : ""
        };
        if (browse.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        mo2SelectionUpdating = true;
        try
        {
            Mo2PathBox.Text = browse.SelectedPath;
            Mo2InstanceBox.ItemsSource = null;
            Mo2ProfileBox.ItemsSource = null;
            UserSettings.WriteMo2Setup(browse.SelectedPath, "", "");
        }
        finally { mo2SelectionUpdating = false; }
        await RefreshMo2SourceAsync();
    }

    private async void Mo2Selection_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || !mo2Mode || mo2SelectionUpdating || mo2RefreshRunning) return;
        if (ReferenceEquals(sender, Mo2InstanceBox))
        {
            mo2SelectionUpdating = true;
            try { Mo2ProfileBox.ItemsSource = null; }
            finally { mo2SelectionUpdating = false; }
        }
        UserSettings.WriteMo2Setup(Mo2PathBox.Text,
            Mo2InstanceBox.SelectedItem as string ?? "",
            Mo2ProfileBox.SelectedItem as string ?? "");
        await RefreshMo2SourceAsync();
    }

    private async Task RefreshMo2SourceAsync()
    {
        if (!initialized || !mo2Mode || mo2RefreshRunning || mo2StartupRunning) return;
        mo2RefreshRunning = true;
        ClearStatusPin();
        SetStatus("Verifying profile...");
        Mo2StatusText.Text = "Checking selected profile";
        mo2Connected = null;
        mo2SelectedSource = null;
        mo2Framework = null;
        SetFrameworkStatuses(null);
        UpdatePathLock();
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = false;
        try
        {
            var saved = UserSettings.ReadMo2Setup();
            var folder = Mo2PathBox.Text;
            var location = Mo2InstallLocator.Inspect(folder,
                Mo2InstanceBox.SelectedItem as string ?? saved.InstanceFolder);
            mo2SelectionUpdating = true;
            try
            {
                Mo2InstanceBox.ItemsSource = location.InstanceChoices;
                Mo2InstanceBox.SelectedItem = location.InstanceFolder;
                if (!location.HasConfiguration || location.InstanceFolder is null)
                {
                    Mo2ProfileBox.ItemsSource = null;
                    Mo2GameText.Text = "Game: not confirmed";
                    Mo2StatusText.Text = location.Detail;
                    SetStatus(location.Detail);
                    return;
                }
                var instance = Mo2SourceInspector.Discover(location.InstanceFolder);
                var profile = Mo2ProfileBox.SelectedItem as string ?? saved.Profile;
                Mo2ProfileBox.ItemsSource = instance.Profiles;
                Mo2ProfileBox.SelectedItem = instance.Profiles.FirstOrDefault(
                    p => p.Equals(profile, StringComparison.OrdinalIgnoreCase))
                    ?? instance.Profiles.FirstOrDefault(p => p.Equals(instance.PreferredProfile,
                        StringComparison.OrdinalIgnoreCase));
                UserSettings.WriteMo2Setup(folder, location.InstanceFolder,
                    Mo2ProfileBox.SelectedItem as string ?? "");
                if (!instance.Recognized)
                {
                    Mo2GameText.Text = "Game: not confirmed";
                    Mo2StatusText.Text = instance.Detail;
                    SetStatus(instance.Detail);
                    return;
                }
                if (Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore())
                {
                    Mo2StatusText.Text = "Click VERIFY";
                    SetStatus("Click VERIFY before installing profilers.");
                    return;
                }
                var connected = Mo2CompanionDiscovery.Inspect(folder, instance);
                if (!connected.Connected)
                {
                    Mo2GameText.Text = "Game: awaiting MO2 companion";
                    Mo2StatusText.Text = connected.Detail;
                    SetStatus("Click VERIFY to prepare Mod Organizer 2.");
                    return;
                }
                Mo2GameText.Text = "Game: " + connected.GameDirectory;
                var reply = await Mo2CompanionCommandClient.ProbeAsync(connected);
                if (!reply.Ok) throw new InvalidOperationException(reply.Detail);
                mo2Connected = connected;
                if (!Mo2CompanionCommandClient.ElevationVerified(connected, reply))
                {
                    Mo2StatusText.Text = "Click VERIFY to prepare MO2";
                    SetStatus("Click VERIFY to start MO2 with the required permissions.");
                    return;
                }
                var selected = Mo2ProfileBox.SelectedItem as string;
                if (string.IsNullOrWhiteSpace(selected))
                {
                    Mo2StatusText.Text = "Bridge authenticated. Select a profile.";
                    SetStatus("MO2 bridge authenticated. Select a profile from the dropdown.");
                    return;
                }
                var result = await Task.Run(() => Mo2ProfileSelectionService.Inspect(folder,
                    location.InstanceFolder, selected, connected.GameDirectory));
                var stack = result.CandidateSource;
                if (result.Recognized && stack is not null)
                {
                    mo2SelectedSource = result;
                    mo2Framework = await Task.Run(() =>
                        FrameworkPreflight.InspectMo2(result.GameDirectory, stack));
                    SetFrameworkStatuses(mo2Framework);
                }
                Mo2StatusText.Text = result.Recognized && stack is not null
                    ? $"Bridge OK | {stack.EnabledPackages} enabled mods | {stack.CetModCount} CET mods | {stack.RedscriptModCount} REDscript mods"
                    : result.Detail;
                SetStatus(result.Recognized
                    ? (mo2Framework?.Ready == true ? $"Profile ready: {selected}."
                        : mo2Framework?.Detail ?? "Framework compatibility unverified.")
                    : result.Detail);
            }
            finally { mo2SelectionUpdating = false; }
        }
        catch (Exception ex)
        {
            mo2Connected = null;
            mo2Framework = null;
            SetFrameworkStatuses(null);
            Mo2StatusText.Text = "MO2 verification failed";
            SetStatus("MO2 verification failed: " + ex.Message);
        }
        finally
        {
            mo2RefreshRunning = false;
            UpdatePathLock();
            UpdateMo2TestButtons();
        }
    }

    private void UpdateMo2TestButtons()
    {
        if (!mo2Mode) return;
        bool running = GameRunning();
        try
        {
            mo2RecoveryBlocked = Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession();
            mo2VerifyRequired = Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore();
        }
        catch (Exception ex)
        {
            mo2RecoveryBlocked = true;
            mo2VerifyRequired = true;
            SetStatus("MO2 session journal requires recovery: " + ex.Message);
        }
        UpdatePathLock();
        var restore = mo2Prepared is not null || mo2RecoveryBlocked;
        InstallButton.Content = restore ? "RESTORE PROFILERS" : "INSTALL PROFILERS";
        InstallButton.Style = (Style)FindResource(restore ? "RestoreStageButton" : "StageActionButton");
        InstallButton.IsEnabled = restore ? !busy && !mo2RefreshRunning :
            !busy && !mo2RefreshRunning && !running && !mo2VerifyRequired && mo2SelectedSource is not null &&
            mo2Connected is not null && mo2Framework?.Ready == true;
        LaunchButton.IsEnabled = !busy && !mo2RefreshRunning && !running &&
            mo2Prepared is not null && !mo2LaunchAttempted && !mo2VerifyRequired &&
            mo2Connected is not null && mo2Framework?.Ready == true;
        CollectButton.IsEnabled = ContinueButton.IsEnabled = false;
        if (mo2RecoveryBlocked && mo2Prepared is null)
            SetStatus("Previous profiling session requires recovery. Existing results are preserved.");
    }

    private async Task<Mo2ProfileSelection> InspectMo2TestSelectionAsync()
    {
        if (Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore())
            throw new InvalidOperationException("Click VERIFY before installing profilers.");
        var instancePath = Mo2InstanceBox.SelectedItem as string ??
            throw new InvalidOperationException("Select an MO2 instance.");
        var profile = Mo2ProfileBox.SelectedItem as string ??
            throw new InvalidOperationException("Select an MO2 profile.");
        var folder = Mo2PathBox.Text;
        var instance = Mo2SourceInspector.Discover(instancePath);
        var connected = Mo2CompanionDiscovery.Inspect(folder, instance);
        if (!connected.Connected) throw new InvalidOperationException(connected.Detail);
        var probe = await Mo2CompanionCommandClient.ProbeAsync(connected);
        if (!probe.Ok) throw new InvalidOperationException(probe.Detail);
        if (!Mo2CompanionCommandClient.ElevationVerified(connected, probe))
            throw new InvalidOperationException("Click VERIFY to start MO2 with the required permissions.");
        var selection = await Task.Run(() => Mo2ProfileSelectionService.Inspect(
            folder, instancePath, profile, connected.GameDirectory));
        if (!selection.Recognized || selection.CandidateSource is null)
            throw new InvalidOperationException(selection.Detail);
        mo2Connected = connected;
        mo2Framework = await Task.Run(() => FrameworkPreflight.InspectMo2(
            selection.GameDirectory, selection.CandidateSource!));
        SetFrameworkStatuses(mo2Framework);
        if (!mo2Framework.Ready) throw new InvalidOperationException(mo2Framework.Detail);
        return selection;
    }

    private async Task Mo2InstallOrRestoreTestAsync()
    {
        if (busy || mo2RefreshRunning) return;
        ClearStatusPin();
        if (GameRunning())
        {
            SetStatus("Close Cyberpunk before continuing.");
            return;
        }
        SetBusy(true);
        try
        {
            if (mo2Prepared is not null || mo2RecoveryBlocked ||
                Mo2ProfilerOverlayBuilder.HasUnresolvedTestSession())
            {
                await Task.Run(() => Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions());
                mo2Prepared = null;
                mo2LaunchAttempted = false;
                mo2RecoveryBlocked = false;
                SetPortraitStage(PortraitStage.Black);
                mo2VerifyRequired = Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore();
                if (mo2VerifyRequired)
                {
                    mo2Connected = null;
                    mo2SelectedSource = null;
                    mo2Framework = null;
                    SetFrameworkStatuses(null);
                    PinStatus("Profilers reset. Click VERIFY.");
                }
                else PinStatus("Profilers restored. Original files unchanged.");
            }
            else
            {
                var selection = await InspectMo2TestSelectionAsync();
                mo2Prepared = await Mo2ProfilerOverlayBuilder.PrepareFromBundleAsync(
                    packageRoot, selection.GameDirectory, selection.CandidateSource!,
                    Guid.NewGuid().ToString("N"));
                mo2LaunchAttempted = false;
                SetPortraitStage(PortraitStage.Installed);
                SetStatus("Profilers prepared. Ready to start Cyberpunk.");
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("MO2 profilers: " + ex);
            PinStatus(ex.Message);
        }
        finally { SetBusy(false); UpdateMo2TestButtons(); }
    }

    private async Task Mo2LaunchTestAsync()
    {
        if (busy || mo2RefreshRunning || GameRunning() || mo2Prepared is not { } prepared ||
            mo2LaunchAttempted) return;
        ClearStatusPin();
        SetBusy(true);
        try
        {
            var selection = await InspectMo2TestSelectionAsync();
            Mo2ProfilerOverlayBuilder.VerifyPrepared(prepared, selection.CandidateSource!);
            mo2LaunchAttempted = true;
            var reply = await Mo2CompanionCommandClient.LaunchPreparedTestAsync(
                mo2Connected!, selection, prepared, true);
            SetPortraitStage(PortraitStage.Capturing);
            SetStatus("Cyberpunk launch requested. Use F11 to start/stop profiling.");
            Trace.TraceInformation("MO2 launch acknowledged (PID " + reply.ProcessId + ").");
        }
        catch (Exception ex)
        {
            // Only the durable journal can establish whether dispatch was attempted.
            mo2LaunchAttempted = !Mo2ProfilerOverlayBuilder.IsStillPrepared(prepared);
            Trace.TraceWarning("MO2 launch: " + ex);
            PinStatus(ex.Message);

        }
        finally { SetBusy(false); UpdateMo2TestButtons(); }
    }

    private async void Mo2Bridge_Click(object sender, RoutedEventArgs e)
    {
        if (!mo2Mode || !CanSwitchSource() || mo2RefreshRunning) return;
        ClearStatusPin();
        bool startupSucceeded = false;
        try
        {
            var folder = Mo2PathBox.Text;
            var location = Mo2InstallLocator.Inspect(folder, Mo2InstanceBox.SelectedItem as string);
            if (!location.HasConfiguration || location.InstanceFolder is null)
                throw new InvalidOperationException(location.Detail);
            var instance = Mo2SourceInspector.Discover(location.InstanceFolder);
            if (!instance.Recognized) throw new InvalidOperationException(instance.Detail);
            var existing = Mo2CompanionDiscovery.Inspect(folder, instance);
            // Reuse the correctly elevated live instance for the rest of
            // this session. Never trigger another UAC prompt just to probe.
            if (!Mo2ProfilerOverlayBuilder.RequiresVerificationAfterRestore() &&
                existing.Connected && await Mo2CompanionCommandClient.VerifyElevatedAsync(existing))
            {
                await RefreshMo2SourceAsync();
                return;
            }
            mo2StartupRunning = true;
            SetBusy(true);
            SetStatus("Preparing Mod Organizer 2...");
            await Mo2StartupController.StartAsync(packageRoot, folder, location.InstanceFolder);
            // MO2 creates the authenticated companion heartbeat only after
            // loading its selected instance and plugins.
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed < TimeSpan.FromSeconds(30))
            {
                var connected = Mo2CompanionDiscovery.Inspect(folder, instance);
                if (connected.Connected)
                    break;
                await Task.Delay(500);
            }
            var verified = Mo2CompanionDiscovery.Inspect(folder, instance);
            if (!verified.Connected)
                throw new InvalidOperationException(verified.Detail);
            var probe = await Mo2CompanionCommandClient.ProbeAsync(verified);
            if (!probe.Ok) throw new InvalidOperationException(probe.Detail);
            if (!Mo2CompanionCommandClient.ElevationVerified(verified, probe))
                throw new InvalidOperationException(Mo2ProcessIdentity.InspectElevation(verified.ProcessId).Detail);
            startupSucceeded = true;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("MO2 setup: " + ex);
            PinStatus("MO2 verification failed: " + ex.Message);
        }
        finally
        {
            if (mo2StartupRunning)
            {
                mo2StartupRunning = false;
                SetBusy(false);
                if (startupSucceeded) await RefreshMo2SourceAsync();
                else
                {
                    mo2Connected = null;
                    mo2SelectedSource = null;
                    mo2Framework = null;
                    SetFrameworkStatuses(null);
                    Mo2StatusText.Text = "MO2 setup failed";
                    UpdateMo2TestButtons();
                }
            }
        }
    }

    private void UpdatePathLock()
    {
        // A valid journal pins its game; a corrupt journal must not lock out
        // selection of the original game for owner-managed RESTORE.
        var locked = Workflow.ReadRecoveryGameRoot() is not null;
        GamePathBox.IsReadOnly = locked;
        GamePathBox.IsEnabled = !busy;
        BrowseButton.IsEnabled = !busy && !locked;
        RefreshButton.IsEnabled = !busy && !mo2RefreshRunning;
        var switchAllowed = CanSwitchSource() && !mo2RefreshRunning;
        DirectSourceButton.IsEnabled = mo2Mode && switchAllowed && !mo2VerifyRequired;
        Mo2SourceButton.IsEnabled = !mo2Mode && switchAllowed;
        Mo2BrowseButton.IsEnabled = mo2Mode && switchAllowed;
        Mo2InstanceBox.IsEnabled = Mo2ProfileBox.IsEnabled = Mo2BrowseButton.IsEnabled;
        Mo2BridgeButton.IsEnabled = Mo2BrowseButton.IsEnabled;
        Mo2BridgeButton.Content = "VERIFY";
    }

    private void SetBusy(bool isBusy)
    {
        busy = isBusy;
        UpdatePathLock();
        InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
            ContinueButton.IsEnabled = false;
        if (!isBusy && (pendingExternalGameStart || pendingGameExitRefresh))
            QueueGameReconciliation();
    }

    // The same three-step animation is used by every actual asynchronous
    // operation. It does not imply measured progress or create game files.
    internal long BeginConsoleActivity(string message)
    {
        long id = ++nextConsoleActivityId;
        bool results = ResultsPage.Visibility == Visibility.Visible;
        string previous = results ? ResultsStatusText.Text : StatusText.Text;
        consoleActivities.Add(id, new ConsoleActivity(message, results, previous,
            results ? resultsConsoleRevision : captureConsoleRevision));
        consoleActivityDots = 0;
        consoleActivityTimer.Start();
        AdvanceConsoleActivity();
        return id;
    }

    internal void AdvanceConsoleActivity()
    {
        if (consoleActivities.Count == 0) return;
        var activity = consoleActivities.MaxBy(pair => pair.Key).Value;
        consoleActivityDots = consoleActivityDots % 3 + 1;
        string rendered = activity.Message + new string('.', consoleActivityDots);
        lastActivityRender = activity.Results
            ? rendered : UserFacingConsoleText.Render(rendered);
        if (activity.Results)
            ResultsStatusText.Text = lastActivityRender;
        else
            StatusText.Text = lastActivityRender;
    }

    internal void EndConsoleActivity(long id)
    {
        if (!consoleActivities.Remove(id, out var completed)) return;
        bool sameText = lastActivityRender is not null &&
            (completed.Results ? ResultsStatusText.Text : StatusText.Text) ==
            lastActivityRender;
        if (consoleActivities.Count > 0)
        {
            consoleActivityDots = 0;
            AdvanceConsoleActivity();
            return;
        }
        consoleActivityTimer.Stop();
        lastActivityRender = null;
        // A timer tick may follow a final status while a cleanup await is
        // still pending. Final success/failure messages must win over dots.
        if (completed.Results && resultsConsoleRevision != completed.StatusRevision)
            ResultsStatusText.Text = latestResultsConsoleText;
        else if (!completed.Results &&
                 captureConsoleRevision != completed.StatusRevision)
            StatusText.Text = latestCaptureConsoleText;
        else if (sameText)
        {
            if (completed.Results)
                ResultsStatusText.Text = completed.PreviousText;
            else
                StatusText.Text = completed.PreviousText;
        }
    }

    // Real source milestones remain authoritative. A UI-only estimate eases
    // toward the next checkpoint and is never allowed to indicate completion.
    private enum WorkPhase { None, Collect, Analyze, Generate }
    private WorkPhase activeWorkPhase = WorkPhase.None;
    private long activeWorkToken;

    private long BeginWorkProgress(WorkPhase phase, string label)
    {
        workProgressTimer.Stop();
        activeWorkPhase = phase;
        long token = ++activeWorkToken;
        progressSmoother.Start(phase switch
        {
            WorkPhase.Collect => 10,
            WorkPhase.Analyze => 5,
            WorkPhase.Generate => 6,
            _ => 1
        });
        workProgressClock.Restart();
        WorkflowProgressBar.Value = 0;
        WorkflowProgressText.Text = label;
        WorkflowProgressHost.Visibility = Visibility.Visible;
        workProgressTimer.Start();
        return token;
    }

    private void SetWorkProgress(long token, WorkPhase phase, int completed, int total, string label)
    {
        if (token != activeWorkToken || activeWorkPhase != phase || total <= 0) return;
        progressSmoother.Confirm(completed, total,
            workProgressClock.Elapsed.TotalSeconds);
        WorkflowProgressText.Text = label;
    }

    private IProgress<WorkflowStepProgress> WorkProgressReporter(WorkPhase phase, long token) =>
        new Progress<WorkflowStepProgress>(step =>
            SetWorkProgress(token, phase, step.Completed, step.Total, step.Detail));

    private void FinishWorkProgress(long token, WorkPhase phase, bool success)
    {
        if (token != activeWorkToken || activeWorkPhase != phase) return;
        workProgressTimer.Stop();
        workProgressClock.Stop();
        progressSmoother.Stop();
        if (success)
        {
            WorkflowProgressBar.Value = 100;
            WorkflowProgressText.Text = phase switch
            {
                WorkPhase.Collect => "COLLECT / REPORTS READY",
                WorkPhase.Analyze => "ANALYSIS READY",
                WorkPhase.Generate => "ZIP READY",
                _ => "READY"
            };
        }
        // Completion is reported by the buttons and console. The shared
        // progress row is visible only while Collect, Analyze or Generate runs.
        WorkflowProgressHost.Visibility = Visibility.Collapsed;
        activeWorkPhase = WorkPhase.None;
    }

    private void ClearWorkProgress()
    {
        ++activeWorkToken;
        workProgressTimer.Stop();
        workProgressClock.Stop();
        progressSmoother.Stop();
        activeWorkPhase = WorkPhase.None;
        WorkflowProgressBar.Value = 0;
        WorkflowProgressHost.Visibility = Visibility.Collapsed;
    }

    internal void SetResultsConsoleStatus(string message)
    {
        latestResultsConsoleText = message;
        ++resultsConsoleRevision;
        ResultsStatusText.Text = message;
    }

    // The Capture console is the main workflow guide: brief normal statuses,
    // distinct action rows for framework blockers and missing capture evidence.
    private void SetStatus(string message)
    {
        latestCaptureConsoleText = UserFacingConsoleText.Render(consolePinned ?? message);
        ++captureConsoleRevision;
        StatusText.Text = latestCaptureConsoleText;
    }
    private void PinStatus(string message, string? detail = null)
    {
        consolePinned = message;
        latestCaptureConsoleText = UserFacingConsoleText.Render(message);
        ++captureConsoleRevision;
        StatusText.Text = latestCaptureConsoleText;
        if (!string.IsNullOrWhiteSpace(detail))
            Trace.TraceInformation("GCO capture status detail: " + detail);
    }
    private void ClearStatusPin() => consolePinned = null;

    // Four independent indicators reflect FrameworkPreflight's *existing*
    // gates, not a second UI compatibility parser or a new version lock.
    // Unknown/invalid root keeps both icons hidden for every component.
    private void ShowFrameworkGate(FrameworkGateStatus? gate, string name,
        System.Windows.Controls.Image success,
        System.Windows.Controls.Image failure, TextBlock label)
    {
        success.Visibility = gate?.Ready == true ? Visibility.Visible : Visibility.Collapsed;
        failure.Visibility = gate is { Ready: false } ? Visibility.Visible : Visibility.Collapsed;
        label.Text = gate is null ? name + " · NOT CHECKED"
            : gate.Ready ? name + (gate.Required == "present" ? " · PRESENT" : " " + gate.Required)
            : !gate.Present ? name + " · MISSING"
            : string.IsNullOrWhiteSpace(gate.Found) ? name + " · UNVERIFIED"
            : name + " " + gate.Found + " → " + gate.Required;
        label.Foreground = (Brush)FindResource(gate?.Ready == true ? "Yellow" : "Cyan");
    }

    internal void SetFrameworkStatuses(FrameworkPreflightResult? result)
    {
        FrameworkGateStatus? Gate(string key) =>
            result?.Gates.FirstOrDefault(g => string.Equals(g.Name, key, StringComparison.Ordinal));
        ShowFrameworkGate(Gate("CET"), "CET",
            CetFrameworkSuccessIcon, CetFrameworkFailureIcon, CetFrameworkLabel);
        ShowFrameworkGate(Gate("0-Engine"), "0-Engine",
            ZeroEngineFrameworkSuccessIcon, ZeroEngineFrameworkFailureIcon, ZeroEngineFrameworkLabel);
        ShowFrameworkGate(Gate("RED4ext"), "RED4ext",
            Red4extFrameworkSuccessIcon, Red4extFrameworkFailureIcon, Red4extFrameworkLabel);
        ShowFrameworkGate(Gate("redscript"), "redscript",
            RedscriptFrameworkSuccessIcon, RedscriptFrameworkFailureIcon, RedscriptFrameworkLabel);
    }

    // Derive the label/surface directly from the actual, game-bound owner
    // journal. Verified restore deletes the journal; switching source roots
    // never inherits another game's recovery control.
    private bool UpdateProfilerAction(string? gameRoot)
    {
        // RESTORE is available from either GCO's session journal or an
        // authoritative profiler-owned game-side transaction marker.
        var recovery = Workflow.RestoreActionAvailable(gameRoot);
        var label = recovery ? "RESTORE PROFILERS" : "INSTALL PROFILERS";
        var style = (Style)FindResource(
            recovery ? "RestoreStageButton" : "StageActionButton");
        // Re-applying the same template during status refresh would redraw
        // its SVG even if IsEnabled does not change.
        if (!string.Equals(InstallButton.Content as string, label, StringComparison.Ordinal))
            InstallButton.Content = label;
        if (!ReferenceEquals(InstallButton.Style, style))
            InstallButton.Style = style;
        return recovery;
    }

    private async Task RefreshSourceAsync()
    {
        if (!initialized || busy) return;
        // A deferred game/source refresh must not quietly progress Page 2
        // either; the same UI-only behavior applies to both refresh paths.
        if (ResultsPage.Visibility == Visibility.Visible)
        {
            RefreshResultsPresentationOnly();
            return;
        }
        if (mo2Mode) { await RefreshMo2SourceAsync(); return; }
        long refreshActivity = BeginConsoleActivity("Checking game and profiler status");
        try
        {
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
        var sourceChanged = !GameInspector.SameVerifiedGameRoot(inspection, current);
        inspection = current;
        // Focus refresh on the same valid game must not briefly force the
        // buttons to "Unavailable". New source/invalid source DOES revoke
        // cached permissions before awaiting any owner status.
        if (sourceChanged)
        {
            SetPortraitStage(PortraitStage.Black);
            SetFrameworkStatuses(null);
            liveState = null;
            savedCapture = null;
            InstallButton.IsEnabled = LaunchButton.IsEnabled = CollectButton.IsEnabled =
                ContinueButton.IsEnabled = false;
            LaunchButton.Content = "START CYBERPUNK";
        }
        var recoveryMode = UpdateProfilerAction(
            current.IsValidGame ? current.GameRoot : GamePathBox.Text);
        // Recovery is always callable for a durable journal, independent of
        // failed source inspection. Its handler enforces game-closed safety.
        if (recoveryMode && !InstallButton.IsEnabled)
            InstallButton.IsEnabled = true;

        UserSettings.WriteGameRoot(GamePathBox.Text);
        if (!current.IsValidGame)
        {
            ModCountText.Text = "Select the Cyberpunk 2077 game folder";
            SetFrameworkStatuses(null);
            CetStatusText.Text = "G-CET Profiler  ·  NOT CHECKED";
            RedStatusText.Text = "G-REDscript Profiler  ·  NOT CHECKED";
            PresentStatusText.Text = "PresentMon  ·  AUTOMATIC";
            SetStatus(Workflow.HasRecoveryJournal
                ? "Game folder unavailable. Use RESTORE PROFILERS."
                : "Select the Cyberpunk 2077 game folder containing bin/x64/Cyberpunk2077.exe.");
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
            // Unlike a normal focus refresh, failed provenance inspection
            // is a real new failure: invalidate stale actions immediately.
            InstallButton.IsEnabled = recoveryMode;
            LaunchButton.IsEnabled = CollectButton.IsEnabled =
                ContinueButton.IsEnabled = false;
            SetFrameworkStatuses(null);
            SetStatus("GCO package verification failed. Re-extract the official build.");
            Trace.TraceError("GCO package verification: " + bundle.Detail);
            return;
        }
        try
        {
            var state = await Workflow.InspectAsync(current.GameRoot!, cancel.Token);
            if (cancel.IsCancellationRequested || !IsLoaded || busy || mo2Mode ||
                (captureAndRestoreVerified && ResultsPage.Visibility == Visibility.Visible)) return;
            liveState = state;
            var running = GameRunning();
            var restoreAvailable = UpdateProfilerAction(current.GameRoot!);
            var session = Workflow.HasSessionForGame(current.GameRoot!);
            var foreignSession = Workflow.HasRecoveryJournal && !session;
            // A game-bound, verified owner installation reuses the original
            // admitted baseline until verified restore removes the journal.
            // No extra profiler version whitelist is needed.
            var pinnedFramework = Workflow.HasInstalledFrameworkBaseline(current.GameRoot!, state);
            var framework = pinnedFramework
                ? FrameworkPreflightResult.FromVerifiedInstalledBaseline()
                : FrameworkPreflight.Inspect(current.GameRoot!);
            SetFrameworkStatuses(framework);
            var saved = session ? Workflow.ReadCollected(current.GameRoot!, verifyHashes: true) : null;
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

            // Reopen/tray resume never resets the session: owners determine
            // which F11 captures actually completed. A failed Collect is
            // permanently disabled for this attempt, even after restarting WPF.
            var captureStarted = session && Workflow.HasCaptureAttempt(current.GameRoot!);
            var staged = captureStarted
                ? Workflow.PendingStagedNativeCaptures(current.GameRoot!)
                : (Cet: false, Redscript: false);
            var available = CaptureAdmissionPolicy.AvailableNativeCaptures(
                state, saved, staged.Cet, staged.Redscript);
            var readyToCollect = CaptureAdmissionPolicy.CanCollect(
                session, captureStarted, state, saved, staged.Cet, staged.Redscript);
            var processLaunchPending = DateTime.UtcNow < launchProcessGuardUntilUtc;
            InstallButton.IsEnabled = restoreAvailable ||
                (!foreignSession && framework.Ready && state.CleanForInstall && !running);
            // Do not start another game while the previous attempt still
            // needs collection; BeginCaptureAttempt creates a fresh identity
            // after collection, which re-enables Collect on the next launch.
            // A user may deliberately skip Collect: a later Start discards
            // ONLY GCO's uncollected attempt after stopping owned PresentMon.
            LaunchButton.IsEnabled = pinnedFramework &&
                !running && !processLaunchPending;
            CollectButton.IsEnabled = !running && readyToCollect;
            ContinueButton.IsEnabled = !running && !profilerReportRunning &&
                CaptureAdmissionPolicy.CanContinue(saved, frame is null) &&
                saved is not null && HasCompletedProfilerReport(packageRoot, saved.SessionId);
            // Reconstruct Page 1's decorative state from the authoritative
            // journal after tray hibernation/reopen, not from a local flag.
            SetPortraitStage(CaptureAdmissionPolicy.CanContinue(saved, frame is null)
                ? PortraitStage.Collecting
                : !state.BothInstalled || !session ? PortraitStage.Black
                : captureStarted && saved?.CollectInvoked != true
                    ? PortraitStage.Capturing
                    : PortraitStage.Installed);
            // Recover a sealed capture on restart or REFRESH without allowing
            // Continue to skip unfinished derived report generation.
            if (!running && saved is not null &&
                CaptureAdmissionPolicy.CanContinue(saved, frame is null) &&
                !profilerReportRunning && !HasCompletedProfilerReport(packageRoot, saved.SessionId))
            {
                await GenerateCollectedProfilerReportAsync(current.GameRoot!, saved.SessionId);
                if (cancel.IsCancellationRequested || !IsLoaded || busy || mo2Mode) return;
                ContinueButton.IsEnabled = HasCompletedProfilerReport(packageRoot, saved.SessionId);
            }
            // Adapt the captions to actual verified workflow state. Disabled
            // controls never suggest an unverified domain is installed.
            LaunchButton.Content = running ? "GAME RUNNING"
                : processLaunchPending ? "STARTING CYBERPUNK"
                : "START CYBERPUNK";
            CollectButton.Content = "COLLECT RESULTS";

            // Gameplay status replaces stale event/exit pins, rather than
            // reporting "Game closed" while Cyberpunk is still running.
            if (running || (captureStarted && saved?.CollectInvoked != true))
                ClearStatusPin();
            SetStatus(foreignSession
                ? "Another game session is pending. Restore it before switching games."
                : restoreAvailable && !session
                    ? "Profiler installation detected. Click RESTORE PROFILERS."
                : running ? "Cyberpunk running. Record gameplay; F11 START / STOP."
                : !framework.Ready ? UserFacingConsoleText.FrameworkGuidance(framework)
                : saved?.CollectionAttempted == true
                    ? UserFacingConsoleText.CollectionGuidance(saved)
                    : captureStarted && saved?.CollectInvoked == true
                        ? "Collect failed. START again or RESTORE."
                    : captureStarted && state.BothInstalled
                        ? CaptureAdmissionPolicy.PendingCaptureGuidance(available)
                        : state.BothInstalled
                            ? "Profilers installed. Click START CYBERPUNK.\nIn-game: F11 start, F11 stop."
                            : session
                                ? "Profiler recovery needed. Close the game and click RESTORE PROFILERS."
                                : $"G-CET Profiler: {cetProfiler} | G-REDscript Profiler: {redProfiler}");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested && IsLoaded && !mo2Mode)
            {
                // A failed inspection cannot leave stale actions enabled.
                // Keep the owner's emergency Restore action available.
                InstallButton.IsEnabled = Workflow.RestoreActionAvailable(current.GameRoot);
                LaunchButton.IsEnabled = CollectButton.IsEnabled =
                    ContinueButton.IsEnabled = false;
                SetFrameworkStatuses(null);
                Trace.TraceWarning("Profiler status inspection: " + ex);
                SetStatus("Profiler status unavailable. Retry Refresh.");
            }
        }
        }
        finally
        {
            EndConsoleActivity(refreshActivity);
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (mo2Mode) { await Mo2InstallOrRestoreTestAsync(); return; }
        // Only RESTORE and CONTINUE trigger owner-managed restoration.
        if (Workflow.RestoreActionAvailable(GamePathBox.Text))
            await RestoreIncompleteSessionAsync();
        else
            await InstallProfilersAsync();
    }
    private async Task InstallProfilersAsync()
    {
        if (mo2Mode || busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        ClearStatusPin();
        var frameworks = FrameworkPreflight.Inspect(inspection.GameRoot);
        if (!frameworks.Ready)
        {
            Trace.TraceWarning("Framework preflight: " + frameworks.Detail);
            PinStatus(UserFacingConsoleText.FrameworkGuidance(frameworks));
            return;
        }
        SetBusy(true);
        SetPortraitStage(PortraitStage.Installed);
        SetStatus("Installing CET and REDscript profilers...");
        long installActivity = BeginConsoleActivity("Installing profilers");
        try
        {
            var result = await Workflow.InstallAsync(inspection.GameRoot);
            if (result.Ok)
            {
                SetPortraitStage(PortraitStage.Installed);
                // InstallAsync already re-inspects both source-owned managers.
                PinStatus("Profilers installed. Ready to start Cyberpunk.");
            }
            else
            {
                SetPortraitStage(PortraitStage.Black);
                Warn(result.Detail, "Profiler installation");
            }
        }
        catch (Exception ex)
        {
            SetPortraitStage(PortraitStage.Black);
            Warn(ex.Message, "Profiler installation");
        }
        finally { EndConsoleActivity(installActivity); SetBusy(false); await RefreshSourceAsync(); }
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
        // Reset native owners while the game is closed. The old ETW writer is
        // separate and must be gracefully stopped by its existing controller.
        var reset = await Workflow.ResetNativeOutputAsync(game);
        if (!reset.Ok) throw new InvalidOperationException(reset.Detail);
        var clean = await Workflow.InspectAsync(game);

        // Before changing the authoritative journal, provision the next
        // destination. The ONE elevated controller then stops any prior
        // GCO-owned PresentMon (including a surviving forced-close writer)
        // and launches a fresh session with this distinct output CSV.
        var preparedId = Guid.NewGuid().ToString("N");
        CollectedCaptureStore.CreateDatedSessionFolder(
            packageRoot, preparedId, DateTimeOffset.Now, game,
            clean.CetCaptureReady, clean.GrspCaptureReady);
        var csv = PresentMonLifecyclePolicy.NewSessionCsv(packageRoot, preparedId);
        string? warning = null;
        var monStarted = false;
        try
        {
            await PresentMonCaptureController.RunAsync(packageRoot, csv,
                restart: true, captureSessionId: preparedId);
            monStarted = true;
        }
        catch (Exception ex)
        {
            warning = ex.Message;
            // Optional startup failure may fall back to CET/GRSP, but only
            // after proving neither an OLD nor a newly-started PresentMon
            // still owns an active CSV. Never delete a running writer's scope.
            var oldStopped = await PresentMonCaptureController.StopTrackedAsync(
                packageRoot, clearTracking: false);
            if (!oldStopped.Stopped)
                throw new InvalidOperationException(
                    "Previous PresentMon could not be reset; old output preserved. " +
                    oldStopped.Error, ex);
            var processes = Process.GetProcessesByName("PresentMon");
            try
            {
                if (processes.Length != 0)
                    throw new InvalidOperationException(
                        "PresentMon restart incomplete; writer still active. Old output preserved.", ex);
            }
            finally { foreach (var process in processes) process.Dispose(); }
        }
        // The controller has positively stopped the prior owned writer.
        // Retire only uncollected old scratch. A sealed collection stays
        // immutable. On success RunAsync already updated the tracking pointer
        // to the new CSV, so do not clear it while switching journals.
        Workflow.AbandonCurrentUncollected(game);
        if (!monStarted) PresentMonCaptureController.ClearTrackedCsv(packageRoot);
        Workflow.BeginCaptureAttempt(game,
            observedGameAlreadyRunning: gameAlreadyRunning,
            ownerBeforeCapture: clean, preparedSessionId: preparedId);
        return warning;
    }

    private async Task HandleExternalGameLaunchAsync()
    {
        if (mo2Mode || busy || !inspection.IsValidGame || inspection.GameRoot is null ||
            !Workflow.HasSessionForGame(inspection.GameRoot)) return;
        var game = inspection.GameRoot;
        SetBusy(true);
        long externalStartActivity = BeginConsoleActivity("Preparing capture for detected game");
        try
        {
            var state = await Workflow.InspectAsync(game);
            var warning = await BeginFreshCaptureAsync(game, state,
                gameAlreadyRunning: true);
            // A fast external exit still triggers a deferred status refresh.
            // Its ETW stop is handled exclusively by the native observer.
            if (!GameRunning())
                pendingGameExitRefresh = true;
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
        finally { EndConsoleActivity(externalStartActivity); SetBusy(false); }
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (mo2Mode) await Mo2LaunchTestAsync();
        else await LaunchCaptureAsync();
    }
    private async Task LaunchCaptureAsync()
    {
        if (mo2Mode || busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        if (GameRunning()) { Warn("Cyberpunk is already running.", "Start"); return; }
        if (DateTime.UtcNow < launchProcessGuardUntilUtc)
        {
            Warn("Cyberpunk startup is still being processed.", "Start");
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
        var launched = false;
        SetBusy(true);
        SetPortraitStage(PortraitStage.Capturing);
        long launchActivity = BeginConsoleActivity("Preparing profilers and starting Cyberpunk");
        try
        {
            var state = await Workflow.InspectAsync(game);
            var warning = await BeginFreshCaptureAsync(game, state,
                gameAlreadyRunning: false);
            try
            {
                // Arm before CreateProcess so a fast WMI start is classified
                // as this GCO launch rather than an external capture restart.
                gcoLaunchInProgress = true;
                ArmProcessStartGuard();
                using var started = Process.Start(new ProcessStartInfo(executable)
                {
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(executable)!
                }) ?? throw new InvalidOperationException("Cyberpunk did not start.");
                gcoLaunchedGamePid = started.Id;
                // Only track the real game executable, not a transient launch
                // delegate. If it has not started yet, wait for WMI/fallback.
                try
                {
                    if (!started.HasExited &&
                        string.Equals(started.ProcessName, "Cyberpunk2077",
                            StringComparison.OrdinalIgnoreCase))
                        GameStarted(started.Id);
                }
                catch (InvalidOperationException)
                {
                    // Already exited: a game startup notification may still
                    // arrive for the actual game PID.
                }
            }
            catch
            {
                gcoLaunchInProgress = false;
                launchProcessGuardUntilUtc = DateTime.MinValue;
                ++launchProcessGuardGeneration;
                // A failed game launch is not a meaningful capture. Flush ETW,
                // then drop its own uncollected folder without touching CET /
                // REDscript native files or the recovery journal.
                var stop = await PresentMonCaptureController.StopTrackedAsync(packageRoot);
                if (stop.Stopped) Workflow.AbandonCurrentUncollected(game);
                throw;
            }
            SetPortraitStage(PortraitStage.Capturing);
            PinStatus(warning is null
                ? "Cyberpunk started. Use F11 to start/stop profiling."
                : "Cyberpunk started. F11 ready; PresentMon unavailable.",
                warning);
            launched = true;
        }
        catch (Exception ex) { Warn(ex.Message, "Launch and capture"); }
        finally
        {
            gcoLaunchInProgress = false;
            gcoLaunchedGamePid = null;
            EndConsoleActivity(launchActivity);
            SetBusy(false);
            if (!launched) SetPortraitStage(PortraitStage.Installed);
            // Handoff when the launch request succeeds. The native tray and
            // observer are independent of whether Cyberpunk is observable
            // yet; the user can reopen GCO after a slow start or crash.
            if (!launched || !ArmNativeGameReopen(game) || !HibernateAfterLaunch())
                await RefreshSourceAsync();
        }
    }



    private async void Collect_Click(object sender, RoutedEventArgs e) => await CollectResultsAsync();
    private async Task CollectResultsAsync()
    {
        if (mo2Mode || busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
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
        if (existing?.CollectInvoked == true || existing?.CollectionAttempted == true)
        {
            Warn("Collect already used. START again or RESTORE.", "Collection");
            return;
        }
        // If native output vanishes between the visible readiness check and
        // the click, that failed click still ends this attempt exactly once.
        // The inner collection verifies owner readiness after the latch.
        // One-shot state is durable BEFORE any native copy, ETW flush or
        // potential exception. Successful saves remain; failed scratch clears.
        try { Workflow.RecordCollectInvoked(game); }
        catch (Exception ex) { Warn(ex.Message, "Collection"); return; }
        ClearStatusPin();
        SetBusy(true);
        SetPortraitStage(PortraitStage.Collecting);
        SetStatus("Collecting profiler results...");
        long collectActivity = BeginConsoleActivity("Collecting and verifying profiler results");
        long collectProgress = BeginWorkProgress(WorkPhase.Collect, "COLLECT / VERIFY CAPTURES");
        var sealedSuccess = false;
        try
        {
            var attempt = Workflow.ReadCollected(game) ??
                throw new InvalidDataException("No matching GCO capture attempt.");
            var nativeStatus = await Workflow.InspectAsync(game);
            var indexed = Workflow.ReadCollected(game, verifyHashes: true);
            var staged = Workflow.PendingStagedNativeCaptures(game);
            var ready = CaptureAdmissionPolicy.AvailableNativeCaptures(
                nativeStatus, indexed, staged.Cet, staged.Redscript);
            if (!ready.Cet || !ready.Redscript)
                throw new InvalidOperationException(
                    CaptureAdmissionPolicy.PendingCaptureGuidance(ready));
            // After the owned writer is stopped, first hash/copy BOTH native
            // F11 archives; they are the clock authority for frame alignment.
            var stop = await PresentMonCaptureController.StopTrackedAsync(packageRoot,
                clearTracking: false);
            string? frameWarning = stop.Error;
            if (stop.Stopped)
                SetWorkProgress(collectProgress, WorkPhase.Collect, 1, 10, "COLLECT / PRESENTMON STOPPED");
            var native = await Workflow.CollectResultsAsync(game);
            if (native.Ok)
                SetWorkProgress(collectProgress, WorkPhase.Collect, 2, 10, "COLLECT / CET + REDSCRIPT");
            if (native.Ok && stop.Stopped && stop.Csv is not null)
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
                        var both = Workflow.ReadCollected(game, verifyHashes: true) ??
                            throw new InvalidDataException("Native session index missing.");
                        var overlap = PresentMonCaptureAlignment.RequireOverlap(packageRoot, both);
                        CollectedCaptureStore.CollectPresentMon(packageRoot, game,
                            attempt.SessionId, stop.Csv, overlap);
                    }
                    catch (Exception ex)
                    {
                        // Do not invent frame-time evidence when owner clocks
                        // or real frame rows cannot prove the common window.
                        frameWarning = "Frame-time alignment unavailable: " + ex.Message;
                    }
                }
            }
            SetWorkProgress(collectProgress, WorkPhase.Collect, 3, 10, "COLLECT / FRAME EVIDENCE CHECKED");
            // Do not seal a failed/empty native collection. Keep the pending
            // capture intact so Collect may be retried or the run abandoned.
            var collected = Workflow.ReadCollected(game, verifyHashes: true);
            if (!native.Ok || !stop.Stopped ||
                collected?.Cet is null || collected.Redscript is null)
                throw new InvalidOperationException("Native collection incomplete: " + native.Detail +
                    (frameWarning is null ? "" : " | PresentMon: " + frameWarning));
            // Retire the temporary PresentMon pointer only after the CET/GRSP
            // archives are durable, hashes verify, and ETW is stopped. If this
            // cleanup fails, do not seal Collect; the same session can retry.
            PresentMonCaptureController.ClearTrackedCsv(packageRoot);
            var saved = Workflow.RecordCollectionAttempt(game, stop.Stopped);
            saved = Workflow.ReadCollected(game, verifyHashes: true) ?? saved;
            savedCapture = saved;
            sealedSuccess = true;
            SetWorkProgress(collectProgress, WorkPhase.Collect, 4, 10, "COLLECT / CAPTURES VERIFIED");
            if (saved.Cet is not null && saved.Redscript is not null)
            {
                // Keep Collect's black-dominant signal through report
                // building and while awaiting CONTINUE.
                UpdateResultTexts(saved);
            }
            else SetPortraitStage(PortraitStage.Black);

            var nativeWarning = native.Ok &&
                (saved.CetReadyBeforeAttempt || saved.RedscriptReadyBeforeAttempt)
                    ? native.Detail : native.Ok ? null : native.Detail;
            PinStatus(UserFacingConsoleText.CollectionGuidance(saved),
                "Collection: " + native.Detail +
                (frameWarning is null ? "" : " | PresentMon: " + frameWarning) +
                (nativeWarning is null ? "" : " | Native: " + nativeWarning));
        }
        catch (Exception ex)
        {
            SetPortraitStage(PortraitStage.Black);
            Warn(ex.Message, "Capture collection");
        }
        finally
        {
            if (!sealedSuccess)
            {
                // Attempt both owner resets, even on partial collection. No
                // file can be cleared while the owned PresentMon writer runs.
                try
                {
                    var stopped = await PresentMonCaptureController.StopTrackedAsync(
                        packageRoot, clearTracking: false);
                    if (stopped.Stopped)
                    {
                        var reset = await Workflow.ResetNativeOutputAsync(game);
                        if (reset.Ok)
                        {
                            Workflow.AbandonCurrentUncollected(game);
                            PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                        }
                        else Trace.TraceWarning("Failed-Collect native cleanup: " + reset.Detail);
                    }
                    else Trace.TraceWarning("Failed-Collect PresentMon shutdown: " + stopped.Error);
                }
                catch (Exception ex) { Trace.TraceWarning("Failed-Collect scratch cleanup: " + ex); }
                // Never convert a failed one-shot attempt into a saved capture.
                // START will use the normal capture reset if cleanup is pending.
                SetPortraitStage(PortraitStage.Black);
                PinStatus("Collect failed. START again or RESTORE.");
            }
            EndConsoleActivity(collectActivity);
            bool reportsReady = false;
            if (sealedSuccess && savedCapture is { } complete)
                reportsReady = await GenerateCollectedProfilerReportAsync(game, complete.SessionId);
            FinishWorkProgress(collectProgress, WorkPhase.Collect, sealedSuccess && reportsReady);
            SetBusy(false);
            await RefreshSourceAsync();
        }
    }

    internal static bool HasCompletedProfilerReport(string packageRoot, string sessionId)
    {
        var html = GcoProfilerReportService.ReportFile(packageRoot, sessionId);
        var json = Path.Combine(Path.GetDirectoryName(html)!,
            GcoProfilerReportService.JsonFile);
        if (!File.Exists(html) || !File.Exists(json)) return false;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(json));
            return document.RootElement.TryGetProperty("sessionId", out var id) &&
                id.GetString() == sessionId &&
                new FileInfo(html).Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or JsonException or InvalidOperationException)
        {
            Trace.TraceWarning("Combined report readiness: " + ex.Message);
            return false;
        }
    }

    private async Task<bool> GenerateCollectedProfilerReportAsync(string game, string sessionId)
    {
        if (profilerReportRunning) return false;
        if (HasCompletedProfilerReport(packageRoot, sessionId))
        {
            if (activeWorkPhase == WorkPhase.Collect)
                SetWorkProgress(activeWorkToken, WorkPhase.Collect, 10, 10, "COLLECT / REPORTS VERIFIED");
            return true;
        }
        bool ownsRail = activeWorkPhase != WorkPhase.Collect;
        long reportToken = ownsRail
            ? BeginWorkProgress(WorkPhase.Collect, "COLLECT / BUILDING REPORTS")
            : activeWorkToken;
        bool reportReady = false;
        profilerReportRunning = true;
        ContinueButton.IsEnabled = false;
        long reportActivity = BeginConsoleActivity("Preparing performance graphs and report");
        try
        {
            var reporter = WorkProgressReporter(WorkPhase.Collect, reportToken);
            var result = await Task.Run(() =>
                GcoProfilerReportService.GenerateAsync(packageRoot, game, sessionId,
                    progress: reporter));
            if (!HasCompletedProfilerReport(packageRoot, sessionId))
                throw new InvalidDataException("Combined profiler report was not published completely.");
            Trace.TraceInformation("Unified profiler report: " + result.HtmlPath +
                " | PresentMon frames=" + result.PresentMonFrames);
            ClearStatusPin();
            SetStatus("Combined profiler report ready. Click CONTINUE.");
            reportReady = true;
            return true;
        }
        catch (Exception ex)
        {
            // Never discard sealed native captures on a derived report failure.
            // REFRESH can retry reporting; emergency RESTORE remains available.
            Trace.TraceWarning("GCO combined profiler reporting: " + ex);
            PinStatus("Combined report failed. Click REFRESH to retry. " +
                "Captures are saved; CONTINUE is unavailable.", ex.ToString());
            return false;
        }
        finally
        {
            EndConsoleActivity(reportActivity);
            profilerReportRunning = false;
            if (ownsRail) FinishWorkProgress(reportToken, WorkPhase.Collect, reportReady);
        }
    }

    private void UpdateResultTexts(CollectedCaptureIndex saved)
    {
        // Page 2 retains the restoration confirmation above both panels.
        // The redundant capture-domain OK row is no longer displayed.
        ResultsRestoreStatusText.Text = saved.Restored
            ? "Both profilers uninstalled. All game files are restored."
            : "Profiler restoration has not yet been verified.";
        ResultsRestoreStatusText.Foreground = saved.Restored
            ? (System.Windows.Media.Brush)FindResource("Cyan") : AnalyzeYellow;

        // A normal refresh must not erase a completed analysis or replace
        // it with capture paths, compiler details, or development diagnostics.
        if (analyzedSessionId != saved.SessionId)
        {
            RefreshPlannedWork();
            CetReportText.Text = saved.Cet is null
                ? "CET capture unavailable."
                : "Capture verified. Click ANALYZE for optimization candidates.";
            RedReportText.Text = saved.Redscript is null
                ? "REDscript capture unavailable."
                : "Capture verified. Click ANALYZE for optimization candidates.";
        }
    }

    private async void Continue_Click(object sender, RoutedEventArgs e) => await ContinueToAnalyticsAsync();
    private async Task ContinueToAnalyticsAsync()
    {
        if (mo2Mode || busy || !inspection.IsValidGame || inspection.GameRoot is null) return;
        if (captureAndRestoreVerified)
        {
            RefreshCompletedWorkspace();
            if (portraitStage is PortraitStage.Black or PortraitStage.Installed or
                PortraitStage.Capturing or PortraitStage.Collecting)
                SetPortraitStage(PortraitStage.Results);
            CapturePage.Visibility = Visibility.Collapsed;
            CaptureFooterHost.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            BackToCaptureButton.Visibility = Visibility.Visible;
            ClearWorkProgress();
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
        if (profilerReportRunning || saved is null ||
            !HasCompletedProfilerReport(packageRoot, saved.SessionId))
        {
            Warn("Wait for the combined profiler report to finish. " +
                "If reporting failed, click REFRESH to retry.", "Continue");
            return;
        }
        SetBusy(true);
        long continueActivity = BeginConsoleActivity("Restoring profilers and preparing analysis");
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
            if (completedSessionId != saved.SessionId)
                HotscenesEnglishOnlyCheckBox.IsChecked = false;
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
            SetPortraitStage(PortraitStage.Results);
            CapturePage.Visibility = Visibility.Collapsed;
            CaptureFooterHost.Visibility = Visibility.Collapsed;
            ResultsPage.Visibility = Visibility.Visible;
            BackToCaptureButton.Visibility = Visibility.Visible;
            ClearWorkProgress();
            SetActiveTab(true);
            RefreshCompletedWorkspace();
            if (workspaceWarning is not null) PinStatus(workspaceWarning);
        }
        catch (Exception ex) { Warn(ex.Message, "Continue failed"); }
        finally { EndConsoleActivity(continueActivity); SetBusy(false); }
    }

    private void RefreshCompletedWorkspace()
    {
        if (!captureAndRestoreVerified || completedSessionId is null || completedGameRoot is null)
            return;
        try
        {
            // Verify the sealed native evidence AND the recorded owner
            // restoration before presenting Page 2 as ready.
            var saved = CollectedCaptureStore.ReadRestored(
                packageRoot, completedGameRoot, completedSessionId);
            UpdateResultTexts(saved);
            RefreshHotscenesOption();
            if (analyzedSessionId != saved.SessionId && !analysisRunning)
                SetResultsConsoleStatus("Capture verified. Click ANALYZE to find optimizations.");
            AnalyzeButton.IsEnabled = !analysisRunning && !generationRunning &&
                analyzedSessionId != saved.SessionId;
            if (!analysisRunning)
                AnalyzeButton.Content = analyzedSessionId == saved.SessionId
                    ? "ANALYZED" : "ANALYZE";
            RefreshDoneAvailability();
            GenerateButton.IsEnabled = !generationRunning && !analysisRunning &&
                analyzedSessionId == saved.SessionId && redscriptPlanReady &&
                !HasPublishedOptimizationZip() && HasNewOptimizationAvailable();
            if (analyzedSessionId == saved.SessionId && !generationRunning &&
                !HasPublishedOptimizationZip() && analyzedAvailability is not null &&
                !HasNewOptimizationAvailable())
                SetResultsConsoleStatus(NoNewOptimizationStatus());
            OpenFolderButton.IsEnabled = !generationRunning &&
                generatedPassZipPath is not null && File.Exists(generatedPassZipPath);
        }
        catch (Exception ex)
        {
            SetHotscenesOptionVisible(false);
            SetResultsConsoleStatus("Saved capture verification failed: " + ex.Message);
            ResultsRestoreStatusText.Text = "Restoration or capture verification unavailable.";
            ResultsRestoreStatusText.Foreground = AnalyzeYellow;
            AnalyzeButton.IsEnabled = false;
            GenerateButton.IsEnabled = false;
            OpenFolderButton.IsEnabled = false;
            CloseAfterGenerationButton.Visibility = Visibility.Collapsed;
        }
    }

    private async void Analyze_Click(object sender, RoutedEventArgs e)
    {
        if (analysisRunning || generationRunning || !captureAndRestoreVerified ||
            completedSessionId is null || completedGameRoot is null ||
            analyzedSessionId == completedSessionId) return;
        try
        {
            // Manual user decision. CONTINUE and reopening never launch a
            // Resolver. The original captures remain hash-sealed.
            var saved = CollectedCaptureStore.ReadRestored(
                packageRoot, completedGameRoot, completedSessionId);
            await RunReadOnlyAnalysisAsync(saved);
        }
        catch (Exception ex)
        {
            // Verify safety first, then retain the actual error in the
            // console if the captured workspace is still usable.
            RefreshCompletedWorkspace(); // disables actions if provenance failed
            SetResultsConsoleStatus("Cannot analyze: " + ex.Message);
            Trace.TraceWarning("Manual Analyze refused: " + ex);
        }
    }

    private async Task RunReadOnlyAnalysisAsync(CollectedCaptureIndex saved)
    {
        // Explicit ANALYZE click only; CONTINUE solely restores profilers
        // and displays verified capture readiness.
        if (analysisRunning ||
            completedGameRoot is null || completedSessionId != saved.SessionId ||
            ResultsPage.Visibility != Visibility.Visible)
            return;

        analysisRunning = true;
        SetPortraitStage(PortraitStage.Analyzing);
        redscriptPlanReady = false;
        analyzedAvailability = null;
        analyzedCetCounts = null;
        analyzedRedscriptCounts = null;
        RefreshPlannedWork();
        hotscenesOptInRefused = false;
        analysisCancellation?.Dispose();
        analysisCancellation = new CancellationTokenSource();
        var cancel = analysisCancellation;
        AnalyzeButton.Content = "ANALYZING";
        AnalyzeButton.IsEnabled = false;
        GenerateButton.IsEnabled = false;
        CloseAfterGenerationButton.Visibility = Visibility.Collapsed;
        long analyzeActivity = BeginConsoleActivity("Analyzing CET and REDscript sources");
        long analyzeProgress = BeginWorkProgress(WorkPhase.Analyze, "ANALYZE / CET SOURCES");
        try
        {
            var report = await readOnlyAnalyzer.AnalyzeAsync(
                packageRoot, completedGameRoot, saved.SessionId, cancel.Token);
            if (cancel.IsCancellationRequested || !IsLoaded ||
                completedSessionId != saved.SessionId) return;
            SetWorkProgress(analyzeProgress, WorkPhase.Analyze, 1, 5, "ANALYZE / REDSCRIPT SOURCES");
            // Do not mark the session ANALYZED until both CET and the
            // REDscript generation plan have completed successfully.
            // MaterialRemaining is an analysis callback count, not a
            // generation refusal count. Below-threshold work is not shown here.
            CetReportText.Text = "Checking current CET source and callback evidence...";
            // REDscript is no longer reported as absent: use the actual
            // bundled GCO-local Resolver to decide source-specific passes and
            // their generation order. This is a plan, not an optimization ZIP.
            // The six existing source-generators and the five still-unbuilt
            // experimental systems are deliberately counted separately.
            string redPlanStatus;
            try
            {
                string sourceRoot = completedGameRoot!;
                string sessionRoot = CollectedCaptureStore.SessionFolder(
                    packageRoot, saved.SessionId);
                string planPath = Path.Combine(sessionRoot,
                    "REDscript_GenerationPlan.json");
                var planned = await Task.Run(() =>
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    Directory.CreateDirectory(sessionRoot);
                    string tempPlan = Path.Combine(sessionRoot,
                        "REDscript_GenerationPlan." +
                        Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        var result = RedscriptGenerationPlan.Create(
                            sourceRoot, tempPlan);
                        cancel.Token.ThrowIfCancellationRequested();
                        File.Move(tempPlan, planPath, overwrite: true);
                        return result;
                    }
                    finally
                    {
                        if (File.Exists(tempPlan)) File.Delete(tempPlan);
                    }
                }, cancel.Token);
                if (cancel.IsCancellationRequested || !IsLoaded ||
                    completedSessionId != saved.SessionId) return;
                redscriptPlanReady = true;
                analyzedCetCounts = report;
                analyzedRedscriptCounts = planned;
                SetWorkProgress(analyzeProgress, WorkPhase.Analyze, 2, 5,
                    "ANALYZE / VERIFYING APPLICABLE RULES");
                redPlanStatus = "REDscript source analysis ready.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                redscriptPlanReady = false;
                redPlanStatus = "REDscript current-source generation plan unavailable: " +
                    ex.Message + " | Existing generated source rules remain preview-only.";
                Trace.TraceWarning("REDscript Resolver plan: " + ex);
            }
            if (redscriptPlanReady)
            {
                // Probe source-aware eligibility in disposable output using
                // the existing CET and REDscript generators. Hotscenes remains
                // Generate-only, as required by the owner's opt-in contract.
                analyzedAvailability = await combinedPassGenerator.InspectAvailableAsync(
                    packageRoot, completedGameRoot!, saved.SessionId, cancel.Token,
                    WorkProgressReporter(WorkPhase.Analyze, analyzeProgress));
                if (cancel.IsCancellationRequested || !IsLoaded ||
                    completedSessionId != saved.SessionId) return;
                SetWorkProgress(analyzeProgress, WorkPhase.Analyze, 5, 5,
                    "ANALYZE / APPLICABLE RULES VERIFIED");
                if (analyzedAvailability.RedscriptSkipped.Length > 0)
                    Trace.TraceInformation("REDscript source-specific refusals: " +
                        string.Join(" | ", analyzedAvailability.RedscriptSkipped.Select(
                            item => item.Id + ": " + item.Reason)));
            }
            RedReportText.Text = redPlanStatus;
            analyzedSessionId = redscriptPlanReady ? saved.SessionId : null;
            RefreshPlannedWork();
            AnalyzeButton.Content = redscriptPlanReady ? "ANALYZED" : "ANALYZE";
            SetResultsConsoleStatus(!redscriptPlanReady
                ? "REDscript plan unavailable. GENERATE remains disabled; the source and capture are unchanged."
                : HasNewOptimizationAvailable()
                    ? "CET + REDscript analysis ready. GENERATE creates one ZIP containing all optimizations."
                    : NoNewOptimizationStatus());
        }
        catch (OperationCanceledException) { /* Closing/reopening cancels only this derived analysis. */ }
        catch (Exception ex)
        {
            if (cancel.IsCancellationRequested || !IsLoaded) return;
            Trace.TraceWarning("Read-only CET analysis: " + ex);
            SetResultsConsoleStatus("CET analysis unavailable: " + ex.Message +
                " Collected results remain untouched.");
            analyzedSessionId = null;
            analyzedAvailability = null;
            RefreshPlannedWork();
            CetReportText.Text = "CET Resolver unavailable. The collected capture is unchanged.";
            AnalyzeButton.Content = "UNAVAILABLE";
        }
        finally
        {
            EndConsoleActivity(analyzeActivity);
            FinishWorkProgress(analyzeProgress, WorkPhase.Analyze,
                analyzedSessionId == saved.SessionId && redscriptPlanReady);
            analysisRunning = false;
            if (analyzedSessionId != saved.SessionId || !redscriptPlanReady)
                SetPortraitStage(PortraitStage.Results);
            RefreshDoneAvailability();
            AnalyzeButton.IsEnabled = analyzedSessionId != saved.SessionId &&
                !generationRunning;
            if (analyzedSessionId != saved.SessionId)
                AnalyzeButton.Content = "ANALYZE";
            GenerateButton.IsEnabled = analyzedSessionId == saved.SessionId &&
                redscriptPlanReady && analyzedAvailability is not null &&
                HasNewOptimizationAvailable() && !generationRunning &&
                !HasPublishedOptimizationZip();
        }
    }

    private async void GenerateCetPass_Click(object sender, RoutedEventArgs e)
    {
        if (generationRunning || analysisRunning ||
            HasPublishedOptimizationZip() ||
            completedSessionId is null || completedGameRoot is null ||
            analyzedSessionId != completedSessionId ||
            !redscriptPlanReady || !captureAndRestoreVerified ||
            !HasNewOptimizationAvailable())
            return;
        if (GameRunning())
        {
            Warn("Close Cyberpunk before generating the optimization ZIP.", "Generate");
            return;
        }

        // Recheck the live installed source immediately before Generate.
        // A removed Hotscenes mod must not leave a stale ENG-only request.
        RefreshHotscenesOption();
        // Snapshot the explicit player choice for this generation only.
        bool hotscenesEnglishOnly =
            HotscenesEnglishOnlyCheckBox.Visibility == Visibility.Visible &&
            HotscenesEnglishOnlyCheckBox.IsChecked == true;
        generationRunning = true;
        SetPortraitStage(PortraitStage.Generating);
        generatedPassZipPath = null;
        ClearWorkProgress();
        GeneratedPassNotice.Visibility = Visibility.Collapsed;
        HotscenesEnglishOnlyCheckBox.IsEnabled = false;
        GenerateButton.IsEnabled = false;
        OpenFolderButton.IsEnabled = false;
        var session = completedSessionId;
        var game = completedGameRoot;
        var cancellation = new CancellationTokenSource();
        analysisCancellation?.Dispose();
        analysisCancellation = cancellation;
        GenerateButton.Content = "GENERATING...";
        CloseAfterGenerationButton.Visibility = Visibility.Collapsed;
        long generateActivity = BeginConsoleActivity("Generating CET and REDscript optimizations");
        long generateProgress = BeginWorkProgress(WorkPhase.Generate, "GENERATE / CET OPTIMIZATIONS");
        try
        {
            // CET retains its owner generator; GCO-local REDscript stages
            // the canonical ordered plan. Only one game-relative ZIP is
            // published after source drift and ZIP ownership/integrity checks.
            var pass = await combinedPassGenerator.GenerateAsync(
                packageRoot, game, session, cancellation.Token,
                hotscenesEnglishOnly, WorkProgressReporter(WorkPhase.Generate, generateProgress));
            if (cancellation.IsCancellationRequested || !IsLoaded) return;
            // Keep the three source-summary rows unchanged after GENERATE.
            // Actual output/refusals are already displayed in the console and
            // the verified ownership sidecar, never as comparable source units.
            var skippedCetOwners = pass.CetSkipDetails
                .Select(item => item.Owner)
                .Where(owner => !string.IsNullOrWhiteSpace(owner))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            int unavailableRuleFamilies = pass.RedscriptSkipped
                .Select(item => item.Id)
                .Distinct(StringComparer.Ordinal).Count();

            string summary = "ZIP generated. CET: " + pass.CetTransforms +
                " transforms | REDscript: " + pass.RedscriptEdits + " edits.";
            // Publish access only after the combined owner returns a real ZIP.
            generatedPassZipPath = File.Exists(pass.ZipPath) ? pass.ZipPath : null;
            if (generatedPassZipPath is not null)
                summary += Environment.NewLine + "in GCO folder /" +
                    Path.GetRelativePath(packageRoot, generatedPassZipPath).Replace('\\', '/');
            if (pass.CetSkipped > 0 || pass.RedscriptSkipped.Length > 0)
            {
                summary += Environment.NewLine + "Unavailable: CET " +
                    pass.CetSkipped + " entries";
                if (pass.CetSkipped > 0 && skippedCetOwners.Length > 0)
                    summary += " (" + string.Join(", ", skippedCetOwners) + ")";
                summary += " | REDscript " + unavailableRuleFamilies + " rule families (" +
                    pass.RedscriptSkipped.Length + " operations).";
            }
            if (pass.HotscenesEnglishOnlyRequested &&
                pass.HotscenesEnglishOnlyEdits == 0 &&
                !HasCompletedHotscenesEnglishOnly(game))
                summary += Environment.NewLine + "Hotscenes ENG-only: not applied.";
            SetResultsConsoleStatus(summary);
            RefreshPlannedWork();
            if (generatedPassZipPath is not null)
            {
                GeneratedPassNotice.Visibility = Visibility.Visible;
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
                {
                    if (GeneratedPassNotice.Visibility == Visibility.Visible)
                        GeneratedPassNotice.BringIntoView();
                }));
            }
            OpenFolderButton.IsEnabled = generatedPassZipPath is not null;
            CloseAfterGenerationButton.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) { }
        catch (NoNewOptimizationsException)
        {
            // A source-dependent Hotscenes attempt may have been refused:
            // do not re-enable an identical unsuccessful Generate forever.
            hotscenesOptInRefused = true;
            analyzedAvailability = new CombinedPassAvailability(0, 0,
                analyzedAvailability?.RedscriptSkipped ?? []);
            RefreshPlannedWork();
            SetResultsConsoleStatus(NoNewOptimizationStatus());
        }
        catch (Exception ex)
        {
            if (!cancellation.IsCancellationRequested && IsLoaded)
            {
                Trace.TraceWarning("Combined pass generation: " + ex);
                SetResultsConsoleStatus("Combined pass refused: " + ex.Message +
                    " Original game and capture files were not changed.");
            }
        }
        finally
        {
            EndConsoleActivity(generateActivity);
            FinishWorkProgress(generateProgress, WorkPhase.Generate,
                generatedPassZipPath is not null && File.Exists(generatedPassZipPath));
            generationRunning = false;
            if (IsLoaded)
            {
                bool generated = HasPublishedOptimizationZip();
                if (!generated) SetPortraitStage(PortraitStage.Analyzing);
                RefreshDoneAvailability();
                HotscenesEnglishOnlyCheckBox.IsEnabled = !generated;
                OpenFolderButton.IsEnabled = generated && generatedPassZipPath is not null &&
                    File.Exists(generatedPassZipPath);
                GenerateButton.Content = generated ? "GENERATED" :
                    "GENERATE OPTIMIZATION ZIP";
                GenerateButton.IsEnabled = !generated && analyzedSessionId == session &&
                    redscriptPlanReady && HasNewOptimizationAvailable();
            }
        }
    }

    private async Task RestoreIncompleteSessionAsync()
    {
        if (busy) return;
        // Prefer the verified journal root. If the journal is absent/corrupt,
        // use the user-selected game only when its owner marker proves a
        // managed installation. Owner managers perform all actual restoration.
        var game = Workflow.ReadRecoveryGameRoot() ??
            (inspection.IsValidGame ? inspection.GameRoot : GamePathBox.Text);
        if (!Workflow.RestoreActionAvailable(game) ||
            (Workflow.ReadRecoveryGameRoot() is null && !Workflow.HasOwnerRestoreState(game)))
        {
            Warn("Select the game with installed profilers to restore.", "Recovery required");
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
        long restoreActivity = BeginConsoleActivity("Restoring original game files");
        try
        {
            // No capture index/collection attempt is required. Recover even
            // directly after a partial install, including after GCO restarts.
            var stop = await PresentMonCaptureController.StopTrackedAsync(packageRoot);
            // Restore original source files even when stopping PresentMon
            // fails, but keep the durable journal and uncollected results
            // until the full reset can be verified on a subsequent click.
            // The owner restores and GCO scratch disposal are completed by
            // the same workflow, not independently in the UI.
            var result = await Workflow.RestoreOwnedAsync(game,
                preserveIncompleteEvidence: false,
                pendingPresentMonStopped: stop.Stopped);
            if (!result.RestoreVerified)
            {
                Warn(result.Detail, "Recovery required");
                return;
            }
            string? cleanupWarning = null;
            try { CollectedCaptureStore.ForgetLastRestored(packageRoot); }
            catch (Exception ex)
            {
                cleanupWarning = "Saved Analyze pointer could not be cleared: " + ex.Message;
                Trace.TraceWarning("Restored workspace pointer cleanup: " + ex);
            }
            SetPortraitStage(PortraitStage.Black);
            // Owner verification removed the journal. The same button changes
            // immediately from RESTORE to INSTALL, and the path unlocks.
            UpdateProfilerAction(game);
            UpdatePathLock();
            var fullDetails = result.Detail +
                (cleanupWarning is null ? "" : " | " + cleanupWarning);
            Trace.TraceInformation("Profiler restore: " + fullDetails);
            PinStatus(cleanupWarning is null
                    ? "Restore complete. Original files restored."
                    : "Original files restored. Check saved workspace.",
                fullDetails);
        }
        catch (Exception ex) { Warn(ex.Message, "Recovery required"); }
        finally { EndConsoleActivity(restoreActivity); SetBusy(false); await RefreshSourceAsync(); }
    }

    // Reset only the active workflow and resume bookmark, never the
    // hash-sealed RESULTS or previously published game-only ZIPs.
    internal bool ResetCompletedWorkflowForNextCapture()
    {
        if (busy || analysisRunning || generationRunning || profilerReportRunning ||
            Workflow.HasRecoveryJournal || ResultsPage.Visibility != Visibility.Visible)
            return false;

        CollectedCaptureStore.ForgetLastRestored(packageRoot);
        analyzedSessionId = null;
        completedSessionId = null;
        completedGameRoot = null;
        captureAndRestoreVerified = false;
        redscriptPlanReady = false;
        analyzedAvailability = null;
        hotscenesOptInRefused = false;
        savedCapture = null;
        generatedPassZipPath = null;
        RefreshPlannedWork();
        ClearWorkProgress();
        GeneratedPassNotice.Visibility = Visibility.Collapsed;
        OpenFolderButton.IsEnabled = false;
        SetHotscenesOptionVisible(false);
        HotscenesEnglishOnlyCheckBox.IsEnabled = true;
        CetReportText.Text = "Capture verified. Click ANALYZE for optimization candidates.";
        RedReportText.Text = "Capture verified. Click ANALYZE for optimization candidates.";
        AnalyzeButton.Content = "ANALYZE";
        AnalyzeButton.IsEnabled = false;
        GenerateButton.Content = "GENERATE OPTIMIZATION ZIP";
        GenerateButton.IsEnabled = false;
        CloseAfterGenerationButton.Visibility = Visibility.Collapsed;
        SetResultsConsoleStatus("Capture verified. Click ANALYZE to find optimizations.");
        ResultsScrollViewer.ScrollToTop();
        ClearStatusPin();
        SetPortraitStage(PortraitStage.Black);
        ResultsPage.Visibility = Visibility.Collapsed;
        BackToCaptureButton.Visibility = Visibility.Collapsed;
        CapturePage.Visibility = Visibility.Visible;
        CaptureFooterHost.Visibility = Visibility.Visible;
        SetActiveTab(false);
        SetStatus("Ready for a new profiler capture.");
        return true;
    }

    private async void BackToCapture_Click(object sender, RoutedEventArgs e)
    {
        if (busy || analysisRunning || generationRunning || profilerReportRunning)
        {
            Warn("Finish the current operation before returning to Capture.", "Back");
            return;
        }
        if (System.Windows.MessageBox.Show(this,
                "Return to page one. This will reset your captures and return you to the first page." +
                Environment.NewLine + Environment.NewLine +
                "Do you want to continue?",
                "Return to Capture", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        try
        {
            if (ResetCompletedWorkflowForNextCapture())
                await RefreshSourceAsync();
        }
        catch (Exception ex)
        {
            Warn("Could not reset the active workflow: " + ex.Message, "Back");
        }
    }

    private void CloseAfterGeneration_Click(object sender, RoutedEventArgs e)
    {
        if (CloseAfterGenerationButton.Visibility != Visibility.Visible) return;
        try
        {
            if (ResetCompletedWorkflowForNextCapture())
                Close(); // full exit, not capture-hibernation tray
        }
        catch (Exception ex)
        {
            Warn("Could not reset the active workflow: " + ex.Message, "Close");
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        // The source of truth is the exact ZIP returned by GenerateAsync.
        // Never open a prior pass or the application root before completion.
        if (generationRunning || generatedPassZipPath is null ||
            !File.Exists(generatedPassZipPath))
        {
            OpenFolderButton.IsEnabled = false;
            return;
        }
        try
        {
            // OPEN FOLDER opens the pass folder with the generated ZIP selected.
            // Use ArgumentList so spaces in the game/package path stay intact.
            var explorer = new ProcessStartInfo("explorer.exe")
            {
                UseShellExecute = false
            };
            explorer.ArgumentList.Add("/select,");
            explorer.ArgumentList.Add(generatedPassZipPath);
            Process.Start(explorer);
        }
        catch (Exception ex)
        {
            Warn("Could not show the generated optimization ZIP: " +
                ex.Message, "Open folder");
        }
    }

    // The pinned console is the one normal diagnostic channel. No modal popup
    // can interrupt collection/recovery or bypass the verified Continue gate.
    private void Warn(string message, string title)
    {
        Trace.TraceWarning(title + ": " + message);
        var summary = UserFacingConsoleText.Failure(title, message);
        if (ResultsPage.Visibility == Visibility.Visible)
            SetResultsConsoleStatus(summary);
        else
            PinStatus(summary, title + ": " + message);
    }
}
