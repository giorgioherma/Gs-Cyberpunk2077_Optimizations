"""RedWindows historical Pass 9 dormancy: effective overlay visibility."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/"src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
WINDOW="""module RedWindows
public class RWWindow {
  private let m_canvas: wref<inkCanvas>;
  private let m_mgr: wref<RWManager>;
  public let visible: Bool = true;
  public let overlayOnly: Bool;
  public func RefreshVisibility() -> Void {
    if !IsDefined(this.m_canvas) { return; }
    let overlay: Bool = IsDefined(this.m_mgr) && this.m_mgr.IsOverlay();
    let allowed: Bool = !overlay || this.m_mgr.SoloAllows(this);
    this.m_canvas.SetVisible(this.visible && (!this.overlayOnly || overlay) && allowed);
  }
  public func IsVisible() -> Bool { return this.visible; }
}
"""
BUILDER="""module RedWindows
public class RWBuilderApp extends ScriptableSystem {
  public func OnTick() -> Void {
    let builder: ref<RWWindow> = this.BuilderWindow();
    let gallery: ref<RWWindow> = this.GalleryWindow();
    let builderUp: Bool = IsDefined(builder) && builder.IsVisible();
    let galleryUp: Bool = IsDefined(gallery) && gallery.IsVisible();
    if !builderUp && !galleryUp {
      this.ScheduleTick(1.0);
      return;
    }
    if builderUp { this.UpdateBuilder(builder); }
    if galleryUp { this.UpdateGallery(gallery); }
    this.ScheduleTick(0.1);
  }
}
"""
LOGS="""module RedWindows
public class RWLogsApp extends ScriptableSystem {
  public func OnTick() -> Void {
    let win: ref<RWWindow> = this.Manager().FindWindow(n"RedWindows.Logs");
    let delay: Float = 1.0;
    if win.IsVisible() && RedLogger.Available() {
      this.PollLive();
    }
    this.Schedule(delay);
  }
}
"""


class RedWindowsRenderDormancyTests(unittest.TestCase):
    def setUp(self):
        tmp=tempfile.TemporaryDirectory()
        self.addCleanup(tmp.cleanup)
        self.root=Path(tmp.name)
        self.n=0

    def generate(self, files):
        self.n+=1
        game=self.root/f"game{self.n}"
        scripts=game/"r6/scripts"
        scripts.mkdir(parents=True,exist_ok=True)
        for name,source in files.items():
            dest=scripts/name
            dest.parent.mkdir(parents=True,exist_ok=True)
            dest.write_text(source,encoding="utf8")
        out=self.root/f"out{self.n}"
        proc=subprocess.run(
            ["dotnet",str(DLL),"phase2-redwindows-render",
             "--game",str(game),"--out",str(out)],
            capture_output=True,text=True,timeout=90)
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        report=json.loads((out/"phase2-result.json").read_text("utf8"))
        result={}
        for path in (out/"r6/scripts").rglob("*.reds") if (
                out/"r6/scripts").exists() else []:
            result[path.relative_to(out/"r6/scripts").as_posix()]=path.read_text("utf8")
        for name,source in files.items():
            self.assertEqual((scripts/name).read_text("utf8"),source)
        return report,result

    @staticmethod
    def stock():
        return {
            "RedWindows/RW_Window.reds":WINDOW,
            "RedWindows/RW_Builder.reds":BUILDER,
            "RedWindows/RW_Logs.reds":LOGS
        }

    def test_three_source_states_and_repeat_no_changes(self):
        report,out=self.generate(self.stock())
        self.assertEqual(report["status"],"APPLIED",report)
        self.assertEqual(len(report["sourcePaths"]),3)
        window=out["RedWindows/RW_Window.reds"]
        self.assertIn("if !IsDefined(this.m_mgr) { return !this.overlayOnly; }",window)
        self.assertIn("public func IsVisible() -> Bool { return this.visible; }",window)
        self.assertIn("SetVisible(this.visible && (!this.overlayOnly || overlay) && allowed)",window)
        builder=out["RedWindows/RW_Builder.reds"]
        self.assertEqual(builder.count("IsRendered();"),2)
        self.assertIn("this.ScheduleTick(1.0);",builder)
        self.assertIn("this.ScheduleTick(0.1);",builder)
        logs=out["RedWindows/RW_Logs.reds"]
        self.assertIn("if win.IsRendered() && RedLogger.Available()",logs)
        self.assertIn("this.Schedule(delay);",logs)
        complete,nochange=self.generate(out)
        self.assertEqual(complete["status"],"COMPLETE",complete)
        self.assertEqual(nochange,{})

    def test_partial_only_missing_consumer_is_generated(self):
        _,complete=self.generate(self.stock())
        part=dict(complete)
        part["RedWindows/RW_Logs.reds"]=LOGS
        result,out=self.generate(part)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(list(out),["RedWindows/RW_Logs.reds"])

    def test_historical_no_manager_case_corrected(self):
        _,complete=self.generate(self.stock())
        part=dict(complete)
        part["RedWindows/RW_Window.reds"]=part[
            "RedWindows/RW_Window.reds"].replace(
            "return !this.overlayOnly;", "return true;")
        result,out=self.generate(part)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(list(out),["RedWindows/RW_Window.reds"])
        self.assertIn("return !this.overlayOnly;",out["RedWindows/RW_Window.reds"])

    def test_custom_builder_only_refused_independently(self):
        part=self.stock()
        part["RedWindows/RW_Builder.reds"]=part[
            "RedWindows/RW_Builder.reds"].replace(
            "builder.IsVisible()", "builder.CustomVisible()")
        state,out=self.generate(part)
        self.assertEqual(state["status"],"APPLIED",state)
        self.assertIn("RW_RENDER_BUILDER_GUARD_UNPROVEN",state["reason"])
        self.assertNotIn("RedWindows/RW_Builder.reds",out)
        self.assertIn("RedWindows/RW_Logs.reds",out)
        self.assertIn("RedWindows/RW_Window.reds",out)

    def test_custom_refresh_semantics_refuse_entire_operation(self):
        part=self.stock()
        part["RedWindows/RW_Window.reds"]=part[
            "RedWindows/RW_Window.reds"].replace(
            "(!this.overlayOnly || overlay) && allowed",
            "(this.overlayOnly || overlay) && allowed")
        state,out=self.generate(part)
        self.assertEqual(state["status"],"REFUSED",state)
        self.assertIn("RW_RENDER_EFFECTIVE_VISIBILITY_UNPROVEN",state["reason"])
        self.assertEqual(out,{})

    def test_absent_owner(self):
        state,out=self.generate({"Other/Foo.reds":"module Other\nclass Foo {}\n"})
        self.assertEqual(state["status"],"NOT_APPLICABLE")
        self.assertEqual(out,{})


if __name__=="__main__":
    unittest.main()
