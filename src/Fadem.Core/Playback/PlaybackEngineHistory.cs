using Fadem.Core.Domain;
using Fadem.Core.Persistence;

namespace Fadem.Core.Playback;

/// <summary>One change to the transition store, as the view names it for Undo and Redo (phase 6
/// issue 04): the adjacency, the playlist whose override it changed (null for the global
/// transition), and whether it left the layer saved or removed.</summary>
public sealed record HistoryStep(Adjacency Adjacency, string? Playlist, bool Removed)
{
    /// <summary>What the change did, in the words of its notice: "saved A → B in Evening",
    /// "removed A → B", "removed the override of A → B in Evening".</summary>
    public string Describe()
    {
        var where = Playlist is null ? "" : $" in {Playlist}";
        return !Removed ? $"saved {Adjacency.Name}{where}"
            : Playlist is null ? $"removed {Adjacency.Name}"
            : $"removed the override of {Adjacency.Name}{where}";
    }
}

public sealed partial class PlaybackEngine
{
    // ── The authoring history: this session's changes to the store, undone and redone ────

    /// <summary>A change to one layer of one adjacency: what it held before and after. Null is
    /// nothing stored.</summary>
    private sealed record HistoryEntry(Adjacency Adjacency, string? Playlist, TransitionSpec? Before, TransitionSpec? After)
    {
        public HistoryStep Step => new(Adjacency, Playlist, After is null);
    }

    /// <summary>Newest last. A change clears <see cref="_redo"/>; opening another library clears
    /// both, since they name the store left.</summary>
    private readonly List<HistoryEntry> _undo = new();
    private readonly List<HistoryEntry> _redo = new();

    /// <summary>Puts the layer the newest change touched back as it was before it, and returns
    /// its adjacency, or null with nothing to undo. What was stored is written unvalidated: it was
    /// valid when stored. A pending boundary is rescheduled as a save reschedules it. A store
    /// that cannot be written is a notice, and the change stays to undo.</summary>
    public Adjacency? Undo() => Step(_undo, _redo, e => e.Before, "Undone", "Not undone");

    /// <summary>Puts back the change undone last, as <see cref="Undo"/> undoes one.</summary>
    public Adjacency? Redo() => Step(_redo, _undo, e => e.After, "Redone", "Not redone");

    private Adjacency? Step(
        List<HistoryEntry> from, List<HistoryEntry> to, Func<HistoryEntry, TransitionSpec?> target, string done, string failed)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            if (from.Count == 0 || _store is null)
            {
                return null;
            }
            var entry = from[from.Count - 1];
            try
            {
                Hold(entry.Adjacency, failed, () => Write(entry, target(entry)));
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure(failed, entry.Adjacency, e);
                return null;
            }
            from.RemoveAt(from.Count - 1);
            to.Add(entry);
            StoreChanged();
            RaiseNotice($"{done}: {entry.Step.Describe()}.");
            RescheduleIfPending(entry.Adjacency);
            return entry.Adjacency;
        });
    }

    /// <summary>Stores <paramref name="spec"/> in the entry's layer, or removes the layer's
    /// transition for null.</summary>
    private void Write(HistoryEntry entry, TransitionSpec? spec)
    {
        var (outgoing, incoming) = (entry.Adjacency.Outgoing.FilePath, entry.Adjacency.Incoming.FilePath);
        switch (entry.Playlist, spec)
        {
            case (null, null):
                _store!.Remove(outgoing, incoming);
                break;
            case (null, { } global):
                _store!.Upsert(outgoing, incoming, global);
                break;
            case ({ } playlist, null):
                _store!.RemoveOverride(playlist, outgoing, incoming);
                break;
            case ({ } playlist, { } partial):
                _store!.UpsertOverride(playlist, outgoing, incoming, partial);
                break;
        }
    }

    /// <summary>A change to the store, made: one step to undo, and nothing left to redo. A write
    /// that left the layer as it was is no step.</summary>
    private void Record(Adjacency adjacency, string? playlist, TransitionSpec? before, TransitionSpec? after)
    {
        if (Equals(before, after))
        {
            return;
        }
        _undo.Add(new HistoryEntry(adjacency, playlist, before, after));
        _redo.Clear();
    }

    private void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    /// <summary>A playlist renamed: its steps follow its overrides to the new name.</summary>
    private void RenameInHistory(string old, string name)
    {
        foreach (var steps in new[] { _undo, _redo })
        {
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i].Playlist is { } p && PlaylistFolder.Names.Equals(p, old))
                {
                    steps[i] = steps[i] with { Playlist = name };
                }
            }
        }
    }

    /// <summary>A playlist deleted: its steps go with its overrides.</summary>
    private void RemoveFromHistory(string playlist)
    {
        foreach (var steps in new[] { _undo, _redo })
        {
            steps.RemoveAll(s => s.Playlist is { } p && PlaylistFolder.Names.Equals(p, playlist));
        }
    }

    private HistoryStep? NextUndo => _undo.Count == 0 ? null : _undo[_undo.Count - 1].Step;

    private HistoryStep? NextRedo => _redo.Count == 0 ? null : _redo[_redo.Count - 1].Step;
}
