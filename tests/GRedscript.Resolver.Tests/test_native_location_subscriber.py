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
        self.assertEqual(len(adapters), 2)
        a = next(x for x in adapters if x.name.startswith("GCO_NativeLocation_")).read_text()
        original_overlay = (self.output / "r6/scripts/ExampleMod/System.reds").read_text()
        self.assertIn("GCO-GENERIC:NATIVE_PLAYER_LOCATION_PREDICATE_V1", original_overlay)
        self.assertIn("GCOMayBeNear_", original_overlay)
        self.assertIn("&& Vector4.Distance(playerPos, site.location)", original_overlay)
        self.assertIn("for site in SampleSites()", original_overlay)
        self.assertIn("this.QueueProximityCheck" if "this.QueueProximityCheck" in CONSUMER else "return;", original_overlay)
        for token in (
            "NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1",
            "extends NativeLocationListener", "AllKnownFar()",
            "owner", "state", "MayBeNear(index: Int32)"):
            self.assertIn(token, a)
        for token in (
            "GCO-GENERIC:NATIVE_LOCATION_DIRECT_LIFECYCLE_V1",
            "SubscribeLocation(", "UnsubscribeLocation(",
            "new GCETNativeLocationBackend()",
            "this.GCOAttachNativeLocations_", "this.GCODetachNativeLocations_",
            "import GRedRuntime.*"):
            self.assertIn(token, original_overlay)
        self.assertNotIn("@wrapMethod(", a)
        self.assertEqual((self.folder / "Sites.reds").read_text(), SITES)
        self.assertEqual((self.folder / "System.reds").read_text(), CONSUMER)

    def test_npc_proximity_not_admitted(self):
        p = self.folder / "System.reds"
        p.write_text(CONSUMER.replace(
            "this.player.GetWorldPosition()", "npc.GetWorldPosition()")
            .replace("let playerPos", "let npcPos")
            .replace("Vector4.Distance(playerPos", "Vector4.Distance(npcPos"))
        self.assertEqual(self.scan()["eligible"], 0)

    def test_conditional_helper_sphere_not_mapped_into_static_prefix(self):
        sites = self.folder / "Sites.reds"
        sites.write_text(SITES + """
@if(ModuleExists("ExtraMod"))
private func AdditionalSite() -> SampleSite =
    SampleSite(SampleArea.Other, Vector4(111.0, 222.0, 333.0, 1.0),
               5.0, ExampleType.Other);
""")
        result = self.scan()
        self.assertEqual(result["eligible"], 1)
        self.assertEqual(result["candidates"][0]["SiteCount"], 2)
        stage = self.cli("preview-native-location-subscribers",
                         "--out", self.output,
                         "--source-sha", result["sourceStackSha256"])
        self.assertEqual(stage.returncode, 0, stage.stdout + stage.stderr)
        adapter = next(p for p in self.output.rglob("*.reds")
                       if p.name.startswith("GCO_NativeLocation_")).read_text()
        original_overlay = (self.output / "r6/scripts/ExampleMod/System.reds").read_text()
        self.assertIn("this.m_gcoNativeListener_", original_overlay)
        self.assertIn(".Initialize(2)", original_overlay)

    def test_factory_site_reordering_is_not_admitted(self):
        source = (self.folder / "Sites.reds")
        source.write_text(SITES.replace(
            "  return sites;",
            "  ArrayInsert(sites, 0, extraSite);\n  return sites;"))
        self.assertEqual(self.scan()["eligible"], 0)

    def test_static_prefix_with_dynamic_trailing_site_remains_eligible(self):
        source = (self.folder / "Sites.reds")
        source.write_text(SITES.replace(
            "  return sites;",
            "  if useExtraSite { ArrayPush(sites, extraSite); }\n  return sites;"))
        data = self.scan()
        self.assertEqual(data["eligible"], 1)
        self.assertEqual(data["candidates"][0]["SiteCount"], 2)
        r = self.cli("preview-native-location-subscribers", "--out", self.output,
                     "--source-sha", data["sourceStackSha256"])
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        adapter = next(p for p in self.output.rglob("*.reds")
                       if p.name.startswith("GCO_NativeLocation_")).read_text()
        self.assertIn("index >= ArraySize(this.siteStates)", adapter)
        self.assertIn("return true;", adapter)

    def test_untypeable_distance_field_is_not_rewritten(self):
        source = (self.folder / "Sites.reds")
        source.write_text(SITES.replace("let proximityRadius: Float;",
                                        "let proximityRadius: Int32;"))
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

    def test_generated_source_is_idempotent(self):
        first = self.scan()
        self.assertEqual(first["eligible"], 1)
        r = self.cli("preview-native-location-subscribers", "--out", self.output,
                     "--source-sha", first["sourceStackSha256"])
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        adapter = next(p for p in self.output.rglob("*.reds")
                       if p.name.startswith("GCO_NativeLocation_"))
        installed = self.folder / adapter.name
        installed.write_bytes(adapter.read_bytes())
        self.scan_path.unlink()
        second = self.scan()
        self.assertEqual(second["eligible"], 1)
        self.assertEqual(second["alreadyComplete"], 0)
        # Partial stage (adapter only) requires the original predicate rewrite.

    def test_modified_generated_source_refused(self):
        first = self.scan()
        r = self.cli("preview-native-location-subscribers", "--out", self.output,
                     "--source-sha", first["sourceStackSha256"])
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        adapter = next(p for p in self.output.rglob("*.reds")
                       if p.name.startswith("GCO_NativeLocation_"))
        (self.folder / adapter.name).write_text(adapter.read_text() + "// drift\n")
        self.scan_path.unlink()
        r = self.cli("scan-native-location-subscribers", "--out", self.scan_path)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("NATIVE_CONSUMER_SOURCE_COLLISION", r.stdout + r.stderr)

    def test_complete_pristine_second_pass_no_work(self):
        initial = self.scan()
        stage = self.cli("preview-native-location-subscribers", "--out",
                         self.output, "--source-sha", initial["sourceStackSha256"])
        self.assertEqual(stage.returncode, 0, stage.stdout + stage.stderr)
        for staged in self.output.rglob("*.reds"):
            installed = self.game / staged.relative_to(self.output)
            installed.parent.mkdir(parents=True, exist_ok=True)
            installed.write_bytes(staged.read_bytes())
        self.scan_path.unlink()
        again = self.scan()
        self.assertEqual(again["eligible"], 0)
        self.assertEqual(again["alreadyComplete"], 1)

    def test_fallback_and_dynamic_extra_site_never_skipped(self):
        data = self.scan()
        stage = self.cli("preview-native-location-subscribers", "--out",
                         self.output, "--source-sha", data["sourceStackSha256"])
        self.assertEqual(stage.returncode, 0, stage.stdout + stage.stderr)
        adapter = next(p for p in self.output.rglob("*.reds")
                       if p.name.startswith("GCO_NativeLocation_")).read_text()
        self.assertIn("if index < 0 || index >= ArraySize(this.siteStates)", adapter)
        self.assertIn("return true; // dynamic or absent site", adapter)
        self.assertIn("return this.siteStates[index] != 0;", adapter)
        self.assertIn("this.siteStates[i] = -1;", adapter)

    def test_native_not_required_for_initial_scan(self):
        self.assertEqual(self.scan()["eligible"], 1)
        self.assertFalse((self.game / "red4ext").exists())


if __name__ == "__main__":
    unittest.main()
