using System.Text.Json;
using System.Text.Json.Nodes;

namespace GTotalOptimizer.Core;

// Native CET deployment boundary only. The packaged CET manager still authors
// and verifies the profiler binary, original ASI backup and all Lua changes in
// the existing isolated fixture. The physical route deploys those exact bytes;
// it never runs a second installer on live CET mods, bindings or 0-Engine.
public static partial class Mo2ProfilerOverlayBuilder
{
    internal const string CetAsiRelative = "bin/x64/plugins/cyber_engine_tweaks.asi";
    private const string CetOwnerStateRelative = "bin/x64/plugins/.cet_runtime_profiler";
    private const string CetOwnerBackupRelative =
        CetOwnerStateRelative + "/cyber_engine_tweaks.ORIGINAL.asi";

    private sealed record PhysicalCetState(string SessionId, string Target, string Backup,
        string OriginalHash, string InstalledHash);

    // Called only after the original prepared-source verification and discard,
    // immediately before the signed MO2 launch. Preparation remains read-only.
    // A selected-profile virtual ASI is deliberately left on the existing path.
    internal static void ActivatePhysicalCetForLaunch(Mo2OverlayPreparation prepared, Mo2Stack selected)
    {
        RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before installing the native CET profiler.");
        if (!selected.Ready || !selected.ProfileVerified || selected.GameOnlyFallback ||
            !string.Equals(selected.Profile, prepared.Profile, StringComparison.OrdinalIgnoreCase) ||
            !selected.EffectiveFiles.TryGetValue(CetAsiRelative, out var winner))
            throw new InvalidDataException("Selected CET source is unverified.");
        if (winner.FromMo2) return;

        EnsureNoReparseParents(prepared.JournalFile);
        RequireRegularUnlinkedFile(prepared.JournalFile);
        var journal = JsonNode.Parse(File.ReadAllText(prepared.JournalFile))!.AsObject();
        if (journal["phase"]?.GetValue<string>() != "prepared" ||
            journal["sessionId"]?.GetValue<string>() != prepared.SessionId ||
            journal["profile"]?.GetValue<string>() != prepared.Profile ||
            journal["physicalCet"] is not null ||
            !string.Equals(journal["stageRoot"]?.GetValue<string>(), prepared.StagingDirectory,
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Physical CET launch deployment is not prepared.");

        var game = Path.GetFullPath(journal["gameRoot"]!.GetValue<string>());
        var target = Path.Combine(game, CetAsiRelative);
        if (!string.Equals(Path.GetFullPath(winner.PhysicalPath), Path.GetFullPath(target),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CET physical winner does not belong to the selected game.");
        var manifest = journal["generatedFiles"]!.Deserialize<Mo2OverlayFile[]>() ??
            throw new InvalidDataException("CET owner deployment manifest is missing.");
        if (!manifest.SequenceEqual(prepared.Files))
            throw new InvalidDataException("CET owner deployment manifest changed.");
        var native = manifest.Single(f => f.GameRelativePath.Equals(CetAsiRelative,
            StringComparison.OrdinalIgnoreCase));
        var original = ReadAsiProperty(JsonSerializer.SerializeToElement(journal["originalSources"]));
        var staged = Path.Combine(prepared.StagingDirectory, CetAsiRelative);
        EnsureNoReparseParents(staged);
        RequireRegularUnlinkedFile(staged);
        if (!Hash(staged).Equals(native.Sha256, StringComparison.OrdinalIgnoreCase) ||
            new FileInfo(staged).Length != native.Size)
            throw new InvalidDataException("Verified CET profiler payload changed.");
        journal["physicalCet"] = JsonSerializer.SerializeToNode(new
        {
            originalSha256 = original,
            installedSha256 = native.Sha256
        });
        var physical = ReadPhysicalCet(JsonSerializer.SerializeToElement(journal))!;
        RequirePhysicalCetBackup(physical);
        if (!Hash(physical.Target).Equals(physical.OriginalHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Physical CET ASI changed before deployment.");

        // The ASI is now an identity mapping to the real game file, not a
        // second virtual copy. All other staged files and mappings are unchanged.
        var remaining = manifest.Where(f => !f.GameRelativePath.Equals(CetAsiRelative,
            StringComparison.OrdinalIgnoreCase)).ToArray();
        var passthrough = journal["physicalPassthrough"]!.Deserialize<string[]>() ??
            throw new InvalidDataException("Physical CET dependency manifest missing.");
        if (passthrough.Contains(CetAsiRelative, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Conflicting CET ASI deployment route.");
        journal["generatedFiles"] = JsonSerializer.SerializeToNode(remaining);
        journal["physicalPassthrough"] = JsonSerializer.SerializeToNode(passthrough.Append(CetAsiRelative)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray());

        // Persist the recovery authority BEFORE mutating the live ASI.
        // The journal stays 'prepared' until dispatch; interruption before
        // dispatch can be restored without restarting MO2.
        WriteJournal(prepared.JournalFile, journal);
        using (var durable = new FileStream(prepared.JournalFile, FileMode.Open, FileAccess.ReadWrite))
            durable.Flush(flushToDisk: true);
        try
        {
            ReplacePhysicalCet(physical, staged, physical.OriginalHash, physical.InstalledHash);
            File.Delete(staged);
            if (File.Exists(staged))
                throw new IOException("The redundant virtual CET ASI could not be retired.");
        }
        catch
        {
            // No launch request has been sent. Attempt immediate rollback;
            // if rollback fails, the durable journal remains authoritative.
            RestorePhysicalCet(JsonSerializer.SerializeToElement(journal));
            throw;
        }
    }

    // Only a matching, acknowledged capture may use the verified original
    // CET binary as its framework version input while the profiler is live.
    internal static string? VerifiedOriginalCetForFrameworkCheck(
        Mo2OverlayPreparation prepared, Mo2CaptureIdentity identity, Mo2Stack selected)
    {
        EnsureNoReparseParents(prepared.JournalFile);
        RequireRegularUnlinkedFile(prepared.JournalFile);
        using var doc = JsonDocument.Parse(File.ReadAllText(prepared.JournalFile));
        var journal = doc.RootElement;
        if (journal.GetProperty("phase").GetString() != "test-launched" ||
            journal.GetProperty("sessionId").GetString() != identity.SessionId ||
            prepared.SessionId != identity.SessionId ||
            !string.Equals(journal.GetProperty("profile").GetString(), identity.Profile,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(journal.GetProperty("gameRoot").GetString(), identity.GameRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(selected.Profile, identity.Profile, StringComparison.OrdinalIgnoreCase) ||
            !selected.Ready || !selected.ProfileVerified || selected.GameOnlyFallback)
            return null;
        var state = ReadPhysicalCet(journal);
        if (state is null ||
            !selected.EffectiveFiles.TryGetValue(CetAsiRelative, out var winner) ||
            winner.FromMo2 ||
            !string.Equals(Path.GetFullPath(winner.PhysicalPath), state.Target, StringComparison.OrdinalIgnoreCase) ||
            !Hash(state.Target).Equals(state.InstalledHash, StringComparison.OrdinalIgnoreCase))
            return null;
        RequirePhysicalCetBackup(state);
        return state.Backup;
    }

    private static PhysicalCetState? ReadPhysicalCet(JsonElement journal)
    {
        if (!journal.TryGetProperty("physicalCet", out var native)) return null;
        var id = journal.GetProperty("sessionId").GetString() ?? "";
        if (!SessionIdPattern.IsMatch(id) || journal.GetProperty("schemaVersion").GetInt32() != 1)
            throw new InvalidDataException("Invalid physical CET recovery identity.");
        var game = Path.GetFullPath(journal.GetProperty("gameRoot").GetString()!);
        var stage = Path.GetFullPath(journal.GetProperty("stageRoot").GetString()!);
        var fixture = Path.GetFullPath(journal.GetProperty("fixtureRoot").GetString()!);
        var bridge = Path.GetDirectoryName(Path.GetDirectoryName(stage)!)!;
        if (!stage.Equals(Path.Combine(bridge, "staging", id), StringComparison.OrdinalIgnoreCase) ||
            !fixture.Equals(Path.Combine(bridge, "fixtures", id), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Physical CET recovery storage identity changed.");
        var target = Path.GetFullPath(Path.Combine(game, CetAsiRelative));
        var backup = Path.Combine(fixture, CetOwnerBackupRelative);
        var original = native.GetProperty("originalSha256").GetString() ?? "";
        var installed = native.GetProperty("installedSha256").GetString() ?? "";
        if (original.Length != 64 || !original.All(Uri.IsHexDigit) ||
            installed.Length != 64 || !installed.All(Uri.IsHexDigit) ||
            original.Equals(installed, StringComparison.OrdinalIgnoreCase) ||
            !original.Equals(ReadAsiProperty(journal.GetProperty("originalSources")),
                StringComparison.OrdinalIgnoreCase) ||
            !target.Equals(ReadAsiProperty(journal.GetProperty("originalSourcePaths")),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Physical CET source identity changed.");
        EnsureNoReparseParents(target);
        RequireRegularUnlinkedFile(target);
        // Never compete with a Direct CET owner transaction.
        var liveState = Path.Combine(game, CetOwnerStateRelative);
        if (Directory.Exists(liveState) || File.Exists(liveState))
            throw new InvalidOperationException("An existing physical CET profiler manager transaction must be restored first.");
        return new(id, target, backup, original, installed);
    }

    private static string ReadAsiProperty(JsonElement inventory)
    {
        var matches = inventory.EnumerateObject().Where(p =>
            p.Name.Equals(CetAsiRelative, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1 || matches[0].Value.GetString() is not string value)
            throw new InvalidDataException("Original CET ASI identity is missing or ambiguous.");
        return value;
    }

    private static void RequirePhysicalCetBackup(PhysicalCetState state)
    {
        EnsureNoReparseParents(state.Backup);
        RequireRegularUnlinkedFile(state.Backup);
        if (!Hash(state.Backup).Equals(state.OriginalHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("CET owner's original ASI backup failed verification.");
    }

    private static void ReplacePhysicalCet(PhysicalCetState state, string source,
        string expectedCurrent, string desired)
    {
        RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before changing the native CET profiler.");
        EnsureNoReparseParents(state.Target);
        EnsureNoReparseParents(source);
        RequireRegularUnlinkedFile(state.Target);
        RequireRegularUnlinkedFile(source);
        RequirePhysicalCetBackup(state);
        if (!Hash(state.Target).Equals(expectedCurrent, StringComparison.OrdinalIgnoreCase) ||
            !Hash(source).Equals(desired, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Physical CET ASI or owner payload changed; no replacement made.");

        // Stage on the same volume, then atomically replace. A partial copy
        // cannot corrupt the live ASI. A crash leaves the owner backup intact.
        var temp = state.Target + ".gco-" + state.SessionId + "-" + Guid.NewGuid().ToString("N") + ".tmp";
        bool created = false;
        try
        {
            using (var input = File.OpenRead(source))
            using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                created = true;
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }
            if (!Hash(temp).Equals(desired, StringComparison.OrdinalIgnoreCase) ||
                !Hash(state.Target).Equals(expectedCurrent, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Physical CET replacement verification failed.");
            RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before changing the native CET profiler.");
            File.Replace(temp, state.Target, null);
            if (!Hash(state.Target).Equals(desired, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Physical CET ASI replacement did not verify.");
        }
        finally
        {
            if (created && File.Exists(temp))
            {
                RequireRegularUnlinkedFile(temp);
                File.Delete(temp);
            }
        }
    }

    // Shared RESTORE, repeated START, CONTINUE and deferred VERIFY retirement.
    // Unknown user-modified ASI bytes are never overwritten.
    private static void RestorePhysicalCet(JsonElement journal)
    {
        var state = ReadPhysicalCet(journal);
        if (state is null) return; // Old journals and virtual CET unchanged.
        RequireNoProcess("Cyberpunk2077", "Close Cyberpunk before restoring the native CET profiler.");
        var current = Hash(state.Target);
        if (!current.Equals(state.OriginalHash, StringComparison.OrdinalIgnoreCase))
        {
            if (!current.Equals(state.InstalledHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Physical CET ASI changed outside GCO; original backup preserved.");
            ReplacePhysicalCet(state, state.Backup, state.InstalledHash, state.OriginalHash);
        }
        if (!Hash(state.Target).Equals(state.OriginalHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Original physical CET ASI could not be verified.");
        // A power loss may leave a same-volume temporary copy. Only remove a
        // regular, exact known-byte copy of this session; preserve unknown data.
        var prefix = Path.GetFileName(state.Target) + ".gco-" + state.SessionId + "-";
        foreach (var temp in Directory.EnumerateFiles(Path.GetDirectoryName(state.Target)!, prefix + "*.tmp"))
        {
            var name = Path.GetFileName(temp);
            var suffix = name[prefix.Length..];
            if (suffix.Length != 36 || !suffix.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                !suffix[..32].All(Uri.IsHexDigit)) continue;
            RequireRegularUnlinkedFile(temp);
            var hash = Hash(temp);
            if (hash.Equals(state.OriginalHash, StringComparison.OrdinalIgnoreCase) ||
                hash.Equals(state.InstalledHash, StringComparison.OrdinalIgnoreCase))
                File.Delete(temp);
            else
                System.Diagnostics.Trace.TraceWarning("Unrecognized physical CET recovery temporary file preserved: " + temp);
        }
    }
}
