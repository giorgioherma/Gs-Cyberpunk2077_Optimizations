using System.Diagnostics;
using System.Text.Json;

namespace GTotalOptimizer.Core;

public sealed record ComponentCommandResult(
    BackendKind Kind, string Command, bool Ok, string Detail,
    string? ArchivePath = null, string? RawJson = null);

public static class ComponentCommandParser
{
    // Standalone CET and GRSP own their independent JSON schemas.
    public static ComponentCommandResult Parse(BackendKind kind, string command,
        int exitCode, string? stdout, string? stderr)
    {
        if (exitCode != 0)
        {
            var diagnostic = ExtractError(stderr) ?? ExtractError(stdout);
            return new(kind, command, false,
                diagnostic ?? $"Standalone {kind} {command} exited {exitCode}; inspect component logs.");
        }
        if (string.IsNullOrWhiteSpace(stdout))
            return new(kind, command, false, "Standalone command returned no JSON.");

        try
        {
            using var doc = JsonDocument.Parse(stdout);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return new(kind, command, false, "Command response is not an object.");

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind != JsonValueKind.True)
                return new(kind, command, false, ExtractError(stdout) ?? "Backend returned failure/invalid ok status.");

            if (kind == BackendKind.Grsp &&
                (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object))
                return new(kind, command, false, "GRSP result wrapper missing.");

            string? archive = null;
            if (command == "collect")
            {
                if (kind == BackendKind.Cet &&
                    root.TryGetProperty("destination", out var destination) &&
                    destination.ValueKind == JsonValueKind.String)
                    archive = destination.GetString();
                if (kind == BackendKind.Grsp &&
                    root.GetProperty("result").TryGetProperty("path", out var path) &&
                    path.ValueKind == JsonValueKind.String)
                    archive = path.GetString();
                if (string.IsNullOrWhiteSpace(archive))
                    return new(kind, command, false, "Collection did not identify a new archived capture.", RawJson: stdout);
            }

            return new(kind, command, true, "Standalone component completed the command.", archive, stdout);
        }
        catch (JsonException)
        {
            return new(kind, command, false, "Backend returned malformed JSON.");
        }
    }

    private static string? ExtractError(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.String)
                return error.GetString();
        }
        catch (JsonException) { }
        return null;
    }
}

public sealed class ComponentCommandRunner
{
    public async Task<ComponentCommandResult> RunAsync(BackendKind kind, string executable,
        string command, string? gameRoot, CancellationToken token, string? capture = null)
    {
        if (!File.Exists(executable))
            return new(kind, command, false, "Standalone component executable missing.");
        if (!new[] { "status", "install", "collect", "restore", "emergency-restore", "report" }.Contains(command))
            return new(kind, command, false, "Unsupported standalone command.");
        if (command != "report" && string.IsNullOrWhiteSpace(gameRoot))
            return new(kind, command, false, "A verified game root is required.");
        if (command == "report" && string.IsNullOrWhiteSpace(capture))
            return new(kind, command, false, "A verified capture folder is required.");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory
            }
        };
        process.StartInfo.ArgumentList.Add("--" + command);
        if (command == "report")
        {
            process.StartInfo.ArgumentList.Add("--capture");
            process.StartInfo.ArgumentList.Add(capture!);
        }
        else
        {
            process.StartInfo.ArgumentList.Add("--game");
            process.StartInfo.ArgumentList.Add(gameRoot!);
        }
        process.StartInfo.ArgumentList.Add("--json");

        try
        {
            if (!process.Start())
                return new(kind, command, false, "Could not start the standalone executable.");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(command is "collect" or "restore" ? 180 : 90));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            return ComponentCommandParser.Parse(kind, command, process.ExitCode,
                await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            return new(kind, command, false, $"Standalone {kind} {command} timed out; recovery status is UNKNOWN.");
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return new(kind, command, false, $"Standalone {kind} {command} could not run: {ex.Message}");
        }
    }
}
