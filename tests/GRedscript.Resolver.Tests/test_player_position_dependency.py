"""Source-wide player-position discovery uses GCO's existing lexer/method spans.

This is a read-only provenance inventory. No site is rewrite-authorized.
"""
import json
import subprocess
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCE = """module Test.Position

public class Detector extends IScriptable {
  public func HasPlayerDistance() -> Bool {
    let p = GetPlayer(GetGameInstance());
    let position = p.GetWorldPosition();
    return Vector4.Distance(position, new Vector4(0.0, 0.0, 0.0, 1.0)) < 2.0;
  }
}

public class SpatialChecks extends IScriptable {
  public static func IdentityObject(obj: ref<PlayerPuppet>) -> ref<GameObject> {
    return obj;
  }
  public static func IsNear(position: Vector4) -> Bool {
    return Vector4.Distance(position, new Vector4(1.0, 1.0, 1.0, 1.0)) < 5.0;
  }
  public static func ForwardLocal() -> Bool {
    return SpatialChecks.IsNear(GetPlayer(GetGameInstance()).GetWorldPosition());
  }
  public static func ForwardNpc(npc: ref<NPCPuppet>) -> Bool {
    return SpatialChecks.IsNear(npc.GetWorldPosition());
  }
  public static func IdentityPosition(pos: Vector4) -> Vector4 {
    return pos;
  }
  public static func ForwardTransformed() -> Bool {
    return SpatialChecks.IsNear(SpatialChecks.IdentityPosition(
      GetPlayer(GetGameInstance()).GetWorldPosition()));
  }
}

public class Consumer extends ScriptableSystem {
  private let detector: ref<Detector>;
  private let m_player: wref<PlayerPuppet>;

  public func OnAttach() {
    this.m_player = GetPlayer(GetGameInstance());
  }

  public func Check() {
    this.detector.HasPlayerDistance();
    let pos = this.m_player.GetWorldPosition();
    let npc: ref<NPCPuppet> = null;
    let npcPos = npc.GetWorldPosition();
    let vehicle: ref<VehicleObject> = null;
    let vehiclePos = vehicle.GetWorldPosition();
    let player: ref<NPCPuppet> = null;
    let wronglyNamed = player.GetWorldPosition();
  }

  public func NotDirectGetter() -> Void {
    let disguised = SpatialChecks.IdentityObject(GetPlayer(GetGameInstance()));
    let misleading = disguised.GetWorldPosition();
  }

  public func CompareNpc(npc: ref<NPCPuppet>) -> Bool {
    let here = GetPlayer(GetGameInstance()).GetWorldPosition();
    let there = npc.GetWorldPosition();
    return Vector4.Distance(here, there) < 9.0;
  }

  public func CompareTwoNpcs(npc: ref<NPCPuppet>, other: ref<NPCPuppet>) -> Bool {
    return Vector4.Distance(npc.GetWorldPosition(),
                            other.GetWorldPosition()) < 9.0;
  }

  public func Parameter(possiblePlayer: ref<PlayerPuppet>,
                        someone: ref<GameObject>) {
    let a = possiblePlayer.GetWorldPosition();
    let b = someone.GetWorldPosition();
  }
}

@wrapMethod(PlayerPuppet)
protected cb func OnPossiblePlayerFrame() -> Void {
  let pos = this.GetWorldPosition();
}

// This must not create a fake callsite:
// npc.GetWorldPosition()
// "player.GetWorldPosition()"
"""

OTHER = """module Test.Other

public class VehiclePositionProbe extends VehicleObject {
  public func Inspect() -> Void {
    let here = this.GetWorldPosition();
  }
}

public class DirectProbe extends IScriptable {
  public func Direct() -> Vector4 {
    return GetPlayer(GetGameInstance()).GetWorldPosition();
  }
}
"""


class PlayerPositionDependencyTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.game = self.root / "game"
        source = self.game / "r6/scripts/Test"
        source.mkdir(parents=True)
        (source / "Position.reds").write_text(SOURCE, encoding="utf-8")
        (source / "Other.reds").write_text(OTHER, encoding="utf-8")
        self.report = self.root / "position.json"

    def scan(self):
        return subprocess.run(
            ["dotnet", str(DLL), "scan-player-position",
             "--game", str(self.game), "--out", str(self.report)],
            text=True, capture_output=True, timeout=120)

    def test_provenance_false_positives_and_indirect_source_owner(self):
        before = {p.name: p.read_bytes() for p in
                  (self.game / "r6/scripts/Test").glob("*.reds")}
        result = self.scan()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        report = json.loads(self.report.read_text())
        self.assertEqual(report["schemaVersion"],
                         "gco.redscript.player-position-dependencies.v1")
        self.assertEqual(report["sourceFiles"], 2)
        self.assertTrue(report["sourceStackSha256"])
        self.assertFalse(report["generationAuthorized"])
        self.assertFalse(report["productionZipAuthorized"])
        self.assertTrue(report["originalSourcesModified"] is False)
        rows = report["callsites"]
        # Exact lexer count: comments/strings containing GetWorldPosition
        # and non-call identifiers must not inflate source coverage.
        self.assertEqual(len(rows), 18)
        self.assertEqual(len(rows), report["worldPositionCallsites"])
        self.assertEqual(report["distanceCallsites"], 4)
        self.assertEqual(report["distanceSquaredCallsites"], 0)
        self.assertEqual(len(report["geometries"]), 4)
        self.assertTrue(all(not x["generationAuthorized"]
                            for x in report["geometries"]))
        geom = report["geometries"]
        self.assertTrue(any(x["positionDependency"] == "PLAYER_AND_LITERAL_POSITION"
                            and x["method"] == "HasPlayerDistance" for x in geom))
        self.assertTrue(any(x["positionDependency"] == "PLAYER_AND_DYNAMIC_ENTITY"
                            and x["method"] == "CompareNpc" for x in geom))
        self.assertTrue(any(x["positionDependency"] == "NONPLAYER_ENTITY_DISTANCE"
                            and x["method"] == "CompareTwoNpcs" for x in geom))
        locals_ = [r for r in rows if r["positionOrigin"] == "LOCAL_PLAYER"]
        self.assertGreaterEqual(len(locals_), 2)
        self.assertTrue(any(r["receiver"] == "<call-result>" for r in locals_))
        self.assertTrue(any(r["receiver"] == "this.m_player" for r in locals_))
        self.assertTrue(any(r["positionOrigin"] == "NPC" and
                            r["receiver"] == "player" for r in rows))
        self.assertTrue(any(r["positionOrigin"] == "VEHICLE" and
                            r["receiver"] == "vehicle" for r in rows))
        self.assertTrue(any(r["positionOrigin"] == "VEHICLE" and
                            r["receiver"] == "this" for r in rows))
        self.assertTrue(any(r["playerTypeHint"] and not r["localPlayerProven"]
                            for r in rows))
        helper = next(r for r in rows if r["method"] == "HasPlayerDistance")
        self.assertEqual(helper["operation"], "DISTANCE")
        self.assertTrue(any(h["method"] == "Check" for h in helper["helperCallers"]))
        self.assertGreaterEqual(report["indirectConsumerMethods"], 1)
        forwarded = next(r for r in rows if r["method"] == "ForwardLocal")
        self.assertTrue(any(h["method"] == "IsNear" and
                            h["operation"] == "DISTANCE" and
                            h["provenLocalPositionArgument"]
                            for h in forwarded["downstreamHelpers"]))
        self.assertGreaterEqual(
            report["downstreamGeometryWithDirectPositionArguments"], 1)
        flows = report["helperParameterGeometryFlows"]
        self.assertEqual(report["callsiteScopedPlayerGeometryFlows"],
                         len(flows))
        proven = [f for f in flows if f["callerMethod"] == "ForwardLocal"
                  and f["calleeMethod"] == "IsNear"]
        self.assertEqual(len(proven), 1)
        self.assertEqual(proven[0]["argumentIndex"], 0)
        self.assertEqual(proven[0]["calleeParameter"], "position")
        self.assertEqual(proven[0]["otherOperandOrigin"],
                         "STATIC_LITERAL_VECTOR")
        self.assertTrue(proven[0]["sourceParameterUsedAsExactGeometryOperand"])
        self.assertTrue(proven[0]["callsiteScoped"])
        self.assertFalse(proven[0]["parameterMutationAndTimingClosureProven"])
        self.assertFalse(proven[0]["generationAuthorized"])
        self.assertFalse(any(f["callerMethod"] in (
            "ForwardNpc", "ForwardTransformed") for f in flows))
        disguised = next(r for r in rows if
                         r["method"] == "NotDirectGetter")
        self.assertEqual(disguised["positionOrigin"], "UNKNOWN")
        self.assertFalse(disguised["localPlayerProven"])
        after = {p.name: p.read_bytes() for p in
                 (self.game / "r6/scripts/Test").glob("*.reds")}
        self.assertEqual(before, after)

    def test_refuses_in_game_output_and_existing_scan(self):
        self.report = self.game / "r6/scripts/Test/provenance.json"
        result = self.scan()
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.report.exists())
        self.report = self.root / "position.json"
        self.assertEqual(self.scan().returncode, 0)
        result = self.scan()
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("PLAYER_POSITION_SCAN_OUTPUT_COLLISION",
                      result.stdout + result.stderr)

    def test_unknown_is_discovery_not_optimization_permission(self):
        result = self.scan()
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        rows = json.loads(self.report.read_text())["callsites"]
        for row in rows:
            self.assertTrue(row["originalFallbackRequired"])
            self.assertFalse(row["positionOrigin"] == "UNKNOWN" and
                             row["strategy"] != "NO_AUTOMATIC_REWRITE")
        self.assertTrue(any(row["positionOrigin"] == "UNKNOWN" for row in rows))


if __name__ == "__main__":
    unittest.main()
