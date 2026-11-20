"""DigitalVixenCore Footsteps semantic contract: stock/partial/complete/
custom/drift source, minimal source edits and repeat-generation idempotence.

These are source-shaped tests of the real .NET 10 semantic generator.
Actual combined 2,331-source official SCC runs in the Phase 2 checkpoint.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/DigitalVixenCore/DVCore_Footsteps.reds"

STOCK = """module DigitalVixenCore

public class DVFootstepsService extends ScriptableService {
  private let m_carriers: array<ref<DVFootstepCarrier>>;
  private let m_warnedNoCooked: Bool;

  private func FindAudioClass() -> ref<ReflectionClass> {
    let classes = Reflection.GetClasses();
    let i: Int32 = 0;
    let hit: ref<ReflectionClass>;
    while i < ArraySize(classes) && !IsDefined(hit) {
      if IsDefined(classes[i].GetFunction(n"EquipNewFootwearByPlayer")) { hit = classes[i]; }
      i += 1;
    }
    return hit;
  }

  private func EquipAs(gi: GameInstance, itemRecord: String) -> Bool {
    let cls = this.FindAudioClass();
    return IsDefined(cls);
  }
}
"""

HISTORICAL = """module DigitalVixenCore
public class DVFootstepsService extends ScriptableService {
  private let m_audioClass: ref<ReflectionClass>;
  private let m_warnedNoCooked: Bool;
  private func FindAudioClass() -> ref<ReflectionClass> {
    if IsDefined(this.m_audioClass) {
      return this.m_audioClass;
    }
    let classes = Reflection.GetClasses();
    let i: Int32 = 0;
    while i < ArraySize(classes) {
      if IsDefined(classes[i].GetFunction(n"EquipNewFootwearByPlayer")) {
        this.m_audioClass = classes[i];
        return this.m_audioClass;
      }
      i += 1;
    }
    return null;
  }
}
"""


class FootstepsSemanticContracts(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.counter = 0

    def run_one(self, source, name="DVCore_Footsteps.reds"):
        self.counter += 1
        game = self.root / f"game-{self.counter}"
        file = game / TARGET.replace("DVCore_Footsteps.reds", name)
        file.parent.mkdir(parents=True)
        file.write_text(source, encoding="utf-8")
        out = self.root / f"generated-{self.counter}"
        proc = subprocess.run(
            ["dotnet", str(DLL), "phase2-footsteps", "--game", str(game),
             "--out", str(out)], capture_output=True, text=True, timeout=50)
        self.assertEqual(proc.returncode, 0,
                         proc.stdout + "\n" + proc.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf-8"))
        original = file.read_text("utf-8")
        self.assertEqual(original, source)
        staged = out / "r6/scripts/DigitalVixenCore" / name
        return report, staged.read_text("utf-8") if staged.exists() else None

    def assert_complete(self, source):
        self.assertEqual(1, source.count("private let m_audioClass: ref<ReflectionClass>;"))
        self.assertEqual(1, source.count("if IsDefined(this.m_audioClass)"))
        self.assertEqual(1, source.count("this.m_audioClass = hit;"))
        self.assertEqual(1, source.count("return hit;"))
        self.assertIn('GetFunction(n"EquipNewFootwearByPlayer")', source)

    def test_stock_one_run_complete_and_repeat_zero_edits(self):
        result, value = self.run_one(STOCK)
        self.assertEqual(result["status"], "APPLIED")
        self.assertEqual(result["appliedEdits"], 1)
        self.assertTrue(result["sourceUnchanged"])
        self.assert_complete(value)
        self.assertIn("private func EquipAs", value)
        second, changed = self.run_one(value)
        self.assertEqual(second["status"], "COMPLETE")
        self.assertEqual(second["appliedEdits"], 0)
        self.assertIsNone(changed)

    def test_partials_field_guard_and_store_individually_complete(self):
        field = "  private let m_audioClass: ref<ReflectionClass>;\n"
        guard = ("    if IsDefined(this.m_audioClass) {\n"
                 "      return this.m_audioClass;\n"
                 "    }\n")
        write = ("    if IsDefined(hit) {\n"
                 "      this.m_audioClass = hit;\n"
                 "    }\n")
        for flags in range(1, 7):
            with self.subTest(flags=flags):
                source = STOCK
                if flags & 1:
                    source = source.replace("  private let m_warnedNoCooked: Bool;",
                        field + "  private let m_warnedNoCooked: Bool;")
                if flags & 2:
                    source = source.replace("    let classes = Reflection.GetClasses();",
                        guard + "    let classes = Reflection.GetClasses();")
                if flags & 4:
                    source = source.replace("    return hit;", write + "    return hit;")
                result, staged = self.run_one(source)
                self.assertEqual(result["status"], "APPLIED")
                self.assert_complete(staged)
                again, restaged = self.run_one(staged)
                self.assertEqual(again["status"], "COMPLETE")
                self.assertIsNone(restaged)

    def test_historical_complete_requires_zero_changes(self):
        result, transformed = self.run_one(HISTORICAL)
        self.assertEqual(result["status"], "COMPLETE")
        self.assertEqual(result["appliedEdits"], 0)
        self.assertIsNone(transformed)

    def test_newer_custom_reordered_method_renamed_locals_preserved(self):
        source = STOCK.replace("module DigitalVixenCore",
            "module /* custom layout */\n  DigitalVixenCore")
        source = source.replace("class DVFootstepsService",
            "class\n  DVFootstepsService")
        source = source.replace("let classes = Reflection.GetClasses();",
            "let available = Reflection.GetClasses();")
        source = source.replace("ArraySize(classes)", "ArraySize(available)")
        source = source.replace("classes[i]", "available[i]")
        source = source.replace("let hit: ref<ReflectionClass>;",
            "let foundClass: ref<ReflectionClass>;")
        source = source.replace("IsDefined(hit)", "IsDefined(foundClass)")
        source = source.replace("hit = available[i]", "foundClass = available[i]")
        source = source.replace("return hit;", "return foundClass;")
        source = source.replace("  private func FindAudioClass()",
            "  public func AddedByUser() -> Int32 { return 37; }\n\n"
            "  private let extraVersionSetting: Bool;\n\n"
            "  private func FindAudioClass()")
        source = source.replace("    let available = Reflection.GetClasses();",
            '    this.CustomAudit();\n    let available = Reflection.GetClasses();')
        result, staged = self.run_one(source, "Footsteps_2027.reds")
        self.assertEqual(result["status"], "APPLIED")
        self.assertIn("public func AddedByUser()", staged)
        self.assertIn("private let extraVersionSetting: Bool;", staged)
        self.assertIn("this.CustomAudit();", staged)
        self.assertIn("let available = Reflection.GetClasses();", staged)
        self.assertEqual(1, staged.count("this.m_audioClass = foundClass;"))
        self.assertEqual(1, staged.count("return foundClass;"))
        again, changed = self.run_one(staged, "Footsteps_2027.reds")
        self.assertEqual(again["status"], "COMPLETE")
        self.assertIsNone(changed)

    def test_fully_optimized_markerless_custom_cache_field_name(self):
        complete = HISTORICAL.replace("m_audioClass", "m_footwearReflection")
        report, staged = self.run_one(complete)
        self.assertEqual(report["status"], "COMPLETE")
        self.assertIsNone(staged)

    def test_unrelated_reflection_member_is_not_reused_as_cache(self):
        source = STOCK.replace("  private let m_warnedNoCooked: Bool;",
            "  private let m_unrelatedClass: ref<ReflectionClass>;\n"
            "  private let m_warnedNoCooked: Bool;")
        source = source.replace("    let classes = Reflection.GetClasses();",
            "    let previous = this.m_unrelatedClass;\n"
            "    let classes = Reflection.GetClasses();")
        result, staged = self.run_one(source)
        self.assertEqual(result["status"], "APPLIED")
        self.assert_complete(staged)
        self.assertIn("let previous = this.m_unrelatedClass;", staged)
        self.assertNotIn("return this.m_unrelatedClass", staged)

    def test_incompatible_target_refuses_only_footsteps(self):
        bad = STOCK.replace('n"EquipNewFootwearByPlayer"',
                            'n"OtherTargetFunction"')
        report, staged = self.run_one(bad)
        self.assertEqual(report["status"], "REFUSED")
        self.assertEqual(report["appliedEdits"], 0)
        self.assertEqual(report["reason"],
                         "DVCORE_RTTI_SUCCESS_BRANCH_INCOMPATIBLE")
        self.assertIsNone(staged)

    def test_one_gco_stage_from_ungeneric_stock_applies_generic_and_semantic(self):
        game = self.root / "full-stack"
        dv = game / TARGET
        dv.parent.mkdir(parents=True)
        dv.write_text(STOCK, encoding="utf8")
        provider = game / "r6/scripts/Settings/Static.reds"
        consumer = game / "r6/scripts/Consumer/Example.reds"
        provider.parent.mkdir(parents=True)
        consumer.parent.mkdir(parents=True)
        provider.write_text("""module TestReusableSettings
public class ReusableSettings {
  public final static func Enabled() -> Bool { return true; }
  public final static func Diagnostics() -> Bool { return false; }
}
""", encoding="utf8")
        consumer.write_text("""module GenericConsumer
import TestReusableSettings.*
public class IndependentAuthor extends IScriptable {
  public func OnAction(gi: GameInstance) -> Bool {
    return ReusableSettings.Enabled() &&
           ReusableSettings.Diagnostics() &&
           GameInstance.GetQuestsSystem(gi).GetFact(n"test") == 1;
  }
}
""", encoding="utf8")
        plan = self.root / "all-plan.json"
        analysis = self.root / "all-capture.json"
        output = self.root / "all-generated"
        p = subprocess.run(["dotnet", str(DLL), "plan-redscript",
                            "--game", str(game), "--out", str(plan)],
                           text=True, capture_output=True, timeout=60)
        self.assertEqual(p.returncode, 0, p.stderr)
        manifest = json.loads(plan.read_text("utf8"))
        self.assertEqual(manifest["implementedRuleCount"], 10)
        self.assertGreater(manifest["eligibleSourceEdits"], 0)
        analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance":
                        "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256":
                       manifest["sourceStackSha256"]}
        }), encoding="utf8")
        run = subprocess.run(["dotnet", str(DLL), "stage-redscript-plan",
                             "--game", str(game), "--plan", str(plan),
                             "--analysis", str(analysis), "--out", str(output)],
                             text=True, capture_output=True, timeout=60)
        self.assertEqual(run.returncode, 0, run.stderr + run.stdout)
        report = json.loads((output / "stage-result.json").read_text("utf8"))
        families = [x["id"] for x in report["completed"]]
        self.assertIn("pure-constant-slices", families)
        self.assertIn("dvcore-footsteps", families)
        self.assertEqual(11, report["completed"][-1]["order"])
        self.assertEqual(1, report["completed"][-1]["rewrittenExpressions"])
        optimized = (output / "effective-preview" / TARGET).read_text("utf8")
        self.assert_complete(optimized)
        self.assertEqual(dv.read_text("utf8"), STOCK)
        consumerOutput = output / "effective-preview/r6/scripts/Consumer/Example.reds"
        self.assertTrue(consumerOutput.exists())
        self.assertIn("GCO-GENERIC:", consumerOutput.read_text("utf8"))

        # Source-aware Phase 2 must compose from an existing generic
        # preview even when its target file has not been staged yet.
        genericBase = self.root / "generic-only-preview"
        genericFile = genericBase / "r6/scripts/Consumer/Example.reds"
        genericFile.parent.mkdir(parents=True)
        genericFile.write_bytes(consumerOutput.read_bytes())
        baseOutput = self.root / "with-generic-base"
        baseRun = subprocess.run(
            ["dotnet", str(DLL), "phase2-footsteps",
             "--game", str(game), "--base-overlay", str(genericBase),
             "--out", str(baseOutput)], text=True,
             capture_output=True, timeout=60)
        self.assertEqual(baseRun.returncode, 0,
                         baseRun.stderr + baseRun.stdout)
        baseResult = json.loads(
            (baseOutput / "phase2-result.json").read_text("utf8"))
        self.assertEqual(baseResult["status"], "APPLIED")
        self.assert_complete(
            (baseOutput / TARGET).read_text("utf8"))

        # A partial real owner file must complete in the same Generate,
        # rather than requiring a historical pass before GCO stage.
        partial = STOCK.replace("  private let m_warnedNoCooked: Bool;",
            "  private let m_audioClass: ref<ReflectionClass>;\n"
            "  private let m_warnedNoCooked: Bool;")
        dv.write_text(partial, encoding="utf8")
        againPlan = self.root / "partial-plan.json"
        againCapture = self.root / "partial-capture.json"
        againOutput = self.root / "partial-generated"
        q = subprocess.run(["dotnet", str(DLL), "plan-redscript",
                            "--game", str(game), "--out", str(againPlan)],
                           text=True, capture_output=True, timeout=60)
        self.assertEqual(q.returncode, 0, q.stderr)
        m = json.loads(againPlan.read_text("utf8"))
        againCapture.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance":
                        "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": m["sourceStackSha256"]}
        }), encoding="utf8")
        stage = subprocess.run(["dotnet", str(DLL), "stage-redscript-plan",
                                "--game", str(game), "--plan", str(againPlan),
                                "--analysis", str(againCapture),
                                "--out", str(againOutput)],
                                text=True, capture_output=True, timeout=60)
        self.assertEqual(stage.returncode, 0, stage.stderr + stage.stdout)
        result = json.loads((againOutput / "stage-result.json").read_text("utf8"))
        self.assertIn("dvcore-footsteps", [x["id"] for x in result["completed"]])
        completed = (againOutput / "effective-preview" / TARGET).read_text("utf8")
        self.assert_complete(completed)

    def test_uninstalled_mod_does_not_create_any_source(self):
        result, staged = self.run_one(
            "module DigitalVixenCore\npublic class NotFootsteps {}\n")
        self.assertEqual(result["status"], "NOT_APPLICABLE")
        self.assertIsNone(staged)


if __name__ == "__main__":
    unittest.main()
