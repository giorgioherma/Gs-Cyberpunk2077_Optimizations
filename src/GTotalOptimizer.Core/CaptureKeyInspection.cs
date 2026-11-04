using System.Text.Json;

namespace GTotalOptimizer.Core;

public sealed record CaptureKeyCheck(string Owner, string Value, bool Confirmed, string Detail);

public static class CaptureKeyInspector
{
    // CET's authoritative BindingService.F11BindCode, not a user-owned override.
    public const long CetF11Code = 34339947158700032L;

    public static IReadOnlyList<CaptureKeyCheck> Inspect(string? gameRoot, bool cetBackendPresent,
        bool grspBackendPresent, string? capFrameXExecutable)
    {
        return
        [
            InspectCet(gameRoot, cetBackendPresent),
            new("REDscript", grspBackendPresent ? "F11" : "Unavailable",
                grspBackendPresent, grspBackendPresent
                    ? "GRSP native capture uses F11 twice (start/stop); runtime not yet exercised."
                    : "GRSP component not staged."),
            InspectCapFrameX(capFrameXExecutable)
        ];
    }

    public static CaptureKeyCheck InspectCet(string? gameRoot, bool backendPresent)
    {
        if (string.IsNullOrWhiteSpace(gameRoot) || !backendPresent)
            return new("CET", "Unavailable", false, "Game root or CET profiler component not available.");

        var path = Path.Combine(gameRoot, "bin", "x64", "plugins", "cyber_engine_tweaks", "bindings.json");
        if (!File.Exists(path))
            return new("CET", "Pending", false, "Binding file missing. CET installer seeds F11 only if no binding exists.");

        try
        {
            using var json = JsonDocument.Parse(File.ReadAllText(path));
            if (!json.RootElement.TryGetProperty("CETProfilerControls", out var group) ||
                !group.TryGetProperty("CETProfiler_Toggle", out var value))
                return new("CET", "Pending", false, "CET installer may seed F11; it never replaces an existing user binding.");

            var found = value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt64(out var number) => number,
                JsonValueKind.String when long.TryParse(value.GetString(), out var number) => number,
                _ => -1L
            };
            return found == CetF11Code
                ? new("CET", "F11", true, "Existing CET capture toggle is F11.")
                : new("CET", "CUSTOM", false, "Existing binding is preserved. Set F11 manually in CET before capture.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new("CET", "Unknown", false, "Could not verify CET bindings.json.");
        }
    }

    public static CaptureKeyCheck InspectCapFrameX(string? exe)
    {
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
            return new("CapFrameX", "Optional", false, "CapFrameX executable is not linked or bundled.");

        var baseDir = Path.GetDirectoryName(Path.GetFullPath(exe))!;
        var configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CapFrameX", "Configuration");
        var portable = Path.Combine(baseDir, "portable.json");
        try
        {
            if (File.Exists(portable))
            {
                using var paths = JsonDocument.Parse(File.ReadAllText(portable));
                if (paths.RootElement.TryGetProperty("paths", out var mapping) &&
                    mapping.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.String)
                {
                    var specified = cfg.GetString();
                    if (!string.IsNullOrWhiteSpace(specified))
                        configDir = Path.GetFullPath(Path.IsPathRooted(specified)
                            ? specified : Path.Combine(baseDir, specified));
                }
            }
            var settings = Path.Combine(configDir, "AppSettings.json");
            if (!File.Exists(settings))
                return new("CapFrameX", "Unknown", false, "Settings missing; F11 must be confirmed in CapFrameX.");

            using var json = JsonDocument.Parse(File.ReadAllText(settings));
            var hotkey = FindString(json.RootElement, "CaptureHotKey");
            if (hotkey is null)
                return new("CapFrameX", "Unknown", false, "CaptureHotKey not found; do not assume F11.");
            return new("CapFrameX", hotkey, hotkey.Equals("F11", StringComparison.OrdinalIgnoreCase),
                hotkey.Equals("F11", StringComparison.OrdinalIgnoreCase)
                    ? "F11 confirmed from CapFrameX settings (read only)."
                    : "Existing user setting differs. No automatic overwrite.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new("CapFrameX", "Unknown", false, "Cannot safely read existing CapFrameX settings.");
        }
    }

    private static string? FindString(JsonElement root, string key)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name.Equals(key, StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                    return property.Value.GetString();
                var found = FindString(property.Value, key);
                if (found is not null) return found;
            }
        }
        else if (root.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in root.EnumerateArray())
            {
                var found = FindString(item, key);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
