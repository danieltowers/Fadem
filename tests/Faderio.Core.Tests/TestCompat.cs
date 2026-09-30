namespace Faderio.Core.Tests;

/// <summary>Stand-ins for what the tests call that net48 lacks, compiled on both targets so a
/// test reads the same on each (phase 5 issue 08), as Core's <c>Compat</c> does for the engine.</summary>
internal static class TestCompat
{
    /// <summary>As <c>Path.GetRelativePath</c> for a path under <paramref name="root"/>; any
    /// other path comes back as it is.</summary>
    public static string GetRelativePath(string root, string path)
    {
        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? path.Substring(prefix.Length) : path;
    }
}
