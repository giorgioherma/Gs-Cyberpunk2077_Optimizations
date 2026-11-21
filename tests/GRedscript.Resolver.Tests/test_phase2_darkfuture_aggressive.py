"""Game-tested Dark Future 10/6/6 callback throttles.

A single ordinary generation from post-generic source must install the
complete historical throttles. Partial/current-optimized/custom installations
receive missing work only. Existing 400ms tagged icon cadence is untouched.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/"src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PATH="r6/scripts/Dark Future/UI/DFBaseGameUIOverrides.reds"

STOCK=r'''import DarkFuture.Settings.DFSettings
import DarkFuture.System.*

@replaceMethod(buffListGameController)
private final func UpdateBuffDebuffList() -> Void {
    let buffList: array<BuffInfo>;
    let onScreenBuffsCount: Int32;
    let currBuffLoc: ref<buffListItemLogicController>;
    let currBuffWidget: ref<inkWidget>;
    let i: Int32=0;
    while i < ArraySize(this.m_buffDataList) {
        ArrayPush(buffList,this.m_buffDataList[i]);
        i += 1;
    };
    i = 0;
    while i < ArraySize(this.m_debuffDataList) {
        ArrayPush(buffList,this.m_debuffDataList[i]);
        i += 1;
    };
    onScreenBuffsCount = inkCompoundRef.GetNumChildren(this.m_buffsList);
    i=0;
    while i < onScreenBuffsCount {
        currBuffWidget=this.m_buffWidgets[i];
        currBuffLoc=currBuffWidget.GetController() as buffListItemLogicController;
        currBuffLoc.SetStatusEffectRecord(null);
        i += 1;
    };
    this.SendVisibilityUpdate(true,true);
}

@replaceMethod(inkCooldownGameController)
protected cb func OnEffectUpdate(v: Variant) -> Bool {
    let buffs: array<BuffInfo>;
    let debuffs: array<BuffInfo>;
    if !this.GetRootWidget().IsVisible() { return false; };
    this.GetBuffs(buffs);
    this.GetDebuffs(debuffs);
    this.ParseBuffList(buffs);
}

@addField(SingleCooldownManager)
private let m_gameplayTags: array<CName>;

@wrapMethod(SingleCooldownManager)
public final func ActivateCooldown(buffData: UIBuffInfo) -> Void {
    wrappedMethod(buffData);
    this.m_gameplayTags = TweakDBInterface.GetStatusEffectRecord(buffData.buffID).GameplayTags();
}

@replaceMethod(SingleCooldownManager)
public final func Update(timeLeft: Float, stackCount: Uint32) -> Void {
    let fraction: Float;
    if timeLeft <= 0.01 && !ArrayContains(this.m_gameplayTags, n"DarkFutureInfiniteDurationEffect") {
      this.GetRootWidget().SetVisible(false);
    } else {
      this.GetRootWidget().SetVisible(true);
      fraction = timeLeft / this.m_initialDuration;
    };
    this.SetTimeRemaining(timeLeft);
    this.SetStackCount(Cast<Int32>(stackCount));
    if timeLeft <= this.m_outroDuration {
      this.FillOutroAnimationStart();
    };
}

public func UserHelper() -> Bool { return true; }
'''


class DarkFutureAggressiveTests(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root=Path(tmp.name)
        self.n=0

    def generate(self,source,relative=PATH):
        self.n+=1
        game=self.root/f"game{self.n}"
        file=game/relative
        file.parent.mkdir(parents=True,exist_ok=True)
        file.write_text(source,encoding="utf8")
        out=self.root/f"out{self.n}"
        p=subprocess.run(["dotnet",str(DLL),"phase2-dark-future-aggressive",
            "--game",str(game),"--out",str(out)],
            capture_output=True,text=True,timeout=100)
        self.assertEqual(p.returncode,0,p.stdout+"\n"+p.stderr)
        report=json.loads((out/"phase2-result.json").read_text("utf8"))
        self.assertEqual(file.read_text("utf8"),source,
            "source admission must not rewrite installed game")
        result=out/relative
        return report,result.read_text("utf8") if result.exists() else None

    def check(self,source):
        self.assertIsNotNone(source)
        for name in ("m_gredBuffUpdateInitialized","m_gredBuffCount",
                     "m_gredDebuffCount","m_gredBuffUpdatePhase",
                     "m_gredEffectUpdatePhase","m_gredRenderInitialized",
                     "m_gredRenderPhase","m_gredLastTimeLeft",
                     "m_gredLastStackCount"):
            self.assertEqual(source.count("private let "+name+":"),1,name)
        self.assertIn("this.m_gredBuffUpdatePhase < 10",source)
        self.assertIn("currentBuffCount != this.m_gredBuffCount",source)
        self.assertIn("currentDebuffCount != this.m_gredDebuffCount",source)
        self.assertIn("this.m_gredEffectUpdatePhase < 6",source)
        self.assertIn("this.m_gredRenderPhase < 6",source)
        self.assertIn("timeLeft == -1.00",source)
        self.assertIn("this.m_gredLastTimeLeft == -1.00",source)
        self.assertIn("NotEquals(stackCount, this.m_gredLastStackCount)",source)
        self.assertIn("timeLeft <= this.m_outroDuration",source)
        self.assertIn("return false;",source)
        self.assertIn("this.SetTimeRemaining(timeLeft)",source)
        self.assertIn("this.SetStackCount(Cast<Int32>(stackCount))",source)
        self.assertIn("this.FillOutroAnimationStart()",source)
        self.assertIn("public func UserHelper()",source)

    def test_stock_complete_one_run_then_zero_changes(self):
        result,output=self.generate(STOCK)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],3)
        self.check(output)
        repeated,nothing=self.generate(output)
        self.assertEqual(repeated["status"],"COMPLETE",repeated)
        self.assertEqual(repeated["alreadyComplete"],3)
        self.assertEqual(repeated["appliedEdits"],0)
        self.assertIsNone(nothing)

    def test_partial_existing_fields_only_missing_guard_is_generated(self):
        original=STOCK.replace(
            "@replaceMethod(buffListGameController)",
            "@addField(buffListGameController)\n"
            "private let m_gredBuffCount: Int32;\n"
            "@replaceMethod(buffListGameController)")
        result,output=self.generate(original)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],3)
        self.check(output)
        self.assertEqual(self.generate(output)[0]["status"],"COMPLETE")

    def test_partial_historical_guard_reuses_old_operation(self):
        _,full=self.generate(STOCK)
        start=full.index("@addField(buffListGameController)")
        end=full.index("@replaceMethod(inkCooldownGameController)")
        stockStart=STOCK.index("@replaceMethod(buffListGameController)")
        stockEnd=STOCK.index("@replaceMethod(inkCooldownGameController)")
        partial=STOCK[:stockStart]+full[start:end]+STOCK[stockEnd:]
        result,output=self.generate(partial)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],2)
        self.assertEqual(result["alreadyComplete"],1)
        self.check(output)
        self.assertEqual(self.generate(output)[0]["status"],"COMPLETE")

    def test_newer_custom_and_existing_icon_work_survive(self):
        updated=STOCK.replace(
            "    let buffList: array<BuffInfo>;",
            "    let buffList: array<BuffInfo>;\n"
            "    let __gcoDfIconVisualDue: Bool = true;\n"
            "    // GCO-GENERIC:STABLE_ARRAY_BOUND_V1 (custom evidence)")
        updated+="\npublic func ExtraNewerCallback() -> Void { this.UserExtra(); }\n"
        result,output=self.generate(updated,
            "r6/scripts/Dark Future/UI/AnotherVersion/DfMoved.reds")
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertIn("let __gcoDfIconVisualDue: Bool = true;",output)
        self.assertIn("GCO-GENERIC:STABLE_ARRAY_BOUND_V1",output)
        self.assertIn("ExtraNewerCallback()",output)
        self.check(output)

    def test_incompatible_effect_only_narrow_refusal(self):
        custom=STOCK.replace("    this.GetDebuffs(debuffs);",
            "    this.GetMyDifferentEffects(debuffs);")
        result,output=self.generate(custom)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],2)
        self.assertIn("effect-wheel-six:",result["reason"])
        self.assertNotIn("m_gredEffectUpdatePhase",output)
        self.assertIn("this.GetMyDifferentEffects(debuffs);",output)
        self.assertIn("m_gredBuffUpdatePhase < 10",output)
        self.assertIn("m_gredRenderPhase < 6",output)

    def test_missing_owner_not_applicable(self):
        result,output=self.generate("module Other\nclass Foo {}\n",
            "r6/scripts/Other/Other.reds")
        self.assertEqual(result["status"],"NOT_APPLICABLE")
        self.assertIsNone(output)


if __name__=="__main__":
    unittest.main()
