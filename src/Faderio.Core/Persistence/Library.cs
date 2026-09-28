using System.IO;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Persistence;

/// <summary>
/// A folder the user chooses, and everything faderio writes for it (ADR 0011): the transition
/// store, the settings and the playlists, all under <c>&lt;library&gt;/.faderio/</c>. Opening
/// creates <c>.faderio/</c> and <c>.faderio/playlists/</c> if absent and loads both stores. A
/// corrupt transitions file is moved aside to <c>.corrupt</c>, and one that cannot be moved, or
/// is from a newer schema, is left alone with writes going to <c>.recovered</c> beside it.
/// A folder that cannot be opened (gone, not writable, unreadable) opens unavailable: it holds
/// no store and its <see cref="Notices"/> name why. Opening never throws.
/// </summary>
public sealed class Library
{
    /// <summary>The folder inside the library that faderio writes to, and nothing outside it.</summary>
    public const string StoreFolderName = ".faderio";

    private readonly List<string> _notices = new();

    private Library(string folder)
    {
        Folder = Compat.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var leaf = Path.GetFileName(Folder);
        Name = leaf.Length > 0 ? leaf : Folder; // a drive's root has no leaf
        StoreFolder = Path.Combine(Folder, StoreFolderName);
        TransitionsPath = Path.Combine(StoreFolder, "transitions.json");
        SettingsPath = Path.Combine(StoreFolder, "settings.json");
        PlaylistsFolder = Path.Combine(StoreFolder, "playlists");
    }

    /// <summary>The folder the user chose, in full.</summary>
    public string Folder { get; }

    /// <summary>The folder's leaf name: what the user calls the library.</summary>
    public string Name { get; }

    public string StoreFolder { get; }
    public string TransitionsPath { get; }
    public string SettingsPath { get; }
    public string PlaylistsFolder { get; }

    /// <summary>The transition store, or null when the library is unavailable.</summary>
    public JsonTransitionStore? Transitions { get; private set; }

    /// <summary>The settings, or null when the library is unavailable.</summary>
    public SettingsStore? Settings { get; private set; }

    public bool IsAvailable => Transitions is not null;

    /// <summary>Why the library could not be opened, worded for the user; null when it opened.</summary>
    public string? UnavailableReason { get; private set; }

    /// <summary>What went wrong opening it, worded for the user, in order: the engine raises them.</summary>
    public IReadOnlyList<string> Notices => _notices;

    public LibraryView View => new(Folder, Name, IsAvailable);

    public static Library Open(string folder)
    {
        var library = new Library(folder);
        library.Load();
        return library;
    }

    private void Load()
    {
        if (!Directory.Exists(Folder))
        {
            Unavailable("the folder does not exist");
            return;
        }
        try
        {
            Directory.CreateDirectory(StoreFolder);
            Directory.CreateDirectory(PlaylistsFolder);
            var transitions = OpenTransitionStore(TransitionsPath, _notices.Add);
            Settings = new SettingsStore(SettingsPath);
            Transitions = transitions;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException
                                      or TransitionStoreVersionException)
        {
            Settings = null;
            Unavailable(e.Message);
            return;
        }
        if (Settings.LoadNotice is { } notice)
        {
            _notices.Add(notice);
        }
    }

    private void Unavailable(string reason)
    {
        UnavailableReason = reason;
        _notices.Add($"The library {Name} could not be opened ({reason}). Nothing is saved until a " +
                     "library opens; what is queued still plays.");
    }

    /// <summary>Opens the store, recovering visibly from a file it cannot load.</summary>
    private static JsonTransitionStore OpenTransitionStore(string path, Action<string> warn)
    {
        try
        {
            return new JsonTransitionStore(path);
        }
        catch (TransitionStoreVersionException e)
        {
            warn(e.Message + " A fresh store will be used; your file is left untouched.");
            return new JsonTransitionStore(path + ".recovered");
        }
        catch (InvalidDataException e)
        {
            try
            {
                FileSystemCompat.MoveReplacing(path, path + ".corrupt");
            }
            catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
            {
                warn(e.Message + $" It could not be moved aside ({moveFailure.Message}), so it is left " +
                    $"untouched and a fresh store is used in {Path.GetFileName(path)}.recovered.");
                return new JsonTransitionStore(path + ".recovered");
            }
            warn(e.Message + $" It was moved aside to {Path.GetFileName(path)}.corrupt and a fresh store is used.");
            return new JsonTransitionStore(path);
        }
    }
}
