"""Historical HQ_HoloGirl Pass 4/5 source-state and event lifecycle contracts."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/HQ_HoloGirl/Engine/HoloGirl.reds"

STOCK = r'''module EmreHQ.HoloGirl
public class HoloGirl extends ScriptableService
{
    private let holo_mi: ResourceRef;
    private let evtDefHolo: ref<ResourceEvent>;
    private let evtHQHolo: ref<ResourceEvent>;
    public let holo: ref<Holo>;
    private cb func OnLoad()
    {
        if !IsModExists("HQ_HoloGirl.archive") { return; }
        this.Clear();
        this.holo_mi *= HQRes("holo.mi");
        BindCallbackRL(this, n"UpdateDefHolo", r"base\some\mesh");
        BindCallbackTRL(this, n"UpdateHQHolo", n"CMaterialInstance");
    }
    private func IsValid(e: ref<ResourceEvent>) -> Bool {
        return IsDefined(this.holo) && IsDefined(e);
    }
    public func Unrelated() -> Void { this.UserLogic(); }
    private func UpdateHQHolo(e: ref<ResourceEvent>)
    {
        if !this.IsValid(e) {
            return;
        }
        if !IsDefined(this.evtHQHolo) {
            this.evtHQHolo = e;
        }
        let mtl = e.GetResource() as CMaterialInstance;
        if ResourceRef.GetPath(mtl.baseMaterial) == HQRes("holo_base.mi")
        {
            let i = 0;
            while i < ArraySize(mtl.params)
            {
                let name = mtl.params[i].name;
                if Equals(name, n"Color") || Equals(name, n"Second_Color") {
                    mtl.params[i].data = this.ResolveColor(this.holo.txColor);
                }
                else if Equals(name, n"ParalaxTexture") {
                    mtl.params[i].data = this.tx;
                }
                else if Equals(name, n"Emissive") {
                    mtl.params[i].data = this.GetEmissive();
                }
                i += 1;
            }
        }
    }
}
@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
    wrappedMethod();
    HoloGirl.Get().UpdateAll();
}
'''


class HoloGirlMaterialFastPathTests(unittest.TestCase):
    def setUp(self):
        home = tempfile.TemporaryDirectory()
        self.addCleanup(home.cleanup)
        self.home = Path(home.name)
        self.n = 0

    def generate(self, source, relative=TARGET):
        self.n += 1
        game = self.home / f"game{self.n}"
        path = game / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(source, encoding="utf8")
        out = self.home / f"out{self.n}"
        p = subprocess.run(
            ["dotnet", str(DLL), "phase2-hologirl-material",
             "--game", str(game), "--out", str(out)],
            capture_output=True, text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + "\n" + p.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        self.assertEqual(path.read_text("utf8"), source)
        target = out / relative
        return report, target.read_text("utf8") if target.exists() else None

    def check(self, output):
        self.assertEqual(output.count("private let holoBaseRR: ResRef;"), 1)
        self.assertEqual(
            output.count('this.holoBaseRR = HQRes("holo_base.mi");'), 1)
        self.assertIn('BindCallbackTRL(this, n"UpdateHQHolo"', output)
        self.assertIn('BindCallbackRL(this, n"UpdateDefHolo"', output)
        self.assertIn("if !IsDefined(e) {", output)
        self.assertIn("let mtl = e.GetResource() as CMaterialInstance;", output)
        self.assertIn("ResourceRef.GetPath(mtl.baseMaterial) != this.holoBaseRR",
                      output)
        self.assertIn("if !IsDefined(this.holo) {", output)
        self.assertIn("this.evtHQHolo = e;", output)
        self.assertIn("let color = this.ResolveColor(this.holo.txColor);",
                      output)
        self.assertIn("let emissive = this.GetEmissive();", output)
        self.assertIn("mtl.params[i].data = color;", output)
        self.assertIn("mtl.params[i].data = emissive;", output)
        self.assertNotIn('ResourceRef.GetPath(mtl.baseMaterial) == HQRes(',
                         output)
        self.assertEqual(output.count("protected cb func OnGameAttached"), 1)
        self.assertIn("this.UserLogic();", output)

    def test_stock_one_run_then_no_changes(self):
        report, output = self.generate(STOCK)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.check(output)
        again, nofile = self.generate(output)
        self.assertEqual(again["status"], "COMPLETE", again)
        self.assertIsNone(nofile)

    def test_partial_cached_field_and_init_completed(self):
        partial = STOCK.replace(
            "    private let holo_mi: ResourceRef;",
            "    private let holo_mi: ResourceRef;\n"
            "    private let holoBaseRR: ResRef;")
        partial = partial.replace(
            'this.holo_mi *= HQRes("holo.mi");',
            'this.holo_mi *= HQRes("holo.mi");\n'
            '        this.holoBaseRR = HQRes("holo_base.mi");')
        report, output = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(output)

    def test_partial_missing_initializer_repaired(self):
        _, final = self.generate(STOCK)
        partial = final.replace(
            '        this.holoBaseRR = HQRes("holo_base.mi");', "")
        state, output = self.generate(partial)
        self.assertEqual(state["status"], "APPLIED", state)
        self.check(output)

    def test_relocated_current_source_with_unrelated_code(self):
        custom = STOCK.replace(
            "    public func Unrelated()",
            "    private let userSetting: Bool = true;\n"
            "    public func Unrelated()")
        r, output = self.generate(
            custom, "r6/scripts/HQ_HoloGirl/AnotherRelease/Reordered.reds")
        self.assertEqual(r["status"], "APPLIED", r)
        self.assertIn("private let userSetting: Bool = true;", output)
        self.check(output)

    def test_changed_filter_scope_is_narrow_refusal(self):
        unsafe = STOCK.replace(
            'HQRes("holo_base.mi")\n        {',
            'HQRes("other_base.mi")\n        {')
        report, output = self.generate(unsafe)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertIn("HQ_HOLOGIRL_OLD_FILTER_MISSING", report["reason"])
        self.assertIsNone(output)

    def test_absent_owner(self):
        report, output = self.generate(
            "module Other\npublic class Foo {}\n",
            "r6/scripts/Other/Foo.reds")
        self.assertEqual(report["status"], "NOT_APPLICABLE")
        self.assertIsNone(output)


if __name__ == "__main__":
    unittest.main()
