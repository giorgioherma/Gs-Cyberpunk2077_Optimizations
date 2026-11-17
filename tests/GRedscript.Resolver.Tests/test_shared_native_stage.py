"""Shared CET/REDscript native dispatcher: real staged assets, ownership and
idempotence. No second DLL, no game installation, no optimization ZIP.
"""
import struct
import tempfile
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"
NATIVE = "red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll"
LOCATIONS = "red4ext/plugins/G-CET-StaticLocations/G-CET.locations"
BRIDGE = "r6/scripts/G-RedRuntime/Native/GCOSharedNativeBridge.reds"
REQUIRED = (
    b"ScriptGameInstance", b"GCETStaticReady",
    b"GCETStaticCoordinatorState", b"GCETStaticOwnerCount",
    b"GCETStaticOwnerAnchor", b"GCETStaticState",
    b"GCETPlayerVector4Live", b"GCETPlayerVector4Valid"
)


def table(rows):
    data = bytearray(b"GCAS" + struct.pack("<III", 1, len(rows), 0))
    for index, (owner, x, y, z, radius, xy) in enumerate(rows, 1):
        data.extend(struct.pack("<iffffii", index, x, y, z, radius, xy, owner))
    return bytes(data)


def decode(data):
    assert data[:4] == b"GCAS"
    size, = struct.unpack_from("<I", data, 8)
    assert len(data) == 16 + 28 * size
    return [struct.unpack_from("<iffffii", data, 16 + i * 28)
            for i in range(size)]


class NativeSharedAssetContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        td = tempfile.TemporaryDirectory()
        self.addCleanup(td.cleanup)
        self.root = Path(td.name)
        self.cet = self.root / "cet-package"
        self.output = self.root / "shared-overlay"
        self.game = self.root / "game"
        self.game.mkdir()
        dll = self.cet / "runtime/native/GCETStaticLocations.dll"
        dll.parent.mkdir(parents=True)
        dll.write_bytes(b"MZ" + b"\\0" * 2048 + b"\\0".join(REQUIRED))

    def run_stage(self, *args):
        return subprocess.run(
            ["dotnet", str(DLL), "stage-shared-native",
             "--cet-runtime", str(self.cet),
             "--game", str(self.game),
             "--out", str(self.output), *map(str, args)],
            text=True, capture_output=True, timeout=120)

    def test_red_first_creates_one_native_asset_and_empty_gcas(self):
        r = self.run_stage()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertTrue((self.output / NATIVE).is_file())
        self.assertEqual(decode((self.output / LOCATIONS).read_bytes()), [])
        source = (self.output / BRIDGE).read_text()
        self.assertIn("@addMethod(GameInstance)", source)
        self.assertIn("public static native func GCETPlayerVector4Live()", source)
        self.assertIn("return -1;", source)
        self.assertNotIn("Schedule.Every", source)
        self.assertNotIn("GetWorldPosition", source)
        self.assertFalse(list(self.root.rglob("*.zip")))

    def test_cet_first_then_red_owner_merge_and_repeat_idempotent(self):
        target = self.output / LOCATIONS
        target.parent.mkdir(parents=True)
        target.write_bytes(table([(10, 1., 2., 3., 4., 0)]))
        incoming = self.root / "red-locations"
        incoming.write_bytes(table([(20, 5., 6., 7., 8., 1)]))
        r = self.run_stage("--red-locations", incoming)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        rows = decode(target.read_bytes())
        self.assertEqual([x[-1] for x in rows], [10, 20])
        previous = target.read_bytes()
        repeat = self.run_stage("--red-locations", incoming)
        self.assertEqual(repeat.returncode, 0, repeat.stdout + repeat.stderr)
        self.assertEqual(target.read_bytes(), previous)
        self.assertIn("ALREADY_PRESENT", repeat.stdout)

    def test_red_first_then_cet_owner_merge_and_original_native_preserved(self):
        red = self.root / "red-locations"
        red.write_bytes(table([(20, 5., 6., 7., 8., 0)]))
        self.assertEqual(self.run_stage("--red-locations", red).returncode, 0)
        original_dll = (self.output / NATIVE).read_bytes()
        # New CET owner is merged after RED owner; no native overwrite.
        cet = self.root / "cet-locations"
        cet.write_bytes(table([(10, 1., 2., 3., 4., 1)]))
        r = self.run_stage("--red-locations", cet)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        self.assertEqual([x[-1] for x in decode(
            (self.output / LOCATIONS).read_bytes())], [20, 10])
        self.assertEqual((self.output / NATIVE).read_bytes(), original_dll)

    def test_collision_cannot_destroy_existing_stage(self):
        original = table([(20, 5., 6., 7., 8., 0)])
        target = self.output / LOCATIONS
        target.parent.mkdir(parents=True)
        target.write_bytes(original)
        conflicting = self.root / "red-locations"
        conflicting.write_bytes(table([(20, 5., 6., 7., 9., 0)]))
        r = self.run_stage("--red-locations", conflicting)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("SHARED_NATIVE_OWNER_SOURCE_COLLISION",
                      r.stdout + r.stderr)
        self.assertEqual(target.read_bytes(), original)
        self.assertFalse((self.output / NATIVE).exists())

    def test_refuses_to_write_into_real_game(self):
        self.output = self.game
        run = self.run_stage()
        self.assertNotEqual(run.returncode, 0)
        self.assertIn("SHARED_NATIVE_GAME_OUTPUT_FORBIDDEN",
                      run.stdout + run.stderr)
        self.assertFalse((self.game / NATIVE).exists())

    def test_invalid_manifest_and_missing_native_fail_without_output(self):
        invalid = self.root / "invalid"
        invalid.write_bytes(b"GCAS" + b"\\0" * 12)
        self.assertNotEqual(self.run_stage("--red-locations", invalid).returncode, 0)
        self.assertFalse(self.output.exists())
        (self.cet / "runtime/native/GCETStaticLocations.dll").unlink()
        self.assertNotEqual(self.run_stage().returncode, 0)
        self.assertFalse(self.output.exists())


if __name__ == "__main__":
    unittest.main()
