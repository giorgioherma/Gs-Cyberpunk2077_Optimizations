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
    Require(!Mo2FeatureGate.Enabled,
        "initial integrated app never enables MO2 source/install/Resolver mode before lifecycle verification");

    Require(SetupCopy.CapFrameXProcess ==
        "Frame-time provider paused after crash; CET + REDscript captures remain available.",
        "instructions name CapX process detection exactly");
    Require(SetupCopy.CaptureStartInstruction ==
        "Start Cyberpunk 2077 and load into the world. Press F11 to START; press F11 again to STOP.\r\n" +
        "REDscript uses audio signals; CET displays its capture timer in the upper left corner.",
        "start/stop capture instructions retain both full lines and sound/timer cues");
    Require(SetupCopy.CaptureStartInstruction.Split("\r\n").Length == 2 &&
        SetupPageLayout.InstructionsHeight == 272 &&
        SetupPageLayout.InstructionLastTextBottom == 261,
        "instructions box expands exactly one row, without compressing existing instructions");
    Require(SetupCopy.StepInstall ==
        "1. INSTALL PROFILERS — CET controls / scheduler + GRSP. Component verification required." &&
        SetupCopy.StepStart == "2. START CYBERPUNK — Record CET and REDscript; frame-time currently disabled." &&
        SetupCopy.StepCollect ==
        "3. COLLECT RESULTS — Copy completed captures; Continue restores originals.",
        "restore original cyan/gray workflow descriptions next to profiler/key rows");
    Require(SetupCopy.CaptureDetails.Contains("aim for heavy combat") &&
        SetupCopy.CaptureDetails.Contains("If you're having issues getting a longer combat capture, fight police.") &&
        SetupCopy.CaptureDetails.Contains("If you die, stop recording with F11") &&
        SetupCopy.CaptureDetails.Contains("return to this screen") &&
        SetupCopy.CaptureDetails.Contains("Shorter captures also work.") &&
        SetupCopy.CaptureDetails.Split("\r\n").Length == 3,
        "revised combat, death and return instructions preserve three-line layout");

    var unverified = ProfilerSetupView.Unverified();
    Require(unverified.Heading == "PROFILERS NOT INSTALLED" &&
        !unverified.CetInstalled && !unverified.RedscriptInstalled && !unverified.CapXInstalled,
        "no displayed green profiler install check without authoritative payload evidence");
    Require(unverified.CetKeyDisplay.Contains("F11") && unverified.CapXKeyDisplay.Contains("F11") &&
        unverified.SynchronizationWarning is null,
        "unproven keys show proposed F11 preset without asserting detection or false mismatch");
    var synced = new ProfilerSetupView(false, false, false,
        new CaptureKeyCheck("CET", "F11", true, "detected"),
        new CaptureKeyCheck("CapFrameX", "F11", true, "detected"));
    Require(synced.SynchronizationWarning is null && synced.Heading == "PROFILERS NOT INSTALLED",
        "F11 detection is not installation proof");
    var allRequired = synced with { CetInstalled = true, RedscriptInstalled = true };
    Require(allRequired.Heading == "PROFILERS INSTALLED" && !allRequired.CapXInstalled,
        "verified CET and REDscript profilers allow installed heading; optional CapX remains unchecked");
    var unsynced = allRequired with
    {
        CetKey = new CaptureKeyCheck("CET", "F10", false, "user custom"),
        CapXKey = new CaptureKeyCheck("CapFrameX", "F9", false, "user custom")
    };
    Require(unsynced.SynchronizationWarning?.Contains("CET and CapX keybinds to F11") == true &&
        unsynced.Heading == "PROFILERS INSTALLED",
        "yellow keybind advisory does not falsely uninstall components or block controlled test workflow");
    Require(SetupPageLayout.FitsWithoutScroll(SetupPageLayout.ReferenceClientHeight, mo2: false),
        "Normal setup fits reference window without a scrollbar");
    Require(!Mo2FeatureGate.Enabled &&
        SetupPageLayout.WorkflowHeight(SetupPageLayout.ReferenceClientHeight, mo2: true) <
            SetupPageLayout.MinimumWorkflowHeight,
        "disabled MO2 layout needs its own review before mode is enabled; normal mode is unaffected");
    Require(!SetupPageLayout.FitsWithoutScroll(SetupPageLayout.ReferenceClientHeight - 110, mo2: true),
        "layout contract detects an undersized window instead of claiming no clipping");
    Require(SetupPageLayout.InstructionsHeight - 6 >= SetupPageLayout.InstructionLastTextBottom + 3,
        "the complete three-line capture instructions fit in their non-scrollable section");
    Require(SetupPageLayout.Mo2EngineNoticeTop + 18 <= SetupPageLayout.Mo2ChecksTop,
        "MO2 0-Engine notice is ABOVE all five component checks");
    Require(SetupPageLayout.Mo2ChecksTop + 20 <= SetupPageLayout.Mo2StatusTop,
        "MO2 checks do not overlap the source status");
    Require(SetupPageLayout.Mo2StatusTop + 18 + 4 <= SetupPageLayout.Mo2SourceHeight,
        "MO2 status and the 0-Engine warning remain inside the top box");
    Require(SetupPageLayout.WorkflowHeight(SetupPageLayout.ReferenceClientHeight, false)
        >= SetupPageLayout.MinimumWorkflowHeight,
        "one extra instruction row leaves original three-step box visible without scrolling");
    var absentPresentation = SetupSourcePresentation.ForNormal(GameInspector.Inspect(root), 0);
    Require(absentPresentation.IsWarning && !absentPresentation.ShowEngineLink &&
        !absentPresentation.GamePathConfirmed &&
        absentPresentation.StatusText == "Select the Cyberpunk 2077 Game root - /Cyberpunk2077",
        "missing game root has yellow selection guidance, no dependency link and no verified-game tick");
    Require(!GameInspector.Inspect(root).IsValidGame, "empty folder rejected");
    Directory.CreateDirectory(Path.Combine(root, "bin", "x64"));
    File.WriteAllText(Path.Combine(root, "bin", "x64", "Cyberpunk2077.exe"), "");
    Require(GameInspector.Inspect(root).IsValidGame, "game executable proves candidate root");
    var missingEngineDisplay = SetupSourcePresentation.ForNormal(GameInspector.Inspect(root), 2);
    Require(missingEngineDisplay.IsWarning && missingEngineDisplay.ShowEngineLink &&
        missingEngineDisplay.GamePathConfirmed &&
        missingEngineDisplay.StatusText == "Game found; 0-ENGINE MISSING!!",
        "valid game with missing 0-Engine remains yellow beside Nexus link and confirms game path");

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
    var installedEngineDisplay = SetupSourcePresentation.ForNormal(GameInspector.Inspect(root), 249);
    Require(!installedEngineDisplay.IsWarning && !installedEngineDisplay.ShowEngineLink &&
        installedEngineDisplay.GamePathConfirmed &&
        installedEngineDisplay.StatusText.Contains("249 REDscript mods.", StringComparison.Ordinal),
        "0-Engine present switches normal game status to white with source counts and no warning link");
    Require(probe.Checks.Any(c => c.Name == "0-Engine source" && c.State == CheckState.Unverified),
        "source file detected but not advertised compatible");
    Require(probe.Checks.Any(c => c.Name == "G-REDruntime source" && c.State == CheckState.Unverified),
        "redscript source presence not equated to runtime compatibility");


    // REDscript owner counts must not pretend every .reds file is a separate mod.
    Require(RedscriptModCounter.CountPhysical(root) == 1,
        "one framework top-level REDscript folder groups its nested .reds files");
    var extraRed = Path.Combine(root, "r6", "scripts", "AnotherOwner", "some_code.reds");
    Directory.CreateDirectory(Path.GetDirectoryName(extraRed)!);
    File.WriteAllText(extraRed, "mod");
    File.WriteAllText(Path.Combine(root, "r6", "scripts", "standalone.reds"), "root mod");
    Require(RedscriptModCounter.CountPhysical(root) == 3,
        "REDscript count groups top-level folders and standalone files, not raw .reds count");

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
    Require(CaptureKeyInspector.ReadCetBinding(root).Value == "F11",
        "read-only CET binding inspection works without asserting installed profiler");
    // Real source may contain case-distinct mod IDs; PowerShell ConvertFrom-Json
    // rejects these, but JsonDocument must preserve both and still find CET F11.
    File.WriteAllText(cetBindings,
        """{"HideMeshes":{},"hideMeshes":{},"CETProfilerControls":{"CETProfiler_Toggle":34339947158700032}}""");
    Require(CaptureKeyInspector.ReadCetBinding(root).Confirmed,
        "case-distinct user mod bindings do not break GUI F11 detection");
    File.WriteAllText(cetBindings, "{\"CETProfilerControls\":{\"CETProfiler_Toggle\":34058472181989376}}");
    Require(CaptureKeyInspector.ReadCetBinding(root).Value == "F10" &&
        !CaptureKeyInspector.ReadCetBinding(root).Confirmed,
        "CET F10 is displayed as detected key and not as synchronized F11");
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
            sha256 = Hash(cetBundleExe), sourceCommit = new string('a', 40), ciRun = 1, artifactId = 1,
            releaseSha256 = new string('a', 64) },
        new { id = "grsp", executable = "components/grsp/G-REDscript-Profiler.exe",
            sha256 = Hash(redBundleExe), sourceCommit = new string('b', 40), ciRun = 0, artifactId = 0,
            releaseSha256 = new string('b', 64) }
    };
    var payloadPaths = new[]
    {
        "components/cet/payload/cyber_engine_tweaks.PROFILER.dll",
        "components/cet/payload/0-Engine/modules/G-CET/Scheduler.lua",
        "components/cet/payload/CETProfilerControls/init.lua",
        "components/grsp/payload/G-REDscript-Profiler.dll",
        "components/presentmon/PresentMon.exe"
    };
    var payloads = payloadPaths.Select(path =>
    {
        var full = Path.Combine(bundleDir, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "payload fixture: " + path);
        return new { path, sha256 = Hash(full) };
    }).ToArray();
    var manifestPath = Path.Combine(bundleDir, "components", "manifest.json");
    File.WriteAllText(manifestPath, System.Text.Json.JsonSerializer.Serialize(
        new { schemaVersion = 2, components = items, payloads }));
    Require(ComponentBundleVerifier.Inspect(bundleDir).Ready,
        "complete SHA-matched source/CI-staged component manifest passes read-only identity gate");
    var goodStatus = LiveProfilerStateParser.Parse(
        """{"ok":true,"cet":"OFFICIAL","managed":false,"captureReadyForCollection":false}""",
        """{"ok":true,"result":{"State":"NOT_INSTALLED","ManagedStatePresent":false,"CompletedCaptureCount":0,"LegacyDllPresent":false}}""");
    Require(goodStatus.CleanForInstall && !goodStatus.BothInstalled,
        "CET/GRSP real owner JSON fields establish clean preflight without guessing");
    var installedStatus = LiveProfilerStateParser.Parse(
        """{"ok":true,"cet":"PROFILER_ACTIVE","managed":true,"captureReadyForCollection":true}""",
        """{"ok":true,"result":{"State":"INSTALLED_CURRENT","ManagedStatePresent":true,"CompletedCaptureCount":1,"LegacyDllPresent":false}}""");
    Require(installedStatus.BothInstalled && installedStatus.BothCaptured,
        "both owner-reported installed payloads and captured state verified before collection");
    var installedNoCapture = installedStatus with
    {
        CetCaptureReady = false, GrspCaptureReady = false
    };
    Require(installedNoCapture.BothInstalled &&
        installedNoCapture.CanLaunch(hasCurrentSession: true, gameRunning: false) &&
        !installedNoCapture.CanCollect(hasCurrentSession: true, gameRunning: false),
        "installing both profilers enables launch but NEVER enables Collect before F11 completion");
    Require(!installedNoCapture.CanLaunch(hasCurrentSession: false, gameRunning: false) &&
        !installedNoCapture.CanLaunch(hasCurrentSession: true, gameRunning: true),
        "reopened app cannot launch without ownership journal or while Cyberpunk is still running");
    Require(installedStatus.CanCollect(hasCurrentSession: true, gameRunning: false) &&
        !installedStatus.CanCollect(hasCurrentSession: true, gameRunning: true) &&
        !installedStatus.CanCollect(hasCurrentSession: false, gameRunning: false) &&
        !installedStatus.CanLaunch(hasCurrentSession: true, gameRunning: false),
        "only completed dual-domain owner capture and closed game unlock Collect, not another launch");
    Require((installedStatus with { CetCaptureReady = false })
            .CanCollect(hasCurrentSession: true, gameRunning: false) &&
        (installedStatus with { GrspCaptureReady = false })
            .CanCollect(hasCurrentSession: true, gameRunning: false),
        "independent CET-only and REDscript-only results can be collected");

    // Archive copies are verified, immutable evidence for Continue.
    var archiveGame = Path.Combine(root, "CaptureGame");
    Directory.CreateDirectory(archiveGame);
    var archiveRoot = Path.Combine(root, "OriginalArchives");
    var cetSource = Path.Combine(archiveRoot, "CET");
    var redSource = Path.Combine(archiveRoot, "RED");
    Directory.CreateDirectory(cetSource);
    Directory.CreateDirectory(redSource);
    File.WriteAllText(Path.Combine(cetSource, "CET_Summary.json"), "{\"capture\":\"CET\"}");
    File.WriteAllText(Path.Combine(redSource, "GRSP_Summary.json"), "{\"capture\":\"RED\"}");
    var package = Path.Combine(root, "GCO-package");
    var sessionId = Guid.NewGuid().ToString("N");
    var emptyCapture = CollectedCaptureStore.Read(package, archiveGame, sessionId);
    Require(!emptyCapture.HasAny, "no old files treated as a new session");
    var cetCopy = CollectedCaptureStore.CopyOwner(package, archiveGame, sessionId,
        BackendKind.Cet, cetSource);
    Require(cetCopy.HasAny && cetCopy.Cet is not null && cetCopy.Redscript is null &&
        File.Exists(Path.Combine(CollectedCaptureStore.DomainFolder(package, sessionId, "CET"),
            "CET_Summary.json")), "CET-only capture copied under GCO root RESULTS");
    var redCopy = CollectedCaptureStore.CopyOwner(package, archiveGame, sessionId,
        BackendKind.Grsp, redSource);
    Require(redCopy.Cet is not null && redCopy.Redscript is not null,
        "second capture merges into one session without losing CET");
    Require(File.Exists(Path.Combine(cetSource, "CET_Summary.json")),
        "source-owner archive remains untouched after GCO copy");
    File.AppendAllText(Path.Combine(CollectedCaptureStore.DomainFolder(package,sessionId,"CET"),
        "CET_Summary.json"), "tampered");
    var tamperCaught = false;
    try { CollectedCaptureStore.Read(package, archiveGame, sessionId, verifyHashes: true); }
    catch (InvalidDataException) { tamperCaught = true; }
    Require(tamperCaught, "tampered collected evidence fails SHA-256 verification");

    var pmRoot = Path.Combine(root, "GCO-pm-package");
    var pmExe = PresentMonLifecyclePolicy.Executable(pmRoot);
    var pmCsv = Path.Combine(pmRoot, "RESULTS", "GCO-Capture-20261105", "PresentMon.csv");
    Require(PresentMonLifecyclePolicy.IsOwnedProcess(pmRoot, pmExe) &&
        PresentMonLifecyclePolicy.ValidateOutputCsv(pmRoot, pmCsv) == Path.GetFullPath(pmCsv),
        "PresentMon restart is scoped to the bundled process and to the current GCO results directory");
    Require(!PresentMonLifecyclePolicy.IsOwnedProcess(pmRoot,
        Path.Combine(root, "OtherTool", "PresentMon.exe")),
        "an unrelated PresentMon process is never silently treated as GCO-owned");
    var pmTraversalRejected = false;
    try
    {
        PresentMonLifecyclePolicy.ValidateOutputCsv(pmRoot,
            Path.Combine(pmRoot, "RESULTS", "..", "mods", "PresentMon.csv"));
    }
    catch (InvalidDataException) { pmTraversalRejected = true; }
    Require(pmTraversalRejected,
        "elevated PresentMon helper rejects path traversal into game/source directories");
    pmTraversalRejected = false;
    try
    {
        PresentMonLifecyclePolicy.ValidateOutputCsv(pmRoot,
            Path.Combine(root, "OtherTool", "PresentMon.csv"));
    }
    catch (InvalidDataException) { pmTraversalRejected = true; }
    Require(pmTraversalRejected,
        "elevated PresentMon helper refuses destinations outside its own RESULTS");
    Require(!ComponentBundleVerifier.Inspect(bundleDir).Detail.Contains("schema invalid"),
        "full payload manifest has current schema");
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
        "#highest priority first\n+High\n-LegacyDisabled\n+Low\n+Divider_separator\n");
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
    Require(mo2.SelectedModlistPath("Active") == Path.Combine(activeProfile, "modlist.txt"),
        "MO2 modlist location derived from INI profiles directory and selected profile");
    Require(mo2.SelectedModlistPath("Unknown") is null,
        "unknown MO2 profile cannot resolve to an invented modlist");
    var noSelectedProfile = Mo2SourceInspector.Resolve(root, mo2, "Missing");
    Require(noSelectedProfile.Ready && noSelectedProfile.GameOnlyFallback &&
        !noSelectedProfile.ProfileVerified &&
        noSelectedProfile.EffectiveFiles.Values.All(file => !file.FromMo2),
        "unknown MO2 profile never admits any MO2-managed files; physical game remains available");
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

    // Physical game files are never discarded when any MO2 packages are active.
    // Only same-path conflicts are won by higher-priority active MO2 layers.
    var directPhysical = Path.Combine(root, "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "DirectOwner", "init.lua");
    Directory.CreateDirectory(Path.GetDirectoryName(directPhysical)!);
    File.WriteAllText(directPhysical, "direct owner");
    var physicalConflict = Path.Combine(root, "r6", "scripts", "shared.reds");
    File.WriteAllText(physicalConflict, "physical redscript");
    var physicalExtra = Path.Combine(root, "r6", "scripts", "DirectOnly", "main.reds");
    Directory.CreateDirectory(Path.GetDirectoryName(physicalExtra)!);
    File.WriteAllText(physicalExtra, "direct redscript");
    var withPhysical = Mo2SourceInspector.Resolve(root, mo2, "Active");
    Require(withPhysical.Ready && withPhysical.ProfileVerified && withPhysical.EnabledPackages == 2,
        "enabled MO2 packages remain active on top of a full physical game layer");
    Require(withPhysical.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/DirectOwner/init.lua") ==
        directPhysical && withPhysical.CetOwners == 3,
        "direct CET owner remains in effective stack when other MO2 mods are enabled");
    Require(withPhysical.Winner("r6/scripts/DirectOnly/main.reds") == physicalExtra,
        "direct REDscript file remains visible when MO2 has enabled packages");
    Require(withPhysical.Winner("r6/scripts/shared.reds") == highRed &&
        withPhysical.EffectiveFiles["r6/scripts/shared.reds"].FromMo2,
        "highest-priority MO2 loose file overrides physical game only at the same relative path");
    Require(withPhysical.ShadowedFiles > selectedStack.ShadowedFiles,
        "physical/MO2 conflict contributes to shadowed-file count");

    var overwriteRoot = Path.Combine(mo2Base, "overwrite", "r6", "scripts");
    Directory.CreateDirectory(overwriteRoot);
    File.WriteAllText(Path.Combine(overwriteRoot, "shared.reds"), "MO2 overwrite");
    var withOverwrite = Mo2SourceInspector.Resolve(root, mo2, "Active");
    Require(withOverwrite.Winner("r6/scripts/shared.reds") == Path.Combine(overwriteRoot, "shared.reds"),
        "MO2 Overwrite wins over all enabled package files");
    // Zero enabled MO2 mods: valid modlist, live game still counted.
    // Completely missing modlist: MO2 (including Overwrite) is excluded,
    // but the live game still supplies CET/REDscript/RED4ext files.
    var disabledProfile = Path.Combine(mo2Profiles, "AllDisabled");
    Directory.CreateDirectory(disabledProfile);
    File.WriteAllText(Path.Combine(disabledProfile, "modlist.txt"),
        "# no enabled packages\n-High\n-Low\n");
    var noListProfile = Path.Combine(mo2Profiles, "NoModlist");
    Directory.CreateDirectory(noListProfile);
    var verifiedProfiles = Mo2SourceInspector.Discover(mo2InstanceDir);
    Require(verifiedProfiles.Profiles.Contains("AllDisabled") &&
        verifiedProfiles.Profiles.Contains("NoModlist"),
        "profile choice does not silently exclude an existing profile without modlist.txt");

    var allDisabled = Mo2SourceInspector.Resolve(root, verifiedProfiles, "AllDisabled");
    Require(allDisabled.Ready && allDisabled.ProfileVerified && !allDisabled.GameOnlyFallback &&
        allDisabled.EnabledPackages == 0 && allDisabled.CetOwners >= 2,
        "all-disabled MO2 profile is valid, and the physical CET installation is still scanned");
    Require(allDisabled.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/CyberTrials/init.lua") is null,
        "disabled MO2 CyberTrials is not admitted to game-only content");
    Require(allDisabled.Winner("r6/scripts/DirectOnly/main.reds") == physicalExtra,
        "live game REDscript remains available with all MO2 mods disabled");

    var noModlist = Mo2SourceInspector.Resolve(root, verifiedProfiles, "NoModlist");
    Require(noModlist.Ready && noModlist.GameOnlyFallback &&
        !noModlist.ProfileVerified && noModlist.EnabledPackages == 0,
        "missing modlist is NOT considered verified, but game-only inspection still succeeds");
    Require(noModlist.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/DirectOwner/init.lua") ==
        directPhysical && noModlist.Winner("r6/scripts/DirectOnly/main.reds") == physicalExtra,
        "missing modlist must preserve live game files");
    Require(noModlist.Winner("r6/scripts/shared.reds") == physicalConflict &&
        noModlist.Winner("bin/x64/plugins/cyber_engine_tweaks/mods/CyberTrials/init.lua") is null,
        "missing modlist excludes both MO2 Overwrite and every MO2 package from conflict resolution");
    var unknownProfile = Mo2SourceInspector.Resolve(root, verifiedProfiles, "NotInProfileList");
    Require(unknownProfile.Ready && unknownProfile.GameOnlyFallback &&
        unknownProfile.EffectiveFiles.Values.All(f => !f.FromMo2),
        "unknown profile never admits unselected MO2 files but still scans the direct game");

    var originalValidModlist = File.ReadAllText(Path.Combine(activeProfile, "modlist.txt"));
    try
    {
        File.WriteAllText(Path.Combine(activeProfile, "modlist.txt"), "+../escape\n");
        var malformed = Mo2SourceInspector.Resolve(root, verifiedProfiles, "Active");
        Require(malformed.Ready && malformed.GameOnlyFallback && !malformed.ProfileVerified &&
            malformed.EffectiveFiles.Values.All(file => !file.FromMo2),
            "malformed enabled MO2 paths exclude MO2 completely but preserve live game inventory");
    }
    finally { File.WriteAllText(Path.Combine(activeProfile, "modlist.txt"), originalValidModlist); }

    File.WriteAllText(Path.Combine(mo2InstanceDir, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=%UNKNOWN_VARIABLE%\nprofiles_directory=%BASE_DIR%/profiles\n");
    Require(!Mo2SourceInspector.Discover(mo2InstanceDir).Recognized,
        "unresolved MO2 path variables fail closed, never map to a wrong physical directory");


    // MO2 can live anywhere, with Mods/Profiles/Overwrite on unrelated drives
    // or in arbitrary folders, and the physical game can have NO mods installed.
    var emptyPhysicalGame = Path.Combine(root, "another unrelated game folder");
    Directory.CreateDirectory(Path.Combine(emptyPhysicalGame, "bin", "x64"));
    File.WriteAllText(Path.Combine(emptyPhysicalGame, "bin", "x64", "Cyberpunk2077.exe"), "");
    var mo2Only = Mo2SourceInspector.Resolve(emptyPhysicalGame, mo2, "Active");
    Require(mo2Only.Ready && mo2Only.CetOwners == 1 && !mo2Only.HasZeroEngine,
        "MO2 detects enabled CET mods even when physical game has zero scripts/mods");
    var mo2Framework = Path.Combine(mo2Mods, "High", "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "0-Engine", "init.lua");
    Directory.CreateDirectory(Path.GetDirectoryName(mo2Framework)!);
    File.WriteAllText(mo2Framework, "MO2 engine");
    var mo2OnlyWithEngine = Mo2SourceInspector.Resolve(emptyPhysicalGame, mo2, "Active");
    Require(mo2OnlyWithEngine.HasZeroEngine && mo2OnlyWithEngine.CetOwners == 2,
        "MO2-only 0-Engine source is detected without default physical installation");
    Require(RedscriptModCounter.CountEffective(mo2OnlyWithEngine) == 1,
        "MO2 effective REDscript owner count groups files by winning enabled package");

    // Absolute arbitrary instance storage paths, not assumed within the game root
    // or inside the MO2 executable directory.
    var arbitraryStorage = Path.Combine(root, "Completely random location");
    var arbitraryMods = Path.Combine(arbitraryStorage, "any mods folder");
    var arbitraryProfiles = Path.Combine(arbitraryStorage, "profiles elsewhere");
    var arbitraryOverwrite = Path.Combine(arbitraryStorage, "write anywhere");
    Directory.CreateDirectory(arbitraryMods);
    Directory.CreateDirectory(arbitraryProfiles);
    Directory.CreateDirectory(arbitraryOverwrite);
    Directory.CreateDirectory(Path.Combine(arbitraryProfiles, "Random Profile"));
    File.WriteAllText(Path.Combine(arbitraryProfiles, "Random Profile", "modlist.txt"),
        "+Enabled Elsewhere\n");
    var arbitraryMod = Path.Combine(arbitraryMods, "Enabled Elsewhere", "r6", "scripts", "random.reds");
    Directory.CreateDirectory(Path.GetDirectoryName(arbitraryMod)!);
    File.WriteAllText(arbitraryMod, "external");
    File.WriteAllText(Path.Combine(mo2InstanceDir, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + arbitraryStorage.Replace('\\','/') +
        "\nmod_directory=" + arbitraryMods.Replace('\\','/') +
        "\nprofiles_directory=" + arbitraryProfiles.Replace('\\','/') +
        "\noverwrite_directory=" + arbitraryOverwrite.Replace('\\','/') + "\n");
    var anyLocation = Mo2SourceInspector.Discover(mo2InstanceDir);
    Require(anyLocation.Recognized && anyLocation.ModsDirectory == arbitraryMods &&
        anyLocation.ProfilesDirectory == arbitraryProfiles &&
        anyLocation.OverwriteDirectory == arbitraryOverwrite,
        "MO2 honors configured absolute arbitrary Mods/Profiles/Overwrite directories");
    Require(anyLocation.SelectedModlistPath("Random Profile") ==
        Path.Combine(arbitraryProfiles, "Random Profile", "modlist.txt"),
        "modlist auto-discovered on arbitrary external storage without a file picker");
    var anywhere = Mo2SourceInspector.Resolve(emptyPhysicalGame, anyLocation, "Random Profile");
    Require(anywhere.Ready && anywhere.RedscriptFiles == 1 &&
        RedscriptModCounter.CountEffective(anywhere) == 1,
        "MO2 source scan works without a direct installation from arbitrary paths");

    var unmanaged = Mo2SourceInspector.Discover(Path.Combine(root, "nonexistent-MO2"));
    Require(!unmanaged.Recognized, "missing MO2 instance is refused");


    // MO2 installation detection: portable INI or instance under LocalAppData.
    // A directory's mere existence is never enough to select an active profile.
    var mo2ExecutableDir = Path.Combine(root, "MO2 installed somewhere");
    Directory.CreateDirectory(mo2ExecutableDir);
    File.WriteAllText(Path.Combine(mo2ExecutableDir, "ModOrganizer.exe"), "fixture");
    var exampleAppData = Path.Combine(root, "UserProfile", "AppData", "Local");
    var instanceStore = Path.Combine(exampleAppData, "ModOrganizer");
    Require(!Mo2InstallLocator.Inspect("", localAppData: exampleAppData).HasConfiguration,
        "no MO2 selection never claims configured instance");
    Require(!Mo2InstallLocator.Inspect(mo2ExecutableDir,
        localAppData: exampleAppData).HasConfiguration,
        "MO2 executable alone cannot infer missing instance configuration");

    var firstInstance = Path.Combine(instanceStore, "Cyberpunk 2077");
    var secondInstance = Path.Combine(instanceStore, "Other Game");
    Directory.CreateDirectory(firstInstance);
    File.WriteAllText(Path.Combine(firstInstance, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + mo2Base.Replace('\\', '/') +
        "\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\n" +
        "[General]\nselected_profile=Active\n");
    var singleInstance = Mo2InstallLocator.Inspect(mo2ExecutableDir,
        localAppData: exampleAppData);
    Require(singleInstance.HasConfiguration && !singleInstance.IsPortable &&
        singleInstance.InstanceFolder == firstInstance,
        "one global MO2 instance is selected from LocalAppData");

    var preferred = Mo2SourceInspector.Discover(firstInstance);
    Require(preferred.PreferredProfile == "Active" && preferred.Profiles.Contains("Active"),
        "MO2 preferred profile is read from selected_profile rather than guessed from folder name");

    Directory.CreateDirectory(secondInstance);
    File.WriteAllText(Path.Combine(secondInstance, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + mo2Base.Replace('\\', '/') +
        "\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\n");
    var ambiguous = Mo2InstallLocator.Inspect(mo2ExecutableDir, localAppData: exampleAppData);
    Require(!ambiguous.HasConfiguration && ambiguous.InstanceChoices.Count == 2,
        "multiple MO2 instances require explicit instance selection; neither is assumed active");
    var chosen = Mo2InstallLocator.Inspect(mo2ExecutableDir, firstInstance, exampleAppData);
    Require(chosen.HasConfiguration && chosen.InstanceFolder == firstInstance,
        "user-selected instance resolves its own configured directories");

    var portable = Path.Combine(root, "Portable MO2");
    Directory.CreateDirectory(portable);
    File.WriteAllText(Path.Combine(portable, "ModOrganizer.exe"), "fixture");
    File.WriteAllText(Path.Combine(portable, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + mo2Base.Replace('\\', '/') +
        "\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\n" +
        "[General]\nselected_profile=Active\n");
    var detectedPortable = Mo2InstallLocator.Inspect(portable, localAppData: exampleAppData);
    Require(detectedPortable.IsPortable && detectedPortable.HasConfiguration &&
        detectedPortable.InstanceFolder == portable &&
        Mo2SourceInspector.Discover(detectedPortable.InstanceFolder).PreferredProfile == "Active",
        "portable MO2 resolves INI and preferred profile within installation folder");
    Require(Mo2SourceInspector.Discover(detectedPortable.InstanceFolder)
            .SelectedModlistPath("Active") == Path.Combine(activeProfile, "modlist.txt"),
        "portable MO2 finds active profile modlist from the same selected INI");

    var manualInstance = Path.Combine(root, "Manual instance on another drive");
    Directory.CreateDirectory(manualInstance);
    File.WriteAllText(Path.Combine(manualInstance, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + mo2Base.Replace('\\', '/') +
        "\nmod_directory=%BASE_DIR%/mods\nprofiles_directory=%BASE_DIR%/profiles\n");
    var manual = Mo2InstallLocator.Inspect(mo2ExecutableDir, manualInstance, exampleAppData);
    Require(manual.HasConfiguration && manual.InstanceFolder == manualInstance,
        "manual instance can be selected outside LocalAppData");

    // A missing configured Mods directory requires an explicit replacement and
    // validated enabled package names; arbitrary unowned files never grant checks.
    var brokenConfig = Path.Combine(root, "Broken MO2 instance");
    Directory.CreateDirectory(brokenConfig);
    File.WriteAllText(Path.Combine(brokenConfig, "ModOrganizer.ini"),
        "[Settings]\nbase_directory=" + mo2Base.Replace('\\', '/') +
        "\nmod_directory=" + Path.Combine(root, "not-real").Replace('\\', '/') +
        "\nprofiles_directory=%BASE_DIR%/profiles\n");
    var missingConfiguredMods = Mo2SourceInspector.Discover(brokenConfig);
    Require(missingConfiguredMods.Recognized && !Directory.Exists(missingConfiguredMods.ModsDirectory),
        "missing Mods directory retains configured profile discovery but cannot verify enabled packages");
    var missingConfiguredStack = Mo2SourceInspector.Resolve(emptyPhysicalGame,
        missingConfiguredMods, "Active");
    Require(missingConfiguredStack.Ready && missingConfiguredStack.GameOnlyFallback &&
        !missingConfiguredStack.ProfileVerified,
        "missing configured Mods directory excludes MO2 layers but preserves the physical game");
    var recovered = Mo2SourceInspector.Discover(brokenConfig, mo2Mods);
    Require(recovered.Recognized && recovered.ModsDirectory == mo2Mods,
        "manual Mods folder override restores INI/profile-based source resolution");

    // CET ASI coverage must come from a physical game file or enabled package,
    // not from disabled or inactive packages.
    var asiInEnabled = Path.Combine(mo2Mods, "High", "bin", "x64", "plugins",
        "cyber_engine_tweaks.asi");
    Directory.CreateDirectory(Path.GetDirectoryName(asiInEnabled)!);
    File.WriteAllText(asiInEnabled, "enabled ASI");
    var profileAsi = Mo2SourceInspector.Resolve(emptyPhysicalGame, recovered, "Active");
    Require(profileAsi.Ready && profileAsi.HasCetAsi &&
        profileAsi.CetOwners > 0 && profileAsi.HasRedscript,
        "enabled active-profile ASI/CET/REDscript resolved from winning physical files");

    var brokenList = Path.Combine(mo2Base, "profiles", "Active", "modlist.txt");
    var previousList = File.ReadAllText(brokenList);
    try
    {
        File.WriteAllText(brokenList, "+Missing Enabled Package\n" + previousList);
        var missingEnabled = Mo2SourceInspector.Resolve(emptyPhysicalGame, recovered, "Active");
        Require(missingEnabled.Ready && missingEnabled.GameOnlyFallback &&
            !missingEnabled.ProfileVerified && missingEnabled.EffectiveFiles.Values.All(f => !f.FromMo2),
            "missing enabled package excludes all MO2 packages while preserving physical-game source");
    }
    finally { File.WriteAllText(brokenList, previousList); }

    var noGame = Mo2SourceInspector.Resolve(null, recovered, "Active");
    Require(!noGame.Ready && noGame.CetOwners == 0,
        "without verified game root, a matching MO2 profile cannot claim active checks");

    var portableBase = Path.Combine(root, "Portable Optimizer Package");
    var portableApp = Path.Combine(portableBase, "app");
    Directory.CreateDirectory(portableApp);
    File.WriteAllText(Path.Combine(portableApp, PortablePackagePaths.ManagedExecutableName), "fixture");
    Require(PortablePackagePaths.RootFromAppDirectory(portableApp) == portableBase,
        "CET-style portable layout resolves package root from nested managed app");
    File.Delete(Path.Combine(portableApp, PortablePackagePaths.ManagedExecutableName));
    Require(PortablePackagePaths.RootFromAppDirectory(portableApp) == portableBase,
        "package-root detection cannot fail because the private EXE is missing or locked");
    File.WriteAllText(Path.Combine(portableApp, PortablePackagePaths.ManagedExecutableName), "fixture");
    var unrelatedBase = Path.Combine(root, "Wrong Working Folder", "app");
    Require(PortablePackagePaths.RootFromExecutablePath(
            Path.Combine(portableApp, PortablePackagePaths.ManagedExecutableName), unrelatedBase) == portableBase,
        "real managed process path wins over unrelated base-directory or working-directory hints");
    Require(PortablePackagePaths.RootFromExecutablePath(null, portableApp) == portableBase,
        "process-path unavailable falls back to the private app directory");
    Require(!Mo2FeatureGate.Enabled,
        "MO2 disabled setting cannot affect component package-root resolution");
    Require(PortablePackagePaths.RootFromAppDirectory(root) == Path.GetFullPath(root),
        "IDE/nonportable debug layout keeps its original package path");
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
if (args.Length > 0)
{
    Require(args.Length == 1, "exactly one packaged GCO directory supplied");
    var delivered = Path.GetFullPath(args[0]);
    Require(Directory.Exists(delivered), "finished GCO package directory exists");
    Require(PortablePackagePaths.RootFromAppDirectory(Path.Combine(delivered, "app")) == delivered,
        "actual packaged managed app resolves root shared with native launcher and components");
    Require(PortablePackagePaths.RootFromExecutablePath(
            Path.Combine(delivered, "app", PortablePackagePaths.ManagedExecutableName),
            Path.Combine(delivered, "unrelated")) == delivered,
        "real delivered executable selects correct manifest root despite unrelated host-directory hint");
    Require(File.Exists(Path.Combine(delivered, "G-Total-Optimizer.exe")),
        "final GCO package includes public native launcher");
    var verification = ComponentBundleVerifier.Inspect(delivered);
    Require(verification.Ready,
        "finished CI package contains both profiler managers and all required verified payloads: " +
        verification.Detail);
}
Console.WriteLine($"PASS {checks} installation/CLI/package contracts.");
