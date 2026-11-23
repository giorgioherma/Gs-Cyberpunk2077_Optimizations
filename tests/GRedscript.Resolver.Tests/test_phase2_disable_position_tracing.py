"""Historical DisablePositionReveal Pass 7 tracing-state guard contracts."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/DisablePositionReveal/DisablePositionReveal.reds"
STOCK = """module TracePositionOverhaul

@wrapMethod(NPCPuppet)
public final static func ChangeHighLevelState(obj: ref<GameObject>, newState: gamedataNPCHighLevelState) -> Void {
  wrappedMethod(obj, newState);
  if Equals(newState, gamedataNPCHighLevelState.Combat) {
    NPCPuppet.SetIsTracing(obj, false);
  };
}

@addMethod(NPCPuppet)
protected cb func OnDamageReceived(evt: ref<gameDamageReceivedEvent>) -> Bool {
  if evt.hitEvent.attackData.HasFlag(hitFlag.WasKillingBlow) {
    NPCPuppet.SetIsTracing(this, false);
  };
  super.OnDamageReceived(evt);
}

@addMethod(NPCPuppet)
protected cb func OnDefeated(evt: ref<DefeatedEvent>) -> Bool {
  NPCPuppet.SetIsTracing(this, false);
  super.OnDefeated(evt);
}
"""


class TracingGuardsTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.n = 0

    def generate(self, source, relative=TARGET):
        self.n += 1
        game = self.root / f"game{self.n}"
        file = game / relative
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding="utf8")
        out = self.root / f"out{self.n}"
        p = subprocess.run(
            ["dotnet", str(DLL), "phase2-disable-position-tracing",
             "--game", str(game), "--out", str(out)],
            capture_output=True, text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + "\n" + p.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        self.assertEqual(file.read_text("utf8"), source)
        produced = out / relative
        return report, produced.read_text("utf8") if produced.exists() else None

    def check(self, output):
        self.assertIsNotNone(output)
        self.assertIn("GetIsTracing()", output)
        self.assertIn("if IsDefined(__gcoDprNpc) && __gcoDprNpc.GetIsTracing()", output)
        self.assertIn(
            "if this.GetIsTracing() && evt.hitEvent.attackData.HasFlag(",
            output)
        self.assertIn("if this.GetIsTracing() {", output)
        self.assertEqual(output.count("NPCPuppet.SetIsTracing("), 3)
        self.assertEqual(output.count("super.OnDamageReceived(evt);"), 1)
        self.assertEqual(output.count("super.OnDefeated(evt);"), 1)
        self.assertEqual(output.count("wrappedMethod(obj, newState);"), 1)

    def test_stock_three_callbacks_then_noop(self):
        report, output = self.generate(STOCK)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 3)
        self.check(output)
        second, empty = self.generate(output)
        self.assertEqual(second["status"], "COMPLETE", second)
        self.assertEqual(second["alreadyComplete"], 3)
        self.assertIsNone(empty)

    def test_historical_partial_only_missing_work(self):
        partial = STOCK.replace(
            "  NPCPuppet.SetIsTracing(this, false);\n"
            "  super.OnDefeated(evt);",
            "  if this.GetIsTracing() {\n"
            "    NPCPuppet.SetIsTracing(this, false);\n"
            "  }\n"
            "  super.OnDefeated(evt);")
        report, output = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 2)
        self.assertEqual(report["alreadyComplete"], 1)
        self.check(output)
        self.assertEqual(self.generate(output)[0]["status"], "COMPLETE")

    def test_custom_relocated_unrelated_callback_preserved(self):
        changed = STOCK.replace(
            "  wrappedMethod(obj, newState);",
            "  wrappedMethod(obj, newState);\n"
            "  this.MyUnrelatedStateCheck();")
        changed += "\n@addMethod(NPCPuppet)\nfunc MyExtraMethod() -> Void {}\n"
        report, output = self.generate(changed,
            "r6/scripts/DisablePositionReveal/renamed/TraceCallbacks.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(output)
        self.assertIn("this.MyUnrelatedStateCheck();", output)
        self.assertIn("MyExtraMethod()", output)

    def test_incompatible_damage_still_optimizes_other_callbacks(self):
        changed = STOCK.replace(
            "super.OnDamageReceived(evt);",
            "this.CustomDamageHandler(evt);")
        report, output = self.generate(changed)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 2)
        self.assertIn("DPR_DAMAGE_SUPER_FALLBACK_MISSING", report["reason"])
        self.assertIn("this.CustomDamageHandler(evt);", output)
        self.assertIn(
            "if evt.hitEvent.attackData.HasFlag(hitFlag.WasKillingBlow)",
            output)
        self.assertIn("if this.GetIsTracing() {", output)

    def test_absent_mod_not_applicable(self):
        report, output = self.generate("module Other\npublic class Foo {}\n",
            "r6/scripts/Other/Foo.reds")
        self.assertEqual(report["status"], "NOT_APPLICABLE")
        self.assertIsNone(output)


if __name__ == "__main__":
    unittest.main()
