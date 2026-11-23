"""TriggerModeControl historical Pass 5: literal trigger-mode CNames.

The source-aware generator must recognize current post-generic source, partial
manual changes, complete historical output, version drift and unsafe source.
It never changes aiming or ReadyEvents refresh cadence.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/TriggerModeControl/TriggerModeControl.reds"
STOCK = """module TriggerModeControl
@addMethod(InputContextTransitionEvents)
private final func GetTriggerModeKey(weaponObject: wref<WeaponObject>, weaponRecord: wref<WeaponItem_Record>, trigger: Int32) -> CName {
  let triggerStr: String;
  let triggerType: gamedataTriggerMode;
  let settings: wref<TMCSettings> = TMCSettings.GetSettings();
  if Equals(trigger, 2) {
    triggerType = weaponRecord.SecondaryTriggerMode().Type();
    triggerStr = "Secondary";
  } else {
    triggerType = weaponRecord.PrimaryTriggerMode().Type();
    triggerStr = "Primary";
  };
  if settings.overrideAuto || weaponObject.WeaponHasTag(n"ForceAuto") || weaponObject.WeaponHasTag(StringToName("ForceAuto" + triggerStr)) {
    return n"Mod-TriggerModeCtrl-FullAuto";
  };
  if weaponObject.WeaponHasTag(n"RemoveAuto") || weaponObject.WeaponHasTag(StringToName("RemoveAuto" + triggerStr)) {
    return n"Mod-TriggerModeCtrl-SemiAuto";
  };
  if weaponObject.WeaponHasTag(n"InstantCharge") || weaponObject.WeaponHasTag(StringToName("InstantCharge" + triggerStr)) {
    return n"Mod-TriggerModeCtrl-SemiAuto";
  };
  switch triggerType {
    case gamedataTriggerMode.FullAuto: return n"Mod-TriggerModeCtrl-FullAuto";
    case gamedataTriggerMode.Charge: return n"Mod-TriggerModeCtrl-Charge";
    case gamedataTriggerMode.Burst: return n"Mod-TriggerModeCtrl-Burst";
  };
  return n"Mod-TriggerModeCtrl-SemiAuto";
}
@wrapMethod(AimingContextEvents)
protected final func OnUpdate(timeDelta: Float, stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  wrappedMethod(timeDelta, stateContext, scriptInterface);
  this.AddTriggerModeCtrlInputHints(stateContext, scriptInterface, n"Ranged");
}
@wrapMethod(ReadyEvents)
protected final func OnUpdate(timeDelta: Float, stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  wrappedMethod(timeDelta, stateContext, scriptInterface);
  PlayerGameplayRestrictions.PushForceRefreshInputHintsEventToPSM(scriptInterface.executionOwner as PlayerPuppet);
}
"""


class TriggerModeLiteralTagTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.n = 0

    def generate(self, source=None, path=TARGET):
        self.n += 1
        game = self.root / f"game{self.n}"
        file = game / path
        file.parent.mkdir(parents=True, exist_ok=True)
        if source is not None:
            file.write_text(source, encoding="utf8")
        out = self.root / f"output{self.n}"
        proc = subprocess.run(
            ["dotnet", str(DLL), "phase2-trigger-mode-tags",
             "--game", str(game), "--out", str(out)],
            capture_output=True, text=True, timeout=90)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        if source is not None:
            self.assertEqual(file.read_text("utf8"), source)
        staged = out / path
        return report, staged.read_text("utf8") if staged.exists() else None

    def check(self, result):
        self.assertIsNotNone(result)
        for prefix, local in (("ForceAuto", "forceAutoTag"),
                              ("RemoveAuto", "removeAutoTag"),
                              ("InstantCharge", "instantChargeTag")):
            self.assertEqual(result.count(f"let {local}: CName;"), 1)
            self.assertEqual(result.count(f"WeaponHasTag({local})"), 1)
            self.assertIn(f'{local} = n"{prefix}Secondary";', result)
            self.assertIn(f'{local} = n"{prefix}Primary";', result)
            self.assertNotIn(f'StringToName("{prefix}"', result)
        self.assertNotIn("let triggerStr: String;", result)
        self.assertIn("this.AddTriggerModeCtrlInputHints(", result)
        self.assertIn("PushForceRefreshInputHintsEventToPSM(", result)

    def test_postgeneric_stock_one_run_then_idempotent(self):
        result, out = self.generate(STOCK)
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(result["appliedEdits"], 1)
        self.check(out)
        repeated, nofile = self.generate(out)
        self.assertEqual(repeated["status"], "COMPLETE", repeated)
        self.assertIsNone(nofile)

    def test_partial_historical_conversion_completes_missing_work(self):
        partial = STOCK.replace(
            'let triggerType: gamedataTriggerMode;',
            'let triggerType: gamedataTriggerMode;\n  let forceAutoTag: CName;')
        partial = partial.replace(
            'triggerStr = "Secondary";',
            'triggerStr = "Secondary";\n    forceAutoTag = n"ForceAutoSecondary";')
        partial = partial.replace(
            'triggerStr = "Primary";',
            'triggerStr = "Primary";\n    forceAutoTag = n"ForceAutoPrimary";')
        partial = partial.replace(
            'StringToName("ForceAuto" + triggerStr)', "forceAutoTag")
        result, output = self.generate(partial)
        self.assertEqual(result["status"], "APPLIED", result)
        self.check(output)
        self.assertEqual(self.generate(output)[0]["status"], "COMPLETE")

    def test_markerless_historical_already_complete(self):
        _, out = self.generate(STOCK)
        result, nofile = self.generate(out.replace(
            "    forceAutoTag", "    forceAutoTag"))
        self.assertEqual(result["status"], "COMPLETE", result)
        self.assertIsNone(nofile)

    def test_custom_relocated_and_unrelated_code_preserved(self):
        custom = STOCK.replace(
            "  let triggerType: gamedataTriggerMode;",
            "  let triggerType: gamedataTriggerMode;\n"
            "  // user's additional state and logic\n"
            "  let userCounter: Int32 = 123;")
        custom += '\n@addMethod(WeaponObject)\nfunc UserExtension() -> Bool { return true; }\n'
        result, out = self.generate(custom,
            "r6/scripts/TriggerModeControl/patches/Custom.reds")
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertIn("let userCounter: Int32 = 123;", out)
        self.assertIn("func UserExtension()", out)
        self.check(out)

    def test_extra_suffix_reader_is_preserved(self):
        custom = STOCK.replace(
            '  if settings.overrideAuto',
            '  if Equals(triggerStr, "Secondary") { this.UserNote(); };\n'
            '  if settings.overrideAuto')
        state, out = self.generate(custom)
        self.assertEqual(state["status"], "APPLIED", state)
        self.assertIn("let triggerStr: String;", out)
        self.assertIn('Equals(triggerStr, "Secondary")', out)
        self.assertNotIn('StringToName("ForceAuto"', out)

    def test_unsafe_changed_branch_refuses_this_operation(self):
        unsafe = STOCK.replace('triggerStr = "Secondary";',
                               'triggerStr = "Tertiary";')
        state, out = self.generate(unsafe)
        self.assertEqual(state["status"], "REFUSED", state)
        self.assertIn("TMC_DYNAMIC_SUFFIX_ASSIGNMENTS_UNPROVEN",
                      state["reason"])
        self.assertIsNone(out)

    def test_absent_mod_is_not_applicable(self):
        result, out = self.generate(
            "module Other\npublic class Other {}\n",
            "r6/scripts/Other/Other.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE")
        self.assertIsNone(out)


if __name__ == "__main__":
    unittest.main()
