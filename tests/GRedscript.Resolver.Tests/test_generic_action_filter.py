"""CET-derived source-proven REDscript OnAction literal membership rule.

Synthetic fixtures deliberately use generic owner/module names; production
admission must recognize syntax/dependencies without mod-name allowlists.
"""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

PROVIDER = '''module Generic.Settings

public abstract class FixedActions {
    public static func AllowedNames() -> array<CName> = [n"Primary", n"Secondary"];
}
'''

LISTENER = '''module Generic.Listeners
import Generic.Settings.{FixedActions}

public class SomeInputListener {
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
        let actionName = ListenerAction.GetName(action);
        let allowedNames = FixedActions.AllowedNames();

        if !ArrayContains(allowedNames, actionName) {
            return false;
        }
        if !Equals(ListenerAction.GetType(action), gameinputActionType.BUTTON_HOLD_COMPLETE) {
            return false;
        }
        if Equals(actionName, n"Primary") {
            this.Trigger();
        }
        return false;
    }
}
'''


class ActionFilterGenericContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT),
                            "-c", "Release", "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        root = Path(temp.name)
        self.game = root / "game"
        self.scripts = self.game / "r6" / "scripts"
        self.scripts.mkdir(parents=True)
        self.listener = self.scripts / "Handler.reds"
        self.provider = self.scripts / "Provider.reds"
        self.listener.write_text(LISTENER, encoding="utf8")
        self.provider.write_text(PROVIDER, encoding="utf8")
        self.scan = root / "scan.json"
        self.audit = root / "audit.json"
        self.analysis = root / "analysis.json"
        self.preview = root / "preview"

    def run_cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              text=True, capture_output=True, timeout=90)

    def read_scan(self):
        result = self.run_cli("scan-action-filter", "--game", self.game,
                              "--out", self.scan)
        self.assertEqual(result.returncode, 0, result.stderr)
        return json.loads(self.scan.read_text(encoding="utf8"))

    def prepare_analysis(self):
        result = self.run_cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(result.returncode, 0, result.stderr)
        audited = json.loads(self.audit.read_text(encoding="utf8"))
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"},
            "source": {"sourceStackSha256": audited["sourceStackSha256"]},
        }), encoding="utf8")

    def preview_rule(self):
        return self.run_cli("preview-action-filter",
                            "--game", self.game,
                            "--analysis", self.analysis,
                            "--out", self.preview)

    def test_cross_module_literal_dispatch_and_originals_preserved(self):
        original = self.listener.read_bytes()
        original_provider = self.provider.read_bytes()
        scan = self.read_scan()
        self.assertEqual(scan["schemaVersion"], "gco.redscript.action-filter.scan.v1")
        self.assertEqual(scan["sourceFiles"], 2)
        self.assertEqual(scan["onActionMethods"], 1)
        self.assertEqual(scan["eligible"], 1)
        self.assertFalse(scan["generationAuthorized"])
        self.assertFalse(scan["callbackEntriesProven"])
        self.assertFalse(scan["measuredSavingsClaimed"])

        self.prepare_analysis()
        result = self.preview_rule()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("transformed=1", result.stdout)
        new = self.preview / "r6" / "scripts" / "Handler.reds"
        self.assertTrue(new.is_file())
        staged = new.read_text(encoding="utf8")
        self.assertIn("// GCO-GENERIC:ACTION_LITERAL_MEMBERSHIP_V1:FixedActions.AllowedNames",
                      staged)
        self.assertIn(
            'if !Equals(actionName, n"Primary") && !Equals(actionName, n"Secondary") { return false; }',
            staged)
        self.assertNotIn("let allowedNames =", staged)
        self.assertNotIn("ArrayContains(", staged)
        self.assertIn("this.Trigger();", staged)
        self.assertIn("ListenerAction.GetType(action)", staged)
        self.assertEqual(self.listener.read_bytes(), original)
        self.assertEqual(self.provider.read_bytes(), original_provider)

        # An already applied, intact source is recognized but not re-patched.
        shutil.copyfile(new, self.listener)
        self.scan.unlink()
        after = self.read_scan()
        self.assertEqual(after["eligible"], 0)
        self.assertEqual(after["alreadyComplete"], 1)

    def test_partial_generated_state_refused(self):
        self.prepare_analysis()
        self.assertEqual(self.preview_rule().returncode, 0)
        staged = (self.preview / "r6/scripts/Handler.reds").read_text()
        self.listener.write_text(staged.replace('n"Secondary"', 'n"Surprise"'))
        state = self.read_scan()
        self.assertEqual(state["eligible"], 0)
        self.assertEqual(state["alreadyComplete"], 0)
        self.assertEqual(state["refused"], 1)

    def test_dynamic_provider_and_import_ambiguity_refused(self):
        variants = [
            (PROVIDER.replace('[n"Primary", n"Secondary"]',
                              'SomeRegistry.GetNames()'), LISTENER),
            (PROVIDER, LISTENER.replace(
                "import Generic.Settings.{FixedActions}", "import Generic.Settings.*")),
            (PROVIDER, LISTENER.replace(
                "return false;\n        }\n        if !Equals",
                "return true;\n        }\n        if !Equals")),
            (PROVIDER, LISTENER.replace("ArrayContains(allowedNames, actionName)",
                                        "ArrayContains(allowedNames, this.otherName)")),
            (PROVIDER, LISTENER.replace("let allowedNames = FixedActions.AllowedNames();",
                                        "let allowedNames = this.GetActions();")),
        ]
        for provider, listener in variants:
            with self.subTest(listener=listener[-90:], provider=provider[-90:]):
                self.provider.write_text(provider)
                self.listener.write_text(listener)
                data = self.read_scan()
                self.assertEqual(data["eligible"], 0)
                self.scan.unlink()

    def test_cross_file_wrapped_provider_refused(self):
        (self.scripts / "override.reds").write_text(
            'module Generic.Settings\n'
            '@wrapMethod(FixedActions)\n'
            'public static func AllowedNames() -> array<CName> {\n'
            ' return wrappedMethod();\n'
            '}\n')
        data = self.read_scan()
        self.assertEqual(data["eligible"], 0)
        self.assertGreaterEqual(data["refused"], 1)

    def test_duplicate_class_provider_refused(self):
        (self.scripts / "other.reds").write_text(
            'module SomethingElse\n'
            'public abstract class FixedActions {\n'
            ' public static func AllowedNames() -> array<CName> = [n"One", n"Two"];\n'
            '}\n')
        data = self.read_scan()
        self.assertEqual(data["eligible"], 0)
        self.assertGreaterEqual(data["refused"], 1)

    def test_stale_and_uncollected_capture_refused(self):
        self.prepare_analysis()
        payload = json.loads(self.analysis.read_text())
        payload["source"]["sourceStackSha256"] = "0" * 64
        self.analysis.write_text(json.dumps(payload))
        self.assertNotEqual(self.preview_rule().returncode, 0)
        self.assertFalse(self.preview.exists())

        payload["source"]["sourceStackSha256"] = json.loads(self.audit.read_text())[
            "sourceStackSha256"]
        payload["capture"]["collectionProvenance"] = "UNVERIFIED"
        self.analysis.write_text(json.dumps(payload))
        self.assertNotEqual(self.preview_rule().returncode, 0)
        self.assertFalse(self.preview.exists())

    def test_reject_writing_game_and_existing_output(self):
        path = self.game / "r6" / "action-filter.json"
        output = self.run_cli("scan-action-filter", "--game", self.game,
                              "--out", path)
        self.assertNotEqual(output.returncode, 0)
        self.assertFalse(path.exists())
        self.scan.write_text("{}")
        self.assertNotEqual(self.run_cli(
            "scan-action-filter", "--game", self.game, "--out", self.scan
        ).returncode, 0)


if __name__ == "__main__":
    unittest.main()
