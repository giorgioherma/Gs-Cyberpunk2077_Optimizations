"""Vehicle Call Plus: three independent synchronous QuestSystem getter pairs, wrapper parity."""
import json,subprocess,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
PATH='r6/scripts/Vehicle Call Plus/VehicleCall_FastTravel.reds'
SOURCE='''import DarkFuture.Gameplay.DFVehicleSummonSystem
public class VehicleFastTravelTracking extends ScriptedPuppetPS {
  public let modON: Bool;
}
public class ActionVehicleServices extends ActionBool {
  public static func IsDefaultConditionMet(device: ref<ScriptableDeviceComponentPS>, context: GetActionsContext) -> Bool {
    let isStoryReady = GameInstance.GetQuestsSystem(device.GetGameInstance()).GetFact(n"q001_ripperdoc_done") >= 1;
    let isPhantomLiberty = GameInstance.GetQuestsSystem(device.GetGameInstance()).GetFact(n"ep1_standalone") >= 1;
    return isStoryReady || isPhantomLiberty;
  }
}
@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if Equals(ListenerAction.GetName(action), n"CallVehicle") {
        let ps = this.GetPS();
        if IsDefined(ps) {
            let isStoryReady = GameInstance.GetQuestsSystem(this.GetGame()).GetFact(n"q001_ripperdoc_done") >= 1;
            let isPhantomLiberyStandalone = GameInstance.GetQuestsSystem(this.GetGame()).GetFact(n"ep1_standalone") >= 1;
            if isStoryReady || isPhantomLiberyStandalone { return true; }
        }
    }
    return wrappedMethod(action, consumer);
}
@wrapMethod(PopupsManager)
protected cb func OnQuickSlotButtonHoldStartEvent(evt: ref<QuickSlotButtonHoldStartEvent>) -> Bool {
    if Equals(evt.dPadItemDirection, EDPadSlot.VehicleWheel) {
        let isStoryReady = GameInstance.GetQuestsSystem(this.m_playerPuppet.GetGame()).GetFact(n"q001_ripperdoc_done") >= 1;
        let isPhantomLiberyStandalone = GameInstance.GetQuestsSystem(this.m_playerPuppet.GetGame()).GetFact(n"ep1_standalone") >= 1;
        if isStoryReady || isPhantomLiberyStandalone { return false; }
    }
    return wrappedMethod(evt);
}
'''
class VcpQuestsTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup)
        self.root=Path(t.name);self.i=0
    def gen(self,source=SOURCE,path=PATH):
        self.i+=1;root=self.root/f'game{self.i}'
        f=root/path;f.parent.mkdir(parents=True,exist_ok=True);f.write_text(source,'utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-vcp-quests',
            '--game',str(root),'--out',str(out)],
            capture_output=True,text=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        r=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(f.read_text('utf8'),source)
        o=out/path
        return r,o.read_text('utf8') if o.exists() else None
    def check(self,s):
        self.assertEqual(s.count('let gcoVcpQuests = GameInstance.GetQuestsSystem('),3)
        self.assertEqual(s.count('gcoVcpQuests.GetFact('),6)
        self.assertEqual(s.count('return wrappedMethod(action, consumer);'),1)
        self.assertEqual(s.count('return wrappedMethod(evt);'),1)
        self.assertIn('n"q001_ripperdoc_done"',s)
        self.assertIn('n"ep1_standalone"',s)
    def test_stock_then_noop(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r);self.check(s)
        rr,ss=self.gen(s)
        self.assertEqual(rr['status'],'COMPLETE',rr);self.assertIsNone(ss)
    def test_partially_reused_one_fact_pair_completed(self):
        r,s=self.gen()
        part=s.replace('gcoVcpQuests.GetFact(n"ep1_standalone")',
            'GameInstance.GetQuestsSystem(this.GetGame()).GetFact(n"ep1_standalone")',1)
        self.assertNotEqual(s,part)
        rr,ss=self.gen(part)
        self.assertEqual(rr['status'],'APPLIED',rr);self.check(ss)
    def test_relocated_custom_gameplay_preserved(self):
        src=SOURCE.replace('if isStoryReady || isPhantomLiberyStandalone { return true; }',
            'this.CustomAudit(); if isStoryReady || isPhantomLiberyStandalone { return true; }')
        r,s=self.gen(src,'r6/scripts/Vehicle Call Plus/Overrides/NewOwner.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomAudit();',s);self.check(s)
    def test_independent_changed_game_context_refuses_only_one(self):
        src=SOURCE.replace(
            'GameInstance.GetQuestsSystem(this.GetGame()).GetFact(n"ep1_standalone")',
            'GameInstance.GetQuestsSystem(device.GetGameInstance()).GetFact(n"ep1_standalone")')
        r,s=self.gen(src)
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertEqual(len(r['refusals']),1,r)
        self.assertIn('OnAction:',r['refusals'][0])
        self.assertIsNotNone(s)
    def test_absent_owner(self):
        r,s=self.gen('module Other\npublic class Other {}\n',
            'r6/scripts/Other/Other.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE');self.assertIsNone(s)
if __name__=='__main__':unittest.main()
