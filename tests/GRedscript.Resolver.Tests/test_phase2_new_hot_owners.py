"""Profiler-ranked owner operations: isolated source states and real one-pass
stage admission. Tests do not replace SCC or game-side validation.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"

APT = "r6/scripts/gt_8ug_apt/GetTenSO8ugApartment.reds"
NA_MAIN = "r6/scripts/negotiableaffection/Main/NA_Interaction.reds"
NA_CHOICE = "r6/scripts/negotiableaffection/Main/NA_DialogueChoicePopup.reds"
SC = "r6/scripts/ShatteredChrome/SC_Text.reds"

APT_SRC = '''module Apartment
public class Gt8ugAptSystem {
  private let m_lastInput: CName;
  private let m_shown: Bool;
  private func Init() -> Void { this.m_lastInput = n"None"; }
  public func OnChoiceAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if !this.m_shown {
      this.m_lastInput = n"None";
      return false;
    }
    if NotEquals(ListenerAction.GetType(action), gameinputActionType.BUTTON_PRESSED) {
      this.m_lastInput = n"None";
      return false;
    }
    let name = ListenerAction.GetName(action);
    if Equals(name, n"ChoiceApply") {
      consumer.Consume();
      this.m_lastInput = n"None";
      this.Activate();
      return true;
    }
    this.m_lastInput = n"None";
    return false;
  }
  private func Activate() -> Void {}
}
public class Gt8ugAptInputListener extends IScriptable {
  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let apt = Gt8ugAptSystem.Get();
    if IsDefined(apt) {
      apt.OnChoiceAction(action, consumer);
    }
    return false;
  }
}
'''

NA_MAIN_SRC = '''module NegotiableAffection
@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let result = wrappedMethod(action, consumer);
    // GCO-GENERIC:SYSTEM_READ_AFTER_ACTION_GUARD_V1
    if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) { return result; }
    let qs = GameInstance.GetQuestsSystem(this.GetGame());
    if !IsDefined(qs) { return result; }
    let actionName = ListenerAction.GetName(action);
    if Equals(actionName, n"na_walk_beat") {
      ListenerActionConsumer.Consume(consumer);
    } else if Equals(actionName, n"na_force_work") {
      qs.SetFact(n"na_simple", 1);
    } else if Equals(actionName, n"na_ponc_next") {
      if qs.GetFact(n"na_ponc_active") == 1 { qs.SetFact(n"na_ponc_next", 1); }
    } else if Equals(actionName, n"na_ponc_finish") {
      if qs.GetFact(n"na_ponc_active") == 1 { qs.SetFact(n"na_ponc_next", 1); }
    }
    return result;
}
'''

NA_CHOICE_SRC = '''module NegotiableAffection
@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let qs = GameInstance.GetQuestsSystem(this.GetGame());
    if !IsDefined(qs) || qs.GetFact(n"na_active") == 0 {
        return wrappedMethod(action, consumer);
    }
    let choiceSys = this.m_naDialogueChoiceSystem;
    let actionName = ListenerAction.GetName(action);
    if IsDefined(choiceSys) && choiceSys.IsChoiceActive() {
      let isAxis = Equals(ListenerAction.GetType(action), gameinputActionType.AXIS_CHANGE);
      let isReleased = Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED);
      if Equals(actionName, n"NextWeapon") || Equals(actionName, n"ChoiceScrollUp") {
        ListenerActionConsumer.Consume(consumer);
      } else if Equals(actionName, n"PreviousWeapon") || Equals(actionName, n"ChoiceScrollDown") {
        ListenerActionConsumer.Consume(consumer);
      } else if Equals(actionName, n"mouse_wheel") && isAxis {
        ListenerActionConsumer.Consume(consumer);
      } else if Equals(actionName, n"na_choice_apply") ||
                Equals(actionName, n"na_choice_1") ||
                Equals(actionName, n"na_choice_2") {
        if isReleased { ListenerActionConsumer.Consume(consumer); }
      }
    }
    return wrappedMethod(action, consumer);
}
'''

SC_SRC = '''module ShatteredChrome
public static func SCLeaderWidth() -> Int32 = 27
public static func SC_DotRow(label: String, value: String) -> String {
  let out: String = label + " ";
  while StrLen(out) < SCLeaderWidth() {
    out = out + ".";
  };
  return out + " " + value;
}
public static func SC_DotRowNoisy(label: String, value: String, seed: Int32) -> String {
  return label + value;
}
'''

class NewHotOwnerTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.home = Path(tmp.name)
        self.index = 0

    def generate(self, kind, files, *, overlay=None):
        self.index += 1
        game = self.home / f"game{self.index}"
        for rel, src in files.items():
            target = game / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(src, encoding="utf8")
        out = self.home / f"out{self.index}"
        cmd = ["dotnet", str(DLL), "phase2-new-hot-owner",
               "--kind", kind, "--game", str(game), "--out", str(out)]
        if overlay:
            base = self.home / f"overlay{self.index}"
            for rel, src in overlay.items():
                target = base / rel
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_text(src, encoding="utf8")
            cmd += ["--base-overlay", str(base)]
        p = subprocess.run(cmd, text=True, capture_output=True, timeout=100)
        self.assertEqual(p.returncode, 0, p.stdout + "\n" + p.stderr)
        result = json.loads((out / "phase2-result.json").read_text("utf8"))
        for rel, source in files.items():
            self.assertEqual((game / rel).read_text("utf8"), source)
        staged = {str(p.relative_to(out)).replace("\\", "/"): p.read_text("utf8")
                  for p in out.rglob("*.reds")}
        self.assertEqual(sorted(staged), sorted(result["sourcePaths"]))
        return result, staged

    def test_apartment_stock_and_idempotence(self):
        report, generated = self.generate("apartment", {APT: APT_SRC})
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        body = generated[APT]
        self.assertLess(body.index("NotEquals(ListenerAction.GetName(action)"),
                        body.index("Gt8ugAptSystem.Get();"))
        self.assertIn("gameinputActionType.BUTTON_PRESSED", body)
        self.assertIn("apt.OnChoiceAction(action, consumer)", body)
        report2, untouched = self.generate("apartment", {APT: body})
        self.assertEqual(report2["status"], "COMPLETE", report2)
        self.assertFalse(untouched)

    def test_apartment_partial_guard_and_custom_moved_source(self):
        partial = APT_SRC.replace(
            "    let apt = Gt8ugAptSystem.Get();",
            '    if NotEquals(ListenerAction.GetName(action), n"ChoiceApply") { return false; }\n'
            '    let apt = Gt8ugAptSystem.Get();')
        rel = "r6/scripts/gt_8ug_apt/custom/NewApartment.reds"
        result, staged = self.generate("apartment", {rel: partial})
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(staged[rel].count("NotEquals(ListenerAction.GetName(action)"), 1)
        self.assertIn("BUTTON_PRESSED", staged[rel])
        self.assertEqual(self.generate("apartment", {rel: staged[rel]})[0]["status"],
                         "COMPLETE")

    def test_apartment_nontrivial_state_consumer_refused(self):
        incompatible = APT_SRC.replace(
            "  private func Init() -> Void",
            '  private func IsInputPending() -> Bool { return Equals(this.m_lastInput, n"X"); }\n'
            '  private func Init() -> Void')
        report, staged = self.generate("apartment", {APT: incompatible})
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertFalse(staged)
        self.assertIn("APT_IGNORED_ACTION_SIDE_EFFECT_UNPROVEN",
                      " ".join(report["refusals"]))

    def test_negotiable_both_wrappers_stock_repeat_no_duplicate(self):
        original = {NA_MAIN: NA_MAIN_SRC, NA_CHOICE: NA_CHOICE_SRC}
        report, staged = self.generate("negotiable", original)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 2)
        self.assertEqual(len(staged), 2)
        main = staged[NA_MAIN]
        choice = staged[NA_CHOICE]
        self.assertLess(main.index("NotEquals(actionName, n"),
                        main.index("GameInstance.GetQuestsSystem(this.GetGame());"))
        self.assertIn("GCO-GENERIC:SYSTEM_READ_AFTER_ACTION_GUARD_V1", main)
        self.assertEqual(main.count("ListenerAction.GetName(action)"), 1)
        self.assertLess(choice.index("let __gcoNaChoiceName"),
                        choice.index("let qs = "))
        self.assertEqual(choice.count("ListenerAction.GetName(action)"), 1)
        self.assertIn("return wrappedMethod(action, consumer);", choice)
        self.assertIn("ListenerActionConsumer.Consume(consumer)", choice)
        repeated, none = self.generate("negotiable", staged)
        self.assertEqual(repeated["status"], "COMPLETE", repeated)
        self.assertEqual(repeated["alreadyComplete"], 2)
        self.assertFalse(none)

    def test_negotiable_partial_existing_gate_adds_custom_action(self):
        _, produced = self.generate("negotiable",
                                    {NA_MAIN: NA_MAIN_SRC, NA_CHOICE: NA_CHOICE_SRC})
        main = produced[NA_MAIN].replace(
            'NotEquals(actionName, n"na_ponc_finish")',
            'NotEquals(actionName, n"na_ponc_next")')
        # Source has a new compatible branch and must widen an existing gate.
        main = main.replace("    return result;\n}",
            '    if Equals(actionName, n"na_custom") { qs.SetFact(n"na_custom", 1); }\n'
            "    return result;\n}")
        report, staged = self.generate("negotiable",
            {NA_MAIN: main, NA_CHOICE: produced[NA_CHOICE]})
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.assertIn('NotEquals(actionName, n"na_custom")', staged[NA_MAIN])
        self.assertIn('if Equals(actionName, n"na_custom")', staged[NA_MAIN])
        repeat, none = self.generate("negotiable",
            {NA_MAIN: staged[NA_MAIN], NA_CHOICE: produced[NA_CHOICE]})
        self.assertEqual(repeat["status"], "COMPLETE")
        self.assertFalse(none)

    def test_negotiable_custom_other_consumer_refuses_only_main(self):
        incompatible = NA_MAIN_SRC.replace(
            "    return result;\n}",
            "    this.RecordActionForAnalytics(actionName);\n    return result;\n}")
        report, staged = self.generate("negotiable",
            {NA_MAIN: incompatible, NA_CHOICE: NA_CHOICE_SRC})
        self.assertEqual(report["status"], "APPLIED")
        self.assertEqual(report["appliedEdits"], 1)
        self.assertEqual(list(staged), [NA_CHOICE])
        self.assertIn("NA_MAIN_ACTION_SET_OR_CUSTOM_CONSUMER_UNPROVEN",
                      " ".join(report["refusals"]))

    def test_negotiable_missing_one_wrapper_still_applies_other(self):
        report, staged = self.generate("negotiable", {NA_MAIN: NA_MAIN_SRC})
        self.assertEqual(report["status"], "APPLIED")
        self.assertEqual(list(staged), [NA_MAIN])
        self.assertIn("na-dialogue:HOT_OWNER_TARGET_MISSING_OR_AMBIGUOUS",
                      report["refusals"])

    def test_shattered_stock_repeat_and_noisy_preserved(self):
        report, staged = self.generate("shattered", {SC: SC_SRC})
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        src = staged[SC]
        self.assertIn("let __gcoScDotRemaining: Int32 = SCLeaderWidth() - StrLen(out);", src)
        self.assertIn("__gcoScDotRemaining -= 1;", src)
        self.assertIn("SC_DotRowNoisy", src)
        self.assertEqual(src.count('out = out + ".";'), 1)
        repeated, none = self.generate("shattered", {SC: src})
        self.assertEqual(repeated["status"], "COMPLETE", repeated)
        self.assertFalse(none)

    def test_shattered_partial_cached_count_and_missing_decrement(self):
        partial = SC_SRC.replace(
            '  while StrLen(out) < SCLeaderWidth() {',
            '  let cachedDotCount: Int32 = SCLeaderWidth() - StrLen(out);\n'
            '  while StrLen(out) < SCLeaderWidth() {')
        report, staged = self.generate("shattered", {SC: partial})
        self.assertEqual(report["status"], "APPLIED")
        text = staged[SC]
        self.assertEqual(text.count("let cachedDotCount:"), 1)
        self.assertIn("while cachedDotCount > 0", text)
        partial2 = text.replace("cachedDotCount -= 1;", "")
        report2, fixed = self.generate("shattered", {SC: partial2})
        self.assertEqual(report2["status"], "APPLIED", report2)
        self.assertIn("cachedDotCount -= 1;", fixed[SC])
        self.assertEqual(self.generate("shattered", {SC: fixed[SC]})[0]["status"],
                         "COMPLETE")

    def test_shattered_incompatible_string_loop_refused_without_mutation(self):
        source = SC_SRC.replace('out = out + ".";', 'out = out + "*"')
        report, staged = self.generate("shattered", {SC: source})
        self.assertEqual(report["status"], "REFUSED")
        self.assertFalse(staged)

    def test_missing_mod_is_not_applicable(self):
        for kind in ("apartment", "negotiable", "shattered"):
            report, staged = self.generate(
                kind, {"r6/scripts/Other/Other.reds":
                       "module Other\npublic class Other {}\n"})
            self.assertEqual(report["status"], "NOT_APPLICABLE")
            self.assertFalse(staged)

if __name__ == "__main__":
    unittest.main()
