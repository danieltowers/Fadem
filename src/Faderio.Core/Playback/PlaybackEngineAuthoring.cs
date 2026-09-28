using Faderio.Core.Domain;
using Faderio.Core.Queue;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Authoring ────────────────────────────────────────────────────────────────────────

    /// <summary>A playhead as a point for <paramref name="side"/>: the only sound→point path.
    /// Returns the same value the view's playhead would have at this instant, unquantised: the
    /// attended voice's, or during a preview the preview's own voice for that side, the outgoing
    /// one for a fade-out point and the incoming one for a fade-in point (build issue 16). With
    /// <paramref name="fromEnd"/>, the point is measured from the end of that voice's track:
    /// <c>Length − playhead</c>, for a from-end fade-out point (build issue 14). Null when nothing
    /// is playing; during a preview, null with a notice saying why when that side's voice is not
    /// heard.</summary>
    public Point? Capture(CaptureSide side, bool fromEnd = false)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            var view = View;
            Playhead playhead;
            Track track;
            if (view.Preview is { } preview)
            {
                var outgoing = side == CaptureSide.FadeOut;
                if ((outgoing ? preview.Outgoing : preview.Incoming) is not { } heard)
                {
                    RaiseNotice(outgoing
                        ? "Nothing captured: the preview's outgoing track has fallen silent."
                        : _preview?.IncomingEndedAt is null
                            ? "Nothing captured: the preview's incoming track has not started yet."
                            : "Nothing captured: the preview's incoming track has ended.");
                    return (Point?)null;
                }
                (playhead, track) = (heard, outgoing ? preview.Adjacency.Outgoing : preview.Adjacency.Incoming);
            }
            else if (view.Attended is { } attended)
            {
                (playhead, track) = (attended.Playhead, attended.Row.Track);
            }
            else
            {
                return null;
            }
            var point = Point.FromTimeSpan(playhead.TimeSpan);
            return fromEnd ? MeasuredFromOtherEnd(track, point) : point;
        });
    }

    /// <summary>The same moment in <paramref name="track"/>, measured from its other end:
    /// ticking or unticking from-end on a fade-out point (build issue 14). On a 3:40 track, 3:12
    /// from the start is 0:28 from the end, and back. A point past the track's length names no
    /// moment in it, so it is returned unchanged for <see cref="Author"/> to refuse.</summary>
    public Point FromOtherEnd(Track track, Point point)
    {
        ThrowIfDisposed();
        return MeasuredFromOtherEnd(track, point);
    }

    /// <summary><paramref name="point"/> moved by <paramref name="delta"/>, clamped to
    /// <paramref name="track"/>: the editor's ±0.1 s buttons (interim transport issue 06). A point
    /// from the start and a point from the end both run from zero to the track's length, so the
    /// nudge needs no frame of reference: it moves the number the field shows, whichever end that
    /// is measured from. Exact to the tick, so a captured point keeps its precision.</summary>
    public Point Nudge(Track track, Point point, TimeSpan delta)
    {
        ThrowIfDisposed();
        var moved = point.TimeSpan + delta;
        var length = track.Length.TimeSpan;
        return Point.FromTimeSpan(moved > length ? length : moved);
    }

    private static Point MeasuredFromOtherEnd(Track track, Point point)
    {
        var length = track.Length.TimeSpan;
        return point.TimeSpan > length ? point : Point.FromTimeSpan(length - point.TimeSpan);
    }

    /// <summary>The adjacencies the editor can open from <paramref name="row"/>: this → next,
    /// previous → this and, under repeat-one, this → this (build issue 15). Read from the same
    /// published state as <see cref="View"/>, without the lock. A stale row has none.</summary>
    public RowAdjacencies AdjacenciesOf(RowId row)
    {
        ThrowIfDisposed();
        return _snapshot.AdjacenciesOf(row);
    }

    /// <summary>Whether saving <paramref name="draft"/> would leave the store as it is: it holds
    /// this transition for the adjacency, or holds nothing and the draft is blank, since a blank
    /// save stores nothing. A missing curve is equal-power, as the resolver reads it, and a blank
    /// entry left by an older build is nothing. The editor's test for unsaved edits (build issue
    /// 15).
    /// <para>For <see cref="TransitionLayer.Playlist"/>, whether the open playlist's override is
    /// the draft, as <see cref="AuthorOverride"/> would store it: a missing curve there is the
    /// global's, so it is compared as it stands (playlists issue 04).</para></summary>
    public bool IsAuthored(Adjacency adjacency, TransitionSpec draft, TransitionLayer layer = TransitionLayer.Global)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            if (layer == TransitionLayer.Playlist)
            {
                var global = _store?.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
                var storedOverride = StoredOverride(adjacency) is { } o ? OverrideToStore(global, o) : null;
                return storedOverride == OverrideToStore(global, draft);
            }
            var stored = _store?.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath) is { IsBlank: false } s ? s : null;
            if (stored is null || draft.IsBlank)
            {
                return stored is null && draft.IsBlank;
            }
            return WithCurve(stored) == WithCurve(draft);
        });

        static TransitionSpec WithCurve(TransitionSpec spec) => spec with { Curve = spec.Curve ?? FadeCurveKind.EqualPower };
    }

    /// <summary>The adjacency's transition, layer by layer: the global one, the open playlist's
    /// override and what they merge to, the transition the open playlist plays (playlists issue
    /// 04). With no playlist open there is no override.</summary>
    public TransitionLayers Transition(Adjacency adjacency)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            var global = _store?.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
            var playlistOverride = StoredOverride(adjacency);
            return new TransitionLayers(global, playlistOverride, TransitionResolver.Merge(global, playlistOverride))
            {
                BlankDuration = _resolver.BlankDuration,
            };
        });
    }

    /// <summary>The open playlist's override for the pair, as the resolver takes it; null with no
    /// playlist open.</summary>
    private TransitionSpec? OverrideOf(string outgoingPath, string incomingPath) =>
        _openPlaylist is { } name ? _store?.GetOverride(name, outgoingPath, incomingPath) : null;

    /// <summary>The open playlist's override for the adjacency; one that sets nothing, left by a
    /// store written elsewhere, is none.</summary>
    private TransitionSpec? StoredOverride(Adjacency adjacency) =>
        OverrideOf(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath) is { IsEmpty: false } o ? o : null;

    /// <summary>What <see cref="AuthorOverride"/> stores for <paramref name="partial"/>: the partial
    /// itself, or nothing when it sets nothing, or when with no global transition it sets only a
    /// curve, which shapes fades nobody placed.</summary>
    private static TransitionSpec? OverrideToStore(TransitionSpec? global, TransitionSpec partial) =>
        partial.IsEmpty || TransitionResolver.Merge(global, partial) is not { IsBlank: false } ? null : partial;

    /// <summary>Stores <paramref name="partial"/> as the open playlist's override of the adjacency
    /// (D21): the parameters it sets replace the global transition's in this playlist, and those it
    /// leaves out follow the global, later edits too. With no global it is a playlist-only
    /// transition (D22). It is validated as the transition the playlist will play, the global with
    /// the override laid over it, and refused as <see cref="Author"/> refuses; a pending boundary is
    /// rescheduled as <see cref="Author"/> reschedules it. A partial that sets nothing removes the
    /// override. The outcome is a notice.</summary>
    public IReadOnlyList<Refusal> AuthorOverride(Adjacency adjacency, TransitionSpec partial)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            if (OverrideTarget("Not saved", adjacency) is not { } name)
            {
                return (IReadOnlyList<Refusal>)[];
            }
            if (GoneReason(adjacency) is { } gone)
            {
                RaiseNotice($"Not saved: {adjacency.Name} in {name}. {gone}");
                return [];
            }
            var global = _store!.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
            if (OverrideToStore(global, partial) is null)
            {
                if (!RemoveOverride(adjacency, name))
                {
                    RaiseNotice($"Nothing to save: {adjacency.Name} in {name} overrides nothing.");
                }
                return [];
            }
            var refusals = Refusals(adjacency, TransitionResolver.Merge(global, partial)!);
            if (refusals.Count > 0)
            {
                RaiseNotice($"Not saved: {adjacency.Name} in {name}. " + string.Join(" ", refusals.Select(r => r.Reason)));
                return refusals;
            }
            try
            {
                _store.UpsertOverride(name, adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath, partial);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure("Not saved", adjacency, e);
                return [];
            }
            StoreChanged();
            RaiseNotice($"Saved {adjacency.Name} in {name}.");
            RescheduleIfPending(adjacency);
            return [];
        });
    }

    /// <summary>Removes the open playlist's override of the adjacency, so the playlist plays the
    /// global transition again, rescheduling as <see cref="Author"/> does.</summary>
    public void UnauthorOverride(Adjacency adjacency)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (OverrideTarget("Not removed", adjacency) is { } name)
            {
                RemoveOverride(adjacency, name);
            }
        });
    }

    /// <summary>The playlist whose override a command writes: the open one. Null, with a notice
    /// saying why, when there is no store or no playlist open.</summary>
    private string? OverrideTarget(string outcome, Adjacency adjacency)
    {
        if (_store is null)
        {
            RaiseNotice($"{outcome}: {adjacency.Name}. {NoLibrary}");
            return null;
        }
        if (_openPlaylist is not { } name)
        {
            RaiseNotice($"{outcome}: {adjacency.Name}. No playlist is open.");
            return null;
        }
        return name;
    }

    /// <summary><see cref="UnauthorOverride"/>'s body, under the lock. True when an override was
    /// removed; a failed write is a notice and false.</summary>
    private bool RemoveOverride(Adjacency adjacency, string playlist)
    {
        bool removed;
        try
        {
            removed = _store!.RemoveOverride(playlist, adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            RaiseStoreWriteFailure("Not removed", adjacency, e);
            return false;
        }
        if (removed)
        {
            StoreChanged();
            RaiseNotice($"Removed the override of {adjacency.Name} in {playlist}.");
            RescheduleIfPending(adjacency);
        }
        return removed;
    }

    /// <summary>Upserts the transition and, if the adjacency's boundary is pending in the current
    /// playback, reschedules it: editing a transition during the very playback it governs is the
    /// slice's main authoring loop. A boundary already in flight is left alone. The outcome is a
    /// notice: saved, or not saved and why. A store that cannot be written changes nothing, and
    /// the command stays total (build issue 09).
    /// <para>Impossible values are refused, not stored, and returned with their reasons for the
    /// editor to show beside each field (build issue 14). A duration that only runs past the end
    /// is not refused: the resolver shortens it. A blank spec removes the transition, as
    /// <see cref="Unauthor"/> does, so the store never holds an empty one.</para>
    /// <para>An adjacency with a <see cref="Track.Gone"/> side stores nothing, blank or not, and the
    /// notice names the file (phase 3 issue 02); <see cref="Unauthor"/> removes it. So does
    /// <see cref="AuthorOverride"/>.</para></summary>
    public IReadOnlyList<Refusal> Author(Adjacency adjacency, TransitionSpec spec)
    {
        ThrowIfDisposed();
        return Run(() =>
        {
            ObserveClock();
            if (_store is null)
            {
                RaiseNotice($"Not saved: {adjacency.Name}. {NoLibrary}");
                return (IReadOnlyList<Refusal>)[];
            }
            if (GoneReason(adjacency) is { } gone)
            {
                RaiseNotice($"Not saved: {adjacency.Name}. {gone}");
                return (IReadOnlyList<Refusal>)[];
            }
            if (spec.IsBlank)
            {
                if (!RemoveTransition(adjacency))
                {
                    RaiseNotice($"Nothing to save: every field of {adjacency.Name} is blank.");
                }
                return (IReadOnlyList<Refusal>)[];
            }
            var refusals = Refusals(adjacency, spec);
            if (refusals.Count > 0)
            {
                RaiseNotice($"Not saved: {adjacency.Name}. " + string.Join(" ", refusals.Select(r => r.Reason)));
                return refusals;
            }
            try
            {
                _store.Upsert(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath, spec);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseStoreWriteFailure("Not saved", adjacency, e);
                return [];
            }
            StoreChanged();
            RaiseNotice($"Saved {adjacency.Name}.");
            RescheduleIfPending(adjacency);
            return [];
        });
    }

    /// <summary>Removes the authored transition, rescheduling as <see cref="Author"/> does. Says
    /// so when there was one to remove, and says why when the store cannot be written.</summary>
    public void Unauthor(Adjacency adjacency)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            RemoveTransition(adjacency);
        });
    }

    /// <summary><see cref="Unauthor"/>'s body, under the lock. True when a transition was
    /// removed; a failed write is a notice and false.</summary>
    private bool RemoveTransition(Adjacency adjacency)
    {
        if (_store is null)
        {
            RaiseNotice($"Not removed: {adjacency.Name}. {NoLibrary}");
            return false;
        }
        bool removed;
        try
        {
            removed = _store.Remove(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            RaiseStoreWriteFailure("Not removed", adjacency, e);
            return false;
        }
        if (removed)
        {
            StoreChanged();
            RaiseNotice($"Removed {adjacency.Name}.");
            RescheduleIfPending(adjacency);
        }
        return removed;
    }

    /// <summary>Whether one row into another is authored in the open playlist, its override
    /// included, as last read from the store. A row's track never changes, so only a write to the
    /// store or another playlist opening makes an answer stale, and each clears them all.
    /// Publishing, once per file during an add, then reads the store only for rows that are new to
    /// it.</summary>
    private readonly Dictionary<(RowId Outgoing, RowId Incoming), bool> _authoredPairs = new();

    private bool IsAuthoredPair(QueueEntry outgoing, QueueEntry incoming)
    {
        if (!_authoredPairs.TryGetValue((outgoing.Id, incoming.Id), out var authored))
        {
            var (outPath, inPath) = (outgoing.Track.FilePath, incoming.Track.FilePath);
            authored = TransitionResolver.Merge(_store?.Get(outPath, inPath), OverrideOf(outPath, inPath)) is { IsBlank: false };
            _authoredPairs[(outgoing.Id, incoming.Id)] = authored;
        }
        return authored;
    }

    /// <summary>A transition was saved or removed: the rows' authored marks are published again.</summary>
    private void StoreChanged()
    {
        _storeWritten = true;
        _authoredPairs.Clear();
        Publish();
    }

    /// <summary>The values no track can play: a point past its track's length, measured from
    /// either end, and a negative duration. Only the engine knows both lengths.</summary>
    /// <summary>Why nothing can be saved for an adjacency with a gone side (phase 3 issue 02),
    /// naming each gone file; null when both files were found. Not a refusal of any field's value:
    /// the fields were never the problem.</summary>
    private static string? GoneReason(Adjacency adjacency)
    {
        var gone = new[] { adjacency.Outgoing, adjacency.Incoming }
            .Where(t => t.Gone).Select(t => t.FilePath).Distinct().ToList();
        return gone.Count == 0 ? null
            : $"{string.Join(" and ", gone)} {(gone.Count == 1 ? "is" : "are")} missing: this transition can be removed, not changed.";
    }

    private static List<Refusal> Refusals(Adjacency adjacency, TransitionSpec spec)
    {
        var refusals = new List<Refusal>();
        if (PointRefusal("fade-out point", spec.FadeOutPoint, adjacency.Outgoing) is { } outReason)
        {
            refusals.Add(new Refusal(TransitionField.FadeOutPoint, outReason));
        }
        if (spec.FadeOutDuration < TimeSpan.Zero)
        {
            refusals.Add(new Refusal(TransitionField.FadeOutDuration, "The fade-out duration cannot be negative."));
        }
        if (PointRefusal("fade-in point", spec.FadeInPoint, adjacency.Incoming) is { } inReason)
        {
            refusals.Add(new Refusal(TransitionField.FadeInPoint, inReason));
        }
        if (spec.FadeInDuration < TimeSpan.Zero)
        {
            refusals.Add(new Refusal(TransitionField.FadeInDuration, "The fade-in duration cannot be negative."));
        }
        return refusals;
    }

    private static string? PointRefusal(string name, FadePointSpec? point, Track track)
    {
        if (point is null || point.Position <= track.Length.TimeSpan)
        {
            return null;
        }
        var title = Path.GetFileNameWithoutExtension(track.FilePath);
        return point.FromEnd
            ? $"The {name}, measured from the end, is longer than {title}, which is {track.Length} long."
            : $"The {name} is past the end of {title}, which is {track.Length} long.";
    }

    /// <summary>Why there is no store to author in, worded for the user.</summary>
    private string NoLibrary => _library is { UnavailableReason: { } reason } library
        ? $"The library {library.Name} could not be opened ({reason})."
        : "No library is open.";

    /// <summary>Renders the adjacency's boundary to a WAV file as the open library and playlist
    /// have it: the global transition with the open playlist's override over it, under the
    /// library's settings. The editor's Render button (ticket 06). With no library it renders as
    /// unauthored.</summary>
    public RenderReport RenderBoundary(Adjacency adjacency, string wavPath)
    {
        ThrowIfDisposed();
        // The library and playlist open now, and the settings; rendering takes no lock.
        var (store, settings, playlist) = Run(() => (_store, _settings, _openPlaylist));
        return Renderer.RenderBoundary(adjacency, store, wavPath, settings: settings, playlist: playlist);
    }

    /// <summary>What a file write can throw, as <see cref="SaveAsPlaylist"/> catches it.</summary>
    private static bool IsStoreWriteFailure(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private void RaiseStoreWriteFailure(string outcome, Adjacency adjacency, Exception e) =>
        RaiseNotice($"{outcome}: {adjacency.Name}. The transition store could not be written: {e.Message}");

    private void RescheduleIfPending(Adjacency adjacency)
    {
        if (_currentVoice is not { } voice || !_voiceEntry.TryGetValue(voice, out var entry))
        {
            return;
        }
        if (entry.Track.FilePath != adjacency.Outgoing.FilePath)
        {
            return;
        }
        var index = _queue.IndexOf(entry.Id);
        var nextIndex = index < 0
            ? null
            : PlaybackQueue.Walk(_queue.IndexAfter(index, _settings.Repeat), RowAfter)
                .Where(i => !_passing.Contains(_queue.Entries[i])) // never stepped into
                .Select(i => (int?)i)
                .FirstOrDefault();
        var incomingPath = nextIndex is { } n ? _queue.Entries[n].Track.FilePath : null;
        if (incomingPath is not null && incomingPath != adjacency.Incoming.FilePath)
        {
            return; // the authored adjacency is not the one coming up
        }
        RescheduleCurrentBoundary();
    }
}
