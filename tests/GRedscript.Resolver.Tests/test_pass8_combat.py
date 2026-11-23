"""Pass 8 chipware combat cache: post-generic eight-method composition and fallback."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
RUNNER=ROOT/"tests/GRedscript.Pass8.Checkpoint/bin/Release/net10.0/GRedscript.Pass8.Checkpoint.dll"
PATH_K="r6/scripts/zzy_bChipwareExpansion/chipsScripts/softwareChipsScripts/kerenzikovUpdate.reds"
PATH_W="r6/scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/wreckingBall.reds"
PATH_F="r6/scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/flashAndThunderclap.reds"
PATH_H="r6/scripts/zzy_bChipwareExpansion/chipsScripts/GRedHotpathCache.reds"
def keren(owner, name):
    return """@wrapMethod(%s)
protected final const func %s(const stateContext: ref<StateContext>, const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(GetGameInstance());
    let kerenzikovChipEquipped: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(scriptInterface.executionOwner.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipEquipped")));
    let crackedKerenzikovChipEquipped: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(scriptInterface.executionOwner.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipCrackedEquipped")));
    let kerenzikovChipChargesEquipped: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(scriptInterface.executionOwner.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"KerenzikovChipCharges")));
    if ((kerenzikovChipEquipped > 0.0 && kerenzikovChipChargesEquipped > 0.0) || crackedKerenzikovChipEquipped > 0.0) {
        if this.IsWorkspotValid(stateContext, scriptInterface) { return false; }
    }
    return wrappedMethod(stateContext, scriptInterface);
}
""" % (owner, name)
K = "\n".join([keren("DodgeDecisions", "EnterCondition"),
                 keren("KerenzikovDecisions", "ToTimeDilationReady"),
                 keren("KerenzikovDecisions", "EnterCondition")])
def wreck(owner,name,call):
    return """@wrapMethod(%s)
private final static func %s(const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let vanilla = %s;
    if (vanilla || !scriptInterface.executionOwner.IsPlayer()) { return vanilla; }
    let player = scriptInterface.executionOwner as PlayerPuppet;
    let neuralwareSystem = NeuralWareSystem.GetInstance(player.GetGame());
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(player.GetGame());
    let hasWreckingBallChip: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"WreckingBallEquipped")));
    if (hasWreckingBallChip == 0.0 || !neuralwareSystem.CanSpendNeuralwareCapacity(1, false, false, true)) { return false; }
    return hasWreckingBallChip > 0.0;
}
"""%(owner,name,call)
W="\n".join([
    wreck("SprintDecisions","IsWreckingBallAllowed","wrappedMethod(scriptInterface)"),
    wreck("MeleeTransition","MeleeSprintStateCondition","wrappedMethod(stateContext, scriptInterface)"),
    wreck("MeleeBodySlamAttackDecisions","EnterCondition","wrappedMethod(stateContext, scriptInterface)")])
def flash(owner,name):
    return """@wrapMethod(%s)
protected final const func %s(const stateContext: ref<StateContext>, const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(GetGameInstance());
    let playerHasRelevantChip: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(GetPlayer(GetGameInstance()).GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"FlashAndThunderclapEquipped")));
    if (playerHasRelevantChip > 0.0) { return wrappedMethod(stateContext, scriptInterface); }
    return false;
}
"""%(owner,name)
F=flash("MeleeLeapEvents","LeapToTarget")+flash("MeleeLeapDecisions","EnterCondition")
STOCK={PATH_K:K,PATH_W:W,PATH_F:F}

class CombatTests(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root=Path(tmp.name)
        self.counter=0
    def generate(self,sources,overlay=None):
        self.counter+=1
        game=self.root/f"g{self.counter}"
        (game/"r6/scripts").mkdir(parents=True)
        for path,source in sources.items():
            file=game/path
            file.parent.mkdir(parents=True,exist_ok=True)
            file.write_text(source,encoding="utf8")
        out=self.root/f"out{self.counter}"
        args=["dotnet",str(RUNNER),"--owner","combat","--game",str(game),"--out",str(out)]
        if overlay is not None:args+=["--base-overlay",str(overlay)]
        run=subprocess.run(args,text=True,capture_output=True,timeout=90)
        self.assertEqual(run.returncode,0,run.stdout+run.stderr)
        for path,source in sources.items():
            self.assertEqual((game/path).read_text(encoding="utf8"),source)
        result=json.loads((out/"phase2-result.json").read_text(encoding="utf8"))
        produced={p.relative_to(out).as_posix():p.read_text(encoding="utf8")
                  for p in out.rglob("*.reds")}
        return result,produced
    def test_complete_stock_eight_callbacks_one_helper(self):
        result,produced=self.generate(STOCK)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["refusals"],[],result)
        self.assertEqual(result["appliedEdits"],9,result)
        self.assertEqual(set(produced),set(STOCK)|{PATH_H})
        self.assertEqual(produced[PATH_K].count("GRedChipwareHotpathCache().HasKerenzikov(player)"),3)
        self.assertEqual(produced[PATH_W].count("GRedChipwareHotpathCache().HasWreckingBall(player)"),3)
        self.assertEqual(produced[PATH_F].count("GRedChipwareHotpathCache().HasFlashAndThunderclap(player)"),2)
        self.assertIn("m_nextRefresh = now + 0.05;",produced[PATH_H])
        self.assertIn("wrappedMethod(stateContext, scriptInterface)",produced[PATH_K])
    def test_second_generation_is_complete_and_byte_idempotent(self):
        _,produced=self.generate(STOCK)
        result,again=self.generate(produced)
        self.assertEqual(result["status"],"COMPLETE",result)
        self.assertEqual(result["alreadyComplete"],8,result)
        self.assertEqual(result["appliedEdits"],0,result)
        self.assertEqual(again,{})
    def test_manual_partial_finishes_only_missing_callbacks(self):
        _,produced=self.generate(STOCK)
        partial=dict(STOCK)
        partial[PATH_K]=produced[PATH_K]
        result,output=self.generate(partial)
        self.assertEqual(result["appliedEdits"],6,result) # 5 callbacks + helper
        self.assertNotIn(PATH_K,output)
        self.assertIn(PATH_W,output)
        self.assertIn(PATH_F,output)
        self.assertEqual(result["alreadyComplete"],3,result)
    def test_custom_unrelated_source_and_relocated_file(self):
        relocated=PATH_K.replace("kerenzikovUpdate.reds","custom/ManualKeren.reds")
        custom=K.replace("    let statsSystem:", "    // owner extension\n    let statsSystem:")
        report,output=self.generate({relocated:custom,PATH_W:W,PATH_F:F})
        self.assertEqual(report["appliedEdits"],9,report)
        self.assertIn("owner extension",output[relocated])
    def test_incompatible_one_callback_refuses_only_that_method(self):
        broken=K.replace("this.IsWorkspotValid(stateContext, scriptInterface)",
                         "this.CustomCheck(stateContext, scriptInterface)",1)
        broken=broken.replace("crackedKerenzikovChipEquipped > 0.0",
                              "crackedKerenzikovChipEquipped >= 0.0",1)
        report,output=self.generate({PATH_K:broken,PATH_W:W,PATH_F:F})
        self.assertEqual(report["appliedEdits"],8,report)
        self.assertEqual(len(report["refusals"]),1,report)
        self.assertIn("PASS8_KEREN_ELIGIBILITY_FLOW_CHANGED",report["refusals"][0])
        self.assertIn(PATH_K,output)
        self.assertEqual(output[PATH_K].count("GRedChipwareHotpathCache().HasKerenzikov(player)"),2)
    def test_absent_mod_no_helper_or_synthetic_owner(self):
        result,files=self.generate({"r6/scripts/other/other.reds":"public class Other {}\n"})
        self.assertEqual(result["status"],"NOT_APPLICABLE",result)
        self.assertEqual(files,{})
if __name__=="__main__":
    unittest.main()
