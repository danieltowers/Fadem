using System.Diagnostics;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Queue;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Playlists: the queue mirrors the open one, and every change to it is kept ────────

    /// <summary>The least time between two writes of the open playlist while an add runs: a large
    /// folder would otherwise rewrite the whole file once per track. The run's end always writes.</summary>
    private static readonly TimeSpan AddSaveInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>The playlist the queue mirrors (D15); null only when no library is open, or its
    /// playlist could not be created.</summary>
    private string? _openPlaylist;

    /// <summary>The playlists folder as last listed: by this engine's own commands, since
    /// external edits of the folder are the user's problem (D14).</summary>
    private IReadOnlyList<string> _playlists = [];

    /// <summary>Rows still sounding from the playlist open before: kept so that their voices
    /// finish, at the top of the queue, and in no playlist. The queue passes out of them and never
    /// steps into one; each goes once its voice falls silent (D16).</summary>
    private readonly HashSet<QueueEntry> _passing = new();

    /// <summary>The add that is loading the open playlist's rows. Until it ends, the file holds
    /// rows the queue does not yet, so an edit is only noted and written when it ends.</summary>
    private AddJob? _loading;
    private bool _editedWhileLoading;

    /// <summary>The playlist last tried for <see cref="RememberOpenPlaylist"/>.</summary>
    private string? _rememberTried;

    /// <summary>An add's write skipped by <see cref="AddSaveInterval"/>: the run's end makes it.</summary>
    private bool _saveDeferred;
    private long _saveTakenAt;

    /// <summary>A write was taken under the lock: the command that took it writes it once the
    /// lock is released.</summary>
    private bool _saveRequested;

    /// <summary>Guards <see cref="_pendingSave"/> and every playlist file operation, so a write
    /// made off the lock can never land after the file was renamed or deleted under it. Taken
    /// inside the engine lock or on its own, never the other way round.</summary>
    private readonly object _saveGate = new();
    private PlaylistSave? _pendingSave;

    /// <summary>The file whose last write failed and was told. Under the gate.</summary>
    private string? _failingPlaylist;

    /// <summary>The open playlist's rows in its own order, to be written to its file.</summary>
    private sealed record PlaylistSave(string Name, string Path, IReadOnlyList<Track> Tracks);

    /// <summary>A playlist opened: the add runs it cut short, and the add that loads its rows.</summary>
    private readonly record struct Opened(List<AddCompletion> Cancelled, AddJob? Job, bool StartWorker)
    {
        public static Opened Nothing => new(new List<AddCompletion>(), null, false);
    }

    /// <summary>Test seam: the load of the playlist the engine opened at launch.</summary>
    internal Task<AddReport> Launched { get; private set; } = Task.FromResult(NothingAdded);

    private static AddReport NothingAdded => new(Array.Empty<QueueRow>(), Array.Empty<Rejection>());

    /// <summary>
    /// Opens the playlist named <paramref name="name"/> (D16): the queue is replaced by its rows,
    /// which load as an add does, off the lock, a missing or unreadable entry rejected as
    /// <see cref="Add"/> rejects it. A running add is ended first. If nothing sounds, row 0 is
    /// attended at 0:00. If a voice sounds, it keeps sounding and its row stays as a row the queue
    /// passes out of, so that its row → the new row 0 is the next adjacency, resolved from the
    /// store like any other; the row goes when its voice falls silent. An open Untitled with no
    /// rows is deleted as it is left (D15). The open playlist, or one that is not there, is no
    /// change. The task completes with the load's report.
    /// </summary>
    public Task<AddReport> OpenPlaylist(string name)
    {
        ThrowIfDisposed();
        return Loaded(Run(() =>
        {
            ObserveClock();
            if (!HavePlaylists("Not opened") || Find(name) is not { } found || found == _openPlaylist)
            {
                return Opened.Nothing;
            }
            if (ReadPlaylist(found) is not { } entries)
            {
                return Opened.Nothing;
            }
            var opened = SwitchTo(found, entries);
            Publish();
            return opened;
        }));
    }

    /// <summary>Creates a playlist called "Untitled", or "Untitled N" for the first N from 2 that
    /// is free, and opens it as <see cref="OpenPlaylist"/> does (D15). With an empty Untitled
    /// already open, that is the new playlist, and nothing changes.</summary>
    public void NewPlaylist()
    {
        ThrowIfDisposed();
        Loaded(Run(() =>
        {
            ObserveClock();
            if (!HavePlaylists("No new playlist") || OpenIsAnEmptyUntitled())
            {
                return Opened.Nothing;
            }
            RefreshPlaylists();
            var name = PlaylistFolder.FirstFreeUntitled(_playlists);
            if (!CreatePlaylistFile(name, "No new playlist"))
            {
                return Opened.Nothing;
            }
            var opened = SwitchTo(name, []);
            Publish();
            return opened;
        }));
    }

    /// <summary>Renames a playlist's file and rekeys its overrides in the store to the new name
    /// (D14). The new name is trimmed. One that cannot name a file, or that another playlist
    /// has, is refused with a notice, and so is a rename whose file or store cannot be written:
    /// then nothing changes. The open playlist stays open under its new name.</summary>
    public void RenamePlaylist(string oldName, string newName)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (!HavePlaylists("Not renamed") || Find(oldName) is not { } old)
            {
                return;
            }
            var name = newName.Trim();
            if (PlaylistFolder.Refusal(name) is { } refusal)
            {
                RaiseNotice($"Not renamed: {old}. {refusal}");
                return;
            }
            if (name == old)
            {
                return;
            }
            if (_playlists.FirstOrDefault(p => PlaylistFolder.Names.Equals(p, name)) is { } taken &&
                !PlaylistFolder.Names.Equals(taken, old))
            {
                RaiseNotice($"Not renamed: {old}. There is already a playlist called {taken}.");
                return;
            }
            lock (_saveGate)
            {
                if (old == _openPlaylist)
                {
                    WritePendingUnderGate(RaiseNotice); // its latest rows go with it
                }
                if (RenamePlaylistFiles(old, name) is { } failure)
                {
                    RaiseNotice($"Not renamed: {old}. {failure}");
                    return;
                }
            }
            if (old == _openPlaylist)
            {
                _openPlaylist = name;
                RememberOpenPlaylist();
            }
            RefreshPlaylists();
            Publish();
        });
    }

    /// <summary>Deletes a playlist's file and removes its overrides from the store (D17). If it
    /// was open, the first remaining playlist opens as <see cref="OpenPlaylist"/> opens one, so
    /// playback is not interrupted; with none left, a new Untitled. A file that cannot be deleted
    /// is a notice, and nothing changes.</summary>
    public void DeletePlaylist(string name)
    {
        ThrowIfDisposed();
        Loaded(Run(() =>
        {
            ObserveClock();
            if (!HavePlaylists("Not deleted") || Find(name) is not { } found)
            {
                return Opened.Nothing;
            }
            var wasOpen = found == _openPlaylist;
            lock (_saveGate)
            {
                try
                {
                    File.Delete(PlaylistPath(found));
                }
                catch (Exception e) when (IsStoreWriteFailure(e))
                {
                    RaiseNotice($"Not deleted: {found}. Its file could not be deleted: {e.Message}");
                    return Opened.Nothing;
                }
                if (wasOpen)
                {
                    _pendingSave = null; // rows for a file that is gone
                    _openPlaylist = null; // so nothing is written to it again
                }
            }
            if (RemoveOverrides(found) is { } failure)
            {
                RaiseNotice($"Deleted {found}, but its transition overrides could not be removed: {failure}");
            }
            RefreshPlaylists();
            var opened = wasOpen ? OpenFirstReadableOf(_playlists, launching: false) : Opened.Nothing;
            Publish();
            return opened;
        }));
    }

    // ── Opening, under the lock ──────────────────────────────────────────────────────────

    /// <summary>The library's last open playlist (D20), with row 0 attended at 0:00 since nothing
    /// sounds; if there is none, or it is gone, a new Untitled.</summary>
    private Opened OpenLastPlaylist()
    {
        if (LibraryPlaylistsFolder is null)
        {
            return Opened.Nothing;
        }
        RefreshPlaylists();
        var last = _playlists.FirstOrDefault(p => PlaylistFolder.Names.Equals(p, _settings.LastPlaylist));
        return OpenFirstReadableOf(last is null ? [] : [last], launching: true);
    }

    /// <summary>The first of <paramref name="names"/> that can be read, else a new Untitled.</summary>
    private Opened OpenFirstReadableOf(IEnumerable<string> names, bool launching)
    {
        foreach (var name in names)
        {
            if (ReadPlaylist(name) is { } entries)
            {
                return SwitchTo(name, entries, launching);
            }
        }
        var untitled = PlaylistFolder.FirstFreeUntitled(_playlists);
        return CreatePlaylistFile(untitled, "No playlist is open")
            ? SwitchTo(untitled, [], launching)
            : Opened.Nothing;
    }

    /// <summary>Leaves the open playlist and opens <paramref name="name"/>: a running add ends, the
    /// playlist left is written and, if it is an empty Untitled, deleted; the queue keeps only
    /// what sounds, as passing rows; and <paramref name="entries"/> are queued to load.</summary>
    private Opened SwitchTo(string name, IReadOnlyList<string> entries, bool launching = false)
    {
        var cancelled = EndAddRun(cancelled: true, notify: true);
        lock (_saveGate)
        {
            WritePendingUnderGate(RaiseNotice);
        }
        DeleteIfEmptyUntitled();
        KeepOnlySoundingRows();
        _openPlaylist = name;
        if (entries.Count > 0 || !PlaylistFolder.IsUntitled(name))
        {
            RememberOpenPlaylist(launching);
        }
        RefreshPlaylists();
        if (entries.Count == 0)
        {
            return new Opened(cancelled, null, false);
        }
        var job = new AddJob(entries, opening: true);
        _loading = job;
        _editedWhileLoading = false;
        return new Opened(cancelled, job, Enqueue(job));
    }

    /// <summary>Answers the adds a switch cut short and starts the worker for the load, once the
    /// lock is released.</summary>
    private Task<AddReport> Loaded(Opened opened)
    {
        Complete(opened.Cancelled);
        if (opened.StartWorker)
        {
            StartAddWorker(AddWorker);
        }
        return opened.Job?.Done.Task ?? Task.FromResult(NothingAdded);
    }

    /// <summary>Empties the queue but for the rows whose voices sound, which stay as passing rows
    /// in the order they play, the current one current. Their boundary is scheduled again: with
    /// no row after them yet, there is none until the first new row is queued.</summary>
    private void KeepOnlySoundingRows()
    {
        var sounding = SoundingEntries();
        if (sounding.Count == 0)
        {
            EmptyQueue();
            return;
        }
        foreach (var entry in _queue.Entries.Except(sounding))
        {
            _unreadable.Remove(entry.Id);
        }
        _queue.Reorder(sounding);
        if (_currentVoice is { } voice && IndexOf(voice) is var index and >= 0)
        {
            _queue.SetCurrent(index);
        }
        _originalOrder.Clear();
        _passing.Clear();
        _passing.UnionWith(sounding);
        _queueVersion++;
        RescheduleCurrentBoundary();
    }

    /// <summary>The rows whose voices the listener hears or is about to: the outgoing one, the
    /// attended one and the current one, in that order.</summary>
    private List<QueueEntry> SoundingEntries() =>
        new[] { _outgoingVoice, _attendedVoice, _currentVoice }
            .Select(voice => voice is not null && _voiceEntry.TryGetValue(voice, out var entry) ? entry : null)
            .OfType<QueueEntry>()
            .Distinct()
            .ToList();

    /// <summary>Removes each passing row whose voice has fallen silent. Called once at the end of
    /// every locked section, so no command has to know about them.</summary>
    private void DropSilentPassingRows()
    {
        if (_passing.Count == 0)
        {
            return;
        }
        var sounding = SoundingEntries();
        var silent = _passing.Where(entry => !sounding.Contains(entry)).ToList();
        if (silent.Count == 0)
        {
            return;
        }
        foreach (var entry in silent)
        {
            _passing.Remove(entry);
            _unreadable.Remove(entry.Id);
            if (_queue.IndexOf(entry.Id) is var index and >= 0)
            {
                _queue.RemoveAt(index);
            }
        }
        _queueVersion++;
        Publish();
    }

    /// <summary>The queue's rows in the open playlist, in its own order: under shuffle, the order
    /// shuffle was turned on from, which is what turning it off returns to.</summary>
    private IReadOnlyList<Track> PlaylistTracks => _originalOrder.Select(e => e.Track).ToList();

    /// <summary>Called once the load of the open playlist has ended, whole or cut short: an edit
    /// made while it ran is written now.</summary>
    private void LoadEnded()
    {
        _loading = null;
        if (_editedWhileLoading)
        {
            _editedWhileLoading = false;
            SaveOpenPlaylist();
        }
    }

    // ── Autosave ─────────────────────────────────────────────────────────────────────────

    /// <summary>Takes the open playlist's rows to be written, in the playlist's own order, not the
    /// shuffle's. The command writes them once the lock is released. An add's own writes are
    /// <paramref name="throttled"/> to one per <see cref="AddSaveInterval"/>.</summary>
    private void SaveOpenPlaylist(bool throttled = false)
    {
        if (_openPlaylist is not { } name || LibraryPlaylistsFolder is null)
        {
            return;
        }
        if (_loading is not null)
        {
            _editedWhileLoading = true;
            return;
        }
        if (throttled && Stopwatch.GetElapsedTime(_saveTakenAt) < AddSaveInterval)
        {
            _saveDeferred = true;
            return;
        }
        _saveDeferred = false;
        _saveTakenAt = Stopwatch.GetTimestamp();
        var save = new PlaylistSave(name, PlaylistPath(name), PlaylistTracks);
        lock (_saveGate)
        {
            _pendingSave = save;
        }
        _saveRequested = true;
        RememberOpenPlaylist(); // an Untitled is worth reopening once it has rows
    }

    /// <summary>Writes the rows taken, off the lock. A write that fails is a notice, and the queue
    /// stays as it is; the next change writes again.</summary>
    private void WritePendingPlaylist()
    {
        string? failure = null;
        lock (_saveGate)
        {
            WritePendingUnderGate(notice => failure = notice);
        }
        if (failure is not null)
        {
            _sink.Notice(failure);
        }
    }

    /// <summary>Writes the latest rows taken, if they are not written yet. A playlist that cannot
    /// be written is told once, not at every write of an add, until a write to it succeeds.
    /// Holds the gate.</summary>
    private void WritePendingUnderGate(Action<string> failed)
    {
        if (_pendingSave is not { } save)
        {
            return;
        }
        _pendingSave = null;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(save.Path)!);
            M3UPlaylist.Write(save.Path, save.Tracks);
            _failingPlaylist = null;
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            if (_failingPlaylist == save.Path)
            {
                return;
            }
            _failingPlaylist = save.Path;
            failed($"The playlist {save.Name} could not be saved: {e.Message}");
        }
    }

    // ── The folder ───────────────────────────────────────────────────────────────────────

    private string? LibraryPlaylistsFolder => _library is { IsAvailable: true } library ? library.PlaylistsFolder : null;

    private string PlaylistPath(string name) => PlaylistFolder.PathOf(LibraryPlaylistsFolder!, name);

    private void RefreshPlaylists() =>
        _playlists = LibraryPlaylistsFolder is { } folder ? PlaylistFolder.List(folder) : [];

    /// <summary>Whether a library is open to keep playlists in; if not, a notice says why,
    /// headed <paramref name="outcome"/>.</summary>
    private bool HavePlaylists(string outcome)
    {
        if (LibraryPlaylistsFolder is not null)
        {
            return true;
        }
        RaiseNotice($"{outcome}. {NoLibrary}");
        return false;
    }

    /// <summary>The playlist named <paramref name="name"/>, trimmed and ignoring case, as its file
    /// spells it; if there is none, a notice.</summary>
    private string? Find(string name)
    {
        RefreshPlaylists();
        var trimmed = name.Trim();
        var found = _playlists.FirstOrDefault(p => PlaylistFolder.Names.Equals(p, trimmed));
        if (found is null)
        {
            RaiseNotice($"There is no playlist called {trimmed}.");
        }
        return found;
    }

    /// <summary>The playlist's entries, or null with a notice when its file cannot be read.</summary>
    private IReadOnlyList<string>? ReadPlaylist(string name)
    {
        try
        {
            lock (_saveGate)
            {
                return M3UPlaylist.Read(PlaylistPath(name));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            RaiseNotice($"The playlist {name} could not be opened: {e.Message}");
            return null;
        }
    }

    private bool CreatePlaylistFile(string name, string outcome)
    {
        try
        {
            lock (_saveGate)
            {
                M3UPlaylist.Write(PlaylistPath(name), []);
            }
            return true;
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            RaiseNotice($"{outcome}: the playlist {name} could not be created. {e.Message}");
            return false;
        }
    }

    /// <summary>Moves the file, then the overrides; if the store cannot be written, the file is
    /// moved back. Returns why it failed, or null. Holds the gate.</summary>
    private string? RenamePlaylistFiles(string old, string name)
    {
        try
        {
            File.Move(PlaylistPath(old), PlaylistPath(name));
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            return $"Its file could not be renamed: {e.Message}";
        }
        try
        {
            _store?.RenamePlaylist(old, name);
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            try
            {
                File.Move(PlaylistPath(name), PlaylistPath(old));
            }
            catch (Exception back) when (IsStoreWriteFailure(back))
            {
                return $"The transition store could not be written ({e.Message}), and the file, renamed " +
                       $"already, could not be named back ({back.Message}): its overrides still answer to {old}.";
            }
            return $"The transition store could not be written: {e.Message}";
        }
        return null;
    }

    /// <summary>Removes the playlist's overrides; returns why it could not, or null.</summary>
    private string? RemoveOverrides(string name)
    {
        try
        {
            _store?.RemovePlaylist(name);
            return null;
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            return e.Message;
        }
    }

    private bool OpenIsAnEmptyUntitled() =>
        _openPlaylist is { } name && PlaylistFolder.IsUntitled(name) && _originalOrder.Count == 0 && _loading is null;

    /// <summary>Deletes the open playlist, and its overrides, if it is an Untitled with no rows
    /// (D15). One whose file still holds rows, from a load cut short, is kept. Housekeeping, not a
    /// command: a file that cannot be deleted is left, and says nothing.</summary>
    private void DeleteIfEmptyUntitled()
    {
        if (!OpenIsAnEmptyUntitled())
        {
            return;
        }
        var name = _openPlaylist!;
        lock (_saveGate)
        {
            try
            {
                var path = PlaylistPath(name);
                if (File.Exists(path) && M3UPlaylist.Read(path).Count > 0)
                {
                    return;
                }
                File.Delete(path);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                return;
            }
        }
        RemoveOverrides(name);
        RefreshPlaylists();
    }

    /// <summary>Remembers the open playlist in the library's settings, for the next launch (D20).
    /// An empty Untitled is not remembered until it gets rows: it is deleted on quit, and the
    /// playlist remembered before it opens instead. At launch, a settings file left as it is (a
    /// newer schema) stays so. A settings file that cannot be written is told once per playlist
    /// opened, not at every edit.</summary>
    private void RememberOpenPlaylist(bool launching = false)
    {
        if (_openPlaylist is not { } name || _settings.LastPlaylist == name || _rememberTried == name ||
            (launching && _settingsStore?.LoadNotice is not null))
        {
            return;
        }
        _rememberTried = name;
        KeepSettings(_settings with { LastPlaylist = name }, "the playlist to reopen at launch");
    }
}
