"""Historical Pass 2.1 GRS input: preserve engine fallback and consumed state."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest
ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / 'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
SOURCE_PATH = 'r6/scripts/GearRecoverySystem/25_GRSInteractionController.reds'
FRAMEWORK_PATH = 'r6/scripts/OtherFolder/GRedRuntime.reds'
FRAMEWORK = '''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetInputHub() -> ref<InputHub> { return null; }
}
public class InputListener extends IScriptable { public func OnGRedInput(evt: ref<InputEvent>) -> Void {} }
public class InputEvent extends IScriptable {
 public let actionName: CName; public let actionType: gameinputActionType; public let consumed: Bool;
}
public class InputHub extends IScriptable {
  public func Subscribe(actionName: CName, listener: ref<InputListener>) -> Int32 { return 1; }
  public func Unsubscribe(id: Int32) -> Bool { return true; }
}
'''
SOURCE = '''module GearRecoverySystem
public class GRSRecoverInputListener {
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
        if !Equals(ListenerAction.GetName(action), GRS_RecoverActionName()) {
            return false;
        }
        if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_HOLD_COMPLETE) {
            return false;
        }

        let controller = GRSInteractionController.Get(GetGameInstance());
        if !IsDefined(controller) { return false; }
        if !controller.IsInRange() { return false; }
        controller.TryRecoverGear();
        return true;
    }
}
@addField(PlayerPuppet)
private let m_grsRecoverInputListener: ref<GRSRecoverInputListener>;
@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
    wrappedMethod();
    let handler = GameInstance.GetSystemRequestsHandler();
    if IsDefined(handler) && handler.IsPreGame() { return true; }
    this.m_grsRecoverInputListener = new GRSRecoverInputListener();
    this.RegisterInputListener(this.m_grsRecoverInputListener);
    return true;
}
@wrapMethod(PlayerPuppet)
protected cb func OnDetach() -> Bool {
    wrappedMethod();
    if !IsDefined(this.m_grsRecoverInputListener) { return false; }
    this.UnregisterInputListener(this.m_grsRecoverInputListener);
    this.m_grsRecoverInputListener = null;
    return true;
}
'''

class GrsHubTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.num=0

    def generate(self, source=SOURCE, include_runtime=True, relative=SOURCE_PATH):
        self.num+=1
        game=self.root/f'game{self.num}'
        owner=game/relative
        owner.parent.mkdir(parents=True,exist_ok=True)
        owner.write_text(source,encoding='utf8')
        if include_runtime:
            r=game/FRAMEWORK_PATH
            r.parent.mkdir(parents=True,exist_ok=True)
            r.write_text(FRAMEWORK,encoding='utf8')
        out=self.root/f'out{self.num}'
        run=subprocess.run(['dotnet',str(DLL),'phase2-grs-input-hub',
                            '--game',str(game),'--out',str(out)],
                           capture_output=True,text=True,timeout=120)
        self.assertEqual(run.returncode,0,run.stdout+run.stderr)
        result=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(owner.read_text('utf8'),source)
        generated=(out/relative).read_text('utf8') if (out/relative).exists() else None
        return result, generated

    def complete(self,s):
        self.assertEqual(s.count('m_grsFallbackRegistered: Bool'),1)
        self.assertEqual(s.count('m_grsInputSubscription: Int32'),1)
        self.assertIn('class GRSRecoverInputListener extends InputListener',s)
        self.assertIn('private func GRSPerformRecover() -> Bool',s)
        self.assertIn('return this.GRSPerformRecover();',s)
        self.assertIn('if this.GRSPerformRecover() { evt.consumed = true; }',s)
        self.assertIn('this.m_grsInputHub.Unsubscribe(this.m_grsInputSubscription);',s)
        self.assertIn('if this.m_grsFallbackRegistered {',s)
        self.assertIn('this.RegisterInputListener(this.m_grsRecoverInputListener);',s)
        self.assertIn('controller.TryRecoverGear();',s)
        self.assertIn('if IsDefined(handler) && handler.IsPreGame()',s)
        self.assertIn('return true;',s)

    def test_stock_one_generation_then_noop(self):
        r,s=self.generate()
        self.assertEqual(r['status'],'APPLIED',r)
        self.complete(s)
        again,other=self.generate(s)
        self.assertEqual(again['status'],'COMPLETE',again)
        self.assertIsNone(other)

    def test_partially_missing_field_completed(self):
        r,s=self.generate()
        altered=s.replace('@addField(PlayerPuppet)\nprivate let m_grsFallbackRegistered: Bool;\n','')
        self.assertNotEqual(altered,s)
        next_r,next_s=self.generate(altered)
        self.assertEqual(next_r['status'],'APPLIED',next_r)
        self.complete(next_s)

    def test_relocated_modified_owner_preserved(self):
        current=SOURCE.replace('controller.TryRecoverGear();',
            'controller.TryRecoverGear();\n        controller.CustomAudit();')
        r,s=self.generate(current,relative='r6/scripts/GearRecoverySystem/custom/MyRecovery.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('controller.CustomAudit();',s)
        self.complete(s)

    def test_incompatible_missing_hold_type_refuses(self):
        changed=SOURCE.replace('gameinputActionType.BUTTON_HOLD_COMPLETE',
                               'gameinputActionType.BUTTON_RELEASED')
        r,s=self.generate(changed)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)

    def test_missing_runtime_refuses(self):
        r,s=self.generate(include_runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'GRS_HUB_RUNTIME_API_MISSING')
        self.assertIsNone(s)

    def test_absent_owner_never_emits_new_mod(self):
        r,s=self.generate('module SomethingElse\n',relative='r6/scripts/SomethingElse/a.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE',r)
        self.assertIsNone(s)

if __name__=='__main__':
    unittest.main()
