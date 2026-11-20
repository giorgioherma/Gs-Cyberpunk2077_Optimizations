"""Dark Future per-activation immutable tag membership semantic contracts.

The high-cost animation/buff-list cadence remains unchanged. These tests
verify output semantics, partial completion, compatible custom source, narrow
refusal, idempotence and that all existing animation calls are preserved.
The dedicated Phase 2 checkpoint independently runs official SCC over all
2,331 post-generic sources plus combined semantic output.
"""
from pathlib import Path
import json
import subprocess
import tempfile
import unittest

REPO = Path(__file__).resolve().parents[2]
DLL = REPO / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/Dark Future/UI/DFBaseGameUIOverrides.reds"

STOCK = """import DarkFuture.Settings.*
@addField(SingleCooldownManager)
private let m_gameplayTags: array<CName>;

@wrapMethod(SingleCooldownManager)
public final func ActivateCooldown(buffData: UIBuffInfo) -> Void {
    wrappedMethod(buffData);
    this.m_gameplayTags =
        TweakDBInterface.GetStatusEffectRecord(buffData.buffID).GameplayTags();
    let statusEffectIconCanvas: ref<inkCanvas> =
        inkWidgetRef.Get(this.m_stackCount).GetParentWidget() as inkCanvas;
    if IsDefined(statusEffectIconCanvas) {
        statusEffectIconCanvas.SetVAlign(inkEVerticalAlign.Top);
    }
}

@wrapMethod(SingleCooldownManager)
private final func SetTimeRemaining(time: Float) -> Void {
    if time == -1.0 {
        inkTextRef.SetText(this.m_timeRemaining, "");
        inkWidgetRef.Get(this.m_sprite).SetEffectParamValue(
            inkEffectType.LinearWipe, n"LinearWipe_0", n"transition", AbsF(1.0));
    } else {
        wrappedMethod(time);
    }
}

@replaceMethod(SingleCooldownManager)
public final func Update(timeLeft: Float, stackCount: Uint32) -> Void {
    let fraction: Float;
    let updatedSize: Float;
    if timeLeft <= 0.01 &&
        !ArrayContains(this.m_gameplayTags, n"DarkFutureInfiniteDurationEffect") {
        updatedSize = 0.00;
        this.GetRootWidget().SetVisible(false);
    } else {
        this.GetRootWidget().SetVisible(true);
        if timeLeft == -1.00 {
            fraction = 1.00;
        } else {
            fraction = timeLeft / this.m_initialDuration;
        }
        updatedSize = fraction;
    };
    inkWidgetRef.Get(this.m_sprite).SetEffectParamValue(
        inkEffectType.LinearWipe, n"LinearWipe_0", n"transition", AbsF(updatedSize));
    this.SetTimeRemaining(timeLeft);
    this.SetStackCount(Cast<Int32>(stackCount));
    if timeLeft <= this.m_outroDuration {
        this.FillOutroAnimationStart();
    };
}
"""

F_VALID = """@addField(SingleCooldownManager)
private let m_gcoDfInfiniteTagValid: Bool;
"""
F_CACHED = """@addField(SingleCooldownManager)
private let m_gcoDfInfiniteTagCached: Bool;
"""
INVALIDATE = "this.m_gcoDfInfiniteTagValid = false;"
CACHE = ('this.m_gcoDfInfiniteTagCached = ArrayContains(this.m_gameplayTags, '
         'n"DarkFutureInfiniteDurationEffect");')
VALIDATE = "this.m_gcoDfInfiniteTagValid = true;"


class DarkFutureSemanticContracts(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.counter = 0

    def call(self, text, path=TARGET, extra_files=None, base=None):
        self.counter += 1
        game = self.root / f"source-{self.counter}"
        original = game / path
        original.parent.mkdir(parents=True, exist_ok=True)
        original.write_text(text, encoding="utf8")
        for rel, value in (extra_files or {}).items():
            other = game / rel
            other.parent.mkdir(parents=True, exist_ok=True)
            other.write_text(value, encoding="utf8")
        output = self.root / f"stage-{self.counter}"
        argv = ["dotnet", str(DLL), "phase2-dark-future",
                "--game", str(game), "--out", str(output)]
        if base is not None:
            argv += ["--base-overlay", str(base)]
        result = subprocess.run(argv, text=True, capture_output=True, timeout=60)
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = json.loads(
            (output / "phase2-result.json").read_text("utf8"))
        self.assertEqual(original.read_text("utf8"), text)
        staged = output / path
        return report, staged.read_text("utf8") if staged.exists() else None

    def complete(self, text, original=STOCK):
        self.assertEqual(text.count("private let m_gcoDfInfiniteTagValid: Bool;"), 1)
        self.assertEqual(text.count("private let m_gcoDfInfiniteTagCached: Bool;"), 1)
        self.assertEqual(text.count(INVALIDATE), 1)
        self.assertEqual(text.count(CACHE), 1)
        self.assertEqual(text.count(VALIDATE), 1)
        self.assertIn("!this.m_gcoDfInfiniteTagCached", text)
        self.assertIn("!ArrayContains(this.m_gameplayTags", text)
        self.assertEqual(text.count("this.FillOutroAnimationStart();"),
                         original.count("this.FillOutroAnimationStart();"))
        self.assertEqual(text.count("this.SetTimeRemaining(timeLeft);"),
                         original.count("this.SetTimeRemaining(timeLeft);"))
        self.assertEqual(text.count("this.SetStackCount(Cast<Int32>(stackCount));"),
                         original.count("this.SetStackCount(Cast<Int32>(stackCount));"))
        self.assertIn("statusEffectIconCanvas.SetVAlign", text)

    def test_stock_one_generation_complete_repeat_noop(self):
        report, generated = self.call(STOCK)
        self.assertEqual(report["status"], "APPLIED")
        self.assertEqual(report["appliedEdits"], 1)
        self.complete(generated)
        self.assertLess(generated.index(INVALIDATE),
                        generated.index("wrappedMethod(buffData);"))
        self.assertLess(generated.index("this.m_gameplayTags ="),
                        generated.index(CACHE))
        self.assertLess(generated.index(CACHE), generated.index(VALIDATE))
        again, staged = self.call(generated)
        self.assertEqual(again["status"], "COMPLETE")
        self.assertEqual(again["appliedEdits"], 0)
        self.assertIsNone(staged)

    def test_all_single_partials_and_pairs_finish_without_duplicate(self):
        report, full = self.call(STOCK)
        self.assertEqual(report["status"], "APPLIED")
        for absent in [
            [F_VALID], [F_CACHED], [INVALIDATE], [CACHE], [VALIDATE],
            [F_VALID, F_CACHED], [INVALIDATE, CACHE],
            [CACHE, VALIDATE],
        ]:
            with self.subTest(absent=absent):
                partial = full
                for part in absent:
                    partial = partial.replace(part, "")
                result, transformed = self.call(partial)
                self.assertEqual(result["status"], "APPLIED",
                                 result)
                self.complete(transformed, partial)
                again, restaged = self.call(transformed)
                self.assertEqual(again["status"], "COMPLETE")
                self.assertIsNone(restaged)

    def test_compatible_newer_source_modified_comments_and_filename(self):
        custom = STOCK.replace(
            "    wrappedMethod(buffData);",
            "    this.BeforeCustomAudit();\n    wrappedMethod(buffData);")
        custom = custom.replace(
            "    let fraction: Float;",
            "    let newVersionFlag: Bool = true;\n    let fraction: Float;")
        custom = custom.replace(
            '@wrapMethod(SingleCooldownManager)\npublic final func ActivateCooldown',
            '@wrapMethod ( SingleCooldownManager )\npublic final func ActivateCooldown')
        custom = custom.replace(
            "private let m_gameplayTags: array<CName>;",
            "private let m_gameplayTags: array<CName>;\n"
            "// Local custom user source must be preserved.")
        result, generated = self.call(
            custom, "r6/scripts/Dark Future/UI/Modified2027.reds")
        self.assertEqual(result["status"], "APPLIED")
        self.assertIn("this.BeforeCustomAudit();", generated)
        self.assertIn("let newVersionFlag: Bool = true;", generated)
        self.assertIn("Local custom user source must be preserved", generated)
        again, changed = self.call(
            generated, "r6/scripts/Dark Future/UI/Modified2027.reds")
        self.assertEqual(again["status"], "COMPLETE")
        self.assertIsNone(changed)

    def test_compatible_owner_moved_to_custom_folder(self):
        new_path = "r6/scripts/Custom_DarkFuture_2027/UI/Overrides.reds"
        result, optimized = self.call(STOCK, new_path)
        self.assertEqual(result["status"], "APPLIED")
        self.assertEqual(result["sourcePath"], new_path)
        self.complete(optimized)
        repeated, staged = self.call(optimized, new_path)
        self.assertEqual(repeated["status"], "COMPLETE")
        self.assertIsNone(staged)

    def test_equivalent_postgeneric_overlay_used_not_overwritten(self):
        extra = "r6/scripts/Dark Future/UI/Another.reds"
        base = self.root / "prior-generic"
        previous = base / TARGET
        previous.parent.mkdir(parents=True)
        previous.write_text(STOCK.replace(
            "    let fraction: Float;",
            "    // GCO-GENERIC:AN_APPROVED_OTHER_RULE\n"
            "    let fraction: Float;"), encoding="utf8")
        result, staged = self.call(STOCK, extra_files={
            extra: "// Other author-owned source.\n"
        }, base=base)
        self.assertEqual(result["status"], "APPLIED")
        self.assertIn("GCO-GENERIC:AN_APPROVED_OTHER_RULE", staged)
        self.complete(staged)

    def test_external_writer_refuses_affected_operation_only(self):
        unrelated = (
            "@wrapMethod(SingleCooldownManager)\n"
            "public final func OtherMethod() -> Void {\n"
            "    this.m_gameplayTags = NewTags();\n}\n"
        )
        result, out = self.call(STOCK, extra_files={
            "r6/scripts/SomeOtherMod/Custom.reds": unrelated
        })
        self.assertEqual(result["status"], "REFUSED")
        self.assertEqual(result["reason"],
                         "DARK_FUTURE_EXTERNAL_TAG_MUTATION_OR_ESCAPE_UNPROVEN")
        self.assertIsNone(out)

    def test_external_reader_is_not_a_refusal(self):
        unrelated = (
            "@wrapMethod(SingleCooldownManager)\n"
            "public final func OtherMethod() -> Void {\n"
            "    let size = ArraySize(this.m_gameplayTags);\n}\n"
        )
        report, out = self.call(STOCK, extra_files={
            "r6/scripts/SomeOtherMod/Custom.reds": unrelated
        })
        self.assertEqual(report["status"], "APPLIED")
        self.complete(out)

    def test_custom_tag_array_alias_escape_refuses_safely(self):
        unproven = STOCK.replace(
            "    let fraction: Float;",
            "    let externalAlias = this.m_gameplayTags;\n"
            "    let fraction: Float;")
        result, staged = self.call(unproven)
        self.assertEqual(result["status"], "REFUSED")
        self.assertEqual(result["reason"],
                         "DARK_FUTURE_TAG_ARRAY_MUTATION_OR_ESCAPE_UNPROVEN")
        self.assertIsNone(staged)

    def test_changed_infinite_literal_refused_without_touching(self):
        changed = STOCK.replace('"DarkFutureInfiniteDurationEffect"',
                                '"CustomDynamicTag"')
        result, out = self.call(changed)
        self.assertEqual(result["status"], "REFUSED")
        self.assertEqual(result["reason"],
                         "DARK_FUTURE_COOLDOWN_TARGET_UNRECOGNIZED")
        self.assertIsNone(out)

    def test_one_generate_composes_generic_footsteps_and_dark_future(self):
        from test_phase2_footsteps import STOCK as dv_stock
        from test_phase2_footsteps import TARGET as dv_target

        game = self.root / "combined-game"
        footstep = game / dv_target
        dark = game / TARGET
        footstep.parent.mkdir(parents=True)
        dark.parent.mkdir(parents=True)
        footstep.write_text(dv_stock, encoding="utf8")
        dark.write_text(STOCK, encoding="utf8")
        provider = game / "r6/scripts/Settings/Flags.reds"
        consumer = game / "r6/scripts/Consumer/Example.reds"
        provider.parent.mkdir(parents=True)
        consumer.parent.mkdir(parents=True)
        provider.write_text("""module TestSettings
public class Settings {
  public final static func Active() -> Bool { return true; }
  public final static func Verbose() -> Bool { return false; }
}
""", encoding="utf8")
        consumer.write_text("""module Example
import TestSettings.*
public class Consumer extends IScriptable {
  public func Check() -> Bool {
    return Settings.Active() && Settings.Verbose();
  }
}
""", encoding="utf8")
        plan = self.root / "full-plan.json"
        capture = self.root / "full-analysis.json"
        output = self.root / "full-pass"
        p = subprocess.run(["dotnet", str(DLL), "plan-redscript",
                            "--game", str(game), "--out", str(plan)],
                           text=True, capture_output=True, timeout=60)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        manifest = json.loads(plan.read_text("utf8"))
        self.assertEqual(manifest["implementedRuleCount"], 10)
        capture.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {
                "sourceStackSha256": manifest["sourceStackSha256"]
            }
        }), encoding="utf8")
        p = subprocess.run(["dotnet", str(DLL), "stage-redscript-plan",
                            "--game", str(game), "--plan", str(plan),
                            "--analysis", str(capture), "--out", str(output)],
                           text=True, capture_output=True, timeout=80)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        report = json.loads((output / "stage-result.json").read_text("utf8"))
        rule_ids = [item["id"] for item in report["completed"]]
        self.assertIn("pure-constant-slices", rule_ids)
        self.assertIn("dvcore-footsteps", rule_ids)
        self.assertIn("dark-future-infinite-tag", rule_ids)
        self.assertEqual(11, report["completed"][-2]["order"])
        self.assertEqual(12, report["completed"][-1]["order"])
        self.assertEqual(1, report["completed"][-1]["rewrittenExpressions"])
        generated = output / "effective-preview"
        self.complete((generated / TARGET).read_text("utf8"))
        dv_text = (generated / dv_target).read_text("utf8")
        self.assertIn("this.m_audioClass = hit;", dv_text)
        generic_text = (
            generated / "r6/scripts/Consumer/Example.reds"
        ).read_text("utf8")
        self.assertIn("GCO-GENERIC:", generic_text)
        self.assertEqual(STOCK, dark.read_text("utf8"))
        self.assertEqual(dv_stock, footstep.read_text("utf8"))

        # Reuse a source with just the Dark Future cache fields present.
        partial = STOCK.replace(
            "private let m_gameplayTags: array<CName>;",
            "private let m_gameplayTags: array<CName>;\n" +
            F_VALID + F_CACHED)
        dark.write_text(partial, encoding="utf8")
        next_plan = self.root / "partial-plan.json"
        next_analysis = self.root / "partial-analysis.json"
        next_stage = self.root / "partial-pass"
        q = subprocess.run(["dotnet", str(DLL), "plan-redscript",
                            "--game", str(game), "--out", str(next_plan)],
                           text=True, capture_output=True, timeout=60)
        self.assertEqual(q.returncode, 0, q.stderr)
        m = json.loads(next_plan.read_text("utf8"))
        next_analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {
                "sourceStackSha256": m["sourceStackSha256"]
            }
        }), encoding="utf8")
        q = subprocess.run(["dotnet", str(DLL), "stage-redscript-plan",
                            "--game", str(game), "--plan", str(next_plan),
                            "--analysis", str(next_analysis),
                            "--out", str(next_stage)],
                           text=True, capture_output=True, timeout=80)
        self.assertEqual(q.returncode, 0, q.stdout + q.stderr)
        self.complete((next_stage / "effective-preview" /
                       TARGET).read_text("utf8"), partial)

    def test_missing_mod_is_not_applicable(self):
        result, out = self.call(
            "public class UnrelatedMod {}\n",
            "r6/scripts/Unrelated/Example.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE")
        self.assertIsNone(out)


if __name__ == "__main__":
    unittest.main()
