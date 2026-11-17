"""Generic fact graph: exact literal names, dynamic refusal, source seal."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"
SOURCE_A = """module A
public class SomeFactReader extends IScriptable {
    private let m_listener: Uint32;
    private let m_quests: ref<QuestsSystem>;
    public func Update() -> Void {
        let x = this.m_quests.GetFact(n"test_shared_fact");
        let unknown = this.m_quests.GetFact(this.DynamicName());
        this.m_quests.SetFact(n"local_own_fact", x);
    }
    public func OnAttach() -> Void {
        this.m_listener = this.m_quests.RegisterListener(n"test_shared_fact",
            this, n"OnChanged");
    }
    public func OnDetach() -> Void {
        this.m_quests.UnregisterListener(n"test_shared_fact", this.m_listener);
    }
    protected cb func OnChanged(value: Int32) -> Bool {
        return false;
    }
}
"""
SOURCE_B = """module B
public class OtherFactReader extends IScriptable {
    private let m_quests: ref<QuestsSystem>;
    public func Refresh() -> Void {
        let existing = this.m_quests.GetFact(n"test_shared_fact");
    }
    public func Start() -> Void {
        let ignored = this.m_quests.RegisterListener(n"test_shared_fact",
            this, n"OnChanged");
    }
}
"""


class QuestFactGraphTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        t = tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root = Path(t.name)
        self.game = self.root / "game"
        a = self.game / "r6/scripts/OwnerA/Main.reds"
        b = self.game / "r6/scripts/OwnerB/Main.reds"
        a.parent.mkdir(parents=True)
        b.parent.mkdir(parents=True)
        a.write_text(SOURCE_A, encoding="utf8")
        b.write_text(SOURCE_B, encoding="utf8")
        self.a = a
        self.b = b
        self.out = self.root / "quest-graph.json"

    def run_scan(self, *extra):
        return subprocess.run(
            ["dotnet", str(DLL), "scan-quest-facts",
             "--game", str(self.game), "--out", str(self.out), *map(str, extra)],
            text=True, capture_output=True, timeout=90)

    def test_exact_names_overlap_and_dynamic_require_refusal(self):
        old = self.a.read_bytes(), self.b.read_bytes()
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertEqual(j["schemaVersion"], "gco.redscript.quest-fact.graph.v1")
        self.assertEqual(j["sourceFiles"], 2)
        self.assertEqual(j["factCallsites"], 7)
        self.assertEqual(j["literalCallsites"], 6)
        self.assertEqual(j["dynamicOrUnresolvedCallsites"], 1)
        self.assertEqual(j["factsWithCrossOwnerLiteralRegistrations"], 1)
        facts = {x["fact"]: x for x in j["facts"]}
        self.assertEqual(set(facts), {"test_shared_fact", "local_own_fact"})
        shared = facts["test_shared_fact"]
        self.assertEqual(shared["literalListenerOwners"], ["OwnerA", "OwnerB"])
        self.assertEqual(shared["sourceReaderOwners"], ["OwnerA", "OwnerB"])
        self.assertEqual(shared["registeredListenerCallsites"], 2)
        self.assertEqual(shared["unregisterCallsites"], 1)
        self.assertFalse(shared["sharedSubscriptionAuthorized"])
        self.assertFalse(shared["everyWriterAndAliasProven"])
        self.assertTrue(all(not x["generationAuthorized"] for x in j["callsites"]))
        self.assertFalse(j["generationAuthorized"])
        self.assertFalse(j["sourceWrites"])
        self.assertEqual((self.a.read_bytes(), self.b.read_bytes()), old)

    def test_stale_capture_or_unsealed_capture_fails_closed(self):
        analysis = self.root / "wrong-capture.json"
        analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "UNVERIFIED"},
            "source": {"sourceStackSha256": "00" * 32}
        }), encoding="utf8")
        r = self.run_scan("--analysis", analysis)
        self.assertNotEqual(r.returncode, 0)
        self.assertIn("CALLBACK_SEMANTICS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      r.stdout + r.stderr)
        self.assertFalse(self.out.exists())

    def test_output_isolation_and_existing_output_refusal(self):
        for illegal in [self.a.parent / "inject.json", self.out]:
            if illegal == self.out:
                illegal.write_text("preserve", encoding="utf8")
            r = self.run_scan("--out", illegal)
            self.assertNotEqual(r.returncode, 0)
            self.assertIn("QUEST_FACT_OUTPUT_NOT_ISOLATED", r.stdout + r.stderr)
        self.assertEqual(self.out.read_text(), "preserve")

    def test_dynamic_expression_cannot_be_called_literal(self):
        self.a.write_text(SOURCE_A.replace(
            'n"test_shared_fact"', 'this.DynamicName()'), encoding="utf8")
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertEqual(j["factsWithCrossOwnerLiteralRegistrations"], 0)
        self.assertGreaterEqual(j["dynamicOrUnresolvedCallsites"], 4)
        self.assertFalse(j["completeProducerAndMutationGraph"])


if __name__ == "__main__":
    unittest.main()
