"""Game-tested handmade FE session pins (7.2r1 + 9), reimplemented
structurally from installed post-generic source in one atomic generation.
Official SCC on the real 2,331-file stack is a separate mandatory gate.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PLACES_PATH = "r6/scripts/FlushingEtiquette/FE_Places.reds"
SYSTEM_PATH = "r6/scripts/FlushingEtiquette/FlushingEtiquette.reds"

PLACES = """module FlushingEtiquette
public class FEWashroomPinData extends MappinScriptData {
  public let descKey: CName;
}
public abstract class FEPlaces {
  public static func RegisterPins(gi: GameInstance) -> array<NewMappinID> {
    let ids: array<NewMappinID>;
    let spots: array<Vector4> = [
      new Vector4(1.0, 2.0, 3.0, 1.0),
      new Vector4(4.0, 5.0, 6.0, 1.0)
    ];
    let keys: array<CName> = [n"Washroom-A", n"Washroom-B"];
    let mappins = GameInstance.GetMappinSystem(gi);
    let i = 0;
    while i < ArraySize(spots) {
      let data = new FEWashroomPinData();
      data.descKey = keys[i];
      let pin: MappinData;
      pin.mappinType = t"Mappins.DefaultStaticMappin";
      pin.variant = gamedataMappinVariant.SitVariant;
      pin.active = true;
      pin.scriptData = data;
      ArrayPush(ids, mappins.RegisterMappin(pin, spots[i]));
      i += 1;
    }
    return ids;
  }

  public static func UnregisterPins(gi: GameInstance, ids: array<NewMappinID>) -> Void {
    let mappins = GameInstance.GetMappinSystem(gi);
    for id in ids { mappins.UnregisterMappin(id); }
  }

  public static func PinData(mappin: wref<IMappin>) -> ref<FEWashroomPinData> {
    if !IsDefined(mappin) || NotEquals(mappin.GetVariant(), gamedataMappinVariant.SitVariant) { return null; }
    return mappin.GetScriptData() as FEWashroomPinData;
  }
  public static func PinTint() -> HDRColor = new HDRColor(0.1, 0.5, 1.0, 1.0)
}

@addMethod(BaseMappinBaseController)
protected final func FEApplyWashroomIcon() -> Void {
  inkImageRef.SetTexturePart(this.iconWidget, n"npc");
  inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint());
}

@wrapMethod(BaseMinimapMappinController)
protected func Update() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint()); }
}

@wrapMethod(MinimapPOIMappinController)
protected final func UpdateIcon() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { this.FEApplyWashroomIcon(); }
}

@wrapMethod(BaseWorldMapMappinController)
protected func UpdateIcon() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { this.FEApplyWashroomIcon(); }
}
"""

SYSTEM = """module FlushingEtiquette
public class FESystem extends ScriptableSystem {
  private let m_player: wref<PlayerPuppet>;
  private let m_pins: array<NewMappinID>;

  private func OnPlayerAttach(request: ref<PlayerAttachRequest>) -> Void {
    let player = request.owner as PlayerPuppet;
    if !IsDefined(player) { return; }
    this.DropPlayer();
    this.m_player = player;
    this.WatchPlayer(player);
    this.m_pins = FEPlaces.RegisterPins(this.GetGameInstance());
    this.RefreshCredLock();
  }

  private func DropPlayer() -> Void {
    let gi = this.GetGameInstance();
    if ArraySize(this.m_pins) > 0 {
      FEPlaces.UnregisterPins(gi, this.m_pins);
      ArrayClear(this.m_pins);
    }
    this.m_player = null;
  }
}
"""

class WashroomPinSemanticContracts(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.index = 0

    def generate(self, places=PLACES, system=SYSTEM,
                 place_path=PLACES_PATH, system_path=SYSTEM_PATH):
        self.index += 1
        game = self.root / f"game-{self.index}"
        for path, value in ((place_path, places), (system_path, system)):
            full = game / path
            full.parent.mkdir(parents=True, exist_ok=True)
            full.write_text(value, encoding="utf8")
        output = self.root / f"output-{self.index}"
        run = subprocess.run(
            ["dotnet", str(DLL), "phase2-fe-pins",
             "--game", str(game), "--out", str(output)],
            text=True, capture_output=True, timeout=65)
        self.assertEqual(run.returncode, 0,
                         run.stdout + "\n" + run.stderr)
        report = json.loads((output / "phase2-result.json").read_text("utf8"))
        self.assertEqual((game / place_path).read_text("utf8"), places)
        self.assertEqual((game / system_path).read_text("utf8"), system)
        p_out = output / place_path
        s_out = output / system_path
        return (report, p_out.read_text("utf8") if p_out.exists() else None,
                s_out.read_text("utf8") if s_out.exists() else None)

    def assert_complete(self, p, s):
        self.assertIn("class FEWashroomMappinService", p)
        self.assertEqual(p.count("class FEWashroomMappinService"), 1)
        self.assertIn("Session/Ready", p)
        self.assertIn("Session/BeforeEnd", p)
        self.assertIn("mappins.SetMappinActive(id, true);", p)
        self.assertIn("FEPlaces.PinCount()", p)
        self.assertIn("func PinCount() -> Int32 = 2", p)
        self.assertNotIn("@wrapMethod(BaseMinimapMappinController)", p)
        self.assertIn("@wrapMethod(MinimapPOIMappinController)", p)
        self.assertIn("inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint());", p)
        self.assertIn("return mappin.GetScriptData() as FEWashroomPinData;", p)
        self.assertNotIn("NotEquals(mappin.GetVariant()", p)
        self.assertIn("FEWashroomMappinService.Get(this.GetGameInstance())", s)
        self.assertIn("washrooms.EnsurePins();", s)
        self.assertNotIn("FEPlaces.UnregisterPins(gi, this.m_pins);", s)

    def test_post_generic_stock_generates_all_in_one_run(self):
        report, p, s = self.generate()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.assertEqual(len(report["sourcePaths"]), 2)
        self.assert_complete(p, s)
        again, p2, s2 = self.generate(p, s)
        self.assertEqual(again["status"], "COMPLETE", again)
        self.assertEqual(again["appliedEdits"], 0)
        self.assertIsNone(p2)
        self.assertIsNone(s2)

    def test_partial_pin_activation_finishes_only_missing_operation(self):
        first, p, s = self.generate()
        p = p.replace("mappins.SetMappinActive(id, true);", "")
        report, updated, sys = self.generate(p, s)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(len(report["sourcePaths"]), 1)
        self.assertIsNone(sys)
        self.assert_complete(updated, s)
        self.assertEqual(updated.count("FEWashroomMappinService"), p.count("FEWashroomMappinService"))

    def test_markerless_complete_source_is_unchanged(self):
        _, p, s = self.generate()
        report, p2, s2 = self.generate(p, s)
        self.assertEqual(report["status"], "COMPLETE")
        self.assertIsNone(p2)
        self.assertIsNone(s2)

    def test_compatible_newer_sites_recount_and_preserve_custom_changes(self):
        p = PLACES.replace(
            "new Vector4(4.0, 5.0, 6.0, 1.0)",
            "new Vector4(4.0, 5.0, 6.0, 1.0),\n"
            "      new Vector4(7.0, 8.0, 9.0, 1.0)")
        p = p.replace('n"Washroom-B"', 'n"Washroom-B", n"Washroom-C"')
        p = p.replace("module FlushingEtiquette",
                      "module /* changed */ FlushingEtiquette")
        p += "\n// GCO-GENERIC:EXISTING_CUSTOM_RULE_V1\n"
        report, rewritten, sys = self.generate(
            p, SYSTEM, "r6/scripts/UserEdited/Places.reds",
            "r6/scripts/OtherOwner/Health.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("func PinCount() -> Int32 = 3", rewritten)
        self.assertIn("GCO-GENERIC:EXISTING_CUSTOM_RULE_V1", rewritten)
        self.assert_complete(rewritten.replace(" = 3", " = 2"), sys)

    def test_custom_global_update_effect_refuses_entire_operation(self):
        p = PLACES.replace(
            "  wrappedMethod();\n  if IsDefined(FEPlaces.PinData",
            "  wrappedMethod();\n  this.OtherMinimapEffect();\n"
            "  if IsDefined(FEPlaces.PinData", 1)
        report, p2, s2 = self.generate(p)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"],
                         "FE_PIN_GLOBAL_UPDATE_HAS_OTHER_EFFECTS")
        self.assertIsNone(p2)
        self.assertIsNone(s2)

    def test_extra_unproven_player_cleanup_refuses_entire_operation(self):
        s = SYSTEM.replace(
            "      ArrayClear(this.m_pins);",
            "      ArrayClear(this.m_pins);\n      this.CustomSideEffect();")
        report, p2, s2 = self.generate(PLACES, s)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertIsNone(p2)
        self.assertIsNone(s2)

    def test_site_key_mismatch_refuses_narrowly(self):
        p = PLACES.replace('n"Washroom-A", n"Washroom-B"',
                           'n"Washroom-A"')
        report, p2, s2 = self.generate(p)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"], "FE_PIN_SITE_KEY_COUNT_UNPROVEN")
        self.assertIsNone(p2)
        self.assertIsNone(s2)

    def test_uninstalled_owner_is_not_created(self):
        report, p, s = self.generate(
            "module Other\npublic class OtherClass {}",
            "module Other\npublic class OtherSystem {}")
        self.assertEqual(report["status"], "NOT_APPLICABLE", report)
        self.assertIsNone(p)
        self.assertIsNone(s)

if __name__ == "__main__":
    unittest.main()
