"""Historical Pass 2.0 BrowserExtension journal handle cache contracts."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/BrowserExtension/BrowserExtension.System.reds"
STOCK = '''module BrowserExtension.System
public class BrowserExtensionSystem extends ScriptableSystem {
    private let m_config: ref<BrowserExtensionConfig>;
    private let m_listeners: array<wref<BrowserEventsListener>>;
    private let m_blockedAddress: array<String>;

    public func ShouldBypassBrowserExtension() -> Bool {
        return !this.GetConfig().enabled || this.IsCorpoStartActive() || this.IsEP1EpilogueActive();
    }
    public func IsCorpoStartActive() -> Bool {
        let journal: wref<JournalManager> = GameInstance.GetJournalManager(this.GetGameInstance());
        let corpoStartQuest: wref<JournalQuest> = journal.GetEntryByString(s"quests/main_quest/prologue/q000_corpo", "gameJournalQuest") as JournalQuest;
        let state: gameJournalEntryState = journal.GetEntryState(corpoStartQuest);
        return Equals(state, gameJournalEntryState.Active);
    }
    public func IsEP1EpilogueActive() -> Bool {
        let journal: wref<JournalManager> = GameInstance.GetJournalManager(this.GetGameInstance());
        let corpoStartQuest: wref<JournalQuest> = journal.GetEntryByString(s"ep1/quests/main_quest/q307_tomorrow", "gameJournalQuest") as JournalQuest;
        let state: gameJournalEntryState = journal.GetEntryState(corpoStartQuest);
        return Equals(state, gameJournalEntryState.Active);
    }
}
'''


class BrowserJournalTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.n = 0

    def generate(self, text=None, path=TARGET):
        self.n += 1
        game = self.root / f"game{self.n}"
        if text is not None:
            src = game / path
            src.parent.mkdir(parents=True, exist_ok=True)
            src.write_text(text, encoding="utf8")
        else:
            (game / "r6/scripts").mkdir(parents=True, exist_ok=True)
        out = self.root / f"out{self.n}"
        p = subprocess.run(
            ["dotnet", str(DLL), "phase2-browser-journal", "--game", str(game),
             "--out", str(out)], text=True, capture_output=True, timeout=90)
        self.assertEqual(0, p.returncode, p.stdout + p.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        if text is not None:
            self.assertEqual(text, (game / path).read_text("utf8"))
        staged = out / path
        return report, staged.read_text("utf8") if staged.exists() else None

    def assert_contract(self, text):
        self.assertEqual(1, text.count("private func GetCachedJournal()"))
        self.assertEqual(1, text.count("private let m_journal: wref<JournalManager>"))
        for field in ("m_corpoStartQuest", "m_ep1EpilogueQuest"):
            self.assertEqual(1, text.count(f"private let {field}: wref<JournalQuest>"))
            self.assertIn(f"journal.GetEntryState(this.{field})", text)
            self.assertIn(f"if !IsDefined(this.{field})", text)
        self.assertEqual(2, text.count("this.GetCachedJournal();"))
        self.assertEqual(1, text.count("GameInstance.GetJournalManager"))
        self.assertEqual(2, text.count("return Equals(state, gameJournalEntryState.Active)"))

    def test_postgeneric_stock_one_run_and_repeat_noop(self):
        state, output = self.generate(STOCK)
        self.assertEqual("APPLIED", state["status"], state)
        self.assert_contract(output)
        again, staged = self.generate(output)
        self.assertEqual("COMPLETE", again["status"], again)
        self.assertIsNone(staged)

    def test_partial_one_historical_reader_completed(self):
        _, done = self.generate(STOCK)
        first = STOCK.index('    public func IsEP1EpilogueActive()')
        first_done = done.index('    public func IsEP1EpilogueActive()')
        partial = done[:first_done] + STOCK[first:]
        state, fixed = self.generate(partial)
        self.assertEqual("APPLIED", state["status"], state)
        self.assert_contract(fixed)
        self.assertEqual("COMPLETE", self.generate(fixed)[0]["status"])

    def test_partial_missing_helper_and_field(self):
        _, done = self.generate(STOCK)
        start = done.index('  private func GetCachedJournal()')
        end = done.index('  }', start) # the inner if block
        end = done.index('  }', end + 3) + 3  # method closing
        partial = done[:start] + done[end:]
        partial = partial.replace('  private let m_journal: wref<JournalManager>;', '')
        state, fixed = self.generate(partial)
        self.assertEqual("APPLIED", state["status"], state)
        self.assert_contract(fixed)

    def test_markerless_historical_is_complete(self):
        _, done = self.generate(STOCK)
        state, output = self.generate(done.replace('\n\n', '\n'))
        self.assertEqual("COMPLETE", state["status"], state)
        self.assertIsNone(output)

    def test_modified_quest_path_and_unrelated_source_preserved(self):
        custom = STOCK.replace('q307_tomorrow', 'q307_custom_final')
        custom = custom.replace('    public func ShouldBypassBrowserExtension()',
                                '    private let userField: Int32;\n'
                                '    public func ShouldBypassBrowserExtension()')
        custom += '\n'
        state, output = self.generate(custom,
            'r6/scripts/BrowserExtension/newer/BrowserExtensionSystem.reds')
        self.assertEqual('APPLIED', state['status'], state)
        self.assert_contract(output)
        self.assertIn('q307_custom_final', output)
        self.assertIn('private let userField: Int32;', output)

    def test_changed_state_query_refuses_only_this_operation(self):
        unsafe = STOCK.replace('journal.GetEntryState(corpoStartQuest);',
                               'journal.GetEntryState(this.GetCustomQuest());', 1)
        state, output = self.generate(unsafe)
        self.assertEqual('REFUSED', state['status'], state)
        self.assertIn('BEX_JOURNAL_ENTRY_HAS_OTHER_READERS', state['reason'])
        self.assertIsNone(output)

    def test_absent_owner_not_applicable(self):
        state, output = self.generate('module Other\npublic class Foo {}',
                                      'r6/scripts/Other/Other.reds')
        self.assertEqual('NOT_APPLICABLE', state['status'])
        self.assertIsNone(output)


if __name__ == '__main__':
    unittest.main()
