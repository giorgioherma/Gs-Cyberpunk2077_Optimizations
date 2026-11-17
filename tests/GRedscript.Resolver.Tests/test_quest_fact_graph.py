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
    public func RegisterWidgetCallback() -> Void {
        this.m_widget.RegisterListener(n"test_shared_fact", this, n"OnClick");
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

    def run_scan(self, *extra, out=None):
        return subprocess.run(
            ["dotnet", str(DLL), "scan-quest-facts",
             "--game", str(self.game), "--out", str(out or self.out),
             *map(str, extra)],
            text=True, capture_output=True, timeout=90)

    def test_exact_names_overlap_and_dynamic_require_refusal(self):
        old = self.a.read_bytes(), self.b.read_bytes()
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertEqual(j["schemaVersion"], "gco.redscript.quest-fact.graph.v1")
        self.assertEqual(j["sourceFiles"], 2)
        self.assertEqual(j["factCallsites"], 8)
        self.assertEqual(j["literalCallsites"], 7)
        self.assertEqual(j["dynamicOrUnresolvedCallsites"], 1)
        self.assertEqual(j["factsWithCrossOwnerLiteralRegistrations"], 1)
        self.assertGreaterEqual(
            j["lexicalListenerCallsitesReceiverUnverified"], 1)
        facts = {x["fact"]: x for x in j["facts"]}
        self.assertEqual(set(facts), {"test_shared_fact", "local_own_fact"})
        shared = facts["test_shared_fact"]
        self.assertEqual(shared["literalListenerOwners"], ["OwnerA", "OwnerB"])
        self.assertEqual(shared["sourceReaderOwners"], ["OwnerA", "OwnerB"])
        self.assertEqual(shared["registeredListenerCallsites"], 2)
        self.assertEqual(shared["unregisterCallsites"], 1)
        self.assertFalse(shared["sharedSubscriptionAuthorized"])
        self.assertFalse(shared["everyWriterAndAliasProven"])
        self.assertEqual(j["directRefreshFactReaderMethods"], 2)
        self.assertEqual(j["dynamicDirectRefreshFactReaderMethods"], 1)
        self.assertEqual(j["sourceMatchedDirectRefreshFactReaderMethods"], 0)
        self.assertFalse(j["anyRefreshFullDependencyClosureProven"])
        readers = {x["method"]: x for x in j["directRefreshReaders"]}
        self.assertEqual(set(readers), {"Update", "Refresh"})
        self.assertEqual(readers["Update"]["directFactReads"], 2)
        self.assertEqual(readers["Update"]["dynamicFactReads"], 1)
        self.assertEqual(readers["Update"]["exactLiteralFactNameHints"], ["test_shared_fact"])
        self.assertIn("m_quests", readers["Update"]["directInstanceFields"])
        self.assertTrue(all(
            not x["safeToReplaceGetterOrSkipMethod"] and
            not x["completeTransitiveDependencyClosure"] and
            not x["generationAuthorized"] for x in j["directRefreshReaders"]))
        self.assertTrue(all(not x["generationAuthorized"] for x in j["callsites"]))
        self.assertFalse(j["generationAuthorized"])
        self.assertFalse(j["sourceWrites"])
        self.assertEqual((self.a.read_bytes(), self.b.read_bytes()), old)

    def test_bidirectional_cohort_separates_shared_fact_from_other_state(self):
        # Both methods read the same exact fact. Update also has a dynamic
        # fact and instance state; Refresh has widget/member state. The common
        # subscriber input is only the fact, NEVER the whole method.
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertTrue(j["partialObservationCohortsAreResearchOnly"])
        self.assertTrue(j["cannotInferOriginalGetterCanBeSkipped"])
        self.assertFalse(j["anySubscriptionOrPartialRewriteAuthorized"])
        self.assertGreaterEqual(j["observedLiteralFactReadSites"], 3)
        cohort = next(x for x in j["questSubscriberCohorts"]
                      if x["fact"] == "test_shared_fact")
        self.assertEqual(cohort["distinctReaderMethods"], 2)
        self.assertEqual(cohort["distinctReaderOwners"], 2)
        self.assertEqual(cohort["refreshMethods"], 2)
        self.assertEqual(cohort["methodsWithOtherDependencyHints"], 2)
        self.assertTrue(cohort["sharesNameAcrossOwners"])
        self.assertFalse(cohort["sharedSubscriptionAuthorized"])
        self.assertFalse(cohort["originalGetterReplacementAuthorized"])
        self.assertFalse(cohort["originalMethodSkipAuthorized"])
        reads = [x for x in j["questSubscriberReads"]
                 if x["fact"] == "test_shared_fact"]
        self.assertEqual(len(reads), 2)
        self.assertTrue(all(x["mixedDependencies"] for x in reads))
        self.assertTrue(all(not x["canSkipOriginalMethod"]
                            and not x["canReplaceOriginalGetter"]
                            and not x["generationAuthorized"] for x in reads))
        update = next(x for x in reads if x["source"]["method"] == "Update")
        self.assertTrue(update["otherFactReadsMayBeDynamic"])
        self.assertIn("m_quests", update["instanceFields"])

    def test_same_owner_read_helper_is_visible_but_not_admitted(self):
        wrapper = '''    private const func GetFact(name: CName) -> Int32 {
        let quests: ref<QuestsSystem> = this.GetQuests();
        return IsDefined(quests) ? quests.GetFact(name) : 0;
    }
'''
        modified = SOURCE_A.replace(
            '    protected cb func OnChanged(value: Int32) -> Bool {',
            wrapper + '    protected cb func OnChanged(value: Int32) -> Bool {'
        ).replace(
            '        let unknown = this.m_quests.GetFact(this.DynamicName());',
            '        let unknown = this.m_quests.GetFact(this.DynamicName());\n'
            '        let wrappedValue = this.GetFact(n"test_shared_fact");'
        )
        self.a.write_text(modified, encoding="utf8")
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertGreaterEqual(j["identifiedSameOwnerQuestWrapperReads"], 1)
        cohort = next(x for x in j["questSubscriberCohorts"]
                      if x["fact"] == "test_shared_fact")
        self.assertTrue(cohort["sharesNameAcrossDirectAndWrapperReads"])
        self.assertEqual(cohort["sameOwnerWrapperReadSites"], 1)
        helper = next(x for x in j["questSubscriberReads"]
                      if x["fact"] == "test_shared_fact" and
                      x["readShape"] == "SAME_OWNER_QUEST_WRAPPER_SHAPE")
        self.assertIn("Main.reds:", helper["sourceShapeWitness"])
        # Exact source shape is only evidence for further callgraph proof.
        self.assertFalse(helper["isolatedExpressionProven"])
        self.assertFalse(helper["allWriterAndAliasPathsProven"])
        self.assertFalse(helper["originalExecutionPreserved"])
        self.assertFalse(helper["canReplaceOriginalGetter"])
        self.assertFalse(helper["generationAuthorized"])

    def test_helper_without_exact_owner_and_source_is_not_trusted(self):
        modified = SOURCE_A.replace(
            '        let unknown = this.m_quests.GetFact(this.DynamicName());',
            '        let unknown = this.m_quests.GetFact(this.DynamicName());\n'
            '        let wrappedValue = this.GetFact(n"test_shared_fact");'
        )
        self.a.write_text(modified, encoding="utf8")
        # The helper exists only in another owner, never in this exact class.
        self.b.write_text(SOURCE_B.replace(
            '    public func RegisterWidgetCallback() -> Void {',
            '    private const func GetFact(name: CName) -> Int32 {\n'
            '        let qs: ref<QuestsSystem> = GameInstance.GetQuestsSystem(this.GetGame());\n'
            '        return qs.GetFact(name);\n'
            '    }\n'
            '    public func RegisterWidgetCallback() -> Void {'
        ), encoding="utf8")
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        self.assertGreaterEqual(j["unresolvedSameOwnerHelperReads"], 1)
        helper = next(x for x in j["questSubscriberReads"]
                      if x["fact"] == "test_shared_fact" and
                      x["source"]["owner"] == "SomeFactReader" and
                      x["readShape"] == "UNRESOLVED_SAME_OWNER_HELPER")
        self.assertFalse(helper["canReplaceOriginalGetter"])
        self.assertFalse(helper["generationAuthorized"])

    def test_receiver_type_must_belong_to_method_owner(self):
        # A same-named QuestsSystem member in another class, and an
        # unrelated local typed inside a different method, cannot authorize
        # a quest-fact listener receiver in this class.
        foreign = '''
public class AlienListener extends IScriptable {
    private let m_quests: ref<inkWidget>;
    public func Start() -> Void {
        let token = this.m_quests.RegisterListener(n"test_shared_fact",
            this, n"OnClick");
    }
    public func OtherMethod() -> Void {
        let m_quests: ref<QuestsSystem>;
    }
}
'''
        self.b.write_text(SOURCE_B + foreign, encoding="utf8")
        r = self.run_scan()
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        j = json.loads(self.out.read_text(encoding="utf8"))
        fact = next(x for x in j["facts"]
                    if x["fact"] == "test_shared_fact")
        self.assertEqual(fact["registeredListenerCallsites"], 2)
        alien = [x for x in j["callsites"]
                 if x["Owner"] == "AlienListener"
                 and x["Operation"] == "RegisterListener"]
        self.assertEqual(len(alien), 1)
        self.assertFalse(alien[0]["questReceiverSourceTyped"])
        self.assertFalse(alien[0]["generationAuthorized"])

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
            r = self.run_scan(out=illegal)
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
