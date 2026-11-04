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
    Require(SetupPageLayout.FitsWithoutScroll(SetupPageLayout.ReferenceClientHeight, mo2: false),
        "Normal setup fits reference window without a scrollbar");
    Require(SetupPageLayout.FitsWithoutScroll(SetupPageLayout.ReferenceClientHeight, mo2: true),
        "MO2 setup fits reference window without a scrollbar");
    Require(SetupPageLayout.WorkflowHeight(SetupPageLayout.ReferenceClientHeight, mo2: true) >=
        SetupPageLayout.MinimumWorkflowHeight, "MO2 tab preserves all three workflow steps");
    Require(!SetupPageLayout.FitsWithoutScroll(SetupPageLayout.ReferenceClientHeight - 110, mo2: true),
        "layout contract detects an undersized window instead of claiming no clipping");
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
    var anywhere = Mo2SourceInspector.Resolve(emptyPhysicalGame, anyLocation, "Random Profile");
    Require(anywhere.Ready && anywhere.RedscriptFiles == 1 &&
        RedscriptModCounter.CountEffective(anywhere) == 1,
        "MO2 source scan works without a direct installation from arbitrary paths");

    var unmanaged = Mo2SourceInspector.Discover(Path.Combine(root, "nonexistent-MO2"));
    Require(!unmanaged.Recognized, "missing MO2 instance is refused");


    // Flexible MO2 content-first sources: each folder may contain one, several,
    // or all four components. Do not require a game-root or MO2 instance here.
    var mo2Sources = Path.Combine(root, "arbitrary source locations");
    var asiOnly = Path.Combine(mo2Sources, "anywhere one");
    var cetOnly = Path.Combine(mo2Sources, "another folder");
    var redsOnly = Path.Combine(mo2Sources, "third disk candidate");
    var red4extOnly = Path.Combine(mo2Sources, "fourth folder");

    var asiFile = Path.Combine(asiOnly, "bin", "x64", "plugins", "cyber_engine_tweaks.asi");
    var luaInit = Path.Combine(cetOnly, "bin", "x64", "plugins",
        "cyber_engine_tweaks", "mods", "0-Engine", "init.lua");
    var redFile = Path.Combine(redsOnly, "r6", "scripts", "Mods", "runtime.reds");
    var pluginDll = Path.Combine(red4extOnly, "red4ext", "plugins", "SomePlugin", "SomePlugin.dll");
    foreach (var file in new[] { asiFile, luaInit, redFile, pluginDll })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
    }

    var emptySources = Mo2FolderSources.Inspect(["", "", "", ""]);
    Require(emptySources.VisibleFolders == 1 && !emptySources.Complete,
        "MO2 initially shows only one empty folder, with four unchecked components");

    var fourSources = Mo2FolderSources.Inspect([asiOnly, cetOnly, redsOnly, red4extOnly]);
    Require(fourSources.Complete && fourSources.VisibleFolders == 4 &&
        fourSources.UsedFolders == 4 && fourSources.HasZeroEngine,
        "four unrelated physical folders reveal all four rows and recognize all four file types");
    Require(fourSources.CetOwners == 1 && fourSources.RedscriptOwners == 1,
        "CET owner requires init.lua and REDscript owner is not the raw file count");
    Require(fourSources.Has(ModSourceParts.CetAsi) && fourSources.Has(ModSourceParts.CetMods) &&
        fourSources.Has(ModSourceParts.Redscript) && fourSources.Has(ModSourceParts.Red4ext),
        "four MO2 checkboxes are derived from file content, not folder names");

    var incomplete = Mo2FolderSources.Inspect([asiOnly, cetOnly, "", ""]);
    Require(!incomplete.Complete && incomplete.VisibleFolders == 3 &&
        incomplete.Has(ModSourceParts.CetAsi) && incomplete.Has(ModSourceParts.CetMods) &&
        !incomplete.Has(ModSourceParts.Redscript),
        "third slot appears when two folders do not contain all four components");

    var twoFolderFirst = Path.Combine(mo2Sources, "random combined A");
    var twoFolderSecond = Path.Combine(mo2Sources, "random combined B");
    var twoAsi = Path.Combine(twoFolderFirst, "other", "CET.asi");
    var twoInit = Path.Combine(twoFolderFirst, "mods", "SomeMod", "init.lua");
    var twoReds = Path.Combine(twoFolderSecond, "r6", "scripts", "MyScript", "main.reds");
    var twoDll = Path.Combine(twoFolderSecond, "red4ext", "plugins", "MyPlugin.dll");
    foreach (var file in new[] { twoAsi, twoInit, twoReds, twoDll })
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
    }
    var pair = Mo2FolderSources.Inspect([twoFolderFirst, twoFolderSecond, redsOnly, red4extOnly]);
    Require(pair.Complete && pair.VisibleFolders == 2 && pair.UsedFolders == 2,
        "two physical folders cover all four components and hide rows three and four");

    var threeFirst = Path.Combine(mo2Sources, "three 1");
    var threeSecond = Path.Combine(mo2Sources, "three 2");
    var threeThird = Path.Combine(mo2Sources, "three 3");
    foreach (var (folder, relative) in new[]
    {
        (threeFirst, "cyber_engine_tweaks.asi"),
        (threeSecond, "mods/One/init.lua"),
        (threeSecond, "r6/scripts/C/main.reds"),
        (threeThird, "red4ext/plugins/My.dll")
    })
    {
        var file = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "fixture");
    }
    var triple = Mo2FolderSources.Inspect([threeFirst, threeSecond, threeThird, red4extOnly]);
    Require(triple.Complete && triple.VisibleFolders == 3 && triple.UsedFolders == 3,
        "three source folders automatically hide the fourth chooser");

    var allAtOnce = Path.Combine(mo2Sources, "one random folder");
    foreach (var original in new[] { asiFile, luaInit, redFile, pluginDll })
    {
        var relative = original.StartsWith(asiOnly) ? Path.GetRelativePath(asiOnly, original)
            : original.StartsWith(cetOnly) ? Path.GetRelativePath(cetOnly, original)
            : original.StartsWith(redsOnly) ? Path.GetRelativePath(redsOnly, original)
            : Path.GetRelativePath(red4extOnly, original);
        var copy = Path.Combine(allAtOnce, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.Copy(original, copy);
    }
    var single = Mo2FolderSources.Inspect([allAtOnce, asiOnly, cetOnly, redsOnly]);
    Require(single.Complete && single.VisibleFolders == 1 && single.UsedFolders == 1,
        "one folder containing all four recognized components hides the other three rows");
    Require(single.CetOwners == 1 && single.RedscriptOwners == 1,
        "one-folder source inventory counts the CET and REDscript owners");

    var sameFolderTwice = Mo2FolderSources.Inspect([asiOnly, asiOnly, redsOnly, red4extOnly]);
    Require(!sameFolderTwice.Complete && sameFolderTwice.UsedFolders == 1 &&
        sameFolderTwice.Folders[1].Detail.Contains("Duplicate") &&
        sameFolderTwice.VisibleFolders == 2,
        "duplicate physical source never falsely satisfies a missing component");

    var noFiles = Path.Combine(mo2Sources, "empty no mods");
    Directory.CreateDirectory(noFiles);
    var noContent = Mo2FolderSources.Inspect([noFiles, "", "", ""]);
    Require(!noContent.Complete && noContent.VisibleFolders == 2,
        "a folder is not accepted as a component merely because it exists");
    Require(!Mo2FolderSources.Inspect([Path.Combine(mo2Sources, "not found"), "", "", ""]).Complete,
        "missing directory does not create a false checked component");

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
