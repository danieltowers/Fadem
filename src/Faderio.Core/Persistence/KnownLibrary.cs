using Faderio.Core.Settings;

namespace Faderio.Core.Persistence;

/// <summary>
/// A known library as the chooser lists it (library chooser issue 01): one that has opened on
/// this machine for this host.
/// </summary>
/// <param name="Folder">The library's folder, in full.</param>
/// <param name="Name">The folder's leaf name: what the user calls the library.</param>
/// <param name="IsOpen">Whether it is the library open now: it cannot be forgotten.</param>
/// <param name="Exists">Whether its folder is there now: one that is not cannot be opened.</param>
public sealed record KnownLibrary(string Folder, string Name, bool IsOpen, bool Exists)
{
    /// <summary>The known libraries in <paramref name="settings"/>, most recently opened first,
    /// each looked for on disk now (D8): what <c>PlaybackEngine.KnownLibraries</c> returns, and
    /// what a launch lists before there is an engine.</summary>
    /// <param name="open">The folder of the library open now, or null.</param>
    /// <param name="exists">Whether a folder is there: <see cref="Directory.Exists"/>, bar tests.</param>
    public static IReadOnlyList<KnownLibrary> List(MachineSettings settings, string? open, Func<string, bool> exists) =>
        settings.KnownLibraries
            .Select(folder => new KnownLibrary(folder, LibraryFolders.Name(folder), LibraryFolders.Same(folder, open), exists(folder)))
            .ToList();
}

/// <summary>Library folders as faderio compares and names them.</summary>
public static class LibraryFolders
{
    /// <summary>The folder in full, without a trailing separator, as <see cref="Library.Folder"/>
    /// has it; one that is not a path is left as it is.</summary>
    public static string Full(string folder)
    {
        try
        {
            return Compat.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return folder;
        }
    }

    /// <summary>Whether the two are the same folder: Windows' paths ignore case.</summary>
    public static bool Same(string? a, string? b) =>
        a is not null && b is not null && string.Equals(Full(a), Full(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>The folder's leaf name, or the folder itself for a drive's root.</summary>
    public static string Name(string folder)
    {
        var leaf = Path.GetFileName(Full(folder));
        return leaf.Length > 0 ? leaf : folder;
    }
}
