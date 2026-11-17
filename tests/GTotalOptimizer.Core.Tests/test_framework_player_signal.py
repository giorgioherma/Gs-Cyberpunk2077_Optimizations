"""Owner G-REDruntime player signal lifecycle: source contract and mutation negatives.

Run against the checkout of the ACTUAL framework owner main, not a copy in GCO:
python test_framework_player_signal.py --framework .gco-ci-framework
The official full-source SCC compiler remains a separate mandatory gate.
"""
import argparse
from pathlib import Path
import re
import unittest


def method_body(source: str, method: str) -> str:
    pattern = r"\bfunc\s+" + re.escape(method) + r"\s*\("
    match = re.search(pattern, source)
    if not match:
        raise AssertionError(f"MISSING_LIFECYCLE_METHOD:{method}")
    opening = source.find("{", match.end())
    if opening < 0:
        raise AssertionError(f"MISSING_LIFECYCLE_BODY:{method}")
    depth = 0
    for i in range(opening, len(source)):
        if source[i] == "{":
            depth += 1
        elif source[i] == "}":
            depth -= 1
            if depth == 0:
                return source[opening + 1:i]
    raise AssertionError(f"UNBALANCED_LIFECYCLE_BODY:{method}")


def must_contain(body: str, fragment: str, label: str) -> None:
    if fragment not in body:
        raise AssertionError(label)


def verify(runtime: str, state: str) -> None:
    attach = method_body(runtime, "OnPlayerAttach")
    detach = method_body(runtime, "OnPlayerDetach")
    shutdown = method_body(state, "Shutdown")
    set_player = method_body(state, "SetPlayer")
    invalidate = method_body(state, "InvalidatePlayer")
    getter = method_body(state, "GetPlayer")
    authoritative = method_body(state, "HasAuthoritativePlayer")
    generation = method_body(state, "GetPlayerSignalGeneration")

    # Native ScriptableSystem events, not a synthetic timer or per-frame hook.
    must_contain(runtime, "ref<PlayerDetachRequest>", "MISSING_NATIVE_DETACH_REQUEST")
    must_contain(detach, "IsDefined(request)", "NULL_DETACH_REQUEST_UNGUARDED")
    must_contain(detach, "this.m_state.GetAttachedPlayer()", "MISSING_ATTACHED_PLAYER_PROOF")
    for fragment in ("leaving.GetEntityID()", "attached.GetEntityID()", "NotEquals("):
        must_contain(detach, fragment, "STALE_PLAYER_DETACH_UNGUARDED")
    must_contain(attach, "this.m_state.SetPlayer(player)", "ATTACH_STATE_NOT_PUBLISHED")
    must_contain(attach, "this.m_input.OnPlayerAvailable(player)", "ATTACH_INPUT_NOT_BOUND")
    must_contain(attach, "this.m_dirty.Mark(n\"PLAYER\")", "ATTACH_VERSION_NOT_INVALIDATED")
    must_contain(attach, "RuntimeEvent.Create(n\"PLAYER\", n\"ATTACHED\")",
                 "ATTACH_EVENT_NOT_PUBLISHED")

    required = (
        ("this.m_state.InvalidatePlayer()", "DETACH_STATE_NOT_INVALIDATED"),
        ("this.m_context.Invalidate()", "DETACH_CONTEXT_NOT_INVALIDATED"),
        ("this.m_input.OnPlayerAvailable(null)", "DETACH_INPUT_LISTENERS_NOT_RELEASED"),
        ("this.m_dirty.Mark(n\"PLAYER\")", "DETACH_VERSION_NOT_INVALIDATED"),
        ("this.m_events.HasSubscribersFor(n\"PLAYER\")", "DETACH_EVENT_ALLOCATION_UNGUARDED"),
        ("RuntimeEvent.Create(n\"PLAYER\", n\"DETACHED\")", "DETACH_EVENT_NOT_PUBLISHED"),
    )
    for fragment, reason in required:
        must_contain(detach, fragment, reason)
    # Every observer sees UNKNOWN/current generation, not a stale player
    # or still-registered engine input listener.
    if not (detach.index("GetAttachedPlayer()") <
            detach.index("leaving.GetEntityID()") <
            detach.index("this.m_state.InvalidatePlayer()") <
            detach.index("this.m_input.OnPlayerAvailable(null)") <
            detach.index("this.m_dirty.Mark(n\"PLAYER\")") <
            detach.index("RuntimeEvent.Create(n\"PLAYER\", n\"DETACHED\")")):
        raise AssertionError("DETACH_ORDER_UNSAFE")

    for body, fields, reason in (
        (set_player, ("this.m_playerSignalKnown = IsDefined(player)",
                      "this.m_playerDiscoveryBlocked = false",
                      "this.m_playerSignalGeneration += 1u"), "ATTACH_EPOCH_MISSING"),
        (invalidate, ("this.m_player = null",
                      "this.m_playerSignalKnown = false",
                      "this.m_playerDiscoveryBlocked = true",
                      "this.m_playerSignalGeneration += 1u"), "DETACH_EPOCH_MISSING"),
        (shutdown, ("this.m_playerSignalKnown = false",
                    "this.m_playerDiscoveryBlocked = true"), "SHUTDOWN_STALE_PLAYER_STATE"),
        (authoritative, ("this.m_playerSignalKnown", "IsDefined(this.m_player)"),
         "AUTHORITATIVE_PLAYER_UNPROVEN"),
        (generation, ("this.m_playerSignalGeneration",), "SIGNAL_GENERATION_UNAVAILABLE"),
    ):
        for field in fields:
            must_contain(body, field, reason)
    if not (getter.index("this.m_playerDiscoveryBlocked") <
            getter.index("this.GetPlayerSystem()")):
        raise AssertionError("PLAYER_GETTER_CAN_REACQUIRE_AFTER_DETACH")
    must_contain(getter, "this.m_playerSignalKnown && !IsDefined(this.m_player)",
                 "EXPIRED_WEAK_PLAYER_CAN_REACQUIRE")
    must_contain(getter, "this.GetPlayerSystem()",
                 "PREFIRST_ATTACH_ORIGINAL_FALLBACK_REMOVED")
    for body in (attach, detach, set_player, invalidate):
        for forbidden in ("DelayCallback(", "RegisterInputListener(",
                          "GetWorldPosition(", "GetMountedVehicle(", "IsInCombat("):
            if forbidden in body:
                raise AssertionError("PLAYER_SIGNAL_ADDED_POLL_OR_NATIVE_OBSERVER")


class PlayerSignalContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(ARGS.framework).resolve()
        cls.runtime = (root / "r6/scripts/G-RedRuntime/Core/Runtime.reds").read_text(
            encoding="utf-8-sig")
        cls.state = (root / "r6/scripts/G-RedRuntime/State/StateCache.reds").read_text(
            encoding="utf-8-sig")

    def test_real_owner_lifecycle_and_fallback(self):
        verify(self.runtime, self.state)

    def test_reject_detach_without_input_cleanup(self):
        broken = self.runtime.replace("this.m_input.OnPlayerAvailable(null)",
                                      "this.m_input.HasSubscribers()")
        with self.assertRaisesRegex(AssertionError,
                                    "DETACH_INPUT_LISTENERS_NOT_RELEASED"):
            verify(broken, self.state)

    def test_reject_stale_player_detach_without_identity_gate(self):
        broken = self.runtime.replace("NotEquals(leaving.GetEntityID(), attached.GetEntityID())",
                                      "false")
        with self.assertRaisesRegex(AssertionError, "STALE_PLAYER_DETACH_UNGUARDED"):
            verify(broken, self.state)

    def test_reject_reacquiring_player_between_detach_and_attach(self):
        broken = self.state.replace("this.m_playerDiscoveryBlocked ||", "false ||")
        with self.assertRaisesRegex(AssertionError,
                                    "PLAYER_GETTER_CAN_REACQUIRE_AFTER_DETACH"):
            verify(self.runtime, broken)

    def test_reject_missing_epoch_invalidation(self):
        old = method_body(self.state, "InvalidatePlayer")
        broken = self.state.replace(old, old.replace(
            "this.m_playerSignalGeneration += 1u;", ""))
        with self.assertRaisesRegex(AssertionError, "DETACH_EPOCH_MISSING"):
            verify(self.runtime, broken)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--framework", required=True)
    ARGS = parser.parse_args()
    unittest.main(argv=["framework_player_signal"], verbosity=2)
