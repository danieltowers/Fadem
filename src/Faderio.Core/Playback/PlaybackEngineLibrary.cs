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
    /// and playback is allowed. One that opens is remembered for the next launch.</summary>
    public void OpenLibrary(string folder)
    {
        ThrowIfDisposed();
        var library = Library.Open(folder); // the disk, outside the lock
        Complete(Run(() =>
        {
            ObserveClock();
            var cancelled = EndAddRun(cancelled: true, notify: true);
            ReleasePreview();
            ResetForRestart();
            StopOutput();
            EmptyQueue();
            _library = library;
            _store = library.Transitions;
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
            Publish();
            return cancelled;
        }));
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
