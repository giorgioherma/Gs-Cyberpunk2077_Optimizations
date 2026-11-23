"""CyberwareEx Pass 3.0 controller Overclock: original engine fallback and no duplicate."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
TARGET='r6/scripts/CyberwareEx/CyberwareEx.Global.reds'
RUNTIME='''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetInputHub() -> ref<InputHub> { return null; }
}
public class InputEvent extends IScriptable {
  public let actionName: CName; public let actionType: gameinputActionType;
  public let consumed: Bool;
}
public class InputListener extends IScriptable {
  public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputHub extends IScriptable {
  public func Subscribe(name: CName, listener: ref<InputListener>) -> Int32 { return 1; }
  public func Unsubscribe(id: Int32) -> Bool { return true; }
}
'''
SOURCE='''import CyberwareEx.*

@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
    wrappedMethod();
    if this.IsControlledByLocalPeer() || IsHost() {
        this.RegisterInputListener(this, n"ToggleSprint");
    }
}

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if this.PlayerLastUsedPad() && Equals(ListenerAction.GetName(action), n"ToggleSprint")
        && Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) {
        let psmBlackboard = this.GetPlayerStateMachineBlackboard();
        let isFocusMode = Equals(IntEnum<gamePSMVision>(psmBlackboard.GetInt(GetAllBlackboardDefs().PlayerStateMachine.Vision)), gamePSMVision.Focus);
        if isFocusMode {
            if QuickHackableHelper.TryToCycleOverclockedState(this) {
                let dpadAction = new DPADActionPerformed();
                dpadAction.action = EHotkey.LBRB;
                dpadAction.state = EUIActionState.COMPLETED;
                dpadAction.successful = true;
                GameInstance.GetUISystem(this.GetGame()).QueueEvent(dpadAction);
            }
        }
    } else {
        wrappedMethod(action, consumer);
    }
}
'''
class CyberwareExTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name);self.n=0
    def gen(self,source=SOURCE,runtime=True,path=TARGET):
        self.n+=1
        game=self.root/f'game{self.n}'
        f=game/path
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text(source,'utf8')
        if runtime:
            p=game/'r6/scripts/GRT/Api.reds'
            p.parent.mkdir(parents=True,exist_ok=True)
            p.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.n}'
        proc=subprocess.run(['dotnet',str(DLL),'phase2-cyberware-ex',
            '--game',str(game),'--out',str(out)],capture_output=True,text=True,timeout=120)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        r=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(f.read_text('utf8'),source)
        target=out/path
        return r,target.read_text('utf8') if target.exists() else None
    def check(self,s):
        self.assertEqual(s.count('m_gcoCwHubID: Int32'),1)
        self.assertIn('GCO_CyberwareExInputListener extends InputListener',s)
        self.assertIn('gcoHub.Subscribe(n"ToggleSprint", gcoListener)',s)
        self.assertIn('if this.m_gcoCwHubID <= 0 {',s)
        self.assertIn('this.RegisterInputListener(this, n"ToggleSprint");',s)
        self.assertIn('this.m_gcoCwHub.Unsubscribe(this.m_gcoCwHubID)',s)
        self.assertIn('if this.m_gcoCwHubID > 0 && this.PlayerLastUsedPad()',s)
        self.assertIn('return false;',s)
        self.assertIn('wrappedMethod(action, consumer);',s)
        self.assertIn('QuickHackableHelper.TryToCycleOverclockedState(this)',s)
    def test_stock_and_repeated_noop(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(s)
        again,none=self.gen(s)
        self.assertEqual(again['status'],'COMPLETE',again)
        self.assertIsNone(none)
    def test_partial_field_completed(self):
        r,s=self.gen()
        partial=s.replace('private let m_gcoCwHubID: Int32;','')
        self.assertNotEqual(partial,s)
        rr,ss=self.gen(partial)
        self.assertEqual(rr['status'],'APPLIED',rr)
        self.check(ss)
    def test_custom_relocated_source(self):
        src=SOURCE.replace('wrappedMethod(action, consumer);',
            'this.CustomAudit(); wrappedMethod(action, consumer);')
        r,s=self.gen(src,path='r6/scripts/CyberwareEx/Custom/ChangedOwner.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomAudit();',s)
        self.check(s)
    def test_incompatible_changed_action_type_refuses(self):
        src=SOURCE.replace('gameinputActionType.BUTTON_RELEASED','gameinputActionType.BUTTON_PRESSED')
        r,s=self.gen(src)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)
    def test_missing_runtime_refuses(self):
        r,s=self.gen(runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'CW_INPUT_RUNTIME_ABI_MISSING',r)
        self.assertIsNone(s)
    def test_absent_owner(self):
        r,s=self.gen('module Other\npublic class Other {}\n',
            path='r6/scripts/Other/Other.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE',r)
        self.assertIsNone(s)
if __name__=='__main__':
    unittest.main()
