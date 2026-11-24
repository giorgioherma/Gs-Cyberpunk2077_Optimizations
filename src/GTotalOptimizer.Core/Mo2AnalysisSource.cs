using System.Security.Cryptography;

namespace GTotalOptimizer.Core;

// A per-operation, disposable game-shaped view of the selected profile.
// The physical game and sealed capture remain the identity; only the
// resolver's file-access root changes. No executable is run from this view.
public sealed class Mo2AnalysisSource : IDisposable
{
    private readonly Mo2CaptureIdentity identity;
    private readonly Dictionary<string, (string Path, string Owner, string Hash)> files =
        new(StringComparer.OrdinalIgnoreCase);
    public string Root { get; }
    private bool disposed;

    private Mo2AnalysisSource(Mo2CaptureIdentity identity)
    {
        this.identity = identity;
        Root = Path.Combine(Path.GetTempPath(), "GCO-MO2-Source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public static Mo2Stack Resolve(Mo2CaptureIdentity identity, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(identity.GameRoot) ||
            !Path.IsPathFullyQualified(identity.InstanceDirectory) ||
            string.IsNullOrWhiteSpace(identity.Profile))
            throw new InvalidDataException("MO2 workspace source identity is invalid.");
        var instance = Mo2SourceInspector.Discover(identity.InstanceDirectory);
        var stack = Mo2SourceInspector.Resolve(identity.GameRoot, instance, identity.Profile, token);
        if (!stack.Ready || !stack.ProfileVerified || stack.GameOnlyFallback)
            throw new InvalidDataException("Original MO2 profile is unavailable: " + stack.Detail);
        if (stack.EffectiveFiles.Keys.Any(path =>
            path.Equals("red4ext/plugins/G-REDscript-Profiler.dll", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("bin/x64/plugins/cyber_engine_tweaks/mods/CETProfilerControls/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("bin/x64/plugins/.cet_runtime_profiler/", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("red4ext/plugins/G-REDscript-Profiler/.manager_state.json", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Original MO2 profile still contains profiler files.");
        return stack;
    }

    public static Mo2AnalysisSource Create(CollectedCaptureIndex captured, CancellationToken token = default)
    {
        var identity = captured.Mo2Source ??
            throw new InvalidDataException("Capture has no MO2 source binding.");
        if (!captured.Restored || identity.SessionId != captured.SessionId ||
            !string.Equals(identity.GameRoot, captured.GameRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 workspace binding is not restored.");
        var result = new Mo2AnalysisSource(identity);
        try
        {
            var stack = Resolve(identity, token);
            long bytes = 0;
            foreach (var (relative, winner) in stack.EffectiveFiles.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (!IsSource(relative)) continue;
                var source = Path.GetFullPath(winner.PhysicalPath);
                CheckRegular(source);
                bytes = checked(bytes + new FileInfo(source).Length);
                if (bytes > 4L * 1024 * 1024 * 1024)
                    throw new InvalidDataException("MO2 source view exceeds the bounded copy size.");
                var target = result.Target(relative);
                var hash = Hash(source);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(source, target, overwrite: false);
                if (Hash(target) != hash || Hash(source) != hash)
                    throw new InvalidDataException("MO2 source changed while copying: " + relative);
                result.files.Add(relative, (source, winner.Owner, hash));
            }
            // Only a presence marker for the existing read-only source
            // inspectors. Never copy or execute the actual game executable.
            var marker = result.Target("bin/x64/Cyberpunk2077.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
            File.WriteAllBytes(marker, []);
            Directory.CreateDirectory(result.Target("r6/scripts"));
            result.VerifyCurrent(token);
            return result;
        }
        catch { result.Dispose(); throw; }
    }

    public void RequireBinding(CollectedCaptureIndex captured)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (captured.Mo2Source != identity ||
            captured.SessionId != identity.SessionId || !captured.Restored)
            throw new InvalidDataException("MO2 resolver source does not match the capture.");
    }

    public void VerifyCurrent(CancellationToken token = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var live = Resolve(identity, token);
        var current = live.EffectiveFiles.Where(pair => IsSource(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
        if (current.Count != files.Count)
            throw new InvalidDataException("MO2 source inventory changed; analyze again.");
        var projectedPaths = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!projectedPaths.Remove("bin/x64/Cyberpunk2077.exe") ||
            !projectedPaths.SetEquals(files.Keys) ||
            new FileInfo(Target("bin/x64/Cyberpunk2077.exe")).Length != 0)
            throw new InvalidDataException("MO2 disposable source inventory changed.");
        foreach (var (relative, original) in files)
        {
            token.ThrowIfCancellationRequested();
            if (!current.TryGetValue(relative, out var winner) ||
                !string.Equals(Path.GetFullPath(winner.PhysicalPath), original.Path, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(winner.Owner, original.Owner, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("MO2 source winner changed: " + relative);
            CheckRegular(original.Path);
            var projected = Target(relative);
            CheckRegular(projected);
            if (Hash(original.Path) != original.Hash || Hash(projected) != original.Hash)
                throw new InvalidDataException("MO2 source bytes changed: " + relative);
        }
    }

    // The authoritative source inspector already indexes all CET/REDscript
    // source and native dependencies. Exclude runtime-only capture/log output,
    // not entire mod folders or custom Lua / REDscript content.
    private static bool IsSource(string relative) =>
        !relative.StartsWith("red4ext/plugins/G-REDscript-Profiler/RESULTS/", StringComparison.OrdinalIgnoreCase) &&
        !relative.StartsWith("bin/x64/plugins/cyber_engine_tweaks/CET_Runtime_Profile_", StringComparison.OrdinalIgnoreCase) &&
        !relative.EndsWith(".log", StringComparison.OrdinalIgnoreCase);

    private string Target(string relative)
    {
        if (relative.Contains(':') || relative.Contains('\\') ||
            relative.Split('/').Any(part => part is "" or "." or "..") ||
            Path.IsPathRooted(relative))
            throw new InvalidDataException("Unsafe MO2 source path: " + relative);
        var path = Path.GetFullPath(Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("MO2 source path escapes projection.");
        return path;
    }

    private static void CheckRegular(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("MO2 source file unavailable.", path);
        for (string? current = path; current is not null; current = Path.GetDirectoryName(current))
            if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked MO2 source refused: " + current);
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}
