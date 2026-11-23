"""The framework that GCO installs in the SAME ZIP counts for semantic ABI
admission. It never rewrites the original source or changes its captured hash.
Fixtures model the package manifest; real framework SCC remains the CI gate.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

from test_phase2_ncr_tick import SOURCE as NCR_SOURCE
from test_phase2_grs_state import SOURCE as GRS_SOURCE
from test_phase2_quickslots_input import SOURCE as CQ_SOURCE

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"

NCR = "r6/scripts/NightCityRemembers/NCR.reds"
GRS = "r6/scripts/GearRecoverySystem/25_GRSInteractionController.reds"
CQ = "r6/scripts/custom_quickslots/player_puppet.reds"

COMPONENTS = {
    "Core/Runtime.reds": """module GRedRuntime
public class Runtime extends ScriptableSystem {
 public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
 public func IsReady() -> Bool { return true; }
 public func GetInputHub() -> ref<InputHub> { return null; }
 public func GetScheduler() -> ref<Scheduler> { return null; }
 public func GetStateCache() -> ref<StateCache> { return null; }
}
""",
    "Input/InputHub.reds": """module GRedRuntime
public class InputEvent extends IScriptable {
 public let actionName: CName;
 public let actionType: gameinputActionType;
 public let consumed: Bool;
}
public class InputListener extends IScriptable {
 public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputDeviceListener extends IScriptable {
 public func OnGRedInputDeviceChanged(lastUsedKBM: Bool) -> Void {}
}
public class InputHub extends IScriptable {
 public func Subscribe(name: CName, owner: ref<InputListener>) -> Int32 { return 1; }
 public func SubscribeDevice(owner: ref<InputDeviceListener>) -> Int32 { return 1; }
 public func Unsubscribe(id: Int32) -> Bool { return true; }
 public func UnsubscribeDevice(id: Int32) -> Bool { return true; }
}
""",
    "Scheduler/Scheduler.reds": """module GRedRuntime
public class ScheduledJob extends IScriptable {
 public func Configure(name: CName, interval: Float, repeat: Bool) -> ref<ScheduledJob> { return this; }
 public func Execute(runtime: ref<Runtime>) -> Void {}
}
public class Scheduler extends IScriptable {
 public func Register(job: ref<ScheduledJob>) -> Int32 { return 1; }
 public func Unregister(id: Int32) -> Bool { return true; }
 public func RescheduleJob(id: Int32, interval: Float, delay: Float) -> Bool { return true; }
}
""",
    "State/StateCache.reds": """module GRedRuntime
public class StateCache extends IScriptable {
 public func GetPlayer() -> wref<PlayerPuppet> { return null; }
 public func GetBlackboardSystem() -> wref<BlackboardSystem> { return null; }
}
""",
}
# Minimum complete framework manifest: non-API files have no effect on ABI.
for section, name in [
    ("Context", "ContextService"),
    ("Diagnostics", "Diagnostics"),
    ("Events", "DirtyFlags"),
    ("Events", "EventBus"),
    ("Events", "QuestFactSignals"),
    ("Hooks", "HookBus"),
    ("Location", "NativeLocationSignals"),
    ("Input", "PlayerExtensions")
]:
    COMPONENTS[f"{section}/{name}.reds"] = "module GRedRuntime\n"


class ProvidedRuntimeContracts(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.game = self.root / "game"
        self.owner = self.root / "package/components/redruntime/owner"
        self.plan = self.root / "plan.json"
        self.analysis = self.root / "analysis.json"
        self.sequence = 0

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=240)

    def populate_game(self, owners):
        for relative, source in owners.items():
            path = self.game / relative
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(source, encoding="utf8")
        self.original = {p: (self.game / p).read_bytes() for p in owners}

    def bundle(self, components=None):
        contents = COMPONENTS if components is None else components
        manifest = []
        for name, value in contents.items():
            path = self.owner / "r6/scripts/G-RedRuntime" / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(value, encoding="utf8")
            manifest.append({
                "path": "r6/scripts/G-RedRuntime/" + name,
                "sha256": hashlib.sha256(path.read_bytes()).hexdigest()
            })
        destination = self.owner.parent / "source-sha256.json"
        destination.write_text(json.dumps(manifest), encoding="utf8")

    def ready(self):
        proc = self.cli("plan-redscript", "--game", self.game,
                        "--out", self.plan)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        plan = json.loads(self.plan.read_text(encoding="utf8"))
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": plan["sourceStackSha256"]}
        }), encoding="utf8")
        return plan["sourceStackSha256"]

    def stage(self, provided=True):
        self.sequence += 1
        out = self.root / ("out" + str(self.sequence))
        command = ["stage-redscript-plan", "--game", self.game,
                   "--plan", self.plan, "--analysis", self.analysis,
                   "--out", out]
        if provided:
            command += ["--provided-runtime-owner", self.owner]
        proc = self.cli(*command)
        for rel, content in self.original.items():
            self.assertEqual((self.game / rel).read_bytes(), content)
        return proc, out

    def report(self, out):
        return json.loads((out / "stage-result.json").read_text(encoding="utf8"))

    def test_scheduler_is_off_without_bundle_and_on_with_same_pass_bundle(self):
        self.populate_game({NCR: NCR_SOURCE})
        self.bundle()
        source_hash = self.ready()
        before, absent = self.stage(provided=False)
        self.assertEqual(before.returncode, 0, before.stdout + before.stderr)
        self.assertTrue(any(item["order"] == 34 and
                            "RUNTIME_ABI_MISSING" in item["reason"]
                            for item in self.report(absent)["skipped"]))
        after, generated = self.stage()
        self.assertEqual(after.returncode, 0, after.stdout + after.stderr)
        data = self.report(generated)
        self.assertEqual(data["sourceStackSha256"], source_hash)
        self.assertIn("ncr-maintenance-scheduler",
                      [r["id"] for r in data["completed"]])
        self.assertIn("class NCRGCOMaintenanceJob extends ScheduledJob",
                      (generated / "effective-preview" / NCR).read_text("utf8"))
        self.assertFalse(list((generated / "effective-preview/r6/scripts")
                              .rglob("G-RedRuntime/*.reds")))

    def test_statecache_is_admitted_when_bundled_but_not_in_game(self):
        self.populate_game({GRS: GRS_SOURCE})
        self.bundle()
        self.ready()
        without, missing = self.stage(False)
        self.assertEqual(without.returncode, 0, without.stdout + without.stderr)
        self.assertTrue(any(item["order"] == 40 and
                            "RUNTIME_ABI_MISSING" in item["reason"]
                            for item in self.report(missing)["skipped"]))
        with_bundle, target = self.stage()
        self.assertEqual(with_bundle.returncode, 0,
                         with_bundle.stdout + with_bundle.stderr)
        self.assertIn("gear-recovery-statecache",
                      [x["id"] for x in self.report(target)["completed"]])

    def test_input_hub_and_device_admitted_from_bundled_classes(self):
        self.populate_game({CQ: CQ_SOURCE})
        self.bundle()
        self.ready()
        without, absent = self.stage(False)
        self.assertEqual(without.returncode, 0, without.stdout + without.stderr)
        self.assertTrue(any(x["order"] == 16 and "RUNTIME_ABI_NOT_PROVEN" in x["reason"]
                            for x in self.report(absent)["skipped"]))
        with_bundle, target = self.stage()
        self.assertEqual(with_bundle.returncode, 0,
                         with_bundle.stdout + with_bundle.stderr)
        self.assertIn("custom-quickslots-input",
                      [x["id"] for x in self.report(target)["completed"]])

    def test_existing_incompatible_owner_file_wins_over_bundle(self):
        self.populate_game({GRS: GRS_SOURCE})
        self.bundle()
        live = self.game / "r6/scripts/G-RedRuntime/State/StateCache.reds"
        live.parent.mkdir(parents=True, exist_ok=True)
        live.write_text("module GRedRuntime\npublic class StateCache extends IScriptable {}\n",
                        encoding="utf8")
        self.original["r6/scripts/G-RedRuntime/State/StateCache.reds"] = live.read_bytes()
        self.ready()
        result, out = self.stage()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertTrue(any(x["order"] == 40 and "RUNTIME_ABI_MISSING" in x["reason"]
                            for x in self.report(out)["skipped"]))

    def test_tampered_bundle_manifest_refuses_entire_generation(self):
        self.populate_game({NCR: NCR_SOURCE})
        self.bundle()
        self.ready()
        manifest = self.owner.parent / "source-sha256.json"
        j = json.loads(manifest.read_text("utf8"))
        j[0]["sha256"] = "0" * 64
        manifest.write_text(json.dumps(j), encoding="utf8")
        result, out = self.stage()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("GCO_PROVIDED_RUNTIME_SOURCE_HASH_MISMATCH",
                      result.stdout + result.stderr)
        self.assertFalse((out / "stage-result.json").exists())

    def test_unmanifested_framework_source_refused(self):
        self.populate_game({NCR: NCR_SOURCE})
        self.bundle()
        self.ready()
        new_file = self.owner / "r6/scripts/G-RedRuntime/Extra/Untracked.reds"
        new_file.parent.mkdir(parents=True, exist_ok=True)
        new_file.write_text("module GRedRuntime\n", encoding="utf8")
        result, out = self.stage()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("GCO_PROVIDED_RUNTIME_UNMANIFESTED_SOURCE",
                      result.stdout + result.stderr)
        self.assertFalse((out / "stage-result.json").exists())

if __name__ == "__main__":
    unittest.main()
