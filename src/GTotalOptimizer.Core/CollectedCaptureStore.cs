using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

public sealed record CollectedFile(string Path, long Bytes, string Sha256);
public sealed record CollectedOwner(string SourceArchive, List<CollectedFile> Files);
public sealed record CollectedCaptureIndex(
    string SessionId, string GameRoot, CollectedOwner? Cet, CollectedOwner? Redscript,
    string? FrameTimeCsv, bool Restored = false, List<string>? Skipped = null,
    CollectedFile? FrameTimeEvidence = null, bool CollectionAttempted = false,
    bool PresentMonStopVerified = false,
    bool CetReadyBeforeAttempt = false, bool RedscriptReadyBeforeAttempt = false)
{
    public bool HasAny => Cet is not null || Redscript is not null || FrameTimeCsv is not null;
}

public static class CollectedCaptureStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static void CheckId(string id)
    {
        if (id.Length != 32 || id.Any(c => !Uri.IsHexDigit(c)))
            throw new InvalidDataException("Invalid GCO capture session identity.");
    }
    private static bool IsDatedSessionName(string name, string id)
    {
        const string prefix = "GCO-Capture-";
        const string format = "yyyyMMdd-HHmmss";
        return name.Length == prefix.Length + format.Length + 1 + 32 &&
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
            name[prefix.Length + format.Length] == '-' &&
            name.EndsWith(id, StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParseExact(name.Substring(prefix.Length, format.Length),
                format, CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static bool IsReadableSessionName(string name) =>
        name.StartsWith("GCO-Capture-", StringComparison.OrdinalIgnoreCase) &&
        name.Length == "GCO-Capture-".Length + "ddMMyyyy_HHmmss".Length &&
        DateTime.TryParseExact(name["GCO-Capture-".Length..],
            "ddMMyyyy_HHmmss", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out _);

    // New session folders carry a human timestamp only. collection.json is
    // created at launch with the immutable ID/game root, so the real identity
    // can be recovered after an app restart or verified profiler restore.
    // Read old GUID-only and yyyyMMdd-HHmmss-GUID capture folders unchanged.
    public static string SessionFolder(string root, string id)
    {
        CheckId(id);
        var results = Path.Combine(Path.GetFullPath(root), "RESULTS");
        var legacy = Path.Combine(results, "GCO-Capture-" + id);
        if (!Directory.Exists(results)) return legacy;
        var matches = new List<string>();
        if (Directory.Exists(legacy)) matches.Add(legacy);
        foreach (var dir in Directory.EnumerateDirectories(results, "GCO-Capture-*",
                     SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(dir);
            if (string.Equals(dir, legacy, StringComparison.OrdinalIgnoreCase))
                continue;
            if (IsDatedSessionName(name, id))
                matches.Add(dir);
            else if (IsReadableSessionName(name))
            {
                if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    continue;
                var index = Path.Combine(dir, "collection.json");
                if (!File.Exists(index) ||
                    (File.GetAttributes(index) & FileAttributes.ReparsePoint) != 0)
                    continue;
                // A malformed unrelated index is not evidence that it owns
                // this session; no directory may be guessed from its name.
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(index));
                    if (doc.RootElement.TryGetProperty("SessionId", out var identity) &&
                        identity.ValueKind == JsonValueKind.String &&
                        string.Equals(identity.GetString(), id,
                            StringComparison.OrdinalIgnoreCase))
                        matches.Add(dir);
                }
                catch (JsonException) { }
            }
            if (matches.Count > 1)
                throw new InvalidDataException("Ambiguous GCO capture identity; preserve both directories for recovery.");
        }
        var folder = matches.Count == 1 ? matches[0] : legacy;
        if (Directory.Exists(folder) &&
            (File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked GCO capture folder is not accepted.");
        return folder;
    }

    // Provision once, before PresentMon starts, and create the index BEFORE
    // reporting success so timestamp-only folders remain session-addressable.
    // A same-second name collision refuses instead of inventing a hash/suffix.
    public static string CreateDatedSessionFolder(string root, string id,
        DateTimeOffset started, string game, bool cetReadyBeforeAttempt = false,
        bool redscriptReadyBeforeAttempt = false)
    {
        CheckId(id);
        var results = Path.Combine(Path.GetFullPath(root), "RESULTS");
        Directory.CreateDirectory(results);
        if (Directory.Exists(SessionFolder(root, id)))
            throw new InvalidDataException("Capture session identity already exists.");
        var name = "GCO-Capture-" +
            started.ToLocalTime().ToString("ddMMyyyy_HHmmss", CultureInfo.InvariantCulture);
        var folder = Path.Combine(results, name);
        if (Directory.Exists(folder) || File.Exists(folder))
            throw new InvalidDataException("Capture timestamp already exists; retry after the next second.");
        Directory.CreateDirectory(folder);
        var initial = new CollectedCaptureIndex(id, Path.GetFullPath(game),
            null, null, null, CetReadyBeforeAttempt: cetReadyBeforeAttempt,
            RedscriptReadyBeforeAttempt: redscriptReadyBeforeAttempt);
        var file = Path.Combine(folder, "collection.json");
        var temp = file + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(initial, Json));
            File.Move(temp, file);
        }
        catch
        {
            if (File.Exists(temp)) File.Delete(temp);
            // Only our empty, newly provisioned folder can be removed here.
            if (!File.Exists(file) && !Directory.EnumerateFileSystemEntries(folder).Any())
                Directory.Delete(folder);
            throw;
        }
        return folder;
    }

    // Abandon ONLY the temporary output of a launch that was never collected.
    // An explicit Collect always wins: incomplete / zero / failed-stop
    // collection records are preserved, as is any indexed owner evidence.
    // We never delete native-owner game files or third-party paths.
    public static bool DiscardUncollected(string root, string game, string id)
    {
        var folder = SessionFolder(root, id);
        if (!Directory.Exists(folder)) return false;
        var index = Read(root, game, id, verifyHashes: true);
        if (index.CollectionAttempted || index.Cet is not null ||
            index.Redscript is not null || index.Restored)
            return false;
        static void CheckTree(string directory)
        {
            if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked capture directory cannot be discarded.");
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked capture entry cannot be discarded.");
                if (Directory.Exists(entry)) CheckTree(entry);
            }
        }
        CheckTree(folder);
        // Refuse extra manual evidence even when the session was never
        // explicitly collected; only GCO's known temporary files qualify.
        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Directory.Exists(entry) &&
                Path.GetFileName(entry) is not ("PresentMon" or "FrameTime"))
                throw new InvalidDataException(
                    "Unrecognized capture directory; preserving contents: " + entry);
            if (File.Exists(entry) &&
                !Path.GetFileName(entry).Equals("collection.json",
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "Unrecognized capture evidence; preserving: " + entry);
        }
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            if (Path.GetFileName(file) is not ("PresentMon.csv" or "PresentMon-controller.json"
                                              or "collection.json"))
                throw new InvalidDataException(
                    "Unrecognized uncollected capture file; preserving: " + file);
        Directory.Delete(folder, recursive: true);
        return true;
    }

    public static bool UsesDatedLayout(string root, string id)
    {
        var name = Path.GetFileName(SessionFolder(root, id));
        return IsReadableSessionName(name) || IsDatedSessionName(name, id);
    }

    public static bool UsesFlatLayout(string root, string id) =>
        IsReadableSessionName(Path.GetFileName(SessionFolder(root, id)));

    public static string DomainFolder(string root, string id, string domain)
    {
        if (domain is not ("CET" or "REDscript"))
            throw new InvalidDataException("Unknown profiler domain.");
        var name = UsesDatedLayout(root, id)
            ? (domain == "CET" ? "G-CET" :
                UsesFlatLayout(root, id) ? "G-REDscript" : "G-RED")
            : domain;
        return Path.Combine(SessionFolder(root, id), name);
    }
    private static string IndexFile(string root, string id) =>
        Path.Combine(SessionFolder(root, id), "collection.json");
    public static CollectedCaptureIndex Read(string root, string game, string id,
        bool verifyHashes = false)
    {
        var file = IndexFile(root, id);
        var fullGame = Path.GetFullPath(game);
        if (!File.Exists(file))
            return new(id, fullGame, null, null, null);
        var item = JsonSerializer.Deserialize<CollectedCaptureIndex>(File.ReadAllText(file)) ??
            throw new InvalidDataException("Empty GCO capture index.");
        if (!string.Equals(item.SessionId, id, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFullPath(item.GameRoot), fullGame, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("GCO collection does not belong to current install session.");
        if (item.Cet is not null) Verify(root, id, "CET", item.Cet, verifyHashes);
        if (item.Redscript is not null) Verify(root, id, "REDscript", item.Redscript, verifyHashes);
        if (item.FrameTimeCsv is not null)
        {
            if (item.FrameTimeEvidence is null)
                throw new InvalidDataException("Frame-time data has no SHA-256 provenance.");
            var expected = Path.GetFullPath(Path.Combine(SessionFolder(root, id),
                item.FrameTimeEvidence.Path.Replace('/', Path.DirectorySeparatorChar)));
            var relative = Path.GetRelativePath(SessionFolder(root, id), expected);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                !string.Equals(expected, Path.GetFullPath(item.FrameTimeCsv),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PresentMon evidence is outside its capture session.");
            PresentMonLifecyclePolicy.ValidateOutputCsv(root, expected);
            if (!File.Exists(expected) ||
                (File.GetAttributes(expected) & FileAttributes.ReparsePoint) != 0 ||
                new FileInfo(expected).Length != item.FrameTimeEvidence.Bytes ||
                (verifyHashes && !Hash(expected).Equals(item.FrameTimeEvidence.Sha256,
                    StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Collected PresentMon CSV is missing or modified.");
        }
        return item;
    }
    // After Continue, the owner installation journal is intentionally gone.
    // A finished analysis workspace is instead identified by its immutable
    // session ID, game root and a verified, restored capture index.
    public static CollectedCaptureIndex ReadRestored(string root, string game, string sessionId)
    {
        var saved = Read(root, game, sessionId, verifyHashes: true);
        if (!saved.Restored || !saved.CollectionAttempted || !saved.PresentMonStopVerified ||
            saved.Cet is null || saved.Redscript is null)
            throw new InvalidDataException(
                "The selected capture is not a completed, restored GCO session.");
        return saved;
    }

    // The source-owner install journal is intentionally removed after
    // successful restoration. A separate read-only workspace pointer lets
    // reopening GCO return to the same verified Analyze page, not silently
    // replace it with an unrelated new installation.
    private static string LastRestoredPointer(string root) =>
        Path.Combine(Path.GetFullPath(root), "RESULTS", "GCO-last-restored-session.json");

    public static void RememberRestored(string root, string game, string id)
    {
        _ = ReadRestored(root, game, id); // hard gate: verified native evidence and restore
        var destination = LastRestoredPointer(root);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".tmp";
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(
                new { GameRoot = Path.GetFullPath(game), SessionId = id }, Json));
            File.Move(temp, destination, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    // Reset only the Analyze navigation bookmark. The collected session and
    // its immutable, hash-indexed evidence remain in RESULTS unchanged.
    public static void ForgetLastRestored(string root)
    {
        var pointer = LastRestoredPointer(root);
        if (!File.Exists(pointer)) return;
        if ((File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked Analyze pointer cannot be reset.");
        File.Delete(pointer);
    }

    public static CollectedCaptureIndex? ReadLastRestored(string root, string game)
    {
        var file = LastRestoredPointer(root);
        if (!File.Exists(file)) return null;
        if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked last-workspace pointer refused.");
        using var doc = JsonDocument.Parse(File.ReadAllText(file));
        var data = doc.RootElement;
        if (!data.TryGetProperty("GameRoot", out var previous) ||
            previous.ValueKind != JsonValueKind.String ||
            !data.TryGetProperty("SessionId", out var identity) ||
            identity.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("Last-workspace pointer is invalid.");
        var savedGame = previous.GetString();
        if (string.IsNullOrWhiteSpace(savedGame) ||
            !Path.IsPathFullyQualified(savedGame) ||
            !string.Equals(Path.GetFullPath(savedGame), Path.GetFullPath(game),
                StringComparison.OrdinalIgnoreCase))
            return null; // another selected game never inherits an old workspace
        var id = identity.GetString()!;
        CheckId(id);
        return ReadRestored(root, game, id);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
    private static void Verify(string root, string id, string domain,
        CollectedOwner owner, bool hashes)
    {
        if (owner.Files.Count == 0) throw new InvalidDataException("Captured archive is empty.");
        var folder = DomainFolder(root, id, domain);
        foreach (var entry in owner.Files)
        {
            if (Path.IsPathRooted(entry.Path) || entry.Path.Split('/').Any(x =>
                x is ".." or "." or "" or "\\"))
                throw new InvalidDataException("Archive index contains an unsafe path.");
            var path = Path.Combine(folder, entry.Path.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path) || new FileInfo(path).Length != entry.Bytes ||
                (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
                (hashes && !Hash(path).Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Collected evidence is missing or modified: " + entry.Path);
        }
    }
    public static CollectedCaptureIndex CopyOwner(string root, string game,
        string id, BackendKind domain, string? archive)
    {
        if (domain is not (BackendKind.Cet or BackendKind.Grsp))
            throw new InvalidDataException("Unsupported owner archive.");
        if (string.IsNullOrWhiteSpace(archive) || !Directory.Exists(archive))
            throw new InvalidDataException("Source-owner archive folder not found.");
        var name = domain == BackendKind.Cet ? "CET" : "REDscript";
        var state = Read(root, game, id, verifyHashes: true);
        if ((domain == BackendKind.Cet ? state.Cet : state.Redscript) is not null)
            return state;
        if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Refusing linked owner archive.");
        var destination = DomainFolder(root, id, name);
        if (Directory.Exists(destination))
            throw new InvalidDataException("Unindexed owner archive already exists; preserve for recovery.");
        Directory.CreateDirectory(SessionFolder(root, id));
        var stage = destination + ".partial-" + Guid.NewGuid().ToString("N")[..8];
        var files = new List<CollectedFile>();
        try
        {
            void CopyDir(string source, string relative)
            {
                if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked directories refused.");
                foreach (var sourceFile in Directory.EnumerateFiles(source))
                {
                    if ((File.GetAttributes(sourceFile) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Linked archive file refused.");
                    var rel = (relative.Length == 0 ? "" : relative + "/") + Path.GetFileName(sourceFile);
                    var target = Path.Combine(stage, rel.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(sourceFile, target, overwrite: false);
                    var hash = Hash(sourceFile);
                    if (!hash.Equals(Hash(target), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Failed verification of copied archive: " + rel);
                    files.Add(new(rel, new FileInfo(target).Length, hash));
                }
                foreach (var next in Directory.EnumerateDirectories(source))
                    CopyDir(next, (relative.Length == 0 ? "" : relative + "/") + Path.GetFileName(next));
            }
            CopyDir(archive, "");
            var required = domain == BackendKind.Cet ? "CET_Summary.json" : "GRSP_Summary.json";
            if (!files.Any(x => x.Path.Equals(required, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Owner archive has no expected completed capture summary.");
            Directory.Move(stage, destination);
            var owner = new CollectedOwner(Path.GetFullPath(archive), files);
            state = domain == BackendKind.Cet ? state with { Cet = owner } :
                state with { Redscript = owner };
            Write(root, state);
            return state;
        }
        finally
        {
            if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
        }
    }
    public static CollectedCaptureIndex CollectPresentMon(string root, string game,
        string sessionId, string csv)
    {
        var path = PresentMonLifecyclePolicy.ValidateOutputCsv(root, csv);
        var folder = SessionFolder(root, sessionId);
        var relative = Path.GetRelativePath(folder, path);
        if (Path.IsPathRooted(relative) || relative == ".." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new InvalidDataException("PresentMon CSV belongs to a different capture session.");
        var state = Read(root, game, sessionId, verifyHashes: true);
        // Capture is not counted unless the source-generated CSV has real
        // Cyberpunk frame rows. Collection needs no CET/GRSP completion.
        PresentMonLifecyclePolicy.CountVerifiedFrames(path);
        var proof = new CollectedFile(relative.Replace(Path.DirectorySeparatorChar, '/'),
            new FileInfo(path).Length, Hash(path));
        state = state with { FrameTimeCsv = path, FrameTimeEvidence = proof };
        Write(root, state);
        return state;
    }

    public static CollectedCaptureIndex MarkCollectionAttempted(string root, string game, string sessionId, bool presentMonStopped)
    {
        var state = Read(root, game, sessionId, verifyHashes: true);
        state = state with { CollectionAttempted = true, PresentMonStopVerified = presentMonStopped };
        Write(root, state);
        return state;
    }

    public static void MarkRestored(string root, CollectedCaptureIndex state,
        bool skipCet, bool skipRedscript, bool skipFrameTime)
    {
        var skipped = new List<string>();
        if (state.Cet is null && skipCet) skipped.Add("CET");
        if (state.Redscript is null && skipRedscript) skipped.Add("REDscript");
        if (state.FrameTimeCsv is null && skipFrameTime)
            skipped.Add(UsesDatedLayout(root, state.SessionId) ? "PresentMon" : "FrameTime");
        Write(root, state with { Restored = true, Skipped = skipped });
    }
    private static void Write(string root, CollectedCaptureIndex item)
    {
        var file = IndexFile(root, item.SessionId);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(item, Json));
        File.Move(temp, file, overwrite: true);
    }
}
