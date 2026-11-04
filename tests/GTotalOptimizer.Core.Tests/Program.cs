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


    // MO2 source resolution: highest-priority enabled package wins and the game is a
    // low-priority physical layer. Capture inventories are not used as installation data.
    var mo2InstanceDir = Path.Combine(root, "ModOrganizer");
    var mo2Base = Path.Combine(root, "MO2-Storage");
    var mo2Mods = Path.Combine(mo2Base, "mods");
    var mo2Profiles = Path.Combine(mo2Base, "profiles");
    var activeProfile = Path.Combine(mo2Profiles, "Active");
    Directory.CreateDirectory(mo2InstanceDir);
    Directory.CreateDirectory(activeProfile);
    Directory.CreateDirectory(mo2Mods);
    File.WriteAllText(Path.Combine(mo2InstanceDir, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=../MO2-Storage\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\noverwrite_directory=%BASE_DIR%/overwrite\n");
    File.WriteAllText(Path.Combine(activeProfile, "modlist.txt"),
        "#highest priority first\n+High\n-LegacyDisabled\n+Low\n+../escape\n+Divider_separator\n");
    var highCet = Path.Combine(mo2Mods, "High", "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "CyberTrials");
    var lowCet = Path.Combine(mo2Mods, "Low", "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "CyberTrials");
    Directory.CreateDirectory(highCet);
    Directory.CreateDirectory(lowCet);
    File.WriteAllText(Path.Combine(highCet, "init.lua"), "high-priority");
    File.WriteAllText(Path.Combine(lowCet, "init.lua"), "low-priority");
    var disabledCet = Path.Combine(mo2Mods, "LegacyDisabled", "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "Legacy");
    Directory.CreateDirectory(disabledCet);
    File.WriteAllText(Path.Combine(disabledCet, "init.lua"), "disabled");
    var highRed = Path.Combine(mo2Mods, "High", "r6", "scripts", "shared.reds");
    var lowRed = Path.Combine(mo2Mods, "Low", "r6", "scripts", "shared.reds");
    Directory.CreateDirectory(Path.GetDirectoryName(highRed)!);
    Directory.CreateDirectory(Path.GetDirectoryName(lowRed)!);
    File.WriteAllText(highRed, "high redscript");
    File.WriteAllText(lowRed, "low redscript");
    var mo2Plugin = Path.Combine(mo2Mods, "High", "red4ext", "plugins", "SomePlugin.dll");
    Directory.CreateDirectory(Path.GetDirectoryName(mo2Plugin)!);
    File.WriteAllText(mo2Plugin, "native component");

    var mo2 = Mo2SourceInspector.Discover(mo2InstanceDir);
    Require(mo2.Recognized && mo2.Profiles.SequenceEqual(["Active"]), "MO2 INI paths/profiles parsed");
    Require(mo2.ModsDirectory == mo2Mods && mo2.ProfilesDirectory == mo2Profiles,
        "MO2 separate storage paths resolve without copying mods");
    Require(!Mo2SourceInspector.Resolve(root, mo2, "Missing").Ready,
        "unknown MO2 profile never silently selected");
    Require(!Mo2SourceInspector.Resolve(null, mo2, "Active").Ready,
        "MO2 source package alone cannot establish the current game root");

    var selectedStack = Mo2SourceInspector.Resolve(root, mo2, "Active");
    Require(selectedStack.Ready && selectedStack.EnabledPackages == 2,
        "only enabled valid MO2 package names enter active loose-file stack");
    Require(selectedStack.CetOwners == 2 && selectedStack.HasZeroEngine,
        "source-resolved stack preserves physical 0-Engine and counts winning CET init.lua");
    Require(selectedStack.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/CyberTrials/init.lua") == Path.GetFullPath(Path.Combine(highCet, "init.lua")),
        "highest-priority MO2 package wins when two enabled packages provide same CET file");
    Require(selectedStack.Winner("r6/scripts/shared.reds") == Path.GetFullPath(highRed),
        "REDscript conflicts follow MO2 modlist highest-priority-first order");
    Require(selectedStack.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/Legacy/init.lua") is null,
        "disabled mod excluded even though physical package remains installed in MO2 storage");
    Require(selectedStack.RedscriptFiles >= 2 && selectedStack.Red4extFiles == 1,
        "MO2 REDscript and red4ext loose-file trees are inventoried");
    Require(selectedStack.ShadowedFiles >= 2,
        "MO2 file conflicts are tracked without modifying installed sources");
    Require(selectedStack.Warnings.Any(w => w.Contains("RootBuilder")),
        "MO2 candidate inventory never claims proven runtime VFS/RootBuilder compatibility");

    var overwriteRoot = Path.Combine(mo2Base, "overwrite", "r6", "scripts");
    Directory.CreateDirectory(overwriteRoot);
    File.WriteAllText(Path.Combine(overwriteRoot, "shared.reds"), "MO2 overwrite");
    var withOverwrite = Mo2SourceInspector.Resolve(root, mo2, "Active");
    Require(withOverwrite.Winner("r6/scripts/shared.reds") == Path.Combine(overwriteRoot, "shared.reds"),
        "MO2 Overwrite wins over all enabled package files");
    File.WriteAllText(Path.Combine(mo2InstanceDir, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=%UNKNOWN_VARIABLE%\nprofiles_directory=%BASE_DIR%/profiles\n");
    Require(!Mo2SourceInspector.Discover(mo2InstanceDir).Recognized,
        "unresolved MO2 path variables fail closed, never map to a wrong physical directory");

    var unmanaged = Mo2SourceInspector.Discover(Path.Combine(root, "nonexistent-MO2"));
    Require(!unmanaged.Recognized, "missing MO2 instance is refused");

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
