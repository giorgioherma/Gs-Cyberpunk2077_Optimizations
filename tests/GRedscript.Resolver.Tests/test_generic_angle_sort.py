"""Generic angle-sort hoist: structural proof, source drift, idempotence."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCE = """module ExampleGeometry

public struct SquareArea {
  public let a: Vector2;
  public let b: Vector2;
  public let c: Vector2;
  public let d: Vector2;
}

private func SortGeometry(sq: SquareArea) -> array<Vector2> {
    let center = new Vector2(
        (sq.a.X + sq.b.X + sq.c.X + sq.d.X) / 4.0,
        (sq.a.Y + sq.b.Y + sq.c.Y + sq.d.Y) / 4.0
    );
    let pts: array<Vector2> = [sq.a, sq.b, sq.c, sq.d];
    let i = 0;
    let j: Int32;

    while (i < ArraySize(pts)) {
        j = i + 1;
        while (j < ArraySize(pts)) {
            let angleI = AtanF(pts[i].Y - center.Y, pts[i].X - center.X);
            let angleJ = AtanF(pts[j].Y - center.Y, pts[j].X - center.X);

            if (angleI > angleJ) {
                let tmp = pts[i];
                pts[i] = pts[j];
                pts[j] = tmp;
            }

            j += 1;
        }
        i += 1;
    }

    return pts;
}
"""


class GeometryGenericContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        base = Path(tmp.name)
        self.game = base / "game"
        self.source = self.game / "r6" / "scripts" / "AnyMod" / "Geometry.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(SOURCE, encoding="utf8")
        self.audit = base / "source-audit.json"
        self.analysis = base / "analysis.json"
        self.scan = base / "scan.json"
        self.preview = base / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=90)

    def scan_source(self):
        r = self.cli("scan-angle-sort", "--game", self.game, "--out", self.scan)
        self.assertEqual(r.returncode, 0, r.stderr)
        return json.loads(self.scan.read_text(encoding="utf8"))

    def analysis_for_current(self):
        if self.audit.exists():
            self.audit.unlink()
        r = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stderr)
        sha = json.loads(self.audit.read_text(encoding="utf8"))["sourceStackSha256"]
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": sha}
        }), encoding="utf8")

    def run_preview(self):
        return self.cli("preview-angle-sort", "--game", self.game,
                        "--analysis", self.analysis, "--out", self.preview)

    def test_real_four_vertex_invariant_hoist_preview_and_idempotence(self):
        before = self.source.read_bytes()
        scan = self.scan_source()
        self.assertEqual(scan["sourceFiles"], 1)
        self.assertEqual(scan["eligible"], 1)
        self.assertEqual(scan["alreadyComplete"], 0)
        self.assertFalse(scan["generationAuthorized"])
        self.assertFalse(scan["measuredSavingsClaimed"])
        self.assertFalse(scan["sourceWrites"])
        self.analysis_for_current()
        outcome = self.run_preview()
        self.assertEqual(outcome.returncode, 0, outcome.stderr)
        self.assertIn("transformed=1", outcome.stdout)
        generated = self.preview / "r6" / "scripts" / "AnyMod" / "Geometry.reds"
        self.assertTrue(generated.is_file())
        src = generated.read_text(encoding="utf8")
        self.assertIn("GCO-GENERIC:ANGLE_SORT_INVARIANT_V1", src)
        self.assertIn("angleI = angleJ;", src)
        self.assertIn("if (j < ArraySize(pts))", src)
        self.assertEqual(src.count("AtanF("), 2)
        self.assertEqual(self.source.read_bytes(), before)

        shutil.copyfile(generated, self.source)
        self.scan.unlink()
        scan2 = self.scan_source()
        self.assertEqual(scan2["eligible"], 0)
        self.assertEqual(scan2["alreadyComplete"], 1)
        shutil.rmtree(self.preview)
        self.analysis_for_current()
        second = self.run_preview()
        self.assertEqual(second.returncode, 0, second.stderr)
        self.assertIn("transformed=0", second.stdout)
        self.assertFalse(self.preview.exists())

    def test_altered_generated_marker_fails_closed(self):
        self.analysis_for_current()
        self.assertEqual(self.run_preview().returncode, 0)
        generated = self.preview / "r6/scripts/AnyMod/Geometry.reds"
        damaged = generated.read_text().replace(
            "angleI = angleJ;", "angleI = angleJ + 0.0;")
        self.source.write_text(damaged, encoding="utf8")
        r = self.cli("scan-angle-sort", "--game", self.game,
                     "--out", self.scan)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ANGLE_SORT_PARTIAL_OR_DRIFTED_GENERATED_MARKER",
                      r.stderr + r.stdout)

    def test_non_equivalent_comparison_does_not_match(self):
        self.source.write_text(SOURCE.replace("angleI > angleJ",
                                              "angleI >= angleJ"), encoding="utf8")
        scan = self.scan_source()
        self.assertEqual(scan["eligible"], 0)

    def test_source_drift_rejects_preview(self):
        self.analysis_for_current()
        self.source.write_text(SOURCE + "\n// changed in game\n", encoding="utf8")
        r = self.run_preview()
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ANGLE_SORT_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      r.stdout + r.stderr)

    def test_uncollected_capture_and_game_write_refused(self):
        self.analysis_for_current()
        data = json.loads(self.analysis.read_text())
        data["capture"]["collectionProvenance"] = "UNVERIFIED"
        self.analysis.write_text(json.dumps(data), encoding="utf8")
        self.assertNotEqual(self.run_preview().returncode, 0)
        self.analysis_for_current()
        r = self.cli("preview-angle-sort", "--game", self.game,
                     "--analysis", self.analysis,
                     "--out", self.game / "r6" / "scripts" / "Generated")
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("ANGLE_SORT_PREVIEW_OUTPUT_NOT_ISOLATED",
                      r.stdout + r.stderr)


if __name__ == "__main__":
    unittest.main()
