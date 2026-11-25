using GTotalOptimizer.Core;
using System.Security.Cryptography;
using System.Text.Json;

internal static class Mo2RealOwnerOverlayTests
{
    public static async Task RunAsync(string verifiedPackageRoot, string officialCetAsi,
        string? realZeroEngineDirectory = null, string scenario = "namespaced-complete")
    {
        if (!File.Exists(officialCetAsi))
            throw new FileNotFoundException("Official CET fixture binary unavailable.", officialCetAsi);
        var bundle = ComponentBundleVerifier.Inspect(verifiedPackageRoot);
        if (!bundle.Ready)
            throw new InvalidDataException("GCO_MO2_OWNER_BUNDLE_UNVERIFIED: " + bundle.Detail);

        var folder = Path.Combine(Path.GetTempPath(), "gco-mo2-realowner-" + Guid.NewGuid().ToString("N"));
        const string sessionId = "abcdef0123456789abcdef0123456789";
        try
        {
            string game = Path.Combine(folder, "game");
            string instance = Path.Combine(folder, "MO2-instance");
            string local = Path.Combine(folder, "local");
            string asiRel = Path.Combine("bin", "x64", "plugins", "cyber_engine_tweaks.asi");
            string asi = Path.Combine(game, asiRel);
            Directory.CreateDirectory(Path.GetDirectoryName(asi)!);
            File.Copy(officialCetAsi, asi);
            var sourceSha = Sha256(asi);
            Directory.CreateDirectory(Path.Combine(game, "red4ext", "plugins"));
            File.WriteAllBytes(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), []);

            void Put(string relative, string value)
            {
                string target = Path.Combine(instance, relative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, value);
            }
            Put("ModOrganizer.ini", "[Settings]\nbase_directory=.\nselected_profile=Heavy\n");
            Put("profiles/Heavy/modlist.txt", "+SimpleCETMod\n");
            Put("profiles/Other/modlist.txt", "-SimpleCETMod\n");
            Put("mods/SimpleCETMod/bin/x64/plugins/cyber_engine_tweaks/mods/Example/init.lua",
                "registerForEvent('onInit', function() end)\n");
            // Optional private *historical real source* fixture. Never commit
            // or upload these files; CI checks them out read-only in RUNNER_TEMP.
            string? sourceZeroHash = null;
            if (scenario is not ("namespaced-complete" or "namespaced-worker-missing" or
                                 "original-adaptive" or "legacy-integrated" or "legacy-worker-missing"))
                throw new InvalidDataException("MO2_UNKNOWN_REAL_OWNER_TEST_SCENARIO");
            if (scenario != "namespaced-complete" && string.IsNullOrWhiteSpace(realZeroEngineDirectory))
                throw new InvalidDataException("MO2_VARIANT_0ENGINE_REQUIRES_REAL_SOURCE");
            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory))
            {
                var source = Path.GetFullPath(realZeroEngineDirectory);
                var sourceInit = Path.Combine(source, "init.lua");
                if (!File.Exists(sourceInit))
                    throw new InvalidDataException("MO2_REAL_0ENGINE_SOURCE_INIT_MISSING");
                sourceZeroHash = Sha256(sourceInit);
                var destination = Path.Combine(instance, "mods", "SimpleCETMod", "bin", "x64",
                    "plugins", "cyber_engine_tweaks", "mods", "0-Engine");
                int files = 0;
                foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
                {
                    if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("MO2_REAL_0ENGINE_FIXTURE_LINKED_INPUT_REFUSED");
                    if (++files > 5000)
                        throw new InvalidDataException("MO2_REAL_0ENGINE_FIXTURE_OVERSIZED");
                    var relative = Path.GetRelativePath(source, file);
                    var normalized = relative.Replace('\\', '/');
                    if ((scenario == "namespaced-worker-missing" &&
                         normalized.Equals("modules/G-CET/Scheduler.lua",
                             StringComparison.OrdinalIgnoreCase)) ||
                        (scenario == "legacy-worker-missing" &&
                         normalized.Equals("modules/Scheduler.lua",
                             StringComparison.OrdinalIgnoreCase)))
                        continue; // Simulate missing worker, preserving exact host init.
                    var target = Path.Combine(destination, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(file, target);
                    if (Sha256(file) != Sha256(target))
                        throw new InvalidDataException("MO2_REAL_0ENGINE_INPUT_PROJECTION_MISMATCH");
                }
            }
            // Reinstall following a completed MO2 capture: Overwrite is
            // evidence, not a live GRSP manager installation footprint.
            const string prior = "red4ext/plugins/G-REDscript-Profiler/RESULTS/Capture_0001_WORLD_123/GRSP_Summary.csv";
            const string priorStatus = "red4ext/plugins/G-REDscript-Profiler/RESULTS/GRSP_Status.txt";
            Put("overwrite/" + prior, "previous,completed,capture");
            Put("overwrite/" + priorStatus, "previous status");
            // Reproduce a second installation after a previous MO2 capture:
            // runtime sidecars remain in Overwrite, but no controls source.
            const string controlsLog = "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/CETProfilerControls.log";
            const string controlsDb = "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/db.sqlite3";
            const string exampleLog = "bin/x64/plugins/cyber_engine_tweaks/mods/Example/Example.log";
            const string exampleDb = "bin/x64/plugins/cyber_engine_tweaks/mods/Example/db.sqlite3";
            foreach (var residue in new[] { controlsLog, controlsDb, exampleLog, exampleDb })
                Put("overwrite/" + residue, "previous runtime state");
            var details = Mo2SourceInspector.Discover(instance);
            var effective = Mo2SourceInspector.Resolve(game, details, "Heavy");
            var expectedCetOwners = string.IsNullOrWhiteSpace(realZeroEngineDirectory) ? 1 : 2;
            if (!effective.Ready || !effective.ProfileVerified || effective.GameOnlyFallback ||
                !effective.HasCetAsi || effective.CetOwners != expectedCetOwners)
                throw new InvalidDataException("MO2_REAL_OWNER_SELECTED_PROFILE_NOT_ADMITTED");

            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory) &&
                !effective.HasZeroEngine)
                throw new InvalidDataException("MO2_REAL_0ENGINE_EFFECTIVE_WINNER_NOT_ADMITTED");
            var output = await Mo2ProfilerOverlayBuilder.PrepareFromBundleAsync(
                verifiedPackageRoot, game, effective, sessionId, localAppData: local);
            var names = output.Files.Select(x => x.GameRelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            const string init = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/init.lua";
            const string namespaced = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/G-CET/Scheduler.lua";
            const string rootScheduler = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/Scheduler.lua";
            const string adaptiveScheduler = "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/CETProfilerScheduler.lua";
            // These are source-owner postconditions, not replacement Lua
            // implementation: only the recognized current state may change.
            if (realZeroEngineDirectory is not null)
            {
                bool ownsInit = names.Contains(init);
                bool ownsRoot = names.Contains(rootScheduler);
                bool ownsAdaptive = names.Contains(adaptiveScheduler);
                bool ownsNamespaced = names.Contains(namespaced);
                bool good = scenario switch
                {
                    "namespaced-complete" => !ownsInit && !ownsRoot && !ownsAdaptive && !ownsNamespaced,
                    "namespaced-worker-missing" => !ownsInit && !ownsRoot && !ownsAdaptive && ownsNamespaced,
                    "original-adaptive" => ownsInit && !ownsRoot && ownsAdaptive && !ownsNamespaced,
                    "legacy-integrated" => !ownsInit && !ownsRoot && !ownsAdaptive && !ownsNamespaced,
                    "legacy-worker-missing" => !ownsInit && ownsRoot && !ownsAdaptive && !ownsNamespaced,
                    _ => false
                };
                if (!good)
                    throw new InvalidDataException(
                        "MO2_REAL_OWNER_WORKER_POSTCONDITION_FAILED: scenario=" + scenario +
                        " init=" + ownsInit + " root=" + ownsRoot +
                        " adaptive=" + ownsAdaptive + " namespaced=" + ownsNamespaced);
            }
            if (!names.Contains("bin/x64/plugins/cyber_engine_tweaks.asi") ||
                !names.Contains("red4ext/plugins/G-REDscript-Profiler.dll") ||
                !names.Contains("bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua"))
                throw new InvalidDataException("MO2_REAL_OWNER_PROFILER_PAYLOAD_INCOMPLETE");

            foreach (var item in output.Files)
            {
                var full = Path.Combine(output.StagingDirectory,
                    item.GameRelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full) || Sha256(full) != item.Sha256 ||
                    new FileInfo(full).Length != item.Size)
                    throw new InvalidDataException("MO2_REAL_OWNER_STAGED_OUTPUT_HASH_MISMATCH");
            }
            if (Sha256(asi) != sourceSha ||
                File.Exists(Path.Combine(game, "red4ext", "plugins", "G-REDscript-Profiler.dll")) ||
                Directory.Exists(Path.Combine(game, "bin", "x64", "plugins",
                    ".cet_runtime_profiler")) ||
                Directory.Exists(Path.Combine(game, "bin", "x64", "plugins",
                    "cyber_engine_tweaks", "mods", "CETProfilerControls")))
                throw new InvalidDataException("MO2_REAL_OWNER_CHANGED_LIVE_GAME");

            using var json = JsonDocument.Parse(File.ReadAllText(output.JournalFile));
            if (json.RootElement.GetProperty("phase").GetString() != "prepared")
                throw new InvalidDataException("MO2_REAL_OWNER_SESSION_NOT_JOURNALED");
            if (json.RootElement.TryGetProperty("priorGrspResultsRoot", out _))
                throw new InvalidDataException("MO2_REAL_OWNER_INSTALL_ARCHIVED_UNCOLLECTED_RESULTS");
            foreach (var relative in new[] { prior, priorStatus })
            {
                var original = Path.Combine(instance, "overwrite", relative);
                if (!File.Exists(original) ||
                    File.Exists(Path.Combine(output.StagingDirectory, relative)))
                    throw new InvalidDataException("MO2_REAL_OWNER_INSTALL_CHANGED_LIVE_OUTPUT");
            }
            Mo2ProfilerOverlayBuilder.VerifyPrepared(output, effective);
            foreach (var residue in new[] { controlsLog, controlsDb, exampleLog, exampleDb })
            {
                if (File.ReadAllText(Path.Combine(instance, "overwrite", residue)) != "previous runtime state" ||
                    names.Contains(residue) ||
                    json.RootElement.GetProperty("originalSources").TryGetProperty(residue, out _))
                    throw new InvalidDataException("MO2_CET_RUNTIME_SIDECAR_TREATED_AS_SOURCE: " + residue);
            }
            // Runtime state can change without changing executable sources.
            // Keep the real owner archive, restore and Page 2 checks active.
            foreach (var residue in new[] { controlsLog, controlsDb, exampleLog, exampleDb })
                Put("overwrite/" + residue, "updated runtime state");
            Mo2ProfilerOverlayBuilder.VerifyPrepared(output,
                Mo2SourceInspector.Resolve(game, details, "Heavy"));
            if (realZeroEngineDirectory is null)
            {
                const string unknown = "red4ext/plugins/G-REDscript-Profiler/foreign-config.json";
                Put("overwrite/" + unknown, "foreign configuration");
                var foreign = Mo2SourceInspector.Resolve(game, details, "Heavy");
                try
                {
                    await Mo2ProfilerOverlayBuilder.PrepareFromBundleAsync(
                        verifiedPackageRoot, game, foreign, "fedcba9876543210fedcba9876543210",
                        localAppData: local);
                    throw new Exception("MO2_REAL_GRSP_FOREIGN_INSTALL_DATA_ACCEPTED");
                }
                catch (InvalidOperationException ex) when (
                    ex.Message.Contains("Authoritative GRSP owner refused", StringComparison.Ordinal))
                {
                    if (File.ReadAllText(Path.Combine(instance, "overwrite", unknown)) != "foreign configuration" ||
                        File.ReadAllText(Path.Combine(instance, "overwrite", prior)) != "previous,completed,capture")
                        throw new InvalidDataException("MO2_REAL_GRSP_REFUSAL_MODIFIED_ORIGINAL");
                    Console.WriteLine("PASS real GRSP owner still refuses foreign non-result data");
                }
            }
            Mo2ProfilerOverlayBuilder.DiscardBeforeStart(output);
            if (File.Exists(Path.Combine(instance, "overwrite", prior)) ||
                File.Exists(Path.Combine(instance, "overwrite", priorStatus)) ||
                Directory.Exists(Path.Combine(local, "GCO", "MO2Bridge", "recovery", sessionId, "prior-grsp-results")))
                throw new InvalidDataException("MO2_REAL_OWNER_START_FAILED_TO_DISCARD_OLD_OUTPUT");
            if (!string.IsNullOrWhiteSpace(realZeroEngineDirectory) &&
                (Sha256(Path.Combine(realZeroEngineDirectory, "init.lua")) != sourceZeroHash ||
                !File.Exists(Path.Combine(instance, "mods", "SimpleCETMod", "bin", "x64",
                    "plugins", "cyber_engine_tweaks", "mods", "0-Engine", "init.lua"))))
                throw new InvalidDataException("MO2_REAL_0ENGINE_HISTORICAL_SOURCE_CHANGED");
            if (realZeroEngineDirectory is null)
            {
                // Only this disposable test created the foreign refusal file.
                File.Delete(Path.Combine(instance, "overwrite", "red4ext", "plugins",
                    "G-REDscript-Profiler", "foreign-config.json"));
                await Mo2RealCollectionTests.RunAsync(verifiedPackageRoot, game, instance, local, output);
            }
            else if (scenario == "namespaced-complete")
            {
                await Mo2RealCollectionTests.RunAsync(verifiedPackageRoot, game, instance, local,
                    output, verifyPage2: true);
            }
            foreach (var residue in new[] { controlsLog, controlsDb, exampleLog, exampleDb })
                if (File.ReadAllText(Path.Combine(instance, "overwrite", residue)) != "updated runtime state")
                    throw new InvalidDataException("MO2_CET_RUNTIME_SIDECAR_NOT_PRESERVED: " + residue);
            Console.WriteLine("PASS real CET owner accepts and preserves residual controls log/database and mod runtime state");
            if (realZeroEngineDirectory is null)
            {
                // Additive real-owner proof of the physical native deployment.
                // The original virtual-overlay and collection contracts above
                // remain unchanged. This uses actual owner-produced ASI bytes
                // and the actual CET manager's original backup.
                var physicalLocal = Path.Combine(folder, "physical-native-local");
                var fresh = Mo2SourceInspector.Resolve(game, details, "Heavy");
                var physical = await Mo2ProfilerOverlayBuilder.PrepareFromBundleAsync(
                    verifiedPackageRoot, game, fresh, Guid.NewGuid().ToString("N"), localAppData: physicalLocal);
                var expectedNative = physical.Files.Single(f =>
                    f.GameRelativePath.Equals(Mo2ProfilerOverlayBuilder.CetAsiRelative, StringComparison.OrdinalIgnoreCase));
                Mo2ProfilerOverlayBuilder.VerifyPrepared(physical, fresh);
                Mo2ProfilerOverlayBuilder.ActivatePhysicalCetForLaunch(physical, fresh);
                if (Sha256(asi) != expectedNative.Sha256 ||
                    File.Exists(Path.Combine(physical.StagingDirectory, Mo2ProfilerOverlayBuilder.CetAsiRelative)) ||
                    !File.Exists(Path.Combine(physical.StagingDirectory, "red4ext", "plugins", "G-REDscript-Profiler.dll")))
                    throw new InvalidDataException("MO2_REAL_PHYSICAL_CET_NATIVE_DEPLOYMENT_FAILED");
                if (Mo2ProfilerOverlayBuilder.RestoreOutstandingSessions(physicalLocal, () => { }) != 1 ||
                    Sha256(asi) != sourceSha)
                    throw new InvalidDataException("MO2_REAL_PHYSICAL_CET_NATIVE_RESTORE_FAILED");
                Console.WriteLine("PASS real owner physical CET ASI exact deployment and restore; GRSP remains virtual");
            }
            Console.WriteLine("PASS Stage 3 REAL CET + GRSP owner install on disposable " +
                (realZeroEngineDirectory is null ? "original no-0-Engine" :
                    "historical 0-Engine " + scenario) +
                " profile fixture: " + output.Files.Count +
                " verified virtual overlay files; game and mod originals unchanged");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
    }

    private static string Sha256(string file)
    {
        using var source = File.OpenRead(file);
        return Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant();
    }
}
