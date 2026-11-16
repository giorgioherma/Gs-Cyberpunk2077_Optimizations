"""Conservative CET-inspired REDscript callback family and fixed CName index contracts."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src" / "GRedscript.Resolver" / "bin" / "Release" / "net10.0" / "GRedscript.Resolver.dll"

# Generic shape: all identifiers/owner names are fixtures, never generator rules.
EXAMPLE = """module Example

public abstract class ExampleActions {
  public static func InterestedActions() -> array<CName> {
    let names: array<CName> = [n"First_Action", n"Second_Action", n"Third_Action"];
    return names;
  }

  private static func IndexFor(name: CName) -> Int32 {
    let names = ExampleActions.InterestedActions();
    let i = 0;
    while i < ArraySize(names) {
      if Equals(names[i], name) { return i; }
      i += 1;
    }
    return -1;
  }

  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    let name = ListenerAction.GetName(action);
    if Equals(name, n"First_Action") { return true; }
    return wrappedMethod(action, consumer);
  }
}
"""


class GenericCallbackContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build",
                            str(ROOT / "src" / "GRedscript.Resolver" / "GRSP.Resolver.csproj"),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.game = self.root / "game"
        self.source = self.game / "r6" / "scripts" / "Fixture" / "Actions.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(EXAMPLE, encoding="utf-8")
        self.report = self.root / "callback-scan.json"
        self.audit = self.root / "source-audit.json"
        self.analysis = self.root / "analysis.json"
        self.preview = self.root / "preview"

    def cli(self, command, *args):
        return subprocess.run(["dotnet", str(DLL), command, *map(str, args)],
                              capture_output=True, text=True, timeout=90)

    def make_analysis(self):
        result = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(result.returncode, 0, result.stderr)
        payload = json.loads(self.audit.read_text(encoding="utf-8"))
        # Deterministic synthetic test input; the production command requires
        # sealed source/provenance from GCO's existing analyze stage.
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": payload["sourceStackSha256"]},
        }), encoding="utf-8")

    def test_callback_inventory_and_exact_index_preview(self):
        original = self.source.read_bytes()
        scan = self.cli("scan-callbacks", "--game", self.game, "--out", self.report)
        self.assertEqual(scan.returncode, 0, scan.stderr)
        data = json.loads(self.report.read_text(encoding="utf-8"))
        self.assertEqual(data["schemaVersion"], "gco.redscript.callback.scan.v1")
        self.assertEqual(data["sourceFiles"], 1)
        self.assertGreaterEqual(data["callbackMethods"], 1)
        self.assertEqual(data["fixedCnameIndexEligible"], 1)
        self.assertEqual(data["fixedCnameIndexAlreadyComplete"], 0)
        self.assertFalse(data["generationAuthorized"])
        self.assertFalse(data["compilerValidated"])
        self.assertTrue(any(x["family"] == "ONACTION" for x in data["callbacks"]))
        self.assertEqual(original, self.source.read_bytes())

        self.make_analysis()
        result = self.cli("preview-callback-index", "--game", self.game,
                          "--analysis", self.analysis, "--out", self.preview)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("transformed=1", result.stdout)
        transformed = self.preview / "r6" / "scripts" / "Fixture" / "Actions.reds"
        rewritten = transformed.read_text(encoding="utf-8")
        self.assertIn("// GCO-GENERIC:CNAME_FIXED_INDEX_V1:InterestedActions", rewritten)
        self.assertIn('if Equals(n"First_Action", name) { return 0; }', rewritten)
        self.assertIn('if Equals(n"Second_Action", name) { return 1; }', rewritten)
        self.assertIn('if Equals(n"Third_Action", name) { return 2; }', rewritten)
        self.assertIn("return wrappedMethod(action, consumer);", rewritten)
        self.assertIn("let names: array<CName> =", rewritten)  # original helper preserved
        self.assertNotIn("while i < ArraySize(names)", rewritten)
        self.assertEqual(original, self.source.read_bytes())  # never write game

        # Explicitly simulate subsequent user-controlled installation in fixture.
        shutil.copyfile(transformed, self.source)
        rescan = self.root / "rescan.json"
        result = self.cli("scan-callbacks", "--game", self.game, "--out", rescan)
        self.assertEqual(result.returncode, 0, result.stderr)
        second = json.loads(rescan.read_text(encoding="utf-8"))
        self.assertEqual(second["fixedCnameIndexEligible"], 0)
        self.assertEqual(second["fixedCnameIndexAlreadyComplete"], 1)

    def test_mutated_generated_method_refuses_partial_state(self):
        self.make_analysis()
        result = self.cli("preview-callback-index", "--game", self.game,
                          "--analysis", self.analysis, "--out", self.preview)
        self.assertEqual(result.returncode, 0, result.stderr)
        generated = (self.preview / "r6" / "scripts" / "Fixture" / "Actions.reds").read_text()
        self.source.write_text(generated.replace("return 1;", "return 7;"), encoding="utf-8")
        result = self.cli("scan-callbacks", "--game", self.game,
                          "--out", self.report)
        self.assertEqual(result.returncode, 0, result.stderr)
        report = json.loads(self.report.read_text())
        self.assertEqual(report["fixedCnameIndexEligible"], 0)
        self.assertEqual(report["fixedCnameIndexRejected"], 1)
        self.assertEqual(report["fixedIndexCandidates"][0]["state"], "UNPROVEN_OR_DRIFTED")

    def test_source_proof_refuses_modified_loop_and_dynamic_provider(self):
        for mutated in [
            EXAMPLE.replace("i += 1;", "i += 2;"),
            EXAMPLE.replace('let names: array<CName> = [n"First_Action", n"Second_Action", n"Third_Action"];',
                            'let names: array<CName> = SomeSystem.GetDynamicActions();'),
            EXAMPLE.replace("return -1;", "return 0;"),
        ]:
            with self.subTest(mutated=mutated[-90:]):
                self.source.write_text(mutated, encoding="utf-8")
                # Always a fresh output; scanner refuses existing outputs.
                result = self.cli("scan-callbacks", "--game", self.game,
                                  "--out", self.report)
                self.assertEqual(result.returncode, 0, result.stderr)
                data = json.loads(self.report.read_text())
                self.assertEqual(data["fixedCnameIndexEligible"], 0)
                self.report.unlink()

    def test_preview_refuses_stale_or_unverified_analysis(self):
        self.make_analysis()
        raw = json.loads(self.analysis.read_text())
        raw["source"]["sourceStackSha256"] = "0" * 64
        self.analysis.write_text(json.dumps(raw))
        result = self.cli("preview-callback-index", "--game", self.game,
                          "--analysis", self.analysis, "--out", self.preview)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.preview.exists())
        raw["source"]["sourceStackSha256"] = json.loads(self.audit.read_text())["sourceStackSha256"]
        raw["capture"]["collectionProvenance"] = "UNVERIFIED"
        self.analysis.write_text(json.dumps(raw))
        result = self.cli("preview-callback-index", "--game", self.game,
                          "--analysis", self.analysis, "--out", self.preview)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.preview.exists())

    def test_scanner_refuses_game_write(self):
        result = self.cli("scan-callbacks", "--game", self.game,
                          "--out", self.game / "r6" / "callbacks.json")
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse((self.game / "r6" / "callbacks.json").exists())


if __name__ == "__main__":
    unittest.main()
