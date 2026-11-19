using System.Buffers.Binary;
using System.Text;
using System.Security.Cryptography;

namespace GRedscript.Resolver;

/// <summary>
/// Shared output dispatcher for the EXISTING CET RED4ext native player/location
/// service. No DLL recompilation, duplicate player observer or mod-owner list.
/// Both CET and REDscript source stages may contribute GCAS owners; one final
/// native DLL and one merged immutable location table are emitted.
/// </summary>
public static class SharedNativeRuntimeStage
{
    public const string NativePath =
        "red4ext/plugins/G-CET-StaticLocations/GCETStaticLocations.dll";
    public const string LocationsPath =
        "red4ext/plugins/G-CET-StaticLocations/G-CET.locations";
    public const string RedscriptBridgePath =
        "r6/scripts/G-RedRuntime/Native/GCETNativeLocationBackend.reds";

    public sealed record Result(string NativeStatus, string LocationsStatus,
        int TotalAnchors, int TotalOwners, bool RedscriptBridgeStaged,
        bool ProductionZipAuthorized);

    private sealed record Anchor(int Id, float X, float Y, float Z,
        float Radius, int XY, int Owner)
    {
        public bool SameLocation(Anchor other) =>
            X == other.X && Y == other.Y && Z == other.Z &&
            Radius == other.Radius && XY == other.XY && Owner == other.Owner;
    }

    private static string ResolveIn(string root, string relative)
    {
        var full = Path.GetFullPath(root);
        var path = Path.GetFullPath(Path.Combine(full,
            relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(full.TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_NATIVE_PATH_ESCAPE");
        return path;
    }

    private static void RequireNative(byte[] dll)
    {
        if (dll.Length < 1024 || dll[0] != (byte)'M' || dll[1] != (byte)'Z')
            throw new InvalidDataException("SHARED_NATIVE_INVALID_DLL");
        var symbols = Encoding.Latin1.GetString(dll);
        foreach (var name in new[] {
            "ScriptGameInstance", "GCETStaticReady",
            "GCETStaticCoordinatorState", "GCETStaticOwnerCount",
            "GCETStaticOwnerAnchor", "GCETStaticState",
            "GCETPlayerVector4Live", "GCETPlayerVector4Valid" })
            if (!symbols.Contains(name, StringComparison.Ordinal))
                throw new InvalidDataException(
                    "SHARED_NATIVE_MISSING_RTTI_CAPABILITY: " + name);
    }

    private static List<Anchor> ReadLocations(byte[] data)
    {
        if (data.Length < 16 ||
            !data.AsSpan(0, 4).SequenceEqual("GCAS"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4, 4)) != 1 ||
            BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(12, 4)) != 0)
            throw new InvalidDataException("SHARED_NATIVE_LOCATIONS_HEADER");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(8, 4));
        if (count > 100000 || data.Length != 16L + (long)count * 28)
            throw new InvalidDataException("SHARED_NATIVE_LOCATIONS_LENGTH");
        var rows = new List<Anchor>((int)count);
        var ids = new HashSet<int>();
        for (int i = 0; i < count; i++)
        {
            var span = data.AsSpan(16 + i * 28, 28);
            int id = BinaryPrimitives.ReadInt32LittleEndian(span);
            float x = BinaryPrimitives.ReadSingleLittleEndian(span[4..]);
            float y = BinaryPrimitives.ReadSingleLittleEndian(span[8..]);
            float z = BinaryPrimitives.ReadSingleLittleEndian(span[12..]);
            float r = BinaryPrimitives.ReadSingleLittleEndian(span[16..]);
            int xy = BinaryPrimitives.ReadInt32LittleEndian(span[20..]);
            int owner = BinaryPrimitives.ReadInt32LittleEndian(span[24..]);
            if (id <= 0 || !ids.Add(id) || owner <= 0 ||
                !float.IsFinite(x) || !float.IsFinite(y) ||
                !float.IsFinite(z) || !float.IsFinite(r) ||
                r <= 0 || r > 1024 || (xy != 0 && xy != 1))
                throw new InvalidDataException("SHARED_NATIVE_INVALID_LOCATION_ROW");
            rows.Add(new(id, x, y, z, r, xy, owner));
        }
        return rows;
    }

    private static byte[] WriteLocations(IReadOnlyList<Anchor> rows)
    {
        if (rows.Count > 100000)
            throw new InvalidDataException("SHARED_NATIVE_LOCATIONS_LIMIT");
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, true);
        writer.Write("GCAS"u8);
        writer.Write((uint)1);
        writer.Write((uint)rows.Count);
        writer.Write((uint)0);
        for (int i = 0; i < rows.Count; i++)
        {
            var a = rows[i];
            writer.Write(i + 1);
            writer.Write(a.X); writer.Write(a.Y); writer.Write(a.Z);
            writer.Write(a.Radius); writer.Write(a.XY); writer.Write(a.Owner);
        }
        writer.Flush();
        return bytes.ToArray();
    }

    private static List<Anchor> Merge(
        IReadOnlyList<Anchor> existing, IReadOnlyList<Anchor> incoming)
    {
        var result = existing.ToList();
        foreach (var owner in incoming.Select(a => a.Owner).Distinct())
        {
            var proposed = incoming.Where(a => a.Owner == owner).ToArray();
            var present = result.Where(a => a.Owner == owner).ToArray();
            if (present.Length == 0)
            {
                result.AddRange(proposed);
                continue;
            }
            if (present.Length != proposed.Length ||
                present.Where((a, i) => !a.SameLocation(proposed[i])).Any())
                throw new InvalidDataException(
                    "SHARED_NATIVE_OWNER_SOURCE_COLLISION: " + owner);
        }
        return result;
    }

    /// <summary>
    /// sourceRoot is a CET Resolver package root (runtime/native/..).
    /// outputRoot is one fresh or existing EXTERNAL shared overlay staging root.
    /// Incoming REDscript locations use the canonical GCAS v1 format.
    /// Native DLL staging is first-writer-wins; site inventory is unioned by
    /// owner and identical owner records are idempotent.
    /// </summary>
    public static Result Stage(string sourceRoot, string gameRoot,
        string outputRoot, string frameworkRoot,
        string? redLocationsFile = null, bool includeBridge = true)
    {
        string source = Path.GetFullPath(sourceRoot);
        string output = Path.GetFullPath(outputRoot);
        string game = Path.GetFullPath(gameRoot);
        string framework = Path.GetFullPath(frameworkRoot);
        if (!Directory.Exists(game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(game.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_NATIVE_GAME_OUTPUT_FORBIDDEN");
        if (!Directory.Exists(source) ||
            output.Equals(source, StringComparison.OrdinalIgnoreCase) ||
            output.StartsWith(source.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            source.StartsWith(output.TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("SHARED_NATIVE_STAGE_ROOTS_OVERLAP");
        string sourceDll = ResolveIn(source, "runtime/native/GCETStaticLocations.dll");
        string destinationDll = ResolveIn(output, NativePath);
        string destinationLocations = ResolveIn(output, LocationsPath);
        string bridgeFile = ResolveIn(output, RedscriptBridgePath);
        string ownerBridge = ResolveIn(framework,
            "optional-native/r6/scripts/G-RedRuntime/Native/GCETNativeLocationBackend.reds");
        if (includeBridge && !File.Exists(ownerBridge))
            throw new InvalidDataException("SHARED_NATIVE_FRAMEWORK_OWNER_BACKEND_MISSING");
        byte[] bridgeBytes = includeBridge ? File.ReadAllBytes(ownerBridge) : [];
        if (!File.Exists(sourceDll))
            throw new InvalidDataException("SHARED_NATIVE_OWNER_RUNTIME_ABSENT");
        byte[] ownerDll = File.ReadAllBytes(sourceDll);
        RequireNative(ownerDll);
        byte[]? alreadyDll = File.Exists(destinationDll) ?
            File.ReadAllBytes(destinationDll) : null;
        if (alreadyDll is not null) RequireNative(alreadyDll);
        var existing = File.Exists(destinationLocations)
            ? ReadLocations(File.ReadAllBytes(destinationLocations))
            : new List<Anchor>();
        // REDscript can generate its own inert, source-derived static
        // sites without waiting for CET to generate an overlay.
        var incoming = redLocationsFile is not null
            ? ReadLocations(File.ReadAllBytes(Path.GetFullPath(redLocationsFile)))
            : ReadLocations(RedscriptNativeLocations.Compile(game));
        var combined = Merge(existing, incoming);
        if (combined.Count > 100000)
            throw new InvalidDataException("SHARED_NATIVE_LOCATIONS_LIMIT");
        // Stage the ONE framework-owned optional bridge, including CET's
        // existing strict-live typed Vector4 declarations. Refuse old/partial
        // bridge sources before writing either native DLL or location data.
        string bridgeText = includeBridge ? Encoding.UTF8.GetString(bridgeBytes) : "";
        if (includeBridge && (!bridgeText.Contains(
                "public class GCETNativeLocationBackend extends NativeLocationBackend",
                StringComparison.Ordinal) ||
            !bridgeText.Contains("native func GCETStaticSubscribe",
                StringComparison.Ordinal) ||
            !bridgeText.Contains(
                "public static native func GCETPlayerVector4Live() -> Vector4",
                StringComparison.Ordinal) ||
            !bridgeText.Contains(
                "public static native func GCETPlayerVector4Valid() -> Bool",
                StringComparison.Ordinal) ||
            !bridgeText.Contains("public abstract class GCETNativePlayerVector",
                StringComparison.Ordinal) ||
            !bridgeText.Contains("GameInstance.GCETPlayerVector4Valid()",
                StringComparison.Ordinal) ||
            !bridgeText.Contains("return player.GetWorldPosition();",
                StringComparison.Ordinal)))
            throw new InvalidDataException("SHARED_NATIVE_FRAMEWORK_OWNER_BACKEND_INVALID");
        if (includeBridge && File.Exists(bridgeFile) &&
            !File.ReadAllBytes(bridgeFile).AsSpan().SequenceEqual(bridgeBytes))
            throw new InvalidDataException("SHARED_NATIVE_BRIDGE_COLLISION");
        // Finish all validation BEFORE any staging write.
        Directory.CreateDirectory(Path.GetDirectoryName(destinationDll)!);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationLocations)!);
        if (alreadyDll is null)
            File.WriteAllBytes(destinationDll, ownerDll);
        bool changed = !File.Exists(destinationLocations) ||
            combined.Count != existing.Count;
        if (changed)
            File.WriteAllBytes(destinationLocations, WriteLocations(combined));
        if (includeBridge && !File.Exists(bridgeFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(bridgeFile)!);
            File.WriteAllBytes(bridgeFile, bridgeBytes);
        }
        return new(alreadyDll is null ? "STAGED" : "ALREADY_PRESENT",
            changed ? "MERGED_OR_CREATED" : "ALREADY_PRESENT",
            combined.Count, combined.Select(a => a.Owner).Distinct().Count(),
            includeBridge, false);
    }

}
