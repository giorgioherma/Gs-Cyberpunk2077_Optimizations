"""RedWindows game-tested Pass 8 redundant color/opacity/tint writes.

All four independent exact source operations converge in one generation.
Historical 0.001 opacity tolerance is preserved; compatible custom owner
logic remains unchanged and incompatible individual setters are refused.
"""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/"src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
TARGET="r6/scripts/RedWindows/RW_Window.reds"

STOCK="""module RedWindows
public class RWWindow {
  private static func SameColor(a: HDRColor, b: HDRColor) -> Bool {
    return AbsF(a.Red-b.Red) < 0.001;
  }
  private func Find(id: CName) -> ref<RWWidget> { return null; }

  public func SetShapeColor(id: CName, shapeId: CName, color: HDRColor) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if !IsDefined(widget) { return; }
    let shape: ref<RWShape> = widget.FindShape(shapeId);
    if !IsDefined(shape) { return; }
    shape.fill = color;
    if IsDefined(shape.node) { shape.node.SetTintColor(color); }
    if IsDefined(shape.imgNode) { shape.imgNode.SetTintColor(color); }
  }

  public func SetShapeOpacity(id: CName, shapeId: CName, opacity: Float) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if !IsDefined(widget) { return; }
    let shape: ref<RWShape> = widget.FindShape(shapeId);
    if !IsDefined(shape) { return; }
    shape.opacity = ClampF(opacity, 0.0, 1.0);
    if IsDefined(shape.node) { shape.node.SetOpacity(shape.opacity); }
    if IsDefined(shape.imgNode) { shape.imgNode.SetOpacity(shape.opacity); }
  }

  public func SetShapeBorderOpacity(id: CName, shapeId: CName, opacity: Float) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if !IsDefined(widget) { return; }
    let shape: ref<RWShape> = widget.FindShape(shapeId);
    if !IsDefined(shape) { return; }
    shape.borderOpacity = ClampF(opacity, 0.0, 1.0);
    if IsDefined(shape.borderNode) { shape.borderNode.SetOpacity(shape.borderOpacity); }
  }

  public func SetWidgetColor(id: CName, part: RWPart, color: HDRColor) -> Void {
    let widget: ref<RWWidget> = this.Find(id);
    if !IsDefined(widget) { return; }
    let changed: Bool = false;
    if Equals(part, RWPart.Text) {
      changed = !widget.hasColor || !RWWindow.SameColor(widget.color, color);
      widget.hasColor = true;
      widget.color = color;
      if IsDefined(widget.text) {
        widget.text.SetTintColor(color);
        changed = false;
      }
    }
    if changed && IsDefined(widget.row) {
      this.RebuildRow(widget);
    }
  }
}
"""


class RedWindowsRedundantSetterTests(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root=Path(tmp.name)
        self.n=0

    def generate(self,source,relative=TARGET):
        self.n+=1
        game=self.root/f"game{self.n}"
        file=game/relative
        file.parent.mkdir(parents=True,exist_ok=True)
        file.write_text(source,encoding="utf8")
        out=self.root/f"out{self.n}"
        proc=subprocess.run(
            ["dotnet",str(DLL),"phase2-redwindows-setters",
             "--game",str(game),"--out",str(out)],
            capture_output=True,text=True,timeout=90)
        self.assertEqual(proc.returncode,0,proc.stdout+"\n"+proc.stderr)
        report=json.loads((out/"phase2-result.json").read_text("utf8"))
        self.assertEqual(file.read_text("utf8"),source)
        generated=out/relative
        return report,generated.read_text("utf8") if generated.exists() else None

    def check(self,s):
        self.assertIsNotNone(s)
        self.assertIn("RWWindow.SameColor(shape.fill, color)",s)
        self.assertIn("if AbsF(shape.opacity - clamped) < 0.001 { return; }",s)
        self.assertIn("if AbsF(shape.borderOpacity - clamped) < 0.001 { return; }",s)
        self.assertEqual(s.count("let clamped: Float = ClampF(opacity, 0.0, 1.0);"),2)
        self.assertIn("if changed && IsDefined(widget.text) {",s)
        self.assertIn("this.RebuildRow(widget)",s)
        self.assertIn("shape.node.SetTintColor(color)",s)
        self.assertIn("shape.imgNode.SetOpacity(shape.opacity)",s)
        self.assertIn("shape.borderNode.SetOpacity(shape.borderOpacity)",s)

    def test_stock_four_operations_complete_and_noop(self):
        result,output=self.generate(STOCK)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],4)
        self.check(output)
        repeated,nothing=self.generate(output)
        self.assertEqual(repeated["status"],"COMPLETE",repeated)
        self.assertEqual(repeated["alreadyComplete"],4)
        self.assertIsNone(nothing)

    def test_partial_two_completed_two_missing(self):
        _,full=self.generate(STOCK)
        b=STOCK.index("  public func SetShapeOpacity(")
        done_b=full.index("  public func SetShapeOpacity(")
        partial=full[:done_b]+STOCK[b:]
        state,result=self.generate(partial)
        self.assertEqual(state["status"],"APPLIED",state)
        self.assertEqual(state["appliedEdits"],3)
        self.assertEqual(state["alreadyComplete"],1)
        self.check(result)
        self.assertEqual(self.generate(result)[0]["status"],"COMPLETE")

    def test_partial_clamped_value_missing_guard_completed(self):
        partial=STOCK.replace(
            "    shape.opacity = ClampF(opacity, 0.0, 1.0);",
            "    let clamped: Float = ClampF(opacity, 0.0, 1.0);\n"
            "    shape.opacity = clamped;")
        state,result=self.generate(partial)
        self.assertEqual(state["status"],"APPLIED",state)
        self.check(result)

    def test_compatible_newer_owner_and_comments_preserved(self):
        custom=STOCK.replace(
            "  private func Find(id: CName)",
            "  // user customization must remain\n"
            "  private let themeCache: ref<Theme>;\n"
            "  private func Find(id: CName)")
        custom+="\npublic func UserExtension() -> Bool { return true; }\n"
        state,result=self.generate(custom,
            "r6/scripts/RedWindows/moved/ModernWindow.reds")
        self.assertEqual(state["status"],"APPLIED",state)
        self.check(result)
        self.assertIn("themeCache",result)
        self.assertIn("UserExtension",result)

    def test_incompatible_opacity_does_not_block_other_setters(self):
        custom=STOCK.replace(
            "    shape.opacity = ClampF(opacity, 0.0, 1.0);",
            "    shape.opacity = CustomClamp(opacity);")
        result,output=self.generate(custom)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],3)
        self.assertIn("SetShapeOpacity:",result["reason"])
        self.assertIn("shape.opacity = CustomClamp(opacity);",output)
        self.assertIn("RWWindow.SameColor(shape.fill, color)",output)
        self.assertIn("if changed && IsDefined(widget.text)",output)

    def test_absent_owner_not_applicable(self):
        result,output=self.generate("module Other\nclass Other {}\n",
            "r6/scripts/Other/foo.reds")
        self.assertEqual(result["status"],"NOT_APPLICABLE")
        self.assertIsNone(output)


if __name__=="__main__":
    unittest.main()
