"""EVS historical Pass 6.0 source-aware camera listener removal contracts."""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PATH = "r6/scripts/Enhanced Vehicle System/Enhanced Vehicle System.reds"
SOURCE = '''module Hgyi56.Enhanced_Vehicle_System
public class Utils {
    public static func Fixed() -> Bool { return true; }
}
@wrapMethod(VehicleComponent)
private final func RegisterInputListener() -> Void {
    if this.hgyi56_EVS_IsUnsupportedVehicleType() {
        wrappedMethod();
        return;
    };
    let player: ref<PlayerPuppet> = GetPlayer(GetGameInstance());
    wrappedMethod();
    player.RegisterInputListener(this, n"CameraX");
    player.RegisterInputListener(this, n"CameraY");
    player.RegisterInputListener(this, n"CameraMouseX");
    player.RegisterInputListener(this, n"CameraMouseY");
    player.RegisterInputListener(this, n"CycleDoor");
    player.RegisterInputListener(this, n"CycleWindow");
}
@replaceMethod(VehicleComponent)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    // GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1
    let name = ListenerAction.GetName(action);
    return Equals(name,n"CycleDoor");
}
'''

class EvsCameraTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.i = 0

    def gen(self, source=SOURCE, path=PATH, overlay=None):
        self.i += 1
        game = self.root / f"game{self.i}"
        original = game / path
        original.parent.mkdir(parents=True, exist_ok=True)
        original.write_text(source, encoding="utf8")
        out = self.root / f"out{self.i}"
        cmd = ["dotnet", str(DLL), "phase2-evs-camera",
               "--game", str(game), "--out", str(out)]
        if overlay is not None:
            stage = self.root / f"stage{self.i}"
            staged = stage / path
            staged.parent.mkdir(parents=True, exist_ok=True)
            staged.write_text(overlay, encoding="utf8")
            cmd += ["--base-overlay", str(stage)]
        run = subprocess.run(cmd, capture_output=True, text=True, timeout=120)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        report = json.loads((out / "phase2-result.json").read_text(encoding="utf8"))
        self.assertEqual(original.read_text(encoding="utf8"), source)
        dest = out / path
        return report, dest.read_text(encoding="utf8") if dest.exists() else None

    def check(self, src):
        for n in ["CameraX", "CameraY", "CameraMouseX", "CameraMouseY"]:
            self.assertNotIn('n"' + n + '"', src)
        self.assertIn("player.RegisterInputListener(this, n\"CycleDoor\")", src)
        self.assertIn("player.RegisterInputListener(this, n\"CycleWindow\")", src)
        self.assertIn("wrappedMethod();", src)
        self.assertEqual(src.count("wrappedMethod();"), 2)
        self.assertIn("GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1", src)
        self.assertIn("ListenerAction.GetName(action)", src)

    def test_stock_four_actions_removed_one_run_and_repeat(self):
        report, src = self.gen()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 4)
        self.check(src)
        complete, unchanged = self.gen(src)
        self.assertEqual(complete["status"], "COMPLETE", complete)
        self.assertIsNone(unchanged)

    def test_partial_two_actions_removed_without_duplication(self):
        partial = SOURCE.replace(
            '    player.RegisterInputListener(this, n"CameraX");\n', "").replace(
            '    player.RegisterInputListener(this, n"CameraMouseY");\n', "")
        result, src = self.gen(partial)
        self.assertEqual(result["status"], "APPLIED", result)
        self.assertEqual(result["appliedEdits"], 2)
        self.check(src)

    def test_custom_relocated_and_previous_semantic_overlay_preserved(self):
        modified = SOURCE.replace(
            'player.RegisterInputListener(this, n"CycleWindow");',
            'player.RegisterInputListener(this, n"CycleWindow");\n'
            '    this.OwnerSpecificDiagnostic();')
        rel = "r6/scripts/Hgyi56/CustomEVS.reds"
        report, src = self.gen(modified, path=rel)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("this.OwnerSpecificDiagnostic();", src)
        self.check(src)
        prior = modified.replace(
            '    let name = ListenerAction.GetName(action);',
            '    // #30 EVS PS handle already present\n'
            '    let gcoOnActionPS: ref<VehicleComponentPS> = this.GetPS();\n'
            '    let name = ListenerAction.GetName(action);')
        report, src = self.gen(modified, path=rel, overlay=prior)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("let gcoOnActionPS:", src)
        self.check(src)

    def test_extra_camera_use_refuses_only_this_operation(self):
        custom = SOURCE.replace(
            '    player.RegisterInputListener(this, n"CycleDoor");',
            '    this.SpecialCameraAction(n"CameraX");\n'
            '    player.RegisterInputListener(this, n"CycleDoor");')
        report, src = self.gen(custom)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(
            report["reason"], "EVS_CAMERA_ACTION_USED_OR_REGISTRATION_DRIFT:CameraX")
        self.assertIsNone(src)

    def test_no_wrapped_method_refuses(self):
        custom = SOURCE.replace("wrappedMethod();", "this.SomeOtherListener();")
        report, src = self.gen(custom)
        self.assertEqual(report["status"], "REFUSED", report)
        self.assertEqual(report["reason"], "EVS_CAMERA_NATIVE_WRAPPER_UNPROVEN")
        self.assertIsNone(src)

    def test_absent_owner_no_output(self):
        result, src = self.gen(
            'module Other\npublic class Unrelated {}',
            path="r6/scripts/Other/Unused.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE", result)
        self.assertIsNone(src)

if __name__ == "__main__":
    unittest.main()
