"""StealthRunner Pass 3.0 pickpocket input: atomic routes and original engine fallback."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
TARGET='r6/scripts/StealthRunner/Pickpocket/Choice.reds'
RUNTIME='''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetInputHub() -> ref<InputHub> { return null; }
}
public class InputEvent extends IScriptable {
  public let actionName: CName;
  public let actionType: gameinputActionType;
  public let consumed: Bool;
}
public class InputListener extends IScriptable {
  public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputHub extends IScriptable {
  public func Subscribe(action: CName, listener: ref<InputListener>) -> Int32 { return 1; }
  public func Unsubscribe(id: Int32) -> Bool { return true; }
}
'''
SOURCE='''module StealthRunner
@addField(PlayerPuppet)
public let customLootInterface: ref<CustomLootInterface>;

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
  if IsDefined(this.customLootInterface) {
    if ListenerAction.IsButtonJustPressed(action) {
      if ListenerAction.IsAction(action, n"UI_MoveUp") {
        this.customLootInterface.Up();
      }
      if ListenerAction.IsAction(action, n"UI_MoveDown") {
        this.customLootInterface.Down();
      }
    }
  }
  wrappedMethod(action, consumer);
}
@wrapMethod(UpperBodyEventsTransition)
protected final func UpdateSwitchItem(timeDelta: Float, stateContext: ref<StateContext>, scriptInterface: ref<StateGameScriptInterface>) -> Bool {
  return wrappedMethod(timeDelta, stateContext, scriptInterface);
}
'''
class StealthRunnerTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.n=0
    def generate(self,source=SOURCE,runtime=True,owner=TARGET):
        self.n+=1
        game=self.root/f'game{self.n}'
        f=game/owner
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text(source,'utf8')
        if runtime:
            p=game/'r6/scripts/OtherGRT/Runtime.reds'
            p.parent.mkdir(parents=True,exist_ok=True)
            p.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.n}'
        proc=subprocess.run(['dotnet',str(DLL),'phase2-stealth-pickpocket',
            '--game',str(game),'--out',str(out)],capture_output=True,text=True,timeout=120)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        report=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(f.read_text('utf8'),source)
        result=out/owner
        return report,result.read_text('utf8') if result.exists() else None
    def check(self,s):
        self.assertIn('GCO_SRPickInputListener extends InputListener',s)
        self.assertEqual(s.count('m_gcoSrPickUpID: Int32'),1)
        self.assertIn('let up = hub.Subscribe(n"UI_MoveUp", listener);',s)
        self.assertIn('let down = hub.Subscribe(n"UI_MoveDown", listener);',s)
        self.assertIn('hub.Unsubscribe(up); return;',s)
        self.assertIn('if IsDefined(this.customLootInterface) && !this.m_gcoSrPickActive {',s)
        self.assertIn('this.GCO_SRPickUnbind();',s)
        self.assertIn('wrappedMethod(action, consumer);',s)
        self.assertIn('UpdateSwitchItem',s)
    def test_stock_then_complete_repeated_noop(self):
        r,s=self.generate()
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(s)
        rr,ss=self.generate(s)
        self.assertEqual(rr['status'],'COMPLETE',rr)
        self.assertIsNone(ss)
    def test_partial_missing_field_completed(self):
        r,s=self.generate()
        part=s.replace('private let m_gcoSrPickUpID: Int32;','')
        self.assertNotEqual(part,s)
        rr,ss=self.generate(part)
        self.assertEqual(rr['status'],'APPLIED',rr)
        self.check(ss)
    def test_custom_relocated_layout_preserved(self):
        custom=SOURCE.replace('this.customLootInterface.Down();',
            'this.CustomAudit(); this.customLootInterface.Down();')
        r,s=self.generate(custom,owner='r6/scripts/StealthRunner/custom/PickpocketHandler.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomAudit();',s)
        self.check(s)
    def test_missing_original_action_refuses_only_owner(self):
        src=SOURCE.replace('ListenerAction.IsAction(action, n"UI_MoveDown")',
                            'ListenerAction.IsAction(action, n"Other")')
        r,s=self.generate(src)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)
    def test_runtime_absent_refuses_without_output(self):
        r,s=self.generate(runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'SR_INPUT_RUNTIME_ABI_MISSING',r)
        self.assertIsNone(s)
    def test_absent_owner_no_output(self):
        r,s=self.generate('module Other\npublic class Other {}\n',
            owner='r6/scripts/Other/Other.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE',r)
        self.assertIsNone(s)
if __name__=='__main__':
    unittest.main()
