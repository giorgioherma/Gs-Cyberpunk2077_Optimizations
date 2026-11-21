"""RedWindows retained Find historical pass-9 source-state acceptance."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/RedWindows/RW_Window.reds"
STOCK = """module RedWindows
public class RWWindow {
  private let m_widgets: array<ref<RWWidget>>;

  private func Find(id: CName) -> ref<RWWidget> {
    for widget in this.m_widgets {
      if Equals(widget.id, id) { return widget; }
    }
    return null;
  }
  private func Ensure(id: CName, kind: RWKind) -> ref<RWWidget> {
    let widget: ref<RWWidget> = this.Find(id);
    if IsDefined(widget) { return widget; }
    widget = RWWidget.Make(id, kind);
    ArrayPush(this.m_widgets, widget);
    return widget;
  }
  private func EnsureSpan(id: CName, kind: RWKind, rows: Int32) -> ref<RWWidget> {
    let widget: ref<RWWidget> = RWWidget.Make(id, kind);
    ArrayPush(this.m_widgets, widget);
    return widget;
  }
  public func TextArea(id: CName, initial: String, rows: Int32) -> String {
    let widget: ref<RWWidget> = this.Find(id);
    if IsDefined(widget) { return widget.textValue; }
    widget = this.EnsureSpan(id, RWKind.TextArea, rows);
    return widget.textValue;
  }
  public func TextWrapped(id: CName, value: String, rows: Int32) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if !IsDefined(widget) {
      widget = this.EnsureSpan(id, RWKind.Wrapped, rows);
    }
  }
  public func Shapes(id: CName, rows: Int32) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if IsDefined(widget) { return; }
    this.EnsureSpan(id, RWKind.Shapes, rows);
  }
}
"""
PASS5 = """    if IsDefined(this.m_findCache) && Equals(this.m_findCacheID, id) {
      return this.m_findCache;
    }
    for widget in this.m_widgets {
      if Equals(widget.id, id) {
        this.m_findCacheID = id;
        this.m_findCache = widget;
        return widget;
      }
    }
    return null;
"""
PASS8 = """    let count: Int32 = ArraySize(this.m_widgets);
    if count <= 0 { return null; }
    let start: Int32 = this.m_findCursor;
    if start < 0 || start >= count { start = 0; }
    let offset: Int32 = 0;
    while offset < count {
      let idx: Int32 = start + offset;
      if idx >= count { idx -= count; }
      let widget: ref<RWWidget> = this.m_widgets[idx];
      if IsDefined(widget) && Equals(widget.id, id) {
        this.m_findCursor = idx;
        return widget;
      }
      offset += 1;
    }
    return null;
"""


class RedWindowsLookupTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.n = 0

    def generate(self, source, relative=TARGET):
        self.n += 1
        game = self.root / f"game{self.n}"
        dest = game / relative
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_text(source, encoding="utf8")
        out = self.root / f"out{self.n}"
        run = subprocess.run(
            ["dotnet", str(DLL), "phase2-redwindows-find",
             "--game", str(game), "--out", str(out)],
            text=True, capture_output=True, timeout=80)
        self.assertEqual(run.returncode, 0, run.stdout + run.stderr)
        report = json.loads((out / "phase2-result.json").read_text("utf8"))
        self.assertEqual(dest.read_text("utf8"), source)
        produced = out / relative
        return report, produced.read_text("utf8") if produced.exists() else None

    def check(self, output):
        self.assertIsNotNone(output)
        for name in ("m_findCursor", "m_lastFindId", "m_lastFindWidget"):
            self.assertEqual(output.count("private let " + name + ":"), 1)
        self.assertIn("this.m_findCursor = idx + 1;", output)
        self.assertIn("IsDefined(this.m_lastFindWidget)", output)
        self.assertIn("ArrayPush(this.m_widgets, widget);", output)
        self.assertIn("this.EnsureSpan(id", output)

    def test_stock_to_complete_then_noop(self):
        report, generated = self.generate(STOCK)
        self.assertEqual(report["status"], "APPLIED", report)
        self.assertEqual(report["appliedEdits"], 1)
        self.check(generated)
        after, nothing = self.generate(generated)
        self.assertEqual(after["status"], "COMPLETE", after)
        self.assertIsNone(nothing)

    def test_partial_fields_are_completed(self):
        partial = STOCK.replace(
            "  private let m_widgets: array<ref<RWWidget>>;",
            "  private let m_widgets: array<ref<RWWidget>>;\n"
            "  private let m_findCursor: Int32;")
        report, generated = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(generated)

    def test_historical_pass5_is_upgraded_without_duplicate_caches(self):
        beginning = STOCK.index("    for widget in this.m_widgets")
        finish = STOCK.index("  }\n  private func Ensure", beginning)
        legacy = STOCK[:beginning] + PASS5 + STOCK[finish:]
        legacy = legacy.replace(
            "  private let m_widgets: array<ref<RWWidget>>;",
            "  private let m_widgets: array<ref<RWWidget>>;\n"
            "  private let m_findCache: ref<RWWidget>;\n"
            "  private let m_findCacheID: CName;")
        report, generated = self.generate(legacy)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(generated)
        self.assertNotIn("m_findCache", generated)

    def test_historical_pass8_cursor_is_completed(self):
        beginning = STOCK.index("    for widget in this.m_widgets")
        finish = STOCK.index("  }\n  private func Ensure", beginning)
        partial = STOCK[:beginning] + PASS8 + STOCK[finish:]
        partial = partial.replace(
            "  private let m_widgets: array<ref<RWWidget>>;",
            "  private let m_widgets: array<ref<RWWidget>>;\n"
            "  private let m_findCursor: Int32;")
        report, generated = self.generate(partial)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(generated)

    def test_relocated_and_custom_unrelated_method_preserved(self):
        custom = STOCK.replace(
            "    for widget in this.m_widgets {",
            "    /* my comment */ for widget in this.m_widgets {")
        custom = custom.replace("\n}\n", """
  public func UserExtension() -> Int32 { return 42; }
}
""", 1)
        report, output = self.generate(
            custom, "r6/scripts/RedWindows/another/RW_OwnerMoved.reds")
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(output)
        self.assertIn("UserExtension()", output)

    def test_non_append_mutation_narrowly_refused(self):
        unsafe = STOCK.replace(
            "    ArrayPush(this.m_widgets, widget);\n    return widget;",
            "    ArrayClear(this.m_widgets);\n"
            "    ArrayPush(this.m_widgets, widget);\n    return widget;", 1)
        result, output = self.generate(unsafe)
        self.assertEqual(result["status"], "REFUSED", result)
        self.assertIn("RW_WIDGET_ARRAY_MUTATION_NOT_APPEND_ONLY", result["reason"])
        self.assertIsNone(output)

    def test_missing_owner_not_applicable(self):
        result, output = self.generate("module Other\npublic class Other {}\n",
            "r6/scripts/Other/other.reds")
        self.assertEqual(result["status"], "NOT_APPLICABLE", result)
        self.assertIsNone(output)


if __name__ == "__main__":
    unittest.main()
