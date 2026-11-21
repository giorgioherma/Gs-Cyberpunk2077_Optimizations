"""Isolated Pass 8 source-state tests; no central stage-order modifications."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
RUNNER = (ROOT / "tests/GRedscript.Pass8.Checkpoint/bin/Release/net10.0/"
          "GRedscript.Pass8.Checkpoint.dll")
PERK = """@wrapMethod(PlayerDevelopmentData)
public final const func IsNewPerkBought(perkType: gamedataNewPerkType) -> Int32 {
    let game = GetGameInstance();
    let player = GetPlayer(game);
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(game);
    let playerMemory: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), IntEnum<gamedataStatType>(EnumValueFromName(n"gamedataStatType", n"Memory")));
    if (Equals(gamedataNewPerkType.Cool_Left_Milestone_2, perkType)) {
        if statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), gamedataStatType.Health) > 0.0 { return 2; }
    }
    if (Equals(gamedataNewPerkType.Tech_Central_Milestone_3, perkType)) {
        if playerMemory > 0.0 { return 3; }
    }
    return wrappedMethod(perkType);
}
"""
WRECK = """import ChipwareExpansion.System.*
@wrapMethod(SprintDecisions)
private final static func IsWreckingBallAllowed(const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let vanilaOutcome = wrappedMethod(scriptInterface);
    if (vanilaOutcome || !scriptInterface.executionOwner.IsPlayer()) { return vanilaOutcome; }
    let player = scriptInterface.executionOwner as PlayerPuppet;
    let neuralwareSystem = NeuralWareSystem.GetInstance(player.GetGame());
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(player.GetGame());
    let hasWreckingBallChip: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(player.GetEntityID()), gamedataStatType.Health);
    let isMelee = WeaponObject.IsMelee(GameObject.GetActiveWeapon(scriptInterface.executionOwner).GetItemID());
    if (isMelee && neuralwareSystem.CanSpendNeuralwareCapacity(1, false, false, true) && hasWreckingBallChip > 0.0) { return true; }
    return false;
}
"""
FLASH = """@wrapMethod(MeleeLeapDecisions)
protected final const func EnterCondition(stateContext: ref<StateContext>, const scriptInterface: ref<StateGameScriptInterface>) -> Bool {
    let gorillaArms: Bool = Equals(MeleeTransition.GetWeaponType(scriptInterface), gamedataItemType.Cyb_StrongArms);
    let statsSystem: ref<StatsSystem> = GameInstance.GetStatsSystem(GetGameInstance());
    let playerHasRelevantChip: Float = statsSystem.GetStatValue(Cast<StatsObjectID>(GetPlayer(GetGameInstance()).GetEntityID()), gamedataStatType.Health);
    if (playerHasRelevantChip > 0.0) {
        if gorillaArms && MeleeTransition.WeaponIsCharged(scriptInterface) { return false; }
        return wrappedMethod(stateContext, scriptInterface);
    } else {
        return wrappedMethod(stateContext, scriptInterface);
    }
}
"""
FILES = {
    "r6/scripts/zzy_bChipwareExpansion/chipsScripts/chipPerkUnlock.reds": PERK,
    "r6/scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/wreckingBall.reds": WRECK,
    "r6/scripts/zzy_bChipwareExpansion/chipsScripts/reflexChipsScripts/flashAndThunderclap.reds": FLASH
}

class Pass8Contracts(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.counter = 0

    def run_generator(self, sources, base_overlay=None):
        self.counter += 1
        game = self.root / f"game{self.counter}"
        for relative, text in sources.items():
            p = game / relative
            p.parent.mkdir(parents=True, exist_ok=True)
            p.write_text(text, encoding="utf8")
        (game / "r6/scripts").mkdir(parents=True, exist_ok=True)
        out = self.root / f"output{self.counter}"
        args = ["dotnet", str(RUNNER), "--game", str(game), "--out", str(out)]
        if base_overlay is not None:
            args += ["--base-overlay", str(base_overlay)]
        proc = subprocess.run(args, capture_output=True, text=True, timeout=90)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        for relative, text in sources.items():
            self.assertEqual((game / relative).read_text(encoding="utf8"), text)
        report = json.loads((out / "phase2-result.json").read_text(encoding="utf8"))
        generated = {}
        for path in out.rglob("*.reds"):
            generated[path.relative_to(out).as_posix()] = path.read_text(encoding="utf8")
        return report, generated, out

    def test_stock_three_independent_operations(self):
        report, files, _ = self.run_generator(FILES)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 3, report)
        self.assertEqual(len(files), 3)
        self.assertIn("if !(Equals(gamedataNewPerkType.Cool_Left_Milestone_2", files[next(k for k in files if k.endswith("chipPerkUnlock.reds"))])
        self.assertIn("let playerMemory:", files[next(k for k in files if k.endswith("chipPerkUnlock.reds"))])
        self.assertIn("if !isMelee { return false; }", files[next(k for k in files if k.endswith("wreckingBall.reds"))])
        self.assertIn("if !gorillaArms { return wrappedMethod", files[next(k for k in files if k.endswith("flashAndThunderclap.reds"))])

    def test_stock_to_complete_to_repeat_noop(self):
        _, files, _ = self.run_generator(FILES)
        result, second, _ = self.run_generator(files)
        self.assertEqual(result["status"], "COMPLETE", result)
        self.assertEqual(result["alreadyComplete"], 3, result)
        self.assertEqual(second, {})
        result, third, _ = self.run_generator(files)
        self.assertEqual(result["appliedEdits"], 0)
        self.assertEqual(third, {})

    def test_partial_guard_completes_missing_perk_without_duplication(self):
        partial = PERK.replace("    let game = GetGameInstance();",
            "    if !(Equals(gamedataNewPerkType.Cool_Left_Milestone_2, perkType))"
            " { return wrappedMethod(perkType); }\n    let game = GetGameInstance();")
        report, out, _ = self.run_generator({**FILES,
            next(k for k in FILES if k.endswith("chipPerkUnlock.reds")): partial})
        text = out[next(k for k in out if k.endswith("chipPerkUnlock.reds"))]
        self.assertEqual(report["status"], "APPLIED")
        self.assertEqual(text.count("if !(Equals(gamedataNewPerkType."), 1)
        self.assertIn("Tech_Central_Milestone_3, perkType)", text)

    def test_custom_relocated_owner_and_extra_perk(self):
        p = next(k for k in FILES if k.endswith("chipPerkUnlock.reds"))
        changed = PERK.replace("    let game = GetGameInstance();",
                               "    // Custom owner note; preserve verbatim\n"
                               "    let game = GetGameInstance();")
        changed = changed.replace("    return wrappedMethod(perkType);",
            "    if (Equals(gamedataNewPerkType.Reflexes_Master_Perk_5, perkType))"
            " { return 1; }\n    return wrappedMethod(perkType);")
        relocated = p.replace("chipPerkUnlock.reds", "custom/OwnerPerks.reds")
        report, files, _ = self.run_generator({
            relocated: changed,
            **{k: v for k, v in FILES.items() if k != p}
        })
        self.assertEqual(report["appliedEdits"], 3, report)
        self.assertIn("Custom owner note", files[relocated])
        self.assertIn("Reflexes_Master_Perk_5, perkType)", files[relocated])

    def test_incompatible_perk_refuses_only_perk_not_combat_gates(self):
        p = next(k for k in FILES if k.endswith("chipPerkUnlock.reds"))
        unsafe = PERK.replace("    let game = GetGameInstance();",
                              "    this.UpdateExternalPerkState(perkType);\n"
                              "    let game = GetGameInstance();")
        result, files, _ = self.run_generator({**FILES, p: unsafe})
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(result["appliedEdits"], 2, result)
        self.assertEqual(len(result["refusals"]), 1, result)
        self.assertIn("PASS8_PERK_SETUP_NOT_FIRST", result["refusals"][0])
        self.assertFalse(any(x.endswith("chipPerkUnlock.reds") for x in files))

    def test_absent_owner_is_noop(self):
        report, files, _ = self.run_generator({
            "r6/scripts/unrelated/example.reds": "public class Example {}\n"
        })
        self.assertEqual(report["status"], "NOT_APPLICABLE", report)
        self.assertEqual(report["refusals"], [])
        self.assertEqual(files, {})

    def test_markerless_historical_switch_is_not_rewritten(self):
        p = next(k for k in FILES if k.endswith("chipPerkUnlock.reds"))
        complete = """@wrapMethod(PlayerDevelopmentData)
public final const func IsNewPerkBought(perkType: gamedataNewPerkType) -> Int32 {
    switch perkType {
        case gamedataNewPerkType.Cool_Left_Milestone_2: return 2;
        default: return wrappedMethod(perkType);
    }
}
"""
        report, files, _ = self.run_generator({**FILES, p: complete})
        self.assertEqual(report["appliedEdits"], 2, report)
        self.assertFalse(any(x.endswith("chipPerkUnlock.reds") for x in files))

if __name__ == "__main__":
    unittest.main()
