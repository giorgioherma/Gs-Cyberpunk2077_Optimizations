"""Independent Pass 4 contracts: real source-aware .NET 10 generator.
Tests use tiny owner-valid fragments; GitHub CI additionally exercises
2,331 actual post-generic files and official SCC 0.5.31.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/"src/GRedscript.Pass4/bin/Release/net10.0/GRedscript.Pass4.dll"
STEALTH="r6/scripts/StealthRunner/npc_puppet.reds"
CHIP="r6/scripts/zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds"
R_SYS="r6/scripts/ReinforcementsSystem/ReinforcementSystem.reds"
R_UTIL="r6/scripts/ReinforcementsSystem/Utils.reds"

STEALTH_STOCK="""module StealthRunner
@wrapMethod(NPCInitTask)
public final func OnActivate(context: ScriptExecutionContext) -> Void {
  wrappedMethod(context);
  let npc: ref<NPCPuppet> = AIBehaviorScriptBase.GetNPCPuppet(context);
  if IsDefined(npc) {
    let player: wref<PlayerPuppet> = GameInstance.GetPlayerSystem(npc.GetGame()).GetLocalPlayerMainGameObject() as PlayerPuppet;
    if IsDefined(player) && IsDefined(player.m_stealthRunnerPlayerStorage) && !IsDefined(npc.GetStealthRunnerStatus()) {
      StealthRunnerQuestEvent.NPCInitEvent(npc, player);
    }
  }
}
@wrapMethod(NPCPuppet)
protected cb func OnDetach() -> Bool {
  let player: ref<PlayerPuppet> = GameInstance.GetPlayerSystem(this.GetGame()).GetLocalPlayerMainGameObject() as PlayerPuppet;
  if IsDefined(player) {
    StealthRunnerQuestEvent.UnregisterEnemyEvent(this.m_stealthRunnerStatus.quest, player, this);
  }
  this.m_stealthRunnerStatus = null;
  wrappedMethod();
}
@addField(NPCPuppet)
private let m_stealthRunnerStatus: ref<NPCPuppetStealthRunnerStatus>;
@wrapMethod(NPCPuppet)
protected cb func OnPlayerDetectionChangedEvent(evt: ref<PlayerDetectionChangedEvent>) -> Bool {
  wrappedMethod(evt);
  let player: ref<PlayerPuppet> = GameInstance.GetPlayerSystem(this.GetGame()).GetLocalPlayerMainGameObject() as PlayerPuppet;
  if !IsDefined(player) { return false; }
  let currentHighLevelState = 1;
  return true;
}
"""

CHIP_STOCK="""@wrapMethod(PlayerDevelopmentData)
public final const func IsNewPerkBought(perkType: gamedataNewPerkType) -> Int32 {
    let game = GetGameInstance();
    let player = GetPlayer(game);
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
    let playerMemory: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"Memory")));
    if (Equals(gamedataNewPerkType.Cool_Left_Milestone_2, perkType)) {
        if (playerMemory > 0.0) {
            return 2;
        }
    }
    if (Equals(gamedataNewPerkType.Tech_Central_Milestone_3, perkType)) {
        if (playerMemory > 0.0) {
            return 3;
        }
    }
    return wrappedMethod(perkType);
}
"""

REIN_SYS="""module Phoenicia.ReinforcementsMod.ReinforcementSystem
import Phoenicia.ReinforcementsMod.Utils.*
public class ReinforcementSystem extends ScriptableSystem {
    public let restrictedCordinates: array<SquareArea>;
    public let restrictedCharacters: array<TweakDBID>;
    private final func OnPlayerAttach(request: ref<PlayerAttachRequest>) -> Void {
        this.restrictedCordinates = [
            SquareArea(new Vector2(1.0, 1.0),new Vector2(2.0, 1.0),new Vector2(2.0, 2.0),new Vector2(1.0, 2.0))
        ];
    }
    public static func GetInstance(gi: GameInstance) -> ref<ReinforcementSystem> {
        return null;
    }
    public func ReinforcementsChecksCall(puppet: ref<ScriptedPuppet>, fastCall: Bool, biomon: Bool) -> Bool {
        let p = new Vector2(0.0, 0.0);
        let i: Int32 = 0;
        while (i < ArraySize(this.restrictedCordinates)) {
            if (IsPointInQuad(p, this.restrictedCordinates[i])) {
                return false;
            }
            i += 1;
        }
        let characterRecord = puppet.GetRecordID();
        if ArrayContains(this.restrictedCharacters, characterRecord) {
            return false;
        }
        return true;
    }
}
"""
REIN_UTIL="""module Phoenicia.ReinforcementsMod.Utils
public struct SquareArea {
    public let a: Vector2;
    public let b: Vector2;
    public let c: Vector2;
    public let d: Vector2;
}
private func SortSquare(sq: SquareArea) -> array<Vector2> {
    // GCO-GENERIC:ANGLE_SORT_INVARIANT_V1
    return [sq.a, sq.b, sq.c, sq.d];
}
private func PointInTriangle(p: Vector2,a: Vector2,b: Vector2,c: Vector2) -> Bool {
    return true;
}
public func IsPointInQuad(p: Vector2,sq: SquareArea) -> Bool {
    // GCO-GENERIC:QUAD_SAFE_AABB_BROADPHASE_V1
    let pts = SortSquare(sq);
    return PointInTriangle(p,pts[0],pts[1],pts[2]);
}
"""

class Pass4IsolatedContracts(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.temp=Path(tmp.name)
        self.seq=0

    def run_pass(self, files, overlay=None):
        self.seq+=1
        game=self.temp/f"game-{self.seq}"
        (game/"r6/scripts").mkdir(parents=True)
        for name,content in files.items():
            target=game/name
            target.parent.mkdir(parents=True,exist_ok=True)
            target.write_text(content,encoding="utf8")
        out=self.temp/f"out-{self.seq}"
        args=["dotnet",str(DLL),"pass4","--game",str(game),"--out",str(out)]
        if overlay is not None: args+=["--base-overlay",str(overlay)]
        r=subprocess.run(args,encoding="utf8",capture_output=True,timeout=90)
        self.assertEqual(r.returncode,0,r.stdout+"\n"+r.stderr)
        for name,content in files.items():
            self.assertEqual((game/name).read_text("utf8"),content)
        report=json.loads((out/"pass4-result.json").read_text("utf8"))
        result={name:p.read_text("utf8") for name in files
                if (p:=out/name).exists()}
        return report,result,out

    @staticmethod
    def owners(report):
        return {r["ruleId"]:r for r in report["operations"]}

    def test_all_present_single_generation_and_repeat_is_noop(self):
        initial={STEALTH:STEALTH_STOCK,CHIP:CHIP_STOCK,
                 R_SYS:REIN_SYS,R_UTIL:REIN_UTIL}
        r,output,_=self.run_pass(initial)
        self.assertEqual(r["status"],"APPLIED",r)
        self.assertEqual(set(output),set(initial))
        self.assertIn("this.m_stealthRunnerPlayer = null;",output[STEALTH])
        self.assertIn("this.m_stealthRunnerPlayer = player;",output[STEALTH])
        self.assertIn("if !relevant",output[CHIP])
        self.assertIn("GcoIsPointInPreparedReinforcementSquare(",output[R_SYS])
        self.assertIn("return IsPointInQuad(p, sq);",output[R_UTIL])
        self.assertIn("GCO-GENERIC:ANGLE_SORT_INVARIANT_V1",output[R_UTIL])
        self.assertIn("GCO-GENERIC:QUAD_SAFE_AABB_BROADPHASE_V1",output[R_UTIL])
        r2,o2,_=self.run_pass({**initial,**output})
        self.assertEqual(r2["status"],"COMPLETE",r2)
        self.assertEqual(o2,{})

    def test_stealth_handwritten_partial_completed(self):
        partial=STEALTH_STOCK.replace(
            "private let m_stealthRunnerStatus: ref<NPCPuppetStealthRunnerStatus>;",
            "private let m_stealthRunnerStatus: ref<NPCPuppetStealthRunnerStatus>;\n"
            "@addField(NPCPuppet)\nprivate let m_stealthRunnerPlayer: wref<PlayerPuppet>;")
        partial=partial.replace("    if IsDefined(player) &&",
                "    npc.m_stealthRunnerPlayer = player;\n    if IsDefined(player) &&")
        report,outputs,_=self.run_pass({STEALTH:partial})
        self.assertEqual(self.owners(report)["STEALTHRUNNER_NPC_PLAYER_CACHE_V1"]["status"],
                         "APPLIED",report)
        self.assertEqual(outputs[STEALTH].count(
            "private let m_stealthRunnerPlayer: wref<PlayerPuppet>;"),1)
        self.assertEqual(outputs[STEALTH].count("npc.m_stealthRunnerPlayer = player;"),1)
        self.assertIn("OnPlayerDetectionChangedEvent",outputs[STEALTH])

    def test_chip_perk_list_auto_includes_new_current_guard(self):
        custom=CHIP_STOCK.replace(
            "    return wrappedMethod(perkType);",
            "    if (Equals(gamedataNewPerkType.Reflexes_New_Custom, perkType)) {\n"
            "        if (playerMemory > 0.0) { return 1; }\n"
            "    }\n    return wrappedMethod(perkType);")
        _,output,_=self.run_pass({CHIP:custom})
        self.assertIn("Equals(perkType, gamedataNewPerkType.Reflexes_New_Custom)",
                      output[CHIP])

    def test_chip_partial_gate_completes_missing_perks(self):
        partial=CHIP_STOCK.replace(
            "    let game = GetGameInstance();",
            "    let relevant = Equals(perkType, gamedataNewPerkType.Cool_Left_Milestone_2);\n"
            "    if !relevant { return wrappedMethod(perkType); }\n"
            "    let game = GetGameInstance();")
        report,output,_=self.run_pass({CHIP:partial})
        self.assertEqual(report["status"],"APPLIED",report)
        self.assertIn("gamedataNewPerkType.Tech_Central_Milestone_3",output[CHIP])
        self.assertEqual(output[CHIP].count("if !relevant"),1)

    def test_reinforcement_public_array_preserved_and_mutation_fallback(self):
        report,output,_=self.run_pass({R_SYS:REIN_SYS,R_UTIL:REIN_UTIL})
        self.assertEqual(report["status"],"APPLIED")
        self.assertIn("public let restrictedCordinates: array<SquareArea>",output[R_SYS])
        self.assertIn("ArrayClear(this.gcoPreparedRestrictedCordinates)",output[R_SYS])
        self.assertIn("sq.d.Y != prepared.original.d.Y",output[R_UTIL])
        self.assertIn("return IsPointInQuad(p, sq);",output[R_UTIL])
        self.assertIn("if (gcoInsideRestricted)",output[R_SYS])
        self.assertIn("OnPlayerDetach",output[R_SYS])

    def test_reinforcement_missing_generic_narrow_refusal(self):
        unsafe=REIN_UTIL.replace("GCO-GENERIC:ANGLE_SORT_INVARIANT_V1","OLDER_SORT")
        r,out,_=self.run_pass({R_SYS:REIN_SYS,R_UTIL:unsafe,CHIP:CHIP_STOCK})
        self.assertNotIn(R_SYS,out)
        self.assertNotIn(R_UTIL,out)
        self.assertIn(CHIP,out)
        self.assertEqual(self.owners(r)["REINFORCEMENTS_PREPARED_QUAD_CACHE_V1"]["status"],"REFUSED")

    def test_relocated_owners_and_unrelated_code_retained(self):
        modified=CHIP_STOCK+"// unrelated patch preserved\n"
        r,out,_=self.run_pass({
          "r6/scripts/zzy_bChipwareExpansion/custom/perks.reds":modified,
          "r6/scripts/StealthRunner/v2/puppet.reds":STEALTH_STOCK})
        self.assertEqual(r["status"],"APPLIED")
        self.assertIn("// unrelated patch preserved",
            out["r6/scripts/zzy_bChipwareExpansion/custom/perks.reds"])

    def test_absent_owner_no_files_generated(self):
        r,out,_=self.run_pass({"r6/scripts/OtherMod/something.reds":
                              "module OtherMod\npublic class Test {}"})
        self.assertEqual(r["status"],"NOT_APPLICABLE",r)
        self.assertEqual(out,{})

    def test_unsafe_stealth_callback_narrow_refusal_preserves_chip(self):
        unsafe=STEALTH_STOCK.replace("wrappedMethod(evt);","return false;")
        r,out,_=self.run_pass({STEALTH:unsafe,CHIP:CHIP_STOCK})
        self.assertNotIn(STEALTH,out)
        self.assertIn(CHIP,out)
        self.assertEqual(self.owners(r)["STEALTHRUNNER_NPC_PLAYER_CACHE_V1"]["status"],
                         "REFUSED")

if __name__=="__main__":
    unittest.main()
