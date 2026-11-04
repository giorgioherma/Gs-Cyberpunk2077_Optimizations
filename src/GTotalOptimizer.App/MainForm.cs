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
    private readonly TextBox mo2Instance = Entry();
    private readonly ComboBox mo2Profile = new() { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = PanelAlt, ForeColor = TextColor, FlatStyle = FlatStyle.Flat };
    private readonly RadioButton normalMode = new() { Text = "Normal", Checked = true, AutoSize = true, ForeColor = Cyan };
    private readonly RadioButton mo2Mode = new() { Text = "MO2", AutoSize = true, ForeColor = Cyan };
    private readonly Label zeroEngineCheck = Readout();
    private readonly LinkLabel zeroEngineDownload = new();
    private readonly Label redscriptCheck = Readout();
    private readonly Label red4extCheck = Readout();
    private readonly Label instructionStart = Readout();
    private readonly Label instructionMain = Readout();
    private readonly Label instructionAvoid = Readout();
    private readonly Label instructionEnd = Readout();
    private readonly Label workflowReadiness = Readout();
    private Mo2Inspection? mo2Inspection;
    private readonly Panel mo2Fields = new() { BackColor = PanelBg };

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

        mo2Instance.TextChanged += (_, _) => { LoadMo2Profiles(); _ = RefreshSourceAsync(); };
        mo2Profile.SelectedIndexChanged += async (_, _) => await RefreshSourceAsync();
        normalMode.CheckedChanged += async (_, _) =>
        {
            mo2Fields.Visible = mo2Mode.Checked;
            await RefreshSourceAsync();
        };
        mo2Mode.CheckedChanged += async (_, _) =>
        {
            mo2Fields.Visible = mo2Mode.Checked;
            await RefreshSourceAsync();
        };
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

        mo2Instance.Text = UserSettings.ReadMo2Instance();
        sourcePath.Text = UserSettings.ReadGameRoot();
        LoadMo2Profiles();
        Shown += async (_, _) => await RefreshSourceAsync();
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP", "Install profilers, record gameplay, collect results and restore your installation.");
        var content = (FlowLayoutPanel)grid.GetControlFromPosition(0, 1)!;

        var source = Section("Cyberpunk 2077 — game folder", 270);
        var tabRow = new FlowLayoutPanel { Location = new Point(15, 25), Size = new Size(860, 29), BackColor = PanelBg };
        tabRow.Controls.Add(normalMode);
        tabRow.Controls.Add(mo2Mode);
        source.Controls.Add(tabRow);

        var pathRow = new TableLayoutPanel
        {
            Location = new Point(15, 62), Size = new Size(900, 43), ColumnCount = 2,
            Margin = new Padding(0), Padding = Padding.Empty
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        sourcePath.Font = new Font("Consolas", 11f);
        sourcePath.AutoSize = false;
        sourcePath.Height = 35;
        sourcePath.Margin = new Padding(0, 4, 6, 3);
        pathRow.Controls.Add(sourcePath, 0, 0);
        var browse = Action("BROWSE...");
        browse.Margin = Padding.Empty;
        browse.Click += (_, _) => ChooseSource();
        browse.Dock = DockStyle.Fill;
        pathRow.Controls.Add(browse, 1, 0);
        source.Controls.Add(pathRow);

        mo2Fields.SetBounds(15, 112, 900, 43);
        var moRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        moRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        moRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        moRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94));
        moRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var moLabel = Readout(); moLabel.Text = "MO2 INSTANCE"; moLabel.Dock = DockStyle.Fill;
        moRow.Controls.Add(moLabel, 0, 0);
        mo2Instance.Dock = DockStyle.Fill;
        moRow.Controls.Add(mo2Instance, 1, 0);
        var moBrowse = Action("Browse...");
        moBrowse.Dock = DockStyle.Fill;
        moBrowse.Click += (_, _) => ChooseMo2();
        moRow.Controls.Add(moBrowse, 2, 0);
        mo2Profile.Dock = DockStyle.Fill;
        moRow.Controls.Add(mo2Profile, 3, 0);
        mo2Fields.Controls.Add(moRow);
        mo2Fields.Visible = false;
        source.Controls.Add(mo2Fields);

        sourceStatus.SetBounds(15, 160, 900, 26);
        source.Controls.Add(sourceStatus);
        zeroEngineCheck.SetBounds(15, 188, 650, 23);
        zeroEngineDownload.Text = "0-Engine must be installed!  (Nexus Mods)";
        zeroEngineDownload.LinkColor = Amber;
        zeroEngineDownload.ActiveLinkColor = Cyan;
        zeroEngineDownload.VisitedLinkColor = Amber;
        zeroEngineDownload.Font = new Font("Segoe UI Semibold", 9f);
        zeroEngineDownload.SetBounds(655, 188, 260, 23);
        zeroEngineDownload.Visible = false;
        zeroEngineDownload.LinkClicked += (_, _) =>
            Process.Start(new ProcessStartInfo("https://www.nexusmods.com/cyberpunk2077/mods/27967")
                { UseShellExecute = true });
        source.Controls.Add(zeroEngineDownload);
        redscriptCheck.SetBounds(15, 211, 900, 23);
        red4extCheck.SetBounds(15, 234, 900, 23);
        source.Controls.AddRange([zeroEngineCheck, redscriptCheck, red4extCheck]);

        source.Resize += (_, _) =>
        {
            var width = Math.Max(300, source.ClientSize.Width - 30);
            pathRow.Width = width;
            mo2Fields.Width = width;
            sourceStatus.Width = width;
            zeroEngineCheck.Width = Math.Max(250, width - 280);
            zeroEngineDownload.Left = zeroEngineCheck.Right + 7;
            redscriptCheck.Width = red4extCheck.Width = width;
        };
        content.Controls.Add(source);

        var guidance = Section("INSTRUCTIONS", 347);
        instructionStart.Font = new Font("Segoe UI Semibold", 10.5f);
        instructionStart.ForeColor = Amber;
        instructionStart.Text =
            "Install the profilers. Start Cyberpunk 2077 and load into the world.\r\n" +
            "Press F11 to START recording. Press F11 again to STOP.";
        instructionStart.SetBounds(15, 27, 900, 47);
        guidance.Controls.Add(instructionStart);

        cetKey.SetBounds(15, 79, 900, 22);
        redKey.SetBounds(15, 102, 900, 22);
        capKey.SetBounds(15, 125, 900, 22);
        guidance.Controls.AddRange([cetKey, redKey, capKey]);

        var caution = Readout();
        caution.Font = new Font("Segoe UI", 9f);
        caution.ForeColor = Amber;
        caution.Text = "Temporarily unbind any other mod or game control using F11 in CET and Settings.";
        caution.SetBounds(15, 150, 900, 22);
        guidance.Controls.Add(caution);

        instructionMain.SetBounds(15, 183, 900, 29);
        instructionMain.Font = new Font("Segoe UI Semibold", 13f, FontStyle.Bold);
        instructionMain.ForeColor = Green;
        instructionMain.Text = "RECORD 5–10 MINUTES IN THE CITY";
        guidance.Controls.Add(instructionMain);

        instructionAvoid.SetBounds(15, 214, 900, 59);
        instructionAvoid.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
        instructionAvoid.ForeColor = Green;
        instructionAvoid.Text =
            "IDLE   •   MOVEMENT   •   DRIVING   •   HARD COMBAT\r\n" +
            "AVOID MENUS, INVENTORY, MAP, CET UI AND LOADING SCREENS.";
        guidance.Controls.Add(instructionAvoid);

        instructionEnd.SetBounds(15, 275, 900, 61);
        instructionEnd.Font = new Font("Segoe UI", 9.5f);
        instructionEnd.ForeColor = Amber;
        instructionEnd.Text =
            "Stay in open-world play. Use an existing save or skip ahead on a new game; fight police for combat stress.\r\n" +
            "If you die, stop the capture promptly to avoid the loading screen. Under 5 minutes is usable.\r\n" +
            "Close the game, return here and choose COLLECT RESULTS / RESTORE.";
        guidance.Controls.Add(instructionEnd);
        guidance.Resize += (_, _) =>
        {
            var width = guidance.ClientSize.Width - 30;
            foreach (Control control in guidance.Controls) control.Width = width;
        };
        content.Controls.Add(guidance);

        var readiness = Section("PROFILER STATUS / NEXT STEP", 102);
        workflowReadiness.SetBounds(15, 26, 900, 62);
        workflowReadiness.Font = new Font("Segoe UI Semibold", 10f);
        workflowReadiness.ForeColor = Amber;
        workflowReadiness.Text =
            "PROFILER IS NOT READY.\r\n" +
            "1. Install profilers   2. Run game and capture   3. Collect results / restore";
        readiness.Controls.Add(workflowReadiness);
        readiness.Resize += (_, _) => workflowReadiness.Width = readiness.ClientSize.Width - 30;
        content.Controls.Add(readiness);

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
        pageOneStatus.Text = "Installation and restoration controls remain gated pending component verification.";
        status.Controls.Add(pageOneStatus, 0, 0);
        next.Dock = DockStyle.Fill;
        next.ForeColor = Magenta;
        status.Controls.Add(next, 1, 0);
    }

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

    private static Button Action(string text) => new DarkActionButton
    {
        Text = text, BackColor = PanelAlt, ForeColor = Cyan,
        Font = new Font("Segoe UI Semibold", 9f),
        Margin = new Padding(5, 3, 5, 3), Height = 42
    };

    private sealed class DarkActionButton : Button
    {
        public DarkActionButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 1;
            FlatAppearance.BorderColor = Border;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using var background = new SolidBrush(Enabled ? PanelAlt : Color.FromArgb(18, 25, 31));
            e.Graphics.FillRectangle(background, ClientRectangle);
            using var outline = new Pen(Enabled ? Cyan : Color.FromArgb(49, 61, 68));
            e.Graphics.DrawRectangle(outline, 0, 0, Math.Max(0, Width - 1), Math.Max(0, Height - 1));
            var color = Enabled
                ? (Text.Contains("CONTINUE", StringComparison.OrdinalIgnoreCase) ? Magenta : Cyan)
                : Color.FromArgb(104, 118, 126);
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, color,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
        }
    }

    private void LoadMo2Profiles()
    {
        var previous = mo2Profile.SelectedItem?.ToString();
        mo2Profile.Items.Clear();
        mo2Profile.Items.Add("(auto from MO2 config)");
        try
        {
            // This is a convenience list; the inspector independently reads relocated profile paths.
            var standardProfiles = Path.Combine(mo2Instance.Text.Trim(), "profiles");
            if (Directory.Exists(standardProfiles))
                foreach (var folder in Directory.EnumerateDirectories(standardProfiles))
                    if (File.Exists(Path.Combine(folder, "modlist.txt")))
                        mo2Profile.Items.Add(Path.GetFileName(folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        var index = previous is null ? -1 : mo2Profile.Items.IndexOf(previous);
        mo2Profile.SelectedIndex = index >= 0 ? index : 0;
    }

    private void ChooseMo2()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select MO2 instance containing ModOrganizer.ini",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(mo2Instance.Text)
                ? mo2Instance.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            mo2Instance.Text = dialog.SelectedPath;
            LoadMo2Profiles();
        }
    }

    private void ChooseSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select your Cyberpunk 2077 game folder",
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

        inspection = GameInspector.Inspect(sourcePath.Text);
        mo2Inspection = mo2Mode.Checked
            ? Mo2SourceInspector.Inspect(mo2Instance.Text,
                mo2Profile.SelectedIndex > 0 ? mo2Profile.SelectedItem?.ToString() : null,
                inspection.GameRoot)
            : null;

        captureAndRestoreVerified = false;
        next.Enabled = false;
        install.Enabled = launch.Enabled = collect.Enabled = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;

        var projection = mo2Inspection;
        var mo2Good = mo2Mode.Checked && projection?.Valid == true;
        sourceStatus.Text = mo2Mode.Checked
            ? (projection?.Detail ?? "Select a MO2 instance.")
            : inspection.Summary;
        sourceStatus.ForeColor = mo2Mode.Checked
            ? (mo2Good ? TextColor : Amber)
            : (inspection.IsValidGame ? Green : Amber);

        var physicalRoot = inspection.GameRoot;
        var directMods = inspection.CetModsRoot;
        var zeroFromGame = directMods is not null &&
            File.Exists(Path.Combine(directMods, "0-Engine", "init.lua"));
        var zeroFromMo2 = mo2Good && projection!.HasZeroEngine;
        SetCheck(zeroEngineCheck,
            "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine",
            zeroFromGame || zeroFromMo2,
            "NOT FOUND");
        zeroEngineDownload.Visible = !(zeroFromGame || zeroFromMo2);

        var redFromGame = physicalRoot is not null &&
            Directory.Exists(Path.Combine(physicalRoot, "r6", "scripts"));
        var redFromMo2 = mo2Good && projection!.HasRedscript;
        SetCheck(redscriptCheck, "r6/scripts", redFromGame || redFromMo2,
            "REDscript source not detected.");

        var extFromGame = physicalRoot is not null &&
            Directory.Exists(Path.Combine(physicalRoot, "red4ext", "plugins"));
        var extFromMo2 = mo2Good && projection!.HasRed4ext;
        SetCheck(red4extCheck, "red4ext/plugins", extFromGame || extFromMo2,
            "RED4ext plugin folder not detected.");

        pageOneStatus.Text = mo2Mode.Checked
            ? "MO2 physical source projection only. Active game VFS/RootBuilder not yet verified; generation disabled."
            : "Verified component packaging and capture/restore integration pending.";

        workflowReadiness.Text =
            "PROFILER IS NOT READY.\r\n" +
            "1. INSTALL PROFILERS    →    2. RUN GAME / CAPTURE    →    3. COLLECT / RESTORE";
        workflowReadiness.ForeColor = Amber;

        UserSettings.WriteGameRoot(sourcePath.Text);
        UserSettings.WriteMo2Instance(mo2Instance.Text);

        var cetExe = BackendLocator.Executable(packageRoot, BackendKind.Cet);
        var redExe = BackendLocator.Executable(packageRoot, BackendKind.Grsp);
        // Bundled CapFrameX is intentional. No external executable picker.
        var bundledCapFrameX = Path.Combine(packageRoot, "components", "capframex", "CapFrameX.exe");
        var keys = CaptureKeyInspector.Inspect(inspection.GameRoot,
            File.Exists(cetExe), File.Exists(redExe), bundledCapFrameX);
        var labels = new[] { cetKey, redKey, capKey };
        for (var i = 0; i < keys.Count; i++)
        {
            var state = keys[i];
            labels[i].Text = $"{state.Owner,-11}   {state.Value} {(state.Confirmed ? "✓" : "⚠")} — {state.Detail}";
            labels[i].ForeColor = state.Confirmed ? Green : Amber;
        }

        if (!inspection.IsValidGame || cancellation.IsCancellationRequested) return;
        try
        {
            var statuses = await Task.WhenAll(
                backend.ReadAsync(cetExe, inspection.GameRoot!, BackendKind.Cet, cancellation.Token),
                backend.ReadAsync(redExe, inspection.GameRoot!, BackendKind.Grsp, cancellation.Token));
            if (cancellation.IsCancellationRequested || IsDisposed) return;
            pageOneStatus.Text = string.Join(" | ",
                statuses.Select(x => $"{x.Name}: {(x.Responded ? "status OK" : "not staged")}"));
            if (mo2Mode.Checked) pageOneStatus.Text += " | MO2 VFS unresolved";
        }
        catch (OperationCanceledException) { }
    }

    private static void SetCheck(Label label, string path, bool found, string absentText)
    {
        label.Text = found
            ? $"{path}   ✓  FOUND"
            : $"{path}   ⚠  {absentText}";
        label.ForeColor = found ? TextColor : Amber;
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

internal static class UserSettings
{
    private sealed class Settings
    {
        public string GameRoot { get; set; } = "";
        public string Mo2Instance { get; set; } = "";
    }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G-Total-Optimizer", "settings.json");

    private static Settings Read()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile)) ?? new Settings()
                : new Settings();
        }
        catch { return new Settings(); }
    }

    internal static string ReadGameRoot() => Read().GameRoot;
    internal static string ReadMo2Instance() => Read().Mo2Instance;
    internal static void WriteGameRoot(string path)
    {
        var settings = Read();
        settings.GameRoot = path;
        Write(settings);
    }
    internal static void WriteMo2Instance(string path)
    {
        var settings = Read();
        settings.Mo2Instance = path;
        Write(settings);
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
        catch { /* No writes to the game or MO2 source tree. */ }
    }
}
