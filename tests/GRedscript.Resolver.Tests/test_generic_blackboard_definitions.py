"""Blackboard immutable-definition reuse: local source proof, idempotence and drift."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCE = """module IndependentAuthor

public class Example extends ScriptableSystem {
  public func Test(player: ref<PlayerPuppet>, gi: GameInstance) -> Bool {
    let frame = 1;
    if GameInstance.GetBlackboardSystem(gi).GetLocalInstanced(player.GetEntityID(), GetAllBlackboardDefs().PlayerStateMachine).GetInt(GetAllBlackboardDefs().PlayerStateMachine.SceneTier) > 1 {
        return false;
    };
    GameInstance.GetBlackboardSystem(gi).Get(GetAllBlackboardDefs().UI_Notifications).SetVariant(GetAllBlackboardDefs().UI_Notifications.WarningMessage, ToVariant(frame), true);
    return true;
  }
}
"""


class ImmutableBlackboardDefinitionContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        base = Path(temp.name)
        self.game = base / "game"
        self.source = self.game / "r6/scripts/IndependentAuthor/Example.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(SOURCE, encoding="utf8")
        self.audit = base / "audit.json"
        self.scan = base / "scan.json"
        self.analysis = base / "capture.json"
        self.preview = base / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=90)

    def run_scan(self):
        if self.scan.exists():
            self.scan.unlink()
        r = self.cli("scan-blackboard-defs", "--game", self.game,
                     "--out", self.scan)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(self.scan.read_text(encoding="utf8"))

    def seal(self):
        if self.audit.exists():
            self.audit.unlink()
        r = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        h = json.loads(self.audit.read_text(encoding="utf8"))["sourceStackSha256"]
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": h},
        }), encoding="utf8")

    def run_preview(self, out=None):
        return self.cli("preview-blackboard-defs", "--game", self.game,
                        "--analysis", self.analysis,
                        "--out", out if out is not None else self.preview)

    def test_two_independent_definition_groups_with_live_values(self):
        original = self.source.read_bytes()
        scan = self.run_scan()
        self.assertEqual(scan["sourceFiles"], 1)
        self.assertEqual(scan["eligible"], 2)
        self.assertEqual(scan["alreadyComplete"], 0)
        self.assertEqual({x["group"] for x in scan["candidates"]},
                         {"PlayerStateMachine", "UI_Notifications"})
        self.assertFalse(scan["generationAuthorized"])
        self.assertFalse(scan["sourceWrites"])
        self.assertFalse(scan["measuredSavingsClaimed"])
        self.seal()
        r = self.run_preview()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        self.assertIn("transformed=2", r.stdout)
        generated = self.preview / "r6/scripts/IndependentAuthor/Example.reds"
        text = generated.read_text(encoding="utf8")
        self.assertEqual(text.count("GCO-GENERIC:BLACKBOARD_DEFINITION_STATEMENT_V1"), 2)
        self.assertEqual(text.count("GetAllBlackboardDefs().PlayerStateMachine"), 1)
        self.assertEqual(text.count("GetAllBlackboardDefs().UI_Notifications"), 1)
        self.assertIn("GetInt(__gcoBbDefL", text)
        self.assertIn("SetVariant(__gcoBbDefL", text)
        self.assertIn("WarningMessage", text)
        self.assertIn("SceneTier", text)
        self.assertEqual(self.source.read_bytes(), original)

        # Installed-source state simulation: the output is now fully optimized.
        shutil.copyfile(generated, self.source)
        completed = self.run_scan()
        self.assertEqual(completed["eligible"], 0)
        self.assertEqual(completed["alreadyComplete"], 2)
        shutil.rmtree(self.preview)
        self.seal()
        r = self.run_preview()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        self.assertIn("transformed=0", r.stdout)
        self.assertFalse(self.preview.exists())

    def test_partially_optimized_source_completes_missing_only(self):
        self.seal()
        self.assertEqual(self.run_preview().returncode, 0)
        generated = (self.preview /
                     "r6/scripts/IndependentAuthor/Example.reds").read_text(encoding="utf8")
        original_lines = SOURCE.splitlines(keepends=True)
        generated_lines = generated.splitlines(keepends=True)
        # Keep first completed definition, restore second original statement.
        first = "group=PlayerStateMachine"
        stop = "group=UI_Notifications"
        g = generated_lines[:next(i for i, line in enumerate(generated_lines) if stop in line)]
        rest = [x for x in original_lines if "GetAllBlackboardDefs().UI_Notifications" in x]
        self.assertEqual(len(rest), 1)
        g += rest
        g += ["    return true;\n", "  }\n", "}\n"]
        self.source.write_text("".join(g), encoding="utf8")
        scan = self.run_scan()
        self.assertEqual(scan["eligible"], 1)
        self.assertEqual(scan["alreadyComplete"], 1)

    def test_single_lookup_and_short_circuit_not_admitted(self):
        self.source.write_text(SOURCE.replace(
            "GetAllBlackboardDefs().PlayerStateMachine.SceneTier",
            "GetAllBlackboardDefs().UI_Notifications.SceneTier"
        ).replace(
            "GetAllBlackboardDefs().UI_Notifications.WarningMessage",
            "GetAllBlackboardDefs().UIGameData.WarningMessage"
        ), encoding="utf8")
        self.assertEqual(self.run_scan()["eligible"], 0)
        self.source.write_text(SOURCE.replace(
            "GetAllBlackboardDefs().PlayerStateMachine.SceneTier) > 1",
            "GetAllBlackboardDefs().PlayerStateMachine.SceneTier) > 1 || true"
        ), encoding="utf8")
        self.assertEqual(self.run_scan()["eligible"], 1)

    def test_comment_or_string_rejected(self):
        self.source.write_text(SOURCE.replace(
            "PlayerStateMachine.SceneTier) > 1 {",
            "PlayerStateMachine.SceneTier) > 1 { // author side effect order"
        ), encoding="utf8")
        self.assertEqual(self.run_scan()["eligible"], 1)
        self.source.write_text(SOURCE.replace(
            "ToVariant(frame)", 'ToVariant(n"shown")'
        ), encoding="utf8")
        self.assertEqual(self.run_scan()["eligible"], 1)

    def test_modified_marker_or_generated_body_refused(self):
        self.seal()
        self.assertEqual(self.run_preview().returncode, 0)
        content = (self.preview /
                   "r6/scripts/IndependentAuthor/Example.reds").read_text(encoding="utf8")
        self.source.write_text(content.replace(
            "GCO-GENERIC:BLACKBOARD_DEFINITION_STATEMENT_V1",
            "GCO-GENERIC:BLACKBOARD_DEFINITION_STATEMENT_V1-invalid", 1
        ), encoding="utf8")
        r = self.cli("scan-blackboard-defs", "--game", self.game, "--out", self.scan)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("BBDEFS_PARTIAL_OR_DRIFTED_MARKER",
                      r.stderr + r.stdout)

    def test_capture_drift_and_output_isolation(self):
        r = self.run_preview()
        self.assertNotEqual(r.returncode, 0)
        self.seal()
        self.source.write_text(SOURCE + "//source drift\n", encoding="utf8")
        r = self.run_preview()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("BBDEFS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      r.stderr + r.stdout)
        self.seal()
        r = self.run_preview(self.game / "r6/scripts/generated")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("BBDEFS_PREVIEW_OUTPUT_NOT_ISOLATED",
                      r.stderr + r.stdout)


if __name__ == "__main__":
    unittest.main()
