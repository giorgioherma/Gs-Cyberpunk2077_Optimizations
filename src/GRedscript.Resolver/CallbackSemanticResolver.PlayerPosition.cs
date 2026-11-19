using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

/// <summary>
/// Read-only player-position provenance and bounded consumer graph. Uses the
/// existing SourceAdmission token stream and CallbackSemanticResolver method
/// spans; this is NOT a second parser or a rewrite admission decision.
/// </summary>
internal static partial class CallbackSemanticResolver
{
    internal sealed record PlayerPositionSummary(
        int SourceFiles, int MethodSpans, int WorldPositionCalls,
        int LocalPlayer, int Npc, int Vehicle, int OtherEntity,
        int Unknown, int Mixed, int IndirectConsumers);

    private sealed record PositionOrigin(string Kind, string Reason,
        bool LocalPlayerProven = false, bool PlayerTypeHint = false)
    {
        internal static PositionOrigin Unknown(string reason) =>
            new("UNKNOWN", reason);
    }

    private sealed record PositionSite(MethodSource? Method, FileSource File,
        int At, int Line, string Receiver, PositionOrigin Origin,
        string Lifetime, string Operation);

    private sealed record PositionLink(MethodSource Caller,
        MethodSource Callee, int Line, string Receiver);

    private static readonly PositionOrigin DirectLocal =
        new("LOCAL_PLAYER", "SOURCE_LOCAL_PLAYER_GETTER", true);
    private static readonly PositionOrigin NpcEntity =
        new("NPC", "SOURCE_TYPED_NPC");
    private static readonly PositionOrigin VehicleEntity =
        new("VEHICLE", "SOURCE_TYPED_VEHICLE");

    internal static PlayerPositionSummary ScanPlayerPositionDependencies(
        string gameRoot, string outputPath)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputPath);
        if (Inside(output, game) || File.Exists(output) ||
            Directory.Exists(output))
            throw new InvalidDataException("PLAYER_POSITION_SCAN_OUTPUT_COLLISION");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        var (files, methods) = ReadAllMethodSources(game, admission);
        var byFile = methods.GroupBy(m => m.File.Path,
            StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key,
            g => g.OrderBy(m => m.Close - m.Open).ToArray(),
            StringComparer.OrdinalIgnoreCase);

        // Qualified source-defined member types, not variable-name guesses.
        var fieldTypes = PositionFieldTypes(files, byFile);
        var byOwnerMethod = methods.Where(m => m.Owner.Length != 0)
            .GroupBy(m => m.Owner + "\0" + m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var sites = new List<PositionSite>();
        foreach (var file in files)
        {
            var t = file.Tokens;
            byFile.TryGetValue(file.Path, out var inFile);
            for (int i = 2; i + 1 < t.Count; i++)
            {
                if (t[i].Value != "GetWorldPosition" ||
                    t[i - 1].Value != "." || t[i + 1].Value != "(")
                    continue;
                var method = inFile?.FirstOrDefault(m =>
                    m.Open < i && m.Close > i);
                var receiver = PositionReceiver(t, i);
                var origin = ResolvePositionReceiver(method, file, i - 2,
                    fieldTypes, methods, new HashSet<string>(StringComparer.Ordinal), 0);
                string lifetime = PositionLifetime(t, method, i);
                string operation = method is null ? "UNBOUND_METHOD" :
                    PositionOperation(method);
                sites.Add(new PositionSite(method, file, i, t[i].Line,
                    receiver, origin, lifetime, operation));
            }
        }

        // Interprocedural CALL paths are evidence of potential downstream
        // consumers, never proof that an arbitrary argument is the player.
        // Resolve only a uniquely named, source-defined receiver class.
        var links = PositionHelperLinks(methods, byOwnerMethod, fieldTypes);
        var reverse = links.GroupBy(e => e.Callee.Identity,
            StringComparer.Ordinal).ToDictionary(g => g.Key,
            g => g.ToArray(), StringComparer.Ordinal);
        var upstream = new Dictionary<string, object[]>(StringComparer.Ordinal);
        var reachable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in sites.Where(s => s.Method is not null)
                     .Select(s => s.Method!).DistinctBy(m => m.Identity))
        {
            var found = new List<object>();
            var visited = new HashSet<string>(StringComparer.Ordinal) {
                method.Identity
            };
            var queue = new Queue<(MethodSource Node, int Depth)>();
            queue.Enqueue((method, 0));
            while (queue.Count > 0 && visited.Count < 80)
            {
                var (node, depth) = queue.Dequeue();
                if (depth >= 3 || !reverse.TryGetValue(node.Identity, out var callers))
                    continue;
                foreach (var link in callers)
                {
                    if (!visited.Add(link.Caller.Identity)) continue;
                    reachable.Add(link.Caller.Identity);
                    found.Add(new {
                        path = link.Caller.File.Relative,
                        owner = link.Caller.Owner,
                        method = link.Caller.Name,
                        line = link.Line,
                        via = link.Receiver,
                        depth = depth + 1,
                        receiverResolution = "SOURCE_UNIQUE_TYPE_OR_CLASS",
                        parameterPositionOrigin = "UNPROVEN"
                    });
                    if (found.Count >= 64) break;
                    queue.Enqueue((link.Caller, depth + 1));
                }
            }
            upstream[method.Identity] = found.ToArray();
        }

        var sorted = sites.OrderBy(s => s.File.Relative, StringComparer.Ordinal)
            .ThenBy(s => s.Line).ThenBy(s => s.At).ToArray();
        var counts = sorted.GroupBy(s => s.Origin.Kind)
            .ToDictionary(x => x.Key, x => x.Count(), StringComparer.Ordinal);
        int N(string kind) => counts.GetValueOrDefault(kind);
        int local = N("LOCAL_PLAYER");
        var summary = new PlayerPositionSummary(
            admission.InspectedFileCount, methods.Count, sites.Count,
            local, N("NPC"), N("VEHICLE"), N("OTHER_ENTITY"),
            N("UNKNOWN"), N("MIXED"), reachable.Count);
        var report = new {
            schemaVersion = "gco.redscript.player-position-dependencies.v1",
            sourceStackSha256 = admission.SourceStackSha256,
            sourceFiles = summary.SourceFiles,
            methodSpans = summary.MethodSpans,
            worldPositionCallsites = summary.WorldPositionCalls,
            categoryCounts = counts,
            provenLocalPlayerCallsites = local,
            indirectConsumerMethods = summary.IndirectConsumers,
            sourceOnly = true,
            captureNeededForDiscovery = false,
            generationAuthorized = false,
            gameplayValidated = false,
            productionZipAuthorized = false,
            originalSourcesModified = false,
            // All matched calls are retained, including unknown and
            // calls outside the parser's supported method-body spans.
            callsites = sorted.Select(s => new {
                path = s.File.Relative,
                owner = s.Method?.Owner ?? "",
                method = s.Method?.Name ?? "",
                line = s.Line,
                receiver = s.Receiver,
                positionOrigin = s.Origin.Kind,
                reason = s.Origin.Reason,
                localPlayerProven = s.Origin.LocalPlayerProven,
                playerTypeHint = s.Origin.PlayerTypeHint,
                lifetime = s.Lifetime,
                operation = s.Operation,
                strategy = s.Origin.LocalPlayerProven
                    ? s.Operation is "DISTANCE" or "DISTANCE_SQUARED"
                        ? "PLAYER_POSITION_REUSE_CANDIDATE"
                        : "PLAYER_POSITION_CONSUMER_REVIEW"
                    : "NO_AUTOMATIC_REWRITE",
                originalFallbackRequired = true,
                helperCallers = s.Method is not null &&
                    upstream.TryGetValue(s.Method.Identity, out var found)
                        ? found : []
            }).ToArray(),
            helperGraph = new {
                resolvedEdges = links.Count,
                indirectConsumerMethods = reachable.Count,
                maxDepth = 3,
                maxCallersPerDirectMethod = 64,
                dynamicReceiversAndUnknownImportsAreNotAssumed = true,
                sourceCallsDoNotProveRuntimeCallFrequency = true
            }
        };
        if (!string.Equals(admission.SourceStackSha256,
                admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PLAYER_POSITION_SCAN_SOURCE_DRIFT");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }),
            new UTF8Encoding(false));
        return summary;
    }

    private static Dictionary<string, string> PositionFieldTypes(
        IReadOnlyList<FileSource> files,
        IReadOnlyDictionary<string, MethodSource[]> byFile)
    {
        var types = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var t = file.Tokens;
            byFile.TryGetValue(file.Path, out var methods);
            for (int i = 0; i + 3 < t.Count; i++)
            {
                if (t[i].Value != "class" || !Ident(t[i + 1].Value))
                    continue;
                int open = i + 2;
                while (open < t.Count && open < i + 40 &&
                       t[open].Value is not ("{" or ";")) open++;
                if (open >= t.Count || t[open].Value != "{") continue;
                int close = Match(t, open, "{", "}");
                string owner = t[i + 1].Value;
                for (int k = open + 1; k + 3 < close; k++)
                {
                    if (t[k].Value != "let" || !Ident(t[k + 1].Value) ||
                        t[k + 2].Value != ":") continue;
                    if (methods?.Any(m => m.Open < k && m.Close > k) == true)
                        continue;
                    string type = PositionType(t, k + 3, close);
                    if (type.Length != 0)
                        types[file.Module + "\0" + owner + "\0" +
                            t[k + 1].Value] = type;
                }
                i = close;
            }
        }
        return types;
    }

    private static string PositionType(
        IReadOnlyList<SourceAdmission.Token> t, int start, int end)
    {
        int max = Math.Min(end, start + 14);
        var name = new StringBuilder();
        for (int i = start; i < max; i++)
        {
            if (t[i].Value is "=" or ";" or "," or ")" or "{") break;
            if (Ident(t[i].Value)) name.Append(t[i].Value);
        }
        return name.ToString();
    }

    private static PositionOrigin FromType(string type) =>
        type.Contains("NPCPuppet", StringComparison.Ordinal) ||
        type.Contains("ScriptedPuppet", StringComparison.Ordinal)
            ? NpcEntity :
        type.Contains("VehicleObject", StringComparison.Ordinal) ||
        type.Contains("VehiclePuppet", StringComparison.Ordinal)
            ? VehicleEntity :
        type.Contains("PlayerPuppet", StringComparison.Ordinal)
            ? new("UNKNOWN", "PLAYER_PUPPET_TYPE_NOT_LOCAL_PROVEN",
                PlayerTypeHint: true) :
        type.Contains("GameObject", StringComparison.Ordinal) ||
        type.Contains("Entity", StringComparison.Ordinal)
            ? PositionOrigin.Unknown("GENERIC_OBJECT_TYPE_NOT_LOCAL_PROVEN") :
        PositionOrigin.Unknown("RECEIVER_TYPE_UNRESOLVED");

    private static PositionOrigin ResolvePositionReceiver(MethodSource? method,
        FileSource file, int receiverAt,
        IReadOnlyDictionary<string, string> fields,
        IReadOnlyList<MethodSource> all,
        HashSet<string> visited, int depth)
    {
        var t = file.Tokens;
        if (receiverAt < 0 || depth >= 5)
            return PositionOrigin.Unknown("PROVENANCE_DEPTH_OR_RECEIVER_UNKNOWN");
        if (t[receiverAt].Value == ")")
        {
            int open = receiverAt, level = 0;
            for (; open >= 0; open--)
            {
                if (t[open].Value == ")") level++;
                if (t[open].Value == "(" && --level == 0) break;
            }
            if (open > 0 && t[open - 1].Value == "GetPlayer")
                return DirectLocal;
            if (open > 0 && t[open - 1].Value ==
                "GetLocalPlayerMainGameObject")
                return DirectLocal;
            return PositionOrigin.Unknown("CHAINED_RECEIVER_OR_CAST_UNRESOLVED");
        }
        string name = t[receiverAt].Value;
        if (name == "this")
        {
            string owner = method?.Owner ?? "";
            if (owner == "VehicleObject") return VehicleEntity;
            if (owner == "NPCPuppet") return NpcEntity;
            // Source-declared subclasses can use this.GetWorldPosition()
            // without naming their base type in the method.
            for (int k = 0; k + 3 < t.Count; k++)
                if (t[k].Value == "class" && t[k + 1].Value == owner &&
                    t[k + 2].Value == "extends")
                {
                    var baseType = FromType(t[k + 3].Value);
                    if (baseType.Kind is "NPC" or "VEHICLE")
                        return baseType;
                    break;
                }
            if (owner == "PlayerPuppet")
                return new("UNKNOWN", "PLAYER_PUPPET_CONTEXT_LOCAL_UNPROVEN",
                    PlayerTypeHint: true);
            return PositionOrigin.Unknown("THIS_RECEIVER_CLASS_UNRESOLVED");
        }
        if (!Ident(name))
            return PositionOrigin.Unknown("RECEIVER_EXPRESSION_UNRESOLVED");
        bool field = receiverAt >= 2 &&
            t[receiverAt - 1].Value == "." &&
            t[receiverAt - 2].Value == "this";
        string key = (field ? "FIELD:" : "LOCAL:") + name;
        if (!visited.Add(key))
            return PositionOrigin.Unknown("ALIAS_CYCLE");
        var assigned = new List<PositionOrigin>();
        // Fields can be initialized in any method of their owning class.
        foreach (var sourceMethod in field && method is not null ?
            all.Where(m => m.File.Module == file.Module &&
                           m.Owner == method.Owner) :
            method is null ? [] : [method])
        {
            var st = sourceMethod.File.Tokens;
            for (int i = sourceMethod.Open + 1; i + 1 < sourceMethod.Close; i++)
            {
                if (st[i].Value != name ||
                    (field && (i < 2 || st[i - 1].Value != "." ||
                               st[i - 2].Value != "this")))
                    continue;
                if (!field && i > 0 && st[i - 1].Value == ".") continue;
                int assignAt = i + 1;
                // let player: ref<PlayerPuppet> = GetPlayer(...)
                // has a typed initializer rather than name = ... .
                if (!field && assignAt < sourceMethod.Close &&
                    st[assignAt].Value == ":")
                {
                    while (assignAt < sourceMethod.Close &&
                           assignAt < i + 18 &&
                           st[assignAt].Value is not ("=" or ";" or "{"))
                        assignAt++;
                }
                if (assignAt >= sourceMethod.Close ||
                    st[assignAt].Value != "=") continue;
                int finish = assignAt + 1;
                while (finish < sourceMethod.Close &&
                       finish < assignAt + 45 &&
                       st[finish].Value is not (";" or "{")) finish++;
                var expr = st.Skip(assignAt + 1).Take(finish - assignAt - 1)
                    .Select(x => x.Value).ToArray();
                if (expr.Length == 0) continue;
                if (expr.Contains("GetPlayer") &&
                    expr.Contains("("))
                    assigned.Add(DirectLocal);
                else if (expr.Contains("GetLocalPlayerMainGameObject"))
                    assigned.Add(DirectLocal);
                else if (expr.Contains("NPCPuppet"))
                    assigned.Add(NpcEntity);
                else if (expr.Contains("VehicleObject"))
                    assigned.Add(VehicleEntity);
                else if (expr.Length == 1 && expr[0] == "null")
                {
                    // Explicit lifecycle clear does not change a proven
                    // assignment's origin, but cannot establish freshness.
                }
                else if (expr.Length == 1 && Ident(expr[0]))
                    assigned.Add(ResolvePositionReceiver(sourceMethod,
                        sourceMethod.File, i + 2, fields, all,
                        new HashSet<string>(visited, StringComparer.Ordinal),
                        depth + 1));
                else
                    assigned.Add(PositionOrigin.Unknown(
                        "ASSIGNMENT_EXPRESSION_UNRESOLVED"));
            }
        }
        var origins = assigned.Select(x => x.Kind).Distinct().ToArray();
        if (origins.Length > 1)
            return new("MIXED", "SOURCE_ASSIGNMENTS_HAVE_MIXED_ORIGINS");
        if (origins.Length == 1 && origins[0] != "UNKNOWN")
            return assigned.First(x => x.Kind == origins[0]);
        if (assigned.Count > 0)
            return PositionOrigin.Unknown("SOURCE_ASSIGNMENT_UNPROVEN");

        if (field && method is not null &&
            fields.TryGetValue(file.Module + "\0" + method.Owner + "\0" + name,
                out string? fieldType))
            return FromType(fieldType);
        if (method is not null)
        {
            var st = method.File.Tokens;
            for (int k = method.SignatureOpen + 1;
                 k + 2 < method.SignatureClose; k++)
                if (st[k].Value == name && st[k + 1].Value == ":")
                    return FromType(PositionType(st, k + 2,
                        method.SignatureClose));
            // Source-typed locally declared variables and cast expressions.
            for (int k = method.Open + 1; k + 2 < receiverAt; k++)
                if (st[k].Value == "let" && st[k + 1].Value == name &&
                    st[k + 2].Value == ":")
                    return FromType(PositionType(st, k + 3, receiverAt));
        }
        return PositionOrigin.Unknown("RECEIVER_ORIGIN_UNRESOLVED");
    }

    private static string PositionReceiver(
        IReadOnlyList<SourceAdmission.Token> t, int i)
    {
        int at = i - 2;
        if (at < 0) return "<unresolved>";
        if (t[at].Value == ")") return "<call-result>";
        if (at >= 2 && t[at - 1].Value == "." &&
            t[at - 2].Value == "this") return "this." + t[at].Value;
        return t[at].Value;
    }

    private static string PositionLifetime(
        IReadOnlyList<SourceAdmission.Token> t, MethodSource? m, int i)
    {
        if (m is null) return "UNKNOWN";
        int from = Math.Max(m.Open + 1, i - 24);
        for (int k = i - 1; k >= from; k--)
        {
            if (t[k].Value is ";" or "{" or "}") break;
            if (t[k].Value == "=" && k >= 3 && t[k - 1].Value is not ("=" or "!"))
                return t[k - 1].Value is "position" or "lastPosition" ?
                    "POSSIBLY_SAVED_POSITION" :
                    (k >= 3 && t[k - 3].Value == "this" &&
                     t[k - 2].Value == ".")
                        ? "SAVED_TO_FIELD" : "CALL_LOCAL_OR_PASSED";
        }
        return "IMMEDIATE_OR_ARGUMENT";
    }

    private static string PositionOperation(MethodSource method)
    {
        var t = method.File.Tokens;
        bool sq = false, dist = false, near = false, other = false;
        for (int i = method.Open + 1; i < method.Close; i++)
        {
            sq |= t[i].Value == "DistanceSquared";
            dist |= t[i].Value is "Distance" or "GetDistanceTo";
            near |= t[i].Value is "IsNear" or "IsInRange" or "PointInTriangle";
            other |= t[i].Value is "Length" or "LengthSquared" or "IsPointIn";
        }
        return sq ? "DISTANCE_SQUARED" : dist ? "DISTANCE" :
            near ? "CONTAINMENT_OR_RANGE_HELPER" :
            other ? "VECTOR_OR_CONTAINMENT" : "POSITION_READ_OR_FORWARD";
    }

    private static List<PositionLink> PositionHelperLinks(
        IReadOnlyList<MethodSource> methods,
        IReadOnlyDictionary<string, MethodSource[]> byOwnerMethod,
        IReadOnlyDictionary<string, string> fields)
    {
        var links = new List<PositionLink>();
        foreach (var caller in methods)
        {
            var t = caller.File.Tokens;
            for (int i = caller.Open + 1; i + 1 < caller.Close; i++)
            {
                if (t[i + 1].Value != "(" || !Ident(t[i].Value)) continue;
                string? owner = null, receiver = "";
                if (i >= 2 && t[i - 1].Value == "." &&
                    Ident(t[i - 2].Value))
                {
                    receiver = t[i - 2].Value;
                    if (receiver == "this") owner = caller.Owner;
                    else if (i >= 4 && t[i - 3].Value == "." &&
                        t[i - 4].Value == "this")
                    {
                        fields.TryGetValue(caller.File.Module + "\0" +
                            caller.Owner + "\0" + receiver, out owner);
                        if (owner is not null)
                        {
                            // ref<Foo> / wref<Foo> -> Foo. No dynamic
                            // subtyping or override equivalence assumed.
                            foreach (var prefix in new[] { "wref", "ref" })
                                if (owner.StartsWith(prefix, StringComparison.Ordinal))
                                    owner = owner[prefix.Length..];
                        }
                    }
                    else owner = receiver;
                }
                else if (i < caller.File.Tokens.Count && t[i - 1].Value != ".")
                {
                    owner = caller.Owner;
                    receiver = "this";
                }
                if (owner is null) continue;
                var key = owner + "\0" + t[i].Value;
                if (!byOwnerMethod.TryGetValue(key, out var candidates))
                    continue;
                // Prefer a unique method in the caller module. If not,
                // require global uniqueness; overloads remain unresolved.
                var same = candidates.Where(x =>
                    x.File.Module == caller.File.Module).ToArray();
                var target = same.Length == 1 ? same[0] :
                    candidates.Length == 1 ? candidates[0] : null;
                if (target is null || target.Identity == caller.Identity)
                    continue;
                links.Add(new PositionLink(caller, target, t[i].Line, receiver));
            }
        }
        return links.DistinctBy(x => x.Caller.Identity + "\0" +
                x.Callee.Identity + "\0" + x.Line).ToList();
    }
}
