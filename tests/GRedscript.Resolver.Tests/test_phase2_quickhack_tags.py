"""Historical Quickhack_Fixes pass-7 tag predicate contracts.

The original temporary StatusEffect array must be read only by the tag
presence guard; otherwise this operation is narrowly refused. No copies
from historical complete files and no edits to the installed source.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PATH = "r6/scripts/Quickhack_Fixes/sonic_shock.reds"
STOCK = """@wrapMethod(ReactionManagerComponent)
private final func ShouldStimBeProcessed(stimEvent: ref<StimuliEvent>, delayed: Bool) -> Bool {
    // custom comment must remain
    let sonic_shock_ignore_effects: array<ref<StatusEffect>>;
    let ally = stimEvent.sourceObject;
    StatusEffectHelper.GetAppliedEffectsWithTag(ally, n"CommsNoiseIgnore", sonic_shock_ignore_effects);
    if ArraySize(sonic_shock_ignore_effects) > 0 {
        return false;
    };
    return wrappedMethod(stimEvent, delayed);
}

@wrapMethod(ReactionManagerComponent)
private final func HelpAlly(ally: wref<GameObject>, targetOfAlly: wref<GameObject>, onlyAlertNoThreat: Bool, opt dontTrySquad: Bool) -> Void {
    let sonic_shock_ignore_effects: array<ref<StatusEffect>>;
    StatusEffectHelper.GetAppliedEffectsWithTag(ally, n"CommsNoiseIgnore", sonic_shock_ignore_effects);
    if ArraySize(sonic_shock_ignore_effects) > 0 {
        return;
    };
    wrappedMethod(ally, targetOfAlly, onlyAlertNoThreat, dontTrySquad);
}
"""

HISTORICAL_CHECK = 'if IsDefined(ally) && StatusEffectSystem.ObjectHasStatusEffectWithTag(ally, n"CommsNoiseIgnore") {'


class QuickhackTagTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.home = Path(self.temp.name)
        self.number = 0

    def generate(self, source=None, relative=PATH, overlay=None):
        self.number += 1
        game = self.home / f"g{self.number}"
        scripts = game / "r6/scripts"
        scripts.mkdir(parents=True, exist_ok=True)
        file = game / relative
        if source is not None:
            file.parent.mkdir(parents=True, exist_ok=True)
            file.write_text(source, encoding="utf8")
        out = self.home / f"out{self.number}"
        args = ["dotnet", str(DLL), "phase2-quickhack-tags",
                "--game", str(game), "--out", str(out)]
        if overlay is not None:
            args += ["--base-overlay", str(overlay)]
        p = subprocess.run(args, capture_output=True, text=True, timeout=80)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        if source is not None:
            self.assertEqual(file.read_text("utf8"), source)
        result = out / relative
        return report, result.read_text("utf8") if result.exists() else None

    def test_stock_applies_twice_and_idempotent(self):
        report, output = self.generate(STOCK)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 2)
        self.assertEqual(output.count(HISTORICAL_CHECK), 2)
        self.assertEqual(output.count("wrappedMethod("), 2)
        self.assertIn("custom comment must remain", output)
        self.assertNotIn("GetAppliedEffectsWithTag(", output)
        self.assertNotIn("ArraySize(sonic_shock_ignore_effects)", output)
        report2, output2 = self.generate(output)
        self.assertEqual(report2["status"], "COMPLETE", report2)
        self.assertEqual(report2["alreadyComplete"], 2)
        self.assertIsNone(output2)

    def test_partial_then_complete_without_duplicate(self):
        report, output = self.generate(STOCK)
        first = STOCK.index("@wrapMethod(ReactionManagerComponent)")
        second = STOCK.index("@wrapMethod(ReactionManagerComponent)", first + 1)
        generated_second = output.index("@wrapMethod(ReactionManagerComponent)",
                                        output.index("@wrapMethod(ReactionManagerComponent)") + 1)
        partial = STOCK[:second] + output[generated_second:]
        state, updated = self.generate(partial)
        self.assertEqual(state["status"], "APPLIED", state)
        self.assertEqual(state["appliedEdits"], 1)
        self.assertEqual(state["alreadyComplete"], 1)
        self.assertEqual(updated.count(HISTORICAL_CHECK), 2)
        self.assertEqual(self.generate(updated)[0]["status"], "COMPLETE")

    def test_moved_custom_source_with_comments_preserved(self):
        edited = STOCK.replace(
            "private final func ShouldStimBeProcessed",
            "private /* safe extra comment */ final func ShouldStimBeProcessed")
        edited += "\npublic static func UserCustomHelper() -> Int32 { return 17; }\n"
        report, output = self.generate(
            edited, "r6/scripts/Quickhack_Fixes/AnotherVersion/renamed.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("UserCustomHelper()", output)
        self.assertIn("safe extra comment", output)

    def test_extra_effect_consumer_narrowly_refused(self):
        unsafe = STOCK.replace(
            "    if ArraySize(sonic_shock_ignore_effects) > 0 {",
            "    this.ObserveEffects(sonic_shock_ignore_effects);\n"
            "    if ArraySize(sonic_shock_ignore_effects) > 0 {",
            1)
        result, output = self.generate(unsafe)
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(result["appliedEdits"], 1)
        self.assertIn("QHF_TEMP_ARRAY_HAS_OTHER_READERS", result["reason"])
        self.assertEqual(output.count(HISTORICAL_CHECK), 1)
        self.assertIn("this.ObserveEffects(sonic_shock_ignore_effects)", output)

    def test_mod_absent_is_not_applicable(self):
        result, output = self.generate("public class Unrelated {}",
            "r6/scripts/OtherMod/other.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE", result)
        self.assertIsNone(output)

    def test_unproven_wrapped_fallback_skips_only_that_method(self):
        unsafe = STOCK.replace("    return wrappedMethod(stimEvent, delayed);",
                               "    return true;")
        state, output = self.generate(unsafe)
        self.assertEqual(state["status"], "APPLIED", state)
        self.assertEqual(state["appliedEdits"], 1)
        self.assertIn("QHF_WRAPPED_METHOD_MISSING", state["reason"])
        self.assertIn("GetAppliedEffectsWithTag(ally", output)
        self.assertEqual(output.count(HISTORICAL_CHECK), 1)


if __name__ == "__main__":
    unittest.main()
