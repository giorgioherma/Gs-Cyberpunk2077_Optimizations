"""VehicleMileage Pass 2.2: four independent jobs, safe stock/partial/custom/fallback states."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
OWNERS={
    'VM3DOdoHUD.reds':('OdoHUD','VM3DOdoService','VM3DOdoTick','ArmTick','Tick','tickArmed','this.tickPeriod','0.25'),
    'VMFuelAvailable.reds':('OdoHUD','VMFuelAvailableService','VMFuelAvailableTick','ArmTick','Tick','tickArmed','period','0.5'),
    'VMFuelGauge.reds':('OdoHUD','VM_FuelGauge','VM_FuelGaugeTick','ArmNextTick','Refresh','__armed','this.period','0.10'),
    'VMPriceSign.reds':('OdoHUD','VMPriceSignService','VMPriceSignTick','ArmTick','Tick','tickArmed','period','0.5')
}
RUNTIME='''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetScheduler() -> ref<Scheduler> { return null; }
}
public class Scheduler extends IScriptable {
  public func Register(job: ref<ScheduledJob>) -> Int32 { return 1; }
  public func Unregister(id: Int32) -> Bool { return true; }
  public func RescheduleJob(id: Int32, period: Float, delay: Float) -> Bool { return true; }
}
public class ScheduledJob extends IScriptable {
  public func Configure(name: CName, interval: Float, repeat: Bool) -> Void {}
}
'''
def owner_source(name):
    mod,cls,cb,arm,work,armed,period,_=OWNERS[name]
    variable=name in ('VMFuelAvailable.reds','VMPriceSign.reds')
    gauge=name=='VMFuelGauge.reds'
    token='  public let tickToken: Int32;\n' if variable else ''
    stopped='  private let callbackGeneration: Int32;\n  public func OnNewWorld() -> Void { this.callbackGeneration += 1; }\n' if gauge else ''
    cback=f'''private class {cb} extends DelayCallback {{
  public static func Create(svc: ref<{cls}>) -> ref<{cb}> {{
    return new {cb}();
  }}
  public func Call() -> Void {{}}
}}
'''
    if name=='VM3DOdoHUD.reds':
        delay='''this.tick = VM3DOdoTick.Create(this);
    this.tickArmed = true;
    ds.DelayCallback(this.tick, this.tickPeriod, false);'''
    elif gauge:
        delay='''this.tick = VM_FuelGaugeTick.Create(this);
    this.__armed = true;
    ds.DelayCallback(this.tick, this.period, false);'''
    else:
        delay=f'''this.tickToken += 1;
    this.tick = {cb}.Create(this, this.tickToken);
    this.tickArmed = true;
    ds.DelayCallback(this.tick, period, false);'''
    arg='period: Float' if variable else ''
    invoke='0.5' if variable else ''
    tick=f'''public func {work}() -> Void {{
    this.CustomAudit();
    this.{arm}({invoke});
  }}'''
    if gauge:
        tick='''public func Refresh() -> Void { this.CustomAudit(); }
  public func AnimStep() -> Void { this.AnimStepExisting(); }'''
    request='''
  public func RequestFastTicks(ticks: Int32) -> Void {
    if this.tickArmed {
      this.tickToken += 1;
      this.tickArmed = false;
    };
    this.ArmTick(0.01);
  }
''' if variable else ''
    return f'''module {mod}
{cback}
public class {cls} extends IScriptable {{
  public let {armed}: Bool;
{token}{stopped}
  private let tick: ref<{cb}>;
  private let callbackSystem: wref<CallbackSystem>;
  private let registered: Bool;
  private let tickPeriod: Float = 0.25;
  private let period: Float = 0.10;
  private func HideLastWidget() -> Void {{}}
  public func Start() -> Void {{}}
  {tick}
  private func {arm}({arg}) -> Void {{
    if this.{armed} {{ return; }};
    let ds = GameInstance.GetDelaySystem(GetGameInstance());
    if !IsDefined(ds) {{ return; }};
    {delay}
  }}
  {request}
}}
'''
class VehicleMileageTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.seq=0
    def generate(self,sources=None,with_runtime=True,relocate=None):
        self.seq+=1
        sources=sources if sources is not None else {n:owner_source(n) for n in OWNERS}
        root=self.root/f'game{self.seq}'
        for n,text in sources.items():
            file=root/'r6/scripts/VehicleMileage/UI'/(relocate.get(n,n) if relocate else n)
            file.parent.mkdir(parents=True,exist_ok=True)
            file.write_text(text,'utf8')
        if with_runtime:
            p=root/'r6/scripts/Framework/GRedRuntime.reds'
            p.parent.mkdir(parents=True,exist_ok=True)
            p.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.seq}'
        proc=subprocess.run(['dotnet',str(DLL),'phase2-vehicle-mileage',
            '--game',str(root),'--out',str(out)],capture_output=True,text=True,timeout=120)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        report=json.loads((out/'phase2-result.json').read_text('utf8'))
        generated={}
        for n in sources:
            rel='r6/scripts/VehicleMileage/UI/'+(relocate.get(n,n) if relocate else n)
            p=out/rel
            if p.exists():generated[n]=p.read_text('utf8')
        return report,generated
    def check(self,out):
        self.assertIn('vmScheduler.Register(vmJob)',out)
        self.assertIn('this.m_gcoVMJob = vmJob;',out)
        self.assertIn('this.m_gcoVMScheduler.Unregister(this.m_gcoVMJobID)',out)
        self.assertIn('GCOStopVMJob()',out)
        self.assertIn('DelayCallback(',out)
        self.assertIn('@wrapMethod(PlayerPuppet)',out)
        self.assertIn('this.CustomAudit();',out)
        self.assertEqual(out.count('m_gcoVMJobID: Int32'),1)
    def test_stock_all_four_and_repeat_noop(self):
        r,files=self.generate()
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertEqual(len(files),4,r)
        self.assertEqual(len(r['refusals']),0,r)
        for src in files.values():self.check(src)
        nxt,changes=self.generate(files)
        self.assertEqual(nxt['status'],'COMPLETE',nxt)
        self.assertEqual(changes,{})
    def test_partial_field_completed(self):
        _,files=self.generate()
        s=files['VMFuelAvailable.reds']
        partial=s.replace('  private let m_gcoVMJobID: Int32;','')
        self.assertNotEqual(partial,s)
        r,updated=self.generate({'VMFuelAvailable.reds':partial})
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(updated['VMFuelAvailable.reds'])
    def test_compatible_relocated_custom_source(self):
        src=owner_source('VM3DOdoHUD.reds').replace('this.CustomAudit();','this.CustomAudit(); this.MyCustomUI();')
        r,files=self.generate({'VM3DOdoHUD.reds':src},
                relocate={'VM3DOdoHUD.reds':'CustomNew3dOdo.reds'})
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.MyCustomUI();',files['VM3DOdoHUD.reds'])
        self.check(files['VM3DOdoHUD.reds'])
    def test_one_missing_callback_refuses_only_affected_owner(self):
        sources={n:owner_source(n) for n in OWNERS}
        sources['VMPriceSign.reds']=sources['VMPriceSign.reds'].replace(
            'VMPriceSignTick.Create(this, this.tickToken)', 'OtherCallback.Create(this)')
        r,files=self.generate(sources)
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertEqual(len(files),3,r)
        self.assertEqual(len(r['refusals']),1,r)
        self.assertIn('price-sign:',r['refusals'][0])
    def test_missing_runtime_refuses_independently(self):
        r,files=self.generate(with_runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(len(r['refusals']),4,r)
        self.assertEqual(files,{})
    def test_absent_owner(self):
        r,files=self.generate(sources={})
        self.assertEqual(r['status'],'NOT_APPLICABLE',r)
        self.assertEqual(files,{})
if __name__=='__main__':
    unittest.main()
