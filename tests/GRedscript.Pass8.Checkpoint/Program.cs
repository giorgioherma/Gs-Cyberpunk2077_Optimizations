using GRedscript.Resolver.Phase2;

if (args.Length < 4 || args.Length % 2 != 0)
    throw new ArgumentException("Pass 8 checkpoint requires --game and --out");
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
    if (!options.TryAdd(args[i], args[i+1]))
        throw new ArgumentException("Repeated Pass 8 option: " + args[i]);
if (options.Keys.Any(k => k is not ("--game" or "--out" or "--base-overlay" or "--owner")) ||
    !options.TryGetValue("--game", out var game) ||
    !options.TryGetValue("--out", out var output))
    throw new ArgumentException("Unknown or missing Pass 8 argument");
object report = (options.GetValueOrDefault("--owner") ?? "chipware") switch
{
    "chipware" => Pass8ThroughputPass.Generate(game, output,
        options.GetValueOrDefault("--base-overlay")),
    "immersive" => Pass8ImmersiveCyberwarePass.Generate(game, output,
        options.GetValueOrDefault("--base-overlay")),
    "combat" => Pass8ChipwareCombatCachePass.Generate(game, output,
        options.GetValueOrDefault("--base-overlay")),
    _ => throw new ArgumentException("Unsupported Pass 8 owner")
};
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
