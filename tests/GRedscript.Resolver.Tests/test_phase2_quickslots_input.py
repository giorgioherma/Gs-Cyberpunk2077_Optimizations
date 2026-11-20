"""Custom Quickslots action-specific InputHub / engine fallback semantic:
stock, partial, complete, custom, unsafe drift, repeat generation and generic
marker survival. Official SCC runs on the real post-generic stack in fast CI.
"""
import json
import subprocess
import tempfile
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
OWNER = "r6/scripts/custom_quickslots/player_puppet.reds"
HUB = "r6/scripts/G-RedRuntime/Input/InputHub.reds"
RUNTIME = "r6/scripts/G-RedRuntime/Core/Runtime.reds"

FRAMEWORK = {
    HUB: """module GRedRuntime
public class InputHub extends IScriptable {
  public func Subscribe(actionName: CName, listener: ref<InputListener>) -> Int32 {return 1;}
  public func SubscribeDevice(listener: ref<InputDeviceListener>) -> Int32 {return 1;}
  public func Unsubscribe(id: Int32) -> Bool {return true;}
  public func UnsubscribeDevice(id: Int32) -> Bool {return true;}
}
public class InputListener extends IScriptable {
  public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputDeviceListener extends IScriptable {
  public func OnGRedInputDeviceChanged(lastUsedKBM: Bool) -> Void {}
}
public class InputEvent extends IScriptable {
  public let actionName: CName;
  public let actionType: gameinputActionType;
}
""",
    RUNTIME: """module GRedRuntime
public class Runtime extends ScriptableSystem {
  public func IsReady() -> Bool {return true;}
  public func GetInputHub() -> ref<InputHub> {return null;}
  public static func Get(game: GameInstance) -> ref<Runtime> {return null;}
}
""",
}

SOURCE = """module CustomQuickslots

@addField(PlayerPuppet)
private let customQuickslotsInputListener: ref<CustomQuickslotsGlobalInputListener>;

@addField(PlayerPuppet)
public let gracePeriodOver: Bool;

public class CustomQuickslotGamepadActionEvent extends Event {
  public let actionName: CName;
  public let actionType: gameinputActionType;
  public let gamepadQuickslotInput: Bool;
  public let onlyRefreshHint: Bool;
}

public class CustomQuickslotsGlobalInputListener {
  public let player: wref<PlayerPuppet>;
  public let lastUsedKBM: Bool;
  public let gamepadActions: array<CName>;

  public func Init(player: wref<PlayerPuppet>) -> Void {
    this.player = player;
    this.lastUsedKBM = player.PlayerLastUsedKBM();
    this.gamepadActions = [
      n"dpad_up", n"dpad_left",
      n"DropCarriedObject_CQ_0", n"ToggleCrouch_CQ_0",
      n"QuickMelee_CQ_0"
    ];
  }

  private static func SetLastUsedKBM(lastUsedKBM: Bool, afterGracePeriod: Bool) -> Void {}

  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let inputChanged: Bool = false;
    let gamepadQuickslotInput: Bool = false;
    CustomQuickslotsGlobalInputListener.SetLastUsedKBM(this.player.PlayerLastUsedKBM(), this.player.gracePeriodOver);
    if this.lastUsedKBM {
      if !this.player.PlayerLastUsedKBM() { this.lastUsedKBM = false; inputChanged = true; }
    } else {
      if this.player.PlayerLastUsedKBM() { this.lastUsedKBM = true; inputChanged = true; }
    }
    let actionName: CName = ListenerAction.GetName(action);
    let actionType: gameinputActionType = ListenerAction.GetType(action);
    // GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1
    gamepadQuickslotInput = ArrayContains(this.gamepadActions, actionName);
    if inputChanged || gamepadQuickslotInput {
      let evt: ref<CustomQuickslotGamepadActionEvent> = new CustomQuickslotGamepadActionEvent();
      if gamepadQuickslotInput && !this.lastUsedKBM {
        evt.actionName = actionName;
        evt.actionType = actionType;
        evt.gamepadQuickslotInput = true;
      }
      this.player.QueueEvent(evt);
    }
    if gamepadQuickslotInput {
      this.player.m_customHotkeyPlayerButtonData.ObserveAction(actionName, actionType);
    }
  }
}

@wrapMethod(PlayerPuppet)
protected cb func OnMakePlayerVisibleAfterSpawn(evt: ref<EndGracePeriodAfterSpawn>) -> Bool {
  wrappedMethod(evt);
  this.gracePeriodOver = true;
  let actionEvt: ref<CustomQuickslotGamepadActionEvent> = new CustomQuickslotGamepadActionEvent();
  actionEvt.onlyRefreshHint = true;
  this.QueueEvent(actionEvt);
}

@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
  wrappedMethod();
  this.m_customHotkeyPlayerButtonData = new CustomHotkeyPlayerButtonData();
  this.m_customHotkeyPlayerButtonData.SetPlayer(this);
  this.customQuickslotsInputListener = new CustomQuickslotsGlobalInputListener();
  this.customQuickslotsInputListener.Init(this);
  this.RegisterInputListener(this.customQuickslotsInputListener);
}

@wrapMethod(PlayerPuppet)
protected cb func OnDetach() -> Bool {
  this.UnregisterInventoryListenerCustom();
  this.UnregisterInputListener(this.customQuickslotsInputListener);
  this.customQuickslotsInputListener = null;
  wrappedMethod();
}

public class CustomHotkeyPlayerButtonData {
  private let player: wref<PlayerPuppet>;
  public func ObserveAction(actionName: CName, actionType: gameinputActionType) -> Void {
    if Equals(actionType, gameinputActionType.BUTTON_HOLD_COMPLETE) {
      this.QueueOtherEvent();
    }
  }
  public func SetPlayer(player: wref<PlayerPuppet>) -> Void { this.player = player; }
}
"""

class QuickslotsInputContracts(unittest.TestCase):
    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.base = Path(t.name)
        self.count = 0

    def run_one(self, source=SOURCE, path=OWNER, framework=True):
        self.count += 1
        game = self.base / f"game-{self.count}"
        owner = game / path
        owner.parent.mkdir(parents=True)
        owner.write_text(source, encoding="utf-8")
        if framework:
            for rel, text in FRAMEWORK.items():
                other = game / rel
                other.parent.mkdir(parents=True, exist_ok=True)
                other.write_text(text, encoding="utf-8")
        out = self.base / f"generated-{self.count}"
        proc = subprocess.run(["dotnet", str(DLL), "phase2-quickslots-input",
            "--game", str(game), "--out", str(out)],
            text=True, capture_output=True, timeout=60)
        self.assertEqual(0, proc.returncode,
                         proc.stdout + "\n" + proc.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf-8"))
        self.assertEqual(owner.read_text("utf-8"), source)
        changed = out / path
        return report, changed.read_text("utf-8") if changed.exists() else None

    def verify(self, s):
        self.assertIn("import GRedRuntime.*", s)
        self.assertEqual(s.count("class CustomQuickslotsDeviceListener"), 1)
        self.assertIn("class CustomQuickslotsGlobalInputListener extends InputListener", s)
        self.assertEqual(s.count("func OnGRedInput("), 1)
        self.assertEqual(s.count("func OnDeviceChanged("), 1)
        self.assertEqual(s.count("func SyncGraceState("), 1)
        self.assertIn("SubscribeDevice(this.customQuickslotsDeviceListener)", s)
        self.assertIn("this.customQuickslotsInputHub.Subscribe(", s)
        self.assertIn("this.customQuickslotsInputHub.Unsubscribe(", s)
        self.assertIn("this.customQuickslotsInputHub.UnsubscribeDevice(", s)
        self.assertIn("this.customQuickslotsFallbackRegistered = true;", s)
        self.assertIn("if !IsDefined(this.customQuickslotsInputHub)", s)
        self.assertIn("GRSP-GENERIC:CNAME_ARRAY_MEMBERSHIP_V1", s)
        self.assertIn("gamepadQuickslotInput = ArrayContains(this.gamepadActions, actionName)", s)
        self.assertIn("protected cb func OnAction(action:", s)
        self.assertIn("this.customQuickslotsInputListener.SyncGraceState();", s)
        self.assertIn("this.UnregisterInventoryListenerCustom();", s)
        self.assertIn("this.QueueOtherEvent();", s)
        self.assertIn("this.player.m_customHotkeyPlayerButtonData.ObserveAction(", s)

    def test_stock_all_missing_in_one_generation_and_repeat_zero(self):
        report, out = self.run_one()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.verify(out)
        second, result = self.run_one(out)
        self.assertEqual(second["status"], "COMPLETE", second)
        self.assertEqual(second["appliedEdits"], 0)
        self.assertIsNone(result)

    def test_partial_missing_device_class_and_helper_reconstructed(self):
        _, source = self.run_one()
        src = source.replace("public class CustomQuickslotsDeviceListener extends InputDeviceListener {", 
            "public class CustomQuickslotsDeviceListener extends InputDeviceListener {", 1)
        first = src.index("public class CustomQuickslotsDeviceListener")
        last = src.index("\n@wrapMethod(PlayerPuppet)", first)
        src = src[:first] + src[last:]
        p, recovered = self.run_one(src)
        self.assertEqual(p["status"], "APPLIED", p)
        self.verify(recovered)
        self.assertEqual(recovered.count("SubscribeDevice("), 1)

    def test_partial_missing_field_and_sync_helper_only(self):
        _, source = self.run_one()
        source = source.replace("@addField(PlayerPuppet)\nprivate let customQuickslotsInputHub: wref<InputHub>;", "")
        start = source.index("  public func SyncGraceState(")
        end = source.index("  public func OnGRedInput(", start)
        source = source[:start] + source[end:]
        report, recovered = self.run_one(source)
        self.assertEqual(report["status"], "APPLIED", report)
        self.verify(recovered)
        self.assertEqual(recovered.count("customQuickslotsInputHub: wref<InputHub>;"), 1)

    def test_custom_module_spacing_moved_owner_and_extra_logic_survive(self):
        src = SOURCE.replace("module CustomQuickslots",
                             "module /* release-independent */ CustomQuickslots")
        src = src.replace("  this.QueueOtherEvent();",
                          "  this.QueueOtherEvent();\n      this.MyExtraGameplaySideEffect();")
        src = src.replace("this.RegisterInputListener(this.customQuickslotsInputListener);",
                          "this.CustomAuthorHook();\n  this.RegisterInputListener(this.customQuickslotsInputListener);")
        report, output = self.run_one(src,
            "r6/scripts/VortexModified/QuickslotsMain.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("this.CustomAuthorHook();", output)
        self.assertIn("this.MyExtraGameplaySideEffect();", output)
        self.verify(output)

    def test_framework_missing_refuses_only_bridge(self):
        r, s = self.run_one(framework=False)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(r["reason"], "CQ_INPUT_RUNTIME_ABI_NOT_PROVEN")
        self.assertIsNone(s)

    def test_unrecognized_listener_base_refuses_without_output(self):
        src = SOURCE.replace("class CustomQuickslotsGlobalInputListener {",
                             "class CustomQuickslotsGlobalInputListener extends CustomOwnerBase {")
        r, s = self.run_one(src)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(r["reason"], "CQ_INPUT_LISTENER_BASE_INCOMPATIBLE")
        self.assertIsNone(s)

    def test_source_action_names_mutated_refuses(self):
        src = SOURCE.replace("this.gamepadActions = [",
              "this.gamepadActions = [")
        src += "\npublic func BadMutation(listener: ref<CustomQuickslotsGlobalInputListener>) -> Void {\n"
        src += ' listener.gamepadActions = [n"other"];\n}\n'
        r, s = self.run_one(src)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertEqual(r["reason"], "CQ_INPUT_ACTION_ARRAY_MUTABLE_UNPROVEN")
        self.assertIsNone(s)

    def test_uninstalled_owner_not_added(self):
        r, s = self.run_one("module Other\npublic class OtherClass {}")
        self.assertEqual(r["status"], "NOT_APPLICABLE", r)
        self.assertIsNone(s)

if __name__ == "__main__":
    unittest.main()
