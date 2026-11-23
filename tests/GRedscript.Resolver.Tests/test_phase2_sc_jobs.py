"""Shattered Chrome 2.2: two Scheduler jobs with independent original callbacks as fallbacks."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
PATH='r6/scripts/ShatteredChrome/SC.reds'
CALLBACK='r6/scripts/ShatteredChrome/SC_Callbacks.reds'
SOURCE='''module ShatteredChrome
public class ShatteredChromeSystem extends ScriptableSystem {
  private let m_gameInstance: GameInstance;
  private let m_isSessionActive: Bool;
  private let m_config: ref<SCConfig>;
  private let m_updateCallback: ref<SCUpdateCallback>;
  private let m_updateDelayID: DelayID;
  private let m_hudCallback: ref<SCHUDTickCallback>;
  private let m_hudDelayID: DelayID;
  private func OnDetach() -> Void {
    this.m_isSessionActive = false;
    this.CancelTick();
  }
  private cb func OnSessionReady(event: ref<GameSessionEvent>) -> Void {
    this.m_isSessionActive = true;
    this.ScheduleNextTick();
    this.ScheduleHUDTick();
  }
  private cb func OnSessionEnd(event: ref<GameSessionEvent>) -> Void {
    this.m_isSessionActive = false;
    this.CancelTick();
  }
  private func ScheduleNextTick() -> Void {
    this.m_updateCallback = new SCUpdateCallback();
    this.m_updateCallback.m_system = this;
    this.m_updateDelayID = GameInstance.GetDelaySystem(this.m_gameInstance).DelayCallback(
        this.m_updateCallback,SCTickInterval(),false);
  }
  private func ScheduleHUDTick() -> Void {
    this.m_hudCallback = new SCHUDTickCallback();
    this.m_hudCallback.m_system = this;
    this.m_hudDelayID = GameInstance.GetDelaySystem(this.m_gameInstance).DelayCallback(
        this.m_hudCallback,SCHudTickInterval(),false);
  }
  private func CancelTick() -> Void {
    let delaySys = GameInstance.GetDelaySystem(this.m_gameInstance);
    delaySys.CancelCallback(this.m_updateDelayID);
    delaySys.CancelCallback(this.m_hudDelayID);
  }
  public func OnHUDTick() -> Void {
    if !this.m_isSessionActive { return; }
    if !IsDefined(this.m_config) { this.ScheduleHUDTick(); return; }
    SCHUD.UpdateHUD(null,null,0);
    this.ScheduleHUDTick();
  }
  public func OnTick() -> Void {
    if !this.m_isSessionActive { return; }
    if !GameInstance.IsValid(this.m_gameInstance) { return; }
    if !this.m_config.modON { this.ScheduleNextTick(); return; }
    this.ScheduleNextTick();
  }
}
'''
CALLBACKS='''module ShatteredChrome
public class SCUpdateCallback extends DelayCallback {
  public let m_system: wref<ShatteredChromeSystem>;
  public func Call() -> Void { if IsDefined(this.m_system) { this.m_system.OnTick(); }; }
}
public class SCHUDTickCallback extends DelayCallback {
  public let m_system: wref<ShatteredChromeSystem>;
  public func Call() -> Void { if IsDefined(this.m_system) { this.m_system.OnHUDTick(); }; }
}
'''
RUNTIME='''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetScheduler() -> ref<Scheduler> { return null; }
}
public class Scheduler extends IScriptable {
  public func Register(job: ref<ScheduledJob>) -> Int32 { return 1; }
  public func Unregister(id: Int32) -> Bool { return true; }
}
public class ScheduledJob extends IScriptable {
  public func Configure(name: CName, interval: Float, repeat: Bool) -> Void {}
}
'''
class ScJobTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.i=0
    def gen(self,source=SOURCE,callbacks=CALLBACKS,runtime=True,path=PATH):
        self.i+=1
        root=self.root/f'game{self.i}'
        owner=root/path
        owner.parent.mkdir(parents=True,exist_ok=True)
        owner.write_text(source,'utf8')
        c=root/CALLBACK
        c.parent.mkdir(parents=True,exist_ok=True)
        c.write_text(callbacks,'utf8')
        if runtime:
            r=root/'r6/scripts/Random/Framework.reds'
            r.parent.mkdir(parents=True,exist_ok=True)
            r.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-sc-jobs',
            '--game',str(root),'--out',str(out)],
            capture_output=True,text=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        result=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(owner.read_text('utf8'),source)
        target=out/path
        return result,target.read_text('utf8') if target.exists() else None
    def check(self,s):
        self.assertIn('SCGCOMaintenanceJob extends ScheduledJob',s)
        self.assertIn('SCGCOHudJob extends ScheduledJob',s)
        self.assertEqual(s.count('m_gcoMainID: Int32'),1)
        self.assertEqual(s.count('m_gcoHudID: Int32'),1)
        self.assertIn('this.GCOStartJobs();',s)
        self.assertIn('this.m_gcoScheduler.Unregister(this.m_gcoMainID)',s)
        self.assertIn('this.m_gcoScheduler.Unregister(this.m_gcoHudID)',s)
        self.assertIn('if this.m_gcoMainID <= 0 { this.ScheduleNextTick(); };',s)
        self.assertIn('if this.m_gcoHudID <= 0 { this.ScheduleHUDTick(); };',s)
        self.assertIn('SCTickInterval(), true',s)
        self.assertIn('SCHudTickInterval(), true',s)
        self.assertIn('SCHUD.UpdateHUD(null,null,0);',s)
        self.assertIn('CancelCallback(this.m_updateDelayID)',s)
        self.assertIn('CancelCallback(this.m_hudDelayID)',s)
    def test_stock_and_repeat_noop(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(s)
        again,out=self.gen(s)
        self.assertEqual(again['status'],'COMPLETE',again)
        self.assertIsNone(out)
    def test_partial_field_completed(self):
        r,s=self.gen()
        part=s.replace('  private let m_gcoHudID: Int32;','')
        self.assertNotEqual(s,part)
        rr,ss=self.gen(part)
        self.assertEqual(rr['status'],'APPLIED',rr)
        self.check(ss)
    def test_partial_guard_completed_without_duplicate(self):
        r,s=self.gen()
        part=s.replace('if this.m_gcoHudID <= 0 { this.ScheduleHUDTick(); };',
                       'this.ScheduleHUDTick();',1)
        rr,ss=self.gen(part)
        self.assertEqual(rr['status'],'APPLIED',rr)
        self.check(ss)
    def test_relocated_modified_source(self):
        src=SOURCE.replace('SCHUD.UpdateHUD(null,null,0);',
            'this.CustomRenderAudit();\n    SCHUD.UpdateHUD(null,null,0);')
        r,s=self.gen(src,path='r6/scripts/ShatteredChrome/OwnerNew.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomRenderAudit();',s)
        self.check(s)
    def test_missing_hud_callback_refuses(self):
        altered=CALLBACKS.replace('SCHUDTickCallback','OtherHUDCallback')
        r,s=self.gen(callbacks=altered)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)
    def test_missing_runtime_refuses(self):
        r,s=self.gen(runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'SC_JOBS_RUNTIME_ABI_MISSING')
        self.assertIsNone(s)
    def test_absent_owner(self):
        self.i+=1
        root=self.root/f'game{self.i}'
        f=root/'r6/scripts/Other/Other.reds'
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text('module Other\npublic class Other {}\n','utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-sc-jobs',
            '--game',str(root),'--out',str(out)],capture_output=True,text=True)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        r=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(r['status'],'NOT_APPLICABLE')
if __name__=='__main__':
    unittest.main()
