using GTotalOptimizer.Core;

var checks = 0;
void Require(bool valid, string message)
{
    checks++;
    if (!valid) throw new Exception("FAIL " + message);
}

var root = Path.Combine(Path.GetTempPath(), "GTotal-Contract-" + Guid.NewGuid().ToString("N"));
try
{
    Directory.CreateDirectory(root);
    Require(!GameInspector.Inspect(root).IsValidGame, "empty folder rejected");
    Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
    File.WriteAllText(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), "");
    Require(GameInspector.Inspect(root).IsValidGame, "game executable proves candidate root");
    var initially = GameInspector.Inspect(root).Checks;
    Require(initially.Any(c => c.Name == "REDscript source folder" && c.State == CheckState.Missing),
        "missing redscript not inferred from captures");

    var cet = Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods", "0-Engine");
    Directory.CreateDirectory(cet);
    File.WriteAllText(Path.Combine(cet, "init.lua"), "");
    var red = Path.Combine(root, "r6", "scripts", "G-RedRuntime", "Core");
    Directory.CreateDirectory(red);
    File.WriteAllText(Path.Combine(red, "Runtime.reds"), "");
    var probe = GameInspector.Inspect(root);
    Require(probe.Checks.Any(c => c.Name == "0-Engine source" && c.State == CheckState.Unverified),
        "source file detected but not advertised compatible");
    Require(probe.Checks.Any(c => c.Name == "G-REDruntime source" && c.State == CheckState.Unverified),
        "redscript source presence not equated to runtime compatibility");

    Require(BackendResponse.Interpret(BackendKind.Grsp, "{\"ok\":true,\"result\":{}}", 0).Responded,
        "GRSP success schema");
    Require(!BackendResponse.Interpret(BackendKind.Grsp, "{\"ok\":true}", 0).Responded,
        "GRSP missing result refused");
    Require(!BackendResponse.Interpret(BackendKind.Grsp, "{\"ok\":false,\"error\":\"unsafe\"}", 0).Responded,
        "GRSP error propagated");
    Require(BackendResponse.Interpret(BackendKind.Cet, "{\"state\":\"notInstalled\"}", 0).Responded,
        "CET status schema distinct from GRSP");
    Require(!BackendResponse.Interpret(BackendKind.Cet, "{broken", 0).Responded,
        "malformed JSON rejected");
    Require(!BackendResponse.Interpret(BackendKind.Cet, "{\"ok\":true}", 2).Responded,
        "nonzero exit rejected");
    Require(!BackendResponse.Interpret(BackendKind.Cet, "{\"ok\":\"false\"}", 0).Responded,
        "nonboolean success rejected");


    // The selected source must be the actual physical game tree or an explicit source-only folder.
    var modsRoot = Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "mods");
    var live = GameInspector.Inspect(modsRoot);
    Require(live.IsValidGame && live.SourceKind == SourceKind.LiveCetMods, "live CET mods path resolves matching physical game root");
    Require(live.CetModCount == 1, "only immediate mod folders with init.lua count as installed CET owners");
    var noInit = Path.Combine(modsRoot, "orphan");
    Directory.CreateDirectory(noInit);
    Require(GameInspector.CountCetMods(modsRoot) == 1, "directory without init.lua is not installed mod");

    var separate = Path.Combine(root, "MO2", "active-mods");
    var externalMod = Path.Combine(separate, "authorMod");
    Directory.CreateDirectory(externalMod);
    File.WriteAllText(Path.Combine(externalMod, "init.lua"), "");
    var external = GameInspector.Inspect(separate);
    Require(external.SourceKind == SourceKind.ExternalCetMods && !external.IsValidGame,
        "external CET mod source is NOT proof of effective MO2 game or profiler installation");
    Require(external.CetModCount == 1 && !external.HasVerifiedPhysicalSource,
        "external mod folder is inspectable but not deployable without proven effective source");

    var cetBindings = Path.Combine(root, "bin", "x64", "plugins", "cyber_engine_tweaks", "bindings.json");
    Require(!CaptureKeyInspector.InspectCet(root, true).Confirmed, "missing CET binding is pending, never falsely green");
    File.WriteAllText(cetBindings, "{\"CETProfilerControls\":{\"CETProfiler_Toggle\":34339947158700032}}");
    Require(CaptureKeyInspector.InspectCet(root, true).Confirmed, "CET F11 numeric binding recognized");
    File.WriteAllText(cetBindings, "{\"CETProfilerControls\":{\"CETProfiler_Toggle\":123}}");
    var custom = CaptureKeyInspector.InspectCet(root, true);
    Require(custom.Value == "CUSTOM" && !custom.Confirmed, "existing custom CET key is preserved, not claimed synchronized");
    File.WriteAllText(cetBindings, "{malformed");
    Require(!CaptureKeyInspector.InspectCet(root, true).Confirmed, "corrupt CET binding never claims F11");
    Require(!CaptureKeyInspector.InspectCet(root, false).Confirmed, "absent CET backend never claims synchronization");

    var cap = Path.Combine(root, "CapFrameX");
    Directory.CreateDirectory(cap);
    var capExe = Path.Combine(cap, "CapFrameX.exe");
    File.WriteAllText(capExe, "");
    File.WriteAllText(Path.Combine(cap, "portable.json"), "{\"paths\":{\"config\":\"Portable/Configuration\"}}");
    var capSettingsDir = Path.Combine(cap, "Portable", "Configuration");
    Directory.CreateDirectory(capSettingsDir);
    File.WriteAllText(Path.Combine(capSettingsDir, "AppSettings.json"), "{\"CaptureHotKey\":\"F11\"}");
    Require(CaptureKeyInspector.InspectCapFrameX(capExe).Confirmed, "CapFrameX portable F11 checked read-only");
    File.WriteAllText(Path.Combine(capSettingsDir, "AppSettings.json"), "{\"CaptureHotKey\":\"F9\"}");
    Require(!CaptureKeyInspector.InspectCapFrameX(capExe).Confirmed, "CapFrameX conflicting key must not be overwritten");
    Require(!CaptureKeyInspector.InspectCapFrameX(null).Confirmed, "optional missing CapFrameX not falsely green");


    Require(ComponentCommandParser.Parse(BackendKind.Cet, "collect", 0,
            "{\"ok\":true,\"destination\":\"C:/capture\"}", "").ArchivePath == "C:/capture",
        "CET collect reads its own destination field");
    Require(ComponentCommandParser.Parse(BackendKind.Grsp, "collect", 0,
            "{\"ok\":true,\"result\":{\"path\":\"C:/red\"}}", "").ArchivePath == "C:/red",
        "GRSP collect reads wrapped path");
    Require(!ComponentCommandParser.Parse(BackendKind.Grsp, "collect", 0,
            "{\"ok\":true,\"result\":{}}", "").Ok,
        "missing GRSP archive cannot unlock the next page");
    Require(!ComponentCommandParser.Parse(BackendKind.Cet, "collect", 0,
            "{\"ok\":true}", "").Ok,
        "missing CET archive cannot unlock the next page");
    Require(!ComponentCommandParser.Parse(BackendKind.Cet, "install", 1,
            "", "{\"ok\":false,\"error\":\"foreign profiler already installed\"}").Ok,
        "native owner installation failures propagate");
    Require(!ComponentCommandParser.Parse(BackendKind.Cet, "restore", 0,
            "invalid", "").Ok,
        "malformed restore reply is not accepted as recovery");
    Require(!ComponentCommandParser.Parse(BackendKind.Grsp, "restore", 0,
            "{\"ok\":true}", "").Ok,
        "missing GRSP result wrapper cannot claim restore complete");
    Require(!ComponentBundleVerifier.Inspect(root).Ready,
        "install is blocked when no CI component provenance manifest is staged");

    var bundleDir = Path.Combine(root, "staged");
    var cetBundleExe = BackendLocator.Executable(bundleDir, BackendKind.Cet);
    var redBundleExe = BackendLocator.Executable(bundleDir, BackendKind.Grsp);
    Directory.CreateDirectory(Path.GetDirectoryName(cetBundleExe)!);
    Directory.CreateDirectory(Path.GetDirectoryName(redBundleExe)!);
    File.WriteAllText(cetBundleExe, "CET fixture");
    File.WriteAllText(redBundleExe, "GRSP fixture");
    static string Hash(string file) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file)));
    var items = new[]
    {
        new { id = "cet", executable = "components/cet/G-CET-Runtime-Profiler.exe",
            sha256 = Hash(cetBundleExe), sourceCommit = new string('a', 40), ciRun = 1, artifactId = 1 },
        new { id = "grsp", executable = "components/grsp/G-REDscript-Profiler.exe",
            sha256 = Hash(redBundleExe), sourceCommit = new string('b', 40), ciRun = 2, artifactId = 2 }
    };
    var manifestPath = Path.Combine(bundleDir, "components", "manifest.json");
    File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(
        new { schemaVersion = 1, components = items }));
    Require(ComponentBundleVerifier.Inspect(bundleDir).Ready,
        "complete SHA-matched source/CI-staged component manifest passes read-only identity gate");
    File.AppendAllText(cetBundleExe, "tamper");
    Require(!ComponentBundleVerifier.Inspect(bundleDir).Ready,
        "component drift disables installer");

    var exe = BackendLocator.Executable(Path.Combine(root, "app"), BackendKind.Cet);
    Require(exe.EndsWith(Path.Combine("components", "cet", "G-CET-Runtime-Profiler.exe"), StringComparison.Ordinal),
        "standalone adapter layout predictable");
    var status = await new BackendStatusRunner().ReadAsync(exe, root, BackendKind.Cet, CancellationToken.None);
    Require(!status.Responded, "missing launcher does not claim installed");
}
finally
{
    try { Directory.Delete(root, true); } catch { }
}
Console.WriteLine($"PASS {checks} read-only installation/CLI contracts.");
