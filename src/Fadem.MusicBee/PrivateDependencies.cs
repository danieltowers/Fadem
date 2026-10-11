using System.Reflection;

namespace Fadem.MusicBee;

/// <summary>
/// Loads the plugin's dependencies from the <c>fadem</c> folder beside it (ADR 0012). MusicBee
/// has no binding redirects for them, and another plugin may ship its own copies: kept out of
/// the Plugins folder, they are found only here. The version asked for is not checked, so the
/// copy shipped answers for every version one of them references, as a redirect would.
/// </summary>
internal static class PrivateDependencies
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static string Folder { get; } =
        Path.Combine(Path.GetDirectoryName(typeof(PrivateDependencies).Assembly.Location)!, "fadem");

    public static void Register()
    {
        lock (Gate)
        {
            if (!_registered)
            {
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                _registered = true;
            }
        }
    }

    private static Assembly? Resolve(object? sender, ResolveEventArgs args)
    {
        var path = Path.Combine(Folder, new AssemblyName(args.Name).Name + ".dll");
        return File.Exists(path) ? Assembly.LoadFrom(path) : null;
    }
}
