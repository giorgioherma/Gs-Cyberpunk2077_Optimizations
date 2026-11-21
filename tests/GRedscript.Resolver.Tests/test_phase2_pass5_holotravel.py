"""Pass 5 HoloTravel material-event source-state and integration contracts.

The production generator is exercised through an isolated .NET 10 harness.
Central semantic stage registration is intentionally untouched on this branch.
"""
import json
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
HARNESS = (ROOT / "tests/GRedscript.Resolver.Tests/Pass5Harness/bin/Release"
           / "net10.0/Pass5Harness.dll")
PROJECT = ROOT / "tests/GRedscript.Resolver.Tests/Pass5Harness/Pass5Harness.csproj"
TARGET = "r6/scripts/HQ_HoloTravel/HQ_HoloTravel.reds"

STOCK = r'''module EmreHQ.HoloTravel
public class HoloTravel { public let holo: ref<Holo>; }
public class BaseHandler {
    public let svc: ref<HoloTravel>;
    public let cfg: ref<Config>;
    protected func IsValid(e: ref<ResourceEvent>) -> Bool = IsDefined(this.svc.holo) && IsDefined(e);
    public func ResolveColor(color: Color) -> Color {
        switch this.cfg.colorOption {
            case ColorOptions.TrueColor: return color;
            case ColorOptions.Random: return this.svc.hlp.randomizedColor;
            case ColorOptions.Turquoise: return TurquoiseColor();
            case ColorOptions.Green: return GreenColor();
            default: return GreenColor();
        }
    }
}
public class ParticleHandler extends BaseHandler
{
    public let beam_baseRR: ResRef;
    public let flare_baseRR: ResRef;
    public static func New(service: ref<HoloTravel>) -> ref<ParticleHandler>
    {
        let self = new ParticleHandler();
        self.Init(service);
        self.beam_baseRR = HQRes("particle_beam_base.mt");
        self.flare_baseRR = HQRes("particle_flare_base.remt");
        BindCallbackTRL(self, n"OnUpdate", n"CMaterialInstance");
        return self;
    }
    private cb func OnUpdate(e: ref<ResourceEvent>)
    {
        if !this.IsValid(e) {
            return; }
        let mtl = e.GetResource() as CMaterialInstance;
        let resPath = ResourceRef.GetPath(mtl.baseMaterial);
        if resPath == this.beam_baseRR || resPath == this.flare_baseRR
        {
            let i = 0;
            while i < ArraySize(mtl.params)
            {
                let name = mtl.params[i].name;
                if Equals(name, n"Color") || Equals(name, n"Second_Color") {
                    mtl.params[i].data = this.ResolveColor(this.svc.holo.particleColor); }
                i += 1;
            }
        }
    }
    public func CustomUntouched() -> Void { this.LogCustom(); }
}
'''


class Pass5HoloTravelTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        result = subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                                 "--nologo"], capture_output=True, text=True,
                                timeout=120)
        if result.returncode:
            raise AssertionError(result.stdout + "\n" + result.stderr)
        if not HARNESS.is_file():
            raise AssertionError("PASS5_HARNESS_BINARY_MISSING")

    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.n = 0

    def generate(self, source=STOCK, path=TARGET, prior_overlay=None):
        self.n += 1
        game = self.root / f"game{self.n}"
        game_file = game / path
        game_file.parent.mkdir(parents=True, exist_ok=True)
        game_file.write_text(source, "utf8")
        output = self.root / f"output{self.n}"
        args = ["dotnet", str(HARNESS), str(game), str(output)]
        if prior_overlay:
            args.append(str(prior_overlay))
        process = subprocess.run(args, capture_output=True, text=True, timeout=120)
        self.assertEqual(process.returncode, 0,
                         process.stdout + "\n" + process.stderr)
        report = json.loads((output / "phase2-result.json").read_text("utf8"))
        self.last_output = output
        self.assertEqual(game_file.read_text("utf8"), source)
        staged = output / path
        return report, staged.read_text("utf8") if staged.exists() else None

    def check(self, text):
        self.assertIsNotNone(text)
        self.assertEqual(text.count("let color = this.ResolveColor("
                                    "this.svc.holo.particleColor);"), 1)
        self.assertEqual(text.count("mtl.params[i].data = color;"), 1)
        self.assertIn("if !IsDefined(e) {", text)
        self.assertIn("if !IsDefined(mtl) {", text)
        self.assertIn("if !IsDefined(this.svc.holo) {", text)
        self.assertIn("if resPath != this.beam_baseRR && "
                      "resPath != this.flare_baseRR", text)
        self.assertIn('BindCallbackTRL(self, n"OnUpdate", n"CMaterialInstance")',
                      text)
        self.assertIn("this.LogCustom();", text)
        self.assertNotIn("if !this.IsValid(e) {", text)
        self.assertNotIn("if resPath == this.beam_baseRR ||", text)
        self.assertNotIn("let color = color;", text)

    def test_stock_to_complete_and_repeated_noop(self):
        report, output = self.generate()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.check(output)
        second, no_output = self.generate(output)
        self.assertEqual(second["status"], "COMPLETE", second)
        self.assertIsNone(no_output)

    def test_partial_early_event_guard(self):
        partial = STOCK.replace("if !this.IsValid(e) {",
                                "if !IsDefined(e) {")
        r, output = self.generate(partial)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check(output)

    def test_partial_hoisted_color_declaration_without_loop_rewrite(self):
        partial = STOCK.replace(
            "            let i = 0;",
            "            let color = this.ResolveColor("
            "this.svc.holo.particleColor);\n            let i = 0;")
        r, output = self.generate(partial)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check(output)

    def test_partial_fastpath_missing_holo_guard_completed(self):
        _, completed = self.generate()
        partial = completed.replace(
            "if !IsDefined(this.svc.holo) {\n            return;\n        }", "")
        r, out = self.generate(partial)
        self.assertEqual(r["status"], "APPLIED", r)
        self.check(out)

    def test_relocated_custom_source_preserves_unrelated_modifications(self):
        custom = STOCK.replace(
            "    public func CustomUntouched()",
            "    private let myCustomFlag: Bool = true;\n"
            "    public func CustomUntouched()")
        custom = "// GCO-GENERIC:MY_EXISTING_RULE\n" + custom
        r, out = self.generate(
            custom, "r6/scripts/HQ_HoloTravel/custom/HoloTravel.new.reds")
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn("private let myCustomFlag: Bool = true;", out)
        self.assertIn("GCO-GENERIC:MY_EXISTING_RULE", out)
        self.check(out)

    def test_changed_base_material_filter_refuses_only_target(self):
        unsafe = STOCK.replace("resPath == this.flare_baseRR",
                               "resPath == this.someOtherRR")
        r, out = self.generate(unsafe)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertIn("HOLOTRAVEL_PARTIAL_EVENT_FLOW_UNPROVEN", r["reason"])
        self.assertIsNone(out)

    def test_modified_isvalid_dependency_refuses(self):
        unsafe = STOCK.replace("IsDefined(this.svc.holo) && IsDefined(e);",
                               "this.CheckAndAdvanceState(e);")
        r, out = self.generate(unsafe)
        self.assertEqual(r["status"], "REFUSED", r)
        self.assertIn("HOLOTRAVEL_VALIDATION_OR_COLOR_DEPENDENCY_CHANGED",
                      r["reason"])
        self.assertIsNone(out)

    def test_absent_owner_does_not_emit_mod(self):
        r, out = self.generate("module Other\npublic class Other {}",
                               "r6/scripts/Other/Other.reds")
        self.assertEqual(r["status"], "NOT_APPLICABLE", r)
        self.assertIsNone(out)

    def test_actual_private_postgeneric_source_when_available(self):
        path = (ROOT / ".phase2-private/base_after_generic/r6/scripts/"
                "HQ_HoloTravel/HQ_HoloTravel.reds")
        if not path.is_file():
            self.skipTest("private post-generic source available only in CI")
        original = path.read_text("utf8")
        r, result = self.generate(original)
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertEqual(result.count(
            "let color = this.ResolveColor(this.svc.holo.particleColor);"), 1)
        self.assertIn("class ParticleHandler extends BaseHandler", result)
        self.assertIn("BindCallbackTRL(self, n\"OnUpdate\"", result)
        if os.environ.get("GITHUB_ACTIONS") == "true":
            compiler = ROOT / ".phase2-private/Compiler"
            dll = (ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/"
                   "GRedscript.Resolver.dll")
            verification = (ROOT / "tests/GRedscript.Resolver.Tests/"
                            "Verify-Phase2Checkpoint.ps1")
            self.assertTrue(compiler.is_dir())
            check = subprocess.run(
                ["pwsh", "-NoProfile", "-File", str(verification),
                 "-PostGenericRoot", str(path.parents[2]),
                 "-CompilerRoot", str(compiler),
                 "-ResolverDll", str(dll),
                 "-SemanticOverlayRoot", str(self.last_output)],
                capture_output=True, text=True, timeout=300)
            self.assertEqual(check.returncode, 0,
                             check.stdout + "\n" + check.stderr)
            self.assertIn("SCC=0.5.31", check.stdout)
        second, blank = self.generate(result)
        self.assertEqual(second["status"], "COMPLETE", second)
        self.assertIsNone(blank)


if __name__ == "__main__":
    unittest.main()
