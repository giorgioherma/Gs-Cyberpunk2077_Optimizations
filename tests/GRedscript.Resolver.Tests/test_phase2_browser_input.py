"""Historical Pass 2.0 BrowserExtension action-specific InputHub plus exclusive engine fallback.

Tests source-state convergence, partial migration, unrelated code preservation,
missing-runtime refusal, source immutability and repeated generation.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
CTRL = "r6/scripts/BrowserExtension/browserController.overrides.reds"
LISTENER = "r6/scripts/BrowserExtension/HomePagePagination.reds"
RUNTIME = "r6/scripts/G-RedRuntime/Input/InputHub.reds"

CONTROLLER = '''module BrowserExtension
import BrowserExtensionModSettings.*
@addField(BrowserGameController)
private let m_customBrowserInputListener: ref<CustomBrowserInputListener>;
@wrapMethod(BrowserGameController)
protected cb func OnInitialize() -> Bool {
    let ret: Bool = wrappedMethod();
    this.m_customBrowserInputListener = new CustomBrowserInputListener();
    this.m_customBrowserInputListener.Init(this);
    this.GetPlayerControlledObject().RegisterInputListener(this.m_customBrowserInputListener);
    return ret;
}
@wrapMethod(BrowserGameController)
protected cb func OnUninitialize() -> Bool {
    let ret: Bool = wrappedMethod();
    this.GetPlayerControlledObject().UnregisterInputListener(this.m_customBrowserInputListener);
    return ret;
}
'''

LIST = '''module HomePagePagination
public class CustomBrowserInputListener {
    private let m_gameController: wref<BrowserGameController>;
    public let m_pageNumber: Int32 = 0;
    public final func Init(gameController: ref<BrowserGameController>) -> Void {
        this.m_gameController = gameController;
    }
    protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
        if !this.m_gameController.IsHomePage() { return false; };
        // GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1 GetName=__gcoActionNameN1
        let __gcoActionNameN1 = ListenerAction.GetName(action);
        if Equals(__gcoActionNameN1, n"UI_MoveDown") {
            if ListenerAction.IsButtonJustPressed(action) {
                this.m_pageNumber = this.m_gameController.HomePageScrollDown();
            };
        } else {
            if Equals(__gcoActionNameN1, n"UI_MoveUp") {
                if ListenerAction.IsButtonJustPressed(action) {
                    this.m_pageNumber = this.m_gameController.HomePageScrollUp();
                };
            };
        };
    }
}
'''

FRAMEWORK = '''module GRedRuntime
public class InputListener extends IScriptable {
    public func OnGRedInput(evt: ref<InputEvent>) -> Void {}
}
public class InputHub extends IScriptable {
    public func Subscribe(actionName: CName, listener: ref<InputListener>) -> Int32 { return 1; }
    public func Unsubscribe(id: Int32) -> Bool { return true; }
}
public class Runtime extends ScriptableSystem {
    public static func Get(game: GameInstance) -> ref<Runtime> { return null; }
    public func IsReady() -> Bool { return true; }
    public func GetInputHub() -> ref<InputHub> { return null; }
}
'''


class BrowserInputTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.serial = 0

    def generate(self, controller=CONTROLLER, listener=LIST,
                 include_runtime=True, custom_ctrl=CTRL, custom_listener=LISTENER,
                 framework_path=RUNTIME):
        self.serial += 1
        game = self.root / f"game{self.serial}"
        expected = {custom_ctrl: controller, custom_listener: listener}
        for path, content in expected.items():
            target = game / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf8")
        if include_runtime:
            target = game / framework_path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(FRAMEWORK, encoding="utf8")
        out = self.root / f"output{self.serial}"
        proc = subprocess.run(
            ["dotnet", str(DLL), "phase2-browser-input", "--game", str(game),
             "--out", str(out)], capture_output=True, text=True, timeout=100)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        result = json.loads((out / "phase2-result.json").read_text("utf8"))
        for path, original in expected.items():
            self.assertEqual((game / path).read_text("utf8"), original)
        generated = {path: (out / path).read_text("utf8")
                     for path in expected if (out / path).exists()}
        return result, generated

    def check_complete(self, c, l):
        self.assertEqual(c.count('m_customBrowserInputDownSubscription: Int32'), 1)
        self.assertEqual(c.count('m_customBrowserInputUpSubscription: Int32'), 1)
        self.assertEqual(c.count('m_customBrowserFallbackRegistered: Bool'), 1)
        self.assertEqual(c.count('m_customBrowserFallbackPlayer: wref<GameObject>'), 1)
        self.assertEqual(c.count('Subscribe(n"UI_MoveDown"'), 1)
        self.assertEqual(c.count('Subscribe(n"UI_MoveUp"'), 1)
        self.assertIn('m_customBrowserInputDownSubscription <= 0 || this.m_customBrowserInputUpSubscription <= 0', c)
        self.assertIn('m_customBrowserFallbackPlayer.UnregisterInputListener(', c)
        self.assertIn('this.m_customBrowserInputHub.Unsubscribe(', c)
        self.assertIn('this.m_customBrowserFallbackRegistered = true;', c)
        self.assertIn('class CustomBrowserInputListener extends InputListener', l)
        self.assertIn('func OnGRedInput(evt: ref<InputEvent>)', l)
        self.assertIn('func OnAction(action: ListenerAction', l)
        self.assertIn('gameinputActionType.BUTTON_PRESSED', l)
        self.assertIn('HomePageScrollDown()', l)
        self.assertIn('HomePageScrollUp()', l)

    def test_stock_one_run_and_repeat_noop(self):
        result, out = self.generate()
        self.assertEqual(result['status'], 'APPLIED', result)
        self.assertEqual(result['appliedEdits'], 2)
        self.assertEqual(len(out), 2)
        self.check_complete(out[CTRL], out[LISTENER])
        self.assertIn('GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1', out[LISTENER])
        repeated, outputs = self.generate(out[CTRL], out[LISTENER])
        self.assertEqual(repeated['status'], 'COMPLETE', repeated)
        self.assertFalse(outputs)

    def test_partial_historical_route_completes_fallback_without_duplication(self):
        result, out = self.generate()
        c, l = out[CTRL], out[LISTENER]
        c = c.replace('    ' + 'if this.m_customBrowserInputDownSubscription <= 0 ||',
                      '    ' + 'if this.m_customBrowserInputDownSubscription <= 0 ||')
        # A complete migration with its fallback removed simulates the manually
        # ported historical route, while retaining both InputHub subscriptions.
        from_index = c.index('    if this.m_customBrowserInputDownSubscription <= 0 ||')
        to_index = c.index('    return ret;', from_index)
        c = c[:from_index] + c[to_index:]
        first = c.index('    if this.m_customBrowserFallbackRegistered &&')
        end = c.index('    this.m_customBrowserInputListener = null;', first)
        c = c[:first] + c[end:]
        # Historical listener has OnGRedInput but no original OnAction fallback.
        a = l.index('    protected cb func OnAction(')
        # Use the original generator's stock-like OnAction body before InputHub callback.
        b = l.index('  public func OnGRedInput(', a)
        l = l[:a] + l[b:]
        status, recovered = self.generate(c, l)
        self.assertEqual(status['status'], 'APPLIED', status)
        self.check_complete(recovered[CTRL], recovered[LISTENER])
        again, none = self.generate(recovered[CTRL], recovered[LISTENER])
        self.assertEqual(again['status'], 'COMPLETE', again)
        self.assertFalse(none)

    def test_relocated_owner_and_custom_methods_unchanged(self):
        ctrl = CONTROLLER + '\n@addMethod(BrowserGameController)\npublic func NewTabCustom() -> Bool { return true; }\n'
        listener = LIST.replace('    public let m_pageNumber:',
                                '    private let userCustomColor: Int32 = 7;\n    public let m_pageNumber:')
        cp = 'r6/scripts/BrowserExtension/sub/BrowserController.Custom.reds'
        lp = 'r6/scripts/BrowserExtension/home/BrowserInput.Custom.reds'
        result, out = self.generate(ctrl, listener, custom_ctrl=cp, custom_listener=lp)
        self.assertEqual(result['status'], 'APPLIED', result)
        self.assertIn('func NewTabCustom()', out[cp])
        self.assertIn('userCustomColor', out[lp])
        self.check_complete(out[cp], out[lp])

    def test_relocated_framework_module_is_admitted(self):
        path = 'r6/scripts/AlternateModules/GRedInputCustom.reds'
        result, outputs = self.generate(framework_path=path)
        self.assertEqual(result['status'], 'APPLIED', result)
        self.check_complete(outputs[CTRL], outputs[LISTENER])

    def test_incompatible_event_handler_refuses_both_files(self):
        wrong = LIST.replace('HomePageScrollDown()', 'HomePageScrollLeft()')
        result, out = self.generate(CONTROLLER, wrong)
        self.assertEqual(result['status'], 'REFUSED', result)
        self.assertEqual(result['reason'], 'BEX_INPUT_ORIGINAL_ONACTION_CHANGED')
        self.assertEqual(out, {})

    def test_missing_framework_refuses_without_installing_owner(self):
        result, out = self.generate(include_runtime=False)
        self.assertEqual(result['status'], 'REFUSED', result)
        self.assertEqual(result['reason'], 'BEX_INPUT_RUNTIME_ABI_MISSING')
        self.assertFalse(out)

    def test_absent_mod_no_output(self):
        self.serial += 1
        root = self.root / f"absent{self.serial}"
        p = root / RUNTIME
        p.parent.mkdir(parents=True, exist_ok=True)
        p.write_text(FRAMEWORK, encoding='utf8')
        out = self.root / f"absentoutput{self.serial}"
        proc = subprocess.run(['dotnet', str(DLL), 'phase2-browser-input',
                               '--game', str(root), '--out', str(out)],
                              capture_output=True, text=True)
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        data = json.loads((out / 'phase2-result.json').read_text('utf8'))
        self.assertEqual(data['status'], 'NOT_APPLICABLE')
        self.assertFalse(list(out.rglob('*.reds')))


if __name__ == '__main__':
    unittest.main()
