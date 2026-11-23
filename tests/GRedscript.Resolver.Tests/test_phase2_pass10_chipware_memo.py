"""Approved Chipware 100ms perk memoization: source-state / one-go contracts."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = (ROOT / "tests/GRedscript.Pass8.Checkpoint/bin/Release/net10.0/"
          "GRedscript.Pass8.Checkpoint.dll")
PERK = """@wrapMethod(PlayerDevelopmentData)
public final const func IsNewPerkBought(perkType: gamedataNewPerkType) -> Int32 {
    let game = GetGameInstance();
    let player = GetPlayer(game);
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
    let playerMemory: Float = statsSystem.GetStatValue(
        Cast<StatsObjectID>(player.GetEntityID()), gamedataStatType.Memory);
    if (Equals(gamedataNewPerkType.Cool_Left_Milestone_2, perkType)) {
        if statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), gamedataStatType.Health) > 0.0 { return 2; }
    }
    if (Equals(gamedataNewPerkType.Tech_Central_Milestone_3, perkType)) {
        if playerMemory > 0.0 { return 3; }
    }
    return wrappedMethod(perkType);
}
"""
HELPER = """public class GRedChipwareHotpathCache extends IScriptable {
    private let m_hasKerenzikov: Bool;
    public func HasKerenzikov(player: ref<PlayerPuppet>) -> Bool {
        return this.m_hasKerenzikov;
    }
}
@addField(PlayerPuppet)
private let m_gredChipwareHotpathCache: ref<GRedChipwareHotpathCache>;
@addMethod(PlayerPuppet)
public final func GRedChipwareHotpathCache() -> ref<GRedChipwareHotpathCache> {
    if !IsDefined(this.m_gredChipwareHotpathCache) {
        this.m_gredChipwareHotpathCache = new GRedChipwareHotpathCache();
    }
    return this.m_gredChipwareHotpathCache;
}
"""
PERK_PATH = "r6/scripts/zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds"
HELPER_PATH = "r6/scripts/zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds"

class ChipwareMemoContracts(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root = Path(t.name)
        self.counter = 0

    def generate(self, sources, overlay=None):
        self.counter += 1
        game = self.root / f"game-{self.counter}"
        for name, value in sources.items():
            target = game / name
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(value, encoding="utf8")
        (game / "r6/scripts").mkdir(parents=True, exist_ok=True)
        out = self.root / f"out-{self.counter}"
        args = ["dotnet", str(RUNNER), "--owner", "memo",
                "--game", str(game), "--out", str(out)]
        if overlay is not None:
            args += ["--base-overlay", str(overlay)]
        proc = subprocess.run(args, capture_output=True, text=True, timeout=90)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        for name, value in sources.items():
            self.assertEqual((game / name).read_text(encoding="utf8"), value)
        result = json.loads((out / "phase2-result.json").read_text(encoding="utf8"))
        files = {p.relative_to(out).as_posix(): p.read_text(encoding="utf8")
                 for p in out.rglob("*.reds")}
        return result, files

    def test_stock_generates_source_derived_dispatch_and_historical_100ms_cache(self):
        result, files = self.generate({PERK_PATH: PERK, HELPER_PATH: HELPER})
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(set(files), {PERK_PATH, HELPER_PATH})
        self.assertIn("TryGetPerkResult(cacheIndex, player, cachedResult)", files[PERK_PATH])
        self.assertEqual(files[PERK_PATH].count("cache.StorePerkResult(cacheIndex, player,"), 3)
        self.assertIn("wrappedMethod(perkType)", files[PERK_PATH])
        self.assertIn("case gamedataNewPerkType.Cool_Left_Milestone_2: cacheIndex = 0;", files[PERK_PATH])
        self.assertIn("ArrayResize(this.m_perkResult, 2);", files[HELPER_PATH])
        self.assertIn("+ 0.10", files[HELPER_PATH])

    def test_complete_repeated_generation_produces_no_new_files(self):
        _, files = self.generate({PERK_PATH: PERK, HELPER_PATH: HELPER})
        r, rerun = self.generate(files)
        self.assertEqual(r["status"], "COMPLETE", r)
        self.assertEqual(rerun, {})
        r2, rerun2 = self.generate(files)
        self.assertEqual(r2["appliedEdits"], 0)
        self.assertEqual(rerun2, {})

    def test_partial_owner_completes_one_missing_store_only(self):
        _, files = self.generate({PERK_PATH: PERK, HELPER_PATH: HELPER})
        first = files[PERK_PATH]
        partial = first.replace(
            "cache.StorePerkResult(cacheIndex, player, 2); return 2;",
            "return 2;", 1)
        r, fixed = self.generate({PERK_PATH: partial, HELPER_PATH: files[HELPER_PATH]})
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn(PERK_PATH, fixed)
        self.assertEqual(fixed[PERK_PATH].count("cache.StorePerkResult(cacheIndex, player,"), 3)
        self.assertEqual(fixed[PERK_PATH].count("TryGetPerkResult("), 1)
        self.assertNotIn(HELPER_PATH, fixed)

    def test_custom_relocated_with_added_perk_preserves_comments(self):
        name = PERK_PATH.replace("chipPerkUnlock.reds", "custom/ModdedPerks.reds")
        custom = PERK.replace("    return wrappedMethod(perkType);",
            "    // source-owner custom logic\n"
            "    if (Equals(gamedataNewPerkType.Body_Right_Perk_2_2, perkType)) {\n"
            "        if playerMemory > 3.0 { return 1; }\n"
            "    }\n"
            "    return wrappedMethod(perkType);")
        r, out = self.generate({name: custom, HELPER_PATH: HELPER})
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn(name, out)
        self.assertIn("source-owner custom logic", out[name])
        self.assertIn("ArrayResize(this.m_perkResult, 3);", out[HELPER_PATH])

    def test_incompatible_player_setup_is_narrow_refusal(self):
        source = PERK.replace("let player = GetPlayer(game);",
                              "let player = this.GetCustomPawn(game);")
        r, files = self.generate({PERK_PATH: source, HELPER_PATH: HELPER})
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(files, {})
        self.assertTrue(any("PLAYER_SETUP_UNPROVEN" in s for s in r["refusals"]))

    def test_missing_helper_refuses_without_broken_output(self):
        r, files = self.generate({PERK_PATH: PERK})
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(files, {})
        self.assertTrue(any("REQUIRED_HELPER_MISSING" in s for s in r["refusals"]))

    def test_absent_owner_is_noop(self):
        r, files = self.generate({"r6/scripts/OtherMod/Other.reds":
                                  "public class OtherMod {}"})
        self.assertEqual(r["status"], "NOT_APPLICABLE", r)
        self.assertEqual(files, {})

if __name__ == "__main__":
    unittest.main()
