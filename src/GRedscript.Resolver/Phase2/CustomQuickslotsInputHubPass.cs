using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Approved Custom Quickslots input-route semantic. Build on the effective
/// post-generic source, never the historical whole-file optimized version.
/// Reuse G-REDruntime InputHub's action-specific dispatch and device notifier,
/// retain the original OnAction as an engine fallback if the runtime cannot
/// subscribe. Do not touch item, inventory, held-modifier or D-pad semantics.
/// </summary>
internal static class CustomQuickslotsInputHubPass
{
    internal const string Rule = "CUSTOM_QUICKSLOTS_SPECIFIC_INPUT_BRIDGE_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Start, int Open, int Close);
    private sealed record Patch(int Start, int End, string Text);
    private sealed class Refused : Exception
    {
        internal Refused(string reason) : base(reason) { }
    }

    private const string HubFields = """
@addField(PlayerPuppet)
private let customQuickslotsInputHub: wref<InputHub>;
@addField(PlayerPuppet)
private let customQuickslotsInputSubscriptions: array<Int32>;
@addField(PlayerPuppet)
private let customQuickslotsDeviceListener: ref<CustomQuickslotsDeviceListener>;
@addField(PlayerPuppet)
private let customQuickslotsDeviceSubscription: Int32;
@addField(PlayerPuppet)
private let customQuickslotsFallbackRegistered: Bool;
""";

    private const string ListenerMethods = """
  public func OnDeviceChanged(lastUsedKBM: Bool) -> Void {
    if !IsDefined(this.player) { return; }
    if Equals(this.lastUsedKBM, lastUsedKBM) { return; }
    this.lastUsedKBM = lastUsedKBM;
    CustomQuickslotsGlobalInputListener.SetLastUsedKBM(lastUsedKBM, this.player.gracePeriodOver);
    let actionEvt: ref<CustomQuickslotGamepadActionEvent> = new CustomQuickslotGamepadActionEvent();
    this.player.QueueEvent(actionEvt);
  }

  public func SyncGraceState() -> Void {
    if !IsDefined(this.player) { return; }
    let nowKBM = this.player.PlayerLastUsedKBM();
    this.lastUsedKBM = nowKBM;
    CustomQuickslotsGlobalInputListener.SetLastUsedKBM(nowKBM, this.player.gracePeriodOver);
  }

  public func OnGRedInput(evt: ref<InputEvent>) -> Void {
    if !IsDefined(this.player) || !IsDefined(evt) { return; }
    let nowKBM = this.player.PlayerLastUsedKBM();
    if NotEquals(this.lastUsedKBM, nowKBM) {
      this.lastUsedKBM = nowKBM;
      CustomQuickslotsGlobalInputListener.SetLastUsedKBM(nowKBM, this.player.gracePeriodOver);
    }
    let actionEvt: ref<CustomQuickslotGamepadActionEvent> = new CustomQuickslotGamepadActionEvent();
    if !this.lastUsedKBM {
      actionEvt.actionName = evt.actionName;
      actionEvt.actionType = evt.actionType;
      actionEvt.gamepadQuickslotInput = true;
    }
    this.player.QueueEvent(actionEvt);
    this.player.m_customHotkeyPlayerButtonData.ObserveAction(evt.actionName, evt.actionType);
  }
""";

    private const string DeviceClass = """
public class CustomQuickslotsDeviceListener extends InputDeviceListener {
  private let m_target: wref<CustomQuickslotsGlobalInputListener>;

  public func Init(target: ref<CustomQuickslotsGlobalInputListener>) -> Void {
    this.m_target = target;
  }

  public func OnGRedInputDeviceChanged(lastUsedKBM: Bool) -> Void {
    if IsDefined(this.m_target) {
      this.m_target.OnDeviceChanged(lastUsedKBM);
    }
  }
}
""";

    private const string AttachRouting = """
  // GCO Phase 2: route only current source-owned quickslot actions.
  // Retain the original engine listener if the runtime or any subscription
  // fails; never silently drop inputs when the shared service is unavailable.
  this.customQuickslotsFallbackRegistered = false;
  let cqRuntime = Runtime.Get(this.GetGame());
  if IsDefined(cqRuntime) && cqRuntime.IsReady() {
    this.customQuickslotsInputHub = cqRuntime.GetInputHub();
  }
  if IsDefined(this.customQuickslotsInputHub) {
    let cqActions: array<CName> = this.customQuickslotsInputListener.gamepadActions;
    for cqActionName in cqActions {
      let cqId = this.customQuickslotsInputHub.Subscribe(cqActionName, this.customQuickslotsInputListener);
      if cqId > 0 { ArrayPush(this.customQuickslotsInputSubscriptions, cqId); }
    }
    this.customQuickslotsDeviceListener = new CustomQuickslotsDeviceListener();
    this.customQuickslotsDeviceListener.Init(this.customQuickslotsInputListener);
    this.customQuickslotsDeviceSubscription = this.customQuickslotsInputHub.SubscribeDevice(this.customQuickslotsDeviceListener);
    if ArraySize(this.customQuickslotsInputSubscriptions) != ArraySize(cqActions) ||
       this.customQuickslotsDeviceSubscription <= 0 {
      for cqId in this.customQuickslotsInputSubscriptions {
        if cqId > 0 { this.customQuickslotsInputHub.Unsubscribe(cqId); }
      }
      if this.customQuickslotsDeviceSubscription > 0 {
        this.customQuickslotsInputHub.UnsubscribeDevice(this.customQuickslotsDeviceSubscription);
      }
      ArrayClear(this.customQuickslotsInputSubscriptions);
      this.customQuickslotsDeviceSubscription = 0;
      this.customQuickslotsDeviceListener = null;
      this.customQuickslotsInputHub = null;
    }
  }
  if !IsDefined(this.customQuickslotsInputHub) {
    this.RegisterInputListener(this.customQuickslotsInputListener);
    this.customQuickslotsFallbackRegistered = true;
  }
""";

    private const string DetachRouting = """
  // Same owner removes every subscription before releasing its listeners.
  if IsDefined(this.customQuickslotsInputHub) {
    for cqId in this.customQuickslotsInputSubscriptions {
      if cqId > 0 { this.customQuickslotsInputHub.Unsubscribe(cqId); }
    }
    if this.customQuickslotsDeviceSubscription > 0 {
      this.customQuickslotsInputHub.UnsubscribeDevice(this.customQuickslotsDeviceSubscription);
    }
  }
  if this.customQuickslotsFallbackRegistered {
    this.UnregisterInputListener(this.customQuickslotsInputListener);
  }
  this.customQuickslotsFallbackRegistered = false;
  ArrayClear(this.customQuickslotsInputSubscriptions);
  this.customQuickslotsDeviceSubscription = 0;
  this.customQuickslotsDeviceListener = null;
  this.customQuickslotsInputHub = null;
""";

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t,
        int i, string value) =>
        i >= 0 && i < t.Count && t[i].Value == value;

    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t,
        int at, params string[] values) =>
        values.Select((x, i) => Is(t, at + i, x)).All(x => x);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int at, string a, string b)
    {
        if (!Is(t, at, a)) return -1;
        int depth = 0;
        for (int i = at; i < t.Count; i++)
        {
            if (Is(t, i, a)) depth++;
            else if (Is(t, i, b) && --depth == 0) return i;
        }
        return -1;
    }

    private static bool Contains(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, params string[] p)
    {
        for (int i = first; i + p.Length <= last; i++)
            if (Seq(t, i, p)) return true;
        return false;
    }

    private static int Find(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, params string[] p)
    {
        int pos = -1;
        for (int i = first; i + p.Length <= last; i++)
            if (Seq(t, i, p))
            {
                if (pos >= 0)
                    throw new Refused("CQ_INPUT_MATCH_AMBIGUOUS");
                pos = i;
            }
        return pos;
    }

    private static Block? Type(IReadOnlyList<SourceAdmission.Token> t,
        string name)
    {
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (!Seq(t, i, "class", name)) continue;
            int op = i + 2;
            while (op < t.Count && !Is(t, op, "{") &&
                   !Is(t, op, ";")) op++;
            int end = Close(t, op, "{", "}");
            if (end < 0 || found is not null)
                throw new Refused("CQ_INPUT_CLASS_AMBIGUOUS");
            found = new(t[i].Start, op, end);
            i = end;
        }
        return found;
    }

    private static Block? Method(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, string name)
    {
        Block? found = null;
        for (int i = first; i + 3 < last; i++)
        {
            if (!Seq(t, i, "func", name, "(")) continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) throw new Refused("CQ_INPUT_METHOD_SIGNATURE");
            int open = args + 1;
            while (open < last && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int end = Close(t, open, "{", "}");
            if (end < 0 || end >= last || found is not null)
                throw new Refused("CQ_INPUT_METHOD_AMBIGUOUS");
            found = new(t[i].Start, open, end);
            i = end;
        }
        return found;
    }

    private static Block? Wrapped(IReadOnlyList<SourceAdmission.Token> t,
        string method)
    {
        Block? found = null;
        for (int i = 0; i + 5 < t.Count; i++)
        {
            if (!Seq(t, i, "@", "wrapMethod", "(", "PlayerPuppet", ")"))
                continue;
            int fn = i + 5;
            while (fn < t.Count && !Is(t, fn, "func") &&
                   !Is(t, fn, "{") && !Is(t, fn, "@")) fn++;
            if (!Seq(t, fn, "func", method, "(")) continue;
            int args = Close(t, fn + 2, "(", ")");
            int open = args + 1;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int end = Close(t, open, "{", "}");
            if (end < 0 || found is not null)
                throw new Refused("CQ_INPUT_PLAYER_WRAPPER_AMBIGUOUS");
            found = new(t[i].Start, open, end);
            i = end;
        }
        return found;
    }

    private static string Eol(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string Apply(string source, List<Patch> changes)
    {
        string text = source;
        int end = source.Length + 1;
        foreach (var p in changes.OrderByDescending(x => x.Start))
        {
            if (p.Start < 0 || p.End > end || p.Start > p.End)
                throw new InvalidDataException("CQ_INPUT_OVERLAPPING_EDITS");
            text = text[..p.Start] + p.Text + text[p.End..];
            end = p.Start;
        }
        return text;
    }

    private static void AddMethodIfMissing(string source,
        IReadOnlyList<SourceAdmission.Token> t, Block owner,
        string methodName, string body, List<string> missing)
    {
        Block? current = Method(t, owner.Open + 1, owner.Close, methodName);
        if (current is null) { missing.Add(body); return; }
        string method = source[t[current.Open].Start..t[current.Close].End];
        bool proven = methodName switch
        {
            "OnDeviceChanged" =>
                method.Contains("SetLastUsedKBM(", StringComparison.Ordinal) &&
                method.Contains("QueueEvent(", StringComparison.Ordinal) &&
                method.Contains("lastUsedKBM", StringComparison.Ordinal),
            "SyncGraceState" =>
                method.Contains("PlayerLastUsedKBM()", StringComparison.Ordinal) &&
                method.Contains("SetLastUsedKBM(", StringComparison.Ordinal),
            "OnGRedInput" =>
                method.Contains("QueueEvent(", StringComparison.Ordinal) &&
                method.Contains("ObserveAction(", StringComparison.Ordinal) &&
                method.Contains("evt.actionName", StringComparison.Ordinal) &&
                method.Contains("evt.actionType", StringComparison.Ordinal) &&
                method.Contains("gamepadQuickslotInput", StringComparison.Ordinal),
            _ => false
        };
        if (!proven)
            throw new Refused("CQ_INPUT_EXISTING_HELPER_UNPROVEN_" + methodName);
    }

    private static void AddFields(string source,
        IReadOnlyList<SourceAdmission.Token> t, List<Patch> changes)
    {
        string[] names = ["customQuickslotsInputHub",
            "customQuickslotsInputSubscriptions",
            "customQuickslotsDeviceListener",
            "customQuickslotsDeviceSubscription",
            "customQuickslotsFallbackRegistered"];
        string[] declarations = HubFields.Split('\n',
            StringSplitOptions.RemoveEmptyEntries)
            .Where(x => x.TrimStart().StartsWith("private let ",
                StringComparison.Ordinal)).ToArray();
        if (declarations.Length != names.Length)
            throw new InvalidDataException("CQ_INPUT_INTERNAL_FIELDS_INVALID");
        var missing = new List<string>();
        for (int i = 0; i < names.Length; i++)
        {
            int at = Find(t, 0, t.Count, "let", names[i], ":");
            if (at < 0)
                missing.Add("@addField(PlayerPuppet)" + Eol(source) +
                    declarations[i].Trim());
            else
            {
                string expected = declarations[i].Trim();
                int end = at;
                while (end < t.Count && !Is(t, end, ";")) end++;
                if (end >= t.Count)
                    throw new Refused("CQ_INPUT_FIELD_UNTERMINATED");
                string declared = source[t[at - 1].Start..t[end].End];
                if (!declared.Contains(expected[8..], StringComparison.Ordinal))
                    throw new Refused("CQ_INPUT_FIELD_TYPE_CONFLICT_" + names[i]);
            }
        }
        if (missing.Count == 0) return;
        int site = Find(t, 0, t.Count,
            "let", "customQuickslotsInputListener", ":");
        if (site < 0)
            throw new Refused("CQ_INPUT_LISTENER_FIELD_MISSING");
        int last = site;
        while (last < t.Count && !Is(t, last, ";")) last++;
        changes.Add(new(t[last].End, t[last].End,
            Eol(source) + Eol(source) +
            string.Join(Eol(source) + Eol(source), missing) + Eol(source)));
    }

    private static string Rewrite(string source, out bool modified)
    {
        var t = SourceAdmission.ReadTokens(source);
        if (!Contains(t, 0, t.Count, "module", "CustomQuickslots"))
            throw new Refused("CQ_INPUT_MODULE_MISSING");
        Block listener = Type(t, "CustomQuickslotsGlobalInputListener") ??
            throw new Refused("CQ_INPUT_LISTENER_MISSING");
        Block? device = Type(t, "CustomQuickslotsDeviceListener");
        Block? init = Method(t, listener.Open + 1, listener.Close, "Init");
        Block? action = Method(t, listener.Open + 1, listener.Close, "OnAction");
        Block? setter = Method(t, listener.Open + 1, listener.Close, "SetLastUsedKBM");
        Block? attached = Wrapped(t, "OnGameAttached");
        Block? detached = Wrapped(t, "OnDetach");
        Block? grace = Wrapped(t, "OnMakePlayerVisibleAfterSpawn");
        if (init is null || setter is null || attached is null ||
            detached is null || grace is null)
            throw new Refused("CQ_INPUT_REQUIRED_LIFECYCLE_MISSING");

        // Keep the full legacy event and generic membership implementation
        // as the fallback, including all original gamepad action names.
        if (!Contains(t, init.Open + 1, init.Close,
                "this", ".", "gamepadActions", "=", "[") ||
            !Contains(t, init.Open + 1, init.Close,
                "this", ".", "lastUsedKBM", "=") ||
            !Contains(t, init.Open + 1, init.Close,
                "PlayerLastUsedKBM", "("))
            throw new Refused("CQ_INPUT_ACTION_SOURCE_NOT_FIXED");
        if (action is not null &&
            (!Contains(t, action.Open + 1, action.Close,
                "ListenerAction", ".", "GetName", "(") ||
             !Contains(t, action.Open + 1, action.Close,
                "ListenerAction", ".", "GetType", "(") ||
             !Contains(t, action.Open + 1, action.Close,
                "ArrayContains", "(", "this", ".", "gamepadActions") ||
             !Contains(t, action.Open + 1, action.Close,
                "QueueEvent", "(") ||
             !Contains(t, action.Open + 1, action.Close,
                "ObserveAction", "(")))
            throw new Refused("CQ_INPUT_ORIGINAL_ONACTION_CHANGED");
        int sourceWrites = 0;
        for (int i = 0; i + 2 < t.Count; i++)
            if (Seq(t, i, "gamepadActions", "=")) sourceWrites++;
        if (sourceWrites != 1 ||
            Contains(t, 0, t.Count, "ArrayPush", "(", "this", ".",
                "gamepadActions") ||
            Contains(t, 0, t.Count, "ArrayClear", "(", "this", ".",
                "gamepadActions"))
            throw new Refused("CQ_INPUT_ACTION_ARRAY_MUTABLE_UNPROVEN");

        // Source-owned G-REDruntime ABI is separately checked by the caller.
        int listenerHeaderStart = t.ToList().FindIndex(x =>
            x.Start == listener.Start);
        if (listenerHeaderStart < 0 || !Seq(t, listenerHeaderStart,
                "class", "CustomQuickslotsGlobalInputListener"))
            throw new Refused("CQ_INPUT_LISTENER_HEADER_UNPROVEN");
        bool extendsInput = Contains(t, listenerHeaderStart + 2,
            listener.Open, "extends", "InputListener");
        bool extendsOther = Contains(t, listenerHeaderStart + 2,
            listener.Open, "extends");
        if (!extendsInput && extendsOther)
            throw new Refused("CQ_INPUT_LISTENER_BASE_INCOMPATIBLE");

        var changes = new List<Patch>();
        if (!extendsInput)
            changes.Add(new(t[listener.Open].Start,
                t[listener.Open].Start, "extends InputListener "));

        AddFields(source, t, changes);
        var missingMethods = new List<string>();
        string[] methodNames = ["OnDeviceChanged", "SyncGraceState",
            "OnGRedInput"];
        foreach (string methodName in methodNames)
        {
            // Locate one method declaration and its exact body in the
            // generated source template; leave original methods untouched.
            var template = SourceAdmission.ReadTokens(ListenerMethods);
            Block? owned = Method(template, 0, template.Count, methodName);
            if (owned is null)
                throw new InvalidDataException("CQ_INPUT_INTERNAL_METHOD_MISSING");
            int func = -1;
            for (int i = 0; i < owned.Open; i++)
                if (Seq(template, i, "func", methodName)) func = i;
            if (func < 0)
                throw new InvalidDataException("CQ_INPUT_INTERNAL_SIGNATURE_MISSING");
            int line = ListenerMethods.LastIndexOf('\n',
                Math.Max(0, template[func].Start - 1)) + 1;
            string code = ListenerMethods[line..template[owned.Close].End];
            AddMethodIfMissing(source, t, listener, methodName,
                code, missingMethods);
        }
        if (missingMethods.Count > 0)
            changes.Add(new(t[listener.Close].Start,
                t[listener.Close].Start,
                Eol(source) +
                string.Join(Eol(source) + Eol(source),
                    missingMethods).Replace("\n", Eol(source)) +
                Eol(source)));

        if (device is null)
            changes.Add(new(t[listener.Close].End, t[listener.Close].End,
                Eol(source) + Eol(source) +
                DeviceClass.Replace("\n", Eol(source))));
        else
        {
            if (!Contains(t, device.Open + 1, device.Close,
                    "this", ".", "m_target", "=", "target") ||
                !Contains(t, device.Open + 1, device.Close,
                    "OnDeviceChanged", "(", "lastUsedKBM", ")") ||
                !source[device.Start..t[device.Open].Start]
                    .Contains("extends InputDeviceListener", StringComparison.Ordinal))
                throw new Refused("CQ_INPUT_EXISTING_DEVICE_LISTENER_UNPROVEN");
        }

        // OnGameAttached: a legacy-only input registration is replaced
        // with a complete conditional bridge + engine fallback. Already
        // bridged sources keep existing behavior; unknown partial routes
        // are refused instead of stacking a second listener.
        int legacyAttach = Find(t, attached.Open + 1, attached.Close,
            "this", ".", "RegisterInputListener", "(",
            "this", ".", "customQuickslotsInputListener", ")", ";");
        bool bridgeAttach = Contains(t, attached.Open + 1,
            attached.Close, "this", ".", "customQuickslotsInputHub",
            ".", "Subscribe", "(");
        bool deviceAttach = Contains(t, attached.Open + 1,
            attached.Close, "SubscribeDevice", "(");
        bool fallback = Contains(t, attached.Open + 1, attached.Close,
            "customQuickslotsFallbackRegistered", "=", "true");
        if (bridgeAttach)
        {
            if (!deviceAttach || (legacyAttach >= 0 && !fallback))
                throw new Refused("CQ_INPUT_PARTIAL_ATTACH_ROUTE_UNPROVEN");
        }
        else if (legacyAttach >= 0)
            changes.Add(new(t[legacyAttach].Start,
                t[legacyAttach + 7].End,
                AttachRouting.Replace("\n", Eol(source))));
        else throw new Refused("CQ_INPUT_ATTACH_ROUTE_MISSING");

        int legacyDetach = Find(t, detached.Open + 1, detached.Close,
            "this", ".", "UnregisterInputListener", "(",
            "this", ".", "customQuickslotsInputListener", ")", ";");
        bool unsub = Contains(t, detached.Open + 1,
            detached.Close, "Unsubscribe", "(");
        bool unsubDevice = Contains(t, detached.Open + 1,
            detached.Close, "UnsubscribeDevice", "(");
        if (unsub || unsubDevice)
        {
            if (!unsub || !unsubDevice)
                throw new Refused("CQ_INPUT_PARTIAL_DETACH_ROUTE_UNPROVEN");
        }
        else if (legacyDetach >= 0)
            changes.Add(new(t[legacyDetach].Start,
                t[legacyDetach + 7].End,
                DetachRouting.Replace("\n", Eol(source))));
        else throw new Refused("CQ_INPUT_DETACH_ROUTE_MISSING");

        int graceSet = Find(t, grace.Open + 1, grace.Close,
            "this", ".", "gracePeriodOver", "=", "true", ";");
        if (graceSet < 0)
            throw new Refused("CQ_INPUT_GRACE_TRANSITION_CHANGED");
        if (!Contains(t, grace.Open + 1, grace.Close,
            "SyncGraceState", "(", ")", ";"))
            changes.Add(new(t[graceSet + 6].End,
                t[graceSet + 6].End,
                Eol(source) + "  if IsDefined(this.customQuickslotsInputListener) {" +
                Eol(source) + "    this.customQuickslotsInputListener.SyncGraceState();" +
                Eol(source) + "  }"));

        int moduleAt = Find(t, 0, t.Count, "module", "CustomQuickslots");
        if (moduleAt < 0) throw new Refused("CQ_INPUT_MODULE_AMBIGUOUS");
        if (!Contains(t, moduleAt + 2, Math.Min(t.Count, moduleAt + 14),
                "import", "GRedRuntime", ".", "*"))
            changes.Add(new(t[moduleAt + 1].End,
                t[moduleAt + 1].End,
                Eol(source) + "import GRedRuntime.*"));

        modified = changes.Count > 0;
        return modified ? Apply(source, changes) : source;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool FrameworkAvailable(string game)
    {
        string scripts = Path.Combine(game, "r6", "scripts");
        // Framework APIs are owned by G-REDruntime, not copied into mod
        // output. Do not guess compatibility from an old framework ZIP.
        string[] inputs = Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories)
            .Where(x => x.Replace('\\', '/')
                .Contains("/G-RedRuntime/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return inputs.Any(x =>
        {
            string s = File.ReadAllText(x);
            return s.Contains("class InputHub", StringComparison.Ordinal) &&
                s.Contains("func Subscribe(", StringComparison.Ordinal) &&
                s.Contains("func SubscribeDevice(", StringComparison.Ordinal) &&
                s.Contains("func UnsubscribeDevice(", StringComparison.Ordinal) &&
                s.Contains("func Unsubscribe(", StringComparison.Ordinal) &&
                s.Contains("class InputDeviceListener", StringComparison.Ordinal) &&
                s.Contains("class InputListener", StringComparison.Ordinal);
        }) && inputs.Any(x =>
        {
            string s = File.ReadAllText(x);
            return s.Contains("class Runtime extends ScriptableSystem",
                       StringComparison.Ordinal) &&
                s.Contains("func GetInputHub(", StringComparison.Ordinal) &&
                s.Contains("func IsReady(", StringComparison.Ordinal);
        });
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CQ_INPUT_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) ||
            Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("CQ_INPUT_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("CQ_INPUT_SOURCE_HASH_MISSING");
        var owners = new List<(string Rel, string Text)>();
        foreach (string file in Directory.EnumerateFiles(scripts,
            "*.reds", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CQ_INPUT_SOURCE_LINK_REFUSED");
            string src = File.ReadAllText(file, Encoding.UTF8);
            if (!src.Contains("CustomQuickslots", StringComparison.Ordinal) ||
                !src.Contains("CustomQuickslotsGlobalInputListener",
                    StringComparison.Ordinal))
                continue;
            var t = SourceAdmission.ReadTokens(src);
            if (Contains(t, 0, t.Count, "module", "CustomQuickslots") &&
                Type(t, "CustomQuickslotsGlobalInputListener") is not null)
                owners.Add((Path.GetRelativePath(scripts, file), src));
        }

        Result result;
        string? rewritten = null, relative = null;
        if (owners.Count == 0)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "CQ_INPUT_OWNER_NOT_INSTALLED", true, false);
        else if (owners.Count != 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                "CQ_INPUT_OWNER_AMBIGUOUS", true, false);
        else if (!FrameworkAvailable(game))
            result = new(Rule, "REFUSED", 0, 0, null,
                "CQ_INPUT_RUNTIME_ABI_NOT_PROVEN", true, false);
        else
        {
            (relative, string effective) = owners[0];
            if (basePreviewRoot is not null)
            {
                string overlay = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, "r6", "scripts", relative));
                if (!Inside(overlay, basePreviewRoot))
                    throw new InvalidDataException("CQ_INPUT_OVERLAY_ESCAPE");
                if (File.Exists(overlay))
                    effective = File.ReadAllText(overlay, Encoding.UTF8);
            }
            try
            {
                string updated = Rewrite(effective, out bool changed);
                if (changed)
                {
                    var check = SourceAdmission.ReadTokens(updated);
                    if (!Contains(check, 0, check.Count,
                            "class", "CustomQuickslotsDeviceListener") ||
                        !Contains(check, 0, check.Count,
                            "SubscribeDevice", "(") ||
                        !Contains(check, 0, check.Count,
                            "UnsubscribeDevice", "("))
                        throw new InvalidDataException(
                            "CQ_INPUT_GENERATED_POSTCONDITION_FAILED");
                    // A second invocation must be structurally complete,
                    // with no further source modifications or duplicate help.
                    string repeat = Rewrite(updated, out bool repeatEdits);
                    if (repeatEdits || repeat != updated)
                        throw new InvalidDataException(
                            "CQ_INPUT_NOT_IDEMPOTENT");
                    rewritten = updated;
                    result = new(Rule, "APPLIED", 1, 0,
                        "r6/scripts/" + relative.Replace('\\', '/'),
                        null, true, false);
                }
                else result = new(Rule, "COMPLETE", 0, 1,
                    "r6/scripts/" + relative.Replace('\\', '/'),
                    null, true, false);
            }
            catch (Refused ex)
            {
                result = new(Rule, "REFUSED", 0, 0,
                    "r6/scripts/" + relative.Replace('\\', '/'),
                    ex.Message, true, false);
            }
        }

        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CQ_INPUT_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        if (rewritten is not null && relative is not null)
        {
            string dest = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", relative));
            if (!Inside(dest, output))
                throw new InvalidDataException("CQ_INPUT_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, rewritten, new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }
}
