using System.Reflection;

if (args.Length is < 2 or > 3)
{
    Console.Error.WriteLine("Usage: Pass5Harness <game> <out> [prior-overlay]");
    return 2;
}
try
{
    var type = typeof(GRedscript.Resolver.Program).Assembly.GetType(
        "GRedscript.Resolver.Phase2.HoloTravelMaterialEventPass", throwOnError: true)!;
    var method = type.GetMethod("Generate",
        BindingFlags.Static | BindingFlags.NonPublic)!;
    method.Invoke(null, [args[0], args[1],
        args.Length == 3 ? args[2] : null]);
    return 0;
}
catch (TargetInvocationException exception)
{
    Console.Error.WriteLine(exception.InnerException?.ToString() ?? exception.ToString());
    return 1;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.ToString());
    return 1;
}
