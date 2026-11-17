"""Owner-framework QuestFactSignals: opt-in lifecycle and exact event safety.

This is SOURCE contract/mutation testing. Official REDscript SCC and game
behavior remain separate gates. Nothing in this file authorizes mod rewrites.
"""
import argparse
from pathlib import Path
import re
import unittest


def method(source, name):
    match = re.search(r"\bfunc\s+" + re.escape(name) + r"\s*\(", source)
    if not match:
        raise AssertionError("MISSING_OWNER_METHOD_" + name)
    brace = source.find("{", match.end())
    depth = 0
    for index in range(brace, len(source)):
        if source[index] == "{":
            depth += 1
        if source[index] == "}":
            depth -= 1
            if depth == 0:
                return source[brace:index + 1]
    raise AssertionError("UNBALANCED_OWNER_METHOD_" + name)


def need(text, fragment, reason):
    if fragment not in text:
        raise AssertionError(reason)


def verify(runtime, signals):
    attach = method(runtime, "OnAttach")
    detach = method(runtime, "OnDetach")
    player_attach = method(runtime, "OnPlayerAttach")
    player_detach = method(runtime, "OnPlayerDetach")
    initialize = method(signals, "Initialize")
    acquire = method(signals, "AcquireFact")
    interest = method(signals, "AddInterest")
    release = method(signals, "ReleaseFact")
    callback = method(signals, "OnFactChanged")
    bridge_callback = signals[signals.index("public class QuestFactBridge"):
                              signals.index("public class QuestFactSignals")]
    bridge_callback = method(bridge_callback, "OnFactChanged")
    shutdown = method(signals, "Shutdown")
    invalidate = method(signals, "InvalidateSession")
    known = method(signals, "HasObservedValue")
    version = method(signals, "Version")

    need(attach, "this.m_factSignals = new QuestFactSignals()",
         "FACT_SIGNAL_MISSING_RUNTIME_OWNER")
    need(attach, "this.m_factSignals.Initialize(", "FACT_SIGNAL_NOT_INITIALIZED")
    need(runtime, "GetQuestFactSignals()", "FACT_SIGNAL_NOT_EXPOSED")
    need(detach, "this.m_factSignals.Shutdown()", "FACT_SIGNAL_SHUTDOWN_MISSING")
    for body in (player_attach, player_detach):
        need(body, "this.m_factSignals.InvalidateSession()",
             "FACT_SIGNAL_SESSION_INVALIDATION_MISSING")
    for body in (attach, initialize):
        if ("RegisterListener(" in body or "GetFact(" in body or
            ".AcquireFact(" in body):
            raise AssertionError("DORMANT_FACT_SERVICE_REGISTERS_WITHOUT_CONSUMERS")

    for fragment, reason in (
        ("if !this.m_ready", "FACT_SIGNAL_UNREADY_FALLBACK_MISSING"),
        ("if !IsDefined(qs)", "FACT_SIGNAL_MISSING_QUESTS_FALLBACK"),
        ("watch.interestCount > 0 && watch.engineListenerID != 0u",
         "FACT_SIGNAL_REGISTRATION_NOT_DEDUPLICATED"),
        ("watch.interestCount += 1", "FACT_SIGNAL_REFCOUNT_NOT_INCREMENTED"),
        ("qs.RegisterListener(fact, bridge, n\"OnFactChanged\")",
         "FACT_SIGNAL_ENGINE_SOURCE_MISSING"),
        ("if registration == 0u", "FACT_SIGNAL_FAILED_REGISTRATION_ADMITTED"),
        ("return this.AddInterest(fact)", "FACT_SIGNAL_INTEREST_TOKEN_MISSING"),
    ):
        need(acquire, fragment, reason)
    need(interest, "this.m_nextInterestID += 1", "FACT_SIGNAL_TOKEN_NOT_UNIQUE")
    need(interest, "entry.active = true", "FACT_SIGNAL_INTEREST_NOT_ACTIVE")
    for fragment, reason in (
        ("entry.id == interestID && entry.active", "FACT_SIGNAL_TOKEN_NOT_OWNED"),
        ("entry.active = false", "FACT_SIGNAL_DOUBLE_RELEASE_NOT_BLOCKED"),
        ("watch.interestCount -= 1", "FACT_SIGNAL_REFCOUNT_NOT_DECREMENTED"),
        ("if watch.interestCount == 0", "FACT_SIGNAL_EARLY_UNREGISTER"),
        ("qs.UnregisterListener(watch.fact, watch.engineListenerID)",
         "FACT_SIGNAL_ENGINE_CLEANUP_MISSING"),
        ("watch.hasObservedValue = false", "FACT_SIGNAL_STALE_VALUE_ON_RELEASE")
    ):
        need(release, fragment, reason)
    need(shutdown, "qs.UnregisterListener(watch.fact, watch.engineListenerID)",
         "FACT_SIGNAL_SHUTDOWN_LEAK")
    need(shutdown, "watch.bridge = null", "FACT_SIGNAL_SHUTDOWN_BRIDGE_LEAK")
    need(bridge_callback, "this.m_owner.OnFactChanged(this.m_fact, value)",
         "FACT_SIGNAL_VALUE_FORWARD_MISSING")
    need(bridge_callback, "return false", "FACT_SIGNAL_OBSERVER_RETURN_NOT_SAFE")
    need(callback, "watch.version += 1u", "FACT_SIGNAL_CALLBACK_VERSION_MISSING")
    need(callback, "watch.lastObservedValue = value", "FACT_SIGNAL_VALUE_NOT_FORWARDED")
    need(callback, "watch.hasObservedValue = true", "FACT_SIGNAL_OBSERVED_VALUE_UNKNOWN")
    need(callback, 'this.m_dirty.Mark(n"QUEST_FACT")',
         "FACT_SIGNAL_DIRTY_NOT_PUBLISHED")
    need(callback, 'this.m_events.HasSubscribersFor(n"QUEST_FACT")',
         "FACT_SIGNAL_ALLOCATES_EVENTS_WITHOUT_SUBSCRIBERS")
    need(callback, "evt.intValue = value", "FACT_SIGNAL_EVENT_PAYLOAD_DROPPED")
    need(callback, "evt.nameValue = fact", "FACT_SIGNAL_EVENT_FACT_NAME_DROPPED")
    need(invalidate, "watch.hasObservedValue = false",
         "FACT_SIGNAL_SESSION_EXPOSES_STALE_VALUE")
    need(invalidate, "watch.version += 1u",
         "FACT_SIGNAL_SESSION_NO_VERSION_INVALIDATION")
    need(known, "return watch.hasObservedValue", "FACT_SIGNAL_KNOWN_GATE_MISSING")
    need(version, "return watch.version", "FACT_SIGNAL_EXACT_NAME_VERSION_MISSING")
    for name in ("AcquireFact", "ReleaseFact", "OnFactChanged", "InvalidateSession"):
        body = method(signals, name)
        for forbidden in ("DelayCallback(", "RegisterInputListener(",
                          "GetWorldPosition(", "GetFact("):
            if forbidden in body:
                raise AssertionError("FACT_SIGNAL_POLL_OR_SIDE_EFFECT_ADDED")


class FactSignalContracts(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        root = Path(ARGS.framework).resolve() / "r6/scripts/G-RedRuntime"
        cls.runtime = (root / "Core/Runtime.reds").read_text(encoding="utf-8-sig")
        cls.signals = (root / "Events/QuestFactSignals.reds").read_text(encoding="utf-8-sig")

    def test_owner_quest_fact_contract(self):
        verify(self.runtime, self.signals)

    def test_reject_non_unique_interest_token(self):
        bad = self.signals.replace("this.m_nextInterestID += 1", "")
        with self.assertRaisesRegex(AssertionError, "FACT_SIGNAL_TOKEN_NOT_UNIQUE"):
            verify(self.runtime, bad)

    def test_reject_early_unregister_when_other_consumers_exist(self):
        bad = self.signals.replace("if watch.interestCount == 0 {", "if true {")
        with self.assertRaisesRegex(AssertionError, "FACT_SIGNAL_EARLY_UNREGISTER"):
            verify(self.runtime, bad)

    def test_reject_observer_polling_without_interest(self):
        bad = self.runtime.replace(
            "this.m_factSignals.Initialize(this.GetGameInstance(), this.m_dirty, this.m_events);",
            'this.m_factSignals.Initialize(this.GetGameInstance(), this.m_dirty, this.m_events); this.m_factSignals.AcquireFact(n"unknown");'
        )
        with self.assertRaisesRegex(AssertionError, "DORMANT_FACT_SERVICE_REGISTERS_WITHOUT_CONSUMERS"):
            verify(bad, self.signals)

    def test_reject_unknown_session_value_reuse(self):
        old = method(self.signals, "InvalidateSession")
        bad = self.signals.replace(old, old.replace("watch.hasObservedValue = false", ""))
        with self.assertRaisesRegex(AssertionError, "FACT_SIGNAL_SESSION_EXPOSES_STALE_VALUE"):
            verify(self.runtime, bad)

    def test_reject_missing_real_engine_listener_cleanup(self):
        old = method(self.signals, "Shutdown")
        bad = self.signals.replace(old, old.replace(
            "qs.UnregisterListener(watch.fact, watch.engineListenerID)", ""))
        with self.assertRaisesRegex(AssertionError, "FACT_SIGNAL_SHUTDOWN_LEAK"):
            verify(self.runtime, bad)


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--framework", required=True)
    ARGS = parser.parse_args()
    unittest.main(argv=["owner_fact_signal_contracts"], verbosity=2)
