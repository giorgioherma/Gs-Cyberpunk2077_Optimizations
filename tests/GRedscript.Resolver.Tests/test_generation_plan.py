"""GCO REDscript Resolver owns a consumable ordered GENERATION plan.

Tests real generated .reds staging, truthful A-E status, source/plan drift,
zero ZIP, and the exact existing generator handoff (not a second optimizer).
"""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJ = REPO / "src/GRedscript.Resolver/GRSP.Resolver.csproj"


class GenerationPlanContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJ),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.game = self.root / "game"
        self.provider = self.game / "r6/scripts/Settings/Static.reds"
        self.consumer = self.game / "r6/scripts/Consumer/Example.reds"
        self.provider.parent.mkdir(parents=True)
        self.consumer.parent.mkdir(parents=True)
        self.provider.write_text("""module TestReusableSettings
public class ReusableSettings {
    public final static func Enabled() -> Bool { return true; }
    public final static func Diagnostics() -> Bool { return false; }
}
""", encoding="utf8")
        self.consumer.write_text("""module GenericConsumer
import TestReusableSettings.*
public class IndependentAuthor extends IScriptable {
    public func OnAction(gi: GameInstance) -> Bool {
        return ReusableSettings.Enabled() &&
            ReusableSettings.Diagnostics() &&
            GameInstance.GetQuestsSystem(gi).GetFact(n"test") == 1;
    }
}
""", encoding="utf8")
        self.plan = self.root / "generation-plan.json"
        self.audit = self.root / "audit.json"
        self.analysis = self.root / "analysis.json"
        self.staged = self.root / "generated"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=120)

    def make_plan(self):
        r = self.cli("plan-redscript", "--game", self.game,
                     "--out", self.plan)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(self.plan.read_text(encoding="utf8"))

    def make_analysis(self, sha):
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {"sourceStackSha256": sha}
        }))

    def test_resolver_decides_all_six_existing_generator_orders(self):
        doc = self.make_plan()
        self.assertEqual(doc["schemaVersion"],
                         "gco.redscript.generation-plan.v1")
        self.assertEqual(doc["sourceFiles"], 2)
        self.assertEqual(doc["implementedRuleCount"], 8)
        self.assertEqual(doc["captureGatedExistingRules"], 1)
        self.assertEqual(doc["deferredExperimentalSystems"], 5)
        self.assertFalse(doc["productionZipAuthorized"])
        self.assertFalse(doc["gameplayValidated"])
        active = doc["rules"][:7]
        self.assertEqual([r["order"] for r in active], list(range(1, 8)))
        self.assertEqual([r["id"] for r in active], [
            "fixed-cname-index", "literal-action-filter",
            "angle-sort", "late-system-read",
            "blackboard-definitions", "pure-constant-slices",
            "immutable-action-event-snapshot"
        ])
        self.assertEqual(doc["readyRuleCount"], sum(
            r["status"] == "READY_FOR_GENERATOR" for r in active))
        self.assertEqual(doc["eligibleSourceEdits"], sum(
            r["eligible"] for r in active))
        self.assertEqual(doc["rules"][5]["eligible"], 2)
        self.assertEqual(doc["rules"][5]["sourceFolders"], ["Consumer"])
        capture_gated = doc["rules"][7]
        self.assertEqual(capture_gated["id"], "measured-cname-membership")
        self.assertEqual(capture_gated["status"], "CAPTURE_GATED_EXISTING_GENERATOR")
        self.assertEqual(capture_gated["generator"], "preview-generic")
        self.assertIsNone(capture_gated["order"])
        self.assertIsNone(capture_gated["eligible"])
        for rule in doc["rules"][8:]:
            self.assertEqual(rule["status"], "NO_GENERATOR_YET")
            self.assertIsNone(rule["order"])
            self.assertIsNone(rule["generator"])
            self.assertIsNone(rule["eligible"])
        self.assertEqual([r["system"] for r in doc["rules"][8:]],
                         ["A", "B", "C", "D", "E"])
        self.assertEqual(len(list(self.game.rglob("*.reds"))), 2)

    def test_pass_generator_consumes_the_plan_and_emits_source_not_zip(self):
        doc = self.make_plan()
        self.make_analysis(doc["sourceStackSha256"])
        original = self.consumer.read_bytes()
        run = self.cli("stage-redscript-plan", "--game", self.game,
                       "--plan", self.plan, "--analysis", self.analysis,
                       "--out", self.staged)
        self.assertEqual(run.returncode, 0, run.stderr + run.stdout)
        stage = json.loads(
            (self.staged / "stage-result.json").read_text())
        self.assertEqual(stage["schemaVersion"],
                         "gco.redscript.staged-plan.v1")
        self.assertEqual(stage["generatedFiles"], 1)
        self.assertEqual(stage["appliedEdits"], 2)
        self.assertFalse(stage["productionZipAuthorized"])
        out = self.staged / "effective-preview/r6/scripts/Consumer/Example.reds"
        self.assertTrue(out.is_file())
        value = out.read_text()
        self.assertEqual(value.count(
            "GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1"), 2)
        self.assertIn("GameInstance.GetQuestsSystem(gi).GetFact", value)
        self.assertEqual(self.consumer.read_bytes(), original)
        self.assertFalse(list(self.staged.rglob("*.zip")))

    def test_plan_order_cannot_be_modified_by_caller(self):
        doc = self.make_plan()
        self.make_analysis(doc["sourceStackSha256"])
        doc["rules"][0]["order"] = 2
        self.plan.write_text(json.dumps(doc))
        run = self.cli("stage-redscript-plan", "--game", self.game,
                       "--plan", self.plan, "--analysis", self.analysis,
                       "--out", self.staged)
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("REDSCRIPT_PLAN_COMMAND_OR_ORDER_DRIFT",
                      run.stdout + run.stderr)
        self.assertFalse(self.staged.exists())

    def test_source_drift_rejects_stale_plan(self):
        doc = self.make_plan()
        self.make_analysis(doc["sourceStackSha256"])
        self.consumer.write_text(
            self.consumer.read_text() + "\n// update\n")
        run = self.cli("stage-redscript-plan", "--game", self.game,
                       "--plan", self.plan, "--analysis", self.analysis,
                       "--out", self.staged)
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("REDSCRIPT_PLAN_STALE_OR_TAMPERED",
                      run.stdout + run.stderr)
        self.assertFalse(self.staged.exists())

    def test_no_write_to_game_and_no_overwrite_existing_stage(self):
        run = self.cli("plan-redscript", "--game", self.game,
                       "--out", self.consumer.parent / "plan.json")
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("REDSCRIPT_PLAN_OUTPUT_COLLISION",
                      run.stdout + run.stderr)
        doc = self.make_plan()
        self.make_analysis(doc["sourceStackSha256"])
        self.staged.mkdir()
        sentinel = self.staged / "KEEP.txt"
        sentinel.write_text("keep")
        run = self.cli("stage-redscript-plan", "--game", self.game,
                       "--plan", self.plan, "--analysis", self.analysis,
                       "--out", self.staged)
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("REDSCRIPT_PLAN_STAGE_OUTPUT_OR_INPUT_INVALID",
                      run.stdout + run.stderr)
        self.assertEqual(sentinel.read_text(), "keep")

    def test_capture_hash_must_match_current_plan(self):
        self.make_plan()
        self.make_analysis("0" * 64)
        run = self.cli("stage-redscript-plan", "--game", self.game,
                       "--plan", self.plan, "--analysis", self.analysis,
                       "--out", self.staged)
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("REDSCRIPT_PLAN_CAPTURE_MISMATCH",
                      run.stdout + run.stderr)
        self.assertFalse(self.staged.exists())


if __name__ == "__main__":
    unittest.main()
