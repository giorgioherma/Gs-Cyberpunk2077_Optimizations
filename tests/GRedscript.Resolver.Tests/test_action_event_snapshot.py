"""Generic native input-event snapshots: actual source edits, never listener migration.

One author OnAction invocation may decode ListenerAction.GetName/GetType
repeatedly. Emit a local reuse slice after earlier guards; keep consumption,
wrappedMethod and surrounding state code intact.
"""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = REPO / "src/GRedscript.Resolver/GRSP.Resolver.csproj"
MARKER = "GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1"

SOURCE = """module TestInput
public class SampleInput extends IScriptable {
    private let enabled: Bool;
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
        if !this.enabled { return false; }
        if Equals(ListenerAction.GetName(action), n"Jump") &&
           Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_PRESSED) {
            ListenerActionConsumer.Consume(consumer);
        }
        if Equals(ListenerAction.GetName(action), n"Cancel") &&
           Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) {
            return true;
        }
        if Equals(ListenerAction.GetName(action), n"Exit") { return true; }
        return false;
    }
}
"""


class ActionSnapshotContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.game = self.root / "game"
        self.source = self.game / "r6/scripts/Independent/Input.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(SOURCE, encoding="utf-8")
        self.scan_path = self.root / "scan.json"
        self.audit = self.root / "audit.json"
        self.analysis = self.root / "analysis.json"
        self.preview = self.root / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=90)

    def scan(self, output=None):
        path = output or self.scan_path
        run = self.cli("scan-action-snapshot", "--game", self.game,
                       "--out", path)
        return run, json.loads(path.read_text()) if run.returncode == 0 else None

    def seal(self):
        self.audit.unlink(missing_ok=True)
        run = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(run.returncode, 0, run.stderr + run.stdout)
        source_sha = json.loads(self.audit.read_text())["sourceStackSha256"]
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": source_sha}
        }))

    def stage(self, destination=None, base=None):
        args = ["preview-action-snapshot", "--game", self.game,
                "--analysis", self.analysis,
                "--out", destination or self.preview]
        if base is not None:
            args.extend(["--base-preview", base])
        return self.cli(*args)

    def test_real_rewrite_keeps_guards_consumption_and_original_source(self):
        original = self.source.read_bytes()
        status, scan = self.scan()
        self.assertEqual(status.returncode, 0, status.stderr + status.stdout)
        self.assertEqual(scan["eligible"], 3)  # three name + two type -> save 3
        self.assertEqual(scan["eligibleFiles"], 1)
        self.assertEqual(len(scan["candidates"]), 2)
        self.assertFalse(scan["productionZipAuthorized"])
        self.seal()
        status = self.stage()
        self.assertEqual(status.returncode, 0, status.stderr + status.stdout)
        self.assertIn("transformed=3", status.stdout)
        generated = (self.preview / "r6/scripts/Independent/Input.reds").read_text()
        self.assertEqual(generated.count(MARKER), 2)
        self.assertEqual(generated.count("ListenerAction.GetName(action)"), 1)
        self.assertEqual(generated.count("ListenerAction.GetType(action)"), 1)
        self.assertIn("if !this.enabled { return false; }", generated)
        self.assertLess(generated.index("if !this.enabled"),
                        generated.index(MARKER))
        self.assertIn("ListenerActionConsumer.Consume(consumer);", generated)
        self.assertEqual(self.source.read_bytes(), original)

    def test_idempotent_partial_and_complete(self):
        self.seal()
        self.assertEqual(self.stage().returncode, 0)
        text = (self.preview / "r6/scripts/Independent/Input.reds").read_text()
        self.source.write_text(text)
        status, scan = self.scan(output=self.root / "complete.json")
        self.assertEqual(status.returncode, 0, status.stderr + status.stdout)
        self.assertEqual(scan["eligible"], 0)
        self.assertEqual(scan["alreadyComplete"], 2)
        self.seal()
        noop = self.stage(destination=self.root / "no-work")
        self.assertEqual(noop.returncode, 0, noop.stderr + noop.stdout)
        self.assertIn("transformed=0", noop.stdout)
        self.assertFalse((self.root / "no-work").exists())

    def test_source_drift_and_corrupted_snapshot_refuse(self):
        self.seal()
        self.source.write_text(SOURCE + "\n// changed after capture\n")
        status = self.stage()
        self.assertNotEqual(status.returncode, 0)
        self.assertIn("ACTION_SNAPSHOT_CAPTURE_OR_SOURCE_DRIFT",
                      status.stderr + status.stdout)
        self.assertFalse(self.preview.exists())

        self.source.write_text(SOURCE)
        self.seal()
        self.assertEqual(self.stage().returncode, 0)
        generated = (self.preview / "r6/scripts/Independent/Input.reds").read_text()
        self.source.write_text(generated.replace(
            "ListenerAction.GetName(action);",
            "ListenerAction.GetType(action);", 1))
        status, _ = self.scan(output=self.root / "tampered.json")
        self.assertNotEqual(status.returncode, 0)
        self.assertIn("ACTION_SNAPSHOT_PARTIAL_OR_DRIFTED",
                      status.stderr + status.stdout)

    def test_prior_generic_composition_with_shifted_offsets(self):
        self.seal()
        earlier = self.root / "earlier"
        target = earlier / "r6/scripts/Independent/Input.reds"
        target.parent.mkdir(parents=True)
        target.write_text(SOURCE.replace(
            "        if !this.enabled",
            "        // GCO-GENERIC:BLACKBOARD_DEFINITION_SEQUENCE_V1\n"
            "        if !this.enabled"))
        status = self.stage(base=earlier)
        self.assertEqual(status.returncode, 0, status.stderr + status.stdout)
        generated = (self.preview / "r6/scripts/Independent/Input.reds").read_text()
        self.assertIn("BLACKBOARD_DEFINITION_SEQUENCE_V1", generated)
        self.assertEqual(generated.count(MARKER), 2)

        other = self.root / "wrong"
        target = other / "r6/scripts/Independent/Input.reds"
        target.parent.mkdir(parents=True)
        target.write_text(SOURCE.replace(
            "ListenerAction.GetName(action)", "DifferentGetter(action)", 1))
        status = self.stage(destination=self.root / "refused", base=other)
        self.assertNotEqual(status.returncode, 0)
        self.assertIn("ACTION_SNAPSHOT_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT",
                      status.stderr + status.stdout)

    def test_action_mutation_and_nested_first_reader_refuse(self):
        self.source.write_text(SOURCE.replace(
            "        if !this.enabled { return false; }",
            "        if !this.enabled { return false; }\n"
            "        action = action;"))
        status, scan = self.scan()
        self.assertEqual(status.returncode, 0, status.stderr)
        self.assertEqual(scan["eligible"], 0)

        self.source.write_text(SOURCE.replace(
            "        if Equals(ListenerAction.GetName(action), n\"Jump\") {",
            "        if this.enabled {\n"
            "          if Equals(ListenerAction.GetName(action), n\"Jump\") {").replace(
            "        if Equals(ListenerAction.GetName(action), n\"Cancel\")",
            "        }\n        if Equals(ListenerAction.GetName(action), n\"Cancel\")"))
        status, scan = self.scan(output=self.root / "nested.json")
        self.assertEqual(status.returncode, 0, status.stderr)
        # First name read is inside a conditional branch: never hoist it.
        self.assertEqual(scan["eligible"], 0)  # neither first read is at root level

    def test_do_not_modify_external_game_or_existing_output(self):
        self.seal()
        forbidden = self.game / "r6/scripts/output"
        status = self.stage(destination=forbidden)
        self.assertNotEqual(status.returncode, 0)
        self.assertIn("ACTION_SNAPSHOT_PREVIEW_OUTPUT_NOT_ISOLATED",
                      status.stdout + status.stderr)
        self.preview.mkdir()
        sentinel = self.preview / "KEEP.txt"
        sentinel.write_text("keep")
        status = self.stage()
        self.assertNotEqual(status.returncode, 0)
        self.assertEqual(sentinel.read_text(), "keep")


if __name__ == "__main__":
    unittest.main()
