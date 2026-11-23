"""CI-only authenticated launch request for the *real* MO2 Python companion.

The production GCO LaunchAsync remains gated. This isolated test creates one
signed command against a disposable MO2 instance with an inert native test exe.
It does not add a second product launch path or modify a user profile.
"""
import argparse
import hashlib
import hmac
import json
import os
from pathlib import Path
import secrets
import tempfile
import time


_FIELDS = (
    "schemaVersion", "bridgeEpoch", "requestId", "operation",
    "profile", "gameRoot", "stagingRoot", "sessionId", "createdUtcMs"
)


def sign(secret: bytes, data: str) -> str:
    return hmac.new(secret, data.encode("utf-8"), hashlib.sha256).hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("status")
    parser.add_argument("profile")
    parser.add_argument("game_root")
    parser.add_argument("staging_root")
    parser.add_argument("session_id")
    args = parser.parse_args()
    status = Path(args.status).resolve(strict=True)
    current = json.loads(status.read_text(encoding="utf-8"))
    if current.get("installationEnabled") is not False:
        raise RuntimeError("MO2 feature gate unexpectedly enabled")
    if current.get("currentProfile") == args.profile:
        raise RuntimeError("Fixture must launch a NONCURRENT profile")
    epoch = current["bridgeEpoch"]
    key = status.with_suffix(".key")
    if key.is_symlink():
        raise RuntimeError("Symlinked MO2 bridge key")
    secret = key.read_bytes()
    if len(secret) != 32 or len(epoch) != 32:
        raise RuntimeError("Invalid live companion authentication material")
    request_path = status.with_suffix(".request.json")
    response_path = status.with_suffix(".response.json")
    if request_path.exists():
        raise RuntimeError("Another MO2 command is still pending")
    request = dict(
        schemaVersion=1,
        bridgeEpoch=epoch,
        requestId=secrets.token_hex(16),
        operation="launch",
        profile=args.profile,
        gameRoot=str(Path(args.game_root).resolve(strict=True)),
        stagingRoot=str(Path(args.staging_root).resolve(strict=True)),
        sessionId=args.session_id,
        createdUtcMs=int(time.time() * 1000)
    )
    payload = "\n".join(str(request[field]) for field in _FIELDS)
    request["hmac"] = sign(secret, payload)
    fd, temp = tempfile.mkstemp(dir=status.parent, prefix=".mo2-test-launch-", suffix=".tmp")
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(request, stream, sort_keys=True, separators=(",", ":"))
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temp, request_path)
    finally:
        if os.path.exists(temp):
            os.unlink(temp)
    deadline = time.monotonic() + 20.0
    while time.monotonic() < deadline:
        if response_path.exists():
            result = json.loads(response_path.read_text(encoding="utf-8"))
            if result.get("requestId") == request["requestId"]:
                content = "\n".join((
                    str(result["schemaVersion"]), result["bridgeEpoch"],
                    result["requestId"], "1" if result["ok"] else "0",
                    str(result["processId"]), result["detail"]
                ))
                if result.get("bridgeEpoch") != epoch or not hmac.compare_digest(
                        result.get("hmac", ""), sign(secret, content)):
                    raise RuntimeError("MO2 actual launch response signature invalid")
                if not result["ok"] or int(result["processId"]) <= 0:
                    raise RuntimeError("MO2 actual selected-profile launch refused: " + result["detail"])
                print(json.dumps({"processId": int(result["processId"]),
                                  "profile": request["profile"], "status": "launched"}))
                return
        time.sleep(0.2)
    raise TimeoutError("No signed response from live MO2 selected-profile launch")


if __name__ == "__main__":
    main()
