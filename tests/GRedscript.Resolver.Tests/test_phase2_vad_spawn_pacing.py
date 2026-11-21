"""Historical Pass 6.0 Virtual Atelier dynamic drop-point spawn pacing.

Source-state acceptance: stock, partial, complete, relocated/custom, absent,
incompatible, and repeat generation. Official full-stack SCC runs in branch CI.
"""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PATH = "r6/scripts/VirtualAtelierDelivery/VirtualAtelierDelivery.reds"

SOURCE = '''module AtelierDelivery
public class AtelierDropPointInstance {
  public let uniqueTag: CName;
}
public class AtelierDropPointsSpawnerConfig {
  public func GetSpawnPointsByTag(tag: CName) -> array<ref<AtelierDropPointInstance>> {
    let items: array<ref<AtelierDropPointInstance>>;
    return items;
  }
}
public class AtelierDropPointsSpawner extends ScriptableSystem {
  private let entitySystem: wref<DynamicEntitySystem>;
  private let delaySystem: wref<DelaySystem>;
  private let spawnConfig: ref<AtelierDropPointsSpawnerConfig>;
  public func OnAttach() -> Void {
    this.entitySystem = GameInstance.GetDynamicEntitySystem();
    this.delaySystem = GameInstance.GetDelaySystem(this.GetGameInstance());
  }
  public final func DespawnAll() -> Void {
    for tag in this.spawnConfig.GetIterationTagsNightCity() {
      this.entitySystem.DeleteTagged(tag);
    };
  }
  private final func SpawnInstancesByTag(entityTag: CName) -> Void {
    let instances: array<ref<AtelierDropPointInstance>> = this.spawnConfig.GetSpawnPointsByTag(entityTag);
    for instance in instances {
      this.SpawnDropPointEntity(instance);
    };
  }
  private final func SpawnDropPointEntity(instance: ref<AtelierDropPointInstance>) -> Void {
    this.entitySystem.CreateEntity(new DynamicEntitySpec());
  }
}
'''

class VirtualAtelierPacingTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.attempt = 0

    def run_gen(self, source=SOURCE, path=PATH, prior=None):
        self.attempt += 1
        game = self.root / f"game{self.attempt}"
        owner = game / path
        owner.parent.mkdir(parents=True, exist_ok=True)
        owner.write_text(source, encoding="utf-8")
        output = self.root / f"output{self.attempt}"
        args = ["dotnet", str(DLL), "phase2-vad-spawn",
                "--game", str(game), "--out", str(output)]
        if prior is not None:
            overlay = self.root / f"overlay{self.attempt}"
            staged = overlay / path
            staged.parent.mkdir(parents=True, exist_ok=True)
            staged.write_text(prior, encoding="utf-8")
            args += ["--base-overlay", str(overlay)]
        proc = subprocess.run(args, text=True, capture_output=True, timeout=120)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        report = json.loads((output / "phase2-result.json").read_text("utf-8"))
        self.assertEqual(owner.read_text("utf-8"), source)
        staged = output / path
        files = list(output.rglob("*.reds"))
        self.assertEqual(files, [staged] if staged.exists() else [])
        return report, staged.read_text("utf-8") if staged.exists() else None

    def assert_queued(self, source):
        self.assertEqual(source.count("class GcoAtelierSpawnBatchCallback extends DelayCallback"), 1)
        self.assertEqual(source.count("let m_gcoVadSpawnQueue:"), 1)
        self.assertEqual(source.count("func ProcessSpawnQueue("), 1)
        self.assertEqual(source.count("func QueueGcoVadSpawnTick("), 1)
        self.assertEqual(source.count("func CancelGcoVadSpawnBatch("), 1)
        self.assertIn("DelayCallback(cb, 0.05, false)", source)
        self.assertIn("this.delaySystem.CancelCallback(this.m_gcoVadSpawnCallback)", source)
        self.assertIn("this.CancelGcoVadSpawnBatch();", source)
        self.assertIn("if !IsDefined(this.delaySystem) {", source)
        self.assertIn("this.SpawnDropPointEntity(vadImmediate);", source)
        self.assertIn("ArrayContains(this.m_gcoVadPendingTags, entityTag)", source)
        self.assertIn("this.SpawnDropPointEntity(instance);", source)
        self.assertIn("this.entitySystem.DeleteTagged(tag)", source)

    def test_post_generic_stock_one_pass_and_repeat(self):
        report, staged = self.run_gen()
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["ruleId"], "VIRTUAL_ATELIER_DROPPOINT_SPAWN_PACING_V1")
        self.assertTrue(report["sourceUnchanged"])
        self.assertFalse(report["gameplayValidated"])
        self.assert_queued(staged)
        later, unchanged = self.run_gen(staged)
        self.assertEqual(later["status"], "COMPLETE", later)
        self.assertIsNone(unchanged)

    def test_handwritten_partial_field_completed_without_duplicate_queue(self):
        report, staged = self.run_gen()
        partial = staged.replace(
            "  private let m_gcoVadPendingTags: array<CName>;\n", "")
        self.assertNotEqual(partial, staged)
        result, completed = self.run_gen(partial)
        self.assertEqual(result["status"], "APPLIED", result)
        self.assert_queued(completed)

    def test_handwritten_partial_callback_or_method_completed(self):
        _, full = self.run_gen()
        callback_start = full.index("public class GcoAtelierSpawnBatchCallback")
        callback_end = full.index("public class AtelierDropPointsSpawner extends", callback_start)
        partial = full[:callback_start] + full[callback_end:]
        result, completed = self.run_gen(partial)
        self.assertEqual(result["status"], "APPLIED", result)
        self.assert_queued(completed)

    def test_custom_relocated_file_and_unrelated_source_preserved(self):
        custom = SOURCE.replace(
            "this.entitySystem.DeleteTagged(tag);",
            "this.CustomAudit(tag); this.entitySystem.DeleteTagged(tag);")
        custom = custom.replace(
            "  private final func SpawnInstancesByTag",
            "  // Custom source is preserved.\n  private final func SpawnInstancesByTag")
        path = "r6/scripts/CustomMods/DeliverySource.reds"
        report, staged = self.run_gen(custom, path=path)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("this.CustomAudit(tag);", staged)
        self.assertIn("// Custom source is preserved.", staged)
        self.assert_queued(staged)

    def test_post_generic_source_with_other_semantics_composes(self):
        generic = SOURCE.replace(
            "    this.entitySystem = GameInstance.GetDynamicEntitySystem();",
            "    // GCO-GENERIC:SOME_EXISTING_OPTIMIZATION\n"
            "    this.entitySystem = GameInstance.GetDynamicEntitySystem();")
        report, staged = self.run_gen(SOURCE, prior=generic)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertIn("GCO-GENERIC:SOME_EXISTING_OPTIMIZATION", staged)
        self.assert_queued(staged)

    def test_changed_spawn_loop_narrow_refusal(self):
        unsafe = SOURCE.replace(
            "this.SpawnDropPointEntity(instance);",
            "this.CustomIntercept(instance); this.SpawnDropPointEntity(instance);")
        result, staged = self.run_gen(unsafe)
        self.assertEqual(result["status"], "REFUSED", result)
        self.assertEqual(result["reason"], "VAD_DIRECT_SPAWN_LOOP_INCOMPATIBLE")
        self.assertIsNone(staged)

    def test_missing_lifecycle_narrow_refusal(self):
        unsafe = SOURCE.replace("public final func DespawnAll()", "public final func DeleteEverything()")
        result, staged = self.run_gen(unsafe)
        self.assertEqual(result["status"], "REFUSED", result)
        self.assertEqual(result["reason"], "VAD_SPAWNER_LIFECYCLE_INCOMPATIBLE")
        self.assertIsNone(staged)

    def test_absent_owner_no_output(self):
        result, staged = self.run_gen(
            "module Other\npublic class Unrelated extends ScriptableSystem {}",
            path="r6/scripts/Other/Unrelated.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE", result)
        self.assertIsNone(staged)

    def test_foreign_partial_queue_refusal_without_competing_scheduler(self):
        partial = SOURCE.replace(
            "  private let entitySystem",
            "  private let grtSpawnQueue: array<ref<AtelierDropPointInstance>>;\n"
            "  private let entitySystem")
        result, staged = self.run_gen(partial)
        self.assertEqual(result["status"], "REFUSED", result)
        self.assertEqual(result["reason"], "VAD_FOREIGN_PARTIAL_QUEUE_UNPROVEN")
        self.assertIsNone(staged)

if __name__ == "__main__":
    unittest.main()
