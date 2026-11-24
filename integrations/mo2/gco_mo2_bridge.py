"""GCO MO2 companion: read-only inventory and gated selected-profile launcher.

Install in MO2's plugins directory as gco_mo2_bridge.py after explicit setup.
Registered plugin APIs publish profile facts and provide a one-shot virtual
profiler mapping for a signed GCO launch request. No modlist changes and no
standalone profiler implementation. The GCO production feature remains gated.
"""
import hashlib
import hmac
import secrets
import time
import ctypes
import json
import os
from pathlib import Path
import sys
import tempfile
import subprocess
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
    # QDir exposes BOTH methods but absoluteFilePath(fileName) requires an
    # argument. All MO2 API paths passed here denote directories, so prefer
    # QDir.absolutePath() and only use QFileInfo.absoluteFilePath() when the
    # directory method is unavailable.
    if hasattr(path, "absolutePath"):
        path = path.absolutePath()
    elif hasattr(path, "absoluteFilePath"):
        path = path.absoluteFilePath()
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
        if lower == ("bin", "x64", "plugins", "cyber_engine_tweaks", "bindings.json"):
            return True
        if lower[:5] == ("bin", "x64", "plugins", "cyber_engine_tweaks", "mods"):
            if len(lower) < 7:
                return False
            sub = lower[5:]
            if sub == ("0-engine", "init.lua"):
                return True
            if len(sub) >= 3 and sub[:2] == ("0-engine", "modules"):
                # Authoritative CET owner can generate integrated, adaptive or
                # namespaced G-CET worker. Never accept other Lua modules.
                return (
                    (len(sub) == 3 and
                     sub[2] in ("scheduler.lua", "cetprofilerscheduler.lua"))
                    or (len(sub) == 4 and
                        sub[2:] == ("g-cet", "scheduler.lua"))
                )
            if sub[0] == "cetprofilercontrols":
                return len(sub) >= 2 and all(p and not p.startswith(".") for p in sub) and (
                    lower[-1].endswith(".lua") or lower[-1].endswith(".json")
                    or lower[-1].endswith(".txt")
                )
        if lower[:3] == ("red4ext", "plugins", "g-redscript-profiler.dll") and len(lower) == 3:
            return True
        if len(lower) >= 4 and lower[:3] == ("red4ext", "plugins", "g-redscript-profiler"):
            return all(p and not p.startswith(".") for p in lower[3:]) and (
                lower[-1].endswith(".json") or lower[-1].endswith(".txt")
            )
        return False

    def prepare(self, organizer, selected_profile, game_root, staging_root, session_id,
                physical_passthrough=()):
        if self._plan is not None:
            raise GcoOverlayContractError("An MO2 GCO launch is already prepared")
        if not isinstance(session_id, str) or not self._HEX_ID.fullmatch(session_id):
            raise GcoOverlayContractError("GCO session ID must be an unpredictable 32-digit hex token")
        # Official IOrganizer exposes current profileName() and profilePath(),
        # NOT profileNames(). Derive the real configured profile directory from
        # MO2's current-profile path, not an assumed portable AppData layout.
        active_name = str(organizer.profileName())
        active_path = Path(_canonical(organizer.profilePath()))
        if active_path.is_symlink() or not active_path.is_dir():
            raise GcoOverlayContractError("MO2 current profile directory unavailable or linked")
        active_path = active_path.resolve(strict=True)
        if active_path.name.casefold() != active_name.casefold():
            raise GcoOverlayContractError("MO2 current profile identity/path mismatch")
        profiles_root = active_path.parent
        valid = []
        for candidate in profiles_root.iterdir():
            if candidate.is_symlink():
                raise GcoOverlayContractError("Linked MO2 profile directory refused")
            if (candidate.is_dir() and
                candidate.name.casefold() == str(selected_profile).casefold()):
                if not (candidate / "modlist.txt").is_file():
                    raise GcoOverlayContractError("Selected MO2 profile modlist is missing")
                valid.append(candidate.name)
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
        if not files:
            raise GcoOverlayContractError("Empty temporary profiler layer cannot be launched")
        # Sealing and approval of produced bytes stays with GCO and source owners.
        # Runtime verifies that mappings still point at the approved physical stage.
        # Match MO2's ordinary root mapping, then explicitly restore
        # unchanged physical file winners. Never add a broad physical
        # directory mapping that could shadow enabled profile packages.
        mappings = [(str(root), str(claimed_game), True)]
        if not isinstance(physical_passthrough, (list, tuple)) or len(physical_passthrough) > 250000:
            raise GcoOverlayContractError("Invalid physical dependency manifest")
        overridden = {str(rel).replace("\\", "/").casefold() for _, rel in files}
        seen = set()
        for value in physical_passthrough:
            if not isinstance(value, str) or "\\" in value or ":" in value:
                raise GcoOverlayContractError("Invalid physical dependency path")
            parts = value.split("/")
            lower = tuple(p.casefold() for p in parts)
            if (any(p in ("", ".", "..") for p in parts)
                    or not (lower[:3] == ("bin", "x64", "plugins")
                            or lower[:2] == ("red4ext", "plugins")
                            or lower == ("red4ext", "red4ext.dll"))
                    or value.casefold() in overridden or value.casefold() in seen):
                raise GcoOverlayContractError("Invalid or conflicting physical dependency: " + value)
            seen.add(value.casefold())
            physical = claimed_game.joinpath(*parts)
            if (not physical.is_file() or physical.is_symlink()
                    or physical.resolve(strict=True) != physical
                    or not root.joinpath(*parts[:-1]).is_dir()):
                raise GcoOverlayContractError("Physical dependency or mapping parent missing: " + value)
            mappings.append((str(physical), str(physical), False))
        sealed = [
            (str(p), str(rel), p.stat().st_size, self._digest(p))
            for p, rel in files
        ]
        self._plan = (selected_profile, str(exe), mappings,
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
                           is_directory=is_directory, create_target=False)
            for source, destination, is_directory in dirs
        ]


class GcoMo2ProfilerFileMapper(mobase.IPluginFileMapper):
    """Registered with MO2, but empty outside one authorized launch."""

    def __init__(self, controller):
        super().__init__()
        self._controller = controller

    def init(self, organizer):
        # IPluginFileMapper is exposed through Python's IPlugin bridge. Actual
        # MO2 calls the required pure-virtual IPlugin.init on EVERY returned
        # plugin, including the mapper, before it can become active.
        # The mapper never changes mods, profiles or game files here.
        self._organizer = organizer
        return True

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



class GcoAuthenticatedMailbox:
    """Single-user, per-MO2-process command mailbox.

    Requests are signed with a random 256-bit session secret in the user's
    LocalAppData, scoped to the exact reported instance and bridge epoch.
    This is a narrow local app adapter, not an arbitrary script runner.
    """

    _REQUIRED = (
        "schemaVersion", "bridgeEpoch", "requestId", "operation",
        "profile", "gameRoot", "stagingRoot", "sessionId", "createdUtcMs"
    )

    def __init__(self, organizer, controller):
        self._organizer = organizer
        self._controller = controller
        self.epoch = secrets.token_hex(16)
        self._status = _status_path(organizer.basePath())
        self._key_path = self._status.with_suffix(".key")
        self._request_path = self._status.with_suffix(".request.json")
        self._response_path = self._status.with_suffix(".response.json")
        self._secret = secrets.token_bytes(32)
        self._seen = set()
        self._key_path.parent.mkdir(parents=True, exist_ok=True)
        if self._key_path.is_symlink():
            raise RuntimeError("MO2 GCO authentication key is a symlink")
        fd, temporary = tempfile.mkstemp(
            dir=str(self._key_path.parent), prefix=".gco-key-", suffix=".tmp")
        try:
            with os.fdopen(fd, "wb") as output:
                output.write(self._secret)
                output.flush()
                os.fsync(output.fileno())
            os.chmod(temporary, 0o600)
            os.replace(temporary, self._key_path)
        finally:
            if os.path.exists(temporary):
                os.unlink(temporary)

    @classmethod
    def signature_payload(cls, request):
        fields = []
        for key in cls._REQUIRED:
            value = request[key]
            if key in ("schemaVersion", "createdUtcMs"):
                if type(value) is not int:
                    raise GcoOverlayContractError("Invalid numeric MO2 command field")
            elif type(value) is not str or "\n" in value or "\r" in value or "\0" in value:
                raise GcoOverlayContractError("Invalid MO2 command field: " + key)
            fields.append(str(value))
        return "\n".join(fields).encode("utf-8")

    def _check(self, request):
        if not isinstance(request, dict):
            raise GcoOverlayContractError("MO2 request is not an object")
        if set(request) != set(self._REQUIRED) | {"hmac"}:
            raise GcoOverlayContractError("MO2 request field set is invalid")
        if request.get("schemaVersion") != 1 or request.get("bridgeEpoch") != self.epoch:
            raise GcoOverlayContractError("Wrong or stale MO2 bridge epoch")
        if not isinstance(request.get("requestId"), str) or not self._controller._HEX_ID.fullmatch(
                request["requestId"]):
            raise GcoOverlayContractError("Invalid MO2 request ID")
        if request["requestId"] in self._seen:
            raise GcoOverlayContractError("MO2 request replay")
        timestamp = request.get("createdUtcMs")
        if type(timestamp) is not int or abs(int(time.time() * 1000) - timestamp) > 15000:
            raise GcoOverlayContractError("Expired MO2 command")
        if request.get("operation") not in ("probe", "launch", "frame-package", "frame-start"):
            raise GcoOverlayContractError("Unsupported MO2 command")
        provided = request.get("hmac")
        if not isinstance(provided, str) or len(provided) != 64:
            raise GcoOverlayContractError("Missing MO2 command authorization")
        expected = hmac.new(
            self._secret, self.signature_payload(request), hashlib.sha256).hexdigest()
        if not hmac.compare_digest(expected, provided):
            raise GcoOverlayContractError("MO2 command authentication failed")
        self._seen.add(request["requestId"])
        if len(self._seen) > 256:
            # Epoch and key are renewed on plugin restart. Existing command
            # files are one-shot and cannot use a previous epoch.
            self._seen = {request["requestId"]}

    @staticmethod
    def _own_elevation():
        # Read our own token, not a remote process token. Report unknown
        # separately from non-elevated; never infer elevation from a path.
        if os.name != "nt":
            return "unknown"
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        advapi = ctypes.WinDLL("advapi32", use_last_error=True)
        kernel.GetCurrentProcess.restype = ctypes.c_void_p
        kernel.CloseHandle.argtypes = [ctypes.c_void_p]
        advapi.OpenProcessToken.argtypes = [
            ctypes.c_void_p, ctypes.c_uint32, ctypes.POINTER(ctypes.c_void_p)]
        advapi.GetTokenInformation.argtypes = [
            ctypes.c_void_p, ctypes.c_int, ctypes.c_void_p, ctypes.c_uint32,
            ctypes.POINTER(ctypes.c_uint32)]
        token = ctypes.c_void_p()
        if not advapi.OpenProcessToken(kernel.GetCurrentProcess(), 8, ctypes.byref(token)):
            return "unknown"
        try:
            value = ctypes.c_uint32()
            size = ctypes.c_uint32()
            if not advapi.GetTokenInformation(token, 20, ctypes.byref(value),
                    ctypes.sizeof(value), ctypes.byref(size)):
                return "unknown"
            return "elevated" if value.value else "not-elevated"
        finally:
            kernel.CloseHandle(token)

    @staticmethod
    def _frame_package():
        # The elevated VERIFY helper binds the package, not the request.
        # No caller-controlled executable, arguments, or output path.
        value = os.environ.get("GCO_VERIFIED_PACKAGE_ROOT", "")
        if not value or not os.path.isabs(value):
            raise GcoOverlayContractError("Click VERIFY to authorize frame capture")
        root = Path(value)
        for relative, variable in (
            ("Gs-Cyberpunk2077_Optimizer.exe", "GCO_VERIFIED_LAUNCHER_SHA256"),
            ("app/G-Total-Optimizer.App.exe", "GCO_VERIFIED_APP_SHA256"),
        ):
            target = root / relative
            expected = os.environ.get(variable, "").lower()
            if (len(expected) != 64 or not target.is_file() or target.is_symlink()
                    or hashlib.sha256(target.read_bytes()).hexdigest() != expected):
                raise GcoOverlayContractError("GCO package changed; click VERIFY")
        return root

    def _start_frame_controller(self, request):
        if self._own_elevation() != "elevated":
            raise GcoOverlayContractError("Click VERIFY to authorize frame capture")
        root = self._frame_package()
        session = request["sessionId"]
        if not self._controller._HEX_ID.fullmatch(session):
            raise GcoOverlayContractError("Invalid frame capture identity")
        stage = _bridge_root() / "staging" / session
        if _canonical(request["stagingRoot"]) != _canonical(stage):
            raise GcoOverlayContractError("Frame capture staging identity mismatch")
        journal_path = _bridge_root() / "journals" / (session + ".json")
        if journal_path.is_symlink() or not journal_path.is_file():
            raise GcoOverlayContractError("Frame capture deployment journal missing")
        deployment = json.loads(journal_path.read_text(encoding="utf-8"))
        if (deployment.get("phase") != "test-launch-pending"
                or deployment.get("sessionId") != session
                or deployment.get("profile") != request["profile"]
                or _canonical(deployment.get("gameRoot", "")) != _canonical(request["gameRoot"])
                or _canonical(deployment.get("stageRoot", "")) != _canonical(stage)
                or _canonical(self._organizer.managedGame().gameDirectory()) != _canonical(request["gameRoot"])
                or request["profile"] not in list(self._organizer.profileNames())):
            raise GcoOverlayContractError("Frame capture deployment identity mismatch")
        results = root / "RESULTS"
        active = results / "GCO-active-profile-session.json"
        if active.is_symlink() or not active.is_file():
            raise GcoOverlayContractError("Frame capture journal missing")
        attempt = json.loads(active.read_text(encoding="utf-8-sig"))
        instance = os.environ.get("GCO_VERIFIED_MO2_INSTANCE", "")
        if (not instance or attempt.get("stage") != "mo2-capture-attempt"
                or attempt.get("sessionId") != session
                or attempt.get("mo2Profile") != request["profile"]
                or _canonical(attempt.get("mo2Instance", "")) != _canonical(instance)
                or _canonical(attempt.get("gameRoot", "")) != _canonical(request["gameRoot"])):
            raise GcoOverlayContractError("Frame capture package identity mismatch")
        # Resolve the existing dated capture folder by its immutable index,
        # never accept a destination supplied by the mailbox.
        matches = []
        for folder in results.glob("GCO-Capture-*"):
            index_path = folder / "collection.json"
            if folder.is_symlink() or index_path.is_symlink() or not index_path.is_file():
                continue
            try:
                index = json.loads(index_path.read_text(encoding="utf-8-sig"))
            except (OSError, ValueError):
                continue
            if index.get("SessionId") == session:
                if (_canonical(index.get("GameRoot", "")) != _canonical(request["gameRoot"])
                        or index.get("CollectionAttempted") or index.get("Restored")
                        or index.get("CollectInvoked") or index.get("Cet") is not None
                        or index.get("Redscript") is not None or index.get("FrameTimeCsv") is not None):
                    raise GcoOverlayContractError("Frame capture index is not a fresh attempt")
                matches.append(folder)
        if len(matches) != 1:
            raise GcoOverlayContractError("Frame capture folder identity is ambiguous")
        frame = matches[0] / "PresentMon"
        if frame.is_symlink() or (frame.exists() and not frame.is_dir()):
            raise GcoOverlayContractError("Frame capture destination is redirected")
        csv = frame / "PresentMon.csv"
        result = frame / "PresentMon-controller.json"
        if csv.exists() or result.exists() or csv.is_symlink() or result.is_symlink():
            raise GcoOverlayContractError("Frame capture destination already used")
        # Before spawning, persist uncertainty in the SAME result file the
        # original controller atomically replaces. A crash cannot be
        # mistaken for a settled controller or authorize scratch disposal.
        _write_json_atomic(result, {"ok": False, "pending": True, "sessionId": session})
        launcher = root / "Gs-Cyberpunk2077_Optimizer.exe"
        try:
            ended = subprocess.run(
                [str(launcher), "--presentmon-control", "restart", str(csv), session],
                cwd=str(root), shell=False, check=False)
        except OSError as exc:
            _write_json_atomic(result, {"ok": False, "detail": str(exc)})
            return "PresentMon controller did not start"
        # Native launcher exit 4 means the managed child may still be alive.
        # Preserve the pending result until the existing controller settles.
        if ended.returncode == 4:
            raise GcoOverlayContractError("PresentMon controller still pending")
        proof = json.loads(result.read_text(encoding="utf-8-sig"))
        if proof.get("pending"):
            _write_json_atomic(result, {"ok": False,
                "detail": "PresentMon controller exited without startup proof"})
        return "PresentMon controller exited: " + str(ended.returncode)

    @staticmethod
    def _process_id(handle):
        if os.name != "nt":
            raise GcoOverlayContractError("MO2 launch PID requires Windows")
        library = ctypes.windll.kernel32
        library.GetProcessId.argtypes = [ctypes.c_void_p]
        library.GetProcessId.restype = ctypes.c_uint32
        library.CloseHandle.argtypes = [ctypes.c_void_p]
        library.CloseHandle.restype = ctypes.c_int
        try:
            pid = library.GetProcessId(ctypes.c_void_p(handle))
            if not pid:
                raise GcoOverlayContractError("MO2 returned a handle without a process ID")
            return int(pid)
        finally:
            library.CloseHandle(ctypes.c_void_p(handle))

    def process_one(self, process_id_for_testing=None):
        if not self._request_path.is_file():
            return False
        if self._request_path.is_symlink():
            raise GcoOverlayContractError("MO2 command cannot be a symlink")
        in_progress = self._status.with_suffix(".processing.json")
        if in_progress.exists():
            raise GcoOverlayContractError("Unrecovered MO2 command still in progress")
        os.replace(self._request_path, in_progress)
        request = None
        try:
            if in_progress.stat().st_size > 16 * 1024:
                raise GcoOverlayContractError("MO2 command is oversized")
            request = json.loads(in_progress.read_text(encoding="utf-8"))
            self._check(request)
            operation = request["operation"]
            if operation == "probe":
                pid, detail = 0, "MO2 token=" + self._own_elevation() + "; pid=" + str(os.getpid())
            elif operation == "frame-package":
                pid, detail = 0, "GCO package: " + _canonical(self._frame_package())
            elif operation == "frame-start":
                pid, detail = 0, self._start_frame_controller(request)
            else:
                if not self._controller._HEX_ID.fullmatch(request["sessionId"]):
                    raise GcoOverlayContractError("Invalid GCO profiler session ID")
                journal_path = _bridge_root() / "journals" / (request["sessionId"] + ".json")
                if journal_path.is_symlink() or not journal_path.is_file():
                    raise GcoOverlayContractError("Profiler mapping journal missing")
                journal = json.loads(journal_path.read_text(encoding="utf-8"))
                if (journal.get("phase") != "test-launch-pending"
                        or journal.get("sessionId") != request["sessionId"]
                        or journal.get("profile") != request["profile"]
                        or _canonical(journal.get("gameRoot", "")) != _canonical(request["gameRoot"])
                        or _canonical(journal.get("stageRoot", "")) != _canonical(request["stagingRoot"])):
                    raise GcoOverlayContractError("Profiler mapping journal identity mismatch")
                self._controller.prepare(
                    self._organizer, request["profile"], request["gameRoot"],
                    request["stagingRoot"], request["sessionId"], journal.get("physicalPassthrough"))
                try:
                    handle = self._controller.launch(self._organizer, request["profile"])
                    pid = (process_id_for_testing or self._process_id)(handle)
                finally:
                    self._controller.cancel()
                detail = "Started selected-profile application under MO2"
            result = {"schemaVersion": 1, "bridgeEpoch": self.epoch,
                      "requestId": request["requestId"], "ok": True,
                      "processId": int(pid), "detail": detail}
        except (OSError, RuntimeError, ValueError, TypeError, AttributeError,
                json.JSONDecodeError) as exc:
            result = {"schemaVersion": 1, "bridgeEpoch": self.epoch,
                      "requestId": request.get("requestId", "") if isinstance(request, dict) else "",
                      "ok": False, "processId": 0,
                      "detail": str(exc)[:300]}
            self._controller.cancel()
        finally:
            in_progress.unlink(missing_ok=True)
        reply = "\n".join((
            str(result["schemaVersion"]), result["bridgeEpoch"],
            result["requestId"], "1" if result["ok"] else "0",
            str(result["processId"]), result["detail"]
        )).encode("utf-8")
        result["hmac"] = hmac.new(self._secret, reply, hashlib.sha256).hexdigest()
        _write_json_atomic(self._response_path, result)
        return True



class GcoMo2Bridge(mobase.IPluginTool):
    def __init__(self):
        super().__init__()
        self._organizer = None
        self._timer = None
        self._status = None

    def init(self, organizer):
        self._organizer = organizer
        self._mailbox = GcoAuthenticatedMailbox(organizer, self._launch_controller)
        self._publish()
        organizer.onProfileChanged(lambda _old, _new: self._publish())
        self._timer = QTimer()
        self._timer.timeout.connect(self._publish)
        self._timer.start(5000)
        self._commands_timer = QTimer()
        self._commands_timer.timeout.connect(self._poll_commands)
        self._commands_timer.start(1000)
        return True

    def _poll_commands(self):
        try:
            self._mailbox.process_one()
        except (OSError, RuntimeError, ValueError) as error:
            # Reject malformed or stale commands without blocking MO2.
            self._status = "MO2 command refused: " + str(error)[:200]

    def _publish(self):
        try:
            snapshot = _snapshot(self._organizer)
            snapshot["bridgeEpoch"] = self._mailbox.epoch
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
        return "Profile discovery and authorized one-shot GCO virtual profiler launch."

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
            self._parentWidget(),  # MO2 Python IPluginTool binds this Qt parent helper.
            "GCO MO2 connection",
            str(self._status or "Status not available. No game/mod files were changed."),
        )


def createPlugins():
    controller = GcoLaunchMappingController()
    tool = GcoMo2Bridge()
    tool._launch_controller = controller
    return [tool, GcoMo2ProfilerFileMapper(controller)]
