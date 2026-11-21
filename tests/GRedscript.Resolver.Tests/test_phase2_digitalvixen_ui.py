"""DigitalVixenCore historical Pass 9: real source-state generation contracts."""
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[2]
DLL=ROOT/"src/GRedscript.Resolver/bin/Release/net10.0/GRedscript.Resolver.dll"
BASE="r6/scripts/DigitalVixenCore/"

BODY=r'''module DigitalVixenCore
@if(ModuleExists("RedWindows"))
public abstract class DVBodyMapWindow {
  public static func Update(gi: GameInstance) -> Bool {
    let mgr: ref<RWManager> = RWManager.Get(gi);
    if !IsDefined(mgr) { return false; }
    let win: ref<RWWindow> = RedWindowsAPI.Window(gi, n"DigitalVixenCore.HitIndicator", "Hit Indicator");
    if !IsDefined(win) { return false; }
    win.minW = 340.0;
    let cfg: ref<DVCoreHitSettings> = DVCoreHitSettings.Get();
    let overlay: Bool = RedWindowsAPI.IsOverlay(gi);
    let clean: Bool = IsDefined(cfg) && cfg.hitCleanMode;
    let chrome: Bool = !clean || overlay;
    win.SetShowTitle(chrome);
    win.SetFrameVisible(chrome);
    let cfgPinned: Bool = IsDefined(cfg) ? cfg.hitIndicatorPinned : false;
    let pinned: Bool = win.Checkbox(n"hiPin", "Display when overlay closed", cfgPinned);
    if win.Changed(n"hiPin") {
      if IsDefined(cfg) { cfg.hitIndicatorPinned = pinned; }
    }
    let trk: ref<DVHitTracker> = DVHitTracker.Get(gi);
    let layers: array<ref<DVBodyMapLayer>> = DVBodyMap.Layers();
    let now: Float = EngineTime.ToFloat(GameInstance.GetSimTime(gi));
    let suppressed: Bool = !overlay && DVBodyMapWindow.HideRuleActive(gi,cfg,trk,layers,now);
    win.SetOverlayOnly(!pinned || suppressed);
    if suppressed { return win.IsVisible(); }
    win.SetRowVisible(n"hiEdit", true);
    let useArt: Bool = win.Checkbox(n"hiArt","Use sprite art",true);
    if !win.IsVisible() { return false; }
    let female: Bool = DVPlayerState.IsFemale(gi);
    let pieces: array<ref<DVBodyPiece>> = DVBodyMapWindow.LayoutFor(female);
    win.Shapes(n"dvBody",6);
    return true;
  }
}
'''

ITEM=r'''module DigitalVixenCore
@if(ModuleExists("RedWindows"))
public class DVCItemEditorLoop extends DelayCallback {
  public let gi: GameInstance;
  public func Call() -> Void {
    DVCItemEditor.Update(this.gi);
    let cb: ref<DVCItemEditorLoop> = new DVCItemEditorLoop();
    cb.gi = this.gi;
    GameInstance.GetDelaySystem(this.gi).DelayCallback(cb, 0.5, false);
  }
}
@if(ModuleExists("RedWindows"))
public abstract class DVCItemEditor {
  public static func Update(gi: GameInstance) -> Void {
    let mgr: ref<RWManager> = RWManager.Get(gi);
    if !IsDefined(mgr) { return; }
    let win: ref<RWWindow> = RedWindowsAPI.Window(gi, n"DigitalVixenCore.ItemEditor", "Editor");
    if !IsDefined(win) { return; }
    win.SetOverlayOnly(true);
    win.minW = 760.0;
    if !win.IsVisible() { return; }
    let st: ref<DVCItemEditorSystem> = DVCItemEditorSystem.Get(gi);
    if !IsDefined(st) { return; }
    this.CustomUnrelatedLogic();
  }
}
'''
STATUS=r'''module DigitalVixenCore
@if(ModuleExists("RedWindows"))
public class DVCStatusWindowLoop extends DelayCallback {
  public let gi: GameInstance;
  public func Call() -> Void {
    DVCStatusWindow.Update(this.gi);
    let cb: ref<DVCStatusWindowLoop> = new DVCStatusWindowLoop();
    cb.gi = this.gi;
    GameInstance.GetDelaySystem(this.gi).DelayCallback(cb, 0.5, false);
  }
}
@if(ModuleExists("RedWindows"))
public abstract class DVCStatusWindow {
  public static func Update(gi: GameInstance) -> Void {
    let mgr: ref<RWManager> = RWManager.Get(gi);
    if !IsDefined(mgr) { return; }
    let win: ref<RWWindow> = RedWindowsAPI.Window(gi, n"DigitalVixenCore.Status", "Status");
    if !IsDefined(win) { return; }
    let pinned: Bool = win.Checkbox(n"pin", "Pin", false);
    win.SetOverlayOnly(!pinned);
    if !win.IsVisible() { return; }
    let player: ref<PlayerPuppet> = GetPlayer(gi);
    if !IsDefined(player) { return; }
  }
}
'''
RW='module RedWindows\npublic class RWWindow { public func IsRendered() -> Bool { return true; } }\n'
ORIGINAL={
    "DVCore_BodyMapWindow.reds":BODY,
    "DVCore_ItemEditor.reds":ITEM,
    "DVCore_StatusWindow.reds":STATUS
}


class DigitalVixenDormancyTests(unittest.TestCase):
    def setUp(self):
        t=tempfile.TemporaryDirectory()
        self.addCleanup(t.cleanup)
        self.root=Path(t.name)
        self.counter=0

    def run_generation(self,sources=None,rw=True,nested=""):
        self.counter+=1
        game=self.root/f"game{self.counter}"
        sources=ORIGINAL if sources is None else sources
        for name,source in sources.items():
            path=game/BASE/nested/name
            path.parent.mkdir(parents=True,exist_ok=True)
            path.write_text(source,encoding="utf8")
        if rw:
            path=game/"r6/scripts/RedWindows/RW_Window.reds"
            path.parent.mkdir(parents=True,exist_ok=True)
            path.write_text(RW,encoding="utf8")
        out=self.root/f"out{self.counter}"
        p=subprocess.run(["dotnet",str(DLL),"phase2-digitalvixen-ui",
              "--game",str(game),"--out",str(out)],
              text=True,capture_output=True,timeout=120)
        self.assertEqual(p.returncode,0,p.stdout+"\n"+p.stderr)
        report=json.loads((out/"phase2-result.json").read_text("utf8"))
        output={}
        for name,source in sources.items():
            original=game/BASE/nested/name
            self.assertEqual(original.read_text("utf8"),source)
            staged=out/BASE/nested/name
            if staged.exists():
                output[name]=staged.read_text("utf8")
        return report,output

    def complete(self,files):
        b=files["DVCore_BodyMapWindow.reds"]
        self.assertIn("if !overlay && !cfgPinned",b)
        self.assertIn("win.SetOverlayOnly(true);",b)
        self.assertEqual(b.count("win.IsRendered()"),2)
        self.assertLess(b.index("if !overlay && !cfgPinned"),
                        b.index("win.SetShowTitle(chrome)"))
        self.assertIn("DVBodyMapWindow.HideRuleActive(",b)
        self.assertIn("win.Checkbox(n\"hiPin\"",b)
        for name,klass in (("DVCore_ItemEditor.reds","DVCItemEditor"),
                           ("DVCore_StatusWindow.reds","DVCStatusWindow")):
            s=files[name]
            self.assertEqual(s.count("public let initialized: Bool;"),1)
            self.assertIn("if shouldUpdate {",s)
            self.assertIn(klass+".Update(this.gi);",s)
            self.assertIn("let delay: Float = 2.0;",s)
            self.assertIn("delay = 0.5;",s)
            self.assertIn("delay = 0.25;",s)
            self.assertIn("cb.initialized = true;",s)
            self.assertIn("DelayCallback(cb, delay, false)",s)
            self.assertIn("if !win.IsRendered() { return; }",s)
            self.assertIn("mgr.FindWindow(",s)
        status=files["DVCore_StatusWindow.reds"]
        self.assertIn("if this.initialized && IsDefined(mgr) {",status)
        self.assertIn("if mgr.IsOverlay() { delay = 0.5; }",status)
        self.assertIn("win.SetOverlayOnly(!pinned)",status)

    def test_stock_complete_one_run_then_noop(self):
        r,files=self.run_generation()
        self.assertEqual(r["status"],"APPLIED",r)
        self.assertEqual(r["appliedEdits"],7,r)
        self.assertEqual(len(files),3)
        self.complete(files)
        next,empty=self.run_generation(files)
        self.assertEqual(next["status"],"COMPLETE",next)
        self.assertEqual(next["alreadyComplete"],7,next)
        self.assertEqual(empty,{})

    def test_partial_fields_and_visibility_complete_missing_loop(self):
        sources=dict(ORIGINAL)
        sources["DVCore_ItemEditor.reds"]=ITEM.replace(
            "public let gi: GameInstance;",
            "public let gi: GameInstance;\n  public let initialized: Bool;"
        ).replace("if !win.IsVisible()","if !win.IsRendered()")
        sources["DVCore_StatusWindow.reds"]=STATUS.replace(
            "if !win.IsVisible()","if !win.IsRendered()")
        result,files=self.run_generation(sources)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],5,result)
        self.assertEqual(result["alreadyComplete"],2,result)
        self.complete(files)
        self.assertEqual(self.run_generation(files)[0]["status"],"COMPLETE")

    def test_partial_callback_missing_initialized_propagation(self):
        _,full=self.run_generation()
        partial=dict(full)
        partial["DVCore_StatusWindow.reds"]=partial[
            "DVCore_StatusWindow.reds"].replace(
            "    cb.initialized = true;\n","")
        result,output=self.run_generation(partial)
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],1,result)
        self.assertEqual(list(output),["DVCore_StatusWindow.reds"])
        self.assertIn("cb.initialized = true;",output[
            "DVCore_StatusWindow.reds"])

    def test_custom_newer_source_preserved(self):
        sources=dict(ORIGINAL)
        sources["DVCore_ItemEditor.reds"]=ITEM.replace(
            "    this.CustomUnrelatedLogic();",
            "    this.CustomUnrelatedLogic();\n    this.UserExtension();")
        sources["DVCore_BodyMapWindow.reds"]=BODY.replace(
            "    win.minW = 340.0;",
            "    win.minW = 340.0;\n    // custom user-owned layout")
        result,files=self.run_generation(sources,nested="Modern/")
        self.assertEqual(result["status"],"APPLIED",result)
        self.assertEqual(result["appliedEdits"],7,result)
        self.complete(files)
        self.assertIn("this.UserExtension();",files["DVCore_ItemEditor.reds"])
        self.assertIn("custom user-owned layout",files[
            "DVCore_BodyMapWindow.reds"])

    def test_complete_historical_status_loop_upgrades_pinned_behavior(self):
        import re
        _,fully_optimized=self.run_generation()
        legacy=dict(fully_optimized)
        status=legacy["DVCore_StatusWindow.reds"]
        match=re.search(
            r'let shouldUpdate: Bool = !this.initialized;[\s\S]*?(?=if shouldUpdate \{\s*DVCStatusWindow\.Update)',
            status)
        self.assertIsNotNone(match)
        old=match.group(0)
        original_historical='''let shouldUpdate: Bool = !this.initialized;
    let delay: Float = 2.0;
    let mgr: ref<RWManager> = RWManager.Get(this.gi);
    if this.initialized && IsDefined(mgr) && mgr.IsOverlay() {
      delay = 0.5;
      let win: ref<RWWindow> = mgr.FindWindow(n"DigitalVixenCore.Status");
      shouldUpdate = IsDefined(win) && win.IsRendered();
      if shouldUpdate { delay = 0.25; }
    }
    '''
        legacy["DVCore_StatusWindow.reds"]=status.replace(
            old,original_historical,1)
        report,files=self.run_generation(legacy)
        self.assertEqual(report["status"],"APPLIED",report)
        self.assertEqual(report["appliedEdits"],1,report)
        self.assertEqual(list(files),["DVCore_StatusWindow.reds"])
        self.assertIn("if this.initialized && IsDefined(mgr) {",
                      files["DVCore_StatusWindow.reds"])
        self.assertIn("if mgr.IsOverlay() { delay = 0.5; }",
                      files["DVCore_StatusWindow.reds"])
        final_sources=dict(legacy)
        final_sources["DVCore_StatusWindow.reds"]=files["DVCore_StatusWindow.reds"]
        self.assertEqual(self.run_generation(final_sources)[0]["status"],"COMPLETE")

    def test_incompatible_single_loop_skips_only_that_operation(self):
        sources=dict(ORIGINAL)
        sources["DVCore_ItemEditor.reds"]=ITEM.replace(
            "DelayCallback(cb, 0.5, false)",
            "DelayCallback(cb, ComputeCustomSchedule(), false)")
        report,files=self.run_generation(sources)
        self.assertEqual(report["status"],"APPLIED",report)
        self.assertEqual(report["appliedEdits"],6,report)
        self.assertIn("DVC_UI_LOOP_CALLBACK_LIFECYCLE_CHANGED",
                      report["reason"])
        self.assertIn("if !win.IsRendered() { return; }",
            files["DVCore_ItemEditor.reds"])
        self.assertIn("ComputeCustomSchedule()",
            files["DVCore_ItemEditor.reds"])
        self.assertIn("let delay: Float = 2.0;",files[
            "DVCore_StatusWindow.reds"])

    def test_missing_render_helper_narrow_refusal(self):
        result,files=self.run_generation(rw=False)
        self.assertEqual(result["status"],"REFUSED",result)
        self.assertIn("DVC_UI_REQUIRES_REDWINDOWS_ISRENDERED",
                      result["reason"])
        self.assertEqual(files,{})

    def test_absent_owner_not_applicable(self):
        result,files=self.run_generation(
            {"Other.reds":"module DigitalVixenCore\nclass Other {}\n"})
        self.assertEqual(result["status"],"REFUSED",result)
        self.assertEqual(files,{})


if __name__=="__main__":
    unittest.main()
