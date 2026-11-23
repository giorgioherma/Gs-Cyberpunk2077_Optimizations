"""Dark Future 400 ms visual-only status-icon cadence safety contracts."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

from test_phase2_dark_future import STOCK as TAG_STOCK

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
REL = "r6/scripts/Dark Future/UI/DFBaseGameUIOverrides.reds"

ICON = """
@replaceMethod(buffListGameController)
private final func UpdateBuffDebuffList() -> Void {
    let buffList: array<BuffInfo>;
    let buffTimeRemaining: Float;
    let buffTimeTotal: Float;
    let currBuffLoc: wref<buffListItemLogicController>;
    let currBuffWidget: wref<inkWidget>;
    let data: ref<StatusEffect_Record>;
    let incomingBuffsCount: Int32;
    let onScreenBuffsCount: Int32;
    let visibleIncomingBuffsCount: Int32;
    let i: Int32 = 0;
    while i < ArraySize(this.m_buffDataList) {
        ArrayPush(buffList, this.m_buffDataList[i]);
        i += 1;
    };
    i = 0;
    while i < ArraySize(this.m_debuffDataList) {
        ArrayPush(buffList, this.m_debuffDataList[i]);
        i += 1;
    };
    incomingBuffsCount = ArraySize(buffList);
    onScreenBuffsCount = inkCompoundRef.GetNumChildren(this.m_buffsList);
    i = 0;
    while i < onScreenBuffsCount {
        currBuffWidget = this.m_buffWidgets[i];
        currBuffLoc = currBuffWidget.GetController() as buffListItemLogicController;
        if i >= incomingBuffsCount {
            currBuffWidget.SetVisible(false);
            currBuffLoc.SetStatusEffectRecord(null);
        } else {
            data = TweakDBInterface.GetStatusEffectRecord(buffList[i].buffID);
            buffTimeRemaining = buffList[i].timeRemaining;
            buffTimeTotal = buffList[i].timeTotal;
            if !IsDefined(data) || !IsDefined(data.UiData()) {
                currBuffWidget.SetVisible(false);
                currBuffLoc.SetStatusEffectRecord(null);
            } else {
                if data != currBuffLoc.GetStatusEffectRecord() {
                    currBuffLoc.SetStatusEffectRecord(data);
                };
                currBuffLoc.SetData(StringToName(data.UiData().IconPath()),
                    buffTimeRemaining, buffTimeTotal,
                    Cast<Int32>(buffList[i].stackCount));
                currBuffWidget.SetVisible(true);
                visibleIncomingBuffsCount += 1;
            };
        };
        i += 1;
    };
    this.SendVisibilityUpdate(inkWidgetRef.IsVisible(this.m_buffsList),
        visibleIncomingBuffsCount > 0);
    inkWidgetRef.SetVisible(this.m_buffsList,
        visibleIncomingBuffsCount > 0);
}
"""
SOURCE = "import DarkFuture.UI.*\n" + TAG_STOCK + ICON
FIELD_INIT = ("@addField(buffListGameController)\n"
              "private let m_gcoDfIconCadenceInitialized: Bool;")
FIELD_LAST = ("@addField(buffListGameController)\n"
              "private let m_gcoDfIconCadenceLast: Float;")
FIELD_NEXT = ("@addField(buffListGameController)\n"
              "private let m_gcoDfIconCadenceNext: Float;")


class IconVisualCadenceTests(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.base = Path(t.name)
        self.index = 0

    def run_generator(self, source, path=REL, extra=None):
        self.index += 1
        game = self.base / f"game{self.index}"
        f = game / path
        f.parent.mkdir(parents=True)
        f.write_text(source, encoding="utf8")
        for rel, data in (extra or {}).items():
            other = game / rel
            other.parent.mkdir(parents=True)
            other.write_text(data, encoding="utf8")
        out = self.base / f"output{self.index}"
        cmd = ["dotnet", str(DLL), "phase2-dark-future-icons",
               "--game", str(game), "--out", str(out)]
        p = subprocess.run(cmd, capture_output=True, text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        self.assertEqual(f.read_text(encoding="utf8"), source)
        manifest = json.loads((out / "phase2-result.json").read_text("utf8"))
        generated = out / path
        return (manifest, generated.read_text("utf8") if generated.exists() else None)

    def check_complete(self, original, transformed):
        for field in (FIELD_INIT, FIELD_LAST, FIELD_NEXT):
            self.assertEqual(1, transformed.count(field))
        self.assertIn("now + 0.40", transformed)
        self.assertIn("EngineTime.ToFloat", transformed)
        self.assertIn("GetSimTime()", transformed)
        self.assertIn("if __gcoDfIconVisualDue || __gcoDfIconRecordChanged || "
                      "!ArrayContains(", transformed)
        self.assertIn("n\"DarkFutureCanHideOnBuffBar\"", transformed)
        self.assertIn("    };\n", transformed)
        self.assertIn("__gcoDfIconRecordChanged = true;", transformed)
        self.assertIn("__gcoDfIconRecordChanged = false;", transformed)
        for call in ("SetStatusEffectRecord(null);", "SetVisible(false);",
                     "SetVisible(true);", "SendVisibilityUpdate(",
                     "SetData(", "FillOutroAnimationStart();",
                     "SetTimeRemaining(timeLeft);"):
            self.assertEqual(original.count(call), transformed.count(call),
                             msg=call)
        self.assertEqual(original.count("OnBuffDataChanged("),
                         transformed.count("OnBuffDataChanged("))

    def test_source_one_run_and_repeat_noop(self):
        report, result = self.run_generator(SOURCE)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.check_complete(SOURCE, result)
        repeated, absent = self.run_generator(result)
        self.assertEqual(repeated["status"], "COMPLETE", repeated)
        self.assertEqual(repeated["appliedEdits"], 0)
        self.assertIsNone(absent)

    def test_independent_missing_fields_guard_or_record_force_are_completed(self):
        _, complete = self.run_generator(SOURCE)
        for missing in (FIELD_INIT, FIELD_LAST, FIELD_NEXT,
                        "let __gcoDfIconVisualDue: Bool = true;",
                        "__gcoDfIconRecordChanged = true;",
                        "__gcoDfIconRecordChanged = false;"):
            with self.subTest(missing=missing):
                partial = complete.replace(missing, "")
                report, resolved = self.run_generator(partial)
                self.assertEqual(report["status"], "APPLIED", report)
                self.check_complete(partial, resolved)
                again, absent = self.run_generator(resolved)
                self.assertEqual(again["status"], "COMPLETE")
                self.assertIsNone(absent)

    def test_missing_due_and_record_flag_complete_together(self):
        _, complete = self.run_generator(SOURCE)
        partial = complete.replace(
            "let __gcoDfIconVisualDue: Bool = true;", "").replace(
            "let __gcoDfIconRecordChanged: Bool;", "")
        report, restored = self.run_generator(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check_complete(partial, restored)
        self.assertEqual(restored.count("let __gcoDfIconVisualDue: Bool = true;"), 1)
        self.assertEqual(restored.count("let __gcoDfIconRecordChanged: Bool;"), 1)
        repeat, duplicate = self.run_generator(restored)
        self.assertEqual(repeat["status"], "COMPLETE", repeat)
        self.assertIsNone(duplicate)

    def test_complete_custom_clock_guard_whitespace_is_accepted(self):
        _, complete = self.run_generator(SOURCE)
        custom = complete.replace(
            "if IsDefined(__gcoDfTimeSystem) {",
            "if  IsDefined ( __gcoDfTimeSystem ) {")
        custom = custom.replace(
            "if IsDefined(__gcoDfPlayer) {",
            "if ( IsDefined ( __gcoDfPlayer ) ) {")
        result, staged = self.run_generator(custom)
        self.assertEqual(result["status"], "COMPLETE", result)
        self.assertIsNone(staged)

    def test_complete_semantic_with_invalid_clock_guard_is_not_accepted(self):
        _, complete = self.run_generator(SOURCE)
        unsafe = complete.replace(
            "if IsDefined(__gcoDfTimeSystem) {",
            "if !IsDefined(__gcoDfTimeSystem) {")
        report, staged = self.run_generator(unsafe)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(
            report["reason"], "DF_ICON_PARTIAL_CLOCK_CONTROL_FLOW_UNPROVEN")
        self.assertIsNone(staged)

    def test_missing_due_with_unproven_clock_refuses_only_icon_rule(self):
        _, complete = self.run_generator(SOURCE)
        altered = complete.replace(
            "let __gcoDfIconVisualDue: Bool = true;", "").replace(
            "if IsDefined(__gcoDfTimeSystem) {",
            "if !IsDefined(__gcoDfTimeSystem) {")
        report, staged = self.run_generator(altered)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(
            report["reason"], "DF_ICON_PARTIAL_CLOCK_CONTROL_FLOW_UNPROVEN")
        self.assertIsNone(staged)

    def test_compatible_custom_renamed_locals_and_moved_owner(self):
        variant = SOURCE.replace("currBuffLoc", "vCustomLogicController")
        variant = variant.replace("data", "buffRecord")
        variant = variant.replace(
            "    let buffList: array<BuffInfo>;",
            "    let customFlag: Bool = true;\n"
            "    let buffList: array<BuffInfo>;")
        path = "r6/scripts/RelocatedDarkFuture/UI/CustomHUD.reds"
        report, modified = self.run_generator(variant, path=path)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("let customFlag: Bool = true;", modified)
        self.assertIn("vCustomLogicController.SetData(", modified)
        self.check_complete(variant, modified)
        again, nothing = self.run_generator(modified, path=path)
        self.assertEqual(again["status"], "COMPLETE")
        self.assertIsNone(nothing)

    def test_unknown_target_refuses_without_creating_semantic_source(self):
        incompatible = SOURCE.replace(
            "data != currBuffLoc.GetStatusEffectRecord()",
            "data == currBuffLoc.GetStatusEffectRecord()")
        report, nothing = self.run_generator(incompatible)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"],
                         "DF_ICON_RECORD_IDENTITY_FLOW_UNPROVEN")
        self.assertIsNone(nothing)

    def test_missing_owner_not_applicable(self):
        report, nothing = self.run_generator(
            "module Other\npublic class Different {}\n",
            path="r6/scripts/Other/Test.reds")
        self.assertEqual(report["status"], "NOT_APPLICABLE", report)
        self.assertIsNone(nothing)


if __name__ == "__main__":
    unittest.main()
