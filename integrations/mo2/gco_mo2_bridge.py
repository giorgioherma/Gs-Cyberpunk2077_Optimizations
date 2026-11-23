"""GCO MO2 read-only discovery companion (Stage 0/1).

Install in MO2's plugins directory as gco_mo2_bridge.py after explicit user setup.
It never installs mods, changes a profile, launches the game, or changes user files.
This is intentionally not the full profiler deployment companion.
"""
import hashlib
import json
import os
from pathlib import Path
import sys
import tempfile
from datetime import datetime, timezone

import mobase
from PyQt6.QtCore import QCoreApplication, QTimer
from PyQt6.QtGui import QIcon
from PyQt6.QtWidgets import QMessageBox


SCHEMA_VERSION = 1
PLUGIN_NAME = "GCO MO2 Bridge"


def _canonical(path):
    # MO2's Python bindings return QDir / QFileInfo for several API paths.
    # str(QDir) is not reliably a filesystem path.
    if hasattr(path, "absoluteFilePath"):
        path = path.absoluteFilePath()
    elif hasattr(path, "absolutePath"):
        path = path.absolutePath()
    return os.path.normcase(os.path.abspath(str(path or "")))


def _bridge_root():
    local = os.environ.get("LOCALAPPDATA")
    if not local:
        raise RuntimeError("LOCALAPPDATA unavailable; GCO discovery cannot publish safely")
    return Path(local) / "GCO" / "MO2Bridge"


def _status_path(instance_root):
    token = hashlib.sha256(_canonical(instance_root).encode("utf-8")).hexdigest()[:24]
    return _bridge_root() / ("instance-" + token + ".json")


def _write_json_atomic(target, data):
    target.parent.mkdir(parents=True, exist_ok=True)
    # Never follow a preexisting status-file symlink/reparse point.
    if target.is_symlink():
        raise RuntimeError("GCO bridge status path is a symlink")
    fd, temporary = tempfile.mkstemp(dir=str(target.parent), prefix=".gco-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(data, stream, sort_keys=True, separators=(",", ":"))
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, target)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def _snapshot(organizer):
    game = organizer.managedGame()
    return {
        "schemaVersion": SCHEMA_VERSION,
        "kind": "gco.mo2.readonly.status",
        "mo2Executable": _canonical(QCoreApplication.applicationFilePath()),
        "instanceRoot": _canonical(organizer.basePath()),
        "gameRoot": _canonical(game.gameDirectory()),
        "gameName": str(game.gameName()),
        "currentProfile": str(organizer.profileName()),
        "currentProfilePath": _canonical(organizer.profilePath()),
        "modsRoot": _canonical(organizer.modsPath()),
        "overwriteRoot": _canonical(organizer.overwritePath()),
        "processId": os.getpid(),
        "updatedUtc": datetime.now(timezone.utc).isoformat(),
        "profileSpecificModMutationProven": False,
        "runtimeNativeOverlayProven": False,
        "installationEnabled": False,
    }



# The file mapper is deliberately a separate MO2 plugin interface.  It extends
# the *selected launched profile*'s virtual mappings without touching profile
# modlists, MO2's current selected profile, or the user's real game.
#
# MO2 source proof (master):
# OrganizerProxy::startApplication -> ProcessRunner(profile) ->
# OrganizerCore::beforeRun -> fileMapping(profileName, overwrite) ->
# active profile source maps, Overwrite, then IPluginFileMapper::mappings().
#
# This module does not expose a command receiver or enable installation:
# admission and an authenticated GCO request channel must come first.
import re


class GcoOverlayContractError(RuntimeError):
    pass


class GcoLaunchMappingController:
    """Strict one-launch plan for a GCO-owned temporary source layer.

    Only approved CET/GRSP profiler support paths are admitted. The controller
    supplies mappings to MO2 immediately before *one* explicitly requested
    launch, then returns no mappings for unrelated launches.
    """

    _HEX_ID = re.compile(r"^[0-9a-f]{32}$")
    _MAX_FILES = 1024
    _MAX_FILE_SIZE = 96 * 1024 * 1024
    _ALLOWED_PARENT_DIRS = (
        ("bin", "x64", "plugins"),
        ("red4ext", "plugins"),
    )
    _CET_ROOT = ("bin", "x64", "plugins", "cyber_engine_tweaks")
    _CET_MODS = _CET_ROOT + ("mods",)
    _RED_ROOT = ("red4ext", "plugins")

    def __init__(self):
        self._plan = None
        self._armed = False
        self._calls = 0

    @staticmethod
    def _digest(path):
        hash_code = hashlib.sha256()
        with open(path, "rb") as stream:
            for data in iter(lambda: stream.read(1024 * 1024), b""):
                hash_code.update(data)
        return hash_code.hexdigest()

    @classmethod
    def _files_unchanged(cls, files):
        return all(
            Path(filename).is_file()
            and not Path(filename).is_symlink()
            and Path(filename).stat().st_size == size
            and cls._digest(filename) == digest
            for filename, _relative, size, digest in files
        )

    @staticmethod
    def _within(root, child):
        return child != root and root in child.parents

    @classmethod
    def _admit(cls, parts):
        lower = tuple(component.casefold() for component in parts)
        if lower == ("bin", "x64", "plugins", "cyber_engine_tweaks.asi"):
            return True
        if lower[:5] == ("bin", "x64", "plugins", "cyber_engine_tweaks", "mods"):
            if len(lower) < 7:
                return False
            sub = lower[5:]
            if sub == ("0-engine", "init.lua"):
                return True
            if len(sub) >= 3 and sub[:2] == ("0-engine", "modules"):
                # Source owner is the only producer of scheduler/integration
                # files. Other 0-Engine modules must never be overlaid.
                return sub[2] in ("scheduler.lua", "cetprofilerscheduler.lua") and len(sub) == 3
            if sub[0] == "cetprofilercontrols":
                return len(sub) >= 2 and all(p and not p.startswith(".") for p in sub) and (
                    lower[-1].endswith(".lua") or lower[-1].endswith(".json")
                )
        if lower[:3] == ("red4ext", "plugins", "g-redscript-profiler.dll") and len(lower) == 3:
            return True
        if len(lower) >= 4 and lower[:3] == ("red4ext", "plugins", "g-redscript-profiler"):
            return all(p and not p.startswith(".") for p in lower[3:]) and (
                lower[-1].endswith(".json") or lower[-1].endswith(".txt")
            )
        return False

    def prepare(self, organizer, selected_profile, game_root, staging_root, session_id):
        if self._plan is not None:
            raise GcoOverlayContractError("An MO2 GCO launch is already prepared")
        if not isinstance(session_id, str) or not self._HEX_ID.fullmatch(session_id):
            raise GcoOverlayContractError("GCO session ID must be an unpredictable 32-digit hex token")
        profiles = tuple(str(name) for name in organizer.profileNames())
        valid = [name for name in profiles if name.casefold() == str(selected_profile).casefold()]
        if len(valid) != 1:
            raise GcoOverlayContractError("Chosen MO2 profile is not uniquely present in this instance")
        selected_profile = valid[0]
        physical_game = Path(_canonical(organizer.managedGame().gameDirectory())).resolve(strict=True)
        claimed_game = Path(_canonical(game_root)).resolve(strict=True)
        if physical_game != claimed_game:
            raise GcoOverlayContractError("Selected MO2 instance does not own requested game root")
        exe = claimed_game / "bin" / "x64" / "Cyberpunk2077.exe"
        if not exe.is_file():
            raise GcoOverlayContractError("Selected MO2 game executable missing")
        requested_root = Path(_canonical(staging_root))
        if requested_root.is_symlink():
            raise GcoOverlayContractError("Session-owned stage cannot be a symlink")
        root = requested_root.resolve(strict=True)
        expected_root = (_bridge_root() / "staging" / session_id).resolve()
        if root != expected_root:
            raise GcoOverlayContractError("Profiler staging root is not the session-owned GCO directory")
        files = []
        allowed_directories = set()
        for source in root.rglob("*"):
            if source.is_symlink():
                raise GcoOverlayContractError("GCO stage contains a symlink")
            if source.is_dir():
                continue
            if not source.is_file() or not self._within(root, source.resolve()):
                raise GcoOverlayContractError("GCO stage contains nonregular or escaping file")
            relative = source.relative_to(root)
            if not self._admit(relative.parts):
                raise GcoOverlayContractError("Unexpected or non-profiler path in GCO stage: " + str(relative))
            if source.stat().st_size > self._MAX_FILE_SIZE:
                raise GcoOverlayContractError("Profiler stage file exceeds bounded size")
            files.append((source, relative))
            if len(files) > self._MAX_FILES:
                raise GcoOverlayContractError("Too many temporary profiler support files")
            for allowed in self._ALLOWED_PARENT_DIRS:
                if tuple(part.casefold() for part in relative.parts[:len(allowed)]) == allowed:
                    allowed_directories.add(allowed)
        if not files:
            raise GcoOverlayContractError("Empty temporary profiler layer cannot be launched")
        # Sealing and approval of produced bytes stays with GCO and source owners.
        # Runtime verifies that mappings still point at the approved physical stage.
        directory_mappings = []
        for parent in sorted(allowed_directories):
            source = root.joinpath(*parent)
            dest = claimed_game.joinpath(*parent)
            if not source.is_dir():
                raise GcoOverlayContractError("Source mapping parent missing: " + str(parent))
            directory_mappings.append((str(source), str(dest)))
        sealed = [
            (str(p), str(rel), p.stat().st_size, self._digest(p))
            for p, rel in files
        ]
        self._plan = (selected_profile, str(exe), directory_mappings,
                      str(root), session_id, sealed)
        self._armed = False
        self._calls = 0
        return selected_profile

    def cancel(self):
        self._plan = None
        self._armed = False
        self._calls = 0

    def launch(self, organizer, selected_profile):
        if self._plan is None or self._armed or self._calls:
            raise GcoOverlayContractError("No unused approved MO2 profiler mapping is prepared")
        profile, exe, _dirs, root, _session, files = self._plan
        if profile != selected_profile:
            raise GcoOverlayContractError("Cannot launch a different MO2 profile than the staged profile")
        if not Path(root).is_dir() or not self._files_unchanged(files):
            raise GcoOverlayContractError("MO2 profiler stage changed before launch")
        # Explicit profile is passed to MO2. It may differ from profileName()
        # (the visible MO2 profile); no setActive(), setPriority(), modlist writes.
        self._armed = True
        try:
            handle = organizer.startApplication(
                exe, [], str(Path(exe).parent), profile=profile)
            if self._calls != 1:
                raise GcoOverlayContractError(
                    "MO2 did not request the one-shot profiler mappings exactly once")
            if not handle:
                raise GcoOverlayContractError("MO2 did not return a valid launched process handle")
            return handle
        finally:
            # Never leave a resident mapping for another run, even if start fails.
            self._armed = False
            self._plan = None

    def mappings(self):
        if not self._armed or self._plan is None:
            return []
        if self._calls:
            raise GcoOverlayContractError("Duplicate file-mapping request during single MO2 launch")
        _profile, _exe, dirs, root, _session, files = self._plan
        if not Path(root).is_dir() or not self._files_unchanged(files):
            raise GcoOverlayContractError("Profiler staging bytes changed while mapping")
        self._calls += 1
        return [
            mobase.Mapping(source=source, destination=destination,
                           is_directory=True, create_target=False)
            for source, destination in dirs
        ]


class GcoMo2ProfilerFileMapper(mobase.IPluginFileMapper):
    """Registered with MO2, but empty outside one authorized launch."""

    def __init__(self, controller):
        super().__init__()
        self._controller = controller

    def name(self):
        return "GCO MO2 temporary profiler mapper"

    def author(self):
        return "G-CET"

    def description(self):
        return "One-shot MO2 virtual profiler mapping; no persistent profile modifications."

    def version(self):
        return mobase.VersionInfo(0, 1, 0)

    def isActive(self):
        return True

    def settings(self):
        return []

    def mappings(self):
        return self._controller.mappings()


class GcoMo2Bridge(mobase.IPluginTool):
    def __init__(self):
        super().__init__()
        self._organizer = None
        self._timer = None
        self._status = None

    def init(self, organizer):
        self._organizer = organizer
        self._publish()
        organizer.onProfileChanged(lambda _old, _new: self._publish())
        self._timer = QTimer()
        self._timer.timeout.connect(self._publish)
        self._timer.start(5000)
        return True

    def _publish(self):
        try:
            snapshot = _snapshot(self._organizer)
            path = _status_path(snapshot["instanceRoot"])
            _write_json_atomic(path, snapshot)
            self._status = str(path)
        except (OSError, RuntimeError, ValueError, AttributeError) as error:
            # Never block MO2 or mutate its profile because status could not be saved.
            self._status = "Not available: " + str(error)

    def name(self):
        return PLUGIN_NAME

    def author(self):
        return "G-CET"

    def description(self):
        return "Read-only MO2 instance and profile discovery for GCO."

    def version(self):
        return mobase.VersionInfo(0, 1, 0)

    def isActive(self):
        return True

    def settings(self):
        return []

    def displayName(self):
        return "GCO MO2 connection status"

    def tooltip(self):
        return "Show the read-only GCO MO2 bridge status."

    def icon(self):
        return QIcon()

    def display(self):
        QMessageBox.information(
            self._parentWidget(),
            "GCO MO2 connection",
            str(self._status or "Status not available. No game/mod files were changed."),
        )


def createPlugin():
    # Historical single-plugin loader remains read-only; release installs the
    # createPlugins() bundle below so MO2 also registers the file mapper.
    return GcoMo2Bridge()


def createPlugins():
    controller = GcoLaunchMappingController()
    tool = GcoMo2Bridge()
    tool._launch_controller = controller
    return [tool, GcoMo2ProfilerFileMapper(controller)]
