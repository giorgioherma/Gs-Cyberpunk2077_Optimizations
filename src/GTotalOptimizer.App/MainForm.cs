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
    private readonly TextBox[] mo2SourceFields = [Entry(), Entry(), Entry(), Entry()];
    private readonly Panel[] mo2SourceRows = [new(), new(), new(), new()];
    private readonly CheckBox[] mo2ComponentChecks =
    [
        SourceCheck("CET ASI"), SourceCheck("CET mods"),
        SourceCheck("r6/scripts"), SourceCheck("red4ext/plugins")
    ];
    private ModSourceCoverage? mo2Coverage;
    private bool loadingMo2Sources;
    private readonly RadioButton normalMode = ModeChoice("NORMAL", true);
    private readonly RadioButton mo2Mode = ModeChoice("MO2", false);
    private readonly Panel normalPanel = new();
    private readonly Panel mo2Panel = new() { Visible = false };
    private TableLayoutPanel? setupContent;
    private readonly Label zeroEngineCheck = Readout();
    private readonly Label scriptsCheck = Readout();
    private readonly Label red4extCheck = Readout();
    private readonly LinkLabel zeroEngineLink = new()
    {
        Text = "0-Engine must be installed!  Open Nexus Mods",
        LinkColor = Cyan, ActiveLinkColor = Magenta, VisitedLinkColor = Cyan,
        AutoSize = false, TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label profilerReady = Readout();
    private readonly Label stepOne = Readout();
    private readonly Label stepTwo = Readout();
    private readonly Label stepThree = Readout();
    private GroupBox? sourceSection;
    private readonly Label sourceStatus = Readout();
    private readonly Label cetKey = Readout();
    private readonly Label redKey = Readout();
    private readonly Label capKey = Readout();
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
    private readonly string packageRoot = AppContext.BaseDirectory;

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

        sourcePath.TextChanged += async (_, _) => await RefreshSourceAsync();
        for (var i = 0; i < mo2SourceFields.Length; i++)
        {
            mo2SourceFields[i].TextChanged += async (_, _) =>
            {
                if (loadingMo2Sources) return;
                UserSettings.WriteMo2Sources(mo2SourceFields.Select(field => field.Text).ToArray());
                await RefreshSourceAsync();
            };
        }
        normalMode.CheckedChanged += async (_, _) =>
        {
            if (!normalMode.Checked) return;
            SetSourceMode();
            await RefreshSourceAsync();
        };
        mo2Mode.CheckedChanged += async (_, _) =>
        {
            if (!mo2Mode.Checked) return;
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
        loadingMo2Sources = true;
        try
        {
            var savedSources = UserSettings.ReadMo2Sources();
            for (var i = 0; i < mo2SourceFields.Length && i < savedSources.Length; i++)
                mo2SourceFields[i].Text = savedSources[i];
        }
        finally { loadingMo2Sources = false; }
        sourcePath.Text = UserSettings.ReadGameRoot();
        SetSourceMode();
        Shown += async (_, _) => await RefreshSourceAsync();
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP",
            "Install the profilers, record gameplay, then collect results and restore.", setup: true);
        setupContent = (TableLayoutPanel)grid.GetControlFromPosition(0, 1)!;

        sourceSection = Section("Game folder /Cyberpunk2077", SetupPageLayout.NormalSourceHeight);
        sourceSection.Dock = DockStyle.Fill;
        sourceSection.Margin = new Padding(0, 2, 0, 4);

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

        // MO2 source discovery is content-based: any one folder may contain all
        // four targets, or the user can supply two, three, or four unrelated folders.
        // Only reveal another selector while a component is missing.
        mo2Panel.SetBounds(14, 55, 880, 116);
        for (var i = 0; i < mo2SourceFields.Length; i++)
        {
            var slot = i;
            var row = mo2SourceRows[i];
            row.SetBounds(0, i * 29, 880, 29);
            row.Visible = i == 0;
            var browseMo2 = BrowseAction();
            ConfigureCetPathRow(row, mo2SourceFields[i], browseMo2, 0);
            browseMo2.Click += (_, _) =>
            {
                using var picker = new FolderBrowserDialog
                {
                    Description = "Select a folder containing CET ASI, CET mods, REDscript or RED4ext files",
                    UseDescriptionForTitle = true,
                    SelectedPath = Directory.Exists(mo2SourceFields[slot].Text)
                        ? mo2SourceFields[slot].Text
                        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
                };
                if (picker.ShowDialog(this) == DialogResult.OK)
                    mo2SourceFields[slot].Text = picker.SelectedPath;
            };
            mo2Panel.Controls.Add(row);
        }
        mo2Panel.Resize += (_, _) =>
        {
            foreach (var row in mo2SourceRows)
                row.Width = Math.Max(350, mo2Panel.ClientSize.Width);
        };
        sourceSection.Controls.Add(mo2Panel);

        foreach (var check in mo2ComponentChecks)
        {
            check.ForeColor = TextColor;
            check.Font = new Font("Segoe UI", 9f);
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
        zeroEngineLink.Font = new Font("Segoe UI", 8.7f);
        sourceSection.Controls.Add(zeroEngineLink);

        sourceSection.Resize += (_, _) =>
        {
            var width = Math.Max(350, sourceSection.ClientSize.Width - 28);
            tabs.Width = normalPanel.Width = mo2Panel.Width = width;
            sourceStatus.Width = width;
            for (var i = 0; i < mo2ComponentChecks.Length; i++)
            {
                mo2ComponentChecks[i].Left = 14 + (width * i / 4);
                mo2ComponentChecks[i].Width = width / 4 - 3;
            }
            zeroEngineCheck.Width = Math.Min(520, width);
            zeroEngineLink.Width = Math.Max(190, width - zeroEngineLink.Left);
            red4extCheck.Width = Math.Max(180, width - red4extCheck.Left);
        };
        setupContent.Controls.Add(sourceSection, 0, 0);

        var guide = Section("INSTRUCTIONS", SetupPageLayout.InstructionsHeight);
        guide.Dock = DockStyle.Fill;
        guide.Margin = new Padding(0, 2, 0, 4);

        var top = Readout();
        top.Text = "Start Cyberpunk 2077 and load the world. Press F11 to START; press F11 again to STOP.";
        top.ForeColor = TextColor;
        top.Font = new Font("Segoe UI Semibold", 9.7f, FontStyle.Bold);
        top.SetBounds(15, 23, 890, 23);
        guide.Controls.Add(top);

        cetKey.SetBounds(16, 47, 285, 22);
        redKey.SetBounds(307, 47, 285, 22);
        capKey.SetBounds(598, 47, 290, 22);
        foreach (var key in new[] { cetKey, redKey, capKey })
        {
            key.Font = new Font("Segoe UI Semibold", 9.3f, FontStyle.Bold);
            key.ForeColor = TextColor;
            guide.Controls.Add(key);
        }

        var conflicts = Readout();
        conflicts.Text = "Temporarily unbind other mods or game controls using F11 (CET and Settings).";
        conflicts.ForeColor = TextColor;
        conflicts.Font = new Font("Segoe UI", 9.2f);
        conflicts.SetBounds(15, 70, 890, 21);
        guide.Controls.Add(conflicts);

        var headline = Readout();
        headline.Text = "RECORD 5–10 MINUTES IN THE CITY";
        headline.ForeColor = Green;
        headline.Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold);
        headline.SetBounds(15, 95, 890, 29);
        guide.Controls.Add(headline);

        var phases = Readout();
        phases.Text = "IDLE   •   MOVEMENT   •   DRIVING   •   HEAVY COMBAT";
        phases.ForeColor = Green;
        phases.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        phases.SetBounds(15, 123, 890, 25);
        guide.Controls.Add(phases);

        var avoid = Readout();
        avoid.Text = "AVOID MENUS, INVENTORY, MAP, CET UI AND LOADING SCREENS";
        avoid.ForeColor = TextColor;
        avoid.Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold);
        avoid.SetBounds(15, 147, 890, 22);
        guide.Controls.Add(avoid);

        var detail = Readout();
        detail.ForeColor = TextColor;
        detail.Font = new Font("Segoe UI", 9f);
        detail.Text =
            "Stay in-world. Use an old save or Phantom Liberty start; fight police for heavy combat.\r\n" +
            "If you die, stop with F11 before loading. Captures under five minutes also work.\r\n" +
            "Close the game, return here and use COLLECT RESULTS / RESTORE.";
        detail.SetBounds(15, 169, 890, 48);
        guide.Controls.Add(detail);
        guide.Resize += (_, _) =>
        {
            var width = Math.Max(350, guide.ClientSize.Width - 30);
            foreach (Control item in new Control[] { top, conflicts, headline, phases, avoid, detail })
                item.Width = width;
            var cell = width / 3;
            cetKey.Width = redKey.Width = capKey.Width = cell;
            redKey.Left = 16 + cell;
            capKey.Left = 16 + 2 * cell;
        };
        setupContent.Controls.Add(guide, 0, 1);

        var steps = Section("Profiler status — install → capture → restore", 142);
        steps.Dock = DockStyle.Fill;
        steps.Margin = new Padding(0, 2, 0, 2);
        profilerReady.ForeColor = Amber;
        profilerReady.Font = new Font("Segoe UI Semibold", 11.2f, FontStyle.Bold);
        profilerReady.SetBounds(16, 26, 885, 23);
        steps.Controls.Add(profilerReady);
        stepOne.SetBounds(16, 55, 885, 19);
        stepTwo.SetBounds(16, 77, 885, 19);
        stepThree.SetBounds(16, 99, 885, 19);
        foreach (var step in new[] { stepOne, stepTwo, stepThree })
        {
            step.Font = new Font("Segoe UI", 9f);
            steps.Controls.Add(step);
        }
        steps.Resize += (_, _) =>
        {
            var width = Math.Max(350, steps.ClientSize.Width - 32);
            profilerReady.Width = stepOne.Width = stepTwo.Width = stepThree.Width = width;
        };
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
        var mo2 = mo2Mode.Checked;
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
        foreach (var check in new Control[] { zeroEngineCheck, scriptsCheck, red4extCheck })
            check.Visible = !mo2;

        if (mo2)
        {
            for (var i = 0; i < mo2ComponentChecks.Length; i++)
                mo2ComponentChecks[i].SetBounds(14 + (i * 220), 175, 216, 20);
            sourceStatus.SetBounds(14, 199, 880, 19);
            zeroEngineLink.SetBounds(14, 218, 550, 18);
            RefreshMo2Rows();
        }
        else
        {
            sourceStatus.SetBounds(14, 91, 880, 20);
            zeroEngineCheck.SetBounds(14, 113, 520, 20);
            scriptsCheck.SetBounds(14, 134, 210, 20);
            red4extCheck.SetBounds(240, 134, 220, 20);
            zeroEngineLink.SetBounds(530, 113, 340, 20);
        }
    }

    private void RefreshMo2Rows()
    {
        var count = mo2Coverage?.VisibleFolders ?? 1;
        for (var i = 0; i < mo2SourceRows.Length; i++)
            mo2SourceRows[i].Visible = i < count;
    }

    private static CheckBox SourceCheck(string title) => new()
    {
        Text = title, AutoCheck = false, Checked = false, TabStop = false,
        AutoSize = false, FlatStyle = FlatStyle.Flat,
        BackColor = Color.Transparent, ForeColor = TextColor,
        Cursor = Cursors.Default
    };

    private void UpdateWorkflowVisuals()
    {
        // The current build contains no verified combined session. Never manufacture readiness.
        profilerReady.Text = "PROFILER NOT INSTALLED";
        profilerReady.ForeColor = Amber;
        stepOne.Text = "1. INSTALL PROFILERS — CET controls / scheduler + GRSP. Component verification required.";
        stepTwo.Text = "2. START CYBERPUNK — record synchronized F11 gameplay after both profilers are ready.";
        stepThree.Text = "3. COLLECT RESULTS / RESTORE — close game, archive evidence and restore original files.";
        stepOne.ForeColor = Cyan;
        stepTwo.ForeColor = stepThree.ForeColor = Color.FromArgb(104, 118, 126);
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

    private static void ConfigureCetPathRow(Panel panel, TextBox field, Button browse, int top)
    {
        field.AutoSize = false;
        field.Dock = DockStyle.None;
        field.Margin = Padding.Empty;
        field.Font = new Font("Consolas", 9f);
        field.Height = 26;
        browse.Dock = DockStyle.None;
        browse.Margin = Padding.Empty;

        void Arrange()
        {
            var width = panel.ClientSize.Width;
            field.SetBounds(0, top + 2, Math.Max(180, width - 103), 26);
            browse.SetBounds(Math.Max(185, width - 92), top, 92, 30);
        }
        panel.Controls.AddRange([field, browse]);
        panel.Resize += (_, _) => Arrange();
        Arrange();
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
        var cancellation = scanCancellation = new CancellationTokenSource();
        var current = GameInspector.Inspect(sourcePath.Text);
        if (!mo2Mode.Checked && current.SourceKind == SourceKind.LiveCetMods &&
            !string.IsNullOrWhiteSpace(current.GameRoot) &&
            !string.Equals(current.GameRoot, sourcePath.Text, StringComparison.OrdinalIgnoreCase))
        {
            sourcePath.Text = current.GameRoot;
            return;
        }

        inspection = current;
        captureAndRestoreVerified = false;
        install.Enabled = launch.Enabled = collect.Enabled = next.Enabled = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;
        UpdateWorkflowVisuals();
        UserSettings.WriteGameRoot(sourcePath.Text);

        ModSourceCoverage? coverage = null;
        if (mo2Mode.Checked)
        {
            var inputs = mo2SourceFields.Select(x => (string?)x.Text).ToArray();
            try
            {
                coverage = await Task.Run(
                    () => Mo2FolderSources.Inspect(inputs, cancellation.Token), cancellation.Token);
            }
            catch (OperationCanceledException) { return; }
            if (cancellation.IsCancellationRequested || IsDisposed) return;
        }
        mo2Coverage = coverage;
        RefreshMo2Rows();

        var redscriptCount = mo2Mode.Checked
            ? coverage?.RedscriptOwners ?? 0
            : RedscriptModCounter.CountPhysical(current.GameRoot);
        if (mo2Mode.Checked)
        {
            if (coverage is null || coverage.UsedFolders == 0)
                sourceStatus.Text = "Select a folder containing the MO2 mod files (any location).";
            else
            {
                var ready = new[]
                {
                    ModSourceParts.CetAsi, ModSourceParts.CetMods,
                    ModSourceParts.Redscript, ModSourceParts.Red4ext
                }.Count(part => coverage.Has(part));
                sourceStatus.Text = $"{coverage.UsedFolders} source folder(s) · {ready}/4 found · " +
                    $"{coverage.CetOwners} CET mods · {redscriptCount} REDscript sources";
            }
            var types = new[]
            {
                ModSourceParts.CetAsi, ModSourceParts.CetMods,
                ModSourceParts.Redscript, ModSourceParts.Red4ext
            };
            for (var i = 0; i < types.Length; i++)
                mo2ComponentChecks[i].Checked = coverage?.Has(types[i]) == true;
        }
        else
        {
            sourceStatus.Text = current.IsValidGame
                ? $"Game found. {current.CetModCount} CET mods and {redscriptCount} REDscript mods."
                : "Select the Cyberpunk 2077 Game root - /Cyberpunk2077";
        }
        sourceStatus.ForeColor = TextColor;

        if (!mo2Mode.Checked)
        {
            var zeroEnginePresent = current.Checks.Any(c => c.Name == "0-Engine source" &&
                c.State != GTotalOptimizer.Core.CheckState.Missing);
            var hasScripts = current.Checks.Any(c => c.Name == "REDscript source folder" &&
                c.State != GTotalOptimizer.Core.CheckState.Missing);
            var hasRed4ext = current.Checks.Any(c => c.Name == "RED4ext plugin folder" &&
                c.State != GTotalOptimizer.Core.CheckState.Missing);
            ApplyCheck(zeroEngineCheck, "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine", zeroEnginePresent);
            ApplyCheck(scriptsCheck, "r6/scripts", hasScripts);
            ApplyCheck(red4extCheck, "red4ext/plugins", hasRed4ext);
            zeroEngineLink.Visible = current.IsValidGame && !zeroEnginePresent;
        }
        else
        {
            // CET mods present is not sufficient to infer 0-Engine. Preserve its
            // separate dependency warning without creating a fifth source slot.
            zeroEngineLink.Visible = coverage?.Has(ModSourceParts.CetMods) == true &&
                coverage.HasZeroEngine == false;
        }

        var cetExe = BackendLocator.Executable(packageRoot, BackendKind.Cet);
        var redExe = BackendLocator.Executable(packageRoot, BackendKind.Grsp);
        var bundledCap = Path.Combine(packageRoot, "components", "capframex", "CapFrameX.exe");
        var keys = CaptureKeyInspector.Inspect(current.GameRoot,
            File.Exists(cetExe), File.Exists(redExe), bundledCap);
        var labels = new[] { cetKey, redKey, capKey };
        for (var i = 0; i < keys.Count; i++)
        {
            var actual = keys[i].Value;
            var key = actual.Equals("CUSTOM", StringComparison.OrdinalIgnoreCase)
                ? "CUSTOM (set F11)"
                : actual.Equals("F11", StringComparison.OrdinalIgnoreCase) && keys[i].Confirmed
                    ? "F11 ✓" : "F11 (preset)";
            labels[i].Text = $"{keys[i].Owner}: {key}";
            labels[i].ForeColor = TextColor;
            labels[i].AccessibleDescription = keys[i].Detail;
        }

        pageOneStatus.Text = mo2Mode.Checked
            ? coverage?.Complete == true
                ? current.IsValidGame
                    ? "All four component sources found. MO2 active-profile/runtime overlay still unverified."
                    : "All four component sources found. Game root needed for future profiler installation."
                : "MO2 physical sources only; select folders until all four checkboxes are checked."
            : current.IsValidGame ? "Game detected. Profiler installation and restore are not yet integrated."
                : "Select a valid Cyberpunk 2077 game folder.";

        if (!current.IsValidGame || cancellation.IsCancellationRequested) return;
        try
        {
            var statuses = await Task.WhenAll(
                backend.ReadAsync(cetExe, current.GameRoot!, BackendKind.Cet, cancellation.Token),
                backend.ReadAsync(redExe, current.GameRoot!, BackendKind.Grsp, cancellation.Token));
            if (cancellation.IsCancellationRequested || IsDisposed) return;
            pageOneStatus.Text += "  " + string.Join(" | ", statuses.Select(status =>
                $"{status.Kind}: {(status.Responded ? "status available" : "not staged")}"));
        }
        catch (OperationCanceledException) { }
    }

    private static void ApplyCheck(Label label, string path, bool present)
    {
        label.Text = $"{path}  {(present ? "✓" : "✕  NOT FOUND")}";
        label.ForeColor = TextColor;
        label.Font = new Font("Segoe UI Semibold", 9.5f);
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
    private sealed class Settings
    {
        public string GameRoot { get; set; } = "";
        public string[] Mo2SourceFolders { get; set; } = [];
        // Old settings remain readable to preserve upgrade compatibility.
        public string Mo2Directory { get; set; } = "";
        public string Mo2Profile { get; set; } = "";
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
    internal static string[] ReadMo2Sources() => Read().Mo2SourceFolders ?? [];
    internal static void WriteGameRoot(string value)
    {
        var s = Read(); s.GameRoot = value; Write(s);
    }
    internal static void WriteMo2Sources(string[] values)
    {
        var s = Read(); s.Mo2SourceFolders = values.Take(Mo2FolderSources.MaximumFolders).ToArray(); Write(s);
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
        catch { /* Settings must never affect live game sources. */ }
    }
}
