"""Isolated unit tests for the GCO MO2 companion (no MO2 installation needed)."""
import importlib.util
import hmac
import hashlib
import time
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
    class RealToolParentContract:
        def _parentWidget(self):
            return "verified-mo2-qt-parent"
    mobase.IPluginTool = RealToolParentContract
    class RealFileMapperInterfaceContract:
        def init(self, organizer):
            raise AssertionError("MO2 IPluginFileMapper requires explicit IPlugin.init implementation")
    mobase.IPluginFileMapper = RealFileMapperInterfaceContract
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
        for name in ("Heavy", "Other"):
            folder = self.root / "profiles" / name
            folder.mkdir(parents=True, exist_ok=True)
            (folder / "modlist.txt").write_text("", encoding="utf-8")
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
    def profileName(self):
        return "Other"
    def startApplication(self, executable, args, cwd, profile=""):
        self.last_launch = (executable, args, cwd, profile)
        self.mapped = self.mapper.mappings()
        return 123456


class TestReadOnlyCompanion(unittest.TestCase):
    def test_status_tool_uses_supported_qt_parent_contract(self):
        module = _load_plugin()
        tool, _mapper = module.createPlugins()
        with patch.object(module.QMessageBox, "information") as info:
            tool.display()
            self.assertEqual(info.call_count, 1)
            self.assertEqual(info.call_args.args[0], "verified-mo2-qt-parent")

    def test_reports_real_mo2_api_paths_and_never_mutates_mods(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(root)}):
                self.assertFalse(hasattr(module, 'createPlugin'))
                plugin, mapper = module.createPlugins()
                self.assertEqual(mapper.mappings(), [])
                organizer = Organizer(root)
                self.assertTrue(plugin.init(organizer))
                self.assertTrue(mapper.init(organizer))
                self.assertIs(mapper._organizer, organizer)
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
                    {Path(m.destination).resolve().relative_to(game.resolve()).as_posix() for m in organizer.mapped},
                    {"bin/x64/plugins", "red4ext/plugins"})
                for mapping in organizer.mapped:
                    self.assertTrue(mapping.isDirectory)
                    self.assertFalse(mapping.createTarget)
                    self.assertTrue(Path(mapping.source).is_dir())
                    self.assertTrue(Path(mapping.destination).resolve().is_relative_to(game.resolve()))
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

    def test_full_owner_generated_overlay_paths_are_supported(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            additional = [
                "bin/x64/plugins/cyber_engine_tweaks/bindings.json",
                "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/Scheduler.lua",
                "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/CETProfilerScheduler.lua",
                "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/G-CET/Scheduler.lua",
                "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/init.lua",
                "bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/CaptureTitle.txt",
                "red4ext/plugins/G-REDscript-Profiler/CaptureTitle.txt",
            ]
            for rel in additional:
                file = stage / Path(rel)
                file.parent.mkdir(parents=True, exist_ok=True)
                file.write_text("owner-generated", encoding="utf-8")
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                tool, mapper = module.createPlugins()
                organizer = Organizer(root)
                organizer.mapper = mapper
                self.assertEqual(
                    tool._launch_controller.prepare(
                        organizer, "Heavy", game, stage, self.SESSION), "Heavy")
                self.assertEqual(tool._launch_controller.launch(organizer, "Heavy"), 123456)
                self.assertEqual(len(organizer.mapped), 2)
                self.assertEqual(mapper.mappings(), [])

    def test_refuse_profile_directory_mismatch_and_missing_modlist(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                (root / "profiles" / "Heavy" / "modlist.txt").unlink()
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        organizer, "Heavy", game, stage, self.SESSION)
                (root / "profiles" / "Heavy" / "modlist.txt").write_text("")
                organizer.profilePath = lambda: str(root / "profiles" / "Heavy")
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        organizer, "Heavy", game, stage, self.SESSION)
                self.assertEqual(mapper.mappings(), [])

    def test_reject_unknown_namespaced_scheduler(self):
        module = _load_plugin()
        with tempfile.TemporaryDirectory() as td:
            root = Path(td)
            local, game, stage = self.create(root, module)
            rogue = stage / "bin/x64/plugins/cyber_engine_tweaks/mods/0-Engine/modules/Other/Scheduler.lua"
            rogue.parent.mkdir(parents=True, exist_ok=True)
            rogue.write_text("unknown")
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                tool, mapper = module.createPlugins()
                with self.assertRaises(module.GcoOverlayContractError):
                    tool._launch_controller.prepare(
                        Organizer(root), "Heavy", game, stage, self.SESSION)
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
            def absoluteFilePath(self, name):
                raise AssertionError("QDir.absoluteFilePath requires fileName, never call without it")
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



class TestAuthenticatedMo2Bridge(unittest.TestCase):
    def test_cross_language_canonical_hmac_vector(self):
        module = _load_plugin()
        signed = {
            "schemaVersion": 1,
            "bridgeEpoch": "a" * 32,
            "requestId": "b" * 32,
            "operation": "probe",
            "profile": "",
            "gameRoot": r"C:\Games\Cyberpunk 2077",
            "stagingRoot": "",
            "sessionId": "",
            "createdUtcMs": 1795460000000,
        }
        actual = module.GcoAuthenticatedMailbox.signature_payload(signed)
        digest = hmac.new(bytes(range(32)), actual, hashlib.sha256).hexdigest()
        self.assertEqual(digest,
            "525b9fa14a79e3417649ef7c99600e59dd278bf672968b478ec487d553c23ddd")

    def fixture(self):
        temp = tempfile.TemporaryDirectory()
        root = Path(temp.name)
        module = _load_plugin()
        local, game, stage = TestSelectedProfileOneShotMapper().create(root, module)
        return temp, root, module, local, game, stage

    @staticmethod
    def request(mailbox, operation="probe", profile="", game="", stage="", session="",
                request_id="11111111111111111111111111111111", signing=True):
        body = {
            "schemaVersion": 1,
            "bridgeEpoch": mailbox.epoch,
            "requestId": request_id,
            "operation": operation,
            "profile": profile,
            "gameRoot": str(game),
            "stagingRoot": str(stage),
            "sessionId": session,
            "createdUtcMs": int(time.time() * 1000),
        }
        signature = hmac.new(mailbox._secret,
                             mailbox.signature_payload(body),
                             hashlib.sha256).hexdigest()
        body["hmac"] = signature if signing else "0" * 64
        mailbox._request_path.write_text(json.dumps(body), encoding="utf-8")
        return body

    @staticmethod
    def read_response(mailbox):
        response = json.loads(mailbox._response_path.read_text(encoding="utf-8"))
        payload = "\n".join([
            str(response["schemaVersion"]), response["bridgeEpoch"],
            response["requestId"], "1" if response["ok"] else "0",
            str(response["processId"]), response["detail"]
        ]).encode("utf-8")
        expected = hmac.new(mailbox._secret, payload, hashlib.sha256).hexdigest()
        assert hmac.compare_digest(response["hmac"], expected)
        return response

    def test_authenticated_probe_and_invalid_signature(self):
        temp, root, module, local, game, stage = self.fixture()
        try:
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                self.assertTrue(tool.init(organizer))
                mailbox = tool._mailbox
                self.assertEqual(len(mailbox._key_path.read_bytes()), 32)
                self.assertTrue(mailbox._status.is_file())
                heartbeat = json.loads(mailbox._status.read_text(encoding="utf-8"))
                self.assertEqual(heartbeat["bridgeEpoch"], mailbox.epoch)
                self.request(mailbox)
                self.assertTrue(mailbox.process_one())
                reply = self.read_response(mailbox)
                self.assertTrue(reply["ok"])
                self.assertEqual(reply["processId"], 0)
                self.assertEqual(mapper.mappings(), [])
                self.request(mailbox, request_id="22222222222222222222222222222222",
                             signing=False)
                self.assertTrue(mailbox.process_one())
                reply = self.read_response(mailbox)
                self.assertFalse(reply["ok"])
                self.assertFalse(hasattr(organizer, "last_launch"))
        finally:
            temp.cleanup()

    def test_signed_selected_profile_launch_while_ui_shows_other(self):
        temp, root, module, local, game, stage = self.fixture()
        try:
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                tool.init(organizer)
                mailbox = tool._mailbox
                self.request(mailbox, "launch", "Heavy", game, stage,
                             TestSelectedProfileOneShotMapper.SESSION)
                self.assertTrue(mailbox.process_one(
                    process_id_for_testing=lambda handle: 4567))
                reply = self.read_response(mailbox)
                self.assertTrue(reply["ok"], reply["detail"])
                self.assertEqual(reply["processId"], 4567)
                self.assertEqual(organizer.profileName(), "Other")
                self.assertEqual(organizer.last_launch[3], "Heavy")
                self.assertEqual(len(organizer.mapped), 2)
                self.assertEqual(mapper.mappings(), [])
        finally:
            temp.cleanup()

    def test_real_mo2_runner_refusal_is_signed_failure_not_timeout(self):
        temp, root, module, local, game, stage = self.fixture()
        try:
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                tool.init(organizer)
                def denied(*args, **kwargs):
                    raise RuntimeError("ERROR_ACCESS_DENIED: real MO2 process runner refused")
                organizer.startApplication = denied
                mailbox = tool._mailbox
                self.request(mailbox, "launch", "Heavy", game, stage,
                             TestSelectedProfileOneShotMapper.SESSION)
                self.assertTrue(mailbox.process_one())
                reply = self.read_response(mailbox)
                self.assertFalse(reply["ok"])
                self.assertEqual(reply["processId"], 0)
                self.assertIn("ERROR_ACCESS_DENIED", reply["detail"])
                self.assertEqual(mapper.mappings(), [])
                self.assertEqual(organizer.profileName(), "Other")
        finally:
            temp.cleanup()

    def test_replay_and_wrong_epoch_cannot_start_game(self):
        temp, root, module, local, game, stage = self.fixture()
        try:
            with patch.dict(os.environ, {"LOCALAPPDATA": str(local)}):
                organizer = Organizer(root)
                tool, mapper = module.createPlugins()
                organizer.mapper = mapper
                tool.init(organizer)
                mailbox = tool._mailbox
                original = self.request(mailbox)
                mailbox.process_one()
                self.assertTrue(self.read_response(mailbox)["ok"])
                # Duplicate request ID is rejected even with a valid signature.
                mailbox._request_path.write_text(json.dumps(original), encoding="utf-8")
                mailbox.process_one()
                self.assertFalse(self.read_response(mailbox)["ok"])
                wrong = self.request(mailbox,
                                     request_id="33333333333333333333333333333333")
                wrong["bridgeEpoch"] = "a" * 32
                wrong["hmac"] = hmac.new(mailbox._secret,
                    mailbox.signature_payload(wrong), hashlib.sha256).hexdigest()
                mailbox._request_path.write_text(json.dumps(wrong), encoding="utf-8")
                mailbox.process_one()
                self.assertFalse(self.read_response(mailbox)["ok"])
                self.assertFalse(hasattr(organizer, "last_launch"))
                self.assertEqual(mapper.mappings(), [])
        finally:
            temp.cleanup()


if __name__ == "__main__":
    unittest.main()
