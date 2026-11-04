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
    private readonly TextBox mo2Directory = Entry();
    private readonly ComboBox mo2Profile = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList, BackColor = PanelAlt,
        ForeColor = TextColor, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill
    };
    private readonly RadioButton normalMode = ModeChoice("NORMAL", true);
    private readonly RadioButton mo2Mode = ModeChoice("MO2", false);
    private readonly Panel mo2Panel = new() { Visible = false };
    private readonly Label mo2Hint = Readout();
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
    private Mo2Instance mo2Instance = Mo2SourceInspector.Discover(null);
    private Mo2Stack? mo2Stack;
    private bool settingProfiles;
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
        MinimumSize = new Size(920, 670);
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
        mo2Directory.TextChanged += async (_, _) =>
        {
            RefreshProfiles();
            await RefreshSourceAsync();
        };
        mo2Profile.SelectedIndexChanged += async (_, _) =>
        {
            if (!settingProfiles)
            {
                UserSettings.WriteMo2Profile(mo2Profile.SelectedItem?.ToString() ?? "");
                await RefreshSourceAsync();
            }
        };
        normalMode.CheckedChanged += async (_, _) => { SetSourceMode(); await RefreshSourceAsync(); };
        mo2Mode.CheckedChanged += async (_, _) => { SetSourceMode(); await RefreshSourceAsync(); };
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
        mo2Directory.Text = UserSettings.ReadMo2Directory();
        sourcePath.Text = UserSettings.ReadGameRoot();
        RefreshProfiles();
        SetSourceMode();
        Shown += async (_, _) => await RefreshSourceAsync();
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP",
            "Install the profilers, record your capture, then collect results and restore the original game files.");
        var content = (FlowLayoutPanel)grid.GetControlFromPosition(0, 1)!;

        sourceSection = Section("Game folder /Cyberpunk2077", 245);
        sourceSection.Controls.AddRange([normalMode, mo2Mode]);
        normalMode.SetBounds(17, 29, 108, 27);
        mo2Mode.SetBounds(133, 29, 85, 27);

        var pathRow = new TableLayoutPanel
        {
            Location = new Point(15, 62), Size = new Size(890, 38),
            ColumnCount = 2, RowCount = 1, Margin = Padding.Empty
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 113));
        sourcePath.AutoSize = false;
        sourcePath.Height = 37;
        sourcePath.Font = new Font("Consolas", 11f);
        sourcePath.Margin = new Padding(0, 1, 9, 1);
        sourcePath.Dock = DockStyle.Fill;
        pathRow.Controls.Add(sourcePath, 0, 0);
        var browse = Action("BROWSE...");
        browse.Dock = DockStyle.Fill;
        browse.Margin = Padding.Empty;
        browse.Click += (_, _) => ChooseSource();
        pathRow.Controls.Add(browse, 1, 0);
        sourceSection.Controls.Add(pathRow);

        mo2Panel.SetBounds(15, 106, 890, 108);
        var mo2PathRow = new TableLayoutPanel
        {
            Location = new Point(0, 0), Size = new Size(890, 36),
            ColumnCount = 3, RowCount = 1
        };
        mo2PathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        mo2PathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mo2PathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 113));
        var instanceLabel = Readout();
        instanceLabel.Text = "MO2 instance:";
        instanceLabel.ForeColor = TextColor;
        instanceLabel.Dock = DockStyle.Fill;
        mo2PathRow.Controls.Add(instanceLabel, 0, 0);
        mo2Directory.AutoSize = false;
        mo2Directory.Height = 34;
        mo2Directory.Dock = DockStyle.Fill;
        mo2Directory.Margin = new Padding(0, 1, 9, 1);
        mo2PathRow.Controls.Add(mo2Directory, 1, 0);
        var browseMo2 = Action("BROWSE...");
        browseMo2.Margin = Padding.Empty;
        browseMo2.Dock = DockStyle.Fill;
        browseMo2.Click += (_, _) =>
        {
            using var picker = new FolderBrowserDialog
            {
                Description = "Select the MO2 instance folder containing ModOrganizer.ini",
                UseDescriptionForTitle = true,
                SelectedPath = Directory.Exists(mo2Directory.Text)
                    ? mo2Directory.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            if (picker.ShowDialog(this) == DialogResult.OK) mo2Directory.Text = picker.SelectedPath;
        };
        mo2PathRow.Controls.Add(browseMo2, 2, 0);
        mo2Panel.Controls.Add(mo2PathRow);
        var profileRow = new TableLayoutPanel
        {
            Location = new Point(0, 41), Size = new Size(890, 29),
            ColumnCount = 2, RowCount = 1
        };
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        profileRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var profileLabel = Readout();
        profileLabel.Text = "Active profile:";
        profileLabel.Dock = DockStyle.Fill;
        profileRow.Controls.Add(profileLabel, 0, 0);
        mo2Profile.Margin = Padding.Empty;
        profileRow.Controls.Add(mo2Profile, 1, 0);
        mo2Panel.Controls.Add(profileRow);
        mo2Hint.SetBounds(0, 76, 890, 30);
        mo2Hint.ForeColor = Amber;
        mo2Panel.Controls.Add(mo2Hint);
        sourceSection.Controls.Add(mo2Panel);

        sourceStatus.ForeColor = Amber;
        sourceSection.Controls.Add(sourceStatus);
        zeroEngineCheck.ForeColor = TextColor;
        scriptsCheck.ForeColor = TextColor;
        red4extCheck.ForeColor = TextColor;
        sourceSection.Controls.AddRange([zeroEngineCheck, scriptsCheck, red4extCheck, zeroEngineLink]);
        sourceSection.Resize += (_, _) =>
        {
            var width = Math.Max(320, sourceSection.ClientSize.Width - 30);
            pathRow.Width = mo2Panel.Width = width;
            mo2PathRow.Width = profileRow.Width = mo2Hint.Width = width;
            foreach (var item in new Control[] { sourceStatus, zeroEngineCheck, scriptsCheck, red4extCheck, zeroEngineLink })
                item.Width = width;
        };
        content.Controls.Add(sourceSection);

        var guide = Section("INSTRUCTIONS", 331);
        var top = Readout();
        top.Text = "START the game and load the world. Press F11 to START, then F11 again to STOP.";
        top.ForeColor = Amber;
        top.Font = new Font("Segoe UI Semibold", 11.5f, FontStyle.Bold);
        top.SetBounds(15, 30, 895, 32);
        guide.Controls.Add(top);

        cetKey.SetBounds(18, 69, 292, 28);
        redKey.SetBounds(321, 69, 292, 28);
        capKey.SetBounds(627, 69, 292, 28);
        foreach (var label in new[] { cetKey, redKey, capKey })
        {
            label.Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold);
            guide.Controls.Add(label);
        }
        var conflicts = Readout();
        conflicts.Text = "Temporarily unbind any OTHER mod using F11 in CET and game settings.";
        conflicts.ForeColor = Amber;
        conflicts.SetBounds(15, 103, 905, 25);
        guide.Controls.Add(conflicts);

        var headline = Readout();
        headline.Text = "RECORD 5–10 MINUTES IN THE CITY";
        headline.ForeColor = Green;
        headline.Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold);
        headline.SetBounds(15, 139, 900, 34);
        guide.Controls.Add(headline);
        var phases = Readout();
        phases.Text = "IDLE   →   MOVEMENT   →   DRIVING   →   HEAVY COMBAT";
        phases.ForeColor = Green;
        phases.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        phases.SetBounds(15, 178, 900, 26);
        guide.Controls.Add(phases);
        var avoid = Readout();
        avoid.Text = "AVOID MENUS, INVENTORY, MAP, CET UI AND LOADING SCREENS";
        avoid.ForeColor = Green;
        avoid.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        avoid.SetBounds(15, 209, 900, 28);
        guide.Controls.Add(avoid);

        var detail = Readout();
        detail.ForeColor = Amber;
        detail.Text =
            "Stay in open, in-world gameplay. Use an older save or a Phantom Liberty quick start.\r\n" +
            "For heavy combat, provoke police. If you die, STOP with F11 promptly before loading.\r\n" +
            "Shorter captures can work, but 5–10 minutes is preferred. Close the game when finished.\r\n" +
            "Return here and use COLLECT RESULTS / RESTORE before continuing.";
        detail.Font = new Font("Segoe UI", 10f);
        detail.SetBounds(15, 247, 900, 75);
        guide.Controls.Add(detail);
        guide.Resize += (_, _) =>
        {
            foreach (Control item in new Control[] { top, conflicts, headline, phases, avoid, detail })
                item.Width = Math.Max(300, guide.ClientSize.Width - 30);
            var column = Math.Max(170, (guide.ClientSize.Width - 36) / 3);
            cetKey.Width = redKey.Width = capKey.Width = column;
            redKey.Left = 18 + column;
            capKey.Left = 18 + 2 * column;
        };
        content.Controls.Add(guide);

        var steps = Section("Profiler status — install → capture → restore", 190);
        profilerReady.Text = "PROFILER IS NOT READY!";
        profilerReady.ForeColor = Amber;
        profilerReady.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        profilerReady.SetBounds(17, 31, 890, 30);
        steps.Controls.Add(profilerReady);
        stepOne.SetBounds(18, 72, 895, 27);
        stepTwo.SetBounds(18, 106, 895, 27);
        stepThree.SetBounds(18, 140, 895, 27);
        steps.Controls.AddRange([stepOne, stepTwo, stepThree]);
        steps.Resize += (_, _) => profilerReady.Width = stepOne.Width = stepTwo.Width = stepThree.Width = steps.ClientSize.Width - 34;
        content.Controls.Add(steps);
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
        pageOneStatus.Text = "Profiler binaries and restore integration not yet verified.";
        status.Controls.Add(pageOneStatus, 0, 0);
        next.Dock = DockStyle.Fill;
        if (next is NeonButton nextButton) nextButton.IsAccent = true;
        status.Controls.Add(next, 1, 0);
    }

    private void SetSourceMode()
    {
        if (sourceSection is null) return;
        var mo2 = mo2Mode.Checked;
        mo2Panel.Visible = mo2;
        sourceSection.Height = mo2 ? 363 : 245;
        var y = mo2 ? 219 : 109;
        sourceStatus.SetBounds(15, y, 890, 24);
        zeroEngineCheck.SetBounds(15, y + 26, 890, 22);
        scriptsCheck.SetBounds(15, y + 51, 890, 22);
        red4extCheck.SetBounds(15, y + 76, 890, 22);
        zeroEngineLink.SetBounds(15, y + 100, 890, 23);
    }

    private void RefreshProfiles()
    {
        mo2Instance = Mo2SourceInspector.Discover(mo2Directory.Text);
        settingProfiles = true;
        try
        {
            var preferred = mo2Profile.SelectedItem?.ToString() ?? UserSettings.ReadMo2Profile();
            mo2Profile.Items.Clear();
            foreach (var name in mo2Instance.Profiles) mo2Profile.Items.Add(name);
            var selected = mo2Instance.Profiles.FirstOrDefault(p =>
                p.Equals(preferred, StringComparison.OrdinalIgnoreCase));
            if (selected is not null) mo2Profile.SelectedItem = selected;
            else if (mo2Instance.Profiles.Count == 1) mo2Profile.SelectedIndex = 0;
            else mo2Profile.SelectedIndex = -1;
            mo2Hint.Text = mo2Instance.Detail;
            mo2Hint.ForeColor = mo2Instance.Recognized ? Green : Amber;
            UserSettings.WriteMo2Directory(mo2Directory.Text);
        }
        finally { settingProfiles = false; }
    }

    private void UpdateWorkflowVisuals()
    {
        // The current build contains no verified combined session. Never manufacture readiness.
        var staged = ComponentBundleVerifier.Inspect(packageRoot);
        profilerReady.Text = staged.Ready
            ? "COMPONENTS STAGED — CAPTURE / RESTORE NOT YET VERIFIED"
            : "PROFILER IS NOT READY!";
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
        Text = label, Checked = selected, Appearance = Appearance.Button,
        FlatStyle = FlatStyle.Flat, ForeColor = Cyan, BackColor = PanelAlt,
        Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleCenter,
        Cursor = Cursors.Hand
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

    private static TableLayoutPanel PageLayout(Panel page, string title, string subtitle)
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(19, 14, 19, 12), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 91));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        page.Controls.Add(layout);

        var header = new Panel { Dock = DockStyle.Fill };
        var logo = new Label
        {
            Text = "G", Font = new Font("Segoe UI", 21f, FontStyle.Bold),
            ForeColor = Magenta, BackColor = PanelBg, TextAlign = ContentAlignment.MiddleCenter,
            Location = new Point(0, 5), Size = new Size(55, 59),
            BorderStyle = BorderStyle.FixedSingle
        };
        var heading = new Label
        {
            Text = title, Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold),
            ForeColor = Cyan, Location = new Point(69, 4), AutoSize = true
        };
        var sub = new Label { Text = subtitle, ForeColor = Muted, Location = new Point(70, 42), AutoSize = true };
        var line = new Panel { BackColor = Border, Height = 1, Dock = DockStyle.Bottom };
        header.Controls.AddRange([logo, heading, sub, line]);
        layout.Controls.Add(header, 0, 0);

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
        layout.Controls.Add(new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, Margin = new Padding(0, 7, 0, 0) }, 0, 2);
        layout.Controls.Add(new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 1, Margin = new Padding(0, 3, 0, 0) }, 0, 3);
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
        var selected = sourcePath.Text;
        var current = GameInspector.Inspect(selected);
        inspection = current;
        captureAndRestoreVerified = false;
        install.Enabled = launch.Enabled = collect.Enabled = next.Enabled = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;
        UpdateWorkflowVisuals();
        UserSettings.WriteGameRoot(selected);

        Mo2Stack? stack = null;
        if (mo2Mode.Checked && current.IsValidGame && mo2Instance.Recognized &&
            mo2Profile.SelectedItem is string profile)
        {
            // Filesystem enumeration is potentially large. Never block the WinForms UI thread.
            var snapshot = mo2Instance;
            try
            {
                stack = await Task.Run(() => Mo2SourceInspector.Resolve(current.GameRoot, snapshot, profile, cancellation.Token),
                    cancellation.Token);
            }
            catch (OperationCanceledException) { return; }
            if (cancellation.IsCancellationRequested || IsDisposed) return;
        }
        mo2Stack = stack;
        sourceStatus.Text = mo2Mode.Checked
            ? stack?.Detail ?? (current.IsValidGame ? "Select an MO2 instance and active profile."
                : "Choose a valid game folder first.")
            : current.Summary;
        sourceStatus.ForeColor = current.IsValidGame && (!mo2Mode.Checked || stack?.Ready == true)
            ? Green : Amber;

        var zeroEnginePresent = mo2Mode.Checked
            ? stack?.Ready == true && stack.HasZeroEngine
            : current.Checks.Any(c => c.Name == "0-Engine source" && c.State != GTotalOptimizer.Core.CheckState.Missing);
        var hasScripts = mo2Mode.Checked
            ? stack?.Ready == true && stack.HasRedscript
            : current.Checks.Any(c => c.Name == "REDscript source folder" && c.State != GTotalOptimizer.Core.CheckState.Missing);
        var hasRed4ext = mo2Mode.Checked
            ? stack?.Ready == true && stack.Red4extFiles > 0
            : current.Checks.Any(c => c.Name == "RED4ext plugin folder" && c.State != GTotalOptimizer.Core.CheckState.Missing);
        ApplyCheck(zeroEngineCheck, "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine", zeroEnginePresent);
        ApplyCheck(scriptsCheck, "r6/scripts", hasScripts);
        ApplyCheck(red4extCheck, "red4ext/plugins", hasRed4ext);
        zeroEngineLink.Visible = current.IsValidGame && (!mo2Mode.Checked || stack?.Ready == true) &&
                                 !zeroEnginePresent;

        if (mo2Mode.Checked)
        {
            mo2Hint.Text = stack?.Ready == true
                ? $"Profile {stack.Profile}: {stack.CetOwners} CET mods, {stack.RedscriptFiles} REDscript files; {stack.ShadowedFiles} loose-file conflicts. MO2 runtime not verified."
                : mo2Instance.Detail;
            mo2Hint.ForeColor = stack?.Ready == true ? Green : Amber;
        }

        var cetExe = BackendLocator.Executable(packageRoot, BackendKind.Cet);
        var redExe = BackendLocator.Executable(packageRoot, BackendKind.Grsp);
        // CapFrameX is a planned bundled component; never demand a user executable path.
        var bundledCap = Path.Combine(packageRoot, "components", "capframex", "CapFrameX.exe");
        var keys = CaptureKeyInspector.Inspect(current.GameRoot,
            File.Exists(cetExe), File.Exists(redExe), bundledCap);
        var labels = new[] { cetKey, redKey, capKey };
        for (var i = 0; i < keys.Count; i++)
        {
            labels[i].Text = $"{keys[i].Owner}: {keys[i].Value} {(keys[i].Confirmed ? "✓" : "⚠")}";
            labels[i].ForeColor = keys[i].Confirmed ? Green : Amber;
            labels[i].AccessibleDescription = keys[i].Detail;
        }

        pageOneStatus.Text = mo2Mode.Checked
            ? stack?.Ready == true ? "MO2 loose-file sources indexed; game launch/VFS and native restore remain unverified."
                : "MO2 profile not resolved. No game files were changed."
            : current.IsValidGame ? "Game detected. Profiler installation and restore are not yet integrated."
                : "Select a valid Cyberpunk 2077 game folder.";
        if (!current.IsValidGame || cancellation.IsCancellationRequested) return;
        try
        {
            var statuses = await Task.WhenAll(
                backend.ReadAsync(cetExe, current.GameRoot!, BackendKind.Cet, cancellation.Token),
                backend.ReadAsync(redExe, current.GameRoot!, BackendKind.Grsp, cancellation.Token));
            if (cancellation.IsCancellationRequested || IsDisposed) return;
            pageOneStatus.Text += "  " + string.Join(" | ", statuses.Select(s =>
                $"{s.Kind}: {(s.Responded ? "status available" : "not staged")}"));
        }
        catch (OperationCanceledException) { }
    }

    private static void ApplyCheck(Label label, string path, bool present)
    {
        label.Text = $"{path}  {(present ? "✓" : "✕  NOT FOUND")}";
        label.ForeColor = present ? TextColor : Amber;
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
    internal bool IsAccent { get; set; }
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
    internal static string ReadMo2Directory() => Read().Mo2Directory;
    internal static string ReadMo2Profile() => Read().Mo2Profile;
    internal static void WriteGameRoot(string value) { var s = Read(); s.GameRoot = value; Write(s); }
    internal static void WriteMo2Directory(string value) { var s = Read(); s.Mo2Directory = value; Write(s); }
    internal static void WriteMo2Profile(string value) { var s = Read(); s.Mo2Profile = value; Write(s); }

    private static void Write(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings));
            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch { /* Preferences must never affect game files. */ }
    }
}
