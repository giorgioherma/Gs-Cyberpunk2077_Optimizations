"""G-PureConstantSlice: a source-proven pure Bool getter, never whole-method skip.

Real generated .reds must retain callers' live expressions, order, conditional
branches, and side effects. Source and compilation gates are independent.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"
MARKER = "GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1"

SETTINGS = """module TestReusableSettings

public class ReusableSettings {
    public final static func Enabled() -> Bool { return true; }
    public final static func Diagnostics() -> Bool { return false; }
}
"""

CONSUMER = """module GenericConsumer
import TestReusableSettings.*

public class IndependentAuthor extends IScriptable {
    public func OnEvent(gi: GameInstance) -> Bool {
        return ReusableSettings.Enabled() &&
            ReusableSettings.Diagnostics() &&
            GameInstance.GetQuestsSystem(gi).GetFact(n"test_quest") == 1;
    }
}
"""


class PureConstantSourceContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.game = self.root / "game"
        self.defs = self.game / "r6/scripts/A/Reusable.reds"
        self.consumer = self.game / "r6/scripts/B/Consumer.reds"
        self.defs.parent.mkdir(parents=True)
        self.consumer.parent.mkdir(parents=True)
        self.defs.write_text(SETTINGS, encoding="utf8")
        self.consumer.write_text(CONSUMER, encoding="utf8")
        self.scanfile = self.root / "scan.json"
        self.audit = self.root / "audit.json"
        self.analysis = self.root / "analysis.json"
        self.preview = self.root / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=90)

    def scan(self, output=None):
        out = output or self.scanfile
        r = self.cli("scan-pure-constants", "--game", self.game,
                     "--out", out)
        return r, (json.loads(out.read_text()) if r.returncode == 0 else None)

    def seal(self):
        if self.audit.exists():
            self.audit.unlink()
        r = self.cli("source-audit", "--game", self.game,
                     "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        h = json.loads(self.audit.read_text())["sourceStackSha256"]
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {"sourceStackSha256": h}
        }))

    def preview_stage(self, output=None, base=None):
        args = ["preview-pure-constants", "--game", self.game,
                "--analysis", self.analysis, "--out", output or self.preview]
        if base is not None:
            args.extend(["--base-preview", base])
        return self.cli(*args)

    def test_generates_source_only_for_pure_final_bool(self):
        original = self.consumer.read_bytes()
        result, data = self.scan()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(data["schemaVersion"],
                         "gco.redscript.pure-final-bool.scan.v1")
        self.assertEqual(data["sourceFiles"], 2)
        self.assertEqual(data["eligibleSites"], 2)
        self.assertEqual(data["eligibleFiles"], 1)
        self.assertEqual(data["alreadyComplete"], 0)
        self.assertFalse(data["generationAuthorized"])
        self.assertFalse(data["sourceWrites"])
        self.assertTrue(data["callerExecutionAndLiveStateUnchanged"])
        self.assertTrue(data["sourceCallSideEffectsProvenAbsent"])
        self.seal()
        emitted = self.preview_stage()
        self.assertEqual(emitted.returncode, 0, emitted.stdout + emitted.stderr)
        self.assertIn("transformed=2", emitted.stdout)
        target = self.preview / "r6/scripts/B/Consumer.reds"
        self.assertTrue(target.exists())
        generated = target.read_text()
        self.assertEqual(generated.count(MARKER), 2)
        self.assertIn("true /* " + MARKER, generated)
        self.assertIn("false /* " + MARKER, generated)
        self.assertIn('GameInstance.GetQuestsSystem(gi).GetFact(n"test_quest")', generated)
        self.assertEqual(self.consumer.read_bytes(), original)
        self.assertFalse((self.preview / "r6/scripts/A/Reusable.reds").exists())

    def test_partial_and_fully_generated_sources_are_idempotent(self):
        self.seal()
        self.assertEqual(self.preview_stage().returncode, 0)
        full = (self.preview / "r6/scripts/B/Consumer.reds").read_text()
        # One literal was generated in an earlier compatible pass.
        partial = full.replace(
            "false /* " + MARKER +
            " type=ReusableSettings method=Diagnostics value=false */",
            "ReusableSettings.Diagnostics()")
        self.consumer.write_text(partial)
        r, j = self.scan(output=self.root / "partial.json")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(j["eligibleSites"], 1)
        self.assertEqual(j["alreadyComplete"], 1)
        self.seal()
        out = self.root / "partial-preview"
        staged = self.preview_stage(output=out)
        self.assertEqual(staged.returncode, 0, staged.stderr + staged.stdout)
        restored = (out / "r6/scripts/B/Consumer.reds").read_text()
        self.assertEqual(restored.count(MARKER), 2)
        self.consumer.write_text(restored)
        self.seal()
        r, j = self.scan(output=self.root / "complete.json")
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(j["eligibleSites"], 0)
        self.assertEqual(j["alreadyComplete"], 2)
        no_work = self.preview_stage(output=self.root / "no-work")
        self.assertEqual(no_work.returncode, 0, no_work.stderr)
        self.assertIn("transformed=0", no_work.stdout)
        self.assertFalse((self.root / "no-work").exists())

    def test_changed_literal_or_tampered_marker_fails_closed(self):
        self.seal()
        self.assertEqual(self.preview_stage().returncode, 0)
        content = (self.preview / "r6/scripts/B/Consumer.reds").read_text()
        self.consumer.write_text(content.replace(
            "value=false */", "value=true */"))
        result, _ = self.scan(output=self.root / "tampered.json")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("PURE_BOOL_SOURCE_LITERAL_OR_PROVIDER_DRIFT",
                      result.stderr + result.stdout)
        self.consumer.write_text(content)
        self.defs.write_text(SETTINGS.replace(
            "func Diagnostics() -> Bool { return false; }",
            "func Diagnostics() -> Bool { return true; }"))
        result, _ = self.scan(output=self.root / "new-literal.json")
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("PURE_BOOL_SOURCE_LITERAL_OR_PROVIDER_DRIFT",
                      result.stderr + result.stdout)

    def test_missing_import_extension_and_overload_are_refused(self):
        self.consumer.write_text(CONSUMER.replace(
            "import TestReusableSettings.*", ""))
        result, j = self.scan(output=self.root / "missing-import.json")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(j["eligibleSites"], 0)
        self.assertGreaterEqual(j["refusedSites"], 2)

        self.consumer.write_text(CONSUMER)
        extension = self.game / "r6/scripts/C/Extension.reds"
        extension.parent.mkdir(parents=True)
        extension.write_text("""module TestReusableSettings
@wrapMethod(ReusableSettings)
public static func Enabled() -> Bool { return wrappedMethod(); }
""")
        result, j = self.scan(output=self.root / "wrapper.json")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(j["eligibleSites"], 0)

        extension.unlink()
        self.defs.write_text(SETTINGS.replace(
            "    public final static func Enabled()",
            "    public final static func Enabled(flag: Bool) -> Bool { return flag; }\n"
            "    public final static func Enabled()"))
        result, j = self.scan(output=self.root / "overload.json")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(j["eligibleSites"], 1)  # Diagnostics only

    def test_shadowing_and_comment_text_never_rewritten(self):
        self.consumer.write_text("""module GenericConsumer
import TestReusableSettings.*
public class IndependentAuthor extends IScriptable {
    public func OnEvent(ReusableSettings: ref<IScriptable>) -> Bool {
        /* ReusableSettings.Enabled() &&
           ReusableSettings.Diagnostics() */
        return ReusableSettings.Enabled();
    }
}
""")
        r, j = self.scan()
        self.assertEqual(r.returncode, 0, r.stderr)
        self.assertEqual(j["eligibleSites"], 0)
        self.assertGreaterEqual(j["refusedSites"], 1)

    def test_source_drift_and_unsafe_staging_refuse(self):
        self.seal()
        self.consumer.write_text(CONSUMER + "\n// new unsealed source\n")
        r = self.preview_stage()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("PURE_BOOL_CAPTURE_OR_SOURCE_DRIFT",
                      r.stderr + r.stdout)
        self.assertFalse(self.preview.exists())

        self.seal()
        r = self.cli("scan-pure-constants", "--game", self.game,
                     "--out", self.game / "r6/scripts/unsafe.json")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("PURE_BOOL_SCAN_OUTPUT_NOT_ISOLATED", r.stdout + r.stderr)
        self.preview.mkdir()
        sentinel = self.preview / "DoNotOverwrite.txt"
        sentinel.write_text("keep")
        r = self.preview_stage()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("PURE_BOOL_PREVIEW_OUTPUT_NOT_ISOLATED", r.stdout + r.stderr)
        self.assertEqual(sentinel.read_text(), "keep")

    def test_composes_only_identical_original_expression_with_prior_generic(self):
        self.seal()
        base = self.root / "earlier"
        target = base / "r6/scripts/B/Consumer.reds"
        target.parent.mkdir(parents=True)
        target.write_text(CONSUMER.replace(
            "        return ReusableSettings.Enabled()",
            "        // GCO-GENERIC:ACTION_LITERAL_MEMBERSHIP_V1\n"
            "        return ReusableSettings.Enabled()"))
        r = self.preview_stage(base=base)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        generated = (self.preview / "r6/scripts/B/Consumer.reds").read_text()
        self.assertIn("ACTION_LITERAL_MEMBERSHIP_V1", generated)
        self.assertEqual(generated.count(MARKER), 2)
        self.assertEqual(self.consumer.read_text(), CONSUMER)

        other = self.root / "changed"
        otherFile = other / "r6/scripts/B/Consumer.reds"
        otherFile.parent.mkdir(parents=True)
        otherFile.write_text(CONSUMER.replace(
            "ReusableSettings.Diagnostics()", "OtherThing.Diagnostics()"))
        r = self.preview_stage(output=self.root / "refused", base=other)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("PURE_BOOL_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT",
                      r.stdout + r.stderr)
        self.assertFalse((self.root / "refused").exists())


if __name__ == "__main__":
    unittest.main()
