"""Shared-work source + verified F11 evidence, refusal and no-output-mutation tests."""
from pathlib import Path
import hashlib
import json
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"
SOURCE = """module IndependentAuthor

public class TestRefreshController extends IScriptable {
    public func Update(timeLeft: Float) -> Void {
        this.m_lastProgress = timeLeft;
        this.m_widget.SetTimeRemaining(timeLeft);
    }

    public func Refresh() -> Void {
        this.m_widget.SetText("unchanged?");
    }

    public func Tick() -> Bool {
        return this.m_widget.IsVisible();
    }

    public func NotRelated() -> Void {
        let x = 3;
    }
}
"""


class SharedWorkClassificationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.game = self.root / "game"
        self.original = self.game / "r6/scripts/IndependentAuthor/Source.reds"
        self.original.parent.mkdir(parents=True)
        self.original.write_text(SOURCE, encoding="utf8")
        self.report = self.root / "shared-work.json"
        self.audit = self.root / "audit.json"
        self.analysis = self.root / "analysis.json"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=90)

    def scan(self, analysis=None, out=None):
        args = ("scan-shared-work", "--game", self.game,
                "--out", out or self.report)
        if analysis is not None:
            args += ("--analysis", analysis)
        return self.cli(*args)

    def source_hash(self):
        r = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(self.audit.read_text(encoding="utf8"))["sourceStackSha256"]

    def seal_real_shape(self, source_hash=None, file_sha=None, line=9,
                        status="LINE_METHOD_NAME_MATCH_RECEIVER_UNVERIFIED"):
        sha = source_hash or self.source_hash()
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {"sourceStackSha256": sha},
            "rawTables": {"Developer/RSP_CallSites.csv": {
                "rows": [{
                    "calls": "251",
                    "exclusive_instrumented_ms": "11.50"
                }]
            }},
            "sourceObservations": [{
                "capturedPath": "r6/scripts/IndependentAuthor/Source.reds",
                "sourceFunction": "TestRefreshController::Refresh",
                "sourceLine": line,
                "evidence": {
                    "status": status,
                    "sha256": file_sha or hashlib.sha256(
                        self.original.read_bytes()).hexdigest().upper()
                }
            }]
        }), encoding="utf8")

    def test_every_current_refresh_family_is_examined_without_rewrites(self):
        before = self.original.read_bytes()
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        report = json.loads(self.report.read_text(encoding="utf8"))
        self.assertEqual(report["schemaVersion"], "gco.redscript.shared-work.scan.v1")
        self.assertEqual(report["sourceFiles"], 1)
        self.assertEqual(report["candidateMethods"], 3)
        self.assertEqual(report["sourceMatchedMeasured"], 0)
        self.assertEqual(report["unmeasured"], 3)
        self.assertEqual({c["source"]["method"] for c in report["candidates"]},
                         {"Update", "Refresh", "Tick"})
        self.assertTrue(all(
            c["firstFailedPrerequisite"] == "SOURCE_MATCHED_MEASUREMENT_MISSING"
            for c in report["candidates"]))
        self.assertTrue(all(not c["safeToSkipBody"] for c in report["candidates"]))
        self.assertFalse(report["sourceWrites"])
        self.assertFalse(report["generationAuthorized"])
        self.assertFalse(report["compilerValidated"])
        self.assertIsNone(report["candidates"][0]["callbackEntries"])
        self.assertIsNone(report["candidates"][0]["expectedSavingsMs"])
        self.assertEqual(self.original.read_bytes(), before)

    def test_verified_capture_joins_only_exact_source_method_and_ranks_it(self):
        self.seal_real_shape(line=9)
        r = self.scan(self.analysis)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.report.read_text(encoding="utf8"))
        self.assertEqual(j["sourceMatchedMeasured"], 1)
        self.assertEqual(j["unmeasured"], 2)
        self.assertEqual(j["sourceMatchedOutgoingEdges"], 251)
        self.assertEqual(j["sourceMatchedDirectInstrumentedMs"], 11.5)
        chosen = j["candidates"][0]
        self.assertEqual(chosen["source"]["method"], "Refresh")
        self.assertEqual(chosen["measuredOutgoingEdges"], 251)
        self.assertEqual(chosen["directInstrumentedMs"], 11.5)
        self.assertEqual(chosen["firstFailedPrerequisite"],
                         "UI_WIDGET_VALUE_OR_LIFETIME_UNPROVEN")
        self.assertTrue(chosen["sourceHints"]["widgetEffects"])
        self.assertFalse(chosen["completeWriterSetProven"])
        self.assertFalse(chosen["safeToSkipBody"])

    def test_capture_source_drift_refused(self):
        self.seal_real_shape(line=9)
        before = self.analysis.read_bytes()
        self.original.write_text(SOURCE + "\n// newer live mod version\n", encoding="utf8")
        r = self.scan(self.analysis)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("CALLBACK_SEMANTICS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      r.stderr + r.stdout)
        self.assertFalse(self.report.exists())
        self.assertEqual(self.analysis.read_bytes(), before)

    def test_invalid_source_mapping_cannot_inflate_measured_work(self):
        self.seal_real_shape(line=9, status="UNRESOLVED_WRAPPER")
        r = self.scan(self.analysis)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.report.read_text(encoding="utf8"))
        self.assertEqual(j["sourceMatchedMeasured"], 0)
        self.assertEqual(j["sourceMatchedDirectInstrumentedMs"], 0)
        self.assertTrue(all(c["directInstrumentedMs"] is None
                            for c in j["candidates"]))

    def test_all_methods_get_overlap_counts_and_unproven_triggers(self):
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.report.read_text(encoding="utf8"))
        self.assertTrue(j["sourceDependencyCategoriesAreLexicalObservations"])
        self.assertTrue(j["sharedTriggersAreHypothesesNotPublishers"])
        self.assertTrue(j["directInstanceFieldsAreNotCompleteWriterGraph"])
        self.assertEqual(j["directDependencyCategoryCounts"]["INSTANCE_FIELDS"], 3)
        self.assertEqual(j["directDependencyCategoryCounts"]["CALLBACK_ARGUMENTS"], 1)
        self.assertEqual(j["directDependencyCategoryCounts"]["UI_WIDGET"], 2)
        self.assertEqual(sum(j["primaryCandidateSharedTriggerCounts"].values()), 3)
        rows = {x["source"]["method"]: x for x in j["candidates"]}
        update = rows["Update"]["directDependencies"]
        self.assertEqual(update["usedCallbackParameters"], ["timeLeft"])
        self.assertEqual(update["instanceFields"], ["m_lastProgress", "m_widget"])
        self.assertIn("CALLBACK_ARGUMENT_FRESHNESS", update["possibleSharedTriggers"])
        self.assertIn("TIMER_ANIMATION_FRAME", update["possibleSharedTriggers"])
        self.assertFalse(update["completeWriterGraphProven"])
        self.assertFalse(update["sharedTriggerProven"])
        self.assertFalse(rows["Tick"]["safeToSkipBody"])

    def test_instance_method_call_is_not_misreported_as_field_read(self):
        self.original.write_text(SOURCE.replace(
            'this.m_widget.SetText("unchanged?");',
            'this.RunDiagnostics();\n        this.m_widget.SetText("unchanged?");'
        ), encoding="utf8")
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.report.read_text(encoding="utf8"))
        row = next(x for x in j["candidates"] if x["source"]["method"] == "Refresh")
        deps = row["directDependencies"]
        self.assertIn("RunDiagnostics", deps["instanceMethodCalls"])
        self.assertNotIn("RunDiagnostics", deps["instanceFields"])
        self.assertIn("m_widget", deps["instanceFields"])
        self.assertIn("TRANSITIVE_METHOD_EFFECTS_NOT_RESOLVED",
                      deps["possibleSharedTriggers"])
        self.assertFalse(row["safeToSkipBody"])

    def test_player_identity_does_not_authorize_spatial_or_event_skip(self):
        self.original.write_text(SOURCE.replace(
            'this.m_widget.SetText("unchanged?");',
            'let player = GameInstance.GetPlayer(this.GetGame());\n'
            '        let pos = player.GetWorldPosition();\n'
            '        this.QueueEvent(new SomeEvent());\n'
            '        this.m_widget.SetText("unchanged?");'
        ), encoding="utf8")
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.report.read_text(encoding="utf8"))
        row = next(x for x in j["candidates"] if x["source"]["method"] == "Refresh")
        hints = row["directDependencies"]
        for expected in ("PLAYER_IDENTITY", "WORLD_SPATIAL", "EVENT_LIFECYCLE",
                         "UI_WIDGET", "INSTANCE_METHOD_CALLS"):
            self.assertIn(expected, hints["observedCategoryHints"])
        self.assertIn("PLAYER_ATTACH_DETACH_IDENTITY_ONLY",
                      hints["possibleSharedTriggers"])
        self.assertIn("POSITION_NEARFAR_NEEDS_EXACT_PROOF",
                      hints["possibleSharedTriggers"])
        self.assertFalse(hints["sharedTriggerProven"])
        self.assertFalse(row["generationAuthorized"])

    def test_output_isolation_and_no_overwrites(self):
        r = self.scan(out=self.game / "r6/scripts/injected.json")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("SHARED_WORK_OUTPUT_NOT_ISOLATED", r.stderr + r.stdout)
        self.report.write_text("existing report", encoding="utf8")
        r = self.scan()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("SHARED_WORK_OUTPUT_NOT_ISOLATED", r.stderr + r.stdout)
        self.assertEqual(self.report.read_text(), "existing report")


if __name__ == "__main__":
    unittest.main()
