using System.Text;
using System.Text.Json;

namespace GRedscript.Resolver;

// Isolated Pass 4 development entry point. Integration into the existing
// RedscriptGenerationPlan remains the integration agent's responsibility.
internal static class Pass4Program
{
    private sealed record OwnerOutcome(
        string RuleId, string Status, int AppliedEdits, int AlreadyComplete,
        string? Reason, IReadOnlyList<string> SourcePaths);
    private sealed record Report(
        string SchemaVersion, string Status, int AppliedEdits,
        int AlreadyComplete, IReadOnlyList<OwnerOutcome> Operations,
        IReadOnlyList<string> SourcePaths, string SourceStackSha256,
        bool SourceUnchanged, bool CompilerValidated, bool GameplayValidated);

#if PASS4_CLI
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PASS4_GENERATION_FAILED: " + ex);
            return 1;
        }
    }
#endif

    // This same source-aware generator is used by the production plan.
    // The production build has no second CLI entry point.
    internal static void Generate(string gameRoot,string outputRoot,
        string? baseOverlayRoot=null, bool uniqueGeometryOnly=false)
    {
        var args=new List<string> {"pass4","--game",gameRoot,"--out",outputRoot};
        if(baseOverlayRoot is not null) {
            args.Add("--base-overlay");
            args.Add(baseOverlayRoot);
        }
        if(uniqueGeometryOnly) args.Add("--integration-geometry-only");
        Run(args.ToArray());
    }

    private static void Run(string[] args)
    {
        if (args.Length < 5 || args[0] != "pass4")
            throw new ArgumentException("Usage: pass4 --game ROOT --out OUTPUT [--base-overlay ROOT]");
        string Arg(string key) {
            int i = Array.IndexOf(args, key);
            if (i < 1 || i + 1 >= args.Length || args[i + 1].StartsWith("--",StringComparison.Ordinal))
                throw new ArgumentException("Missing " + key);
            return args[i + 1];
        }
        var game = Path.GetFullPath(Arg("--game"));
        var output = Path.GetFullPath(Arg("--out"));
        string? overlay = Array.IndexOf(args, "--base-overlay") >= 0
            ? Path.GetFullPath(Arg("--base-overlay")) : null;
        if (!Directory.Exists(Path.Combine(game,"r6","scripts")) ||
            Directory.Exists(output) || File.Exists(output) || Inside(output,game))
            throw new InvalidDataException("PASS4_OUTPUT_OR_SOURCE_UNSAFE");
        if (overlay is not null && (!Directory.Exists(overlay) ||
            Inside(overlay,game) || Inside(output,overlay) || Inside(overlay,output)))
            throw new InvalidDataException("PASS4_OVERLAY_UNSAFE");

        var admission = new SourceAdmission(game);
        admission.AuditAll();
        string sha = admission.SourceStackSha256 ??
            throw new InvalidDataException("PASS4_SOURCE_FINGERPRINT_MISSING");
        var changes = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var operations = new List<OwnerOutcome>();

        void RunOwner(string id, Func<string,string?,Pass4Rewrite> rewrite)
        {
            string owner = id switch {
                "STEALTHRUNNER_NPC_PLAYER_CACHE_V1" => "StealthRunner",
                "CHIPWARE_PERK_RELEVANCE_GATE_V1" => "zzy_bChipwareExpansion",
                _ => "ReinforcementsSystem"
            };
            string ownerRoot=Path.Combine(game,"r6","scripts",owner);
            if (!Directory.Exists(ownerRoot)) {
                operations.Add(new(id,"NOT_APPLICABLE",0,0,null,[]));
                return;
            }
            var changesBefore=changes.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var states = new List<Pass4Rewrite>();
            var candidates = Directory.EnumerateFiles(ownerRoot,"*.reds",SearchOption.AllDirectories)
                .Where(x => id switch {
                    "STEALTHRUNNER_NPC_PLAYER_CACHE_V1" =>
                        File.ReadAllText(x).Contains("OnPlayerDetectionChangedEvent",StringComparison.Ordinal),
                    "CHIPWARE_PERK_RELEVANCE_GATE_V1" =>
                        System.Text.RegularExpressions.Regex.IsMatch(
                            File.ReadAllText(x),
                            @"@wrapMethod\s*\(\s*PlayerDevelopmentData\s*\)[\s\S]{0,160}\bfunc\s+IsNewPerkBought\s*\("),
                    _ => System.Text.RegularExpressions.Regex.IsMatch(
                            File.ReadAllText(x),@"\bfunc\s+ReinforcementsChecksCall\s*\(")
                }).ToArray();
            if(candidates.Length != 1) {
                operations.Add(new(id,"REFUSED",0,0,"PASS4_OWNER_TARGET_MISSING_OR_AMBIGUOUS",[]));
                return;
            }
            string target=candidates[0];
            if((File.GetAttributes(target)&FileAttributes.ReparsePoint)!=0)
                throw new InvalidDataException("PASS4_SYMLINK_REFUSED");
            string relative="r6/scripts/"+Path.GetRelativePath(
                Path.Combine(game,"r6","scripts"),target).Replace('\\','/');
            string current=Load(target,relative,overlay);
            Pass4Rewrite outcome=rewrite(current,target);
            states.Add(outcome);
            if (outcome.Changed is not null)
                changes[relative]=outcome.Changed;

            if(id=="REINFORCEMENTS_PREPARED_QUAD_CACHE_V1" && outcome.Status != "REFUSED") {
                var matches=Directory.EnumerateFiles(ownerRoot,"*.reds",SearchOption.AllDirectories)
                    .Where(x=>File.ReadAllText(x).Contains("public func IsPointInQuad(",StringComparison.Ordinal))
                    .ToArray();
                if(matches.Length != 1) {
                    changes.Remove(relative);
                    operations.Add(new(id,"REFUSED",0,0,"PASS4_GEOMETRY_OWNER_NOT_UNIQUE",[]));
                    return;
                }
                string util=matches[0];
                string utilRel="r6/scripts/"+Path.GetRelativePath(
                    Path.Combine(game,"r6","scripts"),util).Replace('\\','/');
                var next=ReinforcementsPass.RewriteUtils(Load(util,utilRel,overlay));
                if(next.Status=="REFUSED") {
                    changes.Remove(relative);
                    operations.Add(new(id,"REFUSED",0,0,next.Reason,[]));
                    return;
                }
                states.Add(next);
                if(next.Changed is not null) changes[utilRel]=next.Changed;
            }
            string status=states.Any(x=>x.Status=="APPLIED")?"APPLIED":
                states.All(x=>x.Status=="COMPLETE")?"COMPLETE":"REFUSED";
            operations.Add(new(id,status,
                states.Sum(x=>x.AppliedEdits),states.Sum(x=>x.AlreadyComplete),
                string.Join("; ", states.Select(x=>x.Reason)
                    .Where(x=>!string.IsNullOrWhiteSpace(x))),
                changes.Keys.Except(changesBefore,StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray()));
        }

        // Integration uses Pass 7's equivalent player cache and Pass 8's
        // equivalent source-derived perk admission. Never emit duplicates.
        // The standalone Pass 4 generator still exposes all three owners.
#if PASS4_CLI
        // Historical isolated reference remains available for the Pass 4 test
        // host only. Production compiles neither duplicate generator.
        if (!args.Contains("--integration-geometry-only",StringComparer.Ordinal))
        {
            RunOwner("STEALTHRUNNER_NPC_PLAYER_CACHE_V1",
                (s,_)=>StealthRunnerPass.Rewrite(s));
            RunOwner("CHIPWARE_PERK_RELEVANCE_GATE_V1",
                (s,_)=>ChipwarePass.Rewrite(s));
        }
#endif
        RunOwner("REINFORCEMENTS_PREPARED_QUAD_CACHE_V1",
            (s,_)=>ReinforcementsPass.RewriteSystem(s));

        if(!string.Equals(sha,admission.FingerprintCurrentSources(),
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("PASS4_INPUT_CHANGED_DURING_GENERATION");
        Directory.CreateDirectory(output);
        foreach(var (rel,content) in changes) {
            string target=Path.GetFullPath(Path.Combine(output,
                rel.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(target,output)||!target.EndsWith(".reds",StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("PASS4_PATH_ESCAPE");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target,content,new UTF8Encoding(false));
        }
        var report=new Report("gco.phase2.pass4.isolated.v1",
            changes.Count>0?"APPLIED":operations.All(x=>x.Status=="NOT_APPLICABLE")?
            "NOT_APPLICABLE":operations.Any(x=>x.Status=="REFUSED")?
            "REFUSED":"COMPLETE",operations.Sum(x=>x.AppliedEdits),
            operations.Sum(x=>x.AlreadyComplete),operations,
            changes.Keys.OrderBy(x=>x,StringComparer.OrdinalIgnoreCase).ToArray(),
            sha,true,false,false);
        File.WriteAllText(Path.Combine(output,"pass4-result.json"),
            JsonSerializer.Serialize(report,new JsonSerializerOptions {
                PropertyNamingPolicy=JsonNamingPolicy.CamelCase,WriteIndented=true
            }),new UTF8Encoding(false));
        Console.WriteLine($"PASS4 status={report.Status} edits={report.AppliedEdits} sources={changes.Count}");
    }
    private static string Load(string path,string relative,string? overlay)
    {
        if(overlay is not null) {
            string candidate=Path.GetFullPath(Path.Combine(overlay,
                relative.Replace('/',Path.DirectorySeparatorChar)));
            if(!Inside(candidate,overlay))
                throw new InvalidDataException("PASS4_OVERLAY_ESCAPE");
            if(File.Exists(candidate)) return File.ReadAllText(candidate,Encoding.UTF8);
        }
        return File.ReadAllText(path,Encoding.UTF8);
    }
    private static bool Inside(string path,string root) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+
            Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
}
