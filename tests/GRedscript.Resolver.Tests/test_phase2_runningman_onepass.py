"""Production GCO one-invocation REDscript Stage contracts for Pass 9.

Runs the true plan-redscript -> stage-redscript-plan pipeline, not a
surrogate CLI. Each run starts from effective post-generic owner sources.
"""
from pathlib import Path
import hashlib
import json
import subprocess
import tempfile
import unittest

from test_phase2_runningman_pass9 import SOURCE, TARGET

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PASS9 = REPO / "tools/Pass9/Pass9.csproj"

GENERIC_PROVIDER = """module TestReusableSettings
public class ReusableSettings {
    public final static func Enabled() -> Bool { return true; }
    public final static func Diagnostics() -> Bool { return false; }
}
"""
GENERIC_CONSUMER = """module GenericConsumer
import TestReusableSettings.*
public class IndependentAuthor extends IScriptable {
    public func OnAction(gi: GameInstance) -> Bool {
        return ReusableSettings.Enabled() &&
            ReusableSettings.Diagnostics() &&
            GameInstance.GetQuestsSystem(gi).GetFact(n"test") == 1;
    }
}
"""


class RunningManCanonicalStageTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.base = Path(temp.name)
        self.game = self.base / "game"
        self.running = self.game / TARGET
        self.running.parent.mkdir(parents=True)
        self.staged = self.base / "staged"
        self.plan = self.base / "plan.json"
        self.analysis = self.base / "analysis.json"
        self.reference = self.base / "independent"
        self.sources = []

    def source(self, running=SOURCE, with_generic=True):
        self.running.write_text(running, "utf8")
        self.sources = [self.running]
        if with_generic:
            for rel, contents in (
                ("r6/scripts/Settings/Static.reds", GENERIC_PROVIDER),
                ("r6/scripts/Consumer/Example.reds", GENERIC_CONSUMER),
            ):
                file = self.game / rel
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text(contents, "utf8")
                self.sources.append(file)
        return [hashlib.sha256(f.read_bytes()).hexdigest() for f in self.sources]

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=120)

    def stage(self):
        p = self.cli("plan-redscript", "--game", self.game, "--out", self.plan)
        self.assertEqual(p.returncode, 0, p.stderr + p.stdout)
        doc = json.loads(self.plan.read_text("utf8"))
        self.assertEqual(doc["implementedRuleCount"], 10)
        self.assertFalse(doc["productionZipAuthorized"])
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {"sourceStackSha256": doc["sourceStackSha256"]}
        }), "utf8")
        st = self.cli("stage-redscript-plan", "--game", self.game,
                      "--plan", self.plan, "--analysis", self.analysis,
                      "--out", self.staged)
        self.assertEqual(st.returncode, 0, st.stdout + st.stderr)
        report = json.loads((self.staged / "stage-result.json").read_text("utf8"))
        self.assertEqual(report["schemaVersion"], "gco.redscript.staged-plan.v1")
        self.assertFalse(report["productionZipAuthorized"])
        return report

    def independent(self):
        result = subprocess.run(
            ["dotnet", "run", "--no-build", "-c", "Release",
             "--project", str(PASS9), "--",
             "--game", str(self.game), "--out", str(self.reference)],
            text=True, capture_output=True, timeout=120)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        return json.loads((self.reference / "phase2-result.json").read_text("utf8"))

    def check_owner(self, report, source_hashes, expected_edits):
        completed = [r for r in report["completed"]
                     if r["id"] == "runningman-pass9"]
        self.assertEqual(len(completed), 1, report)
        self.assertEqual(completed[0]["order"], 37)
        self.assertEqual(completed[0]["rewrittenExpressions"], expected_edits)
        output = self.staged / "effective-preview" / TARGET
        self.assertTrue(output.is_file())
        generated = output.read_text("utf8")
        self.assertIn("m_gredRunningManResumeChecked", generated)
        self.assertIn('SetPermanentBoolParameter(n"RunningMan_ResumeSecondSprint"', generated)
        self.assertEqual(
            [hashlib.sha256(f.read_bytes()).hexdigest() for f in self.sources],
            source_hashes)
        self.assertFalse(list(self.staged.rglob("*.zip")))
        return output

    def test_generic_and_pass9_in_single_production_generate(self):
        source_hashes = self.source()
        stage = self.stage()
        owner = self.check_owner(stage, source_hashes, 2)
        reference = self.independent()
        self.assertEqual(reference["status"], "APPLIED")
        self.assertEqual(owner.read_bytes(), (self.reference / TARGET).read_bytes())
        generic = self.staged / "effective-preview/r6/scripts/Consumer/Example.reds"
        self.assertTrue(generic.is_file())
        self.assertIn("GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1",
                      generic.read_text("utf8"))

    def test_no_generic_eligible_still_applies_pass9(self):
        hashes = self.source(with_generic=False)
        stage = self.stage()
        self.check_owner(stage, hashes, 2)
        self.assertEqual(stage["generatedFiles"], 1)

    def test_partial_existing_perk_cache_is_completed_by_production(self):
        original_hashes = self.source(with_generic=False)
        self.independent()
        full = (self.reference / TARGET).read_text("utf8")
        partial = full.replace(
            "if this.m_gredRunningManDoubleRunPerk || this.m_isInSecondSprint {\n"
            "    this.EvaluateTwoStepSprint(stateContext, scriptInterface);\n  }",
            "this.EvaluateTwoStepSprint(stateContext, scriptInterface);"
        ).replace(
            "@addField(StandEvents)\nprivate let m_gredRunningManResumeChecked: Bool;\n\n", ""
        ).replace(
            "@wrapMethod(StandEvents)\n"
            "public func OnEnter(stateContext: ref<StateContext>, "
            "scriptInterface: ref<StateGameScriptInterface>) -> Void {\n"
            "  wrappedMethod(stateContext, scriptInterface);\n"
            "  this.m_gredRunningManResumeChecked = false;\n}\n\n", ""
        ).replace(
            "!this.m_gredRunningManResumeChecked && ", ""
        ).replace(
            "this.m_gredRunningManResumeChecked = true;", ""
        )
        self.assertNotEqual(partial, full)
        hashes = self.source(partial, with_generic=False)
        stage = self.stage()
        output = self.check_owner(stage, hashes, 2)
        self.assertEqual(output.read_text("utf8"), full)
        self.assertNotEqual(hashes, original_hashes)

    def test_fully_optimized_owner_no_duplicate_or_output(self):
        self.source(with_generic=False)
        self.independent()
        full = (self.reference / TARGET).read_text("utf8")
        hashes = self.source(full, with_generic=False)
        stage = self.stage()
        self.assertFalse(any(r["id"] == "runningman-pass9" for r in stage["completed"]))
        self.assertFalse((self.staged / "effective-preview" / TARGET).exists())
        self.assertEqual(
            [hashlib.sha256(f.read_bytes()).hexdigest() for f in self.sources],
            hashes)

    def test_narrow_incompatibility_does_not_block_other_operations(self):
        altered = SOURCE.replace("this.RemoveMaxSpeedModifier(stateContext, scriptInterface);",
                                 "this.CustomSpeedMode();")
        hashes = self.source(altered, with_generic=False)
        stage = self.stage()
        output = self.check_owner(stage, hashes, 1)
        self.assertIn("this.CustomSpeedMode();", output.read_text("utf8"))
        refusals = [r for r in stage["skipped"] if r["id"] == "runningman-pass9"]
        self.assertEqual(len(refusals), 1)
        self.assertEqual(refusals[0]["order"], 37)
        self.assertIn("RM_SPRINT_TRANSITION_OR_CALLBACK_UNPROVEN",
                      refusals[0]["reason"])


if __name__ == "__main__":
    unittest.main()
