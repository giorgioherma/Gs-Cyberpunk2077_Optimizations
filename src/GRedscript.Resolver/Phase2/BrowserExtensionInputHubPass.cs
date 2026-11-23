using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.0 BrowserExtension: route the two homepage scroll actions
/// through the existing action-specific G-REDruntime InputHub. Preserve the
/// engine listener as an exclusive fallback if either subscription fails.
/// Keep the source's original OnAction method and generic snapshots intact.
/// Both owner files are one atomic semantic operation, with no new framework.
/// </summary>
internal static class BrowserExtensionInputHubPass
{
    internal const string Rule = "BROWSER_EXTENSION_INPUT_HUB_WITH_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string[] SourcePaths, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Open, int Close, int NameEnd);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Reason, string? Changed);

    private static int Matching(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (start >= t.Count || t[start].Value != opening) return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? Class(string source, string name)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != name) continue;
            int open = i + 2;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int close = Matching(t, open, "{", "}");
            if (close < 0 || found is not null) return null;
            found = new(t[open].Start, t[close].Start, t[i + 1].End);
            i = close;
        }
        return found;
    }

    private static Block? Function(string source, string name, string? wrapper = null,
        Block? within = null)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "func" || t[i + 1].Value != name ||
                t[i + 2].Value != "(") continue;
            if (within is not null &&
                (t[i].Start <= within.Open || t[i].End >= within.Close))
                continue;
            if (wrapper is not null)
            {
                string preceding = source[Math.Max(0, t[i].Start - 245)..t[i].Start];
                if (!Regex.IsMatch(preceding,
                    @"@wrapMethod\s*\(\s*" + Regex.Escape(wrapper) +
                    @"\s*\)\s*(?:(?:protected|private|public|cb|final|static)\s+)*$"))
                    continue;
            }
            int args = Matching(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int close = Matching(t, open, "{", "}");
            if (close < 0 || found is not null) return null;
            if (within is not null && t[close].Start > within.Close) return null;
            found = new(t[open].Start, t[close].Start, t[i + 1].End);
            i = close;
        }
        return found;
    }

    private static string Rewrite(string src, IEnumerable<Edit> edits)
    {
        string updated = src;
        foreach (var e in edits.OrderByDescending(x => x.Start))
            updated = updated[..e.Start] + e.Text + updated[e.End..];
        return updated;
    }
    private static bool Has(string source, string part) =>
        source.Contains(part, StringComparison.Ordinal);
    private static string Eol(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static bool BothScrollActions(string source) =>
        Has(source, "UI_MoveDown") && Has(source, "UI_MoveUp") &&
        Has(source, "HomePageScrollDown(") && Has(source, "HomePageScrollUp(") &&
        Has(source, "IsHomePage(");

    private static readonly string[] Fields =
    [
        "m_customBrowserInputHub: wref<InputHub>",
        "m_customBrowserInputDownSubscription: Int32",
        "m_customBrowserInputUpSubscription: Int32",
        "m_customBrowserFallbackRegistered: Bool",
        "m_customBrowserFallbackPlayer: wref<GameObject>"
    ];

    private static string FallbackBody(string indent, string nl)
    {
        // Deliberately use the owner listener and live controlled player: no
        // duplicate listener, no fallback if both InputHub subscriptions work.
        string[] lines =
        [
            "if this.m_customBrowserInputDownSubscription <= 0 || this.m_customBrowserInputUpSubscription <= 0 {",
            "  if IsDefined(this.m_customBrowserInputHub) {",
            "    if this.m_customBrowserInputDownSubscription > 0 { this.m_customBrowserInputHub.Unsubscribe(this.m_customBrowserInputDownSubscription); }",
            "    if this.m_customBrowserInputUpSubscription > 0 { this.m_customBrowserInputHub.Unsubscribe(this.m_customBrowserInputUpSubscription); }",
            "  };",
            "  this.m_customBrowserInputDownSubscription = 0;",
            "  this.m_customBrowserInputUpSubscription = 0;",
            "  this.m_customBrowserInputHub = null;",
            "  let gcoBrowserFallbackPlayer = this.GetPlayerControlledObject();",
            "  if IsDefined(gcoBrowserFallbackPlayer) {",
            "    gcoBrowserFallbackPlayer.RegisterInputListener(this.m_customBrowserInputListener);",
            "    this.m_customBrowserFallbackPlayer = gcoBrowserFallbackPlayer;",
            "    this.m_customBrowserFallbackRegistered = true;",
            "  };",
            "};"
        ];
        return string.Join(nl, lines.Select(x => indent + x)) + nl;
    }

    private static string StockAttach(string indent, string nl)
    {
        string[] lines =
        [
            "this.m_customBrowserInputDownSubscription = 0;",
            "this.m_customBrowserInputUpSubscription = 0;",
            "this.m_customBrowserInputHub = null;",
            "this.m_customBrowserFallbackRegistered = false;",
            "this.m_customBrowserFallbackPlayer = null;",
            "let gcoBrowserPlayer = this.GetPlayerControlledObject();",
            "if IsDefined(gcoBrowserPlayer) {",
            "  let gcoBrowserRuntime = Runtime.Get(gcoBrowserPlayer.GetGame());",
            "  if IsDefined(gcoBrowserRuntime) && gcoBrowserRuntime.IsReady() {",
            "    this.m_customBrowserInputHub = gcoBrowserRuntime.GetInputHub();",
            "    if IsDefined(this.m_customBrowserInputHub) {",
            "      this.m_customBrowserInputDownSubscription = this.m_customBrowserInputHub.Subscribe(n\"UI_MoveDown\", this.m_customBrowserInputListener);",
            "      this.m_customBrowserInputUpSubscription = this.m_customBrowserInputHub.Subscribe(n\"UI_MoveUp\", this.m_customBrowserInputListener);",
            "    };",
            "  };",
            "};"
        ];
        return string.Join(nl, lines.Select(x => indent + x)) + nl +
            FallbackBody(indent, nl);
    }

    private static string FallbackCleanup(string indent, string nl)
    {
        string[] lines =
        [
            "if this.m_customBrowserFallbackRegistered && IsDefined(this.m_customBrowserFallbackPlayer) {",
            "  this.m_customBrowserFallbackPlayer.UnregisterInputListener(this.m_customBrowserInputListener);",
            "};",
            "this.m_customBrowserFallbackRegistered = false;",
            "this.m_customBrowserFallbackPlayer = null;"
        ];
        return string.Join(nl, lines.Select(x => indent + x)) + nl;
    }

    private static string StockCleanup(string indent, string nl)
    {
        string[] lines =
        [
            "if IsDefined(this.m_customBrowserInputHub) {",
            "  if this.m_customBrowserInputDownSubscription > 0 { this.m_customBrowserInputHub.Unsubscribe(this.m_customBrowserInputDownSubscription); };",
            "  if this.m_customBrowserInputUpSubscription > 0 { this.m_customBrowserInputHub.Unsubscribe(this.m_customBrowserInputUpSubscription); };",
            "};",
            "this.m_customBrowserInputDownSubscription = 0;",
            "this.m_customBrowserInputUpSubscription = 0;",
            "this.m_customBrowserInputHub = null;"
        ];
        return string.Join(nl, lines.Select(x => indent + x)) + nl +
            FallbackCleanup(indent, nl) + indent +
            "this.m_customBrowserInputListener = null;" + nl;
    }

    private static Decision Controller(string source)
    {
        var init = Function(source, "OnInitialize", "BrowserGameController");
        var done = Function(source, "OnUninitialize", "BrowserGameController");
        if (init is null || done is null ||
            !Regex.IsMatch(source,
                @"@addField\s*\(\s*BrowserGameController\s*\)\s*private\s+let\s+m_customBrowserInputListener\s*:\s*ref\s*<\s*CustomBrowserInputListener\s*>\s*;"))
            return new("REFUSED", "BEX_INPUT_CONTROLLER_LIFECYCLE_UNPROVEN", null);
        string initBody = source[(init.Open + 1)..init.Close];
        string doneBody = source[(done.Open + 1)..done.Close];
        if (!Has(initBody, "m_customBrowserInputListener.Init(this)") ||
            !Has(initBody, "wrappedMethod(") ||
            !Has(doneBody, "wrappedMethod(") ||
            !Has(initBody, "return ret;") || !Has(doneBody, "return ret;"))
            return new("REFUSED", "BEX_INPUT_WRAPPER_ORDER_UNPROVEN", null);
        string nl = Eol(source);
        var edits = new List<Edit>();

        foreach (string field in Fields)
        {
            string name = field.Split(':')[0];
            string type = field.Split(':')[1].Trim();
            var any = Regex.Matches(source, @"\blet\s+" + name + @"\s*:\s*([^;]+);");
            if (any.Count > 1 || (any.Count == 1 &&
                string.Join("", any[0].Groups[1].Value.Where(c => !char.IsWhiteSpace(c))) !=
                string.Join("", type.Where(c => !char.IsWhiteSpace(c)))))
                return new("REFUSED", "BEX_INPUT_FIELD_INCOMPATIBLE:" + name, null);
            if (any.Count == 0)
            {
                var ownerField = Regex.Match(source,
                    @"@addField\s*\(\s*BrowserGameController\s*\)\s*private\s+let\s+m_customBrowserInputListener\s*:\s*ref\s*<\s*CustomBrowserInputListener\s*>\s*;");
                if (!ownerField.Success)
                    return new("REFUSED", "BEX_INPUT_PRIMARY_FIELD_MISSING", null);
                edits.Add(new(ownerField.Index + ownerField.Length,
                    ownerField.Index + ownerField.Length, nl + nl +
                    "@addField(BrowserGameController)" + nl +
                    "private let " + field + ";"));
            }
        }
        if (!Regex.IsMatch(source, @"\bimport\s+GRedRuntime\.\*"))
        {
            var import = Regex.Match(source,
                @"\bimport\s+BrowserExtensionModSettings\.\*[^\r\n]*(?:\r?\n)?");
            if (!import.Success)
                return new("REFUSED", "BEX_INPUT_IMPORT_ANCHOR_MISSING", null);
            edits.Add(new(import.Index + import.Length,
                import.Index + import.Length, "import GRedRuntime.*" + nl));
        }

        var oldAttach = Regex.Matches(initBody,
            @"this\s*\.\s*GetPlayerControlledObject\s*\(\s*\)\s*\.\s*RegisterInputListener\s*\(\s*this\s*\.\s*m_customBrowserInputListener\s*\)\s*;");
        bool hasSubDown = Has(initBody, "Subscribe(n\"UI_MoveDown\"");
        bool hasSubUp = Has(initBody, "Subscribe(n\"UI_MoveUp\"");
        bool routed = hasSubDown && hasSubUp &&
            Has(initBody, "m_customBrowserInputDownSubscription") &&
            Has(initBody, "m_customBrowserInputUpSubscription") &&
            Has(initBody, "GetInputHub(") && Has(initBody, "IsReady(");
        bool fallback = Has(initBody, "m_customBrowserFallbackRegistered = true");
        if (oldAttach.Count == 1 && !routed && !fallback)
        {
            if (Has(initBody, "gcoBrowserPlayer") || Has(initBody, "gcoBrowserRuntime") ||
                Has(initBody, "gcoBrowserFallbackPlayer"))
                return new("REFUSED", "BEX_INPUT_LOCAL_NAME_COLLISION", null);
            var match = oldAttach[0];
            edits.Add(new(init.Open + 1 + match.Index,
                init.Open + 1 + match.Index + match.Length,
                StockAttach("    ", nl).TrimEnd('\r', '\n')));
        }
        else if (oldAttach.Count == 0 && routed && !fallback)
        {
            if (Has(initBody, "gcoBrowserFallbackPlayer"))
                return new("REFUSED", "BEX_INPUT_FALLBACK_LOCAL_COLLISION", null);
            var ret = Regex.Match(initBody, @"\breturn\s+ret\s*;");
            if (!ret.Success)
                return new("REFUSED", "BEX_INPUT_RETURN_ANCHOR_UNPROVEN", null);
            edits.Add(new(init.Open + 1 + ret.Index,
                init.Open + 1 + ret.Index, FallbackBody("    ", nl)));
        }
        else if (oldAttach.Count == 0 && routed && fallback)
        {
            if (!Has(initBody, "m_customBrowserFallbackPlayer =") ||
                !Has(initBody, "Unsubscribe(this.m_customBrowserInputDownSubscription)") ||
                !Has(initBody, "Unsubscribe(this.m_customBrowserInputUpSubscription)"))
                return new("REFUSED", "BEX_INPUT_PARTIAL_FALLBACK_UNPROVEN", null);
        }
        else return new("REFUSED", "BEX_INPUT_ATTACH_SHAPE_INCOMPATIBLE", null);

        var oldDetach = Regex.Matches(doneBody,
            @"this\s*\.\s*GetPlayerControlledObject\s*\(\s*\)\s*\.\s*UnregisterInputListener\s*\(\s*this\s*\.\s*m_customBrowserInputListener\s*\)\s*;");
        bool cleanedSubs = Has(doneBody, "m_customBrowserInputDownSubscription") &&
            Has(doneBody, "m_customBrowserInputUpSubscription") &&
            Has(doneBody, "Unsubscribe(");
        bool cleanedFallback = Has(doneBody, "m_customBrowserFallbackRegistered") &&
            Has(doneBody, "m_customBrowserFallbackPlayer.UnregisterInputListener(");
        if (oldDetach.Count == 1 && !cleanedSubs && !cleanedFallback)
        {
            var match = oldDetach[0];
            edits.Add(new(done.Open + 1 + match.Index,
                done.Open + 1 + match.Index + match.Length,
                StockCleanup("    ", nl).TrimEnd('\r', '\n')));
        }
        else if (oldDetach.Count == 0 && cleanedSubs && !cleanedFallback)
        {
            var anchor = Regex.Match(doneBody,
                @"this\s*\.\s*m_customBrowserInputListener\s*=\s*null\s*;");
            if (!anchor.Success)
                return new("REFUSED", "BEX_INPUT_HISTORICAL_UNINIT_UNPROVEN", null);
            edits.Add(new(done.Open + 1 + anchor.Index,
                done.Open + 1 + anchor.Index,
                FallbackCleanup("    ", nl)));
        }
        else if (oldDetach.Count == 0 && cleanedSubs && cleanedFallback)
        {
            if (!Has(doneBody, "m_customBrowserFallbackRegistered = false") ||
                !Has(doneBody, "m_customBrowserFallbackPlayer = null"))
                return new("REFUSED", "BEX_INPUT_PARTIAL_DETACH_UNPROVEN", null);
        }
        else return new("REFUSED", "BEX_INPUT_DETACH_SHAPE_INCOMPATIBLE", null);

        // Inserts sharing the same field anchor need a deterministic order;
        // all edits are local and leave unrelated source untouched.
        string updated = Rewrite(source, edits);
        return new(edits.Count == 0 ? "COMPLETE" : "APPLIED", null,
            edits.Count == 0 ? null : updated);
    }

    private static string FallbackOnAction(string nl)
    {
        string[] l =
        [
            "  protected cb func OnAction(action: ListenerAction, consumer: ListenerActionConsumer) -> Bool {",
            "    if !IsDefined(this.m_gameController) || !this.m_gameController.IsHomePage() { return false; }",
            "    let gcoBrowserActionName = ListenerAction.GetName(action);",
            "    if Equals(gcoBrowserActionName, n\"UI_MoveDown\") {",
            "      if ListenerAction.IsButtonJustPressed(action) { this.m_pageNumber = this.m_gameController.HomePageScrollDown(); };",
            "    } else {",
            "      if Equals(gcoBrowserActionName, n\"UI_MoveUp\") {",
            "        if ListenerAction.IsButtonJustPressed(action) { this.m_pageNumber = this.m_gameController.HomePageScrollUp(); };",
            "      };",
            "    };",
            "    return false;",
            "  }"
        ];
        return nl + string.Join(nl, l) + nl;
    }

    private static string InputHubCallback(string nl)
    {
        string[] l =
        [
            "  public func OnGRedInput(evt: ref<InputEvent>) -> Void {",
            "    if !IsDefined(this.m_gameController) || !IsDefined(evt) { return; };",
            "    if !Equals(evt.actionType, gameinputActionType.BUTTON_PRESSED) { return; };",
            "    if !this.m_gameController.IsHomePage() { return; };",
            "    if Equals(evt.actionName, n\"UI_MoveDown\") {",
            "      this.m_pageNumber = this.m_gameController.HomePageScrollDown();",
            "    } else {",
            "      if Equals(evt.actionName, n\"UI_MoveUp\") {",
            "        this.m_pageNumber = this.m_gameController.HomePageScrollUp();",
            "      };",
            "    };",
            "  }"
        ];
        return nl + string.Join(nl, l) + nl;
    }

    private static Decision Listener(string source)
    {
        var cls = Class(source, "CustomBrowserInputListener");
        if (cls is null) return new("REFUSED", "BEX_INPUT_LISTENER_CLASS_MISSING", null);
        string head = source[cls.NameEnd..cls.Open];
        if (Regex.IsMatch(head, @"\bextends\b") &&
            !Regex.IsMatch(head, @"\bextends\s+InputListener\b"))
            return new("REFUSED", "BEX_INPUT_LISTENER_OTHER_PARENT", null);
        var original = Function(source, "OnAction", within: cls);
        var hub = Function(source, "OnGRedInput", within: cls);
        if (original is null && hub is null)
            return new("REFUSED", "BEX_INPUT_NO_LISTENER_PATH", null);
        if (original is not null &&
            (!BothScrollActions(source[(original.Open + 1)..original.Close]) ||
             !Has(source[(original.Open + 1)..original.Close], "IsButtonJustPressed(")))
            return new("REFUSED", "BEX_INPUT_ORIGINAL_ONACTION_CHANGED", null);
        if (hub is not null)
        {
            string body = source[(hub.Open + 1)..hub.Close];
            if (!BothScrollActions(body) || !Has(body, "evt.actionName") ||
                !Has(body, "BUTTON_PRESSED") || !Has(body, "evt.actionType"))
                return new("REFUSED", "BEX_INPUT_HUB_CALLBACK_CHANGED", null);
        }
        var edits = new List<Edit>();
        string nl = Eol(source);
        if (!Regex.IsMatch(head, @"\bextends\s+InputListener\b"))
            edits.Add(new(cls.NameEnd, cls.NameEnd, " extends InputListener"));
        if (!Regex.IsMatch(source, @"\bimport\s+GRedRuntime\.\*"))
        {
            var module = Regex.Match(source,
                @"\bmodule\s+HomePagePagination[^\r\n]*(?:\r?\n)");
            if (!module.Success)
                return new("REFUSED", "BEX_INPUT_LISTENER_IMPORT_ANCHOR_MISSING", null);
            edits.Add(new(module.Index + module.Length,
                module.Index + module.Length, "import GRedRuntime.*" + nl));
        }
        if (original is null)
        {
            if (Has(source, "gcoBrowserActionName"))
                return new("REFUSED", "BEX_INPUT_FALLBACK_ACTION_LOCAL_COLLISION", null);
            edits.Add(new(cls.Close, cls.Close, FallbackOnAction(nl)));
        }
        if (hub is null)
            edits.Add(new(cls.Close, cls.Close, InputHubCallback(nl)));
        // Two insertions at one class close are composed explicitly rather
        // than relying on unspecified sort ordering of equal edit positions.
        var tailEdits = edits.Where(e => e.Start == cls.Close && e.End == cls.Close).ToArray();
        if (tailEdits.Length > 1)
        {
            edits.RemoveAll(e => e.Start == cls.Close && e.End == cls.Close);
            edits.Add(new(cls.Close, cls.Close,
                string.Concat(tailEdits.Select(e => e.Text))));
        }
        string updated = Rewrite(source, edits);
        return new(edits.Count == 0 ? "COMPLETE" : "APPLIED", null,
            edits.Count == 0 ? null : updated);
    }

    private static bool SupportsRuntime(string scripts)
    {
        string[] needed =
        [
            "class InputHub", "class InputListener", "func Subscribe(",
            "func Unsubscribe(", "func OnGRedInput(", "class Runtime",
            "func IsReady(", "func GetInputHub(", "static func Get("
        ];
        var found = new bool[needed.Length];
        foreach (string f in ProvidedRuntimeSourceScope.Files(scripts))
        {
            string code = File.ReadAllText(f, Encoding.UTF8);
            // Framework ownership is determined by the declared module/API,
            // never a historical folder, file split or mod version.
            if (!Regex.IsMatch(code, @"\bmodule\s+GRedRuntime\b"))
                continue;
            for (int i=0;i<needed.Length;i++)
                found[i] |= code.Contains(needed[i],StringComparison.Ordinal);
        }
        return found.All(x=>x);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game,"r6","scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("BEX_INPUT_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) || Inside(basePreviewRoot,game) ||
             Inside(output,basePreviewRoot) || Inside(basePreviewRoot,output)))
            throw new InvalidDataException("BEX_INPUT_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("BEX_INPUT_SOURCE_HASH_MISSING");
        string status = "NOT_APPLICABLE";
        string? reason = null;
        var changes = new Dictionary<string,string>(StringComparer.Ordinal);
        var owner = Path.Combine(scripts,"BrowserExtension");
        if (Directory.Exists(owner))
        {
            string[] files = Directory.EnumerateFiles(owner,"*.reds",
                SearchOption.AllDirectories).ToArray();
            var controllers = files.Where(f=>
                File.ReadAllText(f,Encoding.UTF8).Contains(
                    "m_customBrowserInputListener",StringComparison.Ordinal) &&
                File.ReadAllText(f,Encoding.UTF8).Contains(
                    "func OnInitialize",StringComparison.Ordinal)).ToArray();
            var listeners = files.Where(f=>
                File.ReadAllText(f,Encoding.UTF8).Contains(
                    "class CustomBrowserInputListener",StringComparison.Ordinal)).ToArray();
            if (controllers.Length != 1 || listeners.Length != 1 ||
                controllers[0] == listeners[0])
            {
                status = "REFUSED";
                reason = "BEX_INPUT_OWNER_SOURCE_AMBIGUOUS";
            }
            else if (!SupportsRuntime(scripts))
            {
                status = "REFUSED";
                reason = "BEX_INPUT_RUNTIME_ABI_MISSING";
            }
            else
            {
                var c = controllers[0];
                var l = listeners[0];
                if ((File.GetAttributes(c) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(l) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("BEX_INPUT_SOURCE_LINK_UNSAFE");
                string cr = "r6/scripts/" + Path.GetRelativePath(scripts,c)
                    .Replace('\\','/');
                string lr = "r6/scripts/" + Path.GetRelativePath(scripts,l)
                    .Replace('\\','/');
                string cs = File.ReadAllText(c,Encoding.UTF8);
                string ls = File.ReadAllText(l,Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string co = Path.GetFullPath(Path.Combine(basePreviewRoot,
                        cr.Replace('/',Path.DirectorySeparatorChar)));
                    string lo = Path.GetFullPath(Path.Combine(basePreviewRoot,
                        lr.Replace('/',Path.DirectorySeparatorChar)));
                    if (!Inside(co,basePreviewRoot) || !Inside(lo,basePreviewRoot))
                        throw new InvalidDataException("BEX_INPUT_PREVIEW_PATH_ESCAPE");
                    if (File.Exists(co)) cs = File.ReadAllText(co,Encoding.UTF8);
                    if (File.Exists(lo)) ls = File.ReadAllText(lo,Encoding.UTF8);
                }
                var cResult = Controller(cs);
                var lResult = Listener(ls);
                if (cResult.Status == "REFUSED" || lResult.Status == "REFUSED")
                {
                    status = "REFUSED";
                    reason = cResult.Reason ?? lResult.Reason;
                }
                else
                {
                    if (cResult.Changed is not null) changes.Add(cr,cResult.Changed);
                    if (lResult.Changed is not null) changes.Add(lr,lResult.Changed);
                    status = changes.Count > 0 ? "APPLIED" : "COMPLETE";
                }
            }
        }
        if (!hash.Equals(admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("BEX_INPUT_SOURCE_CHANGED_DURING_GENERATION");
        Directory.CreateDirectory(output);
        foreach (var (relative,text) in changes)
        {
            string outputFile = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if (!Inside(outputFile,output) ||
                !outputFile.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("BEX_INPUT_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile)!);
            File.WriteAllText(outputFile,text,new UTF8Encoding(false));
        }
        var result = new Result(Rule,status,changes.Count,
            status == "COMPLETE" ? 1 : 0,
            changes.Keys.Order(StringComparer.Ordinal).ToArray(),reason,true,false);
        File.WriteAllText(Path.Combine(output,"phase2-result.json"),
            JsonSerializer.Serialize(result,new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,
                WriteIndented=true
            }),new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string child,string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
