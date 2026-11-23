"""GRS StateCache: direct player/BB fallback, partial completion and earlier stage preservation."""
import json,subprocess,tempfile,unittest
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
PATH='r6/scripts/GearRecoverySystem/25_GRSInteractionController.reds'
RUNTIME='''module GRedRuntime
public class Runtime extends ScriptableSystem {
  public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
  public func IsReady() -> Bool { return true; }
  public func GetStateCache() -> ref<StateCache> { return null; }
}
public class StateCache extends IScriptable {
  public func GetPlayer() -> wref<PlayerPuppet> { return null; }
  public func GetBlackboardSystem() -> wref<BlackboardSystem> { return null; }
}
'''
SOURCE='''module GearRecoverySystem
public class GRSInteractionController extends ScriptableSystem {
    private let pollQueued: Bool;
    private func OnDetach() -> Void {
        this.StopVanillaBreachListener();
    }
    private func StopPollingAndClearPrompt() -> Void {
        this.pollQueued = false;
        this.CancelVanillaBreach(false);
    }
    public func OnPollTick() -> Void {
        let recoverySystem = GRSRecoverySystem.Get(GetGameInstance());
        let player = GameInstance.GetPlayerSystem(GetGameInstance()).GetLocalPlayerMainGameObject() as PlayerPuppet;
        if IsDefined(player) { this.QueuePoll(); };
    }
    private func PushPrompt() -> Void {
        let defs = GetAllBlackboardDefs();
        let bb = GameInstance.GetBlackboardSystem(GetGameInstance()).Get(defs.UIInteractions);
    }
    private func PopPrompt() -> Void {
        let defs = GetAllBlackboardDefs();
        let bb = GameInstance.GetBlackboardSystem(GetGameInstance()).Get(defs.UIInteractions);
    }
    private func TryRecover() -> Bool {
        let player = GameInstance.GetPlayerSystem(GetGameInstance()).GetLocalPlayerMainGameObject() as PlayerPuppet;
        return IsDefined(player);
    }
}
'''
class GrsStateCacheTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory();self.addCleanup(t.cleanup)
        self.root=Path(t.name);self.i=0
    def gen(self,source=SOURCE,with_runtime=True,path=PATH):
        self.i+=1;root=self.root/f'game{self.i}'
        file=root/path;file.parent.mkdir(parents=True,exist_ok=True)
        file.write_text(source,'utf8')
        if with_runtime:
            f=root/'r6/scripts/Provider/Runtime.reds'
            f.parent.mkdir(parents=True,exist_ok=True);f.write_text(RUNTIME,'utf8')
        out=self.root/f'out{self.i}'
        p=subprocess.run(['dotnet',str(DLL),'phase2-grs-state',
            '--game',str(root),'--out',str(out)],
            capture_output=True,text=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+p.stderr)
        r=json.loads((out/'phase2-result.json').read_text('utf8'))
        self.assertEqual(file.read_text('utf8'),source)
        result=out/path
        return r,result.read_text('utf8') if result.exists() else None
    def check(self,s):
        self.assertEqual(s.count('m_gcoGrsStateCache: wref<StateCache>'),1)
        self.assertEqual(s.count('private func GCOGrsStateCache()'),1)
        self.assertEqual(s.count('private func GCOGrsPlayer()'),1)
        self.assertEqual(s.count('private func GCOGrsBlackboardSystem()'),1)
        self.assertIn('return GameInstance.GetPlayerSystem(GetGameInstance()).GetLocalPlayerMainGameObject() as PlayerPuppet;',s)
        self.assertIn('return GameInstance.GetBlackboardSystem(GetGameInstance());',s)
        self.assertIn('this.GCOGrsPlayer()',s)
        self.assertIn('this.GCOGrsBlackboardSystem()',s)
        self.assertGreaterEqual(s.count('this.m_gcoGrsStateCache = null;'),2)
    def test_stock_and_complete_noop(self):
        r,s=self.gen()
        self.assertEqual(r['status'],'APPLIED',r);self.check(s)
        rr,ss=self.gen(s)
        self.assertEqual(rr['status'],'COMPLETE',rr);self.assertIsNone(ss)
    def test_partial_missing_field_completed(self):
        r,s=self.gen()
        part=s.replace('    private let m_gcoGrsStateCache: wref<StateCache>;','')
        self.assertNotEqual(s,part)
        rr,ss=self.gen(part)
        self.assertEqual(rr['status'],'APPLIED',rr);self.check(ss)
    def test_relocated_custom_and_current_lifecycle_preserved(self):
        src=SOURCE.replace('this.QueuePoll();','this.QueuePoll(); this.CustomGuardAudit();')
        rr,ss=self.gen(src,path='r6/scripts/GearRecoverySystem/Overrides/Custom.reds')
        self.assertEqual(rr['status'],'APPLIED',rr)
        self.assertIn('this.CustomGuardAudit();',ss);self.check(ss)
    def test_incompatible_poll_refuses_only_state_operation(self):
        src=SOURCE.replace('GRSRecoverySystem.Get(GetGameInstance())','SomeOtherSystem.Get(GetGameInstance())')
        rr,ss=self.gen(src)
        self.assertEqual(rr['status'],'REFUSED',rr);self.assertIsNone(ss)
    def test_missing_runtime_refuses(self):
        rr,ss=self.gen(with_runtime=False)
        self.assertEqual(rr['status'],'REFUSED',rr)
        self.assertEqual(rr['reason'],'GRS_STATE_RUNTIME_ABI_MISSING');self.assertIsNone(ss)
    def test_absent_owner(self):
        rr,ss=self.gen('module Other\npublic class Other {}\n',path='r6/scripts/Other/Other.reds')
        self.assertEqual(rr['status'],'NOT_APPLICABLE');self.assertIsNone(ss)
if __name__=='__main__':unittest.main()
