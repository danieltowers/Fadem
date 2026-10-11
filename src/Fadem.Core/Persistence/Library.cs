using System.IO;
using System.Text;
using Fadem.Core.Playback;
using Fadem.Core.Settings;
using Fadem.Core.Transitions;

namespace Fadem.Core.Persistence;

/// <summary>
/// A folder the user chooses, and everything Fadem writes for it (ADR 0011): the transition
/// store, the settings and the playlists, all under <c>&lt;library&gt;/.fadem/</c>. Opening
/// creates <c>.fadem/</c> and <c>.fadem/playlists/</c> if absent and loads both stores; a
/// library faderio left has its <c>.faderio/</c> renamed first (ADR 0018). A
/// corrupt transitions file is moved aside to <c>.corrupt</c>, and one that cannot be moved, or
/// is from a newer schema, is left alone with writes going to <c>.recovered</c> beside it.
/// A folder that cannot be opened (gone, not writable, unreadable) opens unavailable: it holds
/// no store and its <see cref="Notices"/> name why. Opening never throws.
/// <para>One host holds a library at a time (phase 5 issue 15): the engine that opens it for
/// a host calls <see cref="Hold"/>, which keeps <c>.fadem/host.lock</c> open, named for the
/// host, until <see cref="Dispose"/>; the OS deletes the file with the last handle, a crashed
/// host's included. Another host's hold is refused, and its library opens unavailable with the
/// holder's name. Opening alone holds nothing: a reader is never refused.</para>
/// </summary>
public sealed class Library : IDisposable
{
    /// <summary>The folder inside the library that Fadem writes to, and nothing outside it.</summary>
    public const string StoreFolderName = ".fadem";

    private readonly List<string> _notices = new();
    private FileStream? _hold;

    private Library(string folder)
    {
        Folder = Compat.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        var leaf = Path.GetFileName(Folder);
        Name = leaf.Length > 0 ? leaf : Folder; // a drive's root has no leaf
        StoreFolder = Path.Combine(Folder, StoreFolderName);
        TransitionsPath = Path.Combine(StoreFolder, "transitions.json");
        SettingsPath = Path.Combine(StoreFolder, "settings.json");
        PlaylistsFolder = Path.Combine(StoreFolder, "playlists");
        HoldPath = Path.Combine(StoreFolder, "host.lock");
    }

    /// <summary>The folder the user chose, in full.</summary>
    public string Folder { get; }

    /// <summary>The folder's leaf name: what the user calls the library.</summary>
    public string Name { get; }

    public string StoreFolder { get; }
    public string TransitionsPath { get; }
    public string SettingsPath { get; }
    public string PlaylistsFolder { get; }

    /// <summary>The file a host keeps open while it holds the library, holding the host's name.</summary>
    public string HoldPath { get; }

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
        if (FormerName.AdoptStore(Folder, StoreFolder) is { } failure)
        {
            Unavailable(failure);
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

    /// <summary>Takes the library for <paramref name="host"/> (its name as the other host will
    /// read it): the lock file is opened for writing, shared for reading, deleted by the OS when
    /// the last handle on it closes, and named for the host. A library another host holds
    /// becomes unavailable, its reason naming that host ("another host" when its name cannot be
    /// read); one that is unavailable already is left so. Holding again is a no-op.</summary>
    public void Hold(string host)
    {
        if (!IsAvailable || _hold is not null)
        {
            return;
        }
        try
        {
            // Shared for reading only: nobody else may write it, or unlink it from under its holder.
            var hold = new FileStream(HoldPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose);
            try
            {
                hold.SetLength(0);
                var name = Encoding.UTF8.GetBytes(host + Environment.NewLine);
                hold.Write(name, 0, name.Length);
                hold.Flush(true);
            }
            catch
            {
                hold.Dispose();
                throw;
            }
            _hold = hold;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Transitions = null;
            Settings = null;
            Unavailable(IsHeldByAnother(e) ? $"it is open in {ReadHolder()}" : e.Message);
        }
    }

    /// <summary>Whether the open failed because another handle holds the file: Windows' sharing
    /// or lock violation, the only refusal that means a holder. (A holder that let go while a
    /// reader still had the file open leaves it delete-pending for that instant, which refuses
    /// with access denied instead; the next open, a moment later, takes it.)</summary>
    private static bool IsHeldByAnother(Exception e) => e is IOException && (e.HResult & 0xFFFF) is 32 or 33;

    /// <summary>Lets go of the library, if held: the lock file closes and goes.</summary>
    public void Dispose()
    {
        _hold?.Dispose();
        _hold = null;
    }

    /// <summary>The name in the lock file another host keeps open, or "another host" when it
    /// cannot be read.</summary>
    private string ReadHolder()
    {
        try
        {
            using var stream = new FileStream(HoldPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var name = reader.ReadLine()?.Trim();
            return string.IsNullOrEmpty(name) ? "another host" : name!;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return "another host";
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
