using System.Text.Json;
using GTotalOptimizer.Core;

namespace GTotalOptimizer.App;

internal sealed class MainForm : Form
{
    private readonly TextBox gameRoot = new() { Dock = DockStyle.Fill, PlaceholderText = "Select your Cyberpunk 2077 game folder" };
    private readonly Label summary = new() { Dock = DockStyle.Fill, AutoSize = true, MaximumSize = new Size(840, 0) };
    private readonly ListBox components = new() { Dock = DockStyle.Fill, IntegralHeight = false };
    private readonly Button install = ActionButton("1   Install Profilers");
    private readonly Button capture = ActionButton("2   Create Game Capture");
    private readonly Button continueToResults = ActionButton("3   Continue to Analytics & Pass Generation");
    private readonly Button generate = ActionButton("Generate Optimization ZIP");
    private readonly TabControl pages = new() { Dock = DockStyle.Fill };
    private readonly BackendStatusRunner runner = new();
    private CancellationTokenSource? scanToken;
    private readonly string componentRoot = AppContext.BaseDirectory;

    public MainForm()
    {
        Text = "G's Cyberpunk 2077 Total Optimization Generator";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(790, 580);
        ClientSize = new Size(920, 675);
        Font = new Font("Segoe UI", 10f);
        BackColor = Color.FromArgb(247, 249, 252);
        ForeColor = Color.FromArgb(31, 42, 55);

        var profilerPage = new TabPage("1 · Profiler") { Padding = new Padding(20), BackColor = BackColor };
        var analyticsPage = new TabPage("2 · Analytics & Pass Generation") { Padding = new Padding(20), BackColor = BackColor };
        pages.TabPages.Add(profilerPage);
        pages.TabPages.Add(analyticsPage);
        pages.Selecting += (_, e) =>
        {
            // A previous/stale capture cannot unlock Page 2.
            if (e.TabPage == analyticsPage) e.Cancel = true;
        };
        Controls.Add(pages);

        var first = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(0) };
        first.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        first.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        first.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        profilerPage.Controls.Add(first);

        first.Controls.Add(Heading("PROFILE YOUR INSTALLED MODS"), 0, 0);
        first.Controls.Add(HelpText("Choose your game folder once. The app discovers CET, REDscript, and their installed sources."), 0, 1);

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 5, 0, 7) };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        pathRow.Controls.Add(gameRoot, 0, 0);
        var browse = new Button { Text = "Browse...", Dock = DockStyle.Fill };
        browse.Click += (_, _) => ChooseGameFolder();
        pathRow.Controls.Add(browse, 1, 0);
        first.Controls.Add(pathRow, 0, 2);

        summary.Text = "Choose the Cyberpunk 2077 installation folder.";
        first.Controls.Add(summary, 0, 3);
        first.Controls.Add(components, 0, 4);

        first.Controls.Add(HelpText("Capture guide: roughly five minutes of varied gameplay—idle, running, jumping, driving and combat. Avoid menus and shops. This is guidance, not an automatic timer."), 0, 5);

        var actions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = new Padding(0, 8, 0, 5) };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        install.Dock = capture.Dock = continueToResults.Dock = DockStyle.Fill;
        actions.Controls.Add(install, 0, 0);
        actions.Controls.Add(capture, 1, 0);
        actions.Controls.Add(continueToResults, 2, 0);
        first.Controls.Add(actions, 0, 6);
        first.Controls.Add(HelpText("Initial read-only build: installer, capture and generation remain disabled until their safety tests pass."), 0, 7);
        install.Enabled = capture.Enabled = continueToResults.Enabled = false;

        var second = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        second.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        second.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        second.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        second.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        second.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        analyticsPage.Controls.Add(second);
        second.Controls.Add(Heading("ANALYTICS & PASS GENERATION"), 0, 0);
        second.Controls.Add(HelpText("After a verified capture and successful profiler restoration, this page will automatically analyze the current installed sources."), 0, 1);
        second.Controls.Add(HelpText("Not available until the synchronized capture and restore gates are implemented."), 0, 2);
        generate.Enabled = false;
        generate.Dock = DockStyle.Right;
        second.Controls.Add(generate, 0, 3);

        gameRoot.TextChanged += async (_, _) => await RefreshInspectionAsync();
        gameRoot.Text = UserSettings.ReadGameRoot();
    }

    private static Label Heading(string text) => new()
    {
        Text = text,
        Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
        ForeColor = Color.FromArgb(24, 48, 74),
        TextAlign = ContentAlignment.MiddleLeft,
        Dock = DockStyle.Fill
    };

    private static Label HelpText(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = false,
        ForeColor = Color.FromArgb(84, 94, 106),
        TextAlign = ContentAlignment.MiddleLeft
    };

    private static Button ActionButton(string text) => new()
    {
        Text = text,
        AutoSize = false,
        Height = 45,
        Margin = new Padding(4, 2, 4, 2)
    };

    private void ChooseGameFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Cyberpunk 2077 installation folder",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(gameRoot.Text) ? gameRoot.Text : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            gameRoot.Text = dialog.SelectedPath;
            UserSettings.WriteGameRoot(dialog.SelectedPath);
        }
    }

    private async Task RefreshInspectionAsync()
    {
        scanToken?.Cancel();
        scanToken?.Dispose();
        var token = scanToken = new CancellationTokenSource();
        var root = gameRoot.Text.Trim();
        var inspection = GameInspector.Inspect(root);
        summary.Text = inspection.Summary;
        components.BeginUpdate();
        components.Items.Clear();
        foreach (var check in inspection.Checks)
            components.Items.Add($"{check.Name}: {check.State} — {check.Detail}");
        components.EndUpdate();

        if (!inspection.IsValidGame) return;

        components.Items.Add("Checking available standalone profiler status interfaces...");
        try
        {
            var cetPath = BackendLocator.Executable(componentRoot, BackendKind.Cet);
            var grspPath = BackendLocator.Executable(componentRoot, BackendKind.Grsp);
            var statuses = await Task.WhenAll(
                runner.ReadAsync(cetPath, root, BackendKind.Cet, token.Token),
                runner.ReadAsync(grspPath, root, BackendKind.Grsp, token.Token));
            if (token.IsCancellationRequested || IsDisposed) return;

            foreach (var status in statuses)
                components.Items.Add($"{status.Name}: {(status.Responded ? "Status interface reachable" : "Unavailable")} — {status.Detail}");
            summary.Text = "Read-only inspection finished. No game files were changed.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!token.IsCancellationRequested && !IsDisposed)
                components.Items.Add("Status inspection error: " + ex.Message);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        scanToken?.Cancel();
        scanToken?.Dispose();
        base.OnFormClosed(e);
    }
}

internal static class UserSettings
{
    private sealed class Settings
    {
        public string GameRoot { get; set; } = "";
    }

    private static string SettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "G-Total-Optimizer", "settings.json");

    internal static string ReadGameRoot()
    {
        try
        {
            if (!File.Exists(SettingsFile)) return "";
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsFile))?.GameRoot ?? "";
        }
        catch { return ""; }
    }

    internal static void WriteGameRoot(string path)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            var temp = SettingsFile + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Settings { GameRoot = path }));
            File.Move(temp, SettingsFile, overwrite: true);
        }
        catch
        {
            // A non-writable user profile should not prevent read-only game inspection.
        }
    }
}
