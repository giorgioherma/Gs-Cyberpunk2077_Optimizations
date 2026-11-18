"""First source-matched native location subscriber: one mod lifecycle, no
original gameplay/source modifications, no native DLL required at scan time."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SITES = """module ExampleMod.Locations
public struct SampleSite {
  let location: Vector4;
  let proximityRadius: Float;
}
public func SampleSites() -> array<SampleSite> {
  let sites = [
    SampleSite(SampleArea.Main, Vector4(10.0, 20.0, 30.0, 1.0), 2.0, ExampleType.Main),
    SampleSite(SampleArea.Other, Vector4(40.0, 50.0, 60.0, 1.0), 3.0, ExampleType.Other)
  ];
  return sites;
}
"""

CONSUMER = """module ExampleMod.Locations
public class SampleController extends ScriptableSystem {
  private let player: wref<GameObject>;
  public func Start() { this.player = GetPlayer(GetGameInstance()); }
  public func Stop() { this.player = null; }
  public func OnProximityCheck() {
    let playerPos = this.player.GetWorldPosition();
    for site in SampleSites() {
      if Vector4.Distance(playerPos, site.location) <= site.proximityRadius {
        return;
      }
    }
  }
}
"""


class NativeLocationSubscriberTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.game = self.root / "game"
        self.folder = self.game / "r6/scripts/ExampleMod"
        self.folder.mkdir(parents=True)
        (self.folder / "Sites.reds").write_text(SITES)
        (self.folder / "System.reds").write_text(CONSUMER)
        self.scan_path = self.root / "scan.json"
        self.output = self.root / "preview"

    def cli(self, cmd, *opts):
        return subprocess.run(
            ["dotnet", str(DLL), cmd, "--game", str(self.game),
             *map(str, opts)], text=True, capture_output=True, timeout=120)

    def scan(self):
        r = self.cli("scan-native-location-subscribers", "--out", self.scan_path)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        return json.loads(self.scan_path.read_text())

    def test_generates_additive_mod_adapter_with_cleanup_and_original_checks(self):
        data = self.scan()
        self.assertEqual(data["eligible"], 1)
        self.assertEqual(data["candidates"][0]["SiteCount"], 2)
        self.assertTrue(data["requiresNative"])
        self.assertFalse(data["productionZipAuthorized"])
        r = self.cli("preview-native-location-subscribers", "--out", self.output,
                     "--source-sha", data["sourceStackSha256"])
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        adapters = list(self.output.rglob("*.reds"))
        self.assertEqual(len(adapters), 1)
        a = adapters[0].read_text()
        for token in (
            "NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1",
            "@wrapMethod(SampleController)", "public func Start() -> Void",
            "public func Stop() -> Void",
            "SubscribeLocation(", "UnsubscribeLocation(",
            "new GCETNativeLocationBackend()", "AllKnownFar()",
            "wrappedMethod()", "owner", "state"):
            self.assertIn(token, a)
        self.assertEqual((self.folder / "Sites.reds").read_text(), SITES)
        self.assertEqual((self.folder / "System.reds").read_text(), CONSUMER)

    def test_npc_proximity_not_admitted(self):
        p = self.folder / "System.reds"
        p.write_text(CONSUMER.replace(
            "this.player.GetWorldPosition()", "npc.GetWorldPosition()")
            .replace("let playerPos", "let npcPos")
            .replace("Vector4.Distance(playerPos", "Vector4.Distance(npcPos"))
        self.assertEqual(self.scan()["eligible"], 0)

    def test_no_start_stop_lifecycle_refused(self):
        p = self.folder / "System.reds"
        p.write_text(CONSUMER.replace("public func Stop()", "public func Release()"))
        self.assertEqual(self.scan()["eligible"], 0)

    def test_source_drift_refuses_preview(self):
        data = self.scan()
        p = self.folder / "Sites.reds"
        p.write_text(SITES + "// change to installed mod\n")
        r = self.cli("preview-native-location-subscribers", "--out", self.output,
                     "--source-sha", data["sourceStackSha256"])
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("NATIVE_CONSUMER_CAPTURE_SOURCE_DRIFT",
                      r.stdout + r.stderr)
        self.assertFalse(self.output.exists())

    def test_native_not_required_for_initial_scan(self):
        self.assertEqual(self.scan()["eligible"], 1)
        self.assertFalse((self.game / "red4ext").exists())


if __name__ == "__main__":
    unittest.main()
