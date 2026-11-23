"""Isolated unit tests for the GCO MO2 companion (no MO2 installation needed)."""
import importlib.util
import json
import os
from pathlib import Path
import sys
import tempfile
import types
import unittest
from unittest.mock import patch

PLUGIN = Path(__file__).resolve().parents[2] / "integrations" / "mo2" / "gco_mo2_bridge.py"


def _load_plugin():
    # The real plugin imports MO2's embedded mobase/PyQt6. Test with stubs,
    # not a fabricated capability to manipulate another profile.
    mobase = types.ModuleType("mobase")
    mobase.IPluginTool = object
    mobase.VersionInfo = lambda *args: tuple(args)
    core = types.ModuleType("PyQt6.QtCore")
    core.QCoreApplication = types.SimpleNamespace(
        applicationFilePath=lambda: os.path.join("C:\\", "MO2", "ModOrganizer.exe"))
    class Timer:
        def __init__(self):
            self.timeout = types.SimpleNamespace(connect=lambda callback: None)
        def start(self, milliseconds):
            assert milliseconds >= 1000
    core.QTimer = Timer
    gui = types.ModuleType("PyQt6.QtGui")
    gui.QIcon = object
    widgets = types.ModuleType("PyQt6.QtWidgets")
    widgets.QMessageBox = types.SimpleNamespace(information=lambda *args: None)
    qt = types.ModuleType("PyQt6")
    with patch.dict(sys.modules, {
        "mobase": mobase, "PyQt6": qt, "PyQt6.QtCore": core,
        "PyQt6.QtGui": gui, "PyQt6.QtWidgets": widgets,
    }):
        spec = importlib.util.spec_from_file_location("gco_mo2_bridge_under_test", PLUGIN)
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
    return module


class Organizer:
    def __init__(self, root):
        self.root = root
        self.changed = None
    def managedGame(self):
        return types.SimpleNamespace(gameDirectory=lambda: str(self.root / "game"),
                                     gameName=lambda: "Cyberpunk2077")
    def basePath(self):
        return str(self.root / "instance")
    def profileName(self):
        return "Other"  # GCO-selected profile may be different.
    def profilePath(self):
        return str(self.root / "profiles" / "Other")
    def modsPath(self):
        return str(self.root / "mods")
    def overwritePath(self):
        return str(self.root / "overwrite")
    def onProfileChanged(self, callback):
        self.changed = callback


class TestReadOnlyCompanion(unittest.TestCase):
    def test_reports_real_mo2_api_paths_and_never_mutates_mods(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(root)}):
                plugin = module.createPlugin()
                organizer = Organizer(root)
                self.assertTrue(plugin.init(organizer))
                self.assertIsNotNone(organizer.changed)
                reports = list((root / "GCO" / "MO2Bridge").glob("instance-*.json"))
                self.assertEqual(len(reports), 1)
                data = json.loads(reports[0].read_text(encoding="utf-8"))
                self.assertEqual(data["kind"], "gco.mo2.readonly.status")
                self.assertEqual(data["currentProfile"], "Other")
                self.assertFalse(data["profileSpecificModMutationProven"])
                self.assertFalse(data["runtimeNativeOverlayProven"])
                self.assertFalse(data["installationEnabled"])
                self.assertEqual(data["modsRoot"], module._canonical(organizer.modsPath()))
                self.assertEqual(data["gameRoot"], module._canonical(organizer.managedGame().gameDirectory()))
                organizer.changed(None, None)
                self.assertTrue(reports[0].exists())


if __name__ == "__main__":
    unittest.main()
