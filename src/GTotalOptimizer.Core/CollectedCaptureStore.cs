using System.Security.Cryptography;
using System.Text.Json;

namespace GTotalOptimizer.Core;

public sealed record CollectedFile(string Path, long Bytes, string Sha256);
public sealed record CollectedOwner(string SourceArchive, List<CollectedFile> Files);
public sealed record CollectedCaptureIndex(
    string SessionId, string GameRoot, CollectedOwner? Cet, CollectedOwner? Redscript,
    string? FrameTimeCsv, bool Restored = false, List<string>? Skipped = null,
    CollectedFile? FrameTimeEvidence = null, bool CollectionAttempted = false)
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
    public static string SessionFolder(string root, string id)
    {
        CheckId(id);
        return Path.Combine(Path.GetFullPath(root), "RESULTS", "GCO-Capture-" + id);
    }
    public static string DomainFolder(string root, string id, string domain)
    {
        if (domain is not ("CET" or "REDscript"))
            throw new InvalidDataException("Unknown profiler domain.");
        return Path.Combine(SessionFolder(root, id), domain);
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
        if (!saved.Restored || !saved.CollectionAttempted ||
            saved.Cet is null || saved.Redscript is null)
            throw new InvalidDataException(
                "The selected capture is not a completed, restored GCO session.");
        return saved;
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

    public static CollectedCaptureIndex MarkCollectionAttempted(string root, string game, string sessionId)
    {
        var state = Read(root, game, sessionId, verifyHashes: true);
        state = state with { CollectionAttempted = true };
        Write(root, state);
        return state;
    }

    public static void MarkRestored(string root, CollectedCaptureIndex state,
        bool skipCet, bool skipRedscript, bool skipFrameTime)
    {
        var skipped = new List<string>();
        if (state.Cet is null && skipCet) skipped.Add("CET");
        if (state.Redscript is null && skipRedscript) skipped.Add("REDscript");
        if (state.FrameTimeCsv is null && skipFrameTime) skipped.Add("FrameTime");
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
