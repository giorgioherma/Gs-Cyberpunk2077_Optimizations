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
