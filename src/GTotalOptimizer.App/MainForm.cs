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
    private readonly Button collect = Action("3  COLLECT RESULTS / RESTORE");
    private readonly Button next = Action("CONTINUE  →");
    private readonly Button analyze = Action("1  ANALYZE CAPTURE");
    private readonly Button generate = Action("2  GENERATE OPTIMIZATION PASS");
    private readonly Button openResults = Action("3  OPEN RESULTS FOLDER");
    private readonly BackendStatusRunner backend = new();
    private CancellationTokenSource? scanCancellation;
    private GameInspection inspection = GameInspector.Inspect(null);
    private string? resultsFolder;
    private bool captureAndRestoreVerified;
    private readonly string packageRoot = PortablePackagePaths.RootFromAppDirectory(AppContext.BaseDirectory);

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
        install.Click += (_, _) => ExplainPending("Profiler installation requires exact, independently verified CET and GRSP packages and transactional rollback tests. No game file has been changed.");
        launch.Click += (_, _) => ExplainPending("Game launch and synchronized capture are gated until profiler installation and F11 status are verified.");
        collect.Click += (_, _) => ExplainPending("Collection and restoration require a completed current-session capture and both component-owned restore contracts.");
        next.Click += (_, _) =>
        {
            if (!captureAndRestoreVerified) return;
            pageOne.Visible = false;
            pageTwo.Visible = true;
            pageTwo.BringToFront();
        };
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
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP",
            "Install the profilers, record gameplay, then collect results and restore.", setup: true);
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
        stepOne.Checked = profilerSetup.CetInstalled;
        stepTwo.Checked = profilerSetup.RedscriptInstalled;
        stepThree.Checked = profilerSetup.CapXInstalled;
        cetCaptureKey.Text = profilerSetup.CetKeyDisplay;
        redCaptureKey.Text = "F11 (fixed)";
        capxCaptureKey.Text = profilerSetup.CapXKeyDisplay;
        var warning = profilerSetup.SynchronizationWarning;
        captureKeyWarning.Text = warning ?? "";
        captureKeyWarning.Visible = warning is not null;
        // Keybind mismatch is advisory; never change workflow progression.
        next.Enabled = captureAndRestoreVerified;
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
        status.ColumnCount = 2;
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var back = Action("← BACK");
        back.Click += (_, _) => { pageTwo.Visible = false; pageOne.Visible = true; pageOne.BringToFront(); };
        back.Dock = DockStyle.Fill;
        status.Controls.Add(back, 0, 0);
        pageTwoStatus.Dock = DockStyle.Fill;
        pageTwoStatus.Text = "Generation is blocked until the complete source, dependency and compiler checks pass.";
        status.Controls.Add(pageTwoStatus, 1, 0);
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

    private async Task RefreshSourceAsync()
    {
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
        // The current shell has no staged/verified native profiler payloads.
        // Read the actual CET key from live bindings if available, but NEVER
        // grant an installed check from a binding, directory or loose ASI.
        profilerSetup = ProfilerSetupView.Unverified(
            CaptureKeyInspector.ReadCetBinding(current.GameRoot));
        captureAndRestoreVerified = false;
        install.Enabled = launch.Enabled = collect.Enabled = next.Enabled = false;
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

        var cetExe = BackendLocator.Executable(packageRoot, BackendKind.Cet);
        var redExe = BackendLocator.Executable(packageRoot, BackendKind.Grsp);
        if (!current.IsValidGame || cancel.IsCancellationRequested) return;
        try
        {
            var statuses = await Task.WhenAll(
                backend.ReadAsync(cetExe, current.GameRoot!, BackendKind.Cet, cancel.Token),
                backend.ReadAsync(redExe, current.GameRoot!, BackendKind.Grsp, cancel.Token));
            if (cancel.IsCancellationRequested || IsDisposed) return;
            pageOneStatus.Text += "  " + string.Join(" | ", statuses.Select(x =>
                $"{x.Kind}: {(x.Responded ? "status available" : "not staged")}"));
        }
        catch (OperationCanceledException) { }
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
