"""Canonical callback-work triage: three independent owners, excluded ranking,
sealed source-linked GRSP callsites and strict read-only behavior."""
import hashlib
import json
import subprocess
import tempfile
import unittest
import os
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

DELEGATE = """module Example
public class {owner}Listener extends IScriptable {{
  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {{
    let service = {owner}System.Get();
    if IsDefined(service) {{
      service.HandleAction(action, consumer);
    }}
    return false;
  }}
}}
"""

NESTED = """module Example
public class NestedListener extends IScriptable {
  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if IsDefined(this.manager) {
      if Equals(ListenerAction.GetName(action), n"UI_MoveDown") {
        this.manager.Scroll(1);
      } else {
        if Equals(ListenerAction.GetName(action), n"UI_MoveUp") {
          this.manager.Scroll(-1);
        }
      }
    }
    return false; // ListenerAction.GetName(action) is not active code
  }
}
"""

GUARDED = """module Example
public class AlreadyGuarded extends IScriptable {
  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
    if !Equals(ListenerAction.GetName(action), n"RECOVER") { return false; }
    let c = GRSInteractionController.Get();
    if !IsDefined(c) { return false; }
    c.Perform();
    return true;
  }
}
"""


@unittest.skipUnless(
    os.environ.get("GCO_REDSCRIPT_RESEARCH_TESTS") == "1",
    "Historical/manual REDscript diagnostic or experimental feature; not a shipped generator contract"
)
class GenericCallbackWorkTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.is_file():
            subprocess.run(["dotnet", "build", str(PROJECT), "-c", "Release",
                            "--nologo"], check=True)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.game = self.root / "game"
        self.files = {}
        for owner in ("Alpha", "Beta", "Gamma", "Reserved"):
            self.put(owner, DELEGATE.format(owner=owner))
        self.put("Nested", NESTED)
        self.put("Guarded", GUARDED)
        self.output = self.root / "generic-callback-work.json"
        self.analysis = self.root / "sealed-analysis.json"
        self.audit = self.root / "source-audit.json"
        self.write_analysis()

    def put(self, owner, content):
        path = self.game / "r6/scripts" / owner / "Input.reds"
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
        self.files[owner] = path

    def cli(self, *args):
        return subprocess.run(["dotnet", str(DLL), *map(str, args)],
                              capture_output=True, text=True, timeout=120)

    def source_sha(self):
        r = self.cli("source-audit", "--game", self.game, "--out", self.audit)
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        return json.loads(self.audit.read_text())["sourceStackSha256"]

    def write_analysis(self, wrong_source=False, measured_owner=None):
        if self.audit.exists():
            self.audit.unlink()
        sha = self.source_sha()
        rows = []
        observations = []
        if measured_owner is not None:
            path = self.files[measured_owner]
            rows.append({"calls": "1200", "exclusive_instrumented_ms": "4.5"})
            observations.append({
                "capturedPath": f"r6/scripts/{measured_owner}/Input.reds",
                "sourceFunction": f"{measured_owner}Listener::OnAction",
                "sourceLine": 5,
                "evidence": {
                    "status": "LINE_METHOD_NAME_MATCH_RECEIVER_UNVERIFIED",
                    "sha256": hashlib.sha256(path.read_bytes()).hexdigest().upper()
                }
            })
        self.analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {
                "collectionProvenance": "GCO_INDEXED_COLLECTED_BYTES_VERIFIED"
            },
            "source": {
                "sourceStackSha256": "0" * 64 if wrong_source else sha
            },
            "rawTables": {
                "Developer/RSP_CallSites.csv": {"rows": rows}
            },
            "sourceObservations": observations
        }), encoding="utf-8")

    def scan(self, excludes=""):
        args = ["scan-callback-work", "--game", self.game,
                "--analysis", self.analysis, "--out", self.output]
        if excludes:
            args += ["--exclude-ranking-owners", excludes]
        return self.cli(*args)

    def test_three_independent_owners_reveal_shared_forward_shape(self):
        originals = {o: path.read_bytes() for o, path in self.files.items()}
        r = self.scan("Reserved")
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        report = json.loads(self.output.read_text())
        self.assertEqual(report["schemaVersion"],
                         "gco.redscript.generic-callback-work.v1")
        self.assertEqual(report["sourceFiles"], 6)
        self.assertEqual(report["analyzedOnActionMethods"], 6)
        self.assertEqual(report["rankedOnActionMethods"], 5)
        self.assertEqual(report["excludedFromRankingMethods"], 1)
        self.assertGreaterEqual(report["crossOwnerPatterns"], 1)
        fam = next(x for x in report["patterns"]
                   if x["pattern"] == "DELEGATES_WITHOUT_LOCAL_SELECTOR")
        self.assertEqual(fam["sourceOwnerFolders"], 3)
        self.assertTrue(fam["crossOwnerStructure"])
        self.assertFalse(fam["optimizationAdmitted"])
        rows = report["callbacks"]
        self.assertEqual(len(rows), 6)
        reserved = next(x for x in rows
                        if x["source"]["sourceOwnerFolder"] == "Reserved")
        self.assertFalse(reserved["rankingIncluded"])
        self.assertFalse(reserved["generationAuthorized"])
        self.assertTrue(any(
            x["source"]["sourceOwnerFolder"] == "Nested" and
            x["actionNameReadSites"] == 2 and
            "REPEATED_ACTION_METADATA" in x["observedPatterns"]
            for x in rows))
        self.assertTrue(any(
            x["source"]["sourceOwnerFolder"] == "Guarded" and
            "ACTION_SELECTOR_WITHOUT_EARLY_ENGINE_LOOKUP" in x["observedPatterns"]
            for x in rows))
        self.assertTrue(all(x["callbackEntries"] is None and
                            x["predictedSavingsMs"] is None and
                            not x["generationAuthorized"] for x in rows))
        self.assertFalse(report["canSkipCallback"])
        self.assertFalse(report["originalSourcesModified"])
        self.assertEqual(originals, {o: p.read_bytes() for o, p in self.files.items()})

    def test_exact_verified_callsite_measurement_is_not_callback_entries(self):
        self.write_analysis(measured_owner="Beta")
        r = self.scan("Reserved")
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        report = json.loads(self.output.read_text())
        self.assertEqual(report["sourceMatchedMeasuredMethods"], 1)
        beta = next(x for x in report["callbacks"]
                    if x["source"]["sourceOwnerFolder"] == "Beta")
        self.assertEqual(beta["sourceMatchedOutgoingVmCallEdges"], 1200)
        self.assertEqual(beta["sourceMatchedCallsiteExclusiveMs"], 4.5)
        self.assertIsNone(beta["irrelevantActionFraction"])
        self.assertIsNone(beta["predictedSavingsMs"])

    def test_rejects_source_drift_and_output_collisions(self):
        self.write_analysis(wrong_source=True)
        r = self.scan()
        self.assertNotEqual(r.returncode, 0)
        self.assertFalse(self.output.exists())
        self.write_analysis()
        self.output = self.files["Alpha"].parent / "report.json"
        r = self.scan()
        self.assertNotEqual(r.returncode, 0)
        self.assertFalse(self.output.exists())

    def test_direct_isaction_and_button_checks_are_not_unfiltered(self):
        path = self.files["Gamma"]
        path.write_text(DELEGATE.format(owner="Gamma").replace(
            "service.HandleAction(action, consumer);",
            'if ListenerAction.IsAction(action, n"TEST") && '
            'ListenerAction.IsButtonJustPressed(action) { '
            'service.PerformAction(); }'
        ), encoding="utf-8")
        self.write_analysis()
        result = self.scan("Reserved")
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        rows = json.loads(self.output.read_text())["callbacks"]
        gamma = next(x for x in rows
                     if x["source"]["sourceOwnerFolder"] == "Gamma")
        self.assertEqual(gamma["directActionPredicateSites"], 2)
        self.assertNotIn("DELEGATES_WITHOUT_LOCAL_SELECTOR",
                         gamma["observedPatterns"])
        self.assertFalse(gamma["generationAuthorized"])

    def test_source_variation_only_affects_relevant_match(self):
        # A custom source edit changes one forwarder; no fixed mod identities.
        p = self.files["Gamma"]
        p.write_text(p.read_text().replace(
            "service.HandleAction(action, consumer);",
            "let current = ListenerAction.GetName(action);"
            " if Equals(current, n\"A\") { service.DoSomething(); }"
        ), encoding="utf-8")
        self.write_analysis()
        r = self.scan("Reserved")
        self.assertEqual(r.returncode, 0, r.stderr + r.stdout)
        report = json.loads(self.output.read_text())
        assert not next(x for x in report["patterns"]
                        if x["pattern"] == "DELEGATES_WITHOUT_LOCAL_SELECTOR")[
                            "crossOwnerStructure"]
        self.assertTrue(all(not x["generationAuthorized"]
                            for x in report["callbacks"]))


if __name__ == "__main__":
    unittest.main()
