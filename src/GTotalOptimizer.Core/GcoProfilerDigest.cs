using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace GTotalOptimizer.Core;

/// <summary>
/// Small comparative presentation made from the two authoritative profiler
/// summaries and GCO's already verified common-clock 50ms chronology.
/// No profiler owns or receives edits to its own reporting or captured data.
/// </summary>
internal sealed record GcoProfilerSpike(
    double TimeMs, string Domain, string Owner, double ExclusiveMs);

internal sealed record GcoProfilerDigestOutput(
    string Html, object Data, GcoProfilerSpike[] SpikeMarkers);

internal static class GcoProfilerDigest
{
    private sealed record Row(string Name, double WorkPerSecond, double CallsPerSecond);
    private sealed record PresentWindow(double TimeMs, double IntervalMs,
        double? CetMs, double? RedMs, double? CpuMs, double? GpuMs);

    internal static GcoProfilerDigestOutput Build(
        JsonElement cet, JsonElement red, IReadOnlyList<double> presentIntervals,
        string timelineJson, IReadOnlyList<GcoProfilerSpike> redSpikes)
    {
        var cetCapture = Item(cet, "capture");
        var redCapture = Item(red, "capture");
        var cetOwners = Rank(Item(cet, "topOwners"), "owner",
            "exclusiveMsPerSecond", "callsPerSecond", 8);
        var redOwners = Rank(Item(red, "topOwners"), "owner",
            "exclusiveMsPerSecond", "callsPerSecond", 8);
        var cetCallbacks = RankCallbacks(Item(cet, "callbackHotspots"), 7);
        var redFunctions = RankFunctions(Item(red, "topFunctions"), 7);
        var gc = Item(cet, "collectorGc");
        var heap = Item(cet, "luaHeap");
        var allocation = Item(Item(cet, "phase3PressureScorecard"), "allocation");
        var cetFindings = Item(cet, "findings");
        var redFindings = Item(red, "findings");
        var redHealth = Item(red, "health");

        var validIntervals = presentIntervals.Where(x => double.IsFinite(x) && x > 0)
            .OrderBy(x => x).ToArray();
        double? Percentile(double q) => validIntervals.Length == 0 ? null :
            validIntervals[Math.Clamp((int)Math.Ceiling(q * validIntervals.Length) - 1,
                0, validIntervals.Length - 1)];
        var p95 = Percentile(.95);
        var p99 = Percentile(.99);
        var worst = Percentile(1);

        var presentCandidates = new List<PresentWindow>();
        using (var timeline = JsonDocument.Parse(timelineJson))
            foreach (var item in timeline.RootElement.EnumerateArray())
            {
                double? interval = Number(item, "presentIntervalMaxMs");
                double? t = Number(item, "timeMs");
                if (interval is null || t is null || interval <= 0) continue;
                presentCandidates.Add(new PresentWindow(t.Value, interval.Value,
                    Number(item, "cetExclusiveMeasuredMs"),
                    Number(item, "redscriptExclusiveInstrumentedMs"),
                    Number(item, "cpuBusyMaxMs"), Number(item, "gpuBusyMaxMs")));
            }

        // Cluster nearby 50ms buckets so one long hitch does not fill the
        // entire shortlist. Each displayed datum remains the real bucket value.
        var worstWindows = new List<PresentWindow>();
        foreach (var row in presentCandidates.OrderByDescending(x => x.IntervalMs))
        {
            if (worstWindows.Any(x => Math.Abs(x.TimeMs - row.TimeMs) < 500))
                continue;
            worstWindows.Add(row);
            if (worstWindows.Count == 7) break;
        }

        var spikes = new List<GcoProfilerSpike>();
        var cetSpikes = Item(cet, "spikes");
        if (cetSpikes.ValueKind == JsonValueKind.Array)
            foreach (var item in cetSpikes.EnumerateArray())
            {
                var time = Number(item, "startMs");
                var cost = Number(item, "exclusiveMs");
                if (time is null || cost is null || time < 0 || cost < 0) continue;
                spikes.Add(new GcoProfilerSpike(time.Value, "CET",
                    Text(item, "owner"), cost.Value));
            }
        spikes = spikes.OrderByDescending(x => x.ExclusiveMs).Take(8).ToList();
        spikes.AddRange(redSpikes
            .Where(x => x.TimeMs >= 0 && double.IsFinite(x.TimeMs))
            .OrderByDescending(x => x.ExclusiveMs).Take(8));
        spikes = spikes.OrderBy(x => x.TimeMs).ToList();

        var html = new StringBuilder();
        html.AppendLine("<h2>At a glance</h2>");
        html.AppendLine("<p class=\"muted\">Same capture, two profiling domains. These are independently instrumented workloads, <strong>not additive CPU usage</strong>.</p>");
        html.AppendLine("<section class=\"digest-kpis\">");
        Metric(html, "CET / Lua workload",
            F(Number(cetCapture, "exclusiveMsPerSecond"), 1) + " ms/s",
            F(Number(cetCapture, "measuredCalls"), 0) + " callback calls measured");
        Metric(html, "REDscript / VM workload",
            F(Number(redCapture, "exclusiveInstrumentedMsPerSecond")
                ?? Number(redCapture, "exclusiveMsPerSecond"), 1) + " ms/s",
            F(Number(redCapture, "measuredCalls"), 0) + " outgoing call edges measured");
        Metric(html, "PresentMon / p95 interval",
            F(p95, 1) + " ms", "p99 " + F(p99, 1) + " ms · worst " + F(worst, 1) + " ms");
        Metric(html, "Common capture",
            F(Number(cetCapture, "durationSeconds"), 1) + " s",
            "REDscript " + F(Number(redCapture, "durationSeconds"), 1) + " s · same F11 session");
        html.AppendLine("</section>");

        html.AppendLine("<h2>What stands out in the original profiler reports?</h2>");
        html.AppendLine("<div class=\"side-by-side\">");
        Findings(html, "CET / measured events", cetFindings);
        Findings(html, "REDscript / instrumented events", redFindings);
        html.AppendLine("</div>");

        html.AppendLine("<h2>Which mods are doing the work?</h2>");
        html.AppendLine("<div class=\"side-by-side\">");
        RankedTable(html, "CET / busiest Lua owners", "Measured exclusive ms/s",
            cetOwners, "CET", "owner");
        RankedTable(html, "REDscript / busiest VM owners", "Instrumented exclusive ms/s",
            redOwners, "RED", "owner");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"muted small\">These rankings are within each profiler's measured scope. The numbers are useful for prioritizing within a column, not calculating a combined CPU total.</p>");

        html.AppendLine("<h2>What is keeping them busy?</h2>");
        html.AppendLine("<div class=\"side-by-side\">");
        RankedTable(html, "CET / callback families", "Measured exclusive ms/s",
            cetCallbacks, "CET", "callback");
        RankedTable(html, "REDscript / hottest functions", "Instrumented exclusive ms/s",
            redFunctions, "RED", "function");
        html.AppendLine("</div>");
        html.AppendLine("<p class=\"muted small\">CET groups registrations by callback type and target. REDscript lists individual hot functions; these are intentionally different but complementary views.</p>");

        html.AppendLine("<h2>Runtime pressure and health</h2>");
        html.AppendLine("<div class=\"side-by-side\">");
        html.AppendLine("<section class=\"digest-panel\"><h3>CET / Lua memory and collection</h3><dl class=\"facts\">");
        Fact(html, "Positive LuaJIT allocation",
            F(Number(allocation, "positiveAllocatedMiBPerSecond"), 1) + " MiB/s");
        Fact(html, "Allocation operations",
            F(Number(allocation, "allocationRequestsPerSecond"), 0) + " /s");
        Fact(html, "Observed LuaJIT GC", F(Number(gc, "collectorMsPerSecond"), 1) + " ms/s");
        Fact(html, "Completed collection cycles", F(Number(gc, "completedCycles"), 0));
        Fact(html, "Heap observations", F(Number(heap, "minHeapMiB"), 1) + "–" +
            F(Number(heap, "maxHeapMiB"), 1) + " MiB");
        html.AppendLine("</dl><p class=\"muted small\">Heap range is observed usage, not evidence of leaked memory. Collector timing can overlap CET callback timing.</p></section>");
        html.AppendLine("<section class=\"digest-panel\"><h3>REDscript / script-frame distribution</h3><dl class=\"facts\">");
        Fact(html, "Average instrumented script work / frame",
            F(Number(redCapture, "averageScriptMsPerFrame"), 1) + " ms");
        Fact(html, "p95 / p99 instrumented script work",
            F(Number(redCapture, "p95ScriptMsPerFrame"), 1) + " / " +
            F(Number(redCapture, "p99ScriptMsPerFrame"), 1) + " ms");
        Fact(html, "Profiler evidence quality",
            Text(redHealth, "frameQuality") == "GOOD" &&
            True(redHealth, "shardMergeOk") ? "GOOD · shards merged" : "Review capture health");
        html.AppendLine("</dl><p class=\"muted small\">Instrumented VM work per script frame is not an on-screen frame interval. Concurrent/nested work and profiler semantics prevent direct FPS conversion.</p></section>");
        html.AppendLine("</div>");

        html.AppendLine("<h2>Notable moments on the shared timeline</h2>");
        html.AppendLine("<p class=\"muted\">Largest <em>present intervals</em> in distinct 50 ms windows, with measured CET and REDscript work in those same coarse windows. Select a time to inspect it in the three-track graph.</p>");
        html.AppendLine("<div class=\"digest-panel table-scroll\"><table class=\"digest-table\"><thead><tr><th>Time</th><th>PresentMon worst interval</th><th>CET work / bucket</th><th>REDscript work / bucket</th><th>CPU busy max</th></tr></thead><tbody>");
        foreach (var row in worstWindows)
        {
            html.Append("<tr class=\"jump-row\" data-jump=\"")
                .Append(row.TimeMs.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("\"><td>").Append(F(row.TimeMs / 1000, 2)).Append(" s</td><td>")
                .Append(F(row.IntervalMs, 1)).Append(" ms</td><td>")
                .Append(F(row.CetMs, 1)).Append(" ms</td><td>")
                .Append(F(row.RedMs, 1)).Append(" ms</td><td>")
                .Append(F(row.CpuMs, 1)).AppendLine(" ms</td></tr>");
        }
        if (worstWindows.Count == 0)
            html.AppendLine("<tr><td colspan=\"5\">PresentMon chronology was not captured; no correlation is claimed.</td></tr>");
        html.AppendLine("</tbody></table></div>");
        html.AppendLine("<p class=\"muted small\">These rows show time coincidence only: a heavy script bucket beside a long present interval does not establish which code caused the delay.</p>");

        if (spikes.Count > 0)
        {
            html.AppendLine("<h3>Profiler-recorded spike markers</h3><div class=\"side-by-side\">");
            SpikeTable(html, "CET / slow recorded callbacks", spikes.Where(x => x.Domain == "CET"));
            SpikeTable(html, "REDscript / slow recorded calls", spikes.Where(x => x.Domain == "REDscript"));
            html.AppendLine("</div><p class=\"muted small\">Markers are measured profiler events at their source clocks. Click a row to zoom into the shared F11 timeline. A marker near a PresentMon hitch is not automatic attribution.</p>");
        }

        object facts = new
        {
            captureSeconds = Number(cetCapture, "durationSeconds"),
            cetMeasuredExclusiveMsPerSecond = Number(cetCapture, "exclusiveMsPerSecond"),
            redscriptExclusiveInstrumentedMsPerSecond =
                Number(redCapture, "exclusiveInstrumentedMsPerSecond"),
            presentMonP95Ms = p95, presentMonP99Ms = p99, presentMonWorstMs = worst,
            cetTopOwners = cetOwners, redscriptTopOwners = redOwners,
            cetCallbackFamilies = cetCallbacks, redscriptTopFunctions = redFunctions,
            worstPresentWindows = worstWindows,
            cetCollectorMsPerSecond = Number(gc, "collectorMsPerSecond"),
            cetPositiveAllocatedMiBPerSecond = Number(allocation, "positiveAllocatedMiBPerSecond"),
            cetAllocationRequestsPerSecond = Number(allocation, "allocationRequestsPerSecond"),
            redscriptFrameP99InstrumentedMs = Number(redCapture, "p99ScriptMsPerFrame"),
            relativeWorkloadDomainsNotAdditive = true,
            timeCoincidenceDoesNotProveCausation = true
        };
        return new(html.ToString(), facts, spikes.ToArray());
    }

    private static void Findings(StringBuilder html, string title, JsonElement findings)
    {
        html.Append("<section class=\"digest-panel\"><h3>").Append(H(title))
            .AppendLine("</h3>");
        if (findings.ValueKind != JsonValueKind.Array)
        {
            html.AppendLine("<p class=\"muted\">No source-owned findings captured.</p></section>");
            return;
        }
        int shown = 0;
        foreach (var item in findings.EnumerateArray())
        {
            if (shown == 3) break;
            string name = Text(item, "Title");
            string evidence = Text(item, "Evidence");
            if (name.Length == 0 || evidence.Length == 0) continue;
            html.Append("<div class=\"finding\"><strong>").Append(H(name))
                .Append("</strong><p>").Append(H(evidence))
                .AppendLine("</p></div>");
            shown++;
        }
        html.AppendLine("</section>");
    }

    private static void SpikeTable(StringBuilder html, string title,
        IEnumerable<GcoProfilerSpike> rows)
    {
        html.Append("<section class=\"digest-panel table-scroll\"><h3>")
            .Append(H(title))
            .AppendLine("</h3><table class=\"digest-table\"><thead><tr><th>At</th><th>Owner</th><th>Exclusive</th></tr></thead><tbody>");
        foreach (var row in rows.OrderByDescending(x => x.ExclusiveMs))
        {
            html.Append("<tr class=\"jump-row\" data-jump=\"")
                .Append(row.TimeMs.ToString("0.###", CultureInfo.InvariantCulture))
                .Append("\"><td>")
                .Append(F(row.TimeMs / 1000, 2)).Append(" s</td><td title=\"")
                .Append(H(row.Owner)).Append("\">")
                .Append(H(row.Owner)).Append("</td><td>")
                .Append(F(row.ExclusiveMs, 1)).AppendLine(" ms</td></tr>");
        }
        html.AppendLine("</tbody></table></section>");
    }

    private static void RankedTable(StringBuilder html, string title,
        string measure, IEnumerable<Row> data, string domain, string kind)
    {
        html.Append("<section class=\"digest-panel table-scroll\"><h3>")
            .Append(H(title)).Append("</h3><table class=\"digest-table\"><thead><tr><th>Owner / ")
            .Append(kind == "owner" ? "mod" : kind)
            .Append("</th><th>").Append(H(measure))
            .AppendLine("</th><th>Calls/s</th></tr></thead><tbody>");
        foreach (var item in data)
        {
            html.Append("<tr><td title=\"").Append(H(item.Name)).Append("\">")
                .Append(H(item.Name)).Append("</td><td>")
                .Append(F(item.WorkPerSecond, 2)).Append("</td><td>")
                .Append(F(item.CallsPerSecond, 0)).AppendLine("</td></tr>");
        }
        html.AppendLine("</tbody></table></section>");
    }

    private static Row[] Rank(JsonElement items, string name, string work,
        string calls, int limit)
    {
        if (items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray()
            .Select(x => new Row(Text(x, name), Number(x, work) ?? 0,
                Number(x, calls) ?? 0))
            .Where(x => x.Name.Length > 0).OrderByDescending(x => x.WorkPerSecond)
            .Take(limit).ToArray();
    }

    private static Row[] RankCallbacks(JsonElement items, int limit)
    {
        if (items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray().Select(x => new Row(
            Text(x, "kind") + " · " + Text(x, "target"),
            Number(x, "exclusiveMsPerSecond") ?? 0,
            Number(x, "callsPerSecond") ?? 0))
            .OrderByDescending(x => x.WorkPerSecond).Take(limit).ToArray();
    }

    private static Row[] RankFunctions(JsonElement items, int limit)
    {
        if (items.ValueKind != JsonValueKind.Array) return [];
        return items.EnumerateArray().Select(x => new Row(
            Text(x, "owner") + " · " + Text(x, "function"),
            Number(x, "exclusiveMsPerSecond") ?? 0,
            Number(x, "callsPerSecond") ?? 0))
            .OrderByDescending(x => x.WorkPerSecond).Take(limit).ToArray();
    }

    private static void Metric(StringBuilder html, string label, string value, string sub)
    {
        html.Append("<div class=\"digest-metric\"><span>").Append(H(label))
            .Append("</span><strong>").Append(H(value))
            .Append("</strong><small>").Append(H(sub)).AppendLine("</small></div>");
    }

    private static void Fact(StringBuilder html, string title, string value)
    {
        html.Append("<dt>").Append(H(title)).Append("</dt><dd>")
            .Append(H(value)).AppendLine("</dd>");
    }

    private static JsonElement Item(JsonElement node, string key) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(key, out var value)
            ? value : default;

    private static string Text(JsonElement node, string key) =>
        Item(node, key) is var value && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : "";

    private static bool True(JsonElement node, string key) =>
        Item(node, key).ValueKind == JsonValueKind.True;

    private static double? Number(JsonElement node, string key)
    {
        var v = Item(node, key);
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n) &&
            double.IsFinite(n)) return n;
        return null;
    }

    private static string F(double? n, int decimals) =>
        n is double value && double.IsFinite(value)
            ? value.ToString("N" + decimals, CultureInfo.InvariantCulture)
            : "NOT_CAPTURED";

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
