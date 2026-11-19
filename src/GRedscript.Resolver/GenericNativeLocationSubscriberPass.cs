using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Experimental native-location subscription consumer generation.
/// Recognizes an opt-in source SHAPE, not a fixed mod list:
/// ScriptableSystem Start/Stop + player world-position proximity callback
/// looping over a same-module literal sphere factory. Generates an additive
/// REDscript listener in the source mod directory, with lifecycle injected
/// into the staged original .reds copy. Original gameplay logic remains intact.
/// </summary>
internal static class GenericNativeLocationSubscriberPass
{
    internal const string Rule = "NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;
    private const string PredicateRule = "NATIVE_PLAYER_LOCATION_PREDICATE_V1";
    private const string PredicateMarker = "/* GCO-GENERIC:" + PredicateRule + " */";
    private static readonly Regex Module = new(
        @"(?m)^[ \t]*module[ \t]+(?<module>[A-Za-z_][\w.]*)[ \t]*\r?$",
        RegexOptions.CultureInvariant);
    private static readonly Regex Factory = new(
        @"\bpublic\s+func\s+(?<name>[A-Za-z_]\w*)\s*\(\s*\)\s*->\s*array\s*<",
        RegexOptions.CultureInvariant);
    private static readonly Regex SystemClass = new(
        @"\bpublic\s+class\s+(?<name>[A-Za-z_]\w*)\s+extends\s+ScriptableSystem\s*\{",
        RegexOptions.CultureInvariant);
    private static readonly Regex ForFactory = new(
        @"\bfor\s+(?<item>[A-Za-z_]\w*)\s+in\s+(?<factory>[A-Za-z_]\w*)\s*\(\s*\)",
        RegexOptions.CultureInvariant);
    private static readonly Regex PlayerPosition = new(
        @"\b(?:this\.)?(?<player>[A-Za-z_]\w*)\.GetWorldPosition\s*\(\s*\)",
        RegexOptions.CultureInvariant);

    internal sealed record Candidate(string SourcePath, string SitesPath,
        string ModuleName, string ClassName, string FactoryName,
        int Owner, int SiteCount, string AdapterPath);

    private static string SourceRoot(string gameRoot) =>
        Path.Combine(Path.GetFullPath(gameRoot), "r6", "scripts");

    private static string ExtractBody(string source, Match match)
    {
        int open = source.IndexOf('{', match.Index + match.Length - 1);
        if (open < 0) return "";
        // Use the existing source lexer so comments/string literals cannot
        // create false brace balances or fake callbacks.
        var toks = SourceAdmission.ReadTokens(source);
        int depth = 0;
        foreach (var t in toks)
        {
            if (t.Start < open) continue;
            if (t.Value == "{") depth++;
            if (t.Value == "}" && --depth == 0)
                return source.Substring(open, t.End - open);
        }
        return "";
    }

    internal static IReadOnlyList<Candidate> Discover(string gameRoot)
    {
        string root = SourceRoot(gameRoot);
        if (!Directory.Exists(root))
            throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_ROOT_MISSING");
        var files = Directory.EnumerateFiles(root, "*.reds", SearchOption.AllDirectories)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray();
        var spheres = RedscriptNativeLocations.Inventory(gameRoot)
            .Where(x => x.SphereCount > 0 && x.QuadCount == 0).ToArray();
        var factories = new List<(string Module, string Name,
            RedscriptNativeLocations.SourceSites Sites)>();
        foreach (var site in spheres)
        {
            string source = File.ReadAllText(Path.Combine(root,
                site.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                Encoding.UTF8);
            var module = Module.Match(source);
            if (!module.Success) continue;
            var names = Factory.Matches(source).Cast<Match>().ToArray();
            if (names.Length != 1) continue;
            factories.Add((module.Groups["module"].Value,
                names[0].Groups["name"].Value, site));
        }

        var candidates = new List<Candidate>();
        foreach (string file in files)
        {
            string source = File.ReadAllText(file, Encoding.UTF8);
            var module = Module.Match(source);
            if (!module.Success) continue;
            var classes = SystemClass.Matches(source).Cast<Match>().ToArray();
            foreach (var klass in classes)
            {
                string body = ExtractBody(source, klass);
                if (body.Length == 0 ||
                    !Regex.IsMatch(body, @"\bpublic\s+func\s+Start\s*\(\s*\)",
                        RegexOptions.CultureInvariant) ||
                    !Regex.IsMatch(body, @"\bpublic\s+func\s+Stop\s*\(\s*\)",
                        RegexOptions.CultureInvariant) ||
                    !body.Contains("Vector4.Distance(", StringComparison.Ordinal))
                    continue;
                // Only the actual player source is valid for the CET native
                // coordinator. NPC/puppet position queries must not qualify.
                bool playerPositionProven = PlayerPosition.Matches(body)
                    .Cast<Match>().Any(position =>
                    {
                        string id = position.Groups["player"].Value;
                        return Regex.IsMatch(body,
                            @"\bthis\." + Regex.Escape(id) +
                            @"\s*=\s*GetPlayer\s*\(",
                            RegexOptions.CultureInvariant) ||
                            Regex.IsMatch(body,
                                @"\blet\s+" + Regex.Escape(id) +
                                @"\s*(?::[^=;]+)?=\s*GetPlayer\s*\(",
                                RegexOptions.CultureInvariant);
                    });
                if (!playerPositionProven) continue;

                var loops = ForFactory.Matches(body).Cast<Match>().ToArray();
                foreach (var loop in loops)
                {
                    string factoryName = loop.Groups["factory"].Value;
                    string item = loop.Groups["item"].Value;
                    // Generic field names: location and radius are discovered
                    // from the typed, positive Vector4.Distance source shape.
                    // Fully generated sources have the original exact
                    // expression after a native prefix. Recognize the marker
                    // for second-pass idempotence without re-injecting it.
                    if (!HasPositiveDistancePredicate(body, item) &&
                        !(source.Contains(PredicateMarker, StringComparison.Ordinal) &&
                          body.Contains("GCOMayBeNear_", StringComparison.Ordinal)))
                        continue;
                    var matches = factories.Where(f =>
                        f.Module == module.Groups["module"].Value &&
                        f.Name == factoryName).ToArray();
                    if (matches.Length != 1) continue;
                    var f = matches[0];
                    int count = f.Sites.SphereCount;
                    if (count <= 0 || count > 256) continue;
                    string rel = Path.GetRelativePath(root, file)
                        .Replace('\\', '/');
                    string adapter = Path.GetDirectoryName(rel)!
                        .Replace('\\', '/') + "/GCO_NativeLocation_" +
                        f.Sites.Owner + ".reds";
                    var candidate = new Candidate(rel, f.Sites.RelativePath,
                        f.Module, klass.Groups["name"].Value,
                        factoryName, f.Sites.Owner, count, adapter);
                    string siteSource = File.ReadAllText(Path.Combine(root,
                        f.Sites.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                        Encoding.UTF8);
                    // Scan and generation share the EXACT source pattern
                    // admission. No observation-only adapter can be emitted.
                    if (!source.Contains(PredicateMarker, StringComparison.Ordinal) &&
                        RewriteSource(source, siteSource, candidate,
                            out var reason) is null)
                    {
                        // No private author paths, coordinates or source
                        // excerpts in CI: one structural refusal category.
                        Console.WriteLine("NATIVE_PREDICATE_REFUSED:" + reason);
                        continue;
                    }
                    candidates.Add(candidate);
                }
            }
        }
        // Same owner/factory should never generate multiple wrappers.
        return candidates.GroupBy(x => x.AdapterPath,
                StringComparer.OrdinalIgnoreCase)
            .Where(x => x.Count() == 1).Select(x => x.Single())
            .OrderBy(x => x.AdapterPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string CodeOnly(string source)
    {
        var chars = source.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] != '\n' && chars[i] != '\r') chars[i] = ' ';
        foreach (var token in SourceAdmission.ReadTokens(source))
        {
            if (token.Value == "<literal>") continue;
            for (int i = token.Start; i < token.End; i++)
                chars[i] = source[i];
        }
        return new string(chars);
    }

    private static Regex Predicate(string item) => new(
        @"\bif\s+(?<expr>Vector4\s*\.\s*Distance\s*\(\s*(?<pos>[A-Za-z_]\w*)\s*,\s*" +
        Regex.Escape(item) + @"\s*\.\s*(?<location>[A-Za-z_]\w*)\s*\)\s*<=\s*" +
        Regex.Escape(item) + @"\s*\.\s*(?<radius>[A-Za-z_]\w*))\s*\{",
        RegexOptions.CultureInvariant);

    private static bool HasPositiveDistancePredicate(string body, string item) =>
        Predicate(item).Matches(CodeOnly(body)).Count == 1;

    // Demand a literal prefix in the returned array. Conditional ArrayPush
    // records are allowed only AFTER the static prefix and therefore use
    // indexes >= native SiteCount, which are handled by the original check.
    private static bool HasStableStaticPrefix(string source, string factory,
        int expected, string location, string radius)
    {
        string masked = CodeOnly(source);
        var named = Regex.Matches(masked,
            @"\bpublic\s+func\s+" + Regex.Escape(factory) +
            @"\s*\(\s*\)\s*->\s*array\s*<[^>]+>\s*\{",
            RegexOptions.CultureInvariant);
        if (named.Count != 1) return false;
        var body = ExtractBody(source, named[0]);
        if (body.Length == 0) return false;
        string code = CodeOnly(body);
        var arr = Regex.Matches(code,
            @"\blet\s+(?<array>[A-Za-z_]\w*)\s*=\s*\[",
            RegexOptions.CultureInvariant);
        if (arr.Count != 1) return false;
        string list = arr[0].Groups["array"].Value;
        int close = code.IndexOf("];", arr[0].Index + arr[0].Length,
            StringComparison.Ordinal);
        if (close <= arr[0].Index) return false;
        // Count literal Vector4 sphere records in the initialization slice.
        // No native mapping if a factory injects, shuffles or reorders sites.
        string literal = code.Substring(arr[0].Index,
            close + 2 - arr[0].Index);
        if (Regex.Matches(literal,
            @"\bVector4\s*\(", RegexOptions.CultureInvariant).Count != expected ||
            Regex.Matches(code, @"\bVector4\s*\(",
                RegexOptions.CultureInvariant).Count != expected ||
            !Regex.IsMatch(code, @"\breturn\s+" +
                Regex.Escape(list) + @"\s*;", RegexOptions.CultureInvariant))
            return false;
        string tail = code[(close + 2)..];
        if (Regex.IsMatch(tail,
            @"\b(?:ArrayInsert|ArrayErase|ArrayClear|ArraySort|ArrayPop|ArrayRemove|Sort)\s*\(\s*" +
            Regex.Escape(list) + @"\b", RegexOptions.CultureInvariant) ||
            Regex.IsMatch(tail,
            @"\b" + Regex.Escape(list) + @"\s*(?:=|\[)",
            RegexOptions.CultureInvariant))
            return false;
        // Validate the factory's return element declares the exact typed
        // accessors used by the predicate. Structural, not author-specific.
        string type = Regex.Match(source,
            @"\bpublic\s+func\s+" + Regex.Escape(factory) +
            @"\s*\(\s*\)\s*->\s*array\s*<(?<type>[A-Za-z_]\w*)>",
            RegexOptions.CultureInvariant).Groups["type"].Value;
        var declaration = Regex.Match(source,
            @"\b(?:public\s+)?struct\s+" + Regex.Escape(type) +
            @"\s*\{(?<body>[\s\S]*?)\}",
            RegexOptions.CultureInvariant);
        if (!declaration.Success) return false;
        string fields = CodeOnly(declaration.Groups["body"].Value);
        return Regex.IsMatch(fields, @"\blet\s+" +
                   Regex.Escape(location) + @"\s*:\s*Vector4\s*;",
                   RegexOptions.CultureInvariant) &&
               Regex.IsMatch(fields, @"\blet\s+" +
                   Regex.Escape(radius) + @"\s*:\s*Float\s*;",
                   RegexOptions.CultureInvariant);
    }

    // The overlay rewrites the ORIGINAL source path, just like
    // SHARED_PLAYER_SIGNAL_V1. The separately generated adapter only owns
    // the listener lifecycle and cheap per-site shared-state lookup.
    // Rewrite the original author class in the staged copy. Script-defined
    // classes do not require cross-file @wrapMethod or @addField support.
    // The per-mod companion contains only its native event listener.
    private static string? RewriteSource(string source, string sitesSource,
        Candidate candidate, out string refusal)
    {
        refusal = "NONE";
        string index = "__gcoSiteIndex_" + candidate.Owner;
        string method = "GCOMayBeNear_" + candidate.Owner;
        string attach = "GCOAttachNativeLocations_" + candidate.Owner;
        string detach = "GCODetachNativeLocations_" + candidate.Owner;
        string fields = "m_gcoNativeLocations_" + candidate.Owner;
        string listener = "m_gcoNativeListener_" + candidate.Owner;
        string listenerType = "GCO_NativeSiteObserver_" + candidate.Owner;
        if (source.Contains(PredicateMarker, StringComparison.Ordinal))
        {
            refusal = "ALREADY_REWRITTEN";
            return null;
        }
        if (new[] { index, method, attach, detach, fields, listener }
            .Any(x => source.Contains(x, StringComparison.Ordinal)))
            throw new InvalidDataException("NATIVE_PREDICATE_SYMBOL_COLLISION");

        string code = CodeOnly(source);
        var owners = SystemClass.Matches(code).Cast<Match>()
            .Where(m => m.Groups["name"].Value == candidate.ClassName).ToArray();
        if (owners.Length != 1)
        {
            refusal = "CLASS_MAPPING";
            return null;
        }
        int classOpen = owners[0].Index + owners[0].Length - 1;
        int classEnd = FindBlockEnd(code, classOpen);
        if (classEnd <= classOpen)
        {
            refusal = "CLASS_BODY";
            return null;
        }
        var loops = Regex.Matches(code,
            @"\bfor\s+(?<item>[A-Za-z_]\w*)\s+in\s+" +
            Regex.Escape(candidate.FactoryName) + @"\s*\(\s*\)\s*\{",
            RegexOptions.CultureInvariant).Cast<Match>()
            .Where(x => x.Index > classOpen && x.Index < classEnd).ToArray();
        if (loops.Length != 1)
        {
            refusal = "LOOP_MAPPING";
            return null;
        }
        var loop = loops[0];
        string itemName = loop.Groups["item"].Value;
        int end = FindBlockEnd(code, loop.Index + loop.Length - 1);
        if (end <= loop.Index || end > classEnd)
        {
            refusal = "LOOP_BODY";
            return null;
        }
        string body = code.Substring(loop.Index, end - loop.Index);
        var matches = Predicate(itemName).Matches(body).Cast<Match>().ToArray();
        if (matches.Length != 1)
        {
            refusal = "POSITIVE_PREDICATE";
            return null;
        }
        var predicate = matches[0];
        string posName = predicate.Groups["pos"].Value;
        string location = predicate.Groups["location"].Value;
        string radius = predicate.Groups["radius"].Value;
        if (!Regex.IsMatch(code[..loop.Index],
                @"\blet\s+" + Regex.Escape(posName) +
                @"\s*(?::\s*Vector4)?\s*=\s*this\.(?<receiver>[A-Za-z_]\w*)\s*\.\s*GetWorldPosition\s*\(\s*\)",
                RegexOptions.CultureInvariant))
        {
            refusal = "PLAYER_RECEIVER";
            return null;
        }
        if (!HasStableStaticPrefix(sitesSource, candidate.FactoryName,
                candidate.SiteCount, location, radius))
        {
            refusal = "STATIC_PREFIX";
            return null;
        }

        // Exact lifecycle bodies in the original source, not native
        // @wrapMethod hooks on author-defined ScriptableSystem classes.
        string classCode = code.Substring(classOpen, classEnd - classOpen);
        int[] FindLifecycle(string lifecycle)
        {
            var matches = Regex.Matches(classCode,
                @"\bpublic\s+func\s+" + Regex.Escape(lifecycle) +
                @"\s*\(\s*\)\s*(?:->\s*Void\s*)?\{",
                RegexOptions.CultureInvariant).Cast<Match>().ToArray();
            if (matches.Length != 1) return [];
            int opening = classOpen + matches[0].Index + matches[0].Length - 1;
            int closing = FindBlockEnd(code, opening) - 1;
            return closing > opening && closing < classEnd ?
                [opening, closing] : [];
        }
        int[] startBounds = FindLifecycle("Start");
        int[] stopBounds = FindLifecycle("Stop");
        if (startBounds.Length != 2 || stopBounds.Length != 2)
        {
            refusal = "SOURCE_LIFECYCLE";
            return null;
        }
        int predicateStart = loop.Index + predicate.Groups["expr"].Index;
        int lineStart = source.LastIndexOf('\n', loop.Index);
        lineStart = lineStart < 0 ? 0 : lineStart + 1;
        string indent = source[lineStart..loop.Index];
        if (indent.Any(c => c != ' ' && c != '\t'))
        {
            refusal = "LOOP_INDENTATION";
            return null;
        }
        string nl = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";
        string additions = $$"""

            
              // GCO-GENERIC:NATIVE_LOCATION_DIRECT_LIFECYCLE_V1
              private let {{fields}}: array<Int32>;
              private let {{listener}}: ref<{{listenerType}}>;

              private func {{method}}(siteIndex: Int32) -> Bool {
                if !IsDefined(this.{{listener}}) { return true; }
                return this.{{listener}}.MayBeNear(siteIndex);
              }

              private func {{attach}}() -> Void {
                if ArraySize(this.{{fields}}) > 0 { return; }
                let runtime = Runtime.Get(GetGameInstance());
                if !IsDefined(runtime) || !runtime.IsReady() { return; }
                let service = runtime.GetNativeLocations();
                if !IsDefined(service) { return; }
                let native = new GCETNativeLocationBackend();
                this.{{listener}} = new {{listenerType}}();
                this.{{listener}}.Initialize({{candidate.SiteCount}});
                let i: Int32 = 0;
                while i < {{candidate.SiteCount}} {
                  let id = service.SubscribeLocation({{candidate.Owner}}, i,
                    {{candidate.SiteCount}}, this.{{listener}}, native);
                  if id <= 0 {
                    this.{{detach}}();
                    return;
                  }
                  ArrayPush(this.{{fields}}, id);
                  i += 1;
                }
              }

              private func {{detach}}() -> Void {
                let runtime = Runtime.Get(GetGameInstance());
                if IsDefined(runtime) && runtime.IsReady() {
                  let service = runtime.GetNativeLocations();
                  if IsDefined(service) {
                    for id in this.{{fields}} {
                      service.UnsubscribeLocation(id);
                    }
                  }
                }
                ArrayClear(this.{{fields}});
                this.{{listener}} = null;
              }

            """.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", nl, StringComparison.Ordinal);

        // Insert all source edits in descending position order. Preserve
        // existing Start/Stop statement order, callback cadence and returns.
        var edits = new List<(int At, string Value)>
        {
            (predicateStart, PredicateMarker + " this." + method + "(" + index + ") && "),
            (loop.Index + loop.Length, nl + indent + "    " + index + " += 1;"),
            (loop.Index, "let " + index + ": Int32 = -1;" + nl + indent),
            (startBounds[1], nl + "    this." + attach + "();" + nl),
            (stopBounds[0] + 1, nl + "    this." + detach + "();" + nl),
            (classOpen + 1, additions)
        };
        string result = source;
        foreach (var edit in edits.OrderByDescending(x => x.At))
            result = result.Insert(edit.At, edit.Value);

        if (!Regex.IsMatch(result,
                @"(?m)^[ \t]*import[ \t]+GRedRuntime\.\*[ \t]*\r?$",
                RegexOptions.CultureInvariant))
        {
            var mod = Module.Match(result);
            if (!mod.Success)
                throw new InvalidDataException("NATIVE_PREDICATE_MODULE_MISSING");
            int afterModule = result.IndexOf('\n', mod.Index);
            if (afterModule < 0)
                throw new InvalidDataException("NATIVE_PREDICATE_MODULE_LINE_MISSING");
            result = result.Insert(afterModule + 1, "import GRedRuntime.*" + nl);
        }

        if (Regex.Matches(result, Regex.Escape(PredicateMarker)).Count != 1 ||
            !result.Contains("GCO-GENERIC:NATIVE_LOCATION_DIRECT_LIFECYCLE_V1",
                StringComparison.Ordinal))
            throw new InvalidDataException("NATIVE_PREDICATE_POSTCONDITION");
        return result;
    }

    private static int FindBlockEnd(string masked, int opening)
    {
        int depth = 0;
        foreach (var tok in SourceAdmission.ReadTokens(masked))
        {
            if (tok.Start < opening) continue;
            if (tok.Value == "{") depth++;
            if (tok.Value == "}" && --depth == 0) return tok.End;
        }
        return -1;
    }

    private static bool Inside(string child, string root)
    {
        var p = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var c = Path.GetFullPath(child);
        return c.StartsWith(p + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    // Additional generic location-dependency lane INSIDE the existing native
    // subscriber generator. Ordinary static Bool helpers can now subscribe via
    // G-REDruntime's lazy location-group service; no ScriptableSystem lifecycle,
    // per-mod timer, new native plugin or second generator is introduced.
    private const string LocationGroupRule = "LOCATION_DEPENDENCY_FAR_V1";
    private static string LocationGroupMarker(int owner) =>
        "/* GCO-GENERIC:" + LocationGroupRule + ":" + owner + " */";

    private static bool HasValidGroupGuard(string source,
        RedscriptNativeLocations.LocationDependencyGroup group)
    {
        string code = CodeOnly(source);
        return Regex.IsMatch(code,
            @"\bif\s+GCETNativeLocationGuard\.DefinitelyFarPlayer\s*" +
            @"\(\s*" + Regex.Escape(group.PlayerName) +
            @"\s*,\s*" + group.Owner + @"\s*,\s*" + group.Count +
            @"\s*\)\s*\{\s*return\s+false\s*;\s*\}",
            RegexOptions.CultureInvariant);
    }

    private static int StageLocationDependencies(string gameRoot,
        string? baseScripts, string output,
        List<(string Path, string Source)> generated)
    {
        int changed = 0;
        var discovered = RedscriptNativeLocations.LocationDependencyGroups(gameRoot);
        foreach (var owner in discovered.GroupBy(x => x.RelativePath,
                     StringComparer.OrdinalIgnoreCase))
        {
            string relative = "r6/scripts/" + owner.Key;
            string originalFile = Path.Combine(Path.GetFullPath(gameRoot),
                relative.Replace('/', Path.DirectorySeparatorChar));
            string destination = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(destination, output))
                throw new InvalidDataException("NATIVE_GROUP_OUTPUT_PATH_ESCAPE");

            int existing = generated.FindIndex(x => x.Path.Equals(
                destination, StringComparison.OrdinalIgnoreCase));
            string effective = existing >= 0 ? generated[existing].Source :
                File.ReadAllText(originalFile, Encoding.UTF8);
            if (existing < 0 && baseScripts is not null)
            {
                string prior = Path.GetFullPath(Path.Combine(baseScripts,
                    owner.Key.Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(prior, baseScripts))
                    throw new InvalidDataException("NATIVE_GROUP_BASE_PATH_ESCAPE");
                if (File.Exists(prior)) effective = File.ReadAllText(prior,
                    Encoding.UTF8);
            }
            var current = RedscriptNativeLocations.LocationDependencyGroups(
                owner.Key, effective);
            var edits = new List<(int Position, string Text)>();
            string nl = effective.Contains("\r\n", StringComparison.Ordinal)
                ? "\r\n" : "\n";
            foreach (var original in owner)
            {
                var matches = current.Where(x => x.Owner == original.Owner &&
                    x.Method == original.Method &&
                    x.Count == original.Count).ToArray();
                if (matches.Length != 1)
                    throw new InvalidDataException(
                        "NATIVE_GROUP_EFFECTIVE_SOURCE_DRIFT: " + original.Method);
                var candidate = matches[0];
                string marker = LocationGroupMarker(candidate.Owner);
                if (effective.Contains(marker, StringComparison.Ordinal))
                {
                    if (!HasValidGroupGuard(effective, candidate))
                        throw new InvalidDataException(
                            "NATIVE_GROUP_GENERATED_GUARD_DRIFT");
                    continue;
                }
                string guard = nl + "    " + marker +
                    " if GCETNativeLocationGuard.DefinitelyFarPlayer(" +
                    candidate.PlayerName + ", " + candidate.Owner + ", " +
                    candidate.Count + ") { return false; }" + nl;
                edits.Add((candidate.OpenBrace + 1, guard));
                changed++;
            }
            if (edits.Count == 0) continue;
            foreach (var edit in edits.OrderByDescending(e => e.Position))
                effective = effective.Insert(edit.Position, edit.Text);
            if (!Regex.IsMatch(effective,
                @"(?m)^[ \t]*import[ \t]+GRedRuntime\.\*[ \t]*\r?$",
                RegexOptions.CultureInvariant))
            {
                var module = Module.Match(effective);
                if (!module.Success)
                    throw new InvalidDataException("NATIVE_GROUP_MODULE_MISSING");
                int end = effective.IndexOf('\n', module.Index);
                if (end < 0)
                    throw new InvalidDataException("NATIVE_GROUP_MODULE_LINE");
                effective = effective.Insert(end + 1,
                    "import GRedRuntime.*" + nl);
            }
            if (existing >= 0)
                generated[existing] = (destination, effective);
            else
                generated.Add((destination, effective));
        }
        return changed;
    }

    internal static int Scan(string gameRoot, string outFile)
    {
        var admission = new SourceAdmission(gameRoot);
        _ = admission.AuditAll();
        var candidates = Discover(gameRoot);
        if (Inside(outFile, gameRoot) || File.Exists(outFile))
            throw new InvalidDataException("NATIVE_CONSUMER_SCAN_OUTPUT_COLLISION");
        var remaining = new List<Candidate>();
        int complete = 0;
        var groups = RedscriptNativeLocations.LocationDependencyGroups(gameRoot);
        int groupEligible = 0;
        int groupComplete = 0;
        foreach (var group in groups)
        {
            string path = Path.Combine(SourceRoot(gameRoot),
                group.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            string text = File.ReadAllText(path, Encoding.UTF8);
            if (text.Contains(LocationGroupMarker(group.Owner),
                    StringComparison.Ordinal))
            {
                if (!HasValidGroupGuard(text, group))
                    throw new InvalidDataException("NATIVE_GROUP_GENERATED_GUARD_DRIFT");
                groupComplete++;
            }
            else
                groupEligible++;
        }
        foreach (var c in candidates)
        {
            string present = Path.Combine(SourceRoot(gameRoot),
                c.AdapterPath.Replace('/', Path.DirectorySeparatorChar));
            string owned = Path.Combine(SourceRoot(gameRoot),
                c.SourcePath.Replace('/', Path.DirectorySeparatorChar));
            string original = File.ReadAllText(owned, Encoding.UTF8);
            bool predicateComplete = original.Contains(PredicateMarker,
                StringComparison.Ordinal);
            bool adapterComplete = File.Exists(present) &&
                string.Equals(File.ReadAllText(present, Encoding.UTF8),
                    Generate(c), StringComparison.Ordinal);
            if (File.Exists(present) && !adapterComplete)
            {
                string previous = File.ReadAllText(present, Encoding.UTF8);
                // An existing observer-only V1 is an admitted upgrade. A
                // changed V2 implementation is source drift, not partial.
                if (!previous.Contains(Marker, StringComparison.Ordinal) ||
                    previous.Contains("GCOMayBeNear_", StringComparison.Ordinal) ||
                    previous.Contains("MayBeNear(index:", StringComparison.Ordinal))
                    throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_COLLISION");
            }
            if (predicateComplete && adapterComplete) complete++;
            else remaining.Add(c); // untouched or partial generated stage
        }
        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_DRIFT");
        Directory.CreateDirectory(Path.GetDirectoryName(
            Path.GetFullPath(outFile))!);
        File.WriteAllText(outFile, JsonSerializer.Serialize(new {
            schemaVersion = "gco.redscript.native-location-subscriber.v1",
            rule = Rule, sourceStackSha256 = admission.SourceStackSha256,
            eligible = remaining.Count + groupEligible,
            alreadyComplete = complete + groupComplete,
            locationDependencyRule = LocationGroupRule,
            locationGroupEligible = groupEligible,
            locationGroupAlreadyComplete = groupComplete,
            mode = "NATIVE_FAR_PREDICATE_OR_GROUP_BYPASS_ORIGINAL_CALLBACK_PRESERVED",
            predicateRule = PredicateRule,
            requiresNative = true, productionZipAuthorized = false,
            candidates = candidates.Select(x => (object)new {
                path = "r6/scripts/" + x.SourcePath,
                sitePath = "r6/scripts/" + x.SitesPath,
                adapterPath = "r6/scripts/" + x.AdapterPath,
                x.Owner, x.SiteCount,
                rule = PredicateRule,
                state = remaining.Contains(x) ?
                    "PRISTINE_APPLICABLE" : "ALREADY_COMPLETE"
            }).Concat(groups.Select(x => (object)new {
                path = "r6/scripts/" + x.RelativePath,
                method = x.Method,
                owner = x.Owner,
                siteCount = x.Count,
                rule = LocationGroupRule,
                state = File.ReadAllText(Path.Combine(SourceRoot(gameRoot),
                    x.RelativePath.Replace('/', Path.DirectorySeparatorChar)),
                    Encoding.UTF8).Contains(LocationGroupMarker(x.Owner),
                        StringComparison.Ordinal) ?
                    "ALREADY_COMPLETE" : "PRISTINE_APPLICABLE"
            })).ToArray()
        }), new UTF8Encoding(false));
        return remaining.Count;
    }

    internal static int Preview(string gameRoot, string outputRoot,
        string? expectedSourceHash = null, string? basePreviewRoot = null)
    {
        var admission = new SourceAdmission(gameRoot);
        _ = admission.AuditAll();
        if (expectedSourceHash is not null &&
            !string.Equals(expectedSourceHash, admission.SourceStackSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NATIVE_CONSUMER_CAPTURE_SOURCE_DRIFT");
        string output = Path.GetFullPath(outputRoot);
        if (Inside(output, gameRoot) || File.Exists(output) ||
            (Directory.Exists(output) &&
             Directory.EnumerateFileSystemEntries(output).Any()))
            throw new InvalidDataException("NATIVE_CONSUMER_OUTPUT_COLLISION");
        var candidates = Discover(gameRoot);
        var generated = new List<(string Path, string Source)>();
        string? baseScripts = basePreviewRoot is null ? null :
            Path.Combine(Path.GetFullPath(basePreviewRoot), "r6", "scripts");
        if (baseScripts is not null &&
            (!Directory.Exists(baseScripts) || Inside(baseScripts, gameRoot) ||
             Inside(output, baseScripts) || Inside(baseScripts, output)))
            throw new InvalidDataException("NATIVE_PREDICATE_BASE_STAGE_INVALID");
        int optimizedPredicates = 0;
        foreach (var c in candidates)
        {
            string rel = "r6/scripts/" + c.AdapterPath;
            string destination = Path.GetFullPath(Path.Combine(output,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            string liveAdapter = Path.Combine(Path.GetFullPath(gameRoot),
                rel.Replace('/', Path.DirectorySeparatorChar));
            if (!Inside(destination, output))
                throw new InvalidDataException("NATIVE_CONSUMER_PATH_COLLISION");
            bool alreadyAdapter = File.Exists(liveAdapter) &&
                string.Equals(File.ReadAllText(liveAdapter, Encoding.UTF8),
                    Generate(c), StringComparison.Ordinal);
            if (File.Exists(liveAdapter) && !alreadyAdapter)
            {
                string previous = File.ReadAllText(liveAdapter, Encoding.UTF8);
                if (!previous.Contains(Marker, StringComparison.Ordinal) ||
                    previous.Contains("GCOMayBeNear_", StringComparison.Ordinal) ||
                    previous.Contains("MayBeNear(index:", StringComparison.Ordinal))
                    throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_COLLISION");
            }
            if (!alreadyAdapter)
                generated.Add((destination, Generate(c)));

            string sourceRel = "r6/scripts/" + c.SourcePath;
            string sourcePath = Path.Combine(Path.GetFullPath(gameRoot),
                sourceRel.Replace('/', Path.DirectorySeparatorChar));
            string effective = File.ReadAllText(sourcePath, Encoding.UTF8);
            if (baseScripts is not null)
            {
                string prior = Path.Combine(baseScripts,
                    c.SourcePath.Replace('/', Path.DirectorySeparatorChar));
                if (!Inside(prior, baseScripts))
                    throw new InvalidDataException("NATIVE_PREDICATE_BASE_PATH_ESCAPE");
                if (File.Exists(prior)) effective = File.ReadAllText(prior,
                    Encoding.UTF8);
            }
            if (!effective.Contains(PredicateMarker, StringComparison.Ordinal))
            {
                string sitePath = Path.Combine(SourceRoot(gameRoot),
                    c.SitesPath.Replace('/', Path.DirectorySeparatorChar));
                string? replacement = RewriteSource(effective,
                    File.ReadAllText(sitePath, Encoding.UTF8), c,
                    out var previewReason);
                if (replacement is null)
                    throw new InvalidDataException(
                        "NATIVE_PREDICATE_EFFECTIVE_SOURCE_DRIFT: " + previewReason);
                string sourceDest = Path.Combine(output,
                    sourceRel.Replace('/', Path.DirectorySeparatorChar));
                if (!Inside(sourceDest, output) ||
                    generated.Any(item => item.Path.Equals(sourceDest,
                        StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("NATIVE_PREDICATE_STAGE_COLLISION");
                generated.Add((sourceDest, replacement));
                optimizedPredicates++;
            }
        }
        int optimizedGroups = StageLocationDependencies(
            gameRoot, baseScripts, output, generated);
        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_DRIFT");
        foreach (var item in generated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
            File.WriteAllText(item.Path, item.Source, new UTF8Encoding(false));
        }
        // Count optimizations, not merely additional adapter files.
        return optimizedPredicates + optimizedGroups;
    }

    private static string Generate(Candidate c)
    {
        string name = "GCO_NativeSiteObserver_" + c.Owner;
        string text = $$"""
            module {{c.ModuleName}}
            
            import GRedRuntime.*
            
            {{Marker}}
            // Shared native FAR gates only the exact source-proven geometry
            // predicate. Callback frequency, state, UI and cleanup persist.
            public class {{name}} extends NativeLocationListener {
              public let siteStates: array<Int32>;
              public func Initialize(count: Int32) -> Void {
                ArrayResize(this.siteStates, count);
                let i: Int32 = 0;
                while i < count {
                  this.siteStates[i] = -1;
                  i += 1;
                }
              }
              public func OnGRedNativeLocation(owner: Int32, index: Int32,
                                               state: Int32) -> Void {
                if owner == {{c.Owner}} && index >= 0 &&
                   index < ArraySize(this.siteStates) {
                  this.siteStates[index] = state;
                }
              }
              public func MayBeNear(index: Int32) -> Bool {
                if index < 0 || index >= ArraySize(this.siteStates) {
                  return true; // dynamic or absent site => original check
                }
                return this.siteStates[index] != 0;
              }
              public func AllKnownFar() -> Bool {
                if ArraySize(this.siteStates) != {{c.SiteCount}} {
                  return false;
                }
                for value in this.siteStates {
                  if value != 0 { return false; }
                }
                return true;
              }
            }
            
            """;
        return text + "\n";
    }
}
