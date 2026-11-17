"""Generic shared player signal: generated source and source drift contracts."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"


class SharedPlayerSignalTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.game = self.root / "game"
        self.source = self.game / "r6/scripts/OtherAuthor/Example.reds"
        self.source.parent.mkdir(parents=True)
        self.original = """module IndependentAuthor
public class Owner {
  public static func Find(gi: GameInstance) -> ref<PlayerPuppet> {
    return GameInstance.GetPlayerSystem(gi).GetLocalPlayerMainGameObject() as PlayerPuppet;
  }
  public static func DoNotTouch(gi: GameInstance) -> ref<GameObject> {
    return GameInstance.GetPlayerSystem(gi).GetLocalPlayerMainGameObject();
  }
}
"""
        self.source.write_text(self.original)
        self.scan_file = self.root / "scan.json"
        self.analysis = self.root / "analysis.json"
        self.preview = self.root / "preview"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=120)

    def scan(self):
        r = self.cli("scan-shared-player", "--game", self.game,
                     "--out", self.scan_file)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        result = json.loads(self.scan_file.read_text())
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": result["sourceStackSha256"]}
        }))
        return result

    def test_exact_typed_player_getter_rewritten_with_fallback_service(self):
        result = self.scan()
        self.assertEqual(result["eligible"], 1)
        self.assertEqual(result["eligibleFiles"], 1)
        r = self.cli("preview-shared-player", "--game", self.game,
                     "--analysis", self.analysis, "--out", self.preview)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        edited = (self.preview / "r6/scripts/OtherAuthor/Example.reds").read_text()
        self.assertIn("GCO-GENERIC:SHARED_PLAYER_SIGNAL_V1", edited)
        self.assertIn("GRedRuntime.SharedProviders.Player(gi)", edited)
        self.assertIn(
            "GameInstance.GetPlayerSystem(gi).GetLocalPlayerMainGameObject();",
            edited)
        self.assertEqual(self.source.read_text(), self.original)

    def test_partial_source_without_explicit_cast_is_untouched(self):
        self.source.write_text(self.original.replace(" as PlayerPuppet", ""))
        self.assertEqual(self.scan()["eligible"], 0)

    def test_source_drift_disallows_staged_output(self):
        self.scan()
        self.source.write_text(self.original + "// drift\n")
        r = self.cli("preview-shared-player", "--game", self.game,
                     "--analysis", self.analysis, "--out", self.preview)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("SHARED_PLAYER_CAPTURE_OR_SOURCE_MISMATCH", r.stdout + r.stderr)
        self.assertFalse(self.preview.exists())

    def test_existing_marker_recognized_and_no_second_rewrite(self):
        self.scan()
        r = self.cli("preview-shared-player", "--game", self.game,
                     "--analysis", self.analysis, "--out", self.preview)
        self.assertEqual(r.returncode, 0)
        self.source.write_bytes(
            (self.preview / "r6/scripts/OtherAuthor/Example.reds").read_bytes())
        self.assertEqual(self.scan_file.exists(), True)
        self.scan_file.unlink()
        again = self.scan()
        self.assertEqual(again["eligible"], 0)
        self.assertEqual(again["alreadyComplete"], 1)


if __name__ == "__main__":
    unittest.main()
