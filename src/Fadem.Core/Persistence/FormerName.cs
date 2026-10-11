using System.IO;

namespace Fadem.Core.Persistence;

/// <summary>
/// Fadem was called faderio until 2026-10 (ADR 0018). What it wrote under that name is
/// renamed on first use, never copied, so nothing is left behind to drift: a library's
/// <c>.faderio/</c> becomes <c>.fadem/</c>, and the machine folder <c>faderio</c> becomes
/// <c>fadem</c>. A folder under the new name wins, and one under the old name beside it is left
/// alone.
/// </summary>
public static class FormerName
{
    /// <summary>The library's store folder as faderio named it.</summary>
    public const string FaderioStoreFolder = ".faderio";

    /// <summary>The machine folder, under %APPDATA% or MusicBee's storage, as faderio named it.</summary>
    public const string FaderioMachineFolder = "faderio";

    /// <summary>The machine folder, as Fadem names it.</summary>
    public const string FademMachineFolder = "fadem";

    /// <summary>Renames <c>&lt;library&gt;/.faderio/</c> to <paramref name="storeFolder"/> when only
    /// the old one exists. Null when the store folder can be used: renamed, there already, or
    /// neither there. Otherwise why it could not be renamed, as the library's unavailable reason:
    /// the old folder is left whole, and nothing is written under the new name.</summary>
    public static string? AdoptStore(string libraryFolder, string storeFolder) =>
        Rename(Path.Combine(libraryFolder, FaderioStoreFolder), storeFolder) is { } failure
            ? $"its {FaderioStoreFolder} folder could not be renamed to {Path.GetFileName(storeFolder)} ({failure})"
            : null;

    /// <summary>This machine's folder in <paramref name="parent"/>: <c>fadem</c>, renamed from
    /// <c>faderio</c> when only that exists. When it cannot be renamed (faderio running from it),
    /// the old folder for this run, so nothing is written to a new one beside it; the next run
    /// renames it. Never made here.</summary>
    public static string MachineFolder(string parent)
    {
        var former = Path.Combine(parent, FaderioMachineFolder);
        var folder = Path.Combine(parent, FademMachineFolder);
        return Rename(former, folder) is null ? folder : former;
    }

    /// <summary>Renames <paramref name="former"/> to <paramref name="current"/> when only the
    /// former exists; null unless it had to and could not, then the failure's message.</summary>
    private static string? Rename(string former, string current)
    {
        if (Directory.Exists(current) || !Directory.Exists(former))
        {
            return null;
        }
        try
        {
            Directory.Move(former, current);
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Another host may have renamed it a moment before.
            return Directory.Exists(current) ? null : e.Message;
        }
    }
}
