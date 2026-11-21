"""Historical Pass 2.0 FlushingEtiquette: atomic InputHub registration and
exclusive original OnAction fallback; never lose current generic early gate.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
FE = "r6/scripts/FlushingEtiquette/FE_Input.reds"
FRAME = "r6/scripts/ExternalRuntime/Core/GRedRuntime.reds"

FRAMEWORK = '''module GRedRuntime
public class InputEvent extends IScriptable {
  public let actionName: CName;
  public let actionType: gameinputActionType;
  public let consumed: Bool;
}
public class InputListener extends IScriptable {
  public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputHub extends IScriptable {
  public func Subscribe(actionName: CName, listener: ref<InputListener>) -> Int32 { return 1; }
  public func Unsubscribe(id: Int32) -> Bool { return true; }
}
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetInputHub() -> ref<InputHub> { return null; }
}
'''

SOURCE = '''
module FlushingEtiquette
// One listener set and one OnAction wrap for every key the mod owns. (｡◕‿◕｡)

@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
  let result = wrappedMethod();
  this.RegisterInputListener(this, n"Flush_Relief");
  this.RegisterInputListener(this, n"Flush_VehicleRelief");
  this.RegisterInputListener(this, n"Flush_WashHands");
  let mods = FEInput.ModifierActions();
  let i = 0;
  while i < ArraySize(mods) {
    this.RegisterInputListener(this, mods[i]);
    i += 1;
  }
  this.FERegisterStatusHotkey();
  return result;
}

@addMethod(PlayerPuppet)
@if(ModuleExists("StatusEffectsHotkey"))
private func FERegisterStatusHotkey() -> Void {
  this.RegisterInputListener(this, n"ShowStatusEffects");
}

@addMethod(PlayerPuppet)
@if(!ModuleExists("StatusEffectsHotkey"))
private func FERegisterStatusHotkey() -> Void {}

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
  if FEInput.Handle(this, action) { return true; }
  return wrappedMethod(action, consumer);
}

public class FEModifierState {
  private let m_held: array<Bool>;

  public func Note(index: Int32, pressed: Bool) -> Void {
    while ArraySize(this.m_held) <= index {
      ArrayPush(this.m_held, false);
    }
    this.m_held[index] = pressed;
  }

  public func IsHeld(index: Int32) -> Bool {
    if index < 0 || index >= ArraySize(this.m_held) { return false; }
    return this.m_held[index];
  }
}

public abstract class FEInput {

  public static func ModifierActions() -> array<CName> {
    let names: array<CName> = [n"FEM_Flush_Relief", n"FEM_Flush_VehicleRelief", n"FEM_Flush_WashHands"];
    return names;
  }

  private static func IndexOfModifier(name: CName) -> Int32 {
    // GCO-GENERIC:CNAME_FIXED_INDEX_V1:ModifierActions
    if Equals(n"FEM_Flush_Relief", name) { return 0; }
    if Equals(n"FEM_Flush_VehicleRelief", name) { return 1; }
    if Equals(n"FEM_Flush_WashHands", name) { return 2; }
    return -1;
  }

  private static func IsPadKey(key: EInputKey) -> Bool {
    let v = EnumInt(key);
    return v >= EnumInt(EInputKey.IK_Pad_First) && v <= EnumInt(EInputKey.IK_Pad_Last);
  }

  private static func IsModHeld(player: ref<PlayerPuppet>, sys: ref<FESystem>, index: Int32, modKey: EInputKey) -> Bool {
    if Equals(modKey, EInputKey.IK_None) { return true; }
    if !IsDefined(sys) { return false; }
    if sys.ModifierState().IsHeld(index) { return true; }
    return !Equals(FEInput.IsPadKey(modKey), player.PlayerLastUsedPad());
  }

  public static func KeyLabel(key: EInputKey, modKey: EInputKey) -> String {
    let name = FEInput.Strip(key, "IK_");
    if StrLen(name) == 0 { return GetLocalizedTextByKey(n"flush_key_none"); }
    if Equals(modKey, EInputKey.IK_None) { return name; }
    return FEInput.Strip(modKey, "IK_") + " + " + name;
  }

  public static func PadLabel(key: EInputKey) -> String {
    let name = StrReplace(StrReplace(FEInput.Strip(key, "IK_Pad_"), "IK_", ""), "_", " / ");
    if StrLen(name) == 0 { return GetLocalizedTextByKey(n"flush_key_none"); }
    return name;
  }

  private static func Strip(key: EInputKey, prefix: String) -> String {
    if Equals(key, EInputKey.IK_None) { return ""; }
    return StrReplace(NameToString(EnumValueToName(n"EInputKey", Cast<Int64>(EnumInt(key)))), prefix, "");
  }

  public static func Handle(player: ref<PlayerPuppet>, action: ListenerAction) -> Bool {
    let name = ListenerAction.GetName(action);
    if !Equals(name, n"ShowStatusEffects") && !Equals(name, n"FEM_Flush_Relief") && !Equals(name, n"FEM_Flush_VehicleRelief") && !Equals(name, n"FEM_Flush_WashHands") && !Equals(name, n"Flush_Relief") && !Equals(name, n"Flush_VehicleRelief") && !Equals(name, n"Flush_WashHands") { return false; }
    let type = ListenerAction.GetType(action);
    let gi = player.GetGame();

    if Equals(name, n"ShowStatusEffects") {
      let hud = FEHUDSystem.Get(gi);
      if IsDefined(hud) {
        if Equals(type, gameinputActionType.BUTTON_PRESSED) { hud.SetForceShow(true); }
        if Equals(type, gameinputActionType.BUTTON_RELEASED) { hud.SetForceShow(false); }
      }
      return false;
    }

    let pressed = Equals(type, gameinputActionType.BUTTON_PRESSED);
    let released = Equals(type, gameinputActionType.BUTTON_RELEASED);
    let modIndex = FEInput.IndexOfModifier(name);
    if modIndex >= 0 {
      if pressed || released {
        let modSys = FESystem.Get(gi);
        if IsDefined(modSys) { modSys.ModifierState().Note(modIndex, pressed); }
      }
      return false;
    }

    if !released { return false; }
    let sys = FESystem.Get(gi);
    if !IsDefined(sys) { return false; }
    let cfg = FEConfig.Get(gi);
    if !IsDefined(cfg) { return false; }
    if Equals(name, n"Flush_Relief") {
      if !FEInput.IsModHeld(player, sys, 0, cfg.Flush_Relief_Mod) { return false; }
      return sys.TryToiletRelief(player);
    }
    if Equals(name, n"Flush_VehicleRelief") {
      if !FEInput.IsModHeld(player, sys, 1, cfg.Flush_VehicleRelief_Mod) { return false; }
      return sys.TryVehicleRelief(player);
    }
    if Equals(name, n"Flush_WashHands") {
      if !FEInput.IsModHeld(player, sys, 2, cfg.Flush_WashHands_Mod) { return false; }
      return sys.TryWashHands(player);
    }
    return false;
  }
}
'''

class FeHubTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.serial = 0

    def generate(self, text=SOURCE, framework=True, relative=FE):
        self.serial += 1
        game = self.root / f"game{self.serial}"
        target = game / relative
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding='utf-8')
        if framework:
            mod = game / FRAME
            mod.parent.mkdir(parents=True,exist_ok=True)
            mod.write_text(FRAMEWORK, encoding='utf-8')
        output = self.root / f"out{self.serial}"
        proc = subprocess.run(['dotnet',str(DLL),'phase2-fe-input-hub',
                               '--game',str(game),'--out',str(output)],
                              capture_output=True, text=True, timeout=120)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        report=json.loads((output/'phase2-result.json').read_text('utf-8'))
        self.assertEqual(target.read_text('utf-8'),text)
        generated=(output/relative).read_text('utf-8') if (output/relative).exists() else None
        return report, generated

    def assert_complete(self, text):
        self.assertEqual(text.count('m_feHubInputActive: Bool'),1)
        self.assertEqual(text.count('m_feFallbackInputRegistered: Bool'),1)
        self.assertEqual(text.count('private func FETryHubInputRoutes()'),1)
        self.assertEqual(text.count('private func FECleanupInputRoutes()'),1)
        self.assertEqual(text.count('class FEGRedInputListener extends InputListener'),1)
        self.assertEqual(text.count('func OnGRedInput(evt: ref<InputEvent>)'),1)
        self.assertIn('if !this.m_feHubInputActive && FEInput.Handle(this, action)',text)
        self.assertIn('if !this.FETryHubInputRoutes()',text)
        self.assertIn('if feSubscription <= 0',text)
        self.assertIn('this.FECleanupInputRoutes();',text)
        self.assertIn('this.m_feGRedInputHub.Unsubscribe(',text)
        self.assertIn('this.UnregisterInputListener(this, feMods[feIndex]);',text)
        self.assertIn('return FEInput.HandleValues(player, name, type);',text)
        self.assertIn('evt.consumed = true;',text)
        self.assertIn('// GCO-GENERIC:CNAME_FIXED_INDEX_V1',text)
        self.assertIn('if !Equals(name, n"ShowStatusEffects")',text)
        self.assertIn('@if(!ModuleExists("StatusEffectsHotkey"))',text)
        self.assertIn('this.FERegisterStatusHotkey();',text)
        self.assertIn('return sys.TryWashHands(player);',text)
        self.assertIn('return wrappedMethod(action, consumer);',text)

    def test_stock_one_run_and_repeat(self):
        report, output=self.generate()
        self.assertEqual(report['status'],'APPLIED',report)
        self.assertIsNotNone(output)
        self.assert_complete(output)
        repeat, new=self.generate(output)
        self.assertEqual(repeat['status'],'COMPLETE',repeat)
        self.assertIsNone(new)

    def test_partial_missing_field_completes(self):
        _, output=self.generate()
        changed=output.replace('@addField(PlayerPuppet)\nprivate let m_feHubInputActive: Bool;\n','')
        self.assertNotEqual(changed,output)
        report, repaired=self.generate(changed)
        self.assertEqual(report['status'],'APPLIED',report)
        self.assert_complete(repaired)
        again, other=self.generate(repaired)
        self.assertEqual(again['status'],'COMPLETE',again)
        self.assertIsNone(other)

    def test_partial_missing_cleanup_helper_completes(self):
        _, output=self.generate()
        start=output.index('@addMethod(PlayerPuppet)\nprivate func FECleanupInputRoutes()')
        end=output.index('public class FEGRedInputListener',start)
        damaged=output[:start]+output[end:]
        report, repaired=self.generate(damaged)
        self.assertEqual(report['status'],'APPLIED',report)
        self.assert_complete(repaired)

    def test_custom_method_and_relocated_owner(self):
        custom=SOURCE.replace('  public static func PadLabel(key: EInputKey)',
            '  public static func KeepCustom() -> Int32 { return 73; }\n  public static func PadLabel(key: EInputKey)')
        report, out=self.generate(custom,relative='r6/scripts/FlushingEtiquette/MyInput.custom.reds')
        self.assertEqual(report['status'],'APPLIED',report)
        self.assertIn('KeepCustom() -> Int32 { return 73; }',out)
        self.assert_complete(out)

    def test_incompatible_source_is_narrowly_refused(self):
        damaged=SOURCE.replace('if FEInput.Handle(this, action) { return true; }',
            'if FEInput.Handle(this, action) { return false; }')
        report, output=self.generate(damaged)
        self.assertEqual(report['status'],'REFUSED',report)
        self.assertIn('FE_HUB_ENGINE_WRAPPER_SHAPE_UNPROVEN',report['reason'])
        self.assertIsNone(output)

    def test_absent_owner_no_new_mod(self):
        report, output=self.generate('module OtherMod\n',relative='r6/scripts/OtherMod/Foo.reds')
        self.assertEqual(report['status'],'NOT_APPLICABLE',report)
        self.assertIsNone(output)

    def test_plain_postgeneric_without_prior_semantic_gate_still_applies(self):
        early = '    if !Equals(name, n"ShowStatusEffects") && !Equals(name, n"FEM_Flush_Relief") && !Equals(name, n"FEM_Flush_VehicleRelief") && !Equals(name, n"FEM_Flush_WashHands") && !Equals(name, n"Flush_Relief") && !Equals(name, n"Flush_VehicleRelief") && !Equals(name, n"Flush_WashHands") { return false; }\n'
        plain = SOURCE.replace(early, '')
        self.assertNotEqual(plain, SOURCE)
        report, output = self.generate(plain)
        self.assertEqual(report['status'], 'APPLIED', report)
        self.assertIn('return FEInput.HandleValues(player, name, type);', output)
        self.assertIn('return sys.TryWashHands(player);', output)
        self.assertIn('GCO-GENERIC:CNAME_FIXED_INDEX_V1', output)
        repeated, none = self.generate(output)
        self.assertEqual(repeated['status'], 'COMPLETE', repeated)
        self.assertIsNone(none)

    def test_missing_framework_refuses(self):
        report, output=self.generate(framework=False)
        self.assertEqual(report['status'],'REFUSED',report)
        self.assertEqual(report['reason'],'FE_HUB_RUNTIME_API_MISSING')
        self.assertIsNone(output)

if __name__ == '__main__':
    unittest.main()
