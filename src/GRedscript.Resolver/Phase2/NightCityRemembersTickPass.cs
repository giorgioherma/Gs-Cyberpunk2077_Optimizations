using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Historical Pass 2.2: move NCR's stable 30-second maintenance cadence to
/// GRedRuntime Scheduler when the runtime is ready; keep the owner's original
/// DelaySystem callback as the exclusive fallback. Session end and detach
/// cancel both routes. Never change faction, decay, or pending-kill logic.
/// </summary>
internal static class NightCityRemembersTickPass
{
    internal const string Rule = "NCR_MAINTENANCE_SCHEDULER_FALLBACK_V1";
    internal sealed record Result(string RuleId, string Status, int AppliedEdits,
        int AlreadyComplete, string? SourcePath, string? Reason,
        bool SourceUnchanged, bool GameplayValidated);

    private sealed record Block(int Start, int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Decision(string Status, string? Reason, string? Changed,
        int Edits);

    private static bool Has(string source, string regex) =>
        Regex.IsMatch(source, regex, RegexOptions.CultureInvariant);
    private static string NL(string source) =>
        source.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static int End(IReadOnlyList<SourceAdmission.Token> tokens, int start,
        string opening, string closing)
    {
        if (start < 0 || start >= tokens.Count ||
            tokens[start].Value != opening) return -1;
        int depth = 0;
        for (int i = start; i < tokens.Count; i++)
        {
            if (tokens[i].Value == opening) depth++;
            else if (tokens[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static Block? FindClass(string source, string name)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Value != "class" || t[i + 1].Value != name) continue;
            int open = i + 2;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int end = End(t, open, "{", "}");
            if (end < 0 || found is not null) return null;
            found = new(t[i].Start, t[open].Start, t[end].Start);
            i = end;
        }
        return found;
    }

    private static Block? FindMethod(string source, Block owner, string name)
    {
        var t = SourceAdmission.ReadTokens(source);
        Block? found = null;
        for (int i = 0; i + 2 < t.Count; i++)
        {
            if (t[i].Start <= owner.Open || t[i].Start >= owner.Close ||
                t[i].Value != "func" || t[i + 1].Value != name ||
                t[i + 2].Value != "(") continue;
            int args = End(t, i + 2, "(", ")");
            if (args < 0) return null;
            int open = args + 1;
            while (open < t.Count && t[open].Value is not ("{" or ";")) open++;
            if (open >= t.Count || t[open].Value != "{") return null;
            int end = End(t, open, "{", "}");
            if (end < 0 || t[end].Start > owner.Close || found is not null)
                return null;
            found = new(t[i].Start, t[open].Start, t[end].Start);
            i = end;
        }
        return found;
    }

    private static bool RuntimeAbi(string scripts)
    {
        bool runtime = false, scheduler = false, job = false;
        foreach (string file in Directory.EnumerateFiles(
            scripts, "*.reds", SearchOption.AllDirectories))
        {
            string source = File.ReadAllText(file, Encoding.UTF8);
            if (!Has(source, @"\bmodule\s+GRedRuntime\b")) continue;
            if (Has(source, @"\bclass\s+Runtime\b") &&
                Has(source, @"\bfunc\s+IsReady\s*\(") &&
                Has(source, @"\bfunc\s+GetScheduler\s*\(")) runtime = true;
            if (Has(source, @"\bclass\s+Scheduler\b") &&
                Has(source, @"\bfunc\s+Register\s*\(") &&
                Has(source, @"\bfunc\s+Unregister\s*\(")) scheduler = true;
            if (Has(source, @"\bclass\s+ScheduledJob\b") &&
                Has(source, @"\bfunc\s+Configure\s*\(")) job = true;
            if (runtime && scheduler && job) return true;
        }
        return false;
    }

    private static Decision Rewrite(string source)
    {
        if (!Has(source, @"\bmodule\s+NightCityRemembers\b"))
            return new("REFUSED", "NCR_MODULE_MISSING", null, 0);
        var owner = FindClass(source, "NCRSystem");
        if (owner is null) return new("REFUSED", "NCR_SYSTEM_AMBIGUOUS", null, 0);

        var ready = FindMethod(source, owner, "OnSessionReady");
        var end = FindMethod(source, owner, "OnSessionEnd");
        var detach = FindMethod(source, owner, "OnDetach");
        var tick = FindMethod(source, owner, "OnTick");
        var original = FindMethod(source, owner, "ScheduleTick");
        if (ready is null || end is null || detach is null || tick is null)
            return new("REFUSED", "NCR_REQUIRED_LIFECYCLE_MISSING", null, 0);

        // Accept the fully working historical scheduler instead of creating a
        // competing job. Partial historical conversion cannot safely gain a
        // second callback without knowing which job owns the original clock.
        if (Has(source, @"\bclass\s+NCRTickJob\s+extends\s+ScheduledJob\b"))
            return Has(source, @"\bfunc\s+StartTickJob\s*\(") &&
                   Has(source, @"\bfunc\s+StopTickJob\s*\(") &&
                   Has(source, @"\bUnregister\s*\(")
                ? new("COMPLETE", null, null, 0)
                : new("REFUSED", "NCR_HISTORICAL_JOB_PARTIAL_UNPROVEN", null, 0);

        string body(Block block) => source[(block.Open + 1)..block.Close];
        string readyBody = body(ready), endBody = body(end),
            detachBody = body(detach), tickBody = body(tick);
        if (original is null ||
            !Has(body(original), @"\bDelayCallback\s*\(") ||
            !Has(body(original), @"\bNCRTickCallback\b") ||
            !Has(readyBody, @"\bm_lastGameDay\s*=\s*-1") ||
            !Has(endBody, @"\bm_ready\s*=\s*false") ||
            !Has(tickBody, @"\bExpirePendingKills\s*\(") ||
            !Has(tickBody, @"\bRotateBuckets\s*\(") ||
            !Has(source, @"\bclass\s+NCRTickCallback\s+extends\s+DelayCallback\b"))
            return new("REFUSED", "NCR_MAINTENANCE_FLOW_INCOMPATIBLE", null, 0);

        string nl = NL(source);
        var edits = new List<Edit>();
        void Add(int start, int endAt, string text) =>
            edits.Add(new(start, endAt, text));

        if (!Has(source, @"\bimport\s+GRedRuntime\s*\.\s*\*"))
        {
            var module = Regex.Match(source, @"\bmodule\s+NightCityRemembers\s*\r?\n");
            if (!module.Success) return new("REFUSED", "NCR_MODULE_IMPORT_ANCHOR_MISSING", null, 0);
            Add(module.Index + module.Length, module.Index + module.Length,
                "import GRedRuntime.*" + nl);
        }

        if (!Has(source, @"\bclass\s+NCRGCOMaintenanceJob\b"))
        {
            Add(owner.Start, owner.Start, string.Join(nl, new[]
            {
                "public class NCRGCOMaintenanceJob extends ScheduledJob {",
                "  public let m_system: wref<NCRSystem>;",
                "  public func Execute(runtime: ref<Runtime>) -> Void {",
                "    if IsDefined(this.m_system) { this.m_system.OnTick(); };",
                "  }",
                "}",
                ""
            }) + nl);
        }
        else if (!Has(source, @"\bclass\s+NCRGCOMaintenanceJob\s+extends\s+ScheduledJob\b") ||
                 !Has(source, @"\bfunc\s+Execute\s*\(\s*runtime\s*:\s*ref\s*<\s*Runtime\s*>"))
            return new("REFUSED", "NCR_JOB_COLLISION", null, 0);

        foreach (var field in new[]
        {
            ("m_gcoScheduler", "wref<Scheduler>"),
            ("m_gcoTickJob", "ref<NCRGCOMaintenanceJob>"),
            ("m_gcoTickID", "Int32")
        })
        {
            var existing = Regex.Matches(source, @"\blet\s+" +
                Regex.Escape(field.Item1) + @"\s*:\s*(?<type>[^;]+);");
            if (existing.Count > 1 || (existing.Count == 1 &&
                string.Concat(existing[0].Groups["type"].Value.Where(c => !char.IsWhiteSpace(c))) !=
                string.Concat(field.Item2.Where(c => !char.IsWhiteSpace(c)))))
                return new("REFUSED", "NCR_FIELD_COLLISION:" + field.Item1, null, 0);
            if (existing.Count == 0)
                Add(owner.Open + 1, owner.Open + 1,
                    nl + "  private let " + field.Item1 + ": " + field.Item2 + ";");
        }

        if (!Has(source, @"\bfunc\s+GCOStartTick\s*\("))
        {
            string method = string.Join(nl, new[]
            {
                "  private func GCOStartTick() -> Void {",
                "    this.GCOStopTick();",
                "    let runtime = Runtime.Get(this.GetGameInstance());",
                "    if IsDefined(runtime) && runtime.IsReady() {",
                "      this.m_gcoScheduler = runtime.GetScheduler();",
                "      if IsDefined(this.m_gcoScheduler) {",
                "        this.m_gcoTickJob = new NCRGCOMaintenanceJob();",
                "        this.m_gcoTickJob.m_system = this;",
                "        this.m_gcoTickJob.Configure(n\"NCR_MAINTENANCE\", 30.0, true);",
                "        this.m_gcoTickID = this.m_gcoScheduler.Register(this.m_gcoTickJob);",
                "        if this.m_gcoTickID > 0 { return; };",
                "      };",
                "    };",
                "    this.m_gcoScheduler = null;",
                "    this.m_gcoTickJob = null;",
                "    this.m_gcoTickID = 0;",
                "    this.ScheduleTick();",
                "  }",
                ""
            }) + nl;
            Add(original.Start, original.Start, method);
        }
        else if (!Has(body(FindMethod(source, owner, "GCOStartTick") ?? original),
                  @"\bthis\s*\.\s*ScheduleTick\s*\(") ||
                 !Has(source, @"\bthis\s*\.\s*m_gcoScheduler\s*\.\s*Register\s*\("))
            return new("REFUSED", "NCR_START_PARTIAL_UNPROVEN", null, 0);

        if (!Has(source, @"\bfunc\s+GCOStopTick\s*\("))
        {
            string method = string.Join(nl, new[]
            {
                "  private func GCOStopTick() -> Void {",
                "    if IsDefined(this.m_gcoScheduler) && this.m_gcoTickID > 0 {",
                "      this.m_gcoScheduler.Unregister(this.m_gcoTickID);",
                "    };",
                "    this.m_gcoTickID = 0;",
                "    this.m_gcoTickJob = null;",
                "    this.m_gcoScheduler = null;",
                "    let delaySys = GameInstance.GetDelaySystem(this.GetGameInstance());",
                "    if IsDefined(delaySys) { delaySys.CancelCallback(this.m_tickDelayID); };",
                "  }",
                ""
            }) + nl;
            Add(original.Start, original.Start, method);
        }
        else if (!Has(body(FindMethod(source, owner, "GCOStopTick") ?? original),
                  @"\bUnregister\s*\(") ||
                 !Has(source, @"\bCancelCallback\s*\(\s*this\s*\.\s*m_tickDelayID"))
            return new("REFUSED", "NCR_STOP_PARTIAL_UNPROVEN", null, 0);

        var startCall = Regex.Matches(readyBody,
            @"\bthis\s*\.\s*ScheduleTick\s*\(\s*\)\s*;");
        bool started = Has(readyBody, @"\bthis\s*\.\s*GCOStartTick\s*\(");
        if (!started && startCall.Count == 1)
        {
            var m = startCall[0];
            Add(ready.Open + 1 + m.Index, ready.Open + 1 + m.Index + m.Length,
                "this.GCOStartTick();");
        }
        else if (!started || startCall.Count > 0)
            return new("REFUSED", "NCR_SESSION_START_PARTIAL_OR_DRIFTED", null, 0);

        if (!Has(endBody, @"\bthis\s*\.\s*GCOStopTick\s*\("))
            Add(end.Open + 1, end.Open + 1,
                nl + "    this.GCOStopTick();");
        if (!Has(detachBody, @"\bthis\s*\.\s*GCOStopTick\s*\("))
            Add(detach.Open + 1, detach.Open + 1,
                nl + "    this.GCOStopTick();");

        // Only the OnTick self-reschedule calls become conditional. The
        // original fallback remains untouched, including its 30s cadence.
        var calls = Regex.Matches(tickBody,
            @"\bthis\s*\.\s*ScheduleTick\s*\(\s*\)\s*;");
        foreach (Match m in calls)
            Add(tick.Open + 1 + m.Index, tick.Open + 1 + m.Index + m.Length,
                "if this.m_gcoTickID <= 0 { this.ScheduleTick(); };");
        if (calls.Count == 0 &&
            !Has(tickBody, @"\bm_gcoTickID\s*<=\s*0"))
            return new("REFUSED", "NCR_TICK_REQUEUE_MISSING", null, 0);
        if (calls.Count > 0 && Has(tickBody, @"\bm_gcoTickID\s*<=\s*0"))
            return new("REFUSED", "NCR_MIXED_TICK_REQUEUE_UNPROVEN", null, 0);

        if (edits.Count == 0) return new("COMPLETE", null, null, 0);
        var sorted = edits.OrderByDescending(x => x.Start).ThenByDescending(x => x.End).ToArray();
        for (int i = 1; i < sorted.Length; i++)
            if (sorted[i].End > sorted[i - 1].Start)
                return new("REFUSED", "NCR_EDITS_OVERLAP", null, 0);
        string output = source;
        foreach (var e in sorted)
            output = output[..e.Start] + e.Text + output[e.End..];

        if (!Has(output, @"\bfunc\s+GCOStartTick\s*\(") ||
            !Has(output, @"\bfunc\s+GCOStopTick\s*\(") ||
            !Has(output, @"\bclass\s+NCRGCOMaintenanceJob\s+extends\s+ScheduledJob\b") ||
            !Has(output, @"\bm_gcoTickID\s*<=\s*0") ||
            !Has(output, @"\bthis\s*\.\s*m_gcoScheduler\s*\.\s*Unregister\s*\("))
            throw new InvalidDataException("NCR_SCHEDULER_POSTCONDITION_FAILED");
        foreach (Match m in Regex.Matches(source, @"(?:GCO|GRSP)-GENERIC:[A-Z0-9_]+"))
            if (!output.Contains(m.Value, StringComparison.Ordinal))
                throw new InvalidDataException("NCR_SCHEDULER_GENERIC_LOST");
        return new("APPLIED", null, output, edits.Count);
    }

    internal static Result Generate(string gameRoot, string outputRoot,
        string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot),
            output = Path.GetFullPath(outputRoot),
            scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) || Directory.Exists(output) ||
            File.Exists(output) || Inside(output, game))
            throw new InvalidDataException("NCR_SCHEDULER_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) || Inside(basePreviewRoot, game) ||
             Inside(basePreviewRoot, output) || Inside(output, basePreviewRoot)))
            throw new InvalidDataException("NCR_SCHEDULER_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string hash = admission.SourceStackSha256 ??
            throw new InvalidDataException("NCR_SCHEDULER_SOURCE_HASH_MISSING");
        string status = "NOT_APPLICABLE";
        string? relative = null, reason = null, rewritten = null;
        int edits = 0;
        string owner = Path.Combine(scripts, "NightCityRemembers");
        if (Directory.Exists(owner))
        {
            var candidates = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories).Where(f =>
                Has(File.ReadAllText(f, Encoding.UTF8),
                    @"\bclass\s+NCRSystem\s+extends\s+ScriptableSystem\b"))
                .ToArray();
            if (candidates.Length != 1)
            {
                status = "REFUSED";
                reason = candidates.Length == 0 ?
                    "NCR_SYSTEM_SOURCE_MISSING" : "NCR_SYSTEM_SOURCE_AMBIGUOUS";
            }
            else if (!RuntimeAbi(scripts))
            {
                status = "REFUSED";
                reason = "NCR_SCHEDULER_RUNTIME_ABI_MISSING";
            }
            else
            {
                string file = candidates[0];
                if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("NCR_SOURCE_LINK_UNSAFE");
                relative = "r6/scripts/" + Path.GetRelativePath(scripts, file)
                    .Replace('\\', '/');
                string source = File.ReadAllText(file, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string staged = Path.GetFullPath(Path.Combine(basePreviewRoot,
                        relative.Replace('/', Path.DirectorySeparatorChar)));
                    if (!Inside(staged, basePreviewRoot))
                        throw new InvalidDataException("NCR_PREVIEW_ESCAPE");
                    if (File.Exists(staged)) source = File.ReadAllText(staged, Encoding.UTF8);
                }
                var decision = Rewrite(source);
                status = decision.Status; reason = decision.Reason;
                rewritten = decision.Changed; edits = decision.Edits;
            }
        }
        if (!string.Equals(hash, admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("NCR_SCHEDULER_SOURCE_CHANGED");

        Directory.CreateDirectory(output);
        if (rewritten is not null && relative is not null)
        {
            string dst = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(dst, output) ||
                !dst.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("NCR_SCHEDULER_TARGET_UNSAFE");
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllText(dst, rewritten, new UTF8Encoding(false));
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
        Path.GetFullPath(child).StartsWith(Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase);
}
