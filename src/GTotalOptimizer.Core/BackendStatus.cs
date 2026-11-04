using System.Diagnostics;
using System.Text.Json;

namespace GTotalOptimizer.Core;

public enum BackendKind { Cet, Grsp }

public sealed record BackendStatus(BackendKind Kind, bool Responded, string Detail)
{
    public string Name => Kind == BackendKind.Cet ? "CET Profiler" : "GRSP REDscript Profiler";
}

public static class BackendResponse
{
    public static BackendStatus Interpret(BackendKind kind, string? stdout, int exitCode)
    {
        if (exitCode != 0)
            return new(kind, false, $"Backend exited with code {exitCode}. See its standalone diagnostics.");
        if (string.IsNullOrWhiteSpace(stdout))
            return new(kind, false, "Backend returned no status JSON.");

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(kind, false, "Status response is not a JSON object.");

            if (root.TryGetProperty("ok", out var ok))
            {
                if (ok.ValueKind != JsonValueKind.True && ok.ValueKind != JsonValueKind.False)
                    return new(kind, false, "Invalid backend success indicator.");
                if (ok.ValueKind == JsonValueKind.False)
                    return new(kind, false,
                        root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String
                            ? error.GetString() ?? "Backend reported failure."
                            : "Backend reported failure.");
            }

            if (kind == BackendKind.Grsp
                && (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object))
                return new(kind, false, "GRSP status response is missing its result object.");

            // Do not infer game-side installation readiness from CLI availability alone.
            return new(kind, true, "Status interface responded. Installed-state verification is not implemented yet.");
        }
        catch (JsonException)
        {
            return new(kind, false, "Backend returned malformed JSON.");
        }
    }
}

public static class BackendLocator
{
    public static string Executable(string packageRoot, BackendKind kind) =>
        kind switch
        {
            BackendKind.Cet => Path.Combine(packageRoot, "components", "cet", "G-CET-Runtime-Profiler.exe"),
            BackendKind.Grsp => Path.Combine(packageRoot, "components", "grsp", "G-REDscript-Profiler.exe"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
}

public sealed class BackendStatusRunner
{
    public async Task<BackendStatus> ReadAsync(string executable, string gameRoot, BackendKind kind, CancellationToken cancellationToken)
    {
        if (!File.Exists(executable))
            return new(kind, false, "Current standalone component is not bundled or linked in this build.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            }
        };

        process.StartInfo.ArgumentList.Add("--status");
        process.StartInfo.ArgumentList.Add("--game");
        process.StartInfo.ArgumentList.Add(gameRoot);
        process.StartInfo.ArgumentList.Add("--json");

        try
        {
            if (!process.Start())
                return new(kind, false, "Could not launch the standalone component.");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var standardOutput = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var standardError = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await standardOutput.ConfigureAwait(false);
            var errors = await standardError.ConfigureAwait(false);
            var parsed = BackendResponse.Interpret(kind, output, process.ExitCode);
            if (!parsed.Responded && !string.IsNullOrWhiteSpace(errors))
                return parsed with { Detail = parsed.Detail + " Error output was reported; inspect component logs." };
            return parsed;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return new(kind, false, "Status request timed out.");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new(kind, false, "Status check could not start: " + ex.Message);
        }
    }
}
