"""STABLE_ARRAY_BOUND_V1: source-driven, multi-owner, no-call loop hoisting.

Must keep body, evaluation order, partial/full installation compatibility,
current-source sealing and prior-stage marker composition intact.
"""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCES = {
    "AuthorA": """module AuthorA
public class A extends IScriptable {
  public func Join() -> String {
    let lines: array<String> = ["one", "two"];
    let text: String = "";
    let i: Int32 = 0;
    while i < ArraySize(lines) {
      text += lines[i];
      i += 1;
    };
    return text;
  }
}
""",
    "AuthorB": """module AuthorB
public class B extends IScriptable {
  public func Join(lines: array<String>) -> String {
    let out: String = "";
    let i: Int32 = 0;
    while i < ArraySize(lines) {
      if i > 0 { out += " "; };
      out += lines[i];
      i += 1;
    };
    return out;
  }
}
""",
    "AuthorC": """module AuthorC
public class C extends IScriptable {
  public func Sum() -> Float {
    let values: array<Float> = [1.0, 2.0, 3.0];
    let total: Float = 0.0;
    let i: Int32 = 0;
    while i < ArraySize(values) {
      total += values[i];
      i += 1;
    };
    return total;
  }
}
""",
    "UnsafeCall": """module UnsafeCall
public class Mutator extends IScriptable {
  public func Run() -> Void {
    let values: array<Int32> = [1, 2];
    let i: Int32 = 0;
    while i < ArraySize(values) {
      this.Invalidate(values);
      i += 1;
    };
  }
}
""",
    "UnsafeWrite": """module UnsafeWrite
public class Mutation extends IScriptable {
  public func Run() -> Void {
    let values: array<Int32> = [1, 2];
    let i: Int32 = 0;
    while i < ArraySize(values) {
      values[i] = 7;
      i += 1;
    };
  }
}
""",
    "UnsafeScope": """module UnsafeScope
public class Missing extends IScriptable {
  public func Run() -> Void {
    if true { let values: array<Int32> = [1, 2]; }
    let i: Int32 = 0;
    while i < ArraySize(values) {
      i += 1;
    };
  }
}
""",
}


class StableArrayBoundContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c",
                            "Release", "--nologo"], check=True)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        self.paths = {}
        for owner, source in SOURCES.items():
            file = self.game / "r6/scripts" / owner / "Original.reds"
            file.parent.mkdir(parents=True)
            file.write_text(source, encoding="utf-8")
            self.paths[owner] = file
        self.scanfile = self.root / "scan.json"
        self.audit = self.root / "audit.json"
        self.capture = self.root / "capture.json"
        self.preview = self.root / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=120)

    def scan(self):
        if self.scanfile.exists():
            self.scanfile.unlink()
        r = self.cli("scan-stable-array-bound", "--game",
                     self.game, "--out", self.scanfile)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return json.loads(self.scanfile.read_text())

    def seal(self):
        if self.audit.exists():
            self.audit.unlink()
        r = self.cli("source-audit", "--game", self.game,
                     "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        sha = json.loads(self.audit.read_text())["sourceStackSha256"]
        self.capture.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance":
                        "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": sha}
        }))
        return sha

    def stage(self, folder=None, base=None):
        args = ["preview-stable-array-bound", "--game", self.game,
                "--analysis", self.capture,
                "--out", folder or self.preview]
        if base:
            args += ["--base-preview", base]
        return self.cli(*args)

    def generated(self, owner, root=None):
        return (root or self.preview) / "r6/scripts" / owner / "Original.reds"

    def test_three_independent_owners_rewrite_no_original_bytes(self):
        original = {k: f.read_bytes() for k, f in self.paths.items()}
        scan = self.scan()
        self.assertEqual(scan["sourceFiles"], len(SOURCES))
        self.assertEqual(scan["eligible"], 3)
        self.assertEqual(scan["alreadyComplete"], 0)
        self.assertEqual({x["path"].split("/")[2] for x in scan["candidates"]},
                         {"AuthorA", "AuthorB", "AuthorC"})
        self.assertFalse(scan["generationAuthorized"])
        self.seal()
        r = self.stage()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("transformed=3", r.stdout)
        for owner in ("AuthorA", "AuthorB", "AuthorC"):
            t = self.generated(owner).read_text()
            self.assertEqual(t.count("GCO-GENERIC:STABLE_ARRAY_BOUND_V1"), 1)
            self.assertIn("let __gcoArrBoundL", t)
            self.assertEqual(t.count("ArraySize("), 1)
            self.assertIn("while i < __gcoArrBoundL", t)
            self.assertIn("i += 1;", t)
        for owner in ("UnsafeCall", "UnsafeWrite", "UnsafeScope"):
            self.assertFalse(self.generated(owner).exists())
        self.assertEqual(original, {k: f.read_bytes() for k, f in self.paths.items()})

    def test_partial_and_second_pass_idempotence(self):
        self.seal()
        self.assertEqual(self.stage().returncode, 0)
        shutil.copyfile(self.generated("AuthorA"), self.paths["AuthorA"])
        scan = self.scan()
        self.assertEqual(scan["alreadyComplete"], 1)
        self.assertEqual(scan["eligible"], 2)
        shutil.rmtree(self.preview)
        self.seal()
        r = self.stage()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertIn("transformed=2", r.stdout)
        self.assertFalse(self.generated("AuthorA").exists())
        for owner in ("AuthorB", "AuthorC"):
            shutil.copyfile(self.generated(owner), self.paths[owner])
        self.assertEqual(self.scan()["eligible"], 0)
        self.assertEqual(self.scan()["alreadyComplete"], 3)

    def test_modified_generated_body_refuses_drift(self):
        self.seal()
        self.assertEqual(self.stage().returncode, 0)
        modified = self.generated("AuthorA").read_text().replace(
            "text += lines[i];", "text += lines[i] + \"BAD\";")
        self.paths["AuthorA"].write_text(modified)
        r = self.cli("scan-stable-array-bound", "--game", self.game,
                     "--out", self.root / "tamper.json")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ARRAY_BOUND_MODIFIED_BODY", r.stdout + r.stderr)

    def test_original_source_drift_refuses_preview(self):
        self.seal()
        self.paths["AuthorB"].write_text(
            self.paths["AuthorB"].read_text().replace('out += lines[i];',
                                                      'out += "custom" + lines[i];'))
        r = self.stage()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ARRAY_BOUND_CAPTURE_OR_SOURCE_DRIFT", r.stdout + r.stderr)

    def test_effective_prior_stage_is_preserved(self):
        self.seal()
        base = self.root / "prior"
        for owner in ("AuthorA", "AuthorB", "AuthorC"):
            target = self.generated(owner, base)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text("// GCO-GENERIC:EXISTING_PASS_V1\n" +
                              self.paths[owner].read_text())
        r = self.stage(base=base)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        for owner in ("AuthorA", "AuthorB", "AuthorC"):
            t = self.generated(owner).read_text()
            self.assertTrue(t.startswith("// GCO-GENERIC:EXISTING_PASS_V1"))
            self.assertIn("GCO-GENERIC:STABLE_ARRAY_BOUND_V1", t)
        # Prior pass may change the loop; source-aware composition MUST reject.
        shutil.rmtree(self.preview)
        p = self.generated("AuthorA", base)
        p.write_text(p.read_text().replace("text += lines[i];",
                                           "text += \"changed\" + lines[i];"))
        r = self.stage(base=base)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ARRAY_BOUND_EFFECTIVE_SOURCE_OVERLAP_OR_DRIFT",
                      r.stdout + r.stderr)

    def test_refuse_output_in_live_game(self):
        self.seal()
        r = self.stage(folder=self.game / "r6/scripts/AuthorA/preview")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ARRAY_BOUND_PREVIEW_OUTPUT_NOT_ISOLATED",
                      r.stdout + r.stderr)


if __name__ == "__main__":
    unittest.main()
