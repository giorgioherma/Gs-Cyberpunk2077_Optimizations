"""FE global minimap Update dormancy. Historical pass 9 concept, rebuilt
from current source; official full-stack SCC is the separate Phase 2 gate.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PATH = "r6/scripts/FlushingEtiquette/FE_Places.reds"

WRAPPER = """@wrapMethod(BaseMinimapMappinController)
protected func Update() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint()); }
}
"""

SOURCE = """module FlushingEtiquette
public class FEWashroomPinData extends MappinScriptData {
  public let descKey: CName;
}
public abstract class FEPlaces {
  public static func PinData(mappin: wref<IMappin>) -> ref<FEWashroomPinData> {
    return mappin.GetScriptData() as FEWashroomPinData;
  }
  public static func PinTint() -> HDRColor = new HDRColor(0.1, 0.5, 1.0, 1.0)
}
@addMethod(BaseMappinBaseController)
protected final func FEApplyWashroomIcon() -> Void {
  inkImageRef.SetAtlasResource(this.iconWidget, r"base\\gameplay\\gui\\common\\icons\\mappin_icons.inkatlas");
  inkImageRef.SetTexturePart(this.iconWidget, n"npc");
  inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint());
}
@wrapMethod(BaseWorldMapMappinController)
protected func UpdateIcon() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { this.FEApplyWashroomIcon(); }
}
""" + WRAPPER + """
@wrapMethod(MinimapPOIMappinController)
protected final func UpdateIcon() -> Void {
  wrappedMethod();
  if IsDefined(FEPlaces.PinData(this.GetMappin())) { this.FEApplyWashroomIcon(); }
}
@wrapMethod(WorldMapTooltipController)
public func SetData(const data: script_ref<WorldMapTooltipData>,
                    menu: ref<WorldMapMenuGameController>) -> Void {
  wrappedMethod(data, menu);
  this.UpdateWashroomDescription();
}
"""

class FlushingEtiquetteMinimapContracts(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.run_id = 0

    def generate(self, source=SOURCE, path=PATH, extras=None):
        self.run_id += 1
        game = self.root / f"game-{self.run_id}"
        src = game / path
        src.parent.mkdir(parents=True)
        src.write_text(source, encoding="utf8")
        for extra_path, extra in (extras or {}).items():
            file = game / extra_path
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(extra, encoding="utf8")
        output = self.root / f"output-{self.run_id}"
        process = subprocess.run(
            ["dotnet", str(DLL), "phase2-fe-minimap",
             "--game", str(game), "--out", str(output)],
            capture_output=True, text=True, timeout=65)
        self.assertEqual(0, process.returncode,
                         process.stdout + "\n" + process.stderr)
        report = json.loads((output / "phase2-result.json").read_text("utf8"))
        self.assertEqual(src.read_text("utf8"), source)
        staged = output / path
        return report, staged.read_text("utf8") if staged.exists() else None

    def test_stock_complete_one_pass_and_idempotent(self):
        result, optimized = self.generate()
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(result["appliedEdits"], 1)
        self.assertNotIn("@wrapMethod(BaseMinimapMappinController)", optimized)
        self.assertIn("@wrapMethod(MinimapPOIMappinController)", optimized)
        self.assertIn("this.FEApplyWashroomIcon();", optimized)
        self.assertIn("this.UpdateWashroomDescription();", optimized)
        self.assertIn("FEPlaces.PinTint()", optimized)
        again, second = self.generate(optimized)
        self.assertEqual(again["status"], "COMPLETE", again)
        self.assertEqual(again["appliedEdits"], 0)
        self.assertIsNone(second)

    def test_partially_optimized_existing_specific_hook_only_finishes_wrapper(self):
        partial = SOURCE.replace(
            "  inkImageRef.SetTexturePart(this.iconWidget, n\"npc\");",
            "  inkImageRef.SetTexturePart(this.iconWidget, n\"npc\");\n"
            "  this.OtherCompatibleModChange();")
        report, output = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED")
        self.assertIn("this.OtherCompatibleModChange();", output)
        self.assertEqual(output.count("FEApplyWashroomIcon()"), 1)
        self.assertNotIn("@wrapMethod(BaseMinimapMappinController)", output)

    def test_markerless_complete_custom_version_noop(self):
        complete = SOURCE.replace(WRAPPER, "")
        report, output = self.generate(complete)
        self.assertEqual(report["status"], "COMPLETE")
        self.assertIsNone(output)

    def test_moved_source_comments_and_unrelated_generic_work_survive(self):
        custom = SOURCE.replace("module FlushingEtiquette",
                                "module /* version-fluid */ FlushingEtiquette")
        custom = custom.replace("protected func Update() -> Void",
                                "protected /* moved */ func\n Update() -> Void")
        custom += ("\npublic class CustomSettings {\n"
                   " // GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1\n"
                   " public static func Enabled() -> Bool { return false; }\n}\n")
        path = "r6/scripts/CustomVortex/ModifiedWashroomIcons.reds"
        report, updated = self.generate(custom, path)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("GCO-GENERIC:PURE_FINAL_STATIC_BOOL_V1", updated)
        self.assertIn("CustomSettings", updated)
        self.assertIn("@wrapMethod(WorldMapTooltipController)", updated)
        self.assertNotIn("@wrapMethod(BaseMinimapMappinController)", updated)

    def test_unrelated_custom_wrapper_work_narrow_refusal(self):
        source = SOURCE.replace(
            "  wrappedMethod();\n  if IsDefined(FEPlaces.PinData(this.GetMappin()))",
            "  wrappedMethod();\n  this.CustomOnEachMinimapUpdate();\n"
            "  if IsDefined(FEPlaces.PinData(this.GetMappin()))", 1)
        # Add the extra effect only in the global wrapper, not the other hooks.
        source = SOURCE.replace(WRAPPER,
            WRAPPER.replace("  wrappedMethod();",
                "  wrappedMethod();\n  this.CustomOnEachMinimapUpdate();"))
        report, output = self.generate(source)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"], "FE_MINIMAP_UPDATE_HAS_OTHER_EFFECTS")
        self.assertIsNone(output)

    def test_missing_icon_fallback_refuses_only_this_operation(self):
        source = SOURCE.replace(
            "@wrapMethod(MinimapPOIMappinController)",
            "@wrapMethod(OtherMappinController)")
        report, output = self.generate(source)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"],
                         "FE_MINIMAP_TARGETED_STYLING_UNPROVEN")
        self.assertIsNone(output)

    def test_modified_icon_method_without_tint_refuses(self):
        source = SOURCE.replace(
            "  inkWidgetRef.SetTintColor(this.iconWidget, FEPlaces.PinTint());\n"
            "}\n@wrapMethod(BaseWorldMapMappinController)",
            "  this.OtherRenderOnly();\n}\n"
            "@wrapMethod(BaseWorldMapMappinController)")
        report, output = self.generate(source)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertIsNone(output)

    def test_global_wrapper_with_existing_generic_work_is_not_deleted(self):
        source = SOURCE.replace(
            "protected func Update() -> Void {\n  wrappedMethod();",
            "protected func Update() -> Void {\n"
            "  // GCO-GENERIC:SOME_OTHER_RULE_V1\n  wrappedMethod();")
        report, output = self.generate(source)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"],
                         "FE_MINIMAP_UPDATE_CONTAINS_GENERIC_WORK")
        self.assertIsNone(output)

    def test_missing_owner_does_not_emit_a_mod(self):
        report, output = self.generate(
            "module OtherMod\npublic class OtherClass {}\n")
        self.assertEqual(report["status"], "NOT_APPLICABLE")
        self.assertIsNone(output)

if __name__ == "__main__":
    unittest.main()
