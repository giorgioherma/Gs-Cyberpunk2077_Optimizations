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
    return GcoMo2Bridge()
