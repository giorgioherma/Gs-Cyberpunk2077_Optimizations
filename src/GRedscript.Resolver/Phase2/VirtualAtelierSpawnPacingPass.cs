using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 6.0: spread Virtual Atelier Delivery dynamic drop-point
/// creation over owner-scoped DelaySystem slices. Retain synchronous behavior
/// when the clock is unavailable, cancel queued work on detach/despawn, and
/// never transplant the historical whole file.
/// </summary>
internal static class VirtualAtelierSpawnPacingPass
{
    internal const string Rule = "VIRTUAL_ATELIER_DROPPOINT_SPAWN_PACING_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);
    private sealed record Block(int Start, int Open, int Close);
    private sealed record Edit(int Start, int End, string Value);
    private sealed record Decision(string Status, string? Reason, string? Updated,
        int Edits);

    private static bool Has(string s, string pattern) =>
        Regex.IsMatch(s, pattern, RegexOptions.CultureInvariant);
    private static string NL(string s) => s.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static int Matching(IReadOnlyList<SourceAdmission.Token> t, int i,
        string opening, string closing)
    {
        if (i < 0 || i >= t.Count || t[i].Value != opening) return -1;
        int depth = 0;
        for (; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? FindClass(string src, string name)
    {
        var t = SourceAdmission.ReadTokens(src);
        Block? result = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != name) continue;
            int open = i + 2;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open == t.Count || t[open].Value != "{") return null;
            int end = Matching(t, open, "{", "}");
            if (end < 0 || result != null) return null;
            result = new(t[i].Start, t[open].Start, t[end].Start);
            i = end;
        }
        return result;
    }

    private static Block? FindMethod(string src, Block owner, string name)
    {
        var t = SourceAdmission.ReadTokens(src);
        Block? result = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Start <= owner.Open || t[i].Start >= owner.Close ||
                t[i].Value != "func" || t[i + 1].Value != name || t[i + 2].Value != "(")
                continue;
            int args = Matching(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int end = Matching(t, open, "{", "}");
            if (end < 0 || t[end].Start >= owner.Close || result != null) return null;
            result = new(t[i].Start, t[open].Start, t[end].Start);
            i = end;
        }
        return result;
    }

    private static Decision Rewrite(string src)
    {
        if (!Has(src, @"\bmodule\s+AtelierDelivery\b"))
            return new("REFUSED", "VAD_MODULE_MISSING", null, 0);
        var owner = FindClass(src, "AtelierDropPointsSpawner");
        if (owner == null) return new("REFUSED", "VAD_SPAWNER_AMBIGUOUS", null, 0);
        var spawn = FindMethod(src, owner, "SpawnInstancesByTag");
        var attach = FindMethod(src, owner, "OnAttach");
        var despawn = FindMethod(src, owner, "DespawnAll");
        var detach = FindMethod(src, owner, "OnDetach");
        if (spawn == null || attach == null || despawn == null)
            return new("REFUSED", "VAD_SPAWNER_LIFECYCLE_INCOMPATIBLE", null, 0);
        string body(Block b) => src[(b.Open + 1)..b.Close];
        if (!Has(src, @"\bclass\s+AtelierDropPointInstance\b") ||
            !Has(body(owner), @"\bdelaySystem\s*:\s*wref\s*<\s*DelaySystem\s*>") ||
            !Has(body(owner), @"\bentitySystem\s*:\s*wref\s*<\s*DynamicEntitySystem\s*>") ||
            !Has(body(owner), @"\bspawnConfig\s*:\s*ref\s*<\s*AtelierDropPointsSpawnerConfig\s*>") ||
            !Has(body(attach), @"\bthis\s*\.\s*delaySystem\s*=") ||
            !Has(body(despawn), @"\bthis\s*\.\s*entitySystem\s*\.\s*DeleteTagged\s*\("))
            return new("REFUSED", "VAD_ENTITY_AND_CLOCK_FLOW_UNPROVEN", null, 0);

        // Recognize completed historical hand-edits without adding a second
        // queue. An incomplete *other* implementation cannot safely have two
        // clocks; this source-local operation is refused without blocking peers.
        bool oldQueue = Has(src, @"\bgrtSpawnQueue\b|\bgrtSpawnBatchActive\b");
        if (oldQueue)
            return Has(src, @"\bfunc\s+ProcessSpawnQueue\s*\(") &&
                   Has(src, @"\bclass\s+AtelierDropPointSpawnBatchCallback\b") &&
                   Has(body(spawn), @"\bStartSpawnQueue\s*\(")
                ? new("COMPLETE", null, null, 0)
                : new("REFUSED", "VAD_FOREIGN_PARTIAL_QUEUE_UNPROVEN", null, 0);

        var edits = new List<Edit>();
        string nl = NL(src);
        void add(int start, int end, string value) => edits.Add(new(start, end, value));

        // Partial handwritten implementations of this rule are repaired
        // piece-by-piece. Required names are owner-scoped; no duplicates.
        if (!Has(src, @"\bclass\s+GcoAtelierSpawnBatchCallback\b"))
        {
            add(owner.Start, owner.Start, string.Join(nl, new[]
            {
                "public class GcoAtelierSpawnBatchCallback extends DelayCallback {",
                "  public let spawner: wref<AtelierDropPointsSpawner>;",
                "  public let generation: Int32;",
                "  public func Call() -> Void {",
                "    if IsDefined(this.spawner) { this.spawner.ProcessSpawnQueue(this.generation); };",
                "  }",
                "}",
                ""
            }));
        }

        var fields = new[]
        {
            ("m_gcoVadSpawnQueue", "array<ref<AtelierDropPointInstance>>"),
            ("m_gcoVadPendingTags", "array<CName>"),
            ("m_gcoVadSpawnQueued", "Bool"),
            ("m_gcoVadSpawnCallback", "DelayID"),
            ("m_gcoVadSpawnGeneration", "Int32")
        };
        var missingFields = fields.Where(f =>
            !Has(body(owner), @"\blet\s+" + f.Item1 + @"\s*:")).ToArray();
        if (missingFields.Length > 0)
        {
            add(owner.Open + 1, owner.Open + 1, nl +
                string.Concat(missingFields.Select(f =>
                    "  private let " + f.Item1 + ": " + f.Item2 + ";" + nl)));
        }

        string spawnBody = body(spawn);
        if (!Has(spawnBody, @"\bArrayPush\s*\(\s*this\s*\.\s*m_gcoVadSpawnQueue\s*,"))
        {
            var list = Regex.Match(spawnBody,
                @"\blet\s+(?<list>[A-Za-z_]\w*)\s*:\s*array\s*<\s*ref\s*<\s*AtelierDropPointInstance\s*>\s*>\s*=\s*this\s*\.\s*spawnConfig\s*\.\s*GetSpawnPointsByTag\s*\(\s*entityTag\s*\)\s*;");
            if (!list.Success)
                return new("REFUSED", "VAD_TAG_SOURCE_INCOMPATIBLE", null, 0);
            string listName = list.Groups["list"].Value;
            var loop = Regex.Match(spawnBody,
                @"\bfor\s+(?<item>[A-Za-z_]\w*)\s+in\s+" +
                Regex.Escape(listName) +
                @"\s*\{\s*this\s*\.\s*SpawnDropPointEntity\s*\(\s*(?<arg>[A-Za-z_]\w*)\s*\)\s*;\s*\}\s*;");
            if (!loop.Success || loop.Groups["item"].Value != loop.Groups["arg"].Value ||
                Regex.Matches(spawnBody, @"\bSpawnDropPointEntity\s*\(").Count != 1)
                return new("REFUSED", "VAD_DIRECT_SPAWN_LOOP_INCOMPATIBLE", null, 0);
            string item = loop.Groups["item"].Value;
            string before = string.Join(nl, new[]
            {
                "",
                "    // Retain original synchronous spawning if the owner's clock is unavailable.",
                "    if !IsDefined(this.delaySystem) {",
                "      for vadImmediate in " + listName + " { this.SpawnDropPointEntity(vadImmediate); };",
                "      return;",
                "    };",
                "    if ArrayContains(this.m_gcoVadPendingTags, entityTag) { return; };",
                "    ArrayPush(this.m_gcoVadPendingTags, entityTag);"
            });
            var replacement = Regex.Replace(loop.Value,
                @"this\s*\.\s*SpawnDropPointEntity\s*\(\s*" +
                Regex.Escape(item) + @"\s*\)\s*;",
                "ArrayPush(this.m_gcoVadSpawnQueue, " + item + ");");
            var newBody = spawnBody[..(list.Index + list.Length)] + before +
                spawnBody[(list.Index + list.Length)..loop.Index] + replacement +
                spawnBody[(loop.Index + loop.Length)..] +
                nl + "    this.QueueGcoVadSpawnTick();" + nl + "  ";
            add(spawn.Open + 1, spawn.Close, newBody);
        }
        else if (!Has(spawnBody, @"\bQueueGcoVadSpawnTick\s*\(") ||
                 !Has(spawnBody, @"\bm_gcoVadPendingTags\b") ||
                 !Has(spawnBody, @"\bSpawnDropPointEntity\s*\("))
        {
            return new("REFUSED", "VAD_PARTIAL_SPAWN_BODY_INCOMPATIBLE", null, 0);
        }

        void addLifecycle(Block b)
        {
            if (!Has(body(b), @"\bthis\s*\.\s*CancelGcoVadSpawnBatch\s*\("))
                add(b.Open + 1, b.Open + 1,
                    nl + "    this.CancelGcoVadSpawnBatch();" + nl + "    ");
        }
        addLifecycle(attach);
        addLifecycle(despawn);
        if (detach != null) addLifecycle(detach);

        var methods = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["QueueGcoVadSpawnTick"] = string.Join(nl, new[]
            {
                "  private final func QueueGcoVadSpawnTick() -> Void {",
                "    if this.m_gcoVadSpawnQueued || ArraySize(this.m_gcoVadSpawnQueue) <= 0 { return; };",
                "    if !IsDefined(this.delaySystem) { this.FlushGcoVadSpawnQueue(); return; };",
                "    let cb = new GcoAtelierSpawnBatchCallback();",
                "    cb.spawner = this;",
                "    cb.generation = this.m_gcoVadSpawnGeneration;",
                "    this.m_gcoVadSpawnQueued = true;",
                "    this.m_gcoVadSpawnCallback = this.delaySystem.DelayCallback(cb, 0.05, false);",
                "  }"
            }),
            ["ProcessSpawnQueue"] = string.Join(nl, new[]
            {
                "  public final func ProcessSpawnQueue(generation: Int32) -> Void {",
                "    if generation != this.m_gcoVadSpawnGeneration { return; };",
                "    this.m_gcoVadSpawnQueued = false;",
                "    if ArraySize(this.m_gcoVadSpawnQueue) <= 0 {",
                "      ArrayClear(this.m_gcoVadPendingTags);",
                "      return;",
                "    };",
                "    if !IsDefined(this.entitySystem) { return; };",
                "    let instance = this.m_gcoVadSpawnQueue[0];",
                "    ArrayErase(this.m_gcoVadSpawnQueue, 0);",
                "    if IsDefined(instance) && !this.entitySystem.IsPopulated(instance.uniqueTag) {",
                "      this.SpawnDropPointEntity(instance);",
                "    };",
                "    if ArraySize(this.m_gcoVadSpawnQueue) > 0 {",
                "      this.QueueGcoVadSpawnTick();",
                "    } else {",
                "      ArrayClear(this.m_gcoVadPendingTags);",
                "    };",
                "  }"
            }),
            ["FlushGcoVadSpawnQueue"] = string.Join(nl, new[]
            {
                "  private final func FlushGcoVadSpawnQueue() -> Void {",
                "    while ArraySize(this.m_gcoVadSpawnQueue) > 0 {",
                "      let instance = this.m_gcoVadSpawnQueue[0];",
                "      ArrayErase(this.m_gcoVadSpawnQueue, 0);",
                "      if IsDefined(instance) { this.SpawnDropPointEntity(instance); };",
                "    };",
                "    this.m_gcoVadSpawnQueued = false;",
                "    ArrayClear(this.m_gcoVadPendingTags);",
                "  }"
            }),
            ["CancelGcoVadSpawnBatch"] = string.Join(nl, new[]
            {
                "  private final func CancelGcoVadSpawnBatch() -> Void {",
                "    if this.m_gcoVadSpawnQueued && IsDefined(this.delaySystem) {",
                "      this.delaySystem.CancelCallback(this.m_gcoVadSpawnCallback);",
                "    };",
                "    this.m_gcoVadSpawnQueued = false;",
                "    this.m_gcoVadSpawnGeneration += 1;",
                "    ArrayClear(this.m_gcoVadSpawnQueue);",
                "    ArrayClear(this.m_gcoVadPendingTags);",
                "  }"
            })
        };
        var missing = methods.Where(m => FindMethod(src, owner, m.Key) == null)
            .Select(m => m.Value).ToList();
        if (detach == null)
            missing.Add(string.Join(nl, new[]
            {
                "  private func OnDetach() -> Void {",
                "    this.CancelGcoVadSpawnBatch();",
                "  }"
            }));
        if (missing.Count > 0)
            add(owner.Close, owner.Close, nl + string.Join(nl + nl, missing) + nl);

        if (edits.Count == 0) return new("COMPLETE", null, null, 0);
        string updated = src;
        foreach (var e in edits.OrderByDescending(e => e.Start))
            updated = updated[..e.Start] + e.Value + updated[e.End..];
        var now = FindClass(updated, "AtelierDropPointsSpawner");
        if (now == null || FindMethod(updated, now, "ProcessSpawnQueue") == null ||
            FindMethod(updated, now, "CancelGcoVadSpawnBatch") == null ||
            !Has(updated, @"\bclass\s+GcoAtelierSpawnBatchCallback\b") ||
            !Has(updated, @"\bArrayPush\s*\(\s*this\.m_gcoVadSpawnQueue\s*,"))
            throw new InvalidDataException("VAD_QUEUE_POSTCONDITION_FAILED");
        return new("APPLIED", null, updated, edits.Count);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? baseOverlayRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || File.Exists(output) ||
            Directory.Exists(output) || Inside(output, game))
            throw new InvalidDataException("VAD_OUTPUT_UNSAFE");
        if (baseOverlayRoot != null &&
            (!Directory.Exists(baseOverlayRoot) || Inside(baseOverlayRoot, game) ||
             Inside(output, baseOverlayRoot) || Inside(baseOverlayRoot, output)))
            throw new InvalidDataException("VAD_OVERLAY_UNSAFE");
        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string sha = admission.SourceStackSha256 ??
            throw new InvalidDataException("VAD_SOURCE_HASH_MISSING");
        var matches = Directory.EnumerateFiles(scripts, "*.reds", SearchOption.AllDirectories)
            .Where(p =>
            {
                var src = File.ReadAllText(p, Encoding.UTF8);
                return Has(src, @"\bmodule\s+AtelierDelivery\b") &&
                       Has(src, @"\bclass\s+AtelierDropPointsSpawner\b");
            }).ToArray();
        string status = "NOT_APPLICABLE";
        string? reason = null, relative = null, changed = null;
        int edits = 0;
        if (matches.Length > 1)
        {
            status = "REFUSED";
            reason = "VAD_DUPLICATE_SPAWNER_OWNERS";
        }
        else if (matches.Length == 1)
        {
            var path = matches[0];
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("VAD_OWNER_SOURCE_LINK_UNSAFE");
            relative = "r6/scripts/" +
                Path.GetRelativePath(scripts, path).Replace('\\', '/');
            string source = File.ReadAllText(path, Encoding.UTF8);
            if (baseOverlayRoot != null)
            {
                string staged = Path.GetFullPath(Path.Combine(baseOverlayRoot,
                    relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!Inside(staged, baseOverlayRoot))
                    throw new InvalidDataException("VAD_OVERLAY_ESCAPE");
                if (File.Exists(staged))
                    source = File.ReadAllText(staged, Encoding.UTF8);
            }
            var decision = Rewrite(source);
            status = decision.Status;
            reason = decision.Reason;
            changed = decision.Updated;
            edits = decision.Edits;
        }
        if (!string.Equals(sha, admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("VAD_SOURCE_CHANGED_DURING_GENERATE");
        Directory.CreateDirectory(output);
        if (changed != null && relative != null)
        {
            string dst = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dst, output) ||
                !dst.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("VAD_OUTPUT_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, changed, new UTF8Encoding(false));
        }
        var result = new Result(Rule, status, edits,
            status == "COMPLETE" ? 1 : 0, relative, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return result;
    }

    private static bool Inside(string child, string root) =>
        Path.GetFullPath(child).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
