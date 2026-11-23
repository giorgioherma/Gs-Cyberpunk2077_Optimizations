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
    mobase.IPluginFileMapper = object
    class Mapping:
        def __init__(self, source, destination, is_directory, create_target=False):
            self.source = source
            self.destination = destination
            self.isDirectory = is_directory
            self.createTarget = create_target
    mobase.Mapping = Mapping
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
    def profileNames(self):
        return ["Heavy", "Other"]
    def profileName(self):
        return "Other"
    def startApplication(self, executable, args, cwd, profile=""):
        self.last_launch = (executable, args, cwd, profile)
        self.mapped = self.mapper.mappings()
        return 123456


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




class TestSelectedProfileOneShotMapper(unittest.TestCase):
    SESSION = "0123456789abcdef0123456789abcdef"

    def create(self, root, module):
        local = root / "local"
        game = root / "game"
        stage = local / "GCO" / "MO2Bridge" / "staging" / self.SESSION
        (game / "bin" / "x64").mkdir(parents=True)
        (game / "bin" / "x64" / "Cyberpunk2077.exe").write_text("fake exe")
        (stage / "bin" / "x64" / "plugins" / "cyber_engine_tweaks").mkdir(parents=True)
        (stage / "bin" / "x64" / "plugins" / "cyber_engine_tweaks.asi").write_text("profiler")
        init = stage / "bin" / "x64" / "plugins" / "cyber_engine_tweaks" / "mods" / "0-Engine" / "init.lua"
        init.parent.mkdir(parents=True)
        init.write_text("source-owned modified 0-Engine")
        red = stage / "red4ext" / "plugins" / "G-REDscript-Profiler.dll"
        red.parent.mkdir(parents=True)
        red.write_text("GRSP")
        return local, game, stage

    def test_noncurrent_profile_mapping_only_during_selected_launch(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                plugins = module.createPlugins()
                self.assertEqual(len(plugins), 2)
                tool, mapper = plugins
                self.assertEqual(mapper.name(), "GCO MO2 temporary profiler mapper")
                self.assertEqual(mapper.mappings(), [])
                organizer = Organizer(root)
                organizer.mapper = mapper
                # MO2 UI profile is 'Other', but request is 'Heavy'.
                selected = tool._launch_controller.prepare(
                    organizer, "Heavy", game, stage, self.SESSION)
                self.assertEqual(selected, "Heavy")
                self.assertEqual(mapper.mappings(), [])
                before = organizer.profileName()
                handle = tool._launch_controller.launch(organizer, "Heavy")
                self.assertEqual(handle, 123456)
                self.assertEqual(before, organizer.profileName())
                self.assertEqual(organizer.last_launch[3], "Heavy")
                self.assertEqual(len(organizer.mapped), 2)
                self.assertEqual(
                    {Path(m.destination).relative_to(game).as_posix() for m in organizer.mapped},
                    {"bin/x64/plugins", "red4ext/plugins"})
                for mapping in organizer.mapped:
                    self.assertTrue(mapping.isDirectory)
                    self.assertFalse(mapping.createTarget)
                    self.assertTrue(Path(mapping.source).is_dir())
                    self.assertTrue(Path(mapping.destination).is_relative_to(game))
                self.assertEqual(mapper.mappings(), [])
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.launch(organizer, "Heavy")

    def test_refuse_different_profile_and_no_leak(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                tool, mapper = module.createPlugins()
                organizer = Organizer(root)
                organizer.mapper = mapper
                tool._launch_controller.prepare(
                    organizer, "Heavy", game, stage, self.SESSION)
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.launch(organizer, "Other")
                self.assertEqual(mapper.mappings(), [])
                self.assertFalse(hasattr(organizer, "last_launch"))
                tool._launch_controller.cancel()

    def test_refuse_unapproved_paths_and_wrong_game(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        organizer, "Heavy", root, stage, self.SESSION)
                foreign = stage / "bin" / "x64" / "Cyberpunk2077.exe"
                foreign.write_text("unapproved binary")
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        organizer, "Heavy", game, stage, self.SESSION)
                foreign.unlink()
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        organizer, "Unknown", game, stage, self.SESSION)
                self.assertEqual(mapper.mappings(), [])

    def test_no_mapping_when_mo2_skips_or_duplicates_call(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                controller = tool._launch_controller
                controller.prepare(organizer, "Heavy", game, stage, self.SESSION)
                organizer.startApplication = lambda *args, **kwargs: 42
                with self.assertRaises(module.GcoOverlayContractError):
                    controller.launch(organizer, "Heavy")
                self.assertEqual(mapper.mappings(), [])

                controller.prepare(organizer, "Heavy", game, stage, self.SESSION)
                def duplicate(*args, **kwargs):
                    mapper.mappings()
                    mapper.mappings()
                    return 42
                organizer.startApplication = duplicate
                with self.assertRaises(module.GcoOverlayContractError):
                    controller.launch(organizer, "Heavy")
                self.assertEqual(mapper.mappings(), [])

    def test_staged_bytes_must_not_change_after_approval(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                controller = tool._launch_controller
                controller.prepare(organizer, "Heavy", game, stage, self.SESSION)
                (stage / "bin" / "x64" / "plugins" / "cyber_engine_tweaks.asi").write_text(
                    "tampered profiler")
                with self.assertRaises(module.GcoOverlayContractError):
                    controller.launch(organizer, "Heavy")
                self.assertEqual(mapper.mappings(), [])
                self.assertFalse(hasattr(organizer, "last_launch"))

    def test_qdir_wrapper_path_is_not_stringified(self):
        module = _load_plugin()
        class FakeQDir:
            def __init__(self, directory):
                self.directory = directory
            def absolutePath(self):
                return str(self.directory)
            def __str__(self):
                return "<PyQt6 QDir representation, not a path>"
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                original = organizer.managedGame
                organizer.managedGame = lambda: types.SimpleNamespace(
                    gameDirectory=lambda: FakeQDir(game),
                    gameName=lambda: "Cyberpunk2077")
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                self.assertEqual(
                    tool._launch_controller.prepare(
                        organizer, "Heavy", game, stage, self.SESSION),
                    "Heavy")
                self.assertEqual(len(tool._launch_controller.mappings()), 0)
                self.assertEqual(
                    tool._launch_controller.launch(organizer, "Heavy"), 123456)

    def test_file_mapper_never_enables_mods(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                # Entire organizer mock intentionally has no setActive, setPriority
                # or setCurrentProfile; using one would raise AttributeError.
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                tool._launch_controller.prepare(
                    organizer, "Heavy", game, stage, self.SESSION)
                tool._launch_controller.launch(organizer, "Heavy")
                self.assertEqual(organizer.profileName(), "Other")
                self.assertEqual(mapper.mappings(), [])


if __name__ == "__main__":
    unittest.main()
