"""FE early input guard contracts: generic-first, stock/partial/complete,
custom action names, narrow refusal, preserved fallback and no input listeners.
Official SCC of the current private post-generic game tree runs in fast CI.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/FlushingEtiquette/FE_Input.reds"

SOURCE = """module FlushingEtiquette

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction,
                           consumer: ListenerActionConsumer) -> Bool {
  if FEInput.Handle(this, action) { return true; }
  return wrappedMethod(action, consumer);
}
public abstract class FEInput {
  private static func IndexOfModifier(name: CName) -> Int32 {
    if Equals(n"FEM_Flush_Relief", name) { return 0; }
    if Equals(n"FEM_Flush_VehicleRelief", name) { return 1; }
    if Equals(n"FEM_Flush_WashHands", name) { return 2; }
    return -1;
  }
  public static func Handle(player: ref<PlayerPuppet>,
                            action: ListenerAction) -> Bool {
    let name = ListenerAction.GetName(action);
    let type = ListenerAction.GetType(action);
    let gi = player.GetGame();
    if Equals(name, n"ShowStatusEffects") {
      FEHUDSystem.Get(gi);
      return false;
    }
    let pressed = Equals(type, gameinputActionType.BUTTON_PRESSED);
    let released = Equals(type, gameinputActionType.BUTTON_RELEASED);
    let modIndex = FEInput.IndexOfModifier(name);
    if modIndex >= 0 {
      if pressed || released {
        let modSys = FESystem.Get(gi);
        if IsDefined(modSys) { modSys.ModifierState().Note(modIndex, pressed); }
      }
      return false;
    }
    if !released { return false; }
    let sys = FESystem.Get(gi);
    if !IsDefined(sys) { return false; }
    let cfg = FEConfig.Get(gi);
    if !IsDefined(cfg) { return false; }
    if Equals(name, n"Flush_Relief") {
      return sys.TryToiletRelief(player);
    }
    if Equals(name, n"Flush_VehicleRelief") {
      return sys.TryVehicleRelief(player);
    }
    if Equals(name, n"Flush_WashHands") {
      return sys.TryWashHands(player);
    }
    return false;
  }
}
"""

class FlushingEtiquetteInputGateTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.folder = Path(tmp.name)
        self.counter = 0

    def generate(self, source, relative=TARGET):
        self.counter += 1
        game = self.folder / f"game{self.counter}"
        path = game / relative
        path.parent.mkdir(parents=True)
        path.write_text(source, encoding="utf-8")
        out = self.folder / f"output{self.counter}"
        p = subprocess.run(
            ["dotnet", str(DLL), "phase2-fe-input",
             "--game", str(game), "--out", str(out)],
            capture_output=True, text=True, timeout=65)
        self.assertEqual(p.returncode, 0, p.stdout + "\n" + p.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        self.assertEqual(path.read_text("utf8"), source)
        staged = out / relative
        return report, staged.read_text("utf8") if staged.exists() else None

    def check(self, original, generated):
        self.assertIsNotNone(generated)
        self.assertEqual(generated.count("ListenerAction.GetName(action)"), 1)
        self.assertEqual(generated.count("ListenerAction.GetType(action)"), 1)
        self.assertEqual(generated.count("player.GetGame()"), 1)
        self.assertEqual(generated.count("FEInput.IndexOfModifier(name)"), 1)
        self.assertIn("return wrappedMethod(action, consumer);", generated)
        self.assertLess(generated.index("ListenerAction.GetName(action)"),
                        generated.index("if !Equals(name,"))
        self.assertLess(generated.index("if !Equals(name,"),
                        generated.index("ListenerAction.GetType(action)"))
        for action in ("Flush_Relief", "Flush_VehicleRelief",
                       "Flush_WashHands", "FEM_Flush_Relief",
                       "FEM_Flush_VehicleRelief", "FEM_Flush_WashHands",
                       "ShowStatusEffects"):
            self.assertIn(f'!Equals(name, n"{action}")', generated)
        self.assertEqual(generated.count("return sys.TryToiletRelief"), 1)

    def test_stock_to_full_in_one_generation_then_noop(self):
        report, generated = self.generate(SOURCE)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.check(SOURCE, generated)
        report2, unchanged = self.generate(generated)
        self.assertEqual(report2["status"], "COMPLETE", report2)
        self.assertEqual(report2["appliedEdits"], 0)
        self.assertIsNone(unchanged)

    def test_partial_guard_completes_only_missing_names(self):
        prefix = ('if !Equals(name, n"Flush_Relief") && '
                  '!Equals(name, n"ShowStatusEffects") { return false; }')
        partial = SOURCE.replace(
            "    let type = ListenerAction.GetType(action);",
            "    "+prefix+"\n    let type = ListenerAction.GetType(action);")
        report, output = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(partial, output)
        self.assertEqual(output.count("if !Equals(name,"), 1)
        repeat, again = self.generate(output)
        self.assertEqual(repeat["status"], "COMPLETE")
        self.assertIsNone(again)

    def test_new_custom_action_branch_is_included_without_version_lock(self):
        modified = SOURCE.replace("    return false;\n  }\n}",
            """    if Equals(name, n"Flush_CustomAction") {
      return sys.TryCustomAction(player);
    }
    return false;
  }
}""")
        report, generated = self.generate(
            modified.replace("module FlushingEtiquette",
                             "module /* custom format */ FlushingEtiquette"),
            "r6/scripts/ThirdParty/FE_Changed.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn('!Equals(name, n"Flush_CustomAction")', generated)
        self.assertIn("return sys.TryCustomAction(player);", generated)

    def test_unproven_custom_name_use_refuses_only_fe_rule(self):
        incompatible = SOURCE.replace(
            "    let type = ListenerAction.GetType(action);",
            "    this.RecordUnrelatedAction(name);\n"
            "    let type = ListenerAction.GetType(action);")
        result, no_output = self.generate(incompatible)
        self.assertEqual(result["status"], "REFUSED")
        self.assertIsNone(no_output)

    def test_missing_fallback_refuses(self):
        incompatible = SOURCE.replace(
            "return wrappedMethod(action, consumer);", "return false;")
        result, output = self.generate(incompatible)
        self.assertEqual(result["status"], "REFUSED")
        self.assertEqual(result["reason"], "FE_INPUT_WRAPPED_FALLBACK_UNPROVEN")
        self.assertIsNone(output)

    def test_owner_absent_has_no_output(self):
        result, output = self.generate("module NotFE\npublic class Other {}")
        self.assertEqual(result["status"], "NOT_APPLICABLE")
        self.assertIsNone(output)


if __name__ == "__main__":
    unittest.main()
