using Faderio.Core.Persistence;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── The library ──────────────────────────────────────────────────────────────────────

    /// <summary>Switches to the library in <paramref name="folder"/> (library issue 02, D12):
    /// playback stops, a running add and a preview end, the queue empties, and the store and
    /// settings become that library's, its shuffle and repeat among them, in force before anything
    /// enters the queue (library issue 04). Nothing crosses from the one before (ADR 0011). A folder
    /// that cannot be opened leaves no library open: a notice says why, authoring is refused
    /// and playback is allowed. One that opens is remembered for the next launch. The playlist
    /// left is written, and deleted if it is an empty Untitled; the new library's last open
    /// playlist opens, or a new Untitled (playlists issue 01, D20). The library left is let go
    /// of once its last write is made and before the new one is held (phase 5 issue 15), so the
    /// same folder opened again stays open rather than being refused by its own holder.</summary>
    public void OpenLibrary(string folder)
    {
        ThrowIfDisposed();
        var library = Library.Open(folder); // the disk, outside the lock
        Loaded(Run(() =>
        {
            ObserveClock();
            var cancelled = EndAddRun(cancelled: true, notify: true);
            lock (_saveGate)
            {
                WritePendingUnderGate(RaiseNotice); // the playlist left, in the library left
            }
            DeleteIfEmptyUntitled();
            _library?.Dispose(); // let go only once the library left has its last write
            Hold(library);
            _openPlaylist = null;
            _rememberTried = null;
            ReleasePreview();
            ResetForRestart();
            StopOutput();
            EmptyQueue();
            _library = library;
            _store = library.Transitions;
            ClearHistory(); // its steps name the store left
            _settingsStore = library.Settings;
            _settings = library.Settings?.Settings ?? new PlayerSettings();
            _resolver.Settings = _settings;
            foreach (var notice in library.Notices)
            {
                RaiseNotice(notice);
            }
            if (Remember(library) is { } unremembered)
            {
                RaiseNotice(unremembered);
            }
            var opened = OpenLastPlaylist();
            Publish();
            return opened with { Cancelled = [.. cancelled, .. opened.Cancelled] };
        }));
    }

    /// <summary>The name this engine holds libraries under, or null for an engine that holds none.</summary>
    private readonly string? _host;

    /// <summary>Holds <paramref name="library"/> for this engine's host, if it has one, and
    /// returns it: a library another host holds becomes unavailable, its notices saying so.</summary>
    private Library? Hold(Library? library)
    {
        if (library is not null && _host is not null)
        {
            library.Hold(_host);
        }
        return library;
    }

    /// <summary>Writes a library that opened to the machine's settings, as the one the next
    /// launch opens. Returns why it could not be, worded for the user, or null.</summary>
    private string? Remember(Library? library)
    {
        if (_machine is null || library is not { IsAvailable: true } ||
            _machine.Settings.Library == library.Folder)
        {
            return null;
        }
        try
        {
            _machine.Save(_machine.Settings with { Library = library.Folder });
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            return $"The library {library.Name} is open, but the next launch will not reopen it: " +
                   $"the machine settings file could not be written ({e.Message}).";
        }
        return null;
    }
}
