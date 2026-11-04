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
