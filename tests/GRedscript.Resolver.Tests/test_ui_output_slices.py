"""Generic REDscript UI output slices: source proof before caching/observers."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
PROJECT = ROOT / "src/GRedscript.Resolver/GRSP.Resolver.csproj"

SOURCE = """module GenericUiExample
@replaceMethod(buffListGameController)
private final func UpdateBuffDebuffList() -> Void {
    let currBuffLoc: wref<buffListItemLogicController>;
    let buffTimeRemaining: Float;
    let buffTimeTotal: Float;
    let buffList: array<BuffInfo>;
    let data: ref<StatusEffect_Record>;
    let i: Int32 = 0;
    while i < ArraySize(this.m_buffDataList) {
        i += 1;
    }
    currBuffLoc = this.m_buffWidgets[i].GetController()
        as buffListItemLogicController;
    currBuffLoc.SetData(StringToName(data.UiData().IconPath()),
        buffTimeRemaining, buffTimeTotal, Cast<Int32>(buffList[i].stackCount));
    currBuffLoc.SetStatusEffectRecord(null);
    this.m_buffWidgets[i].SetVisible(true);
    this.SendVisibilityUpdate(true, true);
}
"""


class UiOutputSliceTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        if not DLL.exists():
            subprocess.run(
                ["dotnet", "build", str(PROJECT), "-c", "Release",
                 "--nologo"], check=True)

    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.base = Path(temp.name)
        self.game = self.base / "game"
        self.source = self.game / "r6/scripts/AnonymousOwner/UI.reds"
        self.source.parent.mkdir(parents=True)
        self.source.write_text(SOURCE, encoding="utf8")
        self.out = self.base / "ui.json"

    def scan(self, *extra, out=None):
        return subprocess.run(
            ["dotnet", str(DLL), "scan-ui-outputs",
             "--game", str(self.game), "--out", str(out or self.out),
             *map(str, extra)], text=True, capture_output=True, timeout=90)

    def test_mixed_time_and_status_inputs_are_separate_and_never_skippable(self):
        original = self.source.read_bytes()
        p = self.scan()
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        r = json.loads(self.out.read_text())
        self.assertEqual(r["schemaVersion"], "gco.redscript.ui-output.slices.v1")
        self.assertEqual(r["sourceFiles"], 1)
        self.assertEqual(r["setDataSites"], 1)
        self.assertEqual(r["mixedStructuralTimeSites"], 1)
        self.assertEqual(r["sourceTypedReceiverSites"], 1)
        site = r["outputs"][0]
        self.assertEqual(site["receiverSourceDeclaredType"],
                         "buffListItemLogicController")
        self.assertEqual(site["source"]["owner"], "buffListGameController")
        self.assertEqual(site["argumentCount"], 4)
        self.assertTrue(site["arguments"][0]["statusRecordLexemes"])
        self.assertTrue(site["arguments"][1]["liveTimeLexemes"])
        self.assertTrue(site["arguments"][2]["liveTimeLexemes"])
        self.assertTrue(site["arguments"][3]["stackOrCountLexemes"])
        self.assertTrue(site["hasIndependentOutputs"])
        self.assertIn("SetVisible", site["otherOutputCallsInMethod"])
        self.assertIn("SetStatusEffectRecord",
                      site["otherOutputCallsInMethod"])
        self.assertIn("SendVisibilityUpdate",
                      site["otherOutputCallsInMethod"])
        self.assertTrue(site["mixedStructuralAndTimeInputs"])
        self.assertFalse(site["uiSetterImplementationSourceProven"])
        self.assertFalse(site["fullValueTupleEqualityProven"])
        self.assertFalse(site["widgetGenerationAndLifecycleProven"])
        self.assertFalse(site["timerAndAnimationSemanticsProven"])
        self.assertFalse(site["suppressSetterAuthorized"])
        self.assertFalse(site["suppressWholeRefreshAuthorized"])
        self.assertFalse(site["generationAuthorized"])
        self.assertTrue(r["noCountOnlyOrNthFrameThrottlingAllowed"])
        self.assertFalse(r["generationAuthorized"])
        self.assertFalse(r["sourceWrites"])
        self.assertEqual(r["generatedFiles"], 0)
        self.assertEqual(original, self.source.read_bytes())

    def test_identical_count_does_not_prove_unchanged_tuple_or_widget(self):
        changed = SOURCE.replace(
            '    currBuffLoc.SetStatusEffectRecord(null);',
            '    let structuralCount = ArraySize(this.m_buffDataList);\n'
            '    currBuffLoc.SetStatusEffectRecord(null);')
        self.source.write_text(changed, encoding="utf8")
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        j = json.loads(self.out.read_text())
        self.assertEqual(j["setDataSites"], 1)
        site = j["outputs"][0]
        self.assertTrue(site["mixedStructuralAndTimeInputs"])
        self.assertFalse(site["fullValueTupleEqualityProven"])
        self.assertFalse(site["sourceChangePublisherComplete"])
        self.assertFalse(site["suppressSetterAuthorized"])

    def test_a_receiver_in_another_method_never_proves_this_one(self):
        self.source.write_text("""module GenericUiExample
public class Owner extends IScriptable {
    public func SomeOtherMethod() -> Void {
        let output: wref<buffListItemLogicController>;
    }
    public func Update(timeRemaining: Float) -> Void {
        output.SetData(n"icon", timeRemaining, 1.0, 1);
    }
}
""", encoding="utf8")
        r = self.scan()
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr)
        j = json.loads(self.out.read_text())
        self.assertEqual(j["setDataSites"], 1)
        self.assertIsNone(j["outputs"][0]["receiverSourceDeclaredType"])
        self.assertFalse(j["outputs"][0]["receiverRuntimeIdentityProven"])
        self.assertFalse(j["outputs"][0]["generationAuthorized"])

    def test_stale_unsealed_capture_fails_closed_without_output(self):
        analysis = self.base / "stale.json"
        analysis.write_text(json.dumps({
            "schemaVersion": "grsp.resolver.input.v1",
            "generationAuthorized": False,
            "capture": {"collectionProvenance": "UNVERIFIED"},
            "source": {"sourceStackSha256": "00" * 32}
        }), encoding="utf8")
        p = self.scan("--analysis", analysis)
        self.assertNotEqual(p.returncode, 0)
        self.assertIn("CALLBACK_SEMANTICS_UNVERIFIED_CAPTURE_OR_SOURCE_DRIFT",
                      p.stdout + p.stderr)
        self.assertFalse(self.out.exists())

    def test_output_cannot_touch_live_game_or_overwrite(self):
        original = self.source.read_bytes()
        forbidden = self.source.parent / "ui-report.json"
        p = self.scan(out=forbidden)
        self.assertNotEqual(p.returncode, 0)
        self.assertIn("UI_OUTPUT_ISOLATION_FAILED", p.stdout + p.stderr)
        self.assertFalse(forbidden.exists())
        self.out.write_text("SENTINEL")
        p = self.scan()
        self.assertNotEqual(p.returncode, 0)
        self.assertIn("UI_OUTPUT_ISOLATION_FAILED", p.stdout + p.stderr)
        self.assertEqual(self.out.read_text(), "SENTINEL")
        self.assertEqual(original, self.source.read_bytes())


if __name__ == "__main__":
    unittest.main()
