"""CET-classification parity contracts for the real REDscript callback topology."""
import json
import subprocess
import tempfile
import unittest
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

WRAPPED = '''module Sample

@wrapMethod(PlayerPuppet)
protected cb func OnGameAttached() -> Bool {
    this.RegisterInputListener(this, n"TEST_ACTION");
    return wrappedMethod();
}

@wrapMethod(PlayerPuppet)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if Router.Handle(this, action) {
        return true;
    }
    return wrappedMethod(action, consumer);
}

public abstract class Router {
    public static func Handle(player: ref<PlayerPuppet>, action: ListenerAction) -> Bool {
        let name = ListenerAction.GetName(action);
        if Equals(name, n"TEST_ACTION") {
            player.QueueEvent(new GameEvent());
            return true;
        }
        return false;
    }
}
'''

LISTENERS = '''module Sample

public class CustomInput extends IScriptable {
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
        let actionName = ListenerAction.GetName(action);
        if !Equals(actionName, n"DO_SOMETHING") { return false; }
        if !IsDefined(this.manager) { return false; }
        this.manager.SetData(actionName);
        return true;
    }
}

public class SomeHeartbeat {
    protected cb func OnUpdate(deltaTime: Float) -> Bool {
        if !this.active { return false; }
        this.Refresh();
        return false;
    }
}

public class DelayedThing extends DelayCallback {
    public func Call() -> Void {
        this.owner.Refresh();
    }
}
'''


@unittest.skipUnless(
    os.environ.get("GCO_REDSCRIPT_RESEARCH_TESTS") == "1",
    "Historical/manual REDscript diagnostic or experimental feature; not a shipped generator contract"
)
class WholeCallbackSemanticContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.game = root / "game"
        script_dir = self.game / "r6/scripts"
        script_dir.mkdir(parents=True)
        self.wrapped = script_dir / "Wrapped.reds"
        self.listener = script_dir / "Listeners.reds"
        self.wrapped.write_text(WRAPPED, encoding="utf8")
        self.listener.write_text(LISTENERS, encoding="utf8")
        self.output = root / "semantics.json"
        self.audit = root / "audit.json"
        self.analysis = root / "analysis.json"

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=120)

    def resolve(self, *extra):
        result = self.cli("resolve-callbacks", "--game", self.game,
                          "--out", self.output, *extra)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(self.output.read_text(encoding="utf8"))

    def test_wrapped_player_callback_is_classified_and_helper_traced(self):
        before = {p.name: p.read_bytes() for p in (self.wrapped, self.listener)}
        data = self.resolve()
        self.assertEqual(data["schemaVersion"], "gco.redscript.callback.semantics.v1")
        self.assertEqual(data["sourceFiles"], 2)
        self.assertGreaterEqual(data["classifiedCallbacks"], 4)
        self.assertGreaterEqual(data["onActionMethods"], 2)
        self.assertGreaterEqual(data["playerPuppetOnActionWrappers"], 1)
        self.assertGreaterEqual(data["callbacksWithForwardedHelpers"], 1)
        wrapped = next(x for x in data["callbacks"]
                       if x["source"]["owner"] == "PlayerPuppet"
                       and x["source"]["method"] == "OnAction")
        self.assertEqual(wrapped["declaration"]["annotation"], "wrapMethod")
        self.assertTrue(wrapped["declaration"]["playerPuppetOnActionWrapper"])
        self.assertIn("Router.Handle", wrapped["action"]["forwardedHelpers"])
        self.assertIn("TEST_ACTION", wrapped["action"]["candidateLiteralNames"])
        self.assertTrue(wrapped["action"]["wrappedMethodInvoked"])
        self.assertIn("WRAPPED_METHOD_ORDER_AND_RETURN_MUST_BE_PRESERVED",
                      wrapped["blockers"])
        self.assertIn("G_INPUT_ROUTE", wrapped["candidateRecipeFamilies"])
        self.assertFalse(wrapped["action"]["routingAutomatable"])
        self.assertFalse(wrapped["action"]["literalNamesAreCompleteSet"])
        self.assertIsNone(wrapped["measurement"]["callbackEntries"])
        self.assertIsNone(wrapped["measurement"]["estimatedSavingsMs"])
        self.assertFalse(data["generationAuthorized"])
        self.assertFalse(data["sourceWrites"])
        self.assertEqual(before,
                         {p.name: p.read_bytes() for p in (self.wrapped, self.listener)})

        input_listener = next(x for x in data["callbacks"]
                              if x["source"]["owner"] == "CustomInput")
        self.assertIn("G_UI_UNCHANGED", input_listener["candidateRecipeFamilies"])
        self.assertIn("UI_VALUE_WIDGET_LIFETIME_NOT_PROVEN", input_listener["blockers"])
        self.assertTrue(any(x["family"] == "ONUPDATE" for x in data["callbacks"]))
        self.assertTrue(any(x["family"] == "DELAY_CALLBACK" for x in data["callbacks"]))

    def test_dynamic_forwarding_remains_unproven(self):
        self.wrapped.write_text(WRAPPED.replace("Router.Handle(this, action)",
                                               "DynamicHandler.Handle(this, action)"))
        data = self.resolve()
        wrapped = next(x for x in data["callbacks"]
                       if x["source"]["owner"] == "PlayerPuppet"
                       and x["source"]["method"] == "OnAction")
        self.assertIn("UNRESOLVED_HELPER:DynamicHandler.Handle",
                      wrapped["action"]["unresolvedForwardingOrDynamicCalls"])
        self.assertIn("FORWARDED_HELPER_EFFECTS_OR_BINDING_UNPROVEN",
                      wrapped["blockers"])
        self.assertFalse(wrapped["action"]["routingAutomatable"])

    def test_fake_evidence_refused_not_counted_as_callback_entries(self):
        audit = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(audit.returncode, 0, audit.stderr)
        source = json.loads(self.audit.read_text())
        fake = {
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": "0" * 64},
            "rawTables": {"Developer/RSP_CallSites.csv": {"rows": []}},
            "sourceObservations": []
        }
        self.analysis.write_text(json.dumps(fake))
        result = self.cli("resolve-callbacks", "--game", self.game,
                          "--analysis", self.analysis, "--out", self.output)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(self.output.exists())
        fake["source"]["sourceStackSha256"] = source["sourceStackSha256"]
        self.analysis.write_text(json.dumps(fake))
        data = self.resolve("--analysis", self.analysis)
        self.assertEqual(data["sourceMatchedMeasuredCallbacks"], 0)
        self.assertFalse(data["callbackEntryCountsKnown"])

    def test_output_isolation_and_no_overwrite(self):
        unsafe = self.game / "r6" / "scripts" / "semantics.json"
        result = self.cli("resolve-callbacks", "--game", self.game,
                          "--out", unsafe)
        self.assertNotEqual(result.returncode, 0)
        self.assertFalse(unsafe.exists())
        self.resolve()
        result = self.cli("resolve-callbacks", "--game", self.game,
                          "--out", self.output)
        self.assertNotEqual(result.returncode, 0)


if __name__ == "__main__":
    unittest.main()
