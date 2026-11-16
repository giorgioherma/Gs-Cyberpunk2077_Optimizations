using System.Globalization;

namespace GRedscript.Resolver;

/// <summary>
/// Read-only R5 interpretation of the source-owned GRSP post-F11 proxy CSV.
/// Never converts unresolved VM dispatch into a proven callee or rewrite authority.
/// </summary>
internal static class ProxyAttributionEvidence
{
    private static readonly string[] RequiredFields =
    [
        "capture_id", "stable_callsite_id", "source_owner", "source_function",
        "source_path", "source_line", "call_kind", "target", "target_resolution",
        "attribution_status", "outgoing_edges", "outgoing_edges_per_sec",
        "exclusive_instrumented_ms", "observed_inclusive_ms", "max_inclusive_ms",
        "active_frames", "max_edges_per_frame", "sparse_observed_paths",
        "sparse_observed_max_depth", "sparse_observed_parent",
        "hot_paths_dropped_global", "receiver_proven",
        "callback_entries_known", "causality_proven"
    ];

    internal static object Interpret(
        IReadOnlyList<Dictionary<string, string>>? proxyRows,
        IReadOnlyList<Dictionary<string, string>> callsites,
        SourceAdmission source)
    {
        if (proxyRows is null)
            return new
            {
                status = "NOT_CAPTURED",
                evidenceOwner = "GRSP_NATIVE",
                scope = "PROXY_TARGETS_ONLY",
                generationAuthorized = false,
                receiverIdentitiesKnown = false,
                callbackEntriesKnown = false,
                causalAttributionKnown = false,
                nextProof = "Capture native RSP_ProxyAttribution.csv from a complete F11 session"
            };

        var sourceCallsites = new Dictionary<string, List<Dictionary<string, string>>>(
            StringComparer.Ordinal);
        foreach (var row in callsites)
        {
            string id = Field(row, "stable_callsite_id");
            if (!sourceCallsites.TryGetValue(id, out var repeated))
                sourceCallsites.Add(id, repeated = []);
            repeated.Add(row);
        }

        var ranked = new List<ProxyObservation>(proxyRows.Count);
        var classifications = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in proxyRows)
        {
            foreach (string column in RequiredFields)
                _ = Field(row, column);

            string id = Field(row, "stable_callsite_id");
            string captureId = Field(row, "capture_id");
            string owner = Field(row, "source_owner");
            string method = Field(row, "source_function");
            string path = Field(row, "source_path");
            int line = Int(row, "source_line");
            string kind = Field(row, "call_kind");
            string target = Field(row, "target");
            string resolution = Field(row, "target_resolution");
            string label = Field(row, "attribution_status");
            long edges = NonnegativeLong(row, "outgoing_edges");
            double rate = NonnegativeNumber(row, "outgoing_edges_per_sec");
            double exclusive = NonnegativeNumber(row, "exclusive_instrumented_ms");
            double inclusive = NonnegativeNumber(row, "observed_inclusive_ms");
            _ = NonnegativeNumber(row, "max_inclusive_ms");
            _ = NonnegativeLong(row, "active_frames");
            _ = NonnegativeLong(row, "max_edges_per_frame");
            long sparse = NonnegativeLong(row, "sparse_observed_paths");
            _ = NonnegativeLong(row, "sparse_observed_max_depth");
            long dropped = NonnegativeLong(row, "hot_paths_dropped_global");
            if (line <= 0 || (!target.Contains("proxy$", StringComparison.Ordinal) &&
                !target.Contains("wrapper$", StringComparison.Ordinal)))
                throw new InvalidDataException("PROXY_EVIDENCE_SHAPE_INVALID: source line or target");
            if (Field(row, "receiver_proven") != "false" ||
                Field(row, "callback_entries_known") != "false" ||
                Field(row, "causality_proven") != "false")
                throw new InvalidDataException("PROXY_EVIDENCE_UNSUPPORTED_PROOF_CLAIM");

            string expectedLabel = resolution switch
            {
                "UNRESOLVED_VIRTUAL" => "UNRESOLVED_VIRTUAL_RECEIVER",
                "UNRESOLVED_STATIC" => "UNRESOLVED_STATIC_TARGET",
                _ => "NAMED_TARGET_RECEIVER_NOT_PROVEN"
            };
            if (label != expectedLabel)
                throw new InvalidDataException("PROXY_EVIDENCE_ATTRIBUTION_MISMATCH");
            if (!sourceCallsites.TryGetValue(id, out var candidates) ||
                !candidates.Any(c =>
                    Field(c, "capture_id") == captureId &&
                    Field(c, "owner") == owner &&
                    Field(c, "source_function") == method &&
                    Field(c, "source_path") == path &&
                    Int(c, "source_line") == line &&
                    Field(c, "call_kind") == kind &&
                    Field(c, "target") == target &&
                    Field(c, "target_resolution") == resolution &&
                    NonnegativeLong(c, "calls") == edges))
                throw new InvalidDataException(
                    "PROXY_EVIDENCE_CALLSITE_MISMATCH: native proxy row has no original callsite");

            // Source identity here is diagnostic, not method receiver or semantic proof.
            var match = source.Check(path, method, line);
            classifications[label] = classifications.GetValueOrDefault(label) + 1;
            ranked.Add(new(id, owner, method, target, label, edges, rate, exclusive,
                inclusive, sparse, dropped, match.Status));
        }

        return new
        {
            status = "CAPTURED_UNRESOLVED",
            evidenceOwner = "GRSP_NATIVE",
            scope = "PROXY_TARGETS_ONLY",
            generationAuthorized = false,
            receiverIdentitiesKnown = false,
            callbackEntriesKnown = false,
            causalAttributionKnown = false,
            interpretation = "Outgoing VM invokes and measured callsite work; sparse paths are threshold-limited, not a complete callback census",
            proxyCallsiteRows = proxyRows.Count,
            attributionStatusCounts = classifications,
            rankedProxyCallsites = ranked
                .OrderByDescending(x => x.ExclusiveInstrumentedMs)
                .ThenByDescending(x => x.OutgoingEdges)
                .ThenBy(x => x.StableCallsiteId, StringComparer.Ordinal)
                .Take(100).ToArray(),
            nextProof = "Resolve actual virtual receiver and downstream work with source-owned, bounded GRSP instrumentation"
        };
    }

    private static string Field(Dictionary<string, string> row, string column) =>
        row.TryGetValue(column, out string? value)
            ? value : throw new InvalidDataException("PROXY_EVIDENCE_FIELD_MISSING: " + column);

    private static long NonnegativeLong(Dictionary<string, string> row, string column)
    {
        if (!long.TryParse(Field(row, column), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out long value) || value < 0)
            throw new InvalidDataException("PROXY_EVIDENCE_INVALID_INTEGER: " + column);
        return value;
    }

    private static int Int(Dictionary<string, string> row, string column) =>
        int.TryParse(Field(row, column), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out int value)
            ? value : throw new InvalidDataException("PROXY_EVIDENCE_INVALID_LINE");

    private static double NonnegativeNumber(Dictionary<string, string> row, string column)
    {
        if (!double.TryParse(Field(row, column), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double value) ||
            !double.IsFinite(value) || value < 0)
            throw new InvalidDataException("PROXY_EVIDENCE_INVALID_NUMBER: " + column);
        return value;
    }

    private sealed record ProxyObservation(
        string StableCallsiteId, string Owner, string SourceFunction,
        string Target, string AttributionStatus, long OutgoingEdges,
        double OutgoingEdgesPerSec, double ExclusiveInstrumentedMs,
        double ObservedInclusiveMs, long SparseObservedPaths,
        long HotPathsDroppedGlobal, string SourceMatch);
}
