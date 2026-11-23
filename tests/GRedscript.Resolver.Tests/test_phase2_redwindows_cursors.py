"""RedWindows Pass 8 index cursors: source-state and live-array correctness."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / "src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET = "r6/scripts/RedWindows/RW_Widgets.reds"
STOCK = """module RedWindows
public class RWWidget {
  public let shapes: array<ref<RWShape>>;
  public let labels: array<ref<RWShapeLabel>>;
  public static func Make(id: CName, kind: RWKind) -> ref<RWWidget> {
    let w: ref<RWWidget> = new RWWidget();
    return w;
  }
  public func Drop() -> Void {
    for shape in this.shapes { shape.node = null; }
    for label in this.labels { label.node = null; }
  }
  public func FindShape(shapeId: CName) -> ref<RWShape> {
    for shape in this.shapes {
      if Equals(shape.id, shapeId) { return shape; }
    }
    return null;
  }
  public func FindLabel(labelId: CName) -> ref<RWShapeLabel> {
    for label in this.labels {
      if Equals(label.id, labelId) { return label; }
    }
    return null;
  }
}
"""


class RedWindowsCursorsTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.n = 0

    def generate(self, source, rel=TARGET):
        self.n += 1
        game = self.root / f"game{self.n}"
        file = game / rel
        file.parent.mkdir(parents=True, exist_ok=True)
        file.write_text(source, encoding="utf8")
        output = self.root / f"out{self.n}"
        p = subprocess.run(
            ["dotnet", str(DLL), "phase2-redwindows-cursors",
             "--game", str(game), "--out", str(output)],
            capture_output=True, text=True, timeout=90)
        self.assertEqual(p.returncode, 0, p.stdout + p.stderr)
        report = json.loads((output/"phase2-result.json").read_text("utf8"))
        self.assertEqual(file.read_text("utf8"), source)
        result = output/rel
        return report, result.read_text("utf8") if result.exists() else None

    def check(self, source):
        self.assertIsNotNone(source)
        self.assertEqual(source.count("private let m_shapeFindCursor: Int32;"), 1)
        self.assertEqual(source.count("private let m_labelFindCursor: Int32;"), 1)
        self.assertIn("let count: Int32 = ArraySize(this.shapes);", source)
        self.assertIn("let count: Int32 = ArraySize(this.labels);", source)
        self.assertIn("if start < 0 || start >= count { start = 0; }", source)
        self.assertIn("if IsDefined(shape) && Equals(shape.id, shapeId)", source)
        self.assertIn("if IsDefined(label) && Equals(label.id, labelId)", source)
        self.assertIn("this.m_shapeFindCursor = idx;", source)
        self.assertIn("this.m_labelFindCursor = idx;", source)
        self.assertIn("public func Drop()", source)

    def test_stock_complete_idempotent(self):
        report, output = self.generate(STOCK)
        self.assertEqual(report["status"], "APPLIED", report)
        self.check(output)
        state, empty = self.generate(output)
        self.assertEqual(state["status"], "COMPLETE", state)
        self.assertIsNone(empty)

    def test_partial_field_and_shape_cursor_completes_label(self):
        _, complete = self.generate(STOCK)
        shape_begin = complete.index("  public func FindShape(")
        label_begin = complete.index("  public func FindLabel(")
        old_shape_begin = STOCK.index("  public func FindShape(")
        old_label_begin = STOCK.index("  public func FindLabel(")
        partial = STOCK[:old_shape_begin] + complete[shape_begin:label_begin] + \
            STOCK[old_label_begin:]
        partial = partial.replace(
            "  public let labels: array<ref<RWShapeLabel>>;",
            "  public let labels: array<ref<RWShapeLabel>>;\n"
            "  private let m_shapeFindCursor: Int32;")
        result, output = self.generate(partial)
        self.assertEqual(result["status"], "APPLIED", result)
        self.check(output)

    def test_relocated_with_comments_and_unrelated_method(self):
        changed = STOCK.replace(
            "    for shape in this.shapes {",
            "    // user's stable-order comment\n"
            "    for shape in this.shapes {", 1)
        changed = changed.replace(
            "  public func FindLabel(",
            "  public func UserAction() -> Bool { return true; }\n"
            "  public func FindLabel(")
        result, output = self.generate(
            changed, "r6/scripts/RedWindows/reorganized/WidgetRecords.reds")
        self.assertEqual(result["status"], "APPLIED", result)
        self.check(output)
        self.assertIn("stable-order comment", output)
        self.assertIn("UserAction()", output)

    def test_custom_shape_lookup_refused_but_label_completed(self):
        unsafe = STOCK.replace(
            "if Equals(shape.id, shapeId) { return shape; }",
            "if Equals(shape.id, shapeId) && shape.visible { return shape; }")
        state, output = self.generate(unsafe)
        self.assertEqual(state["status"], "APPLIED", state)
        self.assertIn("FindShape:RW_CURSOR_CUSTOM_SEARCH_UNPROVEN",
                      state["reason"])
        self.assertIn("shape.visible", output)
        self.assertIn("this.m_labelFindCursor = idx;", output)

    def test_absent_mod(self):
        state, out = self.generate("module Other\nclass Other {}\n",
            "r6/scripts/Other/a.reds")
        self.assertEqual(state["status"], "NOT_APPLICABLE")
        self.assertIsNone(out)


if __name__ == "__main__":
    unittest.main()
