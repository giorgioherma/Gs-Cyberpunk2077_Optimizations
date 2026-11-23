using GRedscript.Resolver.Phase2;

if (args.Length < 4 || args[0] != "--game" || args[2] != "--out")
{
    Console.Error.WriteLine("Usage: --game <post-generic-root> --out <overlay-output> [--base-overlay <existing-output>]");
    return 2;
}
if (args.Length != 4 && (args.Length != 6 || args[4] != "--base-overlay"))
{
    Console.Error.WriteLine("Pass9 optional --base-overlay arguments invalid");
    return 2;
}
try
{
    var result = RunningManDoubleSprintPass.Generate(args[1], args[3],
        args.Length == 6 ? args[5] : null);
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result));
    return result.Status == "REFUSED" ? 3 : 0;
}
catch (Exception e)
{
    Console.Error.WriteLine(e);
    return 1;
}
