"""Enhanced Vehicle System Pass 2.0: one PS handle per OnAction invocation.

Preserve current generic player/action snapshots, event behavior and author code;
recognize stock, partial, complete, moved/custom, incompatible and repeat states.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / 'src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll'
TARGET = 'r6/scripts/Enhanced Vehicle System/Enhanced Vehicle System.reds'
STOCK = '''module EVS
@replaceMethod(VehicleComponent)
protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {
  let cycleEvent: ref<UIVehicleRadioCycleEvent>;
  let vehicle: ref<VehicleObject>;
  if this.GetPS().GetIsDestroyed() {
    return false;
  };
  vehicle = this.GetVehicle();
  if !IsDefined(vehicle) { return false; };
  let gi: GameInstance = vehicle.GetGame();
  // GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1 GetName=__gcoActionNameN1
  let __gcoActionNameN1 = ListenerAction.GetName(action);
  let player: ref<PlayerPuppet> = /* GCO-GENERIC:SHARED_PLAYER_SIGNAL_V1 */ SharedProviders.Player(gi);
  if Equals(__gcoActionNameN1, n"VehicleHorn") {
    if this.GetPS().IsHornEnabled() {
      this.GetPS().m_hgyi56_EVS_powerState = !this.GetPS().m_hgyi56_EVS_powerState;
    };
  };
  if AudiowareIsInstalled(gi) { this.UpdateAudio(); };
  return false;
}
'''

class EvsPsTests(unittest.TestCase):
    def setUp(self):
        tmp = tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root = Path(tmp.name)
        self.n = 0

    def generate(self, source=STOCK, relative=TARGET):
        self.n += 1
        game = self.root / f'game{self.n}'
        file = game / relative
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding='utf8')
        output = self.root / f'out{self.n}'
        p = subprocess.run(['dotnet', str(DLL), 'phase2-evs-ps',
                            '--game', str(game), '--out', str(output)],
                           capture_output=True, text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        self.assertEqual(file.read_text('utf8'), source)
        report = json.loads((output/'phase2-result.json').read_text('utf8'))
        generated = output / relative
        return report, generated.read_text('utf8') if generated.exists() else None

    def test_stock_reuses_one_getter_preserves_generic_and_repeats_noop(self):
        report, out = self.generate()
        self.assertEqual(report['status'], 'APPLIED', report)
        self.assertIsNotNone(out)
        self.assertEqual(out.count('this.GetPS()'), 1)
        self.assertIn('let gcoOnActionPS: ref<VehicleComponentPS> = this.GetPS();', out)
        self.assertIn('if !IsDefined(gcoOnActionPS) || gcoOnActionPS.GetIsDestroyed()', out)
        self.assertIn('gcoOnActionPS.m_hgyi56_EVS_powerState = !gcoOnActionPS.m_hgyi56_EVS_powerState', out)
        self.assertIn('GCO-GENERIC:ACTION_EVENT_SNAPSHOT_V1', out)
        self.assertIn('GCO-GENERIC:SHARED_PLAYER_SIGNAL_V1', out)
        self.assertIn('AudiowareIsInstalled(gi)', out)
        second, none = self.generate(out)
        self.assertEqual(second['status'], 'COMPLETE', second)
        self.assertIsNone(none)

    def test_manual_partial_getter_cache_completed(self):
        partial = STOCK.replace('  if this.GetPS().GetIsDestroyed()',
                                '  let ps: ref<VehicleComponentPS> = this.GetPS();\n'
                                '  if !IsDefined(ps) || ps.GetIsDestroyed()')
        report, out = self.generate(partial)
        self.assertEqual(report['status'], 'APPLIED', report)
        self.assertEqual(out.count('this.GetPS()'), 1)
        self.assertIn('if ps.IsHornEnabled()', out)
        self.assertNotIn('let gcoOnActionPS', out)
        again, empty = self.generate(out)
        self.assertEqual(again['status'], 'COMPLETE', again)
        self.assertIsNone(empty)

    def test_markerless_historical_complete(self):
        fully = STOCK.replace('  if this.GetPS().GetIsDestroyed()',
                              '  let ps: ref<VehicleComponentPS> = this.GetPS();\n'
                              '  if !IsDefined(ps) || ps.GetIsDestroyed()')
        fully = fully.replace('this.GetPS().IsHornEnabled()', 'ps.IsHornEnabled()')
        fully = fully.replace('this.GetPS().m_hgyi56_EVS_powerState',
                             'ps.m_hgyi56_EVS_powerState')
        result, out = self.generate(fully)
        self.assertEqual(result['status'], 'COMPLETE', result)
        self.assertIsNone(out)

    def test_relocated_owner_and_unrelated_custom_code(self):
        custom = STOCK.replace('  let cycleEvent:',
                               '  // Custom owner note\n  let customCounter: Int32 = 5;\n  let cycleEvent:')
        custom += '\n@addMethod(VehicleComponent)\npublic func CustomAddon() -> Void {}\n'
        result, out = self.generate(custom,
              'r6/scripts/Enhanced Vehicle System/patches/MyCustom_EV.reds')
        self.assertEqual(result['status'], 'APPLIED', result)
        self.assertIn('Custom owner note', out)
        self.assertIn('func CustomAddon()', out)
        self.assertEqual(out.count('this.GetPS()'), 1)

    def test_incompatible_early_guard_refused_only_this_rule(self):
        changed = STOCK.replace('this.GetPS().GetIsDestroyed()',
                                'this.GetPS().GetIsDisabled()')
        result, out = self.generate(changed)
        self.assertEqual(result['status'], 'REFUSED', result)
        self.assertEqual(result['reason'], 'EVS_PS_EARLY_GUARD_UNPROVEN')
        self.assertIsNone(out)

    def test_absent_owner(self):
        result, out = self.generate('module Other\npublic class Other {}\n',
              'r6/scripts/Other/Other.reds')
        self.assertEqual(result['status'], 'NOT_APPLICABLE', result)
        self.assertIsNone(out)

if __name__ == '__main__':
    unittest.main()
