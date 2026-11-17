"""Proof contracts for source-only late system read after an action guard."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCE = """module AnyMod

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let value = wrappedMethod(action, consumer);

    let questHandle = GameInstance.GetQuestsSystem(this.GetGame());
    if !IsDefined(questHandle) { return value; }
    if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) { return value; }

    let actionName = ListenerAction.GetName(action);
    if Equals(actionName, n"any_mod_action") {
        ListenerActionConsumer.Consume(consumer);
        questHandle.SetFact(n"any_action_count", 1);
    }
    return value;
}
"""


class LateSystemReadContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.game = root / "game"
        self.source = self.game / "r6/scripts/AnyMod/Test.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(SOURCE, encoding="utf8")
        self.audit = root / "audit.json"
        self.analysis = root / "analysis.json"
        self.scan = root / "scan.json"
        self.preview = root / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=90)

    def scan_source(self):
        r = self.cli("scan-late-system-read", "--game", self.game,
                     "--out", self.scan)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(self.scan.read_text(encoding="utf8"))

    def seal(self):
        if self.audit.exists():
            self.audit.unlink()
        r = self.cli("source-audit", "--game", self.game,
                     "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stderr)
        sha = json.loads(self.audit.read_text(encoding="utf8"))["sourceStackSha256"]
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": sha}
        }), encoding="utf8")

    def preview_source(self, out=None):
        return self.cli("preview-late-system-read", "--game", self.game,
                        "--analysis", self.analysis,
                        "--out", out if out is not None else self.preview)

    def test_pristine_then_completed_then_second_pass_zero(self):
        original = self.source.read_bytes()
        before = self.scan_source()
        self.assertEqual(before["eligible"], 1)
        self.assertEqual(before["alreadyComplete"], 0)
        self.assertEqual(before["sourceFiles"], 1)
        self.assertFalse(before["generationAuthorized"])
        self.assertFalse(before["sourceWrites"])
        self.assertFalse(before["measuredSavingsClaimed"])
        self.seal()
        r = self.preview_source()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        self.assertIn("transformed=1", r.stdout)
        result = self.preview / "r6/scripts/AnyMod/Test.reds"
        self.assertTrue(result.is_file())
        text = result.read_text(encoding="utf8")
        self.assertIn("GCO-GENERIC:SYSTEM_READ_AFTER_ACTION_GUARD_V1", text)
        self.assertEqual(text.count("wrappedMethod(action, consumer)"), 1)
        self.assertEqual(text.count("GameInstance.GetQuestsSystem("), 1)
        self.assertEqual(text.count("ListenerAction.GetType(action)"), 1)
        self.assertLess(text.index("ListenerAction.GetType(action)"),
                        text.index("GameInstance.GetQuestsSystem("))
        self.assertLess(text.index("wrappedMethod(action, consumer)"),
                        text.index("ListenerAction.GetType(action)"))
        self.assertEqual(self.source.read_bytes(), original)
        shutil.copyfile(result, self.source)
        self.scan.unlink()
        completed = self.scan_source()
        self.assertEqual(completed["eligible"], 0)
        self.assertEqual(completed["alreadyComplete"], 1)
        shutil.rmtree(self.preview)
        self.seal()
        r = self.preview_source()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        self.assertIn("transformed=0", r.stdout)
        self.assertFalse(self.preview.exists())

    def test_nonterminal_action_guard_refused(self):
        self.source.write_text(SOURCE.replace(
            "if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) { return value; }",
            "if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_RELEASED) { value = false; }"
        ), encoding="utf8")
        self.assertEqual(self.scan_source()["eligible"], 0)

    def test_action_qualifier_refused(self):
        self.source.write_text(SOURCE.replace(
            "gameinputActionType.BUTTON_RELEASED", "gameinputActionType.BUTTON_PRESSED"
        ), encoding="utf8")
        self.assertEqual(self.scan_source()["eligible"], 0)

    def test_intervening_comments_refused(self):
        self.source.write_text(SOURCE.replace(
            "if !IsDefined(questHandle) { return value; }",
            "// intentional author instrumentation\n    if !IsDefined(questHandle) { return value; }"
        ), encoding="utf8")
        self.assertEqual(self.scan_source()["eligible"], 0)

    def test_missing_capture_source_drift_and_game_path_refused(self):
        r = self.preview_source()
        self.assertNotEqual(r.returncode, 0)
        self.seal()
        self.source.write_text(SOURCE + "// source changed\n", encoding="utf8")
        r = self.preview_source()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("LATE_SYSTEM_READ_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      r.stderr + r.stdout)
        self.seal()
        r = self.preview_source(self.game / "r6/scripts/generated")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("LATE_SYSTEM_READ_PREVIEW_OUTPUT_NOT_ISOLATED",
                      r.stderr + r.stdout)

    def test_generated_marker_tamper_fails_closed(self):
        self.seal()
        self.assertEqual(self.preview_source().returncode, 0)
        generated = self.preview / "r6/scripts/AnyMod/Test.reds"
        self.source.write_text(generated.read_text(encoding="utf8").replace(
            "if !IsDefined(questHandle) { return value; }",
            "if !IsDefined(questHandle) { return false; }"), encoding="utf8")
        r = self.cli("scan-late-system-read", "--game", self.game,
                     "--out", self.scan)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("LATE_SYSTEM_READ_PARTIAL_OR_DRIFTED_GENERATED_MARKER",
                      r.stderr + r.stdout)


if __name__ == "__main__":
    unittest.main()
