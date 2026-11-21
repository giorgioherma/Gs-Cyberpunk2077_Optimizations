"""Independent Pass 9 RunningMan source-state contracts.

The fixture models real source control-flow; workflow separately tests the
authoritative 2,331-file post-generic stack and official SCC.
"""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / "tools/Pass9/Pass9.csproj"
TARGET = "r6/scripts/RunningMan/double_sprint.reds"

PERK = (
    'PlayerDevelopmentSystem.GetInstance(scriptInterface.executionOwner)'
    '.IsNewPerkBought(scriptInterface.executionOwner,'
    ' IntEnum<gamedataNewPerkType>(EnumValueFromName('
    'n"gamedataNewPerkType", n"Body_Left_Milestone_1"))) > 1'
)
SOURCE = """
@wrapMethod(SprintEvents)
protected func OnUpdate(timeDelta: Float, stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  wrappedMethod(timeDelta, stateContext, scriptInterface);
  this.EvaluateTwoStepSprint(stateContext, scriptInterface);
}

@replaceMethod(SprintEvents)
private final func EvaluateTwoStepSprint(stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  let hasDoubleRunPerk: Bool = __PERK__;
  if hasDoubleRunPerk {
    let isStaminaPositive: Bool = true;
    if isStaminaPositive && this.ShouldEnterSecondSprint(stateContext, scriptInterface) {
      this.m_isInSecondSprint = true;
      this.AddMaxSpeedModifier(stateContext, scriptInterface);
      stateContext.SetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint", true, true);
    } else {
      if this.m_isInSecondSprint {
        this.m_isInSecondSprint = false;
        this.RemoveMaxSpeedModifier(stateContext, scriptInterface);
        stateContext.SetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint", false, true);
      }
    }
  }
}

@wrapMethod(SprintEvents)
public func OnEnter(stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  wrappedMethod(stateContext, scriptInterface);
  let resume: StateResultBool = stateContext.GetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint");
  if resume.valid && resume.value {
    let hasDoubleRunPerk: Bool = __PERK__;
    let isStaminaPositive: Bool = true;
    if hasDoubleRunPerk && isStaminaPositive && !this.m_isInSecondSprint {
      this.m_isInSecondSprint = true;
      this.AddMaxSpeedModifier(stateContext, scriptInterface);
    }
  }
}

@wrapMethod(StandEvents)
protected func OnUpdate(timeDelta: Float, stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Void {
  wrappedMethod(timeDelta, stateContext, scriptInterface);
  if this.GetInStateTime() >= 0.10 {
    let resume: StateResultBool = stateContext.GetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint");
    if resume.valid && resume.value {
      stateContext.SetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint", false, true);
    }
  }
}
""".replace("__PERK__", PERK)


class RunningManPass9Tests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.temp = Path(temp.name)
        self.index = 0

    def gen(self, source=SOURCE, target=TARGET, second_source=None, overlay=None):
        self.index += 1
        game = self.temp / f"game-{self.index}"
        f = game / target
        f.parent.mkdir(parents=True, exist_ok=True)
        f.write_text(source, encoding="utf-8")
        if second_source:
            second = game / "r6/scripts/Other/Second.reds"
            second.parent.mkdir(parents=True, exist_ok=True)
            second.write_text(second_source, encoding="utf-8")
        output = self.temp / f"out-{self.index}"
        cmd = ["dotnet", "run", "--no-build", "-c", "Release",
               "--project", str(PROJECT), "--",
               "--game", str(game), "--out", str(output)]
        if overlay is not None:
            base = self.temp / f"preview-{self.index}"
            previous = base / target
            previous.parent.mkdir(parents=True, exist_ok=True)
            previous.write_text(overlay, encoding="utf-8")
            cmd.extend(["--base-overlay", str(base)])
        p = subprocess.run(cmd, text=True, capture_output=True, timeout=120)
        self.assertIn(p.returncode, (0, 3), p.stdout + p.stderr)
        report = json.loads((output / "phase2-result.json").read_text(encoding="utf-8"))
        self.assertEqual(f.read_text(encoding="utf-8"), source)
        generated = output / target
        return report, generated.read_text(encoding="utf-8") if generated.exists() else None

    def check_complete(self, source):
        self.assertEqual(source.count("private let m_gredRunningManDoubleRunPerk: Bool;"), 1)
        self.assertEqual(source.count("private let m_gredRunningManResumeChecked: Bool;"), 1)
        self.assertEqual(source.count(
            "this.m_gredRunningManDoubleRunPerk = PlayerDevelopmentSystem"), 1)
        self.assertIn("if this.m_gredRunningManDoubleRunPerk || this.m_isInSecondSprint", source)
        self.assertIn("if this.m_gredRunningManDoubleRunPerk {", source)
        self.assertIn("if this.m_gredRunningManDoubleRunPerk && isStaminaPositive", source)
        self.assertEqual(source.count("@wrapMethod(StandEvents)"), 2)
        self.assertIn("this.m_gredRunningManResumeChecked = false;", source)
        self.assertIn("this.m_gredRunningManResumeChecked = true;", source)
        self.assertIn("!this.m_gredRunningManResumeChecked && this.GetInStateTime()", source)
        self.assertIn("this.RemoveMaxSpeedModifier(stateContext, scriptInterface)", source)
        self.assertIn('n"RunningMan_ResumeSecondSprint"', source)

    def test_stock_single_run_and_second_run_noop(self):
        report, optimized = self.gen()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 2)
        self.check_complete(optimized)
        again, output = self.gen(optimized)
        self.assertEqual(again["status"], "COMPLETE", again)
        self.assertEqual(again["alreadyComplete"], 2)
        self.assertIsNone(output)

    def test_sprint_partial_missing_field_or_guard(self):
        _, full = self.gen()
        missing = full.replace(
            "@addField(SprintEvents)\nprivate let m_gredRunningManDoubleRunPerk: Bool;\n\n", "")
        r, s = self.gen(missing)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check_complete(s)
        guarded = full.replace(
            "if this.m_gredRunningManDoubleRunPerk || this.m_isInSecondSprint {\n"
            "    this.EvaluateTwoStepSprint(stateContext, scriptInterface);\n  }",
            "this.EvaluateTwoStepSprint(stateContext, scriptInterface);")
        self.assertNotEqual(full, guarded)
        r, s = self.gen(guarded)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check_complete(s)

    def test_stand_partial_completed(self):
        _, full = self.gen()
        fragment = full.replace(
            "@addField(StandEvents)\nprivate let m_gredRunningManResumeChecked: Bool;\n\n", "")
        fragment = fragment.replace("this.m_gredRunningManResumeChecked = true;", "")
        r, s = self.gen(fragment)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check_complete(s)

    def test_historical_pass7_partially_complete(self):
        # A previous historical pass cached the perk but lacked the Pass9
        # evaluator gate and stand invalidation: complete both in one run.
        _, full = self.gen()
        historical7 = full.replace(
            "if this.m_gredRunningManDoubleRunPerk || this.m_isInSecondSprint {\n"
            "    this.EvaluateTwoStepSprint(stateContext, scriptInterface);\n  }",
            "this.EvaluateTwoStepSprint(stateContext, scriptInterface);")
        historical7 = historical7.replace(
            "@addField(StandEvents)\nprivate let m_gredRunningManResumeChecked: Bool;\n\n", "")
        historical7 = historical7.replace(
            "@wrapMethod(StandEvents)\n"
            "public func OnEnter(stateContext: ref<StateContext>, "
            "scriptInterface: ref<StateGameScriptInterface>) -> Void {\n"
            "  wrappedMethod(stateContext, scriptInterface);\n"
            "  this.m_gredRunningManResumeChecked = false;\n}\n\n", "")
        historical7 = historical7.replace(
            "!this.m_gredRunningManResumeChecked && ", "")
        historical7 = historical7.replace(
            "this.m_gredRunningManResumeChecked = true;", "")
        r, s = self.gen(historical7)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check_complete(s)

    def test_custom_method_and_relocated_path(self):
        custom = SOURCE.replace(
            "this.EvaluateTwoStepSprint(stateContext, scriptInterface);",
            "this.CustomInputAudit();\n"
            "  this.EvaluateTwoStepSprint(stateContext, scriptInterface);")
        custom = "// custom owner change\n" + custom + "\npublic func ExtraLogic() -> Void {}"
        r, s = self.gen(custom, target="r6/scripts/Other/RunningMan-Revised.reds")
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn("this.CustomInputAudit();", s)
        self.assertIn("public func ExtraLogic() -> Void {}", s)
        self.check_complete(s)

    def test_sprint_incompatible_stand_independent(self):
        broken = SOURCE.replace("this.RemoveMaxSpeedModifier(stateContext, scriptInterface);",
                                "this.NewSpeedLogic();")
        r, s = self.gen(broken)
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertEqual(r["appliedEdits"], 1)
        self.assertIn("RM_SPRINT_TRANSITION_OR_CALLBACK_UNPROVEN", r["reason"])
        self.assertIn("this.NewSpeedLogic();", s)
        self.assertNotIn("m_gredRunningManDoubleRunPerk", s)
        self.assertIn("this.m_gredRunningManResumeChecked = true;", s)

    def test_stand_incompatible_sprint_independent(self):
        broken = SOURCE.replace("if this.GetInStateTime() >= 0.10 {",
                                "if this.GetInStateTime() >= 0.20 {")
        r, s = self.gen(broken)
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertEqual(r["appliedEdits"], 1)
        self.assertIn("RM_STAND_CADENCE_GUARD_UNPROVEN", r["reason"])
        self.assertIn("this.m_gredRunningManDoubleRunPerk", s)
        self.assertIn("if this.GetInStateTime() >= 0.20", s)

    def test_generic_marker_and_effective_overlay_preserved(self):
        generic = "// GCO-GENERIC:STABLE_ARRAY_BOUND_V1\n" + SOURCE
        r, s = self.gen(SOURCE, overlay=generic)
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn("GCO-GENERIC:STABLE_ARRAY_BOUND_V1", s)
        self.check_complete(s)

    def test_absent_owner(self):
        r, output = self.gen("public func Unrelated() -> Void {}",
                             target="r6/scripts/Unrelated/test.reds")
        self.assertEqual(r["status"], "NOT_APPLICABLE", r)
        self.assertIsNone(output)

    def test_changed_target_is_narrow_refusal(self):
        source = SOURCE.replace("if hasDoubleRunPerk {",
                                "if this.CustomPerkCheck() {")
        r, s = self.gen(source)
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn("RM_SPRINT_EVALUATOR_PERK_FLOW_CHANGED", r["reason"])
        self.assertNotIn("private let m_gredRunningManDoubleRunPerk", s)
        self.assertIn("private let m_gredRunningManResumeChecked", s)

    def test_ambiguous_duplicate_owner_is_refused(self):
        r, s = self.gen(SOURCE, second_source=SOURCE)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(r["reason"], "RM_OWNER_SOURCE_AMBIGUOUS")
        self.assertIsNone(s)


if __name__ == "__main__":
    unittest.main()
