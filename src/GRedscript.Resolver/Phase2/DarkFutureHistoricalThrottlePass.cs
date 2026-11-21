using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GRedscript.Resolver.Phase2;

/// <summary>
/// Restore Dark Future's game-tested aggressive Pass 6/7 UI throttling:
/// ten callbacks for unchanged-count buff layout, six for wheel effect
/// reconstruction, and six for non-urgent SingleCooldown rendering.
/// Compose after the existing immutable-tag cache and tagged-icon cadence;
/// never overwrite those semantics or the post-generic source.
/// </summary>
internal static class DarkFutureHistoricalThrottlePass
{
    internal const string Rule = "DARK_FUTURE_HISTORICAL_AGGRESSIVE_THROTTLES_V1";

    internal sealed record Result(
        string RuleId, string Status, int AppliedEdits, int AlreadyComplete,
        string? SourcePath, string? Reason, bool SourceUnchanged,
        bool GameplayValidated);

    private sealed record Method(
        string Owner, string Name, int Annotation, int Open, int Close);
    private sealed record Edit(int Start, int End, string Text);
    private sealed record Operation(
        string Name, string Owner, string MethodName,
        (string Name, string Type)[] Fields, string Prelude);
    private sealed record Outcome(
        string Status, string? Reason, IReadOnlyList<Edit> Edits);

    private const string BuffPrelude = """
        let currentBuffCount = ArraySize(this.m_buffDataList);
        let currentDebuffCount = ArraySize(this.m_debuffDataList);
        let structuralChange = !this.m_gredBuffUpdateInitialized
          || currentBuffCount != this.m_gredBuffCount
          || currentDebuffCount != this.m_gredDebuffCount;

        if !structuralChange {
          this.m_gredBuffUpdatePhase += 1;
          if this.m_gredBuffUpdatePhase < 10 {
            return;
          };
        };

        this.m_gredBuffUpdateInitialized = true;
        this.m_gredBuffCount = currentBuffCount;
        this.m_gredDebuffCount = currentDebuffCount;
        this.m_gredBuffUpdatePhase = 0;
        """;

    private const string EffectPrelude = """
        this.m_gredEffectUpdatePhase += 1;
        if this.m_gredEffectUpdatePhase < 6 {
          return false;
        };
        this.m_gredEffectUpdatePhase = 0;
        """;

    private const string CooldownPrelude = """
        if this.m_gredRenderInitialized
          && timeLeft == -1.00
          && this.m_gredLastTimeLeft == -1.00
          && Equals(stackCount, this.m_gredLastStackCount) {
          return;
        };

        let urgent = !this.m_gredRenderInitialized
          || NotEquals(stackCount, this.m_gredLastStackCount)
          || (timeLeft >= 0.00 && timeLeft <= this.m_outroDuration);

        if !urgent {
          this.m_gredRenderPhase += 1;
          if this.m_gredRenderPhase < 6 {
            return;
          };
        };

        this.m_gredRenderInitialized = true;
        this.m_gredRenderPhase = 0;
        this.m_gredLastTimeLeft = timeLeft;
        this.m_gredLastStackCount = stackCount;
        """;

    private static readonly Operation[] Operations =
    [
        new("buff-list-ten", "buffListGameController",
            "UpdateBuffDebuffList",
            [
                ("m_gredBuffUpdateInitialized", "Bool"),
                ("m_gredBuffCount", "Int32"),
                ("m_gredDebuffCount", "Int32"),
                ("m_gredBuffUpdatePhase", "Int32")
            ], BuffPrelude),
        new("effect-wheel-six", "inkCooldownGameController",
            "OnEffectUpdate",
            [("m_gredEffectUpdatePhase", "Int32")], EffectPrelude),
        new("cooldown-render-six", "SingleCooldownManager",
            "Update",
            [
                ("m_gredRenderInitialized", "Bool"),
                ("m_gredRenderPhase", "Int32"),
                ("m_gredLastTimeLeft", "Float"),
                ("m_gredLastStackCount", "Uint32")
            ], CooldownPrelude)
    ];

    private static int Close(IReadOnlyList<SourceAdmission.Token> t,
        int start, string opening, string closing)
    {
        if (start < 0 || start >= t.Count || t[start].Value != opening)
            return -1;
        int depth = 0;
        for (int i = start; i < t.Count; i++)
        {
            if (t[i].Value == opening) depth++;
            else if (t[i].Value == closing && --depth == 0) return i;
        }
        return -1;
    }

    private static IReadOnlyList<Method> FindMethods(string source)
    {
        var t = SourceAdmission.ReadTokens(source);
        var result = new List<Method>();
        for (int i = 0; i + 5 < t.Count; i++)
        {
            if (t[i].Value != "@" ||
                t[i + 1].Value != "replaceMethod" ||
                t[i + 2].Value != "(" ||
                t[i + 4].Value != ")" ||
                !Operations.Any(o => o.Owner == t[i + 3].Value))
                continue;
            int func = i + 5;
            while (func + 2 < t.Count && func < i + 40 &&
                t[func].Value is not ("func" or "@"))
                func++;
            if (func + 2 >= t.Count || t[func].Value != "func" ||
                t[func + 2].Value != "(")
                continue;
            string name = t[func + 1].Value;
            if (!Operations.Any(o => o.Owner == t[i + 3].Value &&
                o.MethodName == name))
                continue;
            int argsEnd = Close(t, func + 2, "(", ")");
            if (argsEnd < 0) continue;
            int open = argsEnd + 1;
            while (open < t.Count &&
                t[open].Value is not ("{" or ";"))
                open++;
            if (open >= t.Count || t[open].Value != "{")
                continue;
            int close = Close(t, open, "{", "}");
            if (close < 0) continue;
            result.Add(new(t[i + 3].Value, name,
                t[i].Start, t[open].End, t[close].Start));
            i = close;
        }
        return result;
    }

    private static string TokenShape(string text) =>
        string.Join("\u001f", SourceAdmission.ReadTokens(text)
            .Select(t => t.Value));

    private static bool HasSequence(
        IReadOnlyList<SourceAdmission.Token> tokens,
        IReadOnlyList<SourceAdmission.Token> probe)
    {
        for (int i = 0; i + probe.Count <= tokens.Count; i++)
            if (probe.Select((t, j) =>
                tokens[i + j].Value == t.Value).All(x => x))
                return true;
        return false;
    }

    private static bool HasCompletePrelude(
        string body, string canonical)
    {
        // Read the method's real tokens, not markers. The complete
        // historical control-flow block must appear in the correct order.
        // This also accepts whitespace/comments and unrelated statements.
        var tokens = SourceAdmission.ReadTokens(body);
        var target = SourceAdmission.ReadTokens(canonical);
        return HasSequence(tokens, target);
    }

    private static string? ConfirmOwnerLogic(
        string source, Method method, Operation op)
    {
        string body = source[method.Open..method.Close];
        if (op.Name == "buff-list-ten")
        {
            if (!body.Contains("m_buffDataList", StringComparison.Ordinal) ||
                !body.Contains("m_debuffDataList", StringComparison.Ordinal) ||
                !body.Contains("m_buffWidgets", StringComparison.Ordinal) ||
                !body.Contains("SendVisibilityUpdate", StringComparison.Ordinal) ||
                !body.Contains("SetStatusEffectRecord", StringComparison.Ordinal))
                return "DF_AGGRESSIVE_BUFF_RENDER_LIFECYCLE_UNPROVEN";
        }
        else if (op.Name == "effect-wheel-six")
        {
            if (!Regex.IsMatch(body,
                    @"\bif\s*!\s*this\s*\.\s*GetRootWidget\s*\(\s*\)\s*\.\s*IsVisible\s*\(\s*\)") ||
                !body.Contains("ParseBuffList", StringComparison.Ordinal) ||
                !body.Contains("GetBuffs(", StringComparison.Ordinal) ||
                !body.Contains("GetDebuffs(", StringComparison.Ordinal))
                return "DF_AGGRESSIVE_EFFECT_WHEEL_OWNER_UNPROVEN";
        }
        else
        {
            if (!Regex.IsMatch(source,
                    @"@\s*wrapMethod\s*\(\s*SingleCooldownManager\s*\)" +
                    @"(?:(?!@\s*(?:wrapMethod|replaceMethod)).)*\bfunc\s+ActivateCooldown\s*\(",
                    RegexOptions.Singleline) ||
                !body.Contains("m_initialDuration", StringComparison.Ordinal) ||
                !body.Contains("m_outroDuration", StringComparison.Ordinal) ||
                !body.Contains("FillOutroAnimationStart()", StringComparison.Ordinal) ||
                !body.Contains("this.SetTimeRemaining(timeLeft)",
                    StringComparison.Ordinal) ||
                !body.Contains("this.SetStackCount(", StringComparison.Ordinal) ||
                !body.Contains("DarkFutureInfiniteDurationEffect",
                    StringComparison.Ordinal))
                return "DF_AGGRESSIVE_COOLDOWN_LIFECYCLE_UNPROVEN";
        }
        return null;
    }

    private static Outcome InspectOperation(
        string source, Method method, Operation op)
    {
        string? unsafeLogic = ConfirmOwnerLogic(source, method, op);
        if (unsafeLogic is not null)
            return new("REFUSED", unsafeLogic, []);
        var edits = new List<Edit>();
        var tokens = SourceAdmission.ReadTokens(source);
        string body = source[method.Open..method.Close];

        bool complete = HasCompletePrelude(body, op.Prelude);
        // The state names may appear in unrelated extensions. Accept
        // only our historical guard shape, never duplicate a partial guard.
        bool partial = op.Fields.Any(f => SourceAdmission.ReadTokens(body)
            .Any(x => x.Value == f.Name)) && !complete;
        if (partial)
            return new("REFUSED",
                "DF_AGGRESSIVE_PARTIAL_GUARD_CONTROL_FLOW_UNPROVEN:" +
                op.Name, []);

        string nl = source.Contains("\r\n", StringComparison.Ordinal)
            ? "\r\n" : "\n";

        foreach (var field in op.Fields)
        {
            string escaped = Regex.Escape(field.Name);
            var declarations = Regex.Matches(source,
                @"@\s*addField\s*\(\s*" + op.Owner + @"\s*\)" +
                @"\s*(?:(?:private|public|protected)\s+)?let\s+" +
                escaped + @"\s*:\s*(?<type>\w+)\s*;");
            if (declarations.Count > 1 ||
                (declarations.Count == 1 &&
                 declarations[0].Groups["type"].Value != field.Type))
                return new("REFUSED",
                    "DF_AGGRESSIVE_CACHE_FIELD_COLLISION:" +
                    field.Name, []);
            if (declarations.Count == 0)
            {
                // Do not shadow a manually defined different field or
                // helper in the same source.
                if (tokens.Any((t) => t.Value == field.Name))
                {
                    var possible = Regex.Matches(source,
                        @"\blet\s+" + escaped + @"\s*:");
                    if (possible.Count > 0)
                        return new("REFUSED",
                            "DF_AGGRESSIVE_UNOWNED_FIELD:" + field.Name, []);
                }
                edits.Add(new(method.Annotation, method.Annotation,
                    "@addField(" + op.Owner + ")" + nl +
                    "private let " + field.Name + ": " + field.Type +
                    ";" + nl + nl));
            }
        }

        if (!complete)
        {
            string prelude = nl + "    " +
                op.Prelude.Replace("\n", nl + "    ").TrimEnd() +
                nl + "    ";
            edits.Add(new(method.Open, method.Open, prelude));
        }
        return new(edits.Count == 0 ? "COMPLETE" : "APPLIED",
            null, edits);
    }

    private static (string Status, int Edits, int Complete,
        string? Reason, string? Updated) Rewrite(string source)
    {
        var methods = FindMethods(source);
        var edits = new List<Edit>();
        var refusal = new List<string>();
        int applied = 0, complete = 0;
        foreach (var op in Operations)
        {
            var targets = methods.Where(m =>
                m.Owner == op.Owner && m.Name == op.MethodName).ToArray();
            if (targets.Length != 1)
            {
                refusal.Add(op.Name + ":DF_AGGRESSIVE_CALLBACK_MISSING_OR_AMBIGUOUS");
                continue;
            }
            var state = InspectOperation(source, targets[0], op);
            if (state.Status == "REFUSED")
                refusal.Add(op.Name + ":" + state.Reason);
            else if (state.Status == "COMPLETE")
                complete++;
            else
            {
                applied++;
                edits.AddRange(state.Edits);
            }
        }
        if (edits.Count == 0)
            return (refusal.Count > 0 ? "REFUSED" : "COMPLETE",
                0, complete,
                refusal.Count > 0 ? string.Join("; ", refusal) : null, null);

        // Insertions at a single annotation are ordered deterministically.
        // Each field name is distinct, so staged dependencies cannot collide.
        string rewritten = source;
        foreach (var edit in edits.OrderByDescending(x => x.Start)
            .ThenByDescending(x => x.Text, StringComparer.Ordinal))
            rewritten = rewritten[..edit.Start] + edit.Text +
                rewritten[edit.End..];

        var after = FindMethods(rewritten);
        foreach (var op in Operations)
        {
            if (refusal.Any(x => x.StartsWith(op.Name + ":",
                StringComparison.Ordinal)))
                continue;
            var target = after.Single(x => x.Owner == op.Owner &&
                x.Name == op.MethodName);
            var state = InspectOperation(rewritten, target, op);
            if (state.Status != "COMPLETE")
                throw new InvalidDataException(
                    "DF_AGGRESSIVE_GENERATION_POSTCONDITION:" + op.Name +
                    ":" + state.Reason);
        }
        return ("APPLIED", applied, complete,
            refusal.Count > 0 ? string.Join("; ", refusal) : null,
            rewritten);
    }

    internal static Result Generate(
        string gameRoot, string outputRoot, string? basePreviewRoot = null)
    {
        string game = Path.GetFullPath(gameRoot);
        string output = Path.GetFullPath(outputRoot);
        string scripts = Path.Combine(game, "r6", "scripts");
        if (!Directory.Exists(scripts) ||
            Directory.Exists(output) || File.Exists(output) ||
            Inside(output, game) ||
            output.Equals(game, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("DF_AGGRESSIVE_OUTPUT_UNSAFE");
        if (basePreviewRoot is not null &&
            (!Directory.Exists(basePreviewRoot) ||
             Inside(basePreviewRoot, game) ||
             Inside(output, basePreviewRoot) ||
             Inside(basePreviewRoot, output)))
            throw new InvalidDataException("DF_AGGRESSIVE_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        _ = admission.AuditAll();
        string fingerprint = admission.SourceStackSha256 ??
            throw new InvalidDataException("DF_AGGRESSIVE_HASH_MISSING");
        string owner = Path.Combine(scripts, "Dark Future");
        string status = "NOT_APPLICABLE";
        string? reason = null, relative = null, updated = null;
        int applied = 0, complete = 0;
        if (Directory.Exists(owner))
        {
            var targets = Directory.EnumerateFiles(owner, "*.reds",
                SearchOption.AllDirectories)
                .Where(path => File.ReadAllText(path, Encoding.UTF8)
                    .Contains("UpdateBuffDebuffList",
                        StringComparison.Ordinal))
                .ToArray();
            if (targets.Length != 1)
            {
                status = "REFUSED";
                reason = "DF_AGGRESSIVE_OWNER_MISSING_OR_AMBIGUOUS";
            }
            else
            {
                string file = targets[0];
                if ((File.GetAttributes(file) &
                    FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("DF_AGGRESSIVE_OWNER_LINK");
                relative = "r6/scripts/" +
                    Path.GetRelativePath(scripts, file).Replace('\\', '/');
                string effective = File.ReadAllText(file, Encoding.UTF8);
                if (basePreviewRoot is not null)
                {
                    string sourcePath = Path.GetFullPath(Path.Combine(
                        basePreviewRoot,
                        relative.Replace('/',
                            Path.DirectorySeparatorChar)));
                    if (!Inside(sourcePath, basePreviewRoot))
                        throw new InvalidDataException(
                            "DF_AGGRESSIVE_OVERLAY_ESCAPE");
                    if (File.Exists(sourcePath))
                        effective = File.ReadAllText(sourcePath,
                            Encoding.UTF8);
                }
                var result = Rewrite(effective);
                status = result.Status;
                applied = result.Edits;
                complete = result.Complete;
                reason = result.Reason;
                updated = result.Updated;
            }
        }
        if (!string.Equals(fingerprint,
            admission.FingerprintCurrentSources(),
            StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "DF_AGGRESSIVE_SOURCE_CHANGED");

        Directory.CreateDirectory(output);
        if (updated is not null && relative is not null)
        {
            string file = Path.GetFullPath(Path.Combine(output,
                relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!Inside(file, output) ||
                !file.EndsWith(".reds", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    "DF_AGGRESSIVE_OUTPUT_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, updated, new UTF8Encoding(false));
        }
        var report = new Result(Rule, status, applied, complete,
            relative, reason, true, false);
        File.WriteAllText(Path.Combine(output, "phase2-result.json"),
            JsonSerializer.Serialize(report, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }), new UTF8Encoding(false));
        return report;
    }

    private static bool Inside(string path, string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
