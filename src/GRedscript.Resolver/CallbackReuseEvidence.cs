using System.Globalization;

namespace GRedscript.Resolver;

/// <summary>
/// Read-only GCO consumer of GRSP's post-F11 cross-callback read pressure.
/// Never treats outgoing calls, same-frame repetition or shared targets as
/// proven callback entries, pure getters, unchanged values or safe caches.
/// </summary>
internal static class CallbackReuseEvidence
{
    private static readonly HashSet<string> Classes = new(StringComparer.Ordinal)
    {
        "EVENT_ARGUMENT_READ",
        "SHARED_SYSTEM_OR_CONTEXT_READ",
        "PLAYER_LIFETIME_SENSITIVE_READ",
        "READ_LIKE_PURITY_UNKNOWN"
    };

    internal static object Interpret(
        IReadOnlyList<Dictionary<string, string>>? rows,
        IReadOnlyList<Dictionary<string, string>> callbackTargets)
    {
        if (rows is null)
            return new
            {
                status = "NOT_CAPTURED",
                evidenceOwner = "GRSP_NATIVE",
                generationAuthorized = false,
                callbackEntriesKnown = false,
                cacheSafetyKnown = false,
                nextProof = "Capture post-F11 RSP_CallbackReuse.csv"
            };

        var output = new List<ReadCandidate>(rows.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            string id = Field(row, "read_target_id");
            string capture = Field(row, "capture_id");
            string kind = Field(row, "call_kind");
            string target = Field(row, "target");
            string classification = Field(row, "read_candidate_class");
            string resolution = Field(row, "target_resolution");
            string frameStatus = Field(row, "frame_evidence_status");
            if (id.Length != 21 || !id.StartsWith("RSPR-", StringComparison.Ordinal) ||
                !id[5..].All(Uri.IsHexDigit) || !seen.Add(id) ||
                kind is not ("static" or "virtual") ||
                string.IsNullOrWhiteSpace(target) ||
                resolution.StartsWith("UNRESOLVED", StringComparison.Ordinal) ||
                !Classes.Contains(classification) ||
                frameStatus is not ("CAPTURED_FRAME_OVERLAP" or "TRUNCATED_NOT_ZERO_WORK") ||
                Field(row, "callback_entries_proven") != "false" ||
                Field(row, "cache_safe_proven") != "false")
                throw new InvalidDataException("CALLBACK_REUSE_UNSUPPORTED_PROOF_OR_SCHEMA");

            long callbacks = Nonnegative(row, "callback_source_functions");
            long owners = Nonnegative(row, "source_owners");
            long sites = Nonnegative(row, "source_callsites");
            long edges = Nonnegative(row, "outgoing_edges");
            double rate = Number(row, "edges_per_sec");
            double exclusive = Number(row, "exclusive_instrumented_ms");
            long frames = Nonnegative(row, "observed_active_frames");
            long extra = Nonnegative(row, "same_frame_extra_edges");
            long multiCallback = Nonnegative(row, "multi_callback_source_frames");
            long multiOwner = Nonnegative(row, "multi_owner_frames");
            long max = Nonnegative(row, "max_edges_per_frame");
            long dropped = Nonnegative(row, "dropped_frame_rows");
            if (callbacks < 1 || owners < 1 || sites < callbacks ||
                edges < 300 || extra > edges || multiOwner > multiCallback ||
                multiCallback > frames || max > edges ||
                (dropped > 0) != (frameStatus == "TRUNCATED_NOT_ZERO_WORK"))
                throw new InvalidDataException("CALLBACK_REUSE_INVALID_COUNTS");

            // Multiple runtime target pointers may have the same name, so the
            // source-owned aggregate must be <= the matching named callsite
            // population, not necessarily equal. Never infer receiver identity.
            var matching = callbackTargets.Where(r =>
                Field(r, "capture_id") == capture &&
                Field(r, "call_kind") == kind &&
                Field(r, "target") == target).ToArray();
            long available = 0;
            foreach (var item in matching)
                available = checked(available + Nonnegative(item, "outgoing_edges"));
            if (available < edges || matching.Length < sites)
                throw new InvalidDataException("CALLBACK_REUSE_NATIVE_CALLSITE_MISMATCH");

            output.Add(new ReadCandidate(id, target, classification,
                callbacks, owners, sites, edges, rate, exclusive, frames,
                extra, multiCallback, multiOwner, max,
                frameStatus, dropped));
        }

        return new
        {
            status = "CAPTURED_READ_LIKE_ONLY",
            evidenceOwner = "GRSP_NATIVE",
            evidenceScope = "OUTGOING_CALL_EDGES_AND_SAME_FRAME_OVERLAP_NOT_CALLBACK_ENTRIES",
            generationAuthorized = false,
            callbackEntriesKnown = false,
            cacheSafetyKnown = false,
            unchangedArgumentsKnown = false,
            invalidationCoverageKnown = false,
            candidateTargets = output.Count,
            rankedReadTargets = output.OrderByDescending(x => x.EdgesPerSec)
                .ThenByDescending(x => x.SameFrameExtraEdges)
                .Take(100).ToArray(),
            nextProof = "Read live sources: prove getter purity, argument identity, receiver lifetime, invalidation and original callback ordering before any generic rewrite"
        };
    }

    private static string Field(Dictionary<string, string> row, string key) =>
        row.TryGetValue(key, out string? value) && value is not null ? value :
        throw new InvalidDataException("CALLBACK_REUSE_MISSING_FIELD: " + key);

    private static long Nonnegative(Dictionary<string, string> row, string key) =>
        long.TryParse(Field(row, key), NumberStyles.Integer,
            CultureInfo.InvariantCulture, out long n) && n >= 0 ? n :
        throw new InvalidDataException("CALLBACK_REUSE_INVALID_INTEGER: " + key);

    private static double Number(Dictionary<string, string> row, string key) =>
        double.TryParse(Field(row, key), NumberStyles.Float,
            CultureInfo.InvariantCulture, out double n) && double.IsFinite(n) && n >= 0 ? n :
        throw new InvalidDataException("CALLBACK_REUSE_INVALID_NUMBER: " + key);

    private sealed record ReadCandidate(
        string ReadTargetId, string Target, string ReadCandidateClass,
        long CallbackSourceFunctions, long SourceOwners, long SourceCallsites,
        long OutgoingEdges, double EdgesPerSec, double ExclusiveInstrumentedMs,
        long ObservedActiveFrames, long SameFrameExtraEdges,
        long MultiCallbackSourceFrames, long MultiOwnerFrames,
        long MaxEdgesPerFrame, string FrameEvidenceStatus, long DroppedFrameRows);
}
