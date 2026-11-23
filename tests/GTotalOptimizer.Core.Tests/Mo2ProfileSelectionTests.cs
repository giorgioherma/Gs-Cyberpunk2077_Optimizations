using GTotalOptimizer.Core;

internal static class Mo2ProfileSelectionTests
{
    public static void Run()
    {
        string home = Path.Combine(Path.GetTempPath(), "gco-mo2-selection-" + Guid.NewGuid().ToString("N"));
        try
        {
            string mo2 = Path.Combine(home, "MO2");
            string game = Path.Combine(home, "Cyberpunk 2077");
            string baseDir = Path.Combine(home, "separate-instance-data");
            string mods = Path.Combine(baseDir, "mods");
            string profiles = Path.Combine(baseDir, "profiles");
            static void Put(string path, string value)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, value);
            }

            Put(Path.Combine(mo2, "ModOrganizer.ini"),
                "[Settings]\nbase_directory=" + baseDir + "\n" +
                "mod_directory=" + mods + "\n" +
                "profiles_directory=" + profiles + "\n" +
                "overwrite_directory=" + Path.Combine(baseDir, "overwrite") + "\n" +
                "selected_profile=Heavy\n");
            Put(Path.Combine(game, "bin", "x64", "Cyberpunk2077.exe"), "exe");
            Put(Path.Combine(game, "bin", "x64", "plugins", "cyber_engine_tweaks",
                "mods", "Physical", "init.lua"), "physical");
            Put(Path.Combine(mods, "Alpha", "bin", "x64", "plugins", "cyber_engine_tweaks",
                "mods", "Alpha", "init.lua"), "alpha");
            Put(Path.Combine(mods, "Beta", "r6", "scripts", "Beta", "mod.reds"), "beta");
            Put(Path.Combine(profiles, "Heavy", "modlist.txt"), "+Alpha\n+Beta\n");
            Put(Path.Combine(profiles, "Minimal", "modlist.txt"), "+Beta\n-Alpha\n");

            var heavy = Mo2ProfileSelectionService.Inspect(mo2, null, "Heavy", game);
            if (!heavy.Recognized || heavy.CandidateSource is null ||
                heavy.CandidateSource.CetOwners != 2 ||
                heavy.CandidateSource.RedscriptFiles != 1 ||
                heavy.AvailableProfiles.Count != 2 ||
                heavy.AllowsProfilerInstallation || heavy.AllowsGeneration)
                throw new InvalidDataException("MO2_HEAVY_PROFILE_SELECTION_OR_READONLY_GUARD_FAILED");

            var minimal = Mo2ProfileSelectionService.Inspect(mo2, null, "Minimal", game);
            if (!minimal.Recognized || minimal.CandidateSource?.CetOwners != 1 ||
                minimal.CandidateSource.RedscriptFiles != 1)
                throw new InvalidDataException("MO2_SELECTED_PROFILE_NOT_ISOLATED");

            var invalid = Mo2ProfileSelectionService.Inspect(mo2, null, "OtherInstanceProfile", game);
            if (invalid.Recognized || invalid.CandidateSource != null)
                throw new InvalidDataException("MO2_UNKNOWN_PROFILE_ACCEPTED");

            var wrongGame = Mo2ProfileSelectionService.Inspect(mo2, null, "Heavy", home);
            if (wrongGame.Recognized)
                throw new InvalidDataException("MO2_UNVERIFIED_GAME_ACCEPTED");

            File.Delete(Path.Combine(profiles, "Heavy", "modlist.txt"));
            var absent = Mo2ProfileSelectionService.Inspect(mo2, null, "Heavy", game);
            if (absent.Recognized || absent.CandidateSource?.GameOnlyFallback != true)
                throw new InvalidDataException("MO2_MISSING_MODLIST_TREATED_AS_VERIFIED_PROFILE");
        }
        finally
        {
            if (Directory.Exists(home)) Directory.Delete(home, recursive: true);
        }
        Console.WriteLine("PASS MO2 selected-profile source admission and read-only safety");
    }
}
