"""Pass 8 ImmersiveCyberware: helper, ammo state and FocusScanner source states."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = (ROOT / "tests/GRedscript.Pass8.Checkpoint/bin/Release/net10.0/"
          "GRedscript.Pass8.Checkpoint.dll")
AMMO_PATH = "r6/scripts/ImmersiveCyberware/handCyberware.reds"
FOCUS_PATH = "r6/scripts/ImmersiveCyberware/lenses/FocusScanner.reds"
CACHE_PATH = "r6/scripts/ImmersiveCyberware/GRedHotpathCache.reds"
AMMO = """@wrapMethod(WeaponRosterGameController)
protected cb func OnUpdate(dT: Float) -> Bool {
  let outcome = wrappedMethod(dT);
  let game = GetGameInstance();
  let player = GetPlayer(game);
  if !IsDefined(player) {
    return outcome;
  };
  let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
  let hasAimThing: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"AmmoCounterConnected")));
  let root = this.GetRootWidget() as inkCanvas;
  if !IsDefined(root) {
    return outcome;
  };
  let widget = root.GetWidget(n"weapon_on_foot/ammo_counter/weapon_wrapper/ammo_wrapper");
  if !IsDefined(widget) {
    return outcome;
  };
  let isWeaponTotalAmmoVisible: Bool = hasAimThing > 0.0 || this.m_inVehicle || this.m_player.IsReplacer();
  if isWeaponTotalAmmoVisible {
    widget.SetScale(new Vector2(1.0, 1.0));
  } else {
    widget.SetScale(new Vector2(0.0, 0.0));
  };
  return outcome;
}
"""
FOCUS = """import ImmersiveCyberware.Settings.*

@wrapMethod(TimeDilationFocusModeDecisions)
protected final const func EnterCondition(const stateContext: ref<StateContext>, const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let game = GetGameInstance();
    let player = GetPlayer(game);
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
    let hasScannerThing: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"IMScanningFocus")));
    if (hasScannerThing > 0.0 || !(new ICSettings()).scanningFocus) {
        return wrappedMethod(stateContext, scriptInterface);
    } else {
        return false;
    }
}
"""

class ImmersiveContracts(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.n = 0

    def generate(self, sources):
        self.n += 1
        game = self.root / f"game{self.n}"
        (game / "r6/scripts").mkdir(parents=True)
        for path, contents in sources.items():
            file = game / path
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(contents, encoding="utf8")
        out = self.root / f"out{self.n}"
        p = subprocess.run(["dotnet", str(RUNNER), "--game", str(game),
            "--out", str(out), "--owner", "immersive"], capture_output=True,
            text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        for path, contents in sources.items():
            self.assertEqual((game / path).read_text(encoding="utf8"), contents)
        result = json.loads((out/"phase2-result.json").read_text(encoding="utf8"))
        files = {p.relative_to(out).as_posix(): p.read_text(encoding="utf8")
                 for p in out.rglob("*.reds")}
        return result, files

    def test_stock_generates_two_owner_files_and_one_helper(self):
        report, files = self.generate({AMMO_PATH: AMMO, FOCUS_PATH: FOCUS})
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["refusals"], [], report)
        self.assertEqual(report["appliedEdits"], 3, report)
        self.assertEqual(set(files), {AMMO_PATH, FOCUS_PATH, CACHE_PATH})
        self.assertIn("this.m_gredICAmmoWidget != widget", files[AMMO_PATH])
        self.assertIn("this.m_gredICAmmoVisibilityKnown = true", files[AMMO_PATH])
        self.assertIn("HasAmmoCounter(player)", files[AMMO_PATH])
        self.assertIn("HasScanningFocus(player)", files[FOCUS_PATH])
        self.assertIn("RequiresScanningFocus(player)", files[FOCUS_PATH])
        self.assertIn("this.m_statsInitialized", files[CACHE_PATH])
        self.assertIn("now >= this.m_lastStatsRefresh", files[CACHE_PATH])
        self.assertIn("wrappedMethod(dT)", files[AMMO_PATH])
        self.assertIn("wrappedMethod(stateContext, scriptInterface)", files[FOCUS_PATH])

    def test_after_one_generate_is_already_complete_and_idempotent(self):
        _, generated = self.generate({AMMO_PATH: AMMO, FOCUS_PATH: FOCUS})
        report, files = self.generate(generated)
        self.assertEqual(report["status"], "COMPLETE", report)
        self.assertEqual(report["appliedEdits"], 0)
        self.assertEqual(report["alreadyComplete"], 2)
        self.assertEqual(files, {})
        again, files2 = self.generate(generated)
        self.assertEqual(again["appliedEdits"], 0)
        self.assertEqual(files2, {})

    def test_manual_partial_stat_only_completes_setter_and_fields(self):
        partial = AMMO.replace(
            "  let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);\n"
            "  let hasAimThing: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n\"gamedataStatType\", n\"AmmoCounterConnected\")));\n",
            "")
        partial = partial.replace("hasAimThing > 0.0",
            "player.GRedICHotpathCache().HasAmmoCounter(player)")
        report, files = self.generate({AMMO_PATH: partial, FOCUS_PATH: FOCUS})
        self.assertEqual(report["appliedEdits"], 3, report)
        text = files[AMMO_PATH]
        self.assertEqual(text.count("HasAmmoCounter(player)"), 1)
        self.assertEqual(text.count("private let m_gredICAmmoWidget:"), 1)
        self.assertIn("m_gredICAmmoVisibilityKnown", text)

    def test_relocated_custom_comment_preserved(self):
        custom = AMMO.replace("  let root = ", "  // owner custom note\n  let root = ")
        relocated = "r6/scripts/ImmersiveCyberware/ui/customAmmo.reds"
        report, files = self.generate({relocated: custom, FOCUS_PATH: FOCUS})
        self.assertEqual(report["appliedEdits"], 3, report)
        self.assertIn("owner custom note", files[relocated])

    def test_incompatible_ammo_refuses_only_ammo(self):
        unsafe = AMMO.replace("  if isWeaponTotalAmmoVisible {",
                              "  if this.CustomNeedScale() {")
        report, files = self.generate({AMMO_PATH: unsafe, FOCUS_PATH: FOCUS})
        self.assertEqual(report["appliedEdits"], 2, report)
        self.assertEqual(len(report["refusals"]), 1, report)
        self.assertIn("AMMO_WIDGET_LIFECYCLE_UNPROVEN",
                      report["refusals"][0])
        self.assertNotIn(AMMO_PATH, files)
        self.assertIn(FOCUS_PATH, files)
        self.assertIn(CACHE_PATH, files)

    def test_absent_mod_has_zero_output(self):
        report, files = self.generate({"r6/scripts/other/foo.reds":
                                       "public class Other {}\n"})
        self.assertEqual(report["status"], "NOT_APPLICABLE", report)
        self.assertEqual(files, {})

if __name__ == "__main__":
    unittest.main()
