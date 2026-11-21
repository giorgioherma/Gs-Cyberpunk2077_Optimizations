using GRedscript.Resolver.Phase2;

if (args.Length < 4 || args.Length % 2 != 0)
    throw new ArgumentException("Pass 8 checkpoint requires --game and --out");
var options = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i += 2)
    if (!options.TryAdd(args[i], args[i+1]))
        throw new ArgumentException("Repeated Pass 8 option: " + args[i]);
if (options.Keys.Any(k => k is not ("--game" or "--out" or "--base-overlay")) ||
    !options.TryGetValue("--game", out var game) ||
    !options.TryGetValue("--out", out var output))
    throw new ArgumentException("Unknown or missing Pass 8 argument");
var report = Pass8ThroughputPass.Generate(game, output,
    options.GetValueOrDefault("--base-overlay"));
Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report));
