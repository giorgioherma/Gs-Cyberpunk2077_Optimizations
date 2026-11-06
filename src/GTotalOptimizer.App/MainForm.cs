using System.Diagnostics;
using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

internal sealed class MainForm : Form
{
    // Same palette as the current CET Profiler (not a separate light-theme app).
    private static readonly Color Bg = Color.FromArgb(8, 13, 18);
    private static readonly Color PanelBg = Color.FromArgb(14, 23, 31);
    private static readonly Color PanelAlt = Color.FromArgb(11, 18, 25);
    private static readonly Color Border = Color.FromArgb(40, 71, 82);
    private static readonly Color TextColor = Color.FromArgb(232, 243, 246);
    private static readonly Color Muted = Color.FromArgb(172, 188, 197);
    private static readonly Color Cyan = Color.FromArgb(54, 244, 244);
    private static readonly Color Magenta = Color.FromArgb(255, 63, 215);
    private static readonly Color Green = Color.FromArgb(94, 255, 130);
    private static readonly Color Amber = Color.FromArgb(255, 216, 64);

    private readonly Panel pageOne = new() { Dock = DockStyle.Fill };
    private readonly Panel pageTwo = new() { Dock = DockStyle.Fill };
    private readonly TextBox sourcePath = Entry();
    private readonly TextBox mo2Folder = Entry();
    private readonly TextBox mo2GameRoot = Entry();
    private readonly ComboBox mo2InstanceChoice = SourceDropdown();
    private readonly ComboBox mo2Profile = SourceDropdown();
    private readonly CheckBox[] mo2ComponentChecks =
    [
        SourceCheck("MO2 profile"), SourceCheck("CET ASI"), SourceCheck("CET mods"),
        SourceCheck("r6/scripts"), SourceCheck("red4ext/plugins")
    ];
    private Mo2Location mo2Location = Mo2InstallLocator.Inspect(null);
    private Mo2Instance mo2Configuration = Mo2SourceInspector.Discover(null);
    private Mo2Stack? mo2Effective;
    private string? selectedMo2Instance;
    private bool loadingMo2;
    private readonly RadioButton normalMode = ModeChoice("NORMAL", true);
    private readonly RadioButton mo2Mode = ModeChoice("MO2", false);
    private readonly Panel normalPanel = new();
    private readonly Panel mo2Panel = new() { Visible = false };
    private TableLayoutPanel? setupContent;
    private readonly SourcePathCheck zeroEngineCheck = PathCheck();
    private readonly SourcePathCheck scriptsCheck = PathCheck();
    private readonly SourcePathCheck red4extCheck = PathCheck();
    private readonly Label gamePathTick = new()
    {
        Text = "✓", ForeColor = Green, BackColor = Color.Transparent,
        AutoSize = false, Visible = false,
        Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label engineMissingNotice = Readout();
    private readonly Dictionary<TextBox, Panel> pathOutlines = new();
    private readonly LinkLabel zeroEngineLink = new()
    {
        Text = "0-Engine required — Nexus Mods",
        LinkColor = Cyan, ActiveLinkColor = Magenta, VisitedLinkColor = Cyan,
        AutoSize = false, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label profilerReady = Readout();
    private readonly Label stepInstructionOne = Readout();
    private readonly Label stepInstructionTwo = Readout();
    private readonly Label stepInstructionThree = Readout();
    private readonly CheckBox stepOne = SourceCheck("CET");
    private readonly CheckBox stepTwo = SourceCheck("REDscript");
    private readonly CheckBox stepThree = SourceCheck("CapX");
    private readonly Label cetCaptureKey = Readout();
    private readonly Label redCaptureKey = Readout();
    private readonly Label capxCaptureKey = Readout();
    private readonly Label captureKeyWarning = Readout();
    private ProfilerSetupView profilerSetup = ProfilerSetupView.Unverified();
    private GroupBox? sourceSection;
    private readonly Label sourceStatus = Readout();
    private readonly Label pageOneStatus = Readout();
    private readonly Label pageTwoStatus = Readout();
    private readonly TextBox cetReport = ReportBox();
    private readonly TextBox redReport = ReportBox();
    private readonly Button install = Action("1  INSTALL PROFILERS");
    private readonly Button launch = Action("2  START CYBERPUNK / CAPTURE");
    private readonly Button collect = Action("3  COLLECT RESULTS");
    private readonly Button next = Action("CONTINUE  →");
    private readonly Button analyze = Action("1  ANALYZE CAPTURE");
    private readonly Button generate = Action("2  GENERATE OPTIMIZATION PASS");
    private readonly Button openResults = Action("3  OPEN RESULTS FOLDER");
    private readonly BackendStatusRunner backend = new();
    private CancellationTokenSource? scanCancellation;
    private GameInspection inspection = GameInspector.Inspect(null);
    private string? resultsFolder;
    private bool captureAndRestoreVerified;
    // Keep the completed, restored capture bound to this window. Continue
    // deletes the owner journal; it is NOT a signal to discard saved evidence.
    private string? completedSessionId;
    private string? completedGameRoot;
    private readonly string packageRoot = PortablePackagePaths.CurrentPackageRoot();
    private string? packagePathFailure;
    private UnifiedProfilerWorkflow Workflow => new(packageRoot);
    private bool busy;
    private bool captureLaunched;
    private readonly LinkLabel recoveryLink = new();

    public MainForm()
    {
        Text = $"G's Cyberpunk 2077 Total Optimization Generator — v{Application.ProductVersion}";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 828);
        ClientSize = new Size(1010, 790);
        Font = new Font("Segoe UI", 9.5f);
        BackColor = Bg;
        ForeColor = TextColor;

        BuildProfilerPage();
        BuildResultsPage();
        // The status is one line high. Clicking it exposes real full paths.
        pageOneStatus.Click += (_, _) =>
        {
            if (packagePathFailure is not null)
                MessageBox.Show(this, packagePathFailure, "GCO package-path diagnostics",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        Controls.Add(pageTwo);
        Controls.Add(pageOne);
        pageTwo.Visible = false;

        sourcePath.TextChanged += async (_, _) =>
        {
            if (!mo2GameRoot.Text.Equals(sourcePath.Text, StringComparison.Ordinal))
                mo2GameRoot.Text = sourcePath.Text;
            await RefreshSourceAsync();
        };
        mo2GameRoot.TextChanged += (_, _) =>
        {
            if (!sourcePath.Text.Equals(mo2GameRoot.Text, StringComparison.Ordinal))
                sourcePath.Text = mo2GameRoot.Text;
        };
        mo2Folder.TextChanged += async (_, _) =>
        {
            if (loadingMo2) return;
            selectedMo2Instance = null;
            UserSettings.WriteMo2Setup(mo2Folder.Text, "", "");
            RefreshMo2Configuration();
            await RefreshSourceAsync();
        };
        mo2InstanceChoice.SelectedIndexChanged += async (_, _) =>
        {
            if (loadingMo2) return;
            var i = mo2InstanceChoice.SelectedIndex;
            selectedMo2Instance = i >= 0 && i < mo2Location.InstanceChoices.Count
                ? mo2Location.InstanceChoices[i] : null;
            UserSettings.WriteMo2Setup(mo2Folder.Text, selectedMo2Instance ?? "", "");
            RefreshMo2Configuration();
            await RefreshSourceAsync();
        };
        mo2Profile.SelectedIndexChanged += async (_, _) =>
        {
            if (loadingMo2) return;
            UserSettings.WriteMo2Setup(mo2Folder.Text, selectedMo2Instance ?? "",
                mo2Profile.SelectedItem?.ToString() ?? "");
            await RefreshSourceAsync();
        };
        normalMode.CheckedChanged += async (_, _) =>
        {
            if (!normalMode.Checked) return;
            SetSourceMode();
            await RefreshSourceAsync();
        };
        mo2Mode.CheckedChanged += async (_, _) =>
        {
            if (!mo2Mode.Checked) return;
            if (!Mo2FeatureGate.Enabled)
            {
                normalMode.Checked = true;
                return;
            }
            SetSourceMode();
            await RefreshSourceAsync();
        };
        zeroEngineLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "https://www.nexusmods.com/cyberpunk2077/mods/27967") { UseShellExecute = true });
        install.Click += async (_, _) => await InstallProfilersAsync();
        launch.Click += async (_, _) => await LaunchCaptureAsync();
        collect.Click += async (_, _) => await CollectResultsAsync();
        recoveryLink.Click += async (_, _) => await RestoreIncompleteSessionAsync();
        next.Click += async (_, _) => await ContinueToAnalyticsAsync();
        analyze.Click += (_, _) => ExplainPending("CET and REDscript combined analysis requires verified fresh captures and both backends.");
        generate.Click += (_, _) => ExplainPending("The single combined ZIP requires source-proven CET and REDscript generation, full REDscript compilation and dependency validation.");
        openResults.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(resultsFolder) && Directory.Exists(resultsFolder))
                Process.Start(new ProcessStartInfo(resultsFolder) { UseShellExecute = true });
        };

        install.Enabled = launch.Enabled = collect.Enabled = next.Enabled = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;
        var setup = UserSettings.ReadMo2Setup();
        selectedMo2Instance = setup.InstanceFolder;
        // MO2 discovery is retained but its runtime/source workflow is deferred.
        mo2Mode.Enabled = Mo2FeatureGate.Enabled;
        mo2Mode.Text = "MO2 — COMING LATER";
        var mo2Hint = new ToolTip();
        mo2Hint.SetToolTip(mo2Mode,
            "MO2 paths and profiles are discoverable, but profiling and optimization are disabled for the initial release.");
        loadingMo2 = true;
        mo2Folder.Text = setup.InstallationFolder;
        loadingMo2 = false;
        sourcePath.Text = UserSettings.ReadGameRoot();
        RefreshMo2Configuration();
        SetSourceMode();
        Shown += async (_, _) => await RefreshSourceAsync();
        // As in the CET owner UI, returning from the game refreshes owner
        // capture completion rather than assuming a launch flag proves a run.
        Activated += async (_, _) =>
        {
            if (!busy) await RefreshSourceAsync();
        };
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP",
            "Install, capture, collect results. Continue restores original files.", setup: true);
        setupContent = (TableLayoutPanel)grid.GetControlFromPosition(0, 1)!;
        var header = (Panel)grid.GetControlFromPosition(0, 0)!;
        var refreshButton = BrowseAction();
        refreshButton.Text = "REFRESH";
        refreshButton.Size = new Size(94, 28);
        refreshButton.Top = 13;
        refreshButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        void PlaceRefresh() => refreshButton.Left =
            Math.Max(0, header.ClientSize.Width - refreshButton.Width - 7);
        header.Controls.Add(refreshButton);
        header.Resize += (_, _) => PlaceRefresh();
        PlaceRefresh();
        refreshButton.Click += async (_, _) =>
        {
            if (mo2Mode.Checked) RefreshMo2Configuration();
            await RefreshSourceAsync();
        };

        sourceSection = Section("Game folder /Cyberpunk2077", SetupPageLayout.NormalSourceHeight);
        sourceSection.Dock = DockStyle.Fill;
        sourceSection.Margin = new Padding(0, 2, 0, 2);

        // Full-width, mutually exclusive views. MO2 replaces the Normal form;
        // it is not a small radio choice followed by both sets of input fields.
        var tabs = new TableLayoutPanel
        {
            Location = new Point(14, 22), Size = new Size(880, 28),
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tabs.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        foreach (var radio in new[] { normalMode, mo2Mode })
        {
            radio.Dock = DockStyle.Fill;
            radio.Margin = new Padding(0);
            radio.FlatAppearance.BorderSize = 1;
            radio.FlatAppearance.CheckedBackColor = Color.FromArgb(23, 38, 47);
            radio.FlatAppearance.MouseOverBackColor = Color.FromArgb(20, 34, 41);
            tabs.Controls.Add(radio);
        }
        sourceSection.Controls.Add(tabs);

        // Mirror the CET profiler: explicit 26px textbox and 92x30 Browse button.
        // No nested TableLayoutPanel/row border that can clip the Browse control.
        normalPanel.SetBounds(14, 56, 880, 32);
        var browse = BrowseAction();
        ConfigureCetPathRow(normalPanel, sourcePath, browse, 0);
        browse.Click += (_, _) => ChooseSource();
        sourceSection.Controls.Add(normalPanel);

        // MO2 remains disabled for the initial direct-install release.
        // Future mode needs only the game and MO2 folders; the INI determines
        // Mods/Profiles/Overwrite, while the selected profile determines modlist.txt.
        mo2Panel.SetBounds(14, 54, 880, 89);
        void AddMo2Path(string label, TextBox field, int top, Action browse)
        {
            var row = new Panel { Left = 0, Top = top, Width = 880, Height = 29 };
            var tag = Readout();
            tag.Text = label;
            tag.ForeColor = TextColor;
            tag.Font = new Font("Segoe UI", 8.6f);
            tag.SetBounds(0, 0, 95, 28);
            row.Controls.Add(tag);
            var host = new Panel { Left = 96, Top = 0, Width = 784, Height = 29 };
            var button = BrowseAction();
            ConfigureCetPathRow(host, field, button, 0);
            button.Click += (_, _) => browse();
            row.Controls.Add(host);
            row.Resize += (_, _) => host.Width = Math.Max(250, row.ClientSize.Width - 96);
            mo2Panel.Controls.Add(row);
        }
        AddMo2Path("MO2 folder", mo2Folder, 0, () =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the MO2 application folder (or portable MO2 directory)",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) == DialogResult.OK) mo2Folder.Text = dialog.SelectedPath;
        });
        AddMo2Path("Game folder", mo2GameRoot, 29, () => ChooseSource());
        var instanceCaption = Readout();
        instanceCaption.Text = "Instance";
        instanceCaption.ForeColor = TextColor;
        instanceCaption.SetBounds(0, 58, 65, 29);
        mo2Panel.Controls.Add(instanceCaption);
        mo2InstanceChoice.SetBounds(65, 60, 281, 27);
        mo2InstanceChoice.Dock = DockStyle.None;
        mo2Panel.Controls.Add(mo2InstanceChoice);
        var browseInstance = BrowseAction();
        browseInstance.Text = "...";
        browseInstance.SetBounds(351, 59, 36, 28);
        browseInstance.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "If not detected: select the MO2 instance with ModOrganizer.ini",
                UseDescriptionForTitle = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            selectedMo2Instance = dialog.SelectedPath;
            UserSettings.WriteMo2Setup(mo2Folder.Text, selectedMo2Instance, "");
            RefreshMo2Configuration();
            _ = RefreshSourceAsync();
        };
        mo2Panel.Controls.Add(browseInstance);
        var profileCaption = Readout();
        profileCaption.Text = "Profile";
        profileCaption.ForeColor = TextColor;
        profileCaption.SetBounds(401, 58, 55, 29);
        mo2Panel.Controls.Add(profileCaption);
        mo2Profile.SetBounds(456, 60, 424, 27);
        mo2Profile.Dock = DockStyle.None;
        mo2Panel.Controls.Add(mo2Profile);
        mo2Panel.Resize += (_, _) =>
        {
            var width = Math.Max(400, mo2Panel.ClientSize.Width);
            foreach (var row in mo2Panel.Controls.OfType<Panel>())
                row.Width = width;
            mo2Profile.Width = Math.Max(110, width - 456);
        };
        sourceSection.Controls.Add(mo2Panel);
        foreach (var check in mo2ComponentChecks)
        {
            check.ForeColor = TextColor;
            check.Font = new Font("Segoe UI", 8.7f);
            sourceSection.Controls.Add(check);
        }

        sourceStatus.Font = new Font("Segoe UI", 8.8f);
        sourceStatus.AutoEllipsis = true;
        sourceSection.Controls.Add(sourceStatus);
        foreach (var check in new[] { zeroEngineCheck, scriptsCheck, red4extCheck })
        {
            check.ForeColor = TextColor;
            check.Font = new Font("Segoe UI", 8.8f);
            sourceSection.Controls.Add(check);
        }
        engineMissingNotice.Font = new Font("Segoe UI Semibold", 8.8f, FontStyle.Bold);
        engineMissingNotice.ForeColor = Amber;
        engineMissingNotice.Text = "0-ENGINE MISSING!!";
        engineMissingNotice.Visible = false;
        sourceSection.Controls.Add(engineMissingNotice);
        zeroEngineLink.Font = new Font("Segoe UI", 8.7f);
        sourceSection.Controls.Add(zeroEngineLink);

        sourceSection.Resize += (_, _) =>
        {
            var width = Math.Max(350, sourceSection.ClientSize.Width - 28);
            tabs.Width = normalPanel.Width = mo2Panel.Width = width;
            sourceStatus.Width = width;
            for (var i = 0; i < mo2ComponentChecks.Length; i++)
            {
                mo2ComponentChecks[i].Left = 14 + (width * i / mo2ComponentChecks.Length);
                mo2ComponentChecks[i].Width = width / mo2ComponentChecks.Length - 3;
            }
            zeroEngineCheck.Width = Math.Min(610, width);
            zeroEngineLink.Width = Math.Max(150,
                Math.Min(250, width - zeroEngineLink.Left));
            red4extCheck.Width = Math.Max(180, width - red4extCheck.Left);
            LayoutEngineWarning();
        };
        setupContent.Controls.Add(sourceSection, 0, 0);

        var guide = Section("INSTRUCTIONS", SetupPageLayout.InstructionsHeight);
        guide.Dock = DockStyle.Fill;
        guide.Margin = new Padding(0, 2, 0, 4);

        var provision = Readout();
        provision.Text = "Provide the path to the live Cyberpunk 2077 game files.";
        provision.ForeColor = TextColor;
        provision.Font = new Font("Segoe UI", 9.3f);
        provision.SetBounds(15, 23, 890, 21);
        guide.Controls.Add(provision);
        guide.Controls.Add(gamePathTick);
        var provisionSize = TextRenderer.MeasureText(provision.Text, provision.Font,
            new Size(900, provision.Height), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        gamePathTick.SetBounds(provision.Left + provisionSize.Width + 5,
            provision.Top, 22, provision.Height);

        var start = Readout();
        start.Text = SetupCopy.CaptureStartInstruction;
        start.ForeColor = TextColor;
        start.Font = new Font("Segoe UI Semibold", 9.7f, FontStyle.Bold);
        start.TextAlign = ContentAlignment.TopLeft;
        start.SetBounds(15, 44, 890, 43);
        guide.Controls.Add(start);

        var conflicts = Readout();
        conflicts.Text =
            "Temporarily unbind other mods or game controls using F11 (CET and Settings).\r\n" +
            SetupCopy.CapFrameXProcess;
        conflicts.ForeColor = TextColor;
        conflicts.Font = new Font("Segoe UI", 9.2f);
        conflicts.SetBounds(15, 89, 890, 37);
        guide.Controls.Add(conflicts);

        var headline = Readout();
        headline.Text = "RECORD 5–10 MINUTES IN THE CITY";
        headline.ForeColor = Green;
        headline.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        headline.SetBounds(15, 129, 890, 27);
        guide.Controls.Add(headline);

        var phases = Readout();
        phases.Text = "IDLE   •   MOVEMENT   •   DRIVING   •   HEAVY COMBAT";
        phases.ForeColor = Amber;
        phases.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        phases.SetBounds(15, 157, 890, 24);
        guide.Controls.Add(phases);

        var avoid = Readout();
        avoid.Text = "AVOID MENUS, INVENTORY, MAP, CET UI AND LOADING SCREENS";
        avoid.ForeColor = TextColor;
        avoid.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        avoid.SetBounds(15, 182, 890, 22);
        guide.Controls.Add(avoid);

        var detail = Readout();
        detail.ForeColor = TextColor;
        detail.Font = new Font("Segoe UI", 9f);
        detail.Text = SetupCopy.CaptureDetails;
        detail.Font = new Font("Segoe UI", 8.8f);
        detail.SetBounds(15, 206, 890, 55);
        detail.TextAlign = ContentAlignment.TopLeft;
        guide.Controls.Add(detail);
        guide.Resize += (_, _) =>
        {
            var width = Math.Max(350, guide.ClientSize.Width - 30);
            foreach (Control item in new Control[]
                { provision, start, conflicts, headline, phases, avoid, detail })
                item.Width = width;
        };
        setupContent.Controls.Add(guide, 0, 1);

        var steps = Section("PROFILER STATUS / CAPTURE KEYBINDS", 142);
        steps.Dock = DockStyle.Fill;
        steps.Margin = new Padding(0, 2, 0, 2);
        profilerReady.ForeColor = Amber;
        profilerReady.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Bold);
        profilerReady.SetBounds(16, 26, 885, 23);
        steps.Controls.Add(profilerReady);
        recoveryLink.Text = "RESTORE WITHOUT CAPTURE";
        recoveryLink.Font = new Font("Segoe UI", 8.5f);
        recoveryLink.LinkColor = Amber;
        recoveryLink.ActiveLinkColor = Cyan;
        recoveryLink.VisitedLinkColor = Amber;
        recoveryLink.BackColor = PanelBg;
        recoveryLink.SetBounds(690, 28, 240, 20);
        recoveryLink.Visible = false;
        steps.Controls.Add(recoveryLink);

        // Restore the original three workflow descriptions on the LEFT.
        // Read-only installation checks and keybind labels remain on the RIGHT.
        var descriptions = new[] { stepInstructionOne, stepInstructionTwo, stepInstructionThree };
        var originalCopy = new[]
        {
            SetupCopy.StepInstall, SetupCopy.StepStart, SetupCopy.StepCollect
        };
        for (var i = 0; i < descriptions.Length; i++)
        {
            descriptions[i].Text = originalCopy[i];
            descriptions[i].ForeColor = i == 0 ? Cyan : Color.FromArgb(104, 118, 126);
            descriptions[i].Font = new Font("Segoe UI", 8.7f);
            descriptions[i].AutoEllipsis = true;
            descriptions[i].SetBounds(16, 55 + i * 26, 575, 22);
            steps.Controls.Add(descriptions[i]);
        }

        var checks = new[] { stepOne, stepTwo, stepThree };
        var keys = new[] { cetCaptureKey, redCaptureKey, capxCaptureKey };
        for (var i = 0; i < checks.Length; i++)
        {
            var y = 55 + i * 26;
            checks[i].SetBounds(608, y, 117, 23);
            checks[i].Font = new Font("Segoe UI Semibold", 9.5f);
            checks[i].ForeColor = TextColor;
            steps.Controls.Add(checks[i]);
            keys[i].SetBounds(736, y, 220, 23);
            keys[i].Font = new Font("Consolas", 9.3f);
            keys[i].ForeColor = TextColor;
            steps.Controls.Add(keys[i]);
        }
        captureKeyWarning.Font = new Font("Segoe UI", 9f);
        captureKeyWarning.ForeColor = Amber;
        captureKeyWarning.TextAlign = ContentAlignment.TopLeft;
        captureKeyWarning.SetBounds(16, 139, 920, 36);
        steps.Controls.Add(captureKeyWarning);

        void ArrangeProfilerColumns()
        {
            var width = Math.Max(760, steps.ClientSize.Width);
            var right = width - 365;
            profilerReady.Width = captureKeyWarning.Width = width - 32;
            for (var i = 0; i < descriptions.Length; i++)
            {
                descriptions[i].Width = Math.Max(260, right - 25);
                checks[i].Left = right;
                keys[i].Left = right + 128;
                keys[i].Width = Math.Max(120, width - keys[i].Left - 12);
            }
        }
        steps.Resize += (_, _) => ArrangeProfilerColumns();
        ArrangeProfilerColumns();
        setupContent.Controls.Add(steps, 0, 2);
        UpdateWorkflowVisuals();

        var footer = (TableLayoutPanel)grid.GetControlFromPosition(0, 2)!;
        footer.ColumnCount = 3;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
        foreach (var button in new[] { install, launch, collect })
        {
            button.Dock = DockStyle.Fill;
            footer.Controls.Add(button);
        }

        var status = (TableLayoutPanel)grid.GetControlFromPosition(0, 3)!;
        status.ColumnCount = 2;
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        pageOneStatus.Dock = DockStyle.Fill;
        pageOneStatus.Font = new Font("Segoe UI", 8.5f);
        pageOneStatus.AutoEllipsis = true;
        pageOneStatus.Text = "Profiler binaries and restore integration not yet verified.";
        status.Controls.Add(pageOneStatus, 0, 0);
        next.Dock = DockStyle.Fill;
        if (next is NeonButton nextButton) nextButton.IsAccent = true;
        status.Controls.Add(next, 1, 0);
        SetSourceMode();
    }

    private void SetSourceMode()
    {
        if (sourceSection is null || setupContent is null) return;
        var mo2 = Mo2FeatureGate.Enabled && mo2Mode.Checked;
        normalPanel.Visible = !mo2;
        mo2Panel.Visible = mo2;
        setupContent.RowStyles[0].Height = mo2
            ? SetupPageLayout.Mo2SourceHeight : SetupPageLayout.NormalSourceHeight;
        foreach (var tab in new[] { normalMode, mo2Mode })
        {
            var selected = tab.Checked;
            tab.BackColor = selected ? Color.FromArgb(23, 38, 47) : PanelAlt;
            tab.ForeColor = selected ? Cyan : Muted;
            tab.FlatAppearance.BorderColor = selected ? Cyan : Border;
        }
        foreach (var check in mo2ComponentChecks) check.Visible = mo2;
        foreach (var check in new Control[]
            { zeroEngineCheck, scriptsCheck, red4extCheck })
            check.Visible = !mo2;
        if (mo2)
        {
            var width = Math.Max(350, sourceSection.ClientSize.Width - 28);
            // Missing 0-Engine warning belongs ABOVE the five MO2 checks;
            // nothing may extend below the group box bottom border.
            engineMissingNotice.SetBounds(14, SetupPageLayout.Mo2EngineNoticeTop,
                170, 18);
            zeroEngineLink.SetBounds(184, SetupPageLayout.Mo2EngineNoticeTop,
                Math.Min(width - 170, 260), 18);
            for (var i = 0; i < mo2ComponentChecks.Length; i++)
                mo2ComponentChecks[i].SetBounds(14 + width * i / 5,
                    SetupPageLayout.Mo2ChecksTop, width / 5 - 3, 20);
            sourceStatus.SetBounds(14, SetupPageLayout.Mo2StatusTop, width, 18);
        }
        else
        {
            sourceStatus.SetBounds(14, 91, 880, 20);
            zeroEngineCheck.SetBounds(14, 113, 520, 20);
            scriptsCheck.SetBounds(14, 134, 210, 20);
            red4extCheck.SetBounds(240, 134, 220, 20);
            engineMissingNotice.Visible = false;
            LayoutEngineWarning();
        }
    }

    private static CheckBox SourceCheck(string label) => new SourceStatusCheck
    {
        Text = label, AutoCheck = false, Checked = false,
        AutoSize = false, TabStop = false, FlatStyle = FlatStyle.Flat,
        BackColor = PanelBg, ForeColor = TextColor,
        Cursor = Cursors.Default
    };

    private static SourcePathCheck PathCheck() => new()
    {
        BackColor = PanelBg, ForeColor = TextColor,
        Font = new Font("Segoe UI Semibold", 9.3f)
    };

    private static ComboBox SourceDropdown() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat, BackColor = PanelAlt, ForeColor = TextColor,
        Font = new Font("Segoe UI", 9f)
    };

    private void RefreshMo2Configuration()
    {
        // Read only. No candidate is silently chosen when multiple MO2 instances
        // or profiles exist and MO2 does not explicitly identify the current one.
        loadingMo2 = true;
        try
        {
            var saved = UserSettings.ReadMo2Setup();
            mo2Location = Mo2InstallLocator.Inspect(mo2Folder.Text, selectedMo2Instance);
            selectedMo2Instance = mo2Location.InstanceFolder;

            mo2InstanceChoice.Items.Clear();
            foreach (var candidate in mo2Location.InstanceChoices)
                mo2InstanceChoice.Items.Add(mo2Location.IsPortable
                    ? "Portable" : Path.GetFileName(candidate));
            var index = selectedMo2Instance is null ? -1 :
                mo2Location.InstanceChoices.ToList().FindIndex(c =>
                    c.Equals(selectedMo2Instance, StringComparison.OrdinalIgnoreCase));
            mo2InstanceChoice.SelectedIndex = index;

            mo2Configuration = Mo2SourceInspector.Discover(selectedMo2Instance);

            mo2Profile.Items.Clear();
            foreach (var profile in mo2Configuration.Profiles)
                mo2Profile.Items.Add(profile);
            string? preferred = mo2Configuration.PreferredProfile;
            if (string.IsNullOrWhiteSpace(preferred) &&
                saved.InstanceFolder.Equals(selectedMo2Instance, StringComparison.OrdinalIgnoreCase))
                preferred = saved.Profile;
            var active = mo2Configuration.Profiles.FirstOrDefault(p =>
                p.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (active is null && mo2Configuration.Profiles.Count == 1)
                active = mo2Configuration.Profiles[0];
            mo2Profile.SelectedItem = active;
        }
        finally { loadingMo2 = false; }
    }

    private void UpdateWorkflowVisuals()
    {
        // Only authoritative install/restore transactions with verified payload
        // identity may set these to true. Game paths, source checks, binding
        // presence and a responding CLI do not establish profiler installation.
        profilerReady.Text = profilerSetup.Heading;
        profilerReady.ForeColor = profilerSetup.Heading == "PROFILERS INSTALLED" ? Green : Amber;
        stepThree.Text = "PresentMon";
        stepOne.Checked = profilerSetup.CetInstalled;
        stepTwo.Checked = profilerSetup.RedscriptInstalled;
        stepThree.Checked = profilerSetup.CapXInstalled;
        cetCaptureKey.Text = profilerSetup.CetKeyDisplay;
        redCaptureKey.Text = "F11 (fixed)";
        capxCaptureKey.Text = profilerSetup.CapXKeyDisplay;
        var warning = profilerSetup.SynchronizationWarning;
        captureKeyWarning.Text = warning ?? "";
        captureKeyWarning.Visible = warning is not null;
        // During source refresh, keep the initial visual step neutral until
        // the owner status and current-session evidence have been read.
        ApplyWorkflowStage(SetupWorkflowStage.Install);
        // Keybind mismatch is advisory; never change workflow progression.
        if (captureAndRestoreVerified) next.Enabled = true;
    }

    private void ApplyWorkflowStage(SetupWorkflowStage stage)
    {
        // LEFT three-step instructions follow the same authoritative status
        // that controls the footer buttons. PresentMon AUTO is optional and
        // never counts as a native profiler install or F11 capture.
        var descriptions = new[] { stepInstructionOne, stepInstructionTwo, stepInstructionThree };
        for (var i = 0; i < descriptions.Length; i++)
        {
            var step = i + 1;
            descriptions[i].ForeColor = step < (int)stage ? Green :
                step == (int)stage ? Cyan : Color.FromArgb(104, 118, 126);
        }
    }

    private static RadioButton ModeChoice(string label, bool selected) => new()
    {
        Text = label.Equals("NORMAL", StringComparison.OrdinalIgnoreCase) ? "Normal" : "MO2",
        Checked = selected, Appearance = Appearance.Button, AutoSize = false,
        FlatStyle = FlatStyle.Flat, ForeColor = selected ? Cyan : Muted,
        BackColor = selected ? Color.FromArgb(23, 38, 47) : PanelAlt,
        Font = new Font("Segoe UI Semibold", 9.5f),
        TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand
    };

    private void BuildResultsPage()
    {
        var grid = PageLayout(pageTwo, "ANALYTICS & PASS GENERATION", "Analyze your capture, generate one clean game-relative optimization ZIP, then open RESULTS.");
        var content = (FlowLayoutPanel)grid.GetControlFromPosition(0, 1)!;

        var howto = Section("How to apply the optimization pass", 160);
        var info = Readout();
        info.Text =
            "1. ANALYZE CAPTURE — preview CET and REDscript findings.\r\n" +
            "2. GENERATE OPTIMIZATION PASS — build the verified overlay ZIP.\r\n" +
            "3. OPEN RESULTS FOLDER — find G-Cyberpunk2077-{timestamp}_Optimization_Pass.zip.\r\n\r\n" +
            "Vortex: add the ZIP after the original mods and let the overlay win the intended conflicts.\r\n" +
            "Some aggressive changes can affect functionality. Exclude affected owner files only after checking dependencies. " +
            "Keep 0-Engine and G-REDruntime when any remaining generated changes depend on them.";
        info.SetBounds(15, 28, 900, 122);
        howto.Controls.Add(info);
        howto.Resize += (_, _) => info.Width = howto.ClientSize.Width - 30;
        content.Controls.Add(howto);

        var cet = Section("CET — callback, allocations, G-GC and generic/semantic rules", 225);
        cetReport.SetBounds(15, 28, 900, 185);
        cetReport.Text = "No verified CET capture analyzed.\r\nResults will show accepted, already optimized, skipped and unsupported owners.";
        cet.Controls.Add(cetReport);
        cet.Resize += (_, _) => cetReport.Width = cet.ClientSize.Width - 30;
        content.Controls.Add(cet);

        var red = Section("REDscript — callsites, source proof and compile status", 225);
        redReport.SetBounds(15, 28, 900, 185);
        redReport.Text = "No verified REDscript capture analyzed.\r\nREDscript generic generator and full-stack compilation are not yet implemented.";
        red.Controls.Add(redReport);
        red.Resize += (_, _) => redReport.Width = red.ClientSize.Width - 30;
        content.Controls.Add(red);

        var actions = (TableLayoutPanel)grid.GetControlFromPosition(0, 2)!;
        actions.ColumnCount = 3;
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
        foreach (var button in new[] { analyze, generate, openResults })
        {
            button.Dock = DockStyle.Fill;
            actions.Controls.Add(button);
        }

        var status = (TableLayoutPanel)grid.GetControlFromPosition(0, 3)!;
        // After a verified Continue, native original files are restored and
        // the source-owner journal is deleted. Returning to Setup without an
        // explicit new-session transaction would strand the user there.
        // New-capture/reset design will be added separately; do not expose a
        // nonfunctional Back button.
        status.ColumnCount = 1;
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pageTwoStatus.Dock = DockStyle.Fill;
        pageTwoStatus.Text = "Generation is blocked until the complete source, dependency and compiler checks pass.";
        status.Controls.Add(pageTwoStatus, 0, 0);
    }

    private static TableLayoutPanel PageLayout(Panel page, string title, string subtitle, bool setup = false)
    {
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = setup
                ? new Padding(17, SetupPageLayout.LayoutTopPadding, 17, SetupPageLayout.LayoutBottomPadding)
                : new Padding(19, 14, 19, 12),
            ColumnCount = 1, RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
            setup ? SetupPageLayout.HeaderHeight : 91));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
            setup ? SetupPageLayout.ActionsHeight : 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,
            setup ? SetupPageLayout.StatusHeight : 51));
        page.Controls.Add(layout);

        var header = new Panel { Dock = DockStyle.Fill };
        var logo = new Label
        {
            Text = "G", Font = new Font("Segoe UI", setup ? 17f : 21f, FontStyle.Bold),
            ForeColor = Magenta, BackColor = PanelBg, TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 2), Size = setup ? new Size(47, 49) : new Size(55, 59),
            BorderStyle = BorderStyle.FixedSingle
        };
        var heading = new Label
        {
            Text = title, Font = new Font("Segoe UI Semibold", setup ? 16.5f : 18f, FontStyle.Bold),
            ForeColor = Cyan, Location = new Point(setup ? 60 : 69, 2), AutoSize = true
        };
        var sub = new Label
        {
            Text = subtitle, ForeColor = Muted,
            Location = new Point(setup ? 61 : 70, setup ? 36 : 42), AutoSize = true
        };
        var line = new Panel { BackColor = Border, Height = 1, Dock = DockStyle.Bottom };
        header.Controls.AddRange([logo, heading, sub, line]);
        layout.Controls.Add(header, 0, 0);

        if (setup)
        {
            // Page 1 is a three-row, fixed-layout form: never a scrollable flow.
            var compact = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                AutoScroll = false, Margin = Padding.Empty, Padding = Padding.Empty,
                BackColor = Bg
            };
            compact.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            compact.RowStyles.Add(new RowStyle(SizeType.Absolute, SetupPageLayout.NormalSourceHeight));
            compact.RowStyles.Add(new RowStyle(SizeType.Absolute, SetupPageLayout.InstructionsHeight));
            compact.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(compact, 0, 1);
        }
        else
        {
            // Page 2 retains its independently scrollable CET and REDscript reports.
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoScroll = true, BackColor = Bg, Padding = new Padding(0, 5, 5, 5)
            };
            flow.Resize += (_, _) =>
            {
                var width = Math.Max(330, flow.ClientSize.Width - 29);
                foreach (Control child in flow.Controls) child.Width = width;
            };
            layout.Controls.Add(flow, 0, 1);
        }
        layout.Controls.Add(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1,
            Margin = new Padding(0, setup ? 3 : 7, 0, 0)
        }, 0, 2);
        layout.Controls.Add(new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1,
            Margin = new Padding(0, setup ? 1 : 3, 0, 0)
        }, 0, 3);
        return layout;
    }

    private static GroupBox Section(string title, int height) => new()
    {
        Text = title, ForeColor = Cyan, BackColor = PanelBg,
        Font = new Font("Segoe UI Semibold", 9.5f),
        Height = height, Width = 940, Margin = new Padding(0, 2, 0, 12)
    };

    private static TextBox Entry() => new()
    {
        Dock = DockStyle.Fill, BackColor = PanelAlt, ForeColor = TextColor,
        BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9f)
    };

    private static TextBox ReportBox() => new()
    {
        Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
        BackColor = PanelAlt, ForeColor = TextColor, BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Consolas", 9f)
    };

    private static Label Readout() => new()
    {
        ForeColor = TextColor, BackColor = Color.Transparent,
        AutoSize = false, TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button BrowseAction()
    {
        var button = new Button
        {
            Text = "Browse...", FlatStyle = FlatStyle.Flat, BackColor = PanelAlt,
            ForeColor = TextColor, Font = new Font("Segoe UI", 9f),
            UseVisualStyleBackColor = false,
            TabStop = true
        };
        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = Border;
        return button;
    }

    private void ConfigureCetPathRow(Panel panel, TextBox field, Button browse, int top)
    {
        // One-pixel border: the inner surface covers everything except 1px.
        // The previous panel left a 4px solid magenta band around a borderless textbox.
        var outline = new Panel { BackColor = Magenta };
        var surface = new Panel { BackColor = PanelAlt, Margin = Padding.Empty };
        pathOutlines[field] = outline;
        field.AutoSize = false;
        field.Dock = DockStyle.None;
        field.Margin = Padding.Empty;
        field.Font = new Font("Consolas", 9f);
        field.BorderStyle = BorderStyle.None;
        field.BackColor = PanelAlt;
        browse.Dock = DockStyle.None;
        browse.Margin = Padding.Empty;

        void ArrangeField()
        {
            var height = Math.Min(19, field.PreferredHeight);
            field.SetBounds(4, Math.Max(0, (surface.ClientSize.Height - height) / 2),
                Math.Max(100, surface.ClientSize.Width - 8), height);
        }
        void ArrangeOutline()
        {
            surface.SetBounds(1, 1, Math.Max(1, outline.ClientSize.Width - 2),
                Math.Max(1, outline.ClientSize.Height - 2));
            ArrangeField();
        }
        void Arrange()
        {
            var width = panel.ClientSize.Width;
            outline.SetBounds(0, top + 2, Math.Max(180, width - 103), 26);
            browse.SetBounds(Math.Max(185, width - 92), top, 92,
                Math.Min(30, Math.Max(25, panel.ClientSize.Height - top)));
            ArrangeOutline();
        }
        surface.Controls.Add(field);
        outline.Controls.Add(surface);
        panel.Controls.AddRange([outline, browse]);
        outline.Resize += (_, _) => ArrangeOutline();
        surface.Resize += (_, _) => ArrangeField();
        panel.Resize += (_, _) => Arrange();
        Arrange();
    }

    private void SetPathOutline(TextBox field, bool verified)
    {
        if (pathOutlines.TryGetValue(field, out var outline))
            outline.BackColor = verified ? Border : Magenta;
    }

    private static Button Action(string text) => new NeonButton
    {
        Text = text, BackColor = PanelAlt, ForeColor = Cyan,
        Font = new Font("Segoe UI Semibold", 9f),
        Margin = new Padding(5, 3, 5, 3), Height = 42
    };

    private void ChooseSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Cyberpunk 2077 GAME FOLDER (the folder containing bin\\x64)",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(sourcePath.Text)
                ? sourcePath.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            sourcePath.Text = dialog.SelectedPath;
    }

    // Completed results have an identity independent of the profiler install
    // journal. Refresh on focus must never demote Page 2 to an unfinished
    // Page 1 state after the source managers have restored the originals.
    private void RefreshCompletedWorkspace()
    {
        if (!captureAndRestoreVerified || completedSessionId is null ||
            completedGameRoot is null) return;
        try
        {
            var saved = CollectedCaptureStore.ReadRestored(packageRoot, completedGameRoot,
                completedSessionId);
            resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
            cetReport.Text = saved.Cet is null ? "CET capture skipped." :
                "CET capture verified: " +
                CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "CET");
            redReport.Text = saved.Redscript is null ? "REDscript capture skipped." :
                "REDscript capture verified: " +
                CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "REDscript");
            redReport.Text += saved.FrameTimeCsv is null
                ? Environment.NewLine + "PresentMon: no frame-time capture."
                : Environment.NewLine + "PresentMon CSV verified: " + saved.FrameTimeCsv;
            var skipped = saved.Skipped ?? [];
            pageTwoStatus.Text = "Original game files restored. " +
                (skipped.Count == 0 ? "All captures collected. " :
                    "Skipped: " + string.Join(", ", skipped) + ". ") +
                "Analysis and generation are not yet integrated.";
            openResults.Enabled = true;
        }
        catch (Exception ex)
        {
            openResults.Enabled = false;
            analyze.Enabled = false;
            generate.Enabled = false;
            pageTwoStatus.Text = "Saved capture verification failed: " + ex.Message;
        }
    }

    private async Task RefreshSourceAsync()
    {
        if (captureAndRestoreVerified && pageTwo.Visible)
        {
            RefreshCompletedWorkspace();
            return;
        }
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        var cancel = scanCancellation = new CancellationTokenSource();
        var current = GameInspector.Inspect(sourcePath.Text);
        if (!mo2Mode.Checked && current.SourceKind == SourceKind.LiveCetMods &&
            !string.IsNullOrWhiteSpace(current.GameRoot) &&
            !current.GameRoot.Equals(sourcePath.Text, StringComparison.OrdinalIgnoreCase))
        {
            sourcePath.Text = current.GameRoot;
            return;
        }
        inspection = current;
        // Bindings are advisory; only actual owner CLI states can mark profilers installed.
        profilerSetup = ProfilerSetupView.Unverified(
            CaptureKeyInspector.ReadCetBinding(current.GameRoot));
        captureAndRestoreVerified = false;
        install.Enabled = launch.Enabled = collect.Enabled = next.Enabled = false;
        recoveryLink.Visible = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;
        UpdateWorkflowVisuals();
        UserSettings.WriteGameRoot(sourcePath.Text);

        // A discovered installation/INI alone NEVER proves the active mod stack.
        // We need a confirmed, selected profile; modlist.txt; the configured
        // Mods directory; every enabled package; and winning physical file paths.
        Mo2Stack? stack = null;
        var profile = mo2Profile.SelectedItem as string;
        var activeMo2 = Mo2FeatureGate.Enabled && mo2Mode.Checked;
        if (activeMo2 && current.IsValidGame)
        {
            var config = mo2Configuration;
            var gameRoot = current.GameRoot;
            try
            {
                stack = await Task.Run(
                    () => Mo2SourceInspector.Resolve(gameRoot, config, profile, cancel.Token),
                    cancel.Token);
            }
            catch (OperationCanceledException) { return; }
            if (cancel.IsCancellationRequested || IsDisposed) return;
        }

        mo2Effective = stack;
        // A missing modlist is NOT a verified MO2 profile; in that case four
        // source checks can still reflect the direct live game, with no MO2
        // files (including Overwrite) admitted to the effective map.
        var sourceAvailable = activeMo2 && stack?.Ready == true;
        var sourceVerified = sourceAvailable && stack!.ProfileVerified &&
            mo2Location.HasConfiguration && mo2Configuration.Recognized;

        if (activeMo2)
        {
            var checks = new[]
            {
                sourceVerified,
                sourceAvailable && stack!.HasCetAsi,
                sourceAvailable && stack!.CetOwners > 0,
                sourceAvailable && stack!.HasRedscript,
                sourceAvailable && stack!.Red4extFiles > 0
            };
            for (var i = 0; i < checks.Length; i++)
                mo2ComponentChecks[i].Checked = checks[i];

            if (sourceAvailable)
            {
                var reds = RedscriptModCounter.CountEffective(stack);
                var origin = stack!.GameOnlyFallback
                    ? "Live game only — MO2 modlist unavailable"
                    : $"MO2 {profile}: {stack.EnabledPackages} enabled packages";
                sourceStatus.Text = $"{origin} · {stack.CetOwners} CET mods · " +
                    $"{reds} REDscript groups · {stack.ShadowedFiles} file conflicts";
            }
            else
                sourceStatus.Text = !current.IsValidGame
                    ? "Select the Cyberpunk 2077 Game root - /Cyberpunk2077"
                    : stack?.Detail ?? "Select the MO2 profile or inspect the live game files.";

            var engineMissing = sourceAvailable && stack!.CetOwners > 0 && !stack.HasZeroEngine;
            zeroEngineLink.Visible = engineMissing;
            engineMissingNotice.Visible = engineMissing;
            pageOneStatus.Text = sourceVerified
                ? "Physical game and enabled MO2 file winners resolved; USVFS/RootBuilder remains unverified."
                : sourceAvailable
                    ? "Live game scanned. MO2 packages excluded until modlist and profile prove active files."
                    : "No trusted live source. Check the selected physical game folder.";
        }
        else
        {
            var display = SetupSourcePresentation.ForNormal(
                current, RedscriptModCounter.CountPhysical(current.GameRoot));
            sourceStatus.Text = display.StatusText;
            var engine = !display.ShowEngineLink && display.GamePathConfirmed;
            var scripts = current.Checks.Any(c => c.Name == "REDscript source folder" &&
                c.State != GTotalOptimizer.Core.CheckState.Missing);
            var plugins = current.Checks.Any(c => c.Name == "RED4ext plugin folder" &&
                c.State != GTotalOptimizer.Core.CheckState.Missing);
            ApplyCheck(zeroEngineCheck,
                "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine", engine);
            ApplyCheck(scriptsCheck, "r6/scripts", scripts);
            ApplyCheck(red4extCheck, "red4ext/plugins", plugins);
            zeroEngineLink.Visible = display.ShowEngineLink;
            engineMissingNotice.Visible = false;
            pageOneStatus.Text = current.IsValidGame
                ? "Game detected. Profiler installation and restoration are not yet integrated."
                : "Select a valid Cyberpunk 2077 game folder.";
        }
        // Only missing paths/prompts are highlighted. Valid status text is white;
        // the dedicated check/X glyphs carry green/red status.
        sourceStatus.ForeColor = activeMo2
            ? sourceVerified ? TextColor : Amber
            : SetupSourcePresentation.ForNormal(current,
                RedscriptModCounter.CountPhysical(current.GameRoot)).IsWarning
                ? Amber : TextColor;
        gamePathTick.Visible = current.IsValidGame;
        LayoutEngineWarning();
        SetPathOutline(sourcePath, current.IsValidGame);
        SetPathOutline(mo2GameRoot, current.IsValidGame);
        SetPathOutline(mo2Folder, mo2Location.HasInstallation);

        if (!current.IsValidGame || cancel.IsCancellationRequested) return;
        var bundle = ComponentBundleVerifier.Inspect(packageRoot);
        if (!bundle.Ready)
        {
            packagePathFailure = bundle.Detail + Environment.NewLine + Environment.NewLine +
                PortablePackagePaths.RuntimePathDiagnostic(packageRoot);
            pageOneStatus.Text = "Profiler package verification blocked. Click here for actual executable, root and manifest paths.";
            pageOneStatus.Cursor = Cursors.Hand;
            return;
        }
        packagePathFailure = null;
        pageOneStatus.Cursor = Cursors.Default;
        try
        {
            var status = await Workflow.InspectAsync(current.GameRoot!, cancel.Token);
            if (cancel.IsCancellationRequested || IsDisposed ||
                (captureAndRestoreVerified && pageTwo.Visible)) return;
            profilerSetup = new ProfilerSetupView(
                status.Cet == "PROFILER_ACTIVE" && status.CetManaged,
                status.Grsp == "INSTALLED_CURRENT" && status.GrspManaged,
                true, CaptureKeyInspector.InspectCet(current.GameRoot, true),
                new CaptureKeyCheck("PresentMon", "AUTO", true, "Automatic recording; no hotkey registered."));
            UpdateWorkflowVisuals();
            var running = Process.GetProcessesByName("Cyberpunk2077").Length != 0;
            var session = Workflow.HasRecoveryJournal;
            if (!running) captureLaunched = false;
            CollectedCaptureIndex? saved = session ? Workflow.ReadCollected(current.GameRoot!) : null;
            var trackedFrameCsv = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            var sameFrameSession = saved is not null && trackedFrameCsv is not null &&
                Path.GetFullPath(trackedFrameCsv).StartsWith(
                    CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId) +
                    Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            var nativeReady = status.CanCollect(session, running) &&
                ((status.CetCaptureReady && saved?.Cet is null) ||
                 (status.GrspCaptureReady && saved?.Redscript is null));
            var readyToCollect = nativeReady || session && !running && sameFrameSession;
            ApplyWorkflowStage(SetupWorkflowProgress.Resolve(
                status.BothInstalled, session, readyToCollect, saved?.HasAny == true));
            install.Enabled = !busy && status.CleanForInstall && !session;
            launch.Enabled = !busy && status.CanLaunch(session, running) &&
                !status.BothCaptured && !captureLaunched && trackedFrameCsv is null;
            collect.Enabled = !busy && readyToCollect;
            if (!busy && saved?.HasAny == true)
            {
                next.Enabled = true;
                resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
                openResults.Enabled = true;
            }
            recoveryLink.Visible = !busy && session && !running;
            recoveryLink.Text = saved?.HasAny == true
                ? "RESTORE ORIGINALS (CANCEL)" : "RESTORE WITHOUT CAPTURE";
            pageOneStatus.Text = running
                ? "Cyberpunk running. Press F11 twice; close game before Collect."
                : saved?.HasAny == true
                    ? "Results saved. Continue restores originals; missing captures require explicit skips."
                    : readyToCollect
                        ? "Collect ends PresentMon and copies completed captures; originals remain installed."
                        : status.BothInstalled
                            ? "Profilers installed. No completed captures yet."
                            : session
                                ? "Session requires recovery. Restore Originals when game is closed."
                                : $"CET: {status.Cet}  |  REDscript: {status.Grsp}  |  PresentMon: auto";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!cancel.IsCancellationRequested && !IsDisposed)
                pageOneStatus.Text = "Owner status unavailable: " + ex.Message;
        }
    }

    private static void ApplyCheck(SourcePathCheck label, string path, bool present)
    {
        label.SetStatus(path, present);
    }

    private void LayoutEngineWarning()
    {
        if (sourceSection is null || mo2Mode.Checked) return;
        // Keep the yellow missing-dependency message and link adjacent.
        var width = Math.Max(350, sourceSection.ClientSize.Width - 28);
        var textWidth = TextRenderer.MeasureText(sourceStatus.Text, sourceStatus.Font,
            new Size(width, 22), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width;
        sourceStatus.Width = Math.Min(width, textWidth + 4);
        zeroEngineLink.SetBounds(sourceStatus.Left + sourceStatus.Width + 8,
            sourceStatus.Top, Math.Min(280, Math.Max(100, width - sourceStatus.Width - 12)), 20);
    }


    private void SetWorkflowBusy(bool value)
    {
        busy = value;
        sourcePath.Enabled = !value;
        install.Enabled = !value && install.Enabled;
        next.Enabled = !value && next.Enabled;
        launch.Enabled = !value && launch.Enabled;
        collect.Enabled = !value && collect.Enabled;
        recoveryLink.Enabled = !value;
    }

    private async Task InstallProfilersAsync()
    {
        if (busy || !inspection.IsValidGame || string.IsNullOrWhiteSpace(inspection.GameRoot))
            return;
        var game = inspection.GameRoot!;
        SetWorkflowBusy(true);
        pageOneStatus.Text = "Installing CET and REDscript through their existing managers...";
        try
        {
            var result = await Workflow.InstallAsync(game);
            if (IsDisposed) return;
            if (result.Ok) captureLaunched = false;
            await RefreshSourceAsync();
            if (IsDisposed) return;
            pageOneStatus.Text = result.Detail;
            if (!result.Ok)
                MessageBox.Show(this, result.Detail, "Profiler installation", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, ex.Message, "Profiler installation",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                SetWorkflowBusy(false);
                await RefreshSourceAsync();
            }
        }
    }

    private async Task LaunchCaptureAsync()
    {
        if (busy || captureLaunched || Process.GetProcessesByName("Cyberpunk2077").Length != 0 ||
            !inspection.IsValidGame || string.IsNullOrWhiteSpace(inspection.GameRoot))
            return;
        var game = inspection.GameRoot!;
        SetWorkflowBusy(true);
        try
        {
            var state = await Workflow.InspectAsync(game);
            if (!state.BothInstalled || !Workflow.HasRecoveryJournal)
                throw new InvalidOperationException("Both native profilers and the GCO session must be installed before game launch.");

            var gameExe = Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe");
            if (!File.Exists(gameExe))
                throw new FileNotFoundException("Cyberpunk2077.exe is missing.", gameExe);
            if (!ComponentBundleVerifier.Inspect(packageRoot).Ready)
                throw new InvalidOperationException("Combined component provenance is no longer valid.");
            var capture = Workflow.ReadCollected(game) ??
                throw new InvalidOperationException("GCO capture session identity is unavailable.");
            string? frameWarning = null;
            try
            {
                // PresentMon's native CSV writer uses a MAX_PATH buffer. A
                // path-length refusal must disable only optional frame timing,
                // not prevent CET and REDscript from launching the game.
                var csv = PresentMonLifecyclePolicy.NewSessionCsv(packageRoot, capture.SessionId);
                // PresentMon records from launch without RegisterHotKey.
                // F11 remains solely CET + REDscript input.
                await PresentMonCaptureController.RunAsync(packageRoot, csv, restart: true);
            }
            catch (Exception ex)
            {
                frameWarning = ex.Message;
            }
            try
            {
                _ = Process.Start(new ProcessStartInfo(gameExe)
                {
                    UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(gameExe)!
                }) ?? throw new InvalidOperationException("Cyberpunk did not start.");
            }
            catch
            {
                // If launching the game fails, do not abandon a live ETW
                // session. Preserve its tracked path if termination fails.
                var tracked = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
                if (tracked is not null)
                {
                    await PresentMonCaptureController.RunAsync(packageRoot, tracked, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                }
                throw;
            }
            captureLaunched = true;
            pageOneStatus.Text = frameWarning is null
                ? "PresentMon recording automatically. F11 starts/stops CET + REDscript. Collect ends PresentMon."
                : "Game launched. PresentMon failed to start; CET + REDscript remain usable.";
            if (frameWarning is not null)
                MessageBox.Show(this, "PresentMon unavailable: " + frameWarning +
                    Environment.NewLine + "The game launched with CET + REDscript. Frame-time can be skipped.",
                    "Optional frame-time capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
                MessageBox.Show(this, ex.Message, "Launch/capture", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            if (!IsDisposed)
            {
                busy = false;
                sourcePath.Enabled = true;
                launch.Enabled = Workflow.HasRecoveryJournal && !captureLaunched &&
                    Process.GetProcessesByName("Cyberpunk2077").Length == 0;
                collect.Enabled = false;
            }
        }
    }

    private async Task CollectResultsAsync()
    {
        if (busy || !inspection.IsValidGame || string.IsNullOrWhiteSpace(inspection.GameRoot)) return;
        var game = inspection.GameRoot!;
        if (Process.GetProcessesByName("Cyberpunk2077").Length != 0)
        {
            MessageBox.Show(this, "Close Cyberpunk before Collect.", "Game running",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        SetWorkflowBusy(true);
        pageOneStatus.Text = "Stopping PresentMon and copying captured data to RESULTS. No restoration...";
        try
        {
            var current = Workflow.ReadCollected(game);
            if (current is null)
                throw new InvalidOperationException("No current GCO installation session.");
            string? frameWarning = null;
            var tracked = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            if (tracked is not null)
            {
                try
                {
                    var expectedRoot = CollectedCaptureStore.SessionFolder(packageRoot, current.SessionId) +
                        Path.DirectorySeparatorChar;
                    if (!Path.GetFullPath(tracked).StartsWith(expectedRoot, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Tracked frame-time data belongs to another session.");
                    // Collect is the explicit and ONLY normal PresentMon stop.
                    await PresentMonCaptureController.RunAsync(packageRoot, tracked, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                    try
                    {
                        CollectedCaptureStore.CollectPresentMon(packageRoot, game, current.SessionId, tracked);
                    }
                    catch (Exception ex)
                    {
                        frameWarning = "PresentMon stopped but no usable Cyberpunk CSV: " + ex.Message;
                    }
                }
                catch (Exception ex)
                {
                    frameWarning = "PresentMon stop failed: " + ex.Message +
                        ". Retry Collect before Continue.";
                }
            }
            // CET and GRSP are collected independently through their original
            // managers, without restoring either game's native payload.
            var result = await Workflow.CollectResultsAsync(game);
            var saved = Workflow.ReadCollected(game);
            if (saved?.HasAny == true)
            {
                resultsFolder = CollectedCaptureStore.SessionFolder(packageRoot, saved.SessionId);
                cetReport.Text = saved.Cet is null ? "CET capture not available." :
                    "CET copied to " + CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "CET");
                redReport.Text = saved.Redscript is null ? "REDscript capture not available." :
                    "REDscript copied to " + CollectedCaptureStore.DomainFolder(packageRoot, saved.SessionId, "REDscript");
                redReport.Text += saved.FrameTimeCsv is null
                    ? Environment.NewLine + "PresentMon: not available / explicit skip required."
                    : Environment.NewLine + "PresentMon CSV: " + saved.FrameTimeCsv +
                      " (time alignment pending)";
            }
            pageOneStatus.Text = result.Detail + (frameWarning is null ? "" : " " + frameWarning);
            // Only announce missing capture domains. The status strip retains
            // diagnostics, while Continue owns explicit skip acknowledgments.
            // A complete capture refreshes silently into Continue.
            var missingNotice = CaptureCollectionNotice.MissingCaptures(saved);
            if (missingNotice is not null)
                MessageBox.Show(this, missingNotice, "Missing captures",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (Exception ex)
        {
            if (!IsDisposed) MessageBox.Show(this, ex.Message, "Capture collection",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                busy = false;
                sourcePath.Enabled = true;
                await RefreshSourceAsync();
            }
        }
    }

    private HashSet<string>? ConfirmMissingCaptureSkips(CollectedCaptureIndex saved)
    {
        var missing = new List<(string Id, string Description)>();
        if (saved.Cet is null) missing.Add(("CET", "CET — skip CET capture-based optimizations"));
        if (saved.Redscript is null) missing.Add(("REDscript", "REDscript — skip REDscript capture-based optimizations"));
        if (saved.FrameTimeCsv is null) missing.Add(("FrameTime", "Frame-time — skip FPS/hitch correlation"));
        if (missing.Count == 0) return new HashSet<string>();
        using var dialog = new Form
        {
            Text = "Missing capture evidence", StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false,
            MinimizeBox = false, ClientSize = new Size(610, 145 + missing.Count * 33),
            BackColor = PanelBg, ForeColor = TextColor
        };
        dialog.Controls.Add(new Label
        {
            Text = "The following capture data is missing. Check EACH domain to proceed without it.",
            Left = 20, Top = 14, Width = 565, Height = 38, ForeColor = Amber
        });
        var options = new CheckedListBox
        {
            Left = 20, Top = 55, Width = 560, Height = missing.Count * 34,
            CheckOnClick = true, BackColor = PanelAlt, ForeColor = TextColor,
            BorderStyle = BorderStyle.None, Font = Font
        };
        foreach (var item in missing) options.Items.Add(item.Description);
        dialog.Controls.Add(options);
        var proceed = new Button
        {
            Text = "RESTORE AND CONTINUE", Width = 220, Height = 31,
            Left = 365, Top = dialog.ClientSize.Height - 44
        };
        proceed.Click += (_, _) =>
        {
            if (options.CheckedItems.Count != options.Items.Count)
            {
                MessageBox.Show(dialog, "Check every missing capture you agree to skip.",
                    "Explicit confirmation required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            dialog.DialogResult = DialogResult.OK;
            dialog.Close();
        };
        dialog.Controls.Add(proceed);
        if (dialog.ShowDialog(this) != DialogResult.OK) return null;
        return missing.Select(m => m.Id).ToHashSet(StringComparer.Ordinal);
    }

    private async Task ContinueToAnalyticsAsync()
    {
        if (busy) return;
        if (captureAndRestoreVerified)
        {
            RefreshCompletedWorkspace();
            pageOne.Visible = false; pageTwo.Visible = true; pageTwo.BringToFront();
            return;
        }
        if (!inspection.IsValidGame || string.IsNullOrWhiteSpace(inspection.GameRoot)) return;
        var game = inspection.GameRoot!;
        if (Process.GetProcessesByName("Cyberpunk2077").Length != 0)
        {
            MessageBox.Show(this, "Close Cyberpunk before restoring.", "Game running",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        CollectedCaptureIndex? saved;
        try { saved = Workflow.ReadCollected(game, verifyHashes: true); }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Evidence verification failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (saved?.HasAny != true) return;
        if (PresentMonCaptureController.ReadTrackedCsv(packageRoot) is not null)
        {
            MessageBox.Show(this, "PresentMon must be stopped with COLLECT RESULTS before Continue.",
                "Frame-time capture still active", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var skip = ConfirmMissingCaptureSkips(saved);
        if (skip is null) return;
        SetWorkflowBusy(true);
        next.Enabled = false;
        try
        {
            var result = await Workflow.ContinueAsync(game,
                skip.Contains("CET"), skip.Contains("REDscript"), skip.Contains("FrameTime"));
            if (!result.RestoreVerified)
            {
                MessageBox.Show(this, result.Detail, "Restoration blocked",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                pageOneStatus.Text = result.Detail;
                return;
            }
            // The owner journal is intentionally removed by Continue.
            // This saved evidence identity, not the journal, owns Page 2.
            completedSessionId = saved.SessionId;
            completedGameRoot = game;
            captureAndRestoreVerified = true;
            scanCancellation?.Cancel();
            RefreshCompletedWorkspace();
            pageOne.Visible = false; pageTwo.Visible = true; pageTwo.BringToFront();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Continuation failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (!IsDisposed)
            {
                busy = false;
                sourcePath.Enabled = true;
                next.Enabled = captureAndRestoreVerified;
            }
        }
    }

    private async Task RestoreIncompleteSessionAsync()
    {
        if (busy || !inspection.IsValidGame || string.IsNullOrWhiteSpace(inspection.GameRoot) ||
            !Workflow.HasRecoveryJournal)
            return;
        if (Process.GetProcessesByName("Cyberpunk2077").Length != 0)
        {
            MessageBox.Show(this, "Close Cyberpunk before restoring the profilers.",
                "Game running", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var game = inspection.GameRoot!;
        LiveProfilerState state;
        try { state = await Workflow.InspectAsync(game); }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Cannot verify current owners: " + ex.Message,
                "Restore status", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (MessageBox.Show(this,
                "End this profiling session and restore originals now? Already collected RESULTS remain intact. " +
                "Incomplete live diagnostics will be preserved for recovery.",
                "Restore incomplete profiler session", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) != DialogResult.Yes)
            return;

        SetWorkflowBusy(true);
        try
        {
            var result = await Workflow.RestoreOwnedAsync(game);
            var csv = PresentMonCaptureController.ReadTrackedCsv(packageRoot);
            string? pmError = null;
            if (csv is not null)
            {
                try
                {
                    await PresentMonCaptureController.RunAsync(packageRoot, csv, restart: false);
                    PresentMonCaptureController.ClearTrackedCsv(packageRoot);
                }
                catch (Exception ex) { pmError = ex.Message; }
            }
            captureLaunched = false;
            MessageBox.Show(this, result.Detail +
                (pmError is null ? "" : Environment.NewLine + "PresentMon: " + pmError),
                "Owner-managed restoration", MessageBoxButtons.OK,
                result.RestoreVerified ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        finally
        {
            busy = false;
            await RefreshSourceAsync();
        }
    }

    private void ExplainPending(string message) =>
        MessageBox.Show(this, message, "Integration safety gate", MessageBoxButtons.OK, MessageBoxIcon.Information);

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        scanCancellation?.Cancel();
        scanCancellation?.Dispose();
        base.OnFormClosed(e);
    }
}

// Single owner-painted source status: path and NOT FOUND remain white,
// while the X / tick is rendered next to the actual text in red / green.
// This avoids native Label transparency/z-order hiding the status glyph.
internal sealed class SourcePathCheck : Control
{
    private string displayPath = "";
    private bool present;

    internal void SetStatus(string path, bool found)
    {
        displayPath = path;
        present = found;
        Invalidate();
    }

    public SourcePathCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var message = displayPath + (present ? "" : "  NOT FOUND");
        var flags = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        var size = TextRenderer.MeasureText(e.Graphics, message, Font,
            new Size(2048, Math.Max(20, Height)), flags);
        TextRenderer.DrawText(e.Graphics, message, Font,
            new Rectangle(0, 0, Math.Min(Width, size.Width), Height), ForeColor, flags);
        var iconX = size.Width + 6;
        if (iconX + 20 <= Width)
        {
            TextRenderer.DrawText(e.Graphics, present ? "✓" : "✕", Font,
                new Rectangle(iconX, 0, 20, Height),
                present ? Color.FromArgb(94, 255, 130) : Color.FromArgb(255, 95, 112),
                flags);
        }
    }
}

// User-owned source data stays read-only. Only the indicator changes color:
// green tick = found in verified winning-file map; red X = missing/unverified.
// Avoid the black/gray OS checkbox foreground on the CET dark background.
internal sealed class SourceStatusCheck : CheckBox
{
    public SourceStatusCheck()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        AutoCheck = false;
        TabStop = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        var tint = Checked ? Color.FromArgb(94, 255, 130)
                           : Color.FromArgb(255, 95, 112);
        var glyphRect = new Rectangle(0, 0, 20, ClientSize.Height);
        var textRect = new Rectangle(21, 0, Math.Max(0, ClientSize.Width - 21), ClientSize.Height);
        var align = TextFormatFlags.VerticalCenter | TextFormatFlags.Left |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix;
        TextRenderer.DrawText(e.Graphics, Checked ? "✓" : "✕",
            Font, glyphRect, tint, align);
        TextRenderer.DrawText(e.Graphics, Text, Font, textRect, ForeColor, align);
    }
}

// Draw disabled labels explicitly: standard WinForms renders them black on dark backgrounds.
internal sealed class NeonButton : Button
{
    internal bool IsAccent;
    private bool hovered;
    public NeonButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
    }
    protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
    protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var bg = Enabled
            ? hovered ? Color.FromArgb(20, 42, 52) : Color.FromArgb(11, 18, 25)
            : Color.FromArgb(18, 25, 31);
        var line = Enabled
            ? (IsAccent ? Color.FromArgb(255, 63, 215) : Color.FromArgb(54, 244, 244))
            : Color.FromArgb(49, 61, 68);
        var fg = Enabled ? (IsAccent ? Color.FromArgb(255, 63, 215)
                                      : Color.FromArgb(54, 244, 244))
                         : Color.FromArgb(104, 118, 126);
        using var fill = new SolidBrush(bg);
        using var border = new Pen(line, 1f);
        e.Graphics.FillRectangle(fill, ClientRectangle);
        e.Graphics.DrawRectangle(border, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, fg,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
    }
}

internal static class UserSettings
{
    internal sealed record Mo2Setup(string InstallationFolder, string InstanceFolder, string Profile);

    private sealed class Settings
    {
        public string GameRoot { get; set; } = "";
        public string Mo2InstallationFolder { get; set; } = "";
        public string Mo2InstanceFolder { get; set; } = "";
        // Old versions stored a manual Mods-directory override. Retained for
        // backward deserialization only; the INI always supplies this path.
        public string Mo2ModsOverride { get; set; } = "";
        public string Mo2Profile { get; set; } = "";
        // Old per-source-folder settings are retained only for backward
        // deserialization; they never grant MO2 enabled-mod verification.
        public string[] Mo2SourceFolders { get; set; } = [];
    }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G-Total-Optimizer", "settings.json");

    private static Settings Read()
    {
        try { return File.Exists(SettingsFile)
            ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings()
            : new Settings(); }
        catch { return new Settings(); }
    }

    internal static string ReadGameRoot() => Read().GameRoot;
    internal static Mo2Setup ReadMo2Setup()
    {
        var s = Read();
        return new(s.Mo2InstallationFolder, s.Mo2InstanceFolder, s.Mo2Profile);
    }
    internal static void WriteGameRoot(string value)
    {
        var s = Read(); s.GameRoot = value; Write(s);
    }
    internal static void WriteMo2Setup(string installation, string instance, string profile)
    {
        var s = Read();
        s.Mo2InstallationFolder = installation;
        s.Mo2InstanceFolder = instance;
        s.Mo2Profile = profile;
        Write(s);
    }

    private static void Write(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings));
            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch { /* Preferences must never modify game data. */ }
    }
}
