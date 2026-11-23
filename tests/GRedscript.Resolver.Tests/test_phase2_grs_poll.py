"""GRS Pass 2.2 recurring interaction polling: native job + original fallback."""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
PATH='r6/scripts/GearRecoverySystem/25_GRSInteractionController.reds'
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
SOURCE='''module GearRecoverySystem
public class GRSInteractionPollCallback extends DelayCallback {
    public let controller: wref<GRSInteractionController>;
    public func Call() -> Void {
      if IsDefined(this.controller) { this.controller.OnPollTick(); }
    }
}
public class GRSInteractionController extends ScriptableSystem {
    private let pollQueued: Bool;
    private let inRange: Bool;
    private let vanillaBreachActive: Bool;
    private let interactionShown: Bool;
    private func StopVanillaBreachListener() -> Void {}
    private func CancelVanillaBreach(clear: Bool) -> Void {}
    private func ClearPrompt() -> Void {}
    private func PushPrompt() -> Void {}
    private func OnDetach() -> Void {
        this.StopVanillaBreachListener();
    }
    private func StopPollingAndClearPrompt() -> Void {
        this.pollQueued = false;
        this.inRange = false;
        this.CancelVanillaBreach(false);
        if this.interactionShown { this.ClearPrompt(); }
    }
    public func OnPollTick() -> Void {
        this.pollQueued = false;
        let recoverySystem = GRSRecoverySystem.Get(GetGameInstance());
        let player = GetPlayer(GetGameInstance());
        let nowInRange: Bool = false;
        let shouldContinue: Bool = false;
        if IsDefined(recoverySystem) && IsDefined(player) && recoverySystem.IsCacheReadyForInteraction() {
            shouldContinue = true;
            let guardedCacheSystem = GRSGuardedCacheSystem.Get(GetGameInstance());
            if IsDefined(guardedCacheSystem) {
                guardedCacheSystem.TrySpawnRecoveryGuards(player, recoverySystem, 5.0);
                guardedCacheSystem.TryAlertRecoveryGuards(player, recoverySystem, 5.0);
            }
        }
        if NotEquals(nowInRange, this.inRange) {
            this.PushPrompt();
        }
        if shouldContinue {
            this.QueuePoll();
        } else {
            this.StopPollingAndClearPrompt();
        }
    }
    private func QueuePoll() -> Void {
        if this.pollQueued { return; }
        let recoverySystem = GRSRecoverySystem.Get(GetGameInstance());
        if !IsDefined(recoverySystem) || !recoverySystem.IsCacheReadyForInteraction() {
            return;
        }
        this.pollQueued = true;
        let cb = new GRSInteractionPollCallback();
        cb.controller = this;
        GameInstance.GetDelaySystem(GetGameInstance()).DelayCallback(cb, GRS_ConfigInteractionPollInterval(), false);
    }
}
'''
class GrsPollTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.i=0

    def gen(self,source=SOURCE,runtime=True,path=PATH):
        self.i+=1
        game=self.root/f'game{self.i}'
        f=game/path
        f.parent.mkdir(parents=True,exist_ok=True)
        f.write_text(source,'utf8')
        if runtime:
            r=game/'r6/scripts/Relocated/RuntimeCore.reds'
            r.parent.mkdir(parents=True,exist_ok=True)
            r.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-grs-poll',
            '--game',str(game),'--out',str(out)],capture_output=True,text=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        result=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(f.read_text('utf8'),source)
        target=out/path
        return result,target.read_text('utf8') if target.exists() else None

    def check(self,s):
        self.assertEqual(s.count('class GRSGCOInteractionPollJob extends ScheduledJob'),1)
        self.assertEqual(s.count('m_gcoGrsPollID: Int32'),1)
        self.assertIn('this.m_gcoGrsPollScheduler.Unregister(this.m_gcoGrsPollID)',s)
        self.assertIn('grsScheduler.Register(grsJob)',s)
        self.assertIn('new GRSInteractionPollCallback()',s)
        self.assertIn('GRS_ConfigInteractionPollInterval(), true',s)
        self.assertIn('GRS_ConfigInteractionPollInterval(), false',s)
        self.assertIn('if this.m_gcoGrsPollID <= 0 { this.QueuePoll(); };',s)
        self.assertIn('if this.m_gcoGrsPollID <= 0 { this.pollQueued = false; };',s)
        self.assertIn('this.StopPollingAndClearPrompt();',s)
        self.assertIn('TryAlertRecoveryGuards(',s)

    def test_stock_one_pass_and_repeat(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r)
        self.check(s)
        next_r,next_s=self.gen(s)
        self.assertEqual(next_r['status'],'COMPLETE',next_r)
        self.assertIsNone(next_s)

    def test_partial_field_completed(self):
        r,s=self.gen()
        part=s.replace('  private let m_gcoGrsPollID: Int32;','')
        self.assertNotEqual(part,s)
        next_r,next_s=self.gen(part)
        self.assertEqual(next_r['status'],'APPLIED',next_r)
        self.check(next_s)

    def test_custom_owner_relocated_preserved(self):
        original=SOURCE.replace('this.PushPrompt();','this.CustomAudit(); this.PushPrompt();')
        r,s=self.gen(original,path='r6/scripts/GearRecoverySystem/Overrides/Revised.reds')
        self.assertEqual(r['status'],'APPLIED',r)
        self.assertIn('this.CustomAudit();',s)
        self.check(s)

    def test_missing_callback_constructor_refuses(self):
        r,s=self.gen(SOURCE.replace('new GRSInteractionPollCallback()',
                                    'new OtherCallback()'))
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertIsNone(s)

    def test_missing_runtime_refuses(self):
        r,s=self.gen(runtime=False)
        self.assertEqual(r['status'],'REFUSED',r)
        self.assertEqual(r['reason'],'GRS_POLL_RUNTIME_ABI_MISSING')
        self.assertIsNone(s)

    def test_absent_owner_no_output(self):
        r,s=self.gen('module Other\npublic class Other {}\n',
            path='r6/scripts/Other/Other.reds')
        self.assertEqual(r['status'],'NOT_APPLICABLE')
        self.assertIsNone(s)

if __name__=='__main__':
    unittest.main()
