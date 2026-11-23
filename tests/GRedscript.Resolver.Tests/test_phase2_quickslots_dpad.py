"""Current post-generic Quickslots D-pad caching, layered after approved
specific InputHub bridge. Validate stock, partial, complete, custom drift,
device transitions and focused incompatible skip. SCC/full-stage check is CI.
"""
import json
import re
import subprocess
import tempfile
import unittest
from pathlib import Path
from test_phase2_quickslots_input import FRAMEWORK, SOURCE, OWNER, DLL

DATA = """public class CustomHotkeyPlayerButtonData {
  private let player: wref<PlayerPuppet>;
  private let crouchHeld: Bool;
  private let dropBodyHeld: Bool;
  private let quickMeleeHeld: Bool;
  private let modifiersForDpadLeft: array<CName>;
  private let modifiersForDpadRight: array<CName>;
  private let modifiersForDpadUp: array<CName>;
  private let modifiersForDpadDown: array<CName>;

  public func ObserveAction(actionName: CName, actionType: gameinputActionType) -> Void {
    let shouldSendEvent: Bool = false;
    if Equals(actionType, gameinputActionType.BUTTON_HOLD_COMPLETE) {
      this.dropBodyHeld = true;
      this.crouchHeld = true;
      this.quickMeleeHeld = true;
      shouldSendEvent = true;
    }
    if Equals(actionType, gameinputActionType.BUTTON_RELEASED) {
      this.dropBodyHeld = false;
      this.crouchHeld = false;
      this.quickMeleeHeld = false;
      shouldSendEvent = true;
    }
    if shouldSendEvent {
      let evt: ref<HotKeyHoldButtonHeldEvent> = new HotKeyHoldButtonHeldEvent();
      evt.isNormalDpadLeftProhibited = this.IsNormalDpadLeftProhibited();
      evt.isNormalDpadRightProhibited = this.IsNormalDpadRightProhibited();
      evt.isNormalDpadUpProhibited = this.IsNormalDpadUpProhibited();
      evt.isNormalDpadDownProhibited = this.IsNormalDpadDownProhibited();
      this.player.QueueEvent(evt);
    }
  }

  public func SetModifiers(modifiersForDpadLeft: array<CName>,
                           modifiersForDpadRight: array<CName>,
                           modifiersForDpadUp: array<CName>,
                           modifiersForDpadDown: array<CName>) -> Void {
    this.modifiersForDpadLeft = modifiersForDpadLeft;
    this.modifiersForDpadRight = modifiersForDpadRight;
    this.modifiersForDpadUp = modifiersForDpadUp;
    this.modifiersForDpadDown = modifiersForDpadDown;
  }

  public func SetPlayer(player: wref<PlayerPuppet>) -> Void {
    this.player = player;
  }

  public func IsNormalDpadLeftProhibited() -> Bool {
    return this.IsNormalDpadDirProhibited(this.modifiersForDpadLeft);
  }
  public func IsNormalDpadRightProhibited() -> Bool {
    return this.IsNormalDpadDirProhibited(this.modifiersForDpadRight);
  }
  public func IsNormalDpadUpProhibited() -> Bool {
    return this.IsNormalDpadDirProhibited(this.modifiersForDpadUp);
  }
  public func IsNormalDpadDownProhibited() -> Bool {
    return this.IsNormalDpadDirProhibited(this.modifiersForDpadDown);
  }

  private func IsNormalDpadDirProhibited(dpadDirModifiers: array<CName>) -> Bool {
    if !this.player.PlayerLastUsedPad() { return false; }
    let i: Int32 = 0;
    while i < ArraySize(dpadDirModifiers) {
      if this.dropBodyHeld && Equals(dpadDirModifiers[i], n"DropCarriedObject_CQ_0") {
        return true;
      }
      if this.crouchHeld && Equals(dpadDirModifiers[i], n"ToggleCrouch_CQ_0") {
        return true;
      }
      if this.quickMeleeHeld && Equals(dpadDirModifiers[i], n"QuickMelee_CQ_0") {
        return true;
      }
      i += 1;
    }
    return false;
  }
}
"""

BASE = SOURCE[:SOURCE.index("public class CustomHotkeyPlayerButtonData {")] + DATA


class DpadSourceContracts(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.index = 0

    def stage(self, source=BASE, bridge=True, framework=True):
        self.index += 1
        game = self.root / f"game-{self.index}"
        file = game / OWNER
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding="utf8")
        if framework:
            for rel, value in FRAMEWORK.items():
                dest = game / rel
                dest.parent.mkdir(parents=True, exist_ok=True)
                dest.write_text(value, encoding="utf8")
        parent = None
        if bridge:
            parent = self.root / f"input-{self.index}"
            result = subprocess.run(["dotnet", str(DLL), "phase2-quickslots-input",
                  "--game", str(game), "--out", str(parent)],
                  capture_output=True, text=True, timeout=60)
            self.assertEqual(result.returncode, 0,
                             result.stdout + "\n" + result.stderr)
            report = json.loads((parent / "phase2-result.json").read_text("utf8"))
            self.assertIn(report["status"], ("APPLIED", "COMPLETE"), report)
            # Compose the already approved #16 output as the input to the
            # semantic #17 operation, as the canonical Stage does.
        output = self.root / f"dpad-{self.index}"
        cmd = ["dotnet", str(DLL), "phase2-quickslots-dpad",
               "--game", str(game), "--out", str(output)]
        if parent is not None:
            cmd.extend(["--base-overlay", str(parent)])
        process = subprocess.run(cmd, capture_output=True, text=True,
                                 timeout=60)
        self.assertEqual(process.returncode, 0,
                         process.stdout + "\n" + process.stderr)
        self.assertEqual(file.read_text("utf8"), source)
        report = json.loads((output / "phase2-result.json").read_text("utf8"))
        target = output / OWNER
        return report, target.read_text("utf8") if target.exists() else None

    def assert_cached(self, content):
        self.assertIsNotNone(content)
        for direction in ("Left", "Right", "Up", "Down"):
            name = f"normalDpad{direction}Prohibited"
            self.assertIn(f"private let {name}: Bool;", content)
            self.assertIn(f"return this.{name};", content)
            self.assertIn(f"this.{name} = false;", content)
            self.assertIn(f"this.{name} = this.IsNormalDpadDirProhibited(", content)
        self.assertIn("!this.player.PlayerLastUsedPad()", content)
        self.assertNotIn("this.lastUsedKBM || !this.player.PlayerLastUsedPad()", content)
        self.assertIn("public func SetDeviceMode(lastUsedKBM: Bool)", content)
        self.assertIn("this.RecomputeDpadProhibitions();", content)
        self.assertIn("this.modifiersForDpadLeft = modifiersForDpadLeft;", content)
        self.assertIn("GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1", content)
        self.assertIn("this.customQuickslotsInputHub.Subscribe(", content)
        self.assertIn("this.customQuickslotsFallbackRegistered = true;", content)
        self.assertIn("this.dropBodyHeld = false;", content)
        self.assertIn("this.player.QueueEvent(evt);", content)

    def test_post_generic_stock_one_run_and_second_noop(self):
        report, content = self.stage()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.assert_cached(content)
        report2, next_content = self.stage(content)
        self.assertEqual(report2["status"], "COMPLETE", report2)
        self.assertEqual(report2["appliedEdits"], 0)
        self.assertIsNone(next_content)

    def test_inactive_when_keyboard_no_controller_last_used(self):
        report, content = self.stage()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("!this.player.PlayerLastUsedPad()", content)
        self.assertIn("this.normalDpadLeftProhibited = false;", content)
        self.assertIn("this.normalDpadRightProhibited = false;", content)
        self.assertIn("this.normalDpadUpProhibited = false;", content)
        self.assertIn("this.normalDpadDownProhibited = false;", content)
        self.assertIn("this.customQuickslotsInputListener.SyncGraceState();", content)
        self.assertIn("this.player.m_customHotkeyPlayerButtonData.SetDeviceMode(", content)

    def test_partial_single_getter_missing_only_it(self):
        _, full = self.stage()
        partial = full.replace(
            "return this.normalDpadDownProhibited;",
            "return this.IsNormalDpadDirProhibited(this.modifiersForDpadDown);")
        report, result = self.stage(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assert_cached(result)
        self.assertEqual(result.count("private func RecomputeDpadProhibitions()"), 1)
        self.assertEqual(result.count("public func SetDeviceMode(lastUsedKBM:"), 1)

    def test_missing_modifier_recompute_is_completed(self):
        _, full = self.stage()
        prefix = "    this.modifiersForDpadDown = modifiersForDpadDown;"
        partial, replaced = re.subn(
            r"(this\.modifiersForDpadDown = modifiersForDpadDown;)\s*"
            r"this\.RecomputeDpadProhibitions\(\);",
            r"\1", full, count=1)
        self.assertEqual(replaced, 1, "Test must remove an actual cache invalidation")
        report, result = self.stage(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assert_cached(result)
        self.assertEqual(result.count("private func RecomputeDpadProhibitions()"), 1)

    def test_custom_unrelated_source_and_moved_file_accepted(self):
        changed = BASE.replace("module CustomQuickslots",
                               "module /* custom */ CustomQuickslots")
        changed += "\n// GCO-GENERIC:OTHER_GENERIC_RULE_V1\n"
        changed = changed.replace(
            "this.modifiersForDpadDown = modifiersForDpadDown;",
            "this.modifiersForDpadDown = modifiersForDpadDown;\n"
            "    this.AuthorSpecificSideEffect();")
        report, result = self.stage(changed)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("GCO-GENERIC:OTHER_GENERIC_RULE_V1", result)
        self.assertIn("this.AuthorSpecificSideEffect();", result)
        self.assert_cached(result)

    def test_additional_external_modifier_writer_refused(self):
        mutated = BASE.replace(
            "    this.player = player;",
            "    this.player = player;\n"
            "    this.modifiersForDpadUp = [n\"other\"];")
        report, result = self.stage(mutated)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"],
                         "CQ_DPAD_MODIFIER_EXTERNAL_WRITER_modifiersForDpadUp")
        self.assertIsNone(result)

    def test_input_bridge_missing_refuses_operation(self):
        report, result = self.stage(bridge=False)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"], "CQ_DPAD_REQUIRED_METHOD_MISSING_OnDeviceChanged")
        self.assertIsNone(result)

    def test_absent_owner_is_not_created(self):
        report, result = self.stage(
            "module Other\npublic class DifferentOwner {}\n", bridge=False)
        self.assertEqual(report["status"], "NOT_APPLICABLE", report)
        self.assertIsNone(result)


if __name__ == "__main__":
    unittest.main()
