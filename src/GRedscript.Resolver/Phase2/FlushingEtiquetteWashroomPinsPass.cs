using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Reconstruct the game-tested handmade FE 7.2r1 + 9 washroom pin lifecycle
/// from the CURRENT effective source, never from an old whole-file overlay.
/// The session-owned pin service, explicit activation, relaxed FE script-data
/// identity, player detach ownership, and global Update wrapper removal are
/// one atomic semantic operation. No native bridge, timer or second poll.
/// </summary>
internal static class FlushingEtiquetteWashroomPinsPass
{
    internal const string Rule = "FE_SESSION_WASHROOM_PINS_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Open, int Close);
    private sealed record Edit(int Start, int End, string Value);
    private sealed record Owner(string Relative, string Text);
    private sealed class Unsafe : Exception
    {
        internal Unsafe(string reason) : base(reason) { }
    }

    private const string Service = """
public class FEWashroomMappinService extends ScriptableSystem {
  private let m_ids: array<NewMappinID>;
  private let m_sessionReady: Bool;

  public static func Get(gi: GameInstance) -> ref<FEWashroomMappinService> {
    return GameInstance.GetScriptableSystemsContainer(gi).Get(n"FlushingEtiquette.FEWashroomMappinService") as FEWashroomMappinService;
  }

  private func OnAttach() -> Void {
    let callbacks = GameInstance.GetCallbackSystem();
    callbacks.RegisterCallback(n"Session/Ready", this, n"OnSessionReady").SetLifetime(CallbackLifetime.Forever);
    callbacks.RegisterCallback(n"Session/BeforeEnd", this, n"OnSessionBeforeEnd").SetLifetime(CallbackLifetime.Forever);
  }

  private cb func OnSessionReady(event: ref<GameSessionEvent>) -> Void {
    if event.IsPreGame() { return; }
    this.m_sessionReady = true;
    this.EnsurePins();
  }

  private cb func OnSessionBeforeEnd(event: ref<GameSessionEvent>) -> Void {
    if event.IsPreGame() { return; }
    this.m_sessionReady = false;
    this.ClearPins();
  }

  private func OnPlayerAttach(request: ref<PlayerAttachRequest>) -> Void {
    if this.m_sessionReady { this.EnsurePins(); }
  }

  public func EnsurePins() -> Void {
    if !this.m_sessionReady { return; }
    let gi = this.GetGameInstance();
    let mappins = GameInstance.GetMappinSystem(gi);
    if !IsDefined(mappins) { return; }

    let expected = FEPlaces.PinCount();
    let valid = 0;
    let i = 0;
    while i < ArraySize(this.m_ids) {
      if IsDefined(mappins.GetMappin(this.m_ids[i])) {
        valid += 1;
      }
      i += 1;
    }
    if ArraySize(this.m_ids) == expected && valid == expected { return; }
    this.ClearPins();
    this.m_ids = FEPlaces.RegisterPins(gi);
  }

  private func ClearPins() -> Void {
    if ArraySize(this.m_ids) == 0 { return; }
    FEPlaces.UnregisterPins(this.GetGameInstance(), this.m_ids);
    ArrayClear(this.m_ids);
  }
}
""";

    private static bool Is(IReadOnlyList<SourceAdmission.Token> t,
        int i, string value) => i >= 0 && i < t.Count && t[i].Value == value;
    private static bool Seq(IReadOnlyList<SourceAdmission.Token> t,
        int i, params string[] names) =>
        names.Select((v, k) => Is(t, i + k, v)).All(v => v);
    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int opening, string open, string close)
    {
        if (!Is(t, opening, open)) return -1;
        int depth = 0;
        for (int i = opening; i < t.Count; i++)
        {
            if (Is(t, i, open)) depth++;
            else if (Is(t, i, close) && --depth == 0) return i;
        }
        return -1;
    }

    private static bool Module(IReadOnlyList<SourceAdmission.Token> t)
    {
        for (int i = 0; i + 1 < t.Count; i++)
            if (Seq(t, i, "module", "FlushingEtiquette")) return true;
        return false;
    }

    private static Block? Type(IReadOnlyList<SourceAdmission.Token> t,
        string kind, string name)
    {
        Block? result = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (!Seq(t, i, kind, name)) continue;
            int open = i + 2;
            while (open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int close = Close(t, open, "{", "}");
            if (close < 0 || result is not null)
                throw new Unsafe("FE_PIN_OWNER_AMBIGUOUS_OR_UNBALANCED");
            result = new(open, close);
            i = close;
        }
        return result;
    }

    private static Block? Method(IReadOnlyList<SourceAdmission.Token> t,
        Block owner, string name)
    {
        Block? result = null;
        for (int i = owner.Open + 1; i + 3 < owner.Close; i++)
        {
            if (!Seq(t, i, "func", name, "(")) continue;
            int args = Close(t, i + 2, "(", ")");
            if (args < 0) throw new Unsafe("FE_PIN_METHOD_ARGS_UNBALANCED");
            int open = args + 1;
            while (open < owner.Close && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int close = Close(t, open, "{", "}");
            if (close < 0 || close > owner.Close || result is not null)
                throw new Unsafe("FE_PIN_METHOD_AMBIGUOUS_OR_UNBALANCED");
            result = new(open, close);
            i = close;
        }
        return result;
    }

    private static bool Has(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, params string[] pattern)
    {
        for (int i = first; i + pattern.Length <= last; i++)
            if (Seq(t, i, pattern)) return true;
        return false;
    }

    private static int Find(IReadOnlyList<SourceAdmission.Token> t,
        int first, int last, params string[] pattern)
    {
        int found = -1;
        for (int i = first; i + pattern.Length <= last; i++)
            if (Seq(t, i, pattern))
            {
                if (found >= 0) throw new Unsafe("FE_PIN_OPERATION_AMBIGUOUS");
                found = i;
            }
        return found;
    }

    private static void Add(List<Edit> edits,
        IReadOnlyList<SourceAdmission.Token> t,
        int start, int end, string value) =>
        edits.Add(new(t[start].Start, t[end].End, value));

    private static string Apply(string source, List<Edit> edits)
    {
        string result = source;
        int last = source.Length + 1;
        foreach (var edit in edits.OrderByDescending(e => e.Start))
        {
            if (edit.Start < 0 || edit.End > last ||
                edit.End < edit.Start)
                throw new Unsafe("FE_PIN_OVERLAPPING_SOURCE_EDITS");
            result = result[..edit.Start] + edit.Value + result[edit.End..];
            last = edit.Start;
        }
        return result;
    }

    private static string Newline(string text) =>
        text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string PrepareService(string source,
        IReadOnlyList<SourceAdmission.Token> t, Block dataType,
        Block? existing, List<Edit> edits)
    {
        if (existing is null)
        {
            // Keep the player's original FEWashroomPinData and its complete
            // class body. Add only the historical session owner beside it.
            edits.Add(new(t[dataType.Close].End, t[dataType.Close].End,
                Newline(source) + Newline(source) +
                Service.Replace("\n", Newline(source))));
            return "ADDED";
        }

        // Recognize compatible markerless installations; complete missing
        // independent fields/methods, but never replace custom method bodies.
        if (!Has(t, existing.Open + 1, existing.Close,
                "extends", "ScriptableSystem"))
        {
            // The base declaration precedes '{'; check its owner header.
            string header = source[..t[existing.Open].Start];
            int at = header.LastIndexOf("class FEWashroomMappinService",
                StringComparison.Ordinal);
            if (at < 0 || !header[at..].Contains("extends ScriptableSystem",
                StringComparison.Ordinal))
                throw new Unsafe("FE_PIN_SERVICE_BASE_CHANGED");
        }
        var template = SourceAdmission.ReadTokens(Service);
        var prototype = Type(template, "class", "FEWashroomMappinService") ??
            throw new Unsafe("FE_PIN_INTERNAL_TEMPLATE_UNBALANCED");
        var missing = new List<string>();
        foreach (string field in new[] { "m_ids", "m_sessionReady" })
        {
            string declaration = field == "m_ids" ?
                "private let m_ids: array<NewMappinID>;" :
                "private let m_sessionReady: Bool;";
            bool found = Has(t, existing.Open + 1, existing.Close,
                "let", field, ":");
            if (!found) missing.Add(declaration);
        }
        string[] methods = ["Get", "OnAttach", "OnSessionReady",
            "OnSessionBeforeEnd", "OnPlayerAttach", "EnsurePins", "ClearPins"];
        foreach (string method in methods)
        {
            var own = Method(t, existing, method);
            var refMethod = Method(template, prototype, method) ??
                throw new Unsafe("FE_PIN_INTERNAL_TEMPLATE_METHOD_MISSING");
            if (own is null)
            {
                // Extend the existing manually optimized class, never make
                // a second callback service or duplicate method.
                int p = refMethod.Open;
                while (p > prototype.Open && !Is(template, p - 1, ";") &&
                       !Is(template, p - 1, "}")) p--;
                // The template's method signature is the text between its
                // previous declaration end and its opening brace.
                int signature = Service.LastIndexOf('\n',
                    template[refMethod.Open].Start);
                int start = signature + 1;
                string line = Service[start..template[refMethod.Close].End];
                if (!line.Contains("func " + method + "(", StringComparison.Ordinal))
                {
                    // Use the nearest visibility modifier preceding "func".
                    int func = template.Take(refMethod.Open)
                        .Select((x, i) => (x, i))
                        .Last(x => x.x.Value == "func").i;
                    int lineStart = Service.LastIndexOf('\n',
                        Math.Max(0, template[func].Start - 1)) + 1;
                    line = Service[lineStart..template[refMethod.Close].End];
                }
                missing.Add(line.TrimEnd());
                continue;
            }
            // Proof of all essential runtime side effects, rather than
            // marker strings or method names alone.
            string[][] proof = method switch
            {
                "Get" => [["GetScriptableSystemsContainer"], ["FEWashroomMappinService"]],
                "OnAttach" => [["RegisterCallback"], ["SetLifetime"]],
                "OnSessionReady" => [["IsPreGame"], ["m_sessionReady", "=", "true"],
                    ["this", ".", "EnsurePins", "("]],
                "OnSessionBeforeEnd" => [["IsPreGame"], ["m_sessionReady", "=", "false"],
                    ["this", ".", "ClearPins", "("]],
                "OnPlayerAttach" => [["m_sessionReady"], ["this", ".", "EnsurePins", "("]],
                "EnsurePins" => [["m_sessionReady"], ["FEPlaces", ".", "PinCount", "("],
                    ["GetMappin", "("], ["this", ".", "ClearPins", "("],
                    ["FEPlaces", ".", "RegisterPins", "("], ["this", ".", "m_ids"]],
                "ClearPins" => [["FEPlaces", ".", "UnregisterPins", "("],
                    ["ArrayClear", "("], ["this", ".", "m_ids"]],
                _ => []
            };
            if (proof.Any(pat => !Has(t, own.Open + 1, own.Close, pat)) ||
                (method == "OnAttach" &&
                 (!source[t[own.Open].Start..t[own.Close].End]
                      .Contains("Session/Ready", StringComparison.Ordinal) ||
                  !source[t[own.Open].Start..t[own.Close].End]
                      .Contains("Session/BeforeEnd", StringComparison.Ordinal) ||
                  !source[t[own.Open].Start..t[own.Close].End]
                      .Contains("n\"OnSessionReady\"", StringComparison.Ordinal) ||
                  !source[t[own.Open].Start..t[own.Close].End]
                      .Contains("n\"OnSessionBeforeEnd\"", StringComparison.Ordinal))))
                throw new Unsafe("FE_PIN_EXISTING_SERVICE_METHOD_INCOMPLETE_" +
                    method.ToUpperInvariant());
        }
        if (missing.Count != 0)
            edits.Add(new(t[existing.Close].Start, t[existing.Close].Start,
                Newline(source) + string.Join(Newline(source) + Newline(source),
                    missing) + Newline(source)));
        return missing.Count == 0 ? "COMPLETE" : "PARTIAL";
    }

    private static int CountPinSites(IReadOnlyList<SourceAdmission.Token> t,
        Block register)
    {
        int sites = -1, names = -1;
        for (int i = register.Open + 1; i + 4 < register.Close; i++)
        {
            if (!Seq(t, i, "let")) continue;
            bool spot = Seq(t, i, "let", "spots", ":", "array");
            bool keys = Seq(t, i, "let", "keys", ":", "array");
            if (!spot && !keys) continue;
            int opening = i + 1;
            while (opening < register.Close &&
                !Is(t, opening, "[")) opening++;
            int ending = Close(t, opening, "[", "]");
            if (ending < 0 || ending > register.Close)
                throw new Unsafe("FE_PIN_SITE_LIST_UNPROVEN");
            int count = 0;
            for (int n = opening + 1; n < ending; n++)
            {
                if (spot && Seq(t, n, "new", "Vector4", "(")) count++;
                if (keys && Seq(t, n, "n", "<literal>")) count++;
            }
            if (spot) sites = count;
            if (keys) names = count;
            i = ending;
        }
        if (sites <= 0 || sites != names)
            throw new Unsafe("FE_PIN_SITE_KEY_COUNT_UNPROVEN");
        return sites;
    }

    private static string Places(string source, out bool changed)
    {
        var t = SourceAdmission.ReadTokens(source);
        var pinData = Type(t, "class", "FEWashroomPinData") ??
            throw new Unsafe("FE_PIN_DATA_TYPE_MISSING");
        var places = Type(t, "class", "FEPlaces") ??
            throw new Unsafe("FE_PIN_PLACES_TYPE_MISSING");
        var register = Method(t, places, "RegisterPins") ??
            throw new Unsafe("FE_PIN_REGISTER_MISSING");
        var unregister = Method(t, places, "UnregisterPins") ??
            throw new Unsafe("FE_PIN_UNREGISTER_MISSING");
        var pinDataMethod = Method(t, places, "PinData") ??
            throw new Unsafe("FE_PIN_DATA_METHOD_MISSING");
        var edits = new List<Edit>();

        // The historical session service is optional in old source and
        // may already exist in a partially or completely optimized file.
        var service = Type(t, "class", "FEWashroomMappinService");
        PrepareService(source, t, pinData, service, edits);

        // Count actual currently installed immutable pin sites; avoid the
        // historical fixed count as a mod-version allowlist.
        int count = CountPinSites(t, register);
        bool pinCountPresent = Has(t, places.Open + 1, places.Close,
            "func", "PinCount", "(", ")", "-", ">", "Int32", "=",
            count.ToString());
        bool pinCountOther = Has(t, places.Open + 1, places.Close,
            "func", "PinCount", "(");
        if (!pinCountPresent)
        {
            if (pinCountOther)
                throw new Unsafe("FE_PIN_COUNT_CUSTOM_IMPLEMENTATION_UNPROVEN");
            edits.Add(new(t[places.Open].End, t[places.Open].End,
                Newline(source) + "  public static func PinCount() -> Int32 = " +
                count + Newline(source)));
        }

        // A successful registration is explicitly activated. Preserve
        // all user-defined pin positions, keys, data and registration loop.
        string[] original = ["ArrayPush", "(", "ids", ",", "mappins", ".",
            "RegisterMappin", "(", "pin", ",", "spots", "[", "i",
            "]", ")", ")", ";"];
        int old = Find(t, register.Open + 1, register.Close, original);
        int idPos = Find(t, register.Open + 1, register.Close,
            "let", "id", "=", "mappins", ".", "RegisterMappin", "(");
        if (old >= 0)
        {
            // Explicit debugCaption provides the same owner identity that
            // the game-tested handmade pin implementation recorded.
            string caption = Has(t, register.Open + 1, register.Close,
                "pin", ".", "debugCaption", "=") ? "" :
                "pin.debugCaption = \"FE_Washroom|\" + NameToString(keys[i]);" +
                Newline(source) + "      ";
            Add(edits, t, old, old + original.Length - 1,
                caption + "let id = mappins.RegisterMappin(pin, spots[i]);" +
                Newline(source) + "      mappins.SetMappinActive(id, true);" +
                Newline(source) + "      ArrayPush(ids, id);");
        }
        else
        {
            if (idPos < 0 || !Has(t, register.Open + 1,
                register.Close, "ArrayPush", "(", "ids", ",", "id", ")", ";"))
                throw new Unsafe("FE_PIN_REGISTRATION_CUSTOM_FLOW_UNPROVEN");
            if (!Has(t, register.Open + 1, register.Close,
                "pin", ".", "debugCaption", "="))
                edits.Add(new(t[idPos].Start, t[idPos].Start,
                    "pin.debugCaption = \"FE_Washroom|\" + NameToString(keys[i]);" +
                    Newline(source) + "      "));
            if (!Has(t, register.Open + 1, register.Close,
                "mappins", ".", "SetMappinActive", "(", "id", ",", "true", ")", ";"))
            {
                int end = idPos;
                while (end < register.Close && !Is(t, end, ";")) end++;
                if (end == register.Close)
                    throw new Unsafe("FE_PIN_REGISTRATION_ID_END_UNPROVEN");
                edits.Add(new(t[end].End, t[end].End,
                    Newline(source) +
                    "      mappins.SetMappinActive(id, true);"));
            }
        }

        // Match only the FE-owned author pin identity; the historical
        // variant restriction was removed to survive world-map rebuilding.
        string[] variantGuard = ["if", "!", "IsDefined", "(", "mappin", ")",
            "|", "|", "NotEquals", "(", "mappin", ".", "GetVariant",
            "(", ")", ",", "gamedataMappinVariant", ".", "SitVariant",
            ")", "{", "return", "null", ";", "}"];
        int gate = Find(t, pinDataMethod.Open + 1,
            pinDataMethod.Close, variantGuard);
        if (gate >= 0)
            Add(edits, t, gate, gate + variantGuard.Length - 1,
                "if !IsDefined(mappin) { return null; }");
        else if (!Has(t, pinDataMethod.Open + 1, pinDataMethod.Close,
            "if", "!", "IsDefined", "(", "mappin", ")", "{",
            "return", "null", ";", "}"))
            throw new Unsafe("FE_PIN_DATA_IDENTITY_CUSTOM_FLOW_UNPROVEN");
        if (!Has(t, pinDataMethod.Open + 1, pinDataMethod.Close,
            "GetScriptData", "(", ")", "as", "FEWashroomPinData"))
            throw new Unsafe("FE_PIN_DATA_CAST_CHANGED");

        // The original specific icon hooks stay untouched. They do not
        // by themselves prove pin registration; service and activation
        // above are also mandatory parts of this atomic semantic.
        if (!Has(t, 0, t.Count, "MinimapPOIMappinController", ")") ||
            !Has(t, 0, t.Count, "this", ".", "FEApplyWashroomIcon", "(") ||
            !Has(t, 0, t.Count, "SetTintColor", "(",
                "this", ".", "iconWidget", ",",
                "FEPlaces", ".", "PinTint", "("))
            throw new Unsafe("FE_PIN_TARGETED_STYLING_MISSING");

        // Remove only the exact old FE-only global wrapper, preserving
        // extra per-update behavior if a custom mod added anything.
        for (int i = 0; i + 5 < t.Count; i++)
        {
            if (!Seq(t, i, "@", "wrapMethod", "(",
                "BaseMinimapMappinController", ")"))
                continue;
            int fn = i + 5;
            while (fn < t.Count && !Is(t, fn, "func") &&
                   !Is(t, fn, "{") && !Is(t, fn, "@")) fn++;
            if (!Seq(t, fn, "func", "Update", "(")) continue;
            int args = Close(t, fn + 2, "(", ")");
            int open = args < 0 ? -1 : args + 1;
            while (open >= 0 && open < t.Count && !Is(t, open, "{") &&
                   !Is(t, open, ";")) open++;
            int close = Close(t, open, "{", "}");
            if (close < 0)
                throw new Unsafe("FE_PIN_GLOBAL_WRAPPER_UNBALANCED");
            string[] body = ["wrappedMethod", "(", ")", ";",
                "if", "IsDefined", "(", "FEPlaces", ".", "PinData",
                "(", "this", ".", "GetMappin", "(", ")", ")", ")",
                "{", "inkWidgetRef", ".", "SetTintColor",
                "(", "this", ".", "iconWidget", ",", "FEPlaces",
                ".", "PinTint", "(", ")", ")", ";", "}"];
            bool exact = close - open - 1 == body.Length;
            for (int k = 0; exact && k < body.Length; k++)
                exact = Is(t, open + k + 1, body[k]);
            if (!exact)
                throw new Unsafe("FE_PIN_GLOBAL_UPDATE_HAS_OTHER_EFFECTS");
            string span = source[t[i].Start..t[close].End];
            if (span.Contains("GCO-GENERIC:", StringComparison.Ordinal) ||
                span.Contains("GRSP-GENERIC:", StringComparison.Ordinal))
                throw new Unsafe("FE_PIN_GLOBAL_UPDATE_HAS_GENERIC_WORK");
            Add(edits, t, i, close, "");
            i = close;
        }

        changed = edits.Count != 0;
        return changed ? Apply(source, edits) : source;
    }

    private static string SystemSource(string source, out bool changed)
    {
        var t = SourceAdmission.ReadTokens(source);
        var sys = Type(t, "class", "FESystem") ??
            throw new Unsafe("FE_PIN_SYSTEM_CLASS_MISSING");
        var attach = Method(t, sys, "OnPlayerAttach") ??
            throw new Unsafe("FE_PIN_PLAYER_ATTACH_MISSING");
        var drop = Method(t, sys, "DropPlayer") ??
            throw new Unsafe("FE_PIN_DROP_PLAYER_MISSING");
        var edits = new List<Edit>();

        string[] oldAttach = ["this", ".", "m_pins", "=", "FEPlaces",
            ".", "RegisterPins", "(", "this", ".", "GetGameInstance",
            "(", ")", ")", ";"];
        int site = Find(t, attach.Open + 1, attach.Close, oldAttach);
        if (site >= 0)
            Add(edits, t, site, site + oldAttach.Length - 1,
                "let washrooms = FEWashroomMappinService.Get(this.GetGameInstance());" +
                Newline(source) +
                "    if IsDefined(washrooms) { washrooms.EnsurePins(); }");
        else if (!Has(t, attach.Open + 1, attach.Close,
            "FEWashroomMappinService", ".", "Get", "(") ||
            !Has(t, attach.Open + 1, attach.Close,
                "EnsurePins", "(", ")", ";"))
            throw new Unsafe("FE_PIN_PLAYER_REGISTRATION_CUSTOM_FLOW_UNPROVEN");

        string[] oldDrop = ["if", "ArraySize", "(", "this", ".",
            "m_pins", ")", ">", "0", "{", "FEPlaces", ".",
            "UnregisterPins", "(", "gi", ",", "this", ".", "m_pins",
            ")", ";", "ArrayClear", "(", "this", ".", "m_pins",
            ")", ";", "}"];
        int cleanup = Find(t, drop.Open + 1, drop.Close, oldDrop);
        if (cleanup >= 0)
            Add(edits, t, cleanup, cleanup + oldDrop.Length - 1,
                "// Session-owned pins are cleared on Session/BeforeEnd, not player detach.");
        else if (Has(t, drop.Open + 1, drop.Close,
            "FEPlaces", ".", "UnregisterPins", "(") ||
            Has(t, drop.Open + 1, drop.Close,
            "ArrayClear", "(", "this", ".", "m_pins", ")"))
            throw new Unsafe("FE_PIN_CUSTOM_PLAYER_CLEANUP_UNPROVEN");
        if (!Has(t, attach.Open + 1, attach.Close, "this", ".", "WatchPlayer", "(") ||
            !Has(t, drop.Open + 1, drop.Close, "this", ".", "m_player", "=", "null"))
            throw new Unsafe("FE_PIN_PLAYER_LIFECYCLE_CHANGED");
        changed = edits.Count != 0;
        return changed ? Apply(source, edits) : source;
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
            throw new InvalidDataException("FE_PIN_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null && (!Directory.Exists(basePreviewRoot) ||
            Inside(basePreviewRoot, game) ||
            Inside(output, basePreviewRoot) ||
            Inside(basePreviewRoot, output)))
            throw new InvalidDataException("FE_PIN_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("FE_PIN_SOURCE_HASH_MISSING");
        var places = new List<Owner>();
        var systems = new List<Owner>();
        int serviceCount = 0;
        foreach (string file in Directory.EnumerateFiles(scripts, "*.reds",
            SearchOption.AllDirectories))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("FE_PIN_SOURCE_LINK_REFUSED");
            string original = File.ReadAllText(file, Encoding.UTF8);
            if (!original.Contains("FlushingEtiquette", StringComparison.Ordinal))
                continue;
            string rel = Path.GetRelativePath(scripts, file);
            string effective = original;
            if (basePreviewRoot is not null)
            {
                string previous = Path.GetFullPath(Path.Combine(
                    basePreviewRoot, "r6", "scripts", rel));
                if (!Inside(previous, basePreviewRoot))
                    throw new InvalidDataException("FE_PIN_OVERLAY_PATH_ESCAPE");
                if (File.Exists(previous))
                    effective = File.ReadAllText(previous, Encoding.UTF8);
            }
            var tokens = SourceAdmission.ReadTokens(effective);
            if (!Module(tokens)) continue;
            if (Type(tokens, "class", "FEPlaces") is not null)
                places.Add(new(rel, effective));
            if (Type(tokens, "class", "FESystem") is not null)
                systems.Add(new(rel, effective));
            if (Type(tokens, "class", "FEWashroomMappinService") is not null)
                serviceCount++;
        }
        var generated = new List<(string Rel, string Text)>();
        Result result;
        try
        {
            if (places.Count == 0 && systems.Count == 0)
                result = new(Rule, "NOT_APPLICABLE", 0, 0, [],
                    "FE_PIN_OWNER_NOT_INSTALLED", true, false);
            else if (places.Count != 1 || systems.Count != 1 ||
                     serviceCount > 1 || places[0].Relative == systems[0].Relative)
                result = new(Rule, "REFUSED", 0, 0, [],
                    "FE_PIN_OWNER_FILES_OR_SERVICE_AMBIGUOUS", true, false);
            else if (serviceCount == 1 &&
                !places[0].Text.Contains("class FEWashroomMappinService",
                    StringComparison.Ordinal))
                result = new(Rule, "REFUSED", 0, 0, [],
                    "FE_PIN_SERVICE_DIFFERENT_FILE_UNPROVEN", true, false);
            else
            {
                string p = Places(places[0].Text, out bool pChange);
                string s = SystemSource(systems[0].Text, out bool sChange);
                // Postconditions are structural: a second generation of
                // the actual combined output produces no further edits.
                string nextP = Places(p, out bool pAgain);
                string nextS = SystemSource(s, out bool sAgain);
                if (pAgain || sAgain || nextP != p || nextS != s)
                    throw new InvalidDataException("FE_PIN_POSTCONDITION_FAILED");
                if (pChange) generated.Add((places[0].Relative, p));
                if (sChange) generated.Add((systems[0].Relative, s));
                result = new(Rule, generated.Count == 0 ?
                    "COMPLETE" : "APPLIED", generated.Count == 0 ? 0 : 1,
                    generated.Count == 0 ? 1 : 0,
                    generated.Select(f => "r6/scripts/" +
                        f.Rel.Replace('\\', '/')).ToArray(),
                    null, true, false);
            }
        }
        catch (Unsafe ex)
        {
            result = new(Rule, "REFUSED", 0, 0, [], ex.Message, true, false);
        }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("FE_PIN_SOURCE_CHANGED_DURING_STAGE");
        Directory.CreateDirectory(output);
        foreach (var file in generated)
        {
            if (result.Status != "APPLIED") break;
            string dest = Path.GetFullPath(Path.Combine(output,
                "r6", "scripts", file.Rel));
            if (!Inside(dest, output))
                throw new InvalidDataException("FE_PIN_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllText(dest, file.Text, new UTF8Encoding(false));
        }
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
