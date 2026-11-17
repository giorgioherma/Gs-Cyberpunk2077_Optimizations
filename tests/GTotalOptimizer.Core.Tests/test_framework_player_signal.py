"""G-REDruntime owner player signal: source/negative contracts before real SCC.

Run against the exact owner Git checkout used for GCO's full-stack SCC.
The game runtime and measured performance still require owner gameplay tests.
"""
import argparse
from pathlib import Path
import re
import unittest


def method(source: str, name: str) -> str:
    match = re.search(r"\bfunc\s+" + re.escape(name) + r"\s*\(", source)
    if not match:
        raise AssertionError(f"MISSING_OWNER_METHOD:{name}")
    opening = source.find("{", match.end())
    if opening < 0:
        raise AssertionError(f"MISSING_OWNER_METHOD_BODY:{name}")
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:i]
    raise AssertionError(f"UNBALANCED_OWNER_METHOD_BODY:{name}")


def require(text: str, fragment: str, diagnostic: str):
    if fragment not in text:
        raise AssertionError(diagnostic)


def validate(runtime: str, state: str, hub: str):
    attach = method(runtime, "OnPlayerAttach")
    detach = method(runtime, "OnPlayerDetach")
    shutdown = method(state, "Shutdown")
    set_player = method(state, "SetPlayer")
    invalidate = method(state, "InvalidatePlayer")
    getter = method(state, "GetPlayer")
    attached_getter = method(state, "GetAttachedPlayer")
    known = method(state, "HasAuthoritativePlayer")
    generation = method(state, "GetPlayerSignalGeneration")
    input_attach = method(hub, "OnPlayerAvailable")
    input_detach = method(hub, "OnPlayerUnavailable")
    input_refresh = method(hub, "RefreshRegistration")
    input_device = method(hub, "PublishDeviceState")

    require(runtime, "ref<PlayerDetachRequest>", "NATIVE_DETACH_REQUEST_MISSING")
    for fragment, diagnostic in (
        ("this.m_state.SetPlayer(player)", "ATTACH_STATE_NOT_PUBLISHED"),
        ("this.m_input.OnPlayerAvailable(player)", "ATTACH_INPUT_NOT_BOUND"),
        ('this.m_dirty.Mark(n"PLAYER")', "ATTACH_VERSION_NOT_INVALIDATED"),
        ('RuntimeEvent.Create(n"PLAYER", n"ATTACHED")', "ATTACH_EVENT_NOT_PUBLISHED"),
    ):
        require(attach, fragment, diagnostic)

    for fragment, diagnostic in (
        ("IsDefined(request)", "NULL_DETACH_REQUEST_UNGUARDED"),
        ("this.m_state.GetAttachedPlayer()", "ATTACHED_PLAYER_IDENTITY_NOT_PROVEN"),
        ("leaving.GetEntityID()", "STALE_PLAYER_DETACH_UNGUARDED"),
        ("attached.GetEntityID()", "STALE_PLAYER_DETACH_UNGUARDED"),
        ("NotEquals(", "STALE_PLAYER_DETACH_UNGUARDED"),
        ("this.m_state.InvalidatePlayer()", "DETACH_STATE_NOT_INVALIDATED"),
        ("this.m_context.Invalidate()", "DETACH_CONTEXT_NOT_INVALIDATED"),
        ("this.m_input.OnPlayerUnavailable()", "DETACH_INPUT_NOT_RELEASED"),
        ('this.m_dirty.Mark(n"PLAYER")', "DETACH_VERSION_NOT_INVALIDATED"),
        ('this.m_events.HasSubscribersFor(n"PLAYER")', "DETACH_EVENT_ALLOCATION_UNGUARDED"),
        ('RuntimeEvent.Create(n"PLAYER", n"DETACHED")', "DETACH_EVENT_NOT_PUBLISHED"),
    ):
        require(detach, fragment, diagnostic)
    order = ("GetAttachedPlayer()", "leaving.GetEntityID()",
             "this.m_state.InvalidatePlayer()",
             "this.m_input.OnPlayerUnavailable()",
             'this.m_dirty.Mark(n"PLAYER")',
             'RuntimeEvent.Create(n"PLAYER", n"DETACHED")')
    if [detach.index(x) for x in order] != sorted(detach.index(x) for x in order):
        raise AssertionError("DETACH_ORDER_UNSAFE")

    for source, fragments, diagnostic in (
        (set_player, ("this.m_playerSignalKnown = IsDefined(player)",
                      "this.m_playerDiscoveryBlocked = false",
                      "this.m_playerSignalGeneration += 1u"), "ATTACH_EPOCH_MISSING"),
        (invalidate, ("this.m_player = null", "this.m_playerSignalKnown = false",
                      "this.m_playerDiscoveryBlocked = true",
                      "this.m_playerSignalGeneration += 1u"), "DETACH_EPOCH_MISSING"),
        (shutdown, ("this.m_playerSignalKnown = false",
                    "this.m_playerDiscoveryBlocked = true"),
         "SHUTDOWN_STALE_PLAYER_SIGNAL"),
        (known, ("this.m_playerSignalKnown", "IsDefined(this.m_player)"),
         "AUTHORITATIVE_PLAYER_UNKNOWN"),
        (generation, ("this.m_playerSignalGeneration",),
         "PLAYER_SIGNAL_GENERATION_UNAVAILABLE"),
        (attached_getter, ("this.HasAuthoritativePlayer()", "return this.m_player;"),
         "ATTACHED_PLAYER_GETTER_NOT_PROVEN"),
    ):
        for fragment in fragments:
            require(source, fragment, diagnostic)
    if "GetPlayerSystem(" in attached_getter:
        raise AssertionError("ATTACHED_PLAYER_GETTER_MUST_NOT_DISCOVER")

    # Existing unoptimized authors require the original live player getter
    # on UNKNOWN. The returned value may not repopulate the shared cache.
    require(getter, "this.m_playerDiscoveryBlocked",
            "SHARED_PLAYER_REDISCOVERY_NOT_BLOCKED")
    require(getter, "this.m_playerSignalKnown && !IsDefined(this.m_player)",
            "EXPIRED_PLAYER_WEAK_REF_NOT_GATED")
    require(getter, "originalPlayerSystem.GetLocalPlayerMainGameObject() as PlayerPuppet",
            "UNKNOWN_ORIGINAL_PLAYER_FALLBACK_MISSING")
    require(getter, "this.GetPlayerSystem()",
            "PRE_FIRST_ATTACH_ORIGINAL_FALLBACK_MISSING")
    unknown_prefix = getter[:getter.index("if IsDefined(this.m_player)")]
    if "this.m_player =" in unknown_prefix:
        raise AssertionError("UNKNOWN_PLAYER_CACHED_AS_AUTHORITATIVE")
    sites = ("this.m_playerDiscoveryBlocked",
             "originalPlayerSystem.GetLocalPlayerMainGameObject()",
             "this.GetPlayerSystem()")
    if [getter.index(x) for x in sites] != sorted(getter.index(x) for x in sites):
        raise AssertionError("UNKNOWN_FALLBACK_ORDER_UNPROVEN")

    # Input lifecycle is a separate boundary: it must NOT fall back to a
    # live PlayerSystem lookup and silently re-register after detach.
    for fragment in ("this.m_playerDetached = true",
                     "this.UnregisterAllBridges()", "this.StopDevicePolling()"):
        require(input_detach, fragment, "INPUT_DETACH_NOT_QUIESCED")
    require(input_attach, "this.m_playerDetached = false",
            "INPUT_NEXT_ATTACH_NOT_REARMED")
    require(input_refresh, "if this.m_playerDetached",
            "INPUT_REGISTRATIONS_CAN_REAPPEAR_ON_DETACH")
    require(input_device, "this.m_playerDetached",
            "INPUT_DEVICE_JOB_CAN_FIRE_AFTER_DETACH")

    # No added polling, cache of changing values or new native observers.
    for body in (attach, detach, set_player, invalidate):
        for forbidden in ("DelayCallback(", "RegisterInputListener(",
                          "GetWorldPosition(", "GetMountedVehicle(", "IsInCombat("):
            if forbidden in body:
                raise AssertionError("PLAYER_SIGNAL_ADDED_POLL_OR_NATIVE_OBSERVER")


class FrameworkPlayerSignalContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(ARGS.framework).resolve() / "r6/scripts/G-RedRuntime"
        cls.runtime = (root / "Core/Runtime.reds").read_text(encoding="utf-8-sig")
        cls.state = (root / "State/StateCache.reds").read_text(encoding="utf-8-sig")
        cls.hub = (root / "Input/InputHub.reds").read_text(encoding="utf-8-sig")

    def test_real_owner_lifecycle(self):
        validate(self.runtime, self.state, self.hub)

    def test_reject_missing_detach_input_cleanup(self):
        bad = self.runtime.replace("this.m_input.OnPlayerUnavailable()",
                                   "this.m_input.HasSubscribers()")
        with self.assertRaisesRegex(AssertionError, "DETACH_INPUT_NOT_RELEASED"):
            validate(bad, self.state, self.hub)

    def test_reject_stale_player_detach_race(self):
        bad = self.runtime.replace(
            "NotEquals(leaving.GetEntityID(), attached.GetEntityID())", "false")
        with self.assertRaisesRegex(AssertionError, "STALE_PLAYER_DETACH_UNGUARDED"):
            validate(bad, self.state, self.hub)

    def test_reject_missing_generation_invalidation(self):
        old = method(self.state, "InvalidatePlayer")
        bad = self.state.replace(old, old.replace(
            "this.m_playerSignalGeneration += 1u;", ""))
        with self.assertRaisesRegex(AssertionError, "DETACH_EPOCH_MISSING"):
            validate(self.runtime, bad, self.hub)

    def test_reject_unknown_player_rediscovery_into_shared_cache(self):
        bad = self.state.replace("this.m_playerDiscoveryBlocked ||", "false ||")
        with self.assertRaisesRegex(AssertionError,
                                    "SHARED_PLAYER_REDISCOVERY_NOT_BLOCKED"):
            validate(self.runtime, bad, self.hub)

    def test_reject_missing_original_live_fallback(self):
        bad = self.state.replace(
            "return originalPlayerSystem.GetLocalPlayerMainGameObject() as PlayerPuppet;",
            "return null;")
        with self.assertRaisesRegex(AssertionError,
                                    "UNKNOWN_ORIGINAL_PLAYER_FALLBACK_MISSING"):
            validate(self.runtime, bad, self.hub)

    def test_reject_input_registrations_during_detach(self):
        bad = self.hub.replace("if this.m_playerDetached {", "if false {")
        with self.assertRaisesRegex(AssertionError,
                                    "INPUT_REGISTRATIONS_CAN_REAPPEAR_ON_DETACH"):
            validate(self.runtime, self.state, bad)


if __name__ == "__main__":
    p = argparse.ArgumentParser()
    p.add_argument("--framework", required=True)
    ARGS = p.parse_args()
    unittest.main(argv=["owner_shared_player_signal"], verbosity=2)
