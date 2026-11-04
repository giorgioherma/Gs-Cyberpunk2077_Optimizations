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
    private readonly TextBox capFrameXField = Entry();
    private readonly Label sourceStatus = Readout();
    private readonly Label detectedMods = Readout();
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
    private string? capFrameXPath;
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
        capFrameXField.TextChanged += async (_, _) =>
        {
            capFrameXPath = capFrameXField.Text.Trim();
            UserSettings.WriteCapFrameXExecutable(capFrameXPath);
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
        var existingCapFrameX = UserSettings.ReadCapFrameXExecutable();
        var bundledCapFrameX = Path.Combine(packageRoot, "components", "capframex", "CapFrameX.exe");
        capFrameXField.Text = !string.IsNullOrWhiteSpace(existingCapFrameX)
            ? existingCapFrameX : File.Exists(bundledCapFrameX) ? bundledCapFrameX : "";
        sourcePath.Text = UserSettings.ReadGameRoot();
        Shown += async (_, _) => await RefreshSourceAsync();
    }

    private void BuildProfilerPage()
    {
        var grid = PageLayout(pageOne, "SETUP", "Install the profilers, record a gameplay capture, then restore your Cyberpunk 2077 installation.");
        var content = (FlowLayoutPanel)grid.GetControlFromPosition(0, 1)!;

        var source = Section("Cyberpunk 2077 — game root / CET live mods", 156);
        var pathRow = new TableLayoutPanel { Location = new Point(15, 28), Size = new Size(900, 34), ColumnCount = 2, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        pathRow.Controls.Add(sourcePath, 0, 0);
        var browse = Action("Browse...");
        browse.Click += (_, _) => ChooseSource();
        browse.Dock = DockStyle.Fill;
        pathRow.Controls.Add(browse, 1, 0);
        source.Controls.Add(pathRow);
        sourceStatus.SetBounds(15, 70, 885, 28);
        detectedMods.SetBounds(15, 102, 885, 38);
        source.Controls.AddRange([sourceStatus, detectedMods]);
        source.Resize += (_, _) => { pathRow.Width = Math.Max(250, source.ClientSize.Width - 30); sourceStatus.Width = detectedMods.Width = source.ClientSize.Width - 30; };
        content.Controls.Add(source);

        var guidance = Section("Capture instructions", 257);
        var instructions = Readout();
        instructions.SetBounds(15, 27, 900, 218);
        instructions.Text =
            "Install the profilers and record a capture. Start the game and load the world.\r\n" +
            "Press F11 to START recording. Press F11 again to STOP.\r\n\r\n" +
            "Record at least five minutes. Stress different aspects of gameplay:\r\n" +
            "IDLE   •   MOVEMENT   •   DRIVING   •   HEAVY COMBAT\r\n\r\n" +
            "AVOID MENUS AND CET UI. Stay in open play; do not start missions, open inventory or the map.\r\n" +
            "Keep the capture in-world. When finished, close the game, return here and collect results.\r\n" +
            "Collect and restore the original game files before continuing to the next page.";
        guidance.Controls.Add(instructions);
        guidance.Resize += (_, _) => instructions.Width = guidance.ClientSize.Width - 30;
        content.Controls.Add(guidance);

        var bindings = Section("Shared capture key — live checks", 158);
        cetKey.SetBounds(15, 29, 900, 23);
        redKey.SetBounds(15, 53, 900, 23);
        capKey.SetBounds(15, 77, 900, 23);
        bindings.Controls.AddRange([cetKey, redKey, capKey]);
        var capRow = new TableLayoutPanel { Location = new Point(15, 110), Size = new Size(900, 33), ColumnCount = 3 };
        capRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
        capRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        capRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        var capLabel = Readout();
        capLabel.Text = "CapFrameX:";
        capLabel.Dock = DockStyle.Fill;
        capRow.Controls.Add(capLabel, 0, 0);
        capRow.Controls.Add(capFrameXField, 1, 0);
        var capBrowse = Action("Browse...");
        capBrowse.Dock = DockStyle.Fill;
        capBrowse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog
            {
                Filter = "CapFrameX executable (CapFrameX.exe)|CapFrameX.exe|Executables (*.exe)|*.exe",
                Title = "Link your optional CapFrameX installation",
                CheckFileExists = true
            };
            if (picker.ShowDialog(this) == DialogResult.OK) capFrameXField.Text = picker.FileName;
        };
        capRow.Controls.Add(capBrowse, 2, 0);
        bindings.Controls.Add(capRow);
        bindings.Resize += (_, _) =>
        {
            cetKey.Width = redKey.Width = capKey.Width = bindings.ClientSize.Width - 30;
            capRow.Width = bindings.ClientSize.Width - 30;
        };
        content.Controls.Add(bindings);

        // A key mismatch is never displayed as a green check. F11 stays user-owned.
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
        pageOneStatus.Text = "Read-only shell. Verified component packaging and capture lifecycle are pending.";
        status.Controls.Add(pageOneStatus, 0, 0);
        next.Dock = DockStyle.Fill;
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

    private static Button Action(string text) => new()
    {
        Text = text, FlatStyle = FlatStyle.Flat, BackColor = PanelAlt,
        ForeColor = TextColor, Font = new Font("Segoe UI Semibold", 9f),
        Margin = new Padding(5, 3, 5, 3), Height = 42
    };

    private void ChooseSource()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select Cyberpunk 2077 game root or directly the live CET mods folder",
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
        captureAndRestoreVerified = false;
        next.Enabled = false;
        install.Enabled = launch.Enabled = collect.Enabled = false;
        analyze.Enabled = generate.Enabled = openResults.Enabled = false;

        sourceStatus.Text = inspection.Summary;
        sourceStatus.ForeColor = inspection.IsValidGame ? Green : Amber;
        detectedMods.Text = inspection.CetModsRoot is not null
            ? "CET source: " + inspection.CetModsRoot
            : "CET source: not detected";
        detectedMods.ForeColor = inspection.HasCetSource ? Muted : Amber;
        pageOneStatus.Text = inspection.SourceKind == SourceKind.ExternalCetMods
            ? "External/MO2 folder: source preview only. Effective game overlay and profiler install are unverified."
            : "Component packaging and verified capture/restore integration are pending.";
        UserSettings.WriteGameRoot(sourcePath.Text);

        var cetExe = BackendLocator.Executable(packageRoot, BackendKind.Cet);
        var redExe = BackendLocator.Executable(packageRoot, BackendKind.Grsp);
        var keys = CaptureKeyInspector.Inspect(inspection.GameRoot, File.Exists(cetExe), File.Exists(redExe), capFrameXPath);
        var keyLabels = new[] { cetKey, redKey, capKey };
        for (var i = 0; i < keys.Count; i++)
        {
            keyLabels[i].Text = $"{keys[i].Owner,-11} — {keys[i].Value}  {(keys[i].Confirmed ? "✓" : "⚠")}   {keys[i].Detail}";
            keyLabels[i].ForeColor = keys[i].Confirmed ? Green : Amber;
        }

        if (!inspection.IsValidGame || cancellation.IsCancellationRequested) return;
        try
        {
            var statuses = await Task.WhenAll(
                backend.ReadAsync(cetExe, inspection.GameRoot!, BackendKind.Cet, cancellation.Token),
                backend.ReadAsync(redExe, inspection.GameRoot!, BackendKind.Grsp, cancellation.Token));
            if (cancellation.IsCancellationRequested || IsDisposed) return;
            pageOneStatus.Text = string.Join(" | ", statuses.Select(x => $"{x.Name}: {(x.Responded ? "status OK" : "not staged")}"));
        }
        catch (OperationCanceledException) { }
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
        public string CapFrameXExecutable { get; set; } = "";
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
    internal static string ReadCapFrameXExecutable() => Read().CapFrameXExecutable;

    internal static void WriteGameRoot(string path)
    {
        var settings = Read();
        settings.GameRoot = path;
        Write(settings);
    }

    internal static void WriteCapFrameXExecutable(string path)
    {
        var settings = Read();
        settings.CapFrameXExecutable = path;
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
        catch { /* Settings failure must not alter game data. */ }
    }
}
