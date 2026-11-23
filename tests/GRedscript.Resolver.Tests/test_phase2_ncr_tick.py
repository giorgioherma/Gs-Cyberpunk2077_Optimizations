"""NCR Pass 2.2: recurring maintenance scheduler, original callback fallback and cleanup."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
TARGET='r6/scripts/NightCityRemembers/NCR.reds'
FRAMEWORK='''module GRedRuntime
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
SOURCE='''module NightCityRemembers
public class NCRSystem extends ScriptableSystem {
  private let m_tickDelayID: DelayID;
  private let m_ready: Bool;
  private let m_config: ref<NCRConfig>;
  private let m_lastGameDay: Int32;
  private func OnDetach() -> Void {}
  private cb func OnSessionReady(event: ref<GameSessionEvent>) -> Void {
    this.m_ready = true;
    this.m_lastGameDay = -1;
    this.ScheduleTick();
  }
  private cb func OnSessionEnd(event: ref<GameSessionEvent>) -> Void {
    this.m_ready = false;
    let delaySys: ref<DelaySystem> = GameInstance.GetDelaySystem(this.GetGameInstance());
    if IsDefined(delaySys) { delaySys.CancelCallback(this.m_tickDelayID); };
  }
  private func ScheduleTick() -> Void {
    if !this.m_ready { return; };
    let cb: ref<NCRTickCallback> = new NCRTickCallback();
    cb.m_system = this;
    this.m_tickDelayID = GameInstance.GetDelaySystem(this.GetGameInstance()).DelayCallback(cb,30.0,false);
  }
  public func OnTick() -> Void {
    if !this.m_ready || !IsDefined(this.m_config) || !this.m_config.modON { this.ScheduleTick(); return; };
    let player = GetPlayer(this.GetGameInstance());
    if !IsDefined(player) { this.ScheduleTick(); return; };
    let ps = player.GetPS() as PlayerPuppetPS;
    if !IsDefined(ps) { this.ScheduleTick(); return; };
    this.ExpirePendingKills(100.0);
    this.RotateBuckets(ps, 1);
    this.ScheduleTick();
  }
}
public class NCRTickCallback extends DelayCallback {
  public let m_system: wref<NCRSystem>;
  public func Call() -> Void { if IsDefined(this.m_system) { this.m_system.OnTick(); }; }
}
'''
class NcrTickTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.i=0

    def gen(self,source=SOURCE,with_runtime=True,path=TARGET):
        self.i+=1
        game=self.root/f'game{self.i}'
        f=game/path
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text(source,'utf8')
        if with_runtime:
            rt=game/'r6/scripts/Relocated/GRedRuntimeCore.reds'
            rt.parent.mkdir(parents=True,exist_ok=True)
            rt.write_text(FRAMEWORK,'utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-ncr-tick','--game',
           str(game),'--out',str(out)],capture_output=True,text=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        r=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(f.read_text('utf8'),source)
        target=out/path
        return r,target.read_text('utf8') if target.exists() else None

    def check(self,s):
        self.assertEqual(s.count('class NCRGCOMaintenanceJob extends ScheduledJob'),1)
        self.assertEqual(s.count('m_gcoTickID: Int32'),1)
        self.assertIn('this.GCOStartTick();',s)
        self.assertIn('this.m_gcoScheduler.Register(this.m_gcoTickJob)',s)
        self.assertIn('this.m_gcoScheduler.Unregister(this.m_gcoTickID)',s)
        self.assertIn('if this.m_gcoTickID <= 0 { this.ScheduleTick(); };',s)
        self.assertIn('this.ExpirePendingKills(100.0);',s)
        self.assertIn('this.RotateBuckets(ps, 1);',s)
        self.assertIn('CancelCallback(this.m_tickDelayID)',s)
        self.assertEqual(s.count('private func GCOStartTick()'),1)
        self.assertEqual(s.count('private func GCOStopTick()'),1)

    def test_stock_then_repeat_noop(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(s)
        again,out=self.gen(s)
        self.assertEqual(again['status'],'COMPLETE',again)
        self.assertIsNone(out)

    def test_partial_missing_field_or_stop_helper(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r)
        part=s.replace('  private let m_gcoTickID: Int32;','')
        self.assertNotEqual(part,s)
        next_r,next_s=self.gen(part)
        self.assertEqual(next_r['status'],'APPLIED',next_r)
        self.check(next_s)

    def test_custom_maintenance_interval_preserved(self):
        src=SOURCE.replace('cb,30.0,false','cb,12.5,false')
        r,s=self.gen(src)
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('Configure(n"NCR_MAINTENANCE", 12.5, true)',s)

    def test_modified_and_relocated_source_keeps_custom_logic(self):
        src=SOURCE.replace('    this.ExpirePendingKills(100.0);',
           '    this.CustomDailyAudit();\n    this.ExpirePendingKills(100.0);')
        r,s=self.gen(src,path='r6/scripts/NightCityRemembers/MyTweakedNCR.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomDailyAudit();',s)
        self.check(s)

    def test_incompatible_no_callback_refuses_without_output(self):
        src=SOURCE.replace('new NCRTickCallback()', 'new UnknownCallback()')
        r,s=self.gen(src)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)

    def test_missing_runtime_refuses_only_this_owner(self):
        r,s=self.gen(with_runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'NCR_SCHEDULER_RUNTIME_ABI_MISSING')
        self.assertIsNone(s)

    def test_absent_owner(self):
        r,s=self.gen('module Other\npublic class Other {}\n',
             path='r6/scripts/Other/other.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE',r)
        self.assertIsNone(s)

if __name__=='__main__':
    unittest.main()
