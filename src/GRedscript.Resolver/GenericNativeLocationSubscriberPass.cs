using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver;

/// <summary>
/// Experimental native-location subscription consumer generation.
/// Recognizes an opt-in source SHAPE, not a fixed mod list:
/// ScriptableSystem Start/Stop + player world-position proximity callback
/// looping over a same-module literal sphere factory. Generates an additive
/// REDscript adapter in the source mod directory. Original mod timers, distance
/// checks, state transitions and callbacks remain unchanged at this checkpoint.
/// </summary>
internal static class GenericNativeLocationSubscriberPass
{
    internal const string Rule = "NATIVE_PLAYER_LOCATION_SUBSCRIBER_V1";
    private const string Marker = "// GCO-GENERIC:" + Rule;
    private static readonly Regex Module = new(
        @"(?m)^[ \t]*module[ \t]+(?<module>[A-Za-z_][\w.]*)[ \t]*$",
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
                    !Regex.IsMatch(body, @"\b(?:public|private)\s+func\s+OnProximityCheck\s*\(\s*\)",
                        RegexOptions.CultureInvariant) ||
                    !PlayerPosition.IsMatch(body) ||
                    !body.Contains("Vector4.Distance(", StringComparison.Ordinal))
                    continue;

                var loops = ForFactory.Matches(body).Cast<Match>().ToArray();
                foreach (var loop in loops)
                {
                    string factoryName = loop.Groups["factory"].Value;
                    string item = loop.Groups["item"].Value;
                    if (!Regex.IsMatch(body,
                            @"\b" + Regex.Escape(item) +
                            @"\.location\b", RegexOptions.CultureInvariant) ||
                        !Regex.IsMatch(body,
                            @"\b" + Regex.Escape(item) +
                            @"\.proximityRadius\b", RegexOptions.CultureInvariant))
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
                    candidates.Add(new(rel, f.Sites.RelativePath,
                        f.Module, klass.Groups["name"].Value,
                        factoryName, f.Sites.Owner, count, adapter));
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

    private static bool Inside(string child, string root)
    {
        var p = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        var c = Path.GetFullPath(child);
        return c.StartsWith(p + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
    }

    internal static int Scan(string gameRoot, string outFile)
    {
        var admission = new SourceAdmission(gameRoot);
        _ = admission.AuditAll();
        var candidates = Discover(gameRoot);
        if (Inside(outFile, gameRoot) || File.Exists(outFile))
            throw new InvalidDataException("NATIVE_CONSUMER_SCAN_OUTPUT_COLLISION");
        foreach (var c in candidates)
        {
            if (File.Exists(Path.Combine(SourceRoot(gameRoot),
                    c.AdapterPath.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidDataException("NATIVE_CONSUMER_ALREADY_PRESENT_OR_COLLISION");
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
            eligible = candidates.Count,
            mode = "OBSERVER_ONLY_ORIGINAL_MOD_BEHAVIOR_UNCHANGED",
            requiresNative = true, productionZipAuthorized = false,
            candidates = candidates.Select(x => new {
                path = "r6/scripts/" + x.SourcePath,
                sitePath = "r6/scripts/" + x.SitesPath,
                adapterPath = "r6/scripts/" + x.AdapterPath,
                x.Owner, x.SiteCount,
                state = "PRISTINE_APPLICABLE"
            })
        }), new UTF8Encoding(false));
        return candidates.Count;
    }

    internal static int Preview(string gameRoot, string outputRoot,
        string? expectedSourceHash = null)
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
        foreach (var c in candidates)
        {
            string rel = "r6/scripts/" + c.AdapterPath;
            string destination = Path.GetFullPath(Path.Combine(output,
                rel.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(destination, output) ||
                File.Exists(Path.Combine(Path.GetFullPath(gameRoot),
                    rel.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidDataException("NATIVE_CONSUMER_PATH_COLLISION");
            generated.Add((destination, Generate(c)));
        }
        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NATIVE_CONSUMER_SOURCE_DRIFT");
        foreach (var item in generated)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
            File.WriteAllText(item.Path, item.Source, new UTF8Encoding(false));
        }
        return generated.Count;
    }

    private static string Generate(Candidate c)
    {
        string name = "GCO_NativeSiteObserver_" + c.Owner;
        string fields = "m_gcoNativeLocations_" + c.Owner;
        string listener = "m_gcoNativeListener_" + c.Owner;
        string attach = "GCOAttachNativeLocations_" + c.Owner;
        string detach = "GCODetachNativeLocations_" + c.Owner;
        string text = $$"""
            module {{c.ModuleName}}
            
            import GRedRuntime.*
            
            {{Marker}}
            // Experimental native location observer. The original source
            // checks, callbacks, UI and 0.25s game timer are NOT replaced.
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
            
            @addField({{c.ClassName}})
            private let {{fields}}: array<Int32>;
            
            @addField({{c.ClassName}})
            private let {{listener}}: ref<{{name}}>;
            
            @addMethod({{c.ClassName}})
            private func {{attach}}() -> Void {
              if ArraySize(this.{{fields}}) > 0 { return; }
              let runtime = Runtime.Get(GetGameInstance());
              if !IsDefined(runtime) || !runtime.IsReady() { return; }
              let service = runtime.GetNativeLocations();
              if !IsDefined(service) { return; }
              let native = new GCETNativeLocationBackend();
              this.{{listener}} = new {{name}}();
              this.{{listener}}.Initialize({{c.SiteCount}});
              let i: Int32 = 0;
              while i < {{c.SiteCount}} {
                let id = service.SubscribeLocation({{c.Owner}}, i,
                  {{c.SiteCount}}, this.{{listener}}, native);
                if id <= 0 {
                  this.{{detach}}();
                  return;
                }
                ArrayPush(this.{{fields}}, id);
                i += 1;
              }
            }
            
            @addMethod({{c.ClassName}})
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
            
            @wrapMethod({{c.ClassName}})
            public func Start() -> Void {
              wrappedMethod();
              this.{{attach}}();
            }
            
            @wrapMethod({{c.ClassName}})
            public func Stop() -> Void {
              this.{{detach}}();
              wrappedMethod();
            }
            """;
        return text + "\n";
    }
}
