using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Owner-approved handmade pass-8 D-pad concept, reconstructed from the
/// effective post-generic + approved InputHub source. The four public hot
/// getters return cached derived values. Recompute only on modifier-state
/// writes, configuration, player/device changes and engine fallback actions.
/// Keep the original source-owned modifier scan and pad identity predicate
/// intact, rather than copying a historical whole player_puppet.reds file.
/// </summary>
internal static class CustomQuickslotsDpadCachePass
{
    internal const string Rule = "CUSTOM_QUICKSLOTS_DPAD_STATE_CACHE_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Scope(int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed class Incompatible : Exception
    {
        internal Incompatible(string reason) : base(reason) { }
    }

    private static readonly (string Direction, string Field, string Array)[] Dirs =
    [
        ("Left", "normalDpadLeftProhibited", "modifiersForDpadLeft"),
        ("Right", "normalDpadRightProhibited", "modifiersForDpadRight"),
        ("Up", "normalDpadUpProhibited", "modifiersForDpadUp"),
        ("Down", "normalDpadDownProhibited", "modifiersForDpadDown")
    ];
    private static readonly string[] StateFields =
    ["crouchHeld", "dropBodyHeld", "quickMeleeHeld"];
    private static readonly string[] CacheFields =
    ["lastUsedKBM", "normalDpadLeftProhibited",
     "normalDpadRightProhibited", "normalDpadUpProhibited",
     "normalDpadDownProhibited"];

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t,
        int i, string value) =>
        i >= 0 && i < t.Count && t[i].Value == value;

    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t,
        int i, params string[] values) =>
        values.Select((v, k) => Is(t, i + k, v)).All(v => v);

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (!Is(t, start, opening)) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (Is(t, i, opening)) depth++;
            else if (Is(t, i, closing) && --depth == 0) return i;
        }
        return -1;
    }

    private static int Find(IReadOnlyList<SourceAdmission.Token> t,
        int begin, int end, params string[] sequence)
    {
        int found = -1;
        for (int i = begin; i + sequence.Length <= end; i++)
        {
            if (!Seq(t, i, sequence)) continue;
            if (found >= 0)
                throw new Incompatible("CQ_DPAD_AMBIGUOUS_OPERATION");
            found = i;
        }
        return found;
    }

    private static bool Has(IReadOnlyList<SourceAdmission.Token> t,
        int begin, int end, params string[] sequence)
    {
        for (int i = begin; i + sequence.Length <= end; i++)
            if (Seq(t, i, sequence)) return true;
        return false;
    }

    private static Scope? Class(IReadOnlyList<SourceAdmission.Token> t,
        string name)
    {
        Scope? result = null;
        for (int i = 0; i + 1 < t.Count; i++)
        {
            if (!Seq(t, i, "class", name)) continue;
            int open = i + 2;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int end = Close(t, open, "{", "}");
            if (end < 0 || result is not null)
                throw new Incompatible("CQ_DPAD_OWNER_AMBIGUOUS");
            result = new(open, end);
            i = end;
        }
        return result;
    }

    private static Scope? Method(IReadOnlyList<SourceAdmission.Token> t,
        Scope scope, string name)
    {
        Scope? found = null;
        for (int i = scope.Open + 1; i + 3 < scope.Close; i++)
        {
            if (!Seq(t, i, "func", name, "(")) continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0)
                throw new Incompatible("CQ_DPAD_METHOD_ARGS_UNBALANCED");
            int open = args + 1;
            while (open < scope.Close && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int end = Close(t, open, "{", "}");
            if (end < 0 || end >= scope.Close || found is not null)
                throw new Incompatible("CQ_DPAD_METHOD_UNPROVEN_" + name);
            found = new(open, end);
            i = end;
        }
        return found;
    }

    private static string Eol(string s) =>
        s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static void Insert(List<Edit> changes,
        IReadOnlyList<SourceAdmission.Token> t,
        int before, string value) =>
        changes.Add(new(t[before].Start, t[before].Start, value));

    private static string Apply(string text, List<Edit> changes)
    {
        string result = text;
        int previousStart = text.Length + 1;
        foreach (var change in changes.OrderByDescending(e => e.Start)
                     .ThenByDescending(e => e.End))
        {
            if (change.Start < 0 || change.End > previousStart ||
                change.Start > change.End)
                throw new InvalidDataException("CQ_DPAD_OVERLAPPING_EDITS");
            result = result[..change.Start] + change.Text +
                result[change.End..];
            previousStart = change.Start;
        }
        return result;
    }

    private static Scope Require(IReadOnlyList<SourceAdmission.Token> t,
        Scope parent, string name) =>
        Method(t, parent, name) ??
        throw new Incompatible("CQ_DPAD_REQUIRED_METHOD_MISSING_" + name);

    private static void ProveOwnerWriters(
        IReadOnlyList<SourceAdmission.Token> t, Scope owner,
        Scope observe, Scope modifiers, Scope player, Scope? device,
        bool cachePresent)
    {
        foreach (string name in StateFields)
        {
            if (!Has(t, owner.Open + 1, owner.Close, "let", name, ":", "Bool"))
                throw new Incompatible("CQ_DPAD_HELD_STATE_MISSING_" + name);
            int writes = 0;
            for (int i = owner.Open + 1; i + 3 < owner.Close; i++)
            {
                if (!Seq(t, i, "this", ".", name, "=")) continue;
                if (i <= observe.Open || i >= observe.Close)
                    throw new Incompatible("CQ_DPAD_HELD_STATE_EXTERNAL_WRITER_" + name);
                writes++;
            }
            if (writes == 0)
                throw new Incompatible("CQ_DPAD_HELD_STATE_WRITER_MISSING_" + name);
        }
        foreach (var dir in Dirs)
        {
            if (!Has(t, owner.Open + 1, owner.Close,
                    "let", dir.Array, ":", "array", "<", "CName", ">"))
                throw new Incompatible("CQ_DPAD_MODIFIER_ARRAY_MISSING_" + dir.Array);
            int assignments = 0;
            for (int i = owner.Open + 1; i + 3 < owner.Close; i++)
            {
                if (Seq(t, i, "this", ".", dir.Array, "="))
                {
                    if (i <= modifiers.Open || i >= modifiers.Close)
                        throw new Incompatible("CQ_DPAD_MODIFIER_EXTERNAL_WRITER_" + dir.Array);
                    assignments++;
                }
                if (Seq(t, i, "ArrayPush", "(", "this", ".", dir.Array) ||
                    Seq(t, i, "ArrayClear", "(", "this", ".", dir.Array) ||
                    Seq(t, i, "ArrayErase", "(", "this", ".", dir.Array))
                    throw new Incompatible("CQ_DPAD_MODIFIER_MUTATION_UNPROVEN_" + dir.Array);
            }
            if (assignments != 1)
                throw new Incompatible("CQ_DPAD_MODIFIER_ASSIGNMENT_UNPROVEN_" + dir.Array);
        }
        int assigned = 0;
        for (int i = owner.Open + 1; i + 3 < owner.Close; i++)
        {
            if (!Seq(t, i, "this", ".", "player", "=")) continue;
            if (i <= player.Open || i >= player.Close)
                throw new Incompatible("CQ_DPAD_PLAYER_EXTERNAL_WRITER");
            assigned++;
        }
        if (assigned != 1)
            throw new Incompatible("CQ_DPAD_PLAYER_ASSIGNMENT_UNPROVEN");

        if (!cachePresent) return;
        // A hand-optimized partial may already write lastUsedKBM.
        // Verify all such existing writes belong to SetPlayer/SetDeviceMode.
        for (int i = owner.Open + 1; i + 3 < owner.Close; i++)
        {
            if (!Seq(t, i, "this", ".", "lastUsedKBM", "=")) continue;
            if ((i > player.Open && i < player.Close) ||
                (device is not null && i > device.Open && i < device.Close))
                continue;
            throw new Incompatible("CQ_DPAD_DEVICE_CACHE_EXTERNAL_WRITER");
        }
    }

    private static string RecomputeSource(string helper)
    {
        return $$"""
  private func RecomputeDpadProhibitions() -> Void {
    // Preserve the original PlayerLastUsedPad predicate (including unknown
    // device state) when updating the cached value, not at each getter.
    if !IsDefined(this.player) || !this.player.PlayerLastUsedPad() {
      this.normalDpadLeftProhibited = false;
      this.normalDpadRightProhibited = false;
      this.normalDpadUpProhibited = false;
      this.normalDpadDownProhibited = false;
      return;
    }
    this.normalDpadLeftProhibited = this.{{helper}}(this.modifiersForDpadLeft);
    this.normalDpadRightProhibited = this.{{helper}}(this.modifiersForDpadRight);
    this.normalDpadUpProhibited = this.{{helper}}(this.modifiersForDpadUp);
    this.normalDpadDownProhibited = this.{{helper}}(this.modifiersForDpadDown);
  }
""";
    }

    private const string DeviceModeSource = """
  public func SetDeviceMode(lastUsedKBM: Bool) -> Void {
    if NotEquals(this.lastUsedKBM, lastUsedKBM) {
      this.lastUsedKBM = lastUsedKBM;
      this.RecomputeDpadProhibitions();
    }
  }
""";

    private static string Normalize(string value, string source) =>
        value.Replace("\r\n", "\n", StringComparison.Ordinal)
             .Replace('\r', '\n').Replace("\n", Eol(source),
                StringComparison.Ordinal);

    private static void InsertAfterAssignment(
        IReadOnlyList<SourceAdmission.Token> t, Scope method,
        string owner, string field, string value,
        string expression, List<Edit> edits, string source,
        bool required = true)
    {
        if (Has(t, method.Open + 1, method.Close,
                "SetDeviceMode", "(")) return;
        int site = Find(t, method.Open + 1, method.Close,
            owner, ".", field, "=", value, ";");
        if (site < 0)
        {
            if (required)
                throw new Incompatible("CQ_DPAD_DEVICE_HOOK_ASSIGNMENT_UNPROVEN_" +
                    field + "_" + value);
            return;
        }
        int at = site + 5;
        edits.Add(new(t[at].End, t[at].End,
            Eol(source) + "    " + expression));
    }

    private static string Rewrite(string source, out bool changed)
    {
        var t = SourceAdmission.ReadTokens(source);
        if (!Has(t, 0, t.Count, "module", "CustomQuickslots"))
            throw new Incompatible("CQ_DPAD_MODULE_NOT_FOUND");
        Scope data = Class(t, "CustomHotkeyPlayerButtonData") ??
            throw new Incompatible("CQ_DPAD_DATA_CLASS_MISSING");
        Scope input = Class(t, "CustomQuickslotsGlobalInputListener") ??
            throw new Incompatible("CQ_DPAD_INPUT_LISTENER_MISSING");
        Scope observe = Require(t, data, "ObserveAction");
        Scope modifiers = Require(t, data, "SetModifiers");
        Scope player = Require(t, data, "SetPlayer");
        Scope? deviceMode = Method(t, data, "SetDeviceMode");
        Scope? recompute = Method(t, data, "RecomputeDpadProhibitions");
        Scope? scanOriginal = Method(t, data, "IsNormalDpadDirProhibited");
        Scope? scanHandmade = Method(t, data, "ComputeDpadDirProhibited");
        if ((scanOriginal is null) == (scanHandmade is null))
            throw new Incompatible("CQ_DPAD_SCAN_HELPER_MISSING_OR_AMBIGUOUS");
        string scanName = scanOriginal is not null ?
            "IsNormalDpadDirProhibited" : "ComputeDpadDirProhibited";
        Scope scan = scanOriginal ?? scanHandmade!;
        if (!Has(t, scan.Open + 1, scan.Close, "this", ".", "dropBodyHeld") ||
            !Has(t, scan.Open + 1, scan.Close, "this", ".", "crouchHeld") ||
            !Has(t, scan.Open + 1, scan.Close, "this", ".", "quickMeleeHeld") ||
            !Has(t, scan.Open + 1, scan.Close, "dpadDirModifiers", "[", "i", "]") ||
            !Has(t, scan.Open + 1, scan.Close, "return", "true", ";") ||
            !Has(t, scan.Open + 1, scan.Close, "return", "false", ";"))
            throw new Incompatible("CQ_DPAD_SCAN_BEHAVIOR_UNPROVEN");

        var getters = Dirs.Select(dir => (
            Direction: dir.Direction, Field: dir.Field, Array: dir.Array,
            Scope: Require(t, data, "IsNormalDpad" + dir.Direction + "Prohibited")))
            .ToArray();

        // The cache is derived only from the three held booleans, four
        // modifier lists, owning player and pad state. No extra writers or
        // mutable external aliases may invalidate it behind our hooks.
        bool hasLastUsedField = Has(t, data.Open + 1, data.Close,
            "let", "lastUsedKBM", ":", "Bool");
        ProveOwnerWriters(t, data, observe, modifiers, player, deviceMode,
            hasLastUsedField);

        // Require a previously approved, real InputHub/device bridge.
        // The bundled one-pass GCO generation creates it as rule #16; a
        // complete handmade source may have it without GCO markers.
        var init = Require(t, input, "Init");
        var onDevice = Require(t, input, "OnDeviceChanged");
        var sync = Require(t, input, "SyncGraceState");
        var hub = Require(t, input, "OnGRedInput");
        Scope? legacy = Method(t, input, "OnAction");
        if (!Has(t, onDevice.Open + 1, onDevice.Close,
                "SetLastUsedKBM", "(") ||
            !Has(t, sync.Open + 1, sync.Close,
                "SetLastUsedKBM", "(") ||
            !Has(t, hub.Open + 1, hub.Close,
                "ObserveAction", "(") ||
            !Has(t, init.Open + 1, init.Close,
                "PlayerLastUsedKBM", "("))
            throw new Incompatible("CQ_DPAD_INPUT_DEVICE_BRIDGE_UNPROVEN");
        if (legacy is not null &&
            !Has(t, legacy.Open + 1, legacy.Close,
                "ArrayContains", "(", "this", ".", "gamepadActions"))
            throw new Incompatible("CQ_DPAD_ENGINE_FALLBACK_MODIFIED");

        var changes = new List<Edit>();
        string nl = Eol(source);
        foreach (string field in CacheFields)
        {
            int found = Find(t, data.Open + 1, data.Close,
                "let", field, ":", "Bool", ";");
            if (found >= 0) continue;
            if (Has(t, data.Open + 1, data.Close, "let", field, ":"))
                throw new Incompatible("CQ_DPAD_FIELD_TYPE_CONFLICT_" + field);
            Insert(changes, t, data.Open + 1,
                "  private let " + field + ": Bool;" + nl);
        }

        foreach (var getter in getters)
        {
            // Direct single-expression getter only. Preserve caller identity,
            // method signature, comments and unrelated custom member logic.
            int o = getter.Scope.Open + 1;
            int e = getter.Scope.Close;
            string[] cached = ["return", "this", ".", getter.Field, ";"];
            if (e - o == cached.Length &&
                Seq(t, o, cached)) continue;
            string[] original = ["return", "this", ".", scanName, "(",
                "this", ".", getter.Array, ")", ";"];
            if (e - o != original.Length || !Seq(t, o, original))
                throw new Incompatible("CQ_DPAD_GETTER_CUSTOM_FLOW_" + getter.Direction);
            changes.Add(new(t[o].Start, t[o + original.Length - 1].End,
                "return this." + getter.Field + ";"));
        }

        if (recompute is null)
            Insert(changes, t, data.Close,
                nl + Normalize(RecomputeSource(scanName), source) + nl);
        else
        {
            foreach (var dir in Dirs)
                if (!Has(t, recompute.Open + 1, recompute.Close,
                        "this", ".", dir.Field, "=") ||
                    !Has(t, recompute.Open + 1, recompute.Close,
                        "this", ".", dir.Array))
                    throw new Incompatible("CQ_DPAD_RECOMPUTE_CUSTOM_FLOW_" + dir.Direction);
        }

        if (deviceMode is null)
            Insert(changes, t, data.Close,
                nl + Normalize(DeviceModeSource, source) + nl);
        else if (!Has(t, deviceMode.Open + 1, deviceMode.Close,
                     "this", ".", "RecomputeDpadProhibitions", "(") ||
                 !Has(t, deviceMode.Open + 1, deviceMode.Close,
                     "this", ".", "lastUsedKBM", "=", "lastUsedKBM"))
            throw new Incompatible("CQ_DPAD_EXISTING_DEVICE_MODE_UNPROVEN");

        if (!Has(t, modifiers.Open + 1, modifiers.Close,
                "this", ".", "RecomputeDpadProhibitions", "("))
            Insert(changes, t, modifiers.Close,
                "    this.RecomputeDpadProhibitions();" + nl + "  ");

        if (!Has(t, player.Open + 1, player.Close,
                "this", ".", "lastUsedKBM", "="))
        {
            int at = Find(t, player.Open + 1, player.Close,
                "this", ".", "player", "=", "player", ";");
            if (at < 0)
                throw new Incompatible("CQ_DPAD_PLAYER_SETTER_UNPROVEN");
            changes.Add(new(t[at + 5].End, t[at + 5].End,
                nl + "    if IsDefined(player) {" +
                nl + "      this.lastUsedKBM = player.PlayerLastUsedKBM();" +
                nl + "    }"));
        }
        if (!Has(t, player.Open + 1, player.Close,
                "this", ".", "RecomputeDpadProhibitions", "("))
            Insert(changes, t, player.Close,
                "    this.RecomputeDpadProhibitions();" + nl + "  ");

        if (!Has(t, observe.Open + 1, observe.Close,
                "this", ".", "RecomputeDpadProhibitions", "("))
        {
            int signal = Find(t, observe.Open + 1, observe.Close,
                "if", "shouldSendEvent", "{");
            if (signal < 0 ||
                !Has(t, signal + 3, observe.Close,
                    "this", ".", "player", ".", "QueueEvent", "("))
                throw new Incompatible("CQ_DPAD_OBSERVE_EVENT_FLOW_UNPROVEN");
            changes.Add(new(t[signal + 2].End, t[signal + 2].End,
                nl + "      this.RecomputeDpadProhibitions();"));
        }

        // Every input producer synchronizes the cache before notifying
        // the Lua bridge or queueing its CustomQuickslotGamepadActionEvent.
        InsertAfterAssignment(t, init, "this", "lastUsedKBM",
            "player", // parsed method-local expression starts with player.
            "this.player.m_customHotkeyPlayerButtonData.SetDeviceMode(this.lastUsedKBM);",
            changes, source, required: false);
        // Init uses player.PlayerLastUsedKBM(), which is more than one token.
        if (!Has(t, init.Open + 1, init.Close, "SetDeviceMode", "("))
        {
            int at = Find(t, init.Open + 1, init.Close,
                "this", ".", "lastUsedKBM", "=", "player", ".",
                "PlayerLastUsedKBM", "(", ")", ";");
            if (at < 0)
                throw new Incompatible("CQ_DPAD_INIT_DEVICE_CAPTURE_UNPROVEN");
            changes.Add(new(t[at + 9].End, t[at + 9].End,
                nl + "    if IsDefined(player.m_customHotkeyPlayerButtonData) {" +
                nl + "      player.m_customHotkeyPlayerButtonData.SetDeviceMode(this.lastUsedKBM);" +
                nl + "    }"));
        }

        foreach (var producer in new[] {
            (Method: onDevice, Value: "lastUsedKBM"),
            (Method: sync, Value: "nowKBM"),
            (Method: hub, Value: "nowKBM") })
        {
            if (Has(t, producer.Method.Open + 1, producer.Method.Close,
                    "SetDeviceMode", "(")) continue;
            int at = Find(t, producer.Method.Open + 1,
                producer.Method.Close,
                "this", ".", "lastUsedKBM", "=", producer.Value, ";");
            if (at < 0)
                throw new Incompatible("CQ_DPAD_DEVICE_PRODUCER_UNPROVEN");
            changes.Add(new(t[at + 5].End, t[at + 5].End,
                nl + "      if IsDefined(this.player.m_customHotkeyPlayerButtonData) {" +
                nl + "        this.player.m_customHotkeyPlayerButtonData.SetDeviceMode(this.lastUsedKBM);" +
                nl + "      }"));
        }

        if (legacy is not null &&
            !Has(t, legacy.Open + 1, legacy.Close, "SetDeviceMode", "("))
        {
            // Preserve the original generic fixed-action matching. Update
            // only the cached device mode after its own original switches.
            int at = Find(t, legacy.Open + 1, legacy.Close,
                "let", "actionName", ":", "CName", "=");
            if (at < 0)
                throw new Incompatible("CQ_DPAD_ENGINE_FALLBACK_INSERTION_UNPROVEN");
            Insert(changes, t, at,
                "    if IsDefined(this.player.m_customHotkeyPlayerButtonData) {" +
                nl + "      this.player.m_customHotkeyPlayerButtonData.SetDeviceMode(this.lastUsedKBM);" +
                nl + "    }" + nl + "    ");
        }

        changed = changes.Count != 0;
        return changed ? Apply(source, changes) : source;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CQ_DPAD_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("CQ_DPAD_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sourceHash = admission.SourceStackSha256 ??
            throw new InvalidDataException("CQ_DPAD_SOURCE_FINGERPRINT_MISSING");
        var owners = new List<(string Relative, string Text)>();
        foreach (string file in Directory.EnumerateFiles(scripts,
                     "*.reds", SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("CQ_DPAD_SOURCE_LINK_REFUSED");
            string original = File.ReadAllText(file, Encoding.UTF8);
            if (!original.Contains("CustomHotkeyPlayerButtonData",
                StringComparison.Ordinal)) continue;
            var originalTokens = SourceAdmission.ReadTokens(original);
            if (!Has(originalTokens, 0, originalTokens.Count,
                    "module", "CustomQuickslots") ||
                Class(originalTokens, "CustomHotkeyPlayerButtonData") is null)
                continue;
            string relative = Path.GetRelativePath(scripts, file);
            string effective = original;
            if (basePreviewRoot is not null)
            {
                string overlay = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, "r6", "scripts", relative));
                if (!Inside(overlay, basePreviewRoot))
                    throw new InvalidDataException("CQ_DPAD_OVERLAY_PATH_ESCAPE");
                if (File.Exists(overlay))
                    effective = File.ReadAllText(overlay, Encoding.UTF8);
            }
            owners.Add((relative, effective));
        }

        Result result;
        string? edited = null, rel = null;
        if (owners.Count == 0)
            result = new(Rule, "NOT_APPLICABLE", 0, 0, null,
                "CQ_DPAD_MOD_NOT_INSTALLED", true, false);
        else if (owners.Count != 1)
            result = new(Rule, "REFUSED", 0, 0, null,
                "CQ_DPAD_MULTIPLE_OWNER_CLASSES", true, false);
        else
        {
            (rel, string effective) = owners[0];
            try
            {
                string next = Rewrite(effective, out bool hasEdits);
                if (hasEdits)
                {
                    string repeat = Rewrite(next, out bool again);
                    if (again || next != repeat)
                        throw new InvalidDataException(
                            "CQ_DPAD_GENERATOR_NOT_IDEMPOTENT");
                    edited = next;
                    result = new(Rule, "APPLIED", 1, 0,
                        "r6/scripts/" + rel.Replace('\\', '/'),
                        null, true, false);
                }
                else result = new(Rule, "COMPLETE", 0, 1,
                    "r6/scripts/" + rel.Replace('\\', '/'),
                    null, true, false);
            }
            catch (Incompatible ex)
            {
                result = new(Rule, "REFUSED", 0, 0,
                    "r6/scripts/" + rel.Replace('\\', '/'),
                    ex.Message, true, false);
            }
        }
        if (!string.Equals(sourceHash, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CQ_DPAD_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        if (edited is not null && rel is not null)
        {
            string file = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", rel));
            if (!Inside(file, output))
                throw new InvalidDataException("CQ_DPAD_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, edited, new UTF8Encoding(false));
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
