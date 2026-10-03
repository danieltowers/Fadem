using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Dragging a fade on the seek bar (phase 6 issue 11) ────────────────────────────────

    /// <summary>Moves one end of one of <paramref name="row"/>'s fades to <paramref name="to"/>,
    /// keeping the other end: a start mark moves the fade's point and changes its duration so the
    /// end stays, an end mark changes the duration. The fade-in is the transition from the row
    /// before, the fade-out the one into the row after, by <see cref="AdjacenciesOf"/>. Saved as
    /// <see cref="Author"/> or <see cref="AuthorOverride"/> saves, to the layer the value came
    /// from, and refused as they refuse: a drop past the other end is a negative duration. The
    /// row's two fades never cross: a fade-out mark stops where the fade-in ends, a fade-in mark
    /// where the fade-out starts.</summary>
    public IReadOnlyList<Refusal> MoveFadeMark(RowId row, FadeMark mark, Playhead to)
    {
        ThrowIfDisposed();
        var side = mark.Side();
        var start = mark is FadeMark.FadeInStart or FadeMark.FadeOutStart;
        return Run(() => start
            ? MoveFadeBody(row, side, (fade, room) =>
            {
                var at = room.Clamp(to.TimeSpan);
                return new SideFade(at, fade.Point + fade.Duration - at);
            }, setsPoint: true, setsDuration: true)
            : MoveFadeBody(row, side, (fade, room) => fade with { Duration = room.Clamp(to.TimeSpan) - fade.Point }, setsPoint: false, setsDuration: true));
    }

    /// <summary>Slides one of <paramref name="row"/>'s fades by as far as the drag went, taken
    /// at <paramref name="from"/> and dropped at <paramref name="to"/>, keeping its duration. The
    /// fade stops whole at either end of its track, and where the row's other fade begins or ends,
    /// as <see cref="MoveFadeMark"/>'s marks stop. Saved and refused as it saves.</summary>
    public IReadOnlyList<Refusal> MoveFade(RowId row, FadeSide side, Playhead from, Playhead to)
    {
        ThrowIfDisposed();
        var by = to.TimeSpan - from.TimeSpan;
        return Run(() => MoveFadeBody(row, side, (fade, room) =>
        {
            var point = fade.Point + by;
            var last = room.To - fade.Duration;
            point = point > last ? last : point;
            return fade with { Point = point < room.From ? room.From : point };
        }, setsPoint: true, setsDuration: false));
    }

    /// <summary>A fade as played, on its own track: where it starts and how long it lasts.</summary>
    private sealed record SideFade(TimeSpan Point, TimeSpan Duration);

    /// <summary>Where on its track a dragged fade may lie: from the end of the row's fade-in, for
    /// a fade-out, or up to the start of its fade-out, for a fade-in; else the whole track.</summary>
    private sealed record FadeRoom(TimeSpan From, TimeSpan To)
    {
        public TimeSpan Clamp(TimeSpan at) => at < From ? From : at > To ? To : at;
    }

    /// <summary>The drags' body, under the lock: finds the adjacency, moves its fade as the
    /// resolver plays it, and authors the parameters the drag sets in the layer that sets them.
    /// The open playlist's override is written when it sets any of them; otherwise the global
    /// transition, its blanks first made explicit as played, so an unauthored adjacency becomes
    /// authored and the end a drag keeps stays put.</summary>
    private IReadOnlyList<Refusal> MoveFadeBody(
        RowId row, FadeSide side, Func<SideFade, FadeRoom, SideFade> move, bool setsPoint, bool setsDuration)
    {
        var fadeOut = side == FadeSide.FadeOut;
        var name = fadeOut ? "fade-out" : "fade-in";
        var index = _queue.IndexOf(row);
        if (index < 0)
        {
            RaiseNotice("Nothing moved: that row is no longer in the queue.");
            return [];
        }
        var adjacencies = _snapshot.AdjacenciesOf(row);
        if ((fadeOut ? adjacencies.Next : adjacencies.Previous) is not { } adjacency)
        {
            RaiseNotice($"Nothing moved: {_queue.Entries[index].Track} has no {name} here.");
            return [];
        }
        var played = Resolved(adjacency);
        if (played.IsHardCut)
        {
            RaiseNotice($"Nothing moved: {adjacency.Name} is a hard cut, with no {name} to move.");
            return [];
        }
        var track = fadeOut ? adjacency.Outgoing : adjacency.Incoming;
        var length = track.Length.TimeSpan;
        // The row's other fade, as it plays, bounds this one.
        var other = (fadeOut ? adjacencies.Previous : adjacencies.Next) is { } beside && Resolved(beside) is { IsHardCut: false } b ? b : null;
        TimeSpan Within(TimeSpan at) => at < length ? at : length;
        var room = fadeOut
            ? new FadeRoom(other is null ? TimeSpan.Zero : Within(other.FadeInPoint + other.FadeInDuration), length)
            : new FadeRoom(TimeSpan.Zero, other is null ? length : Within(other.FadeOutPoint));
        var moved = move(
            fadeOut ? new SideFade(played.FadeOutPoint, played.FadeOutDuration) : new SideFade(played.FadeInPoint, played.FadeInDuration),
            room);

        var (outPath, inPath) = (adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
        var global = _store?.Get(outPath, inPath) is { IsBlank: false } g ? g : null;
        var playlistOverride = StoredOverride(adjacency);
        var fromEnd = fadeOut && TransitionResolver.Merge(global, playlistOverride)?.FadeOutPoint is { FromEnd: true };
        var point = fromEnd ? new FadePointSpec(track.Length.TimeSpan - moved.Point, true) : new FadePointSpec(moved.Point, false);

        TransitionSpec Moved(TransitionSpec spec) => fadeOut
            ? spec with
            {
                FadeOutPoint = setsPoint ? point : spec.FadeOutPoint,
                FadeOutDuration = setsDuration ? moved.Duration : spec.FadeOutDuration,
            }
            : spec with
            {
                FadeInPoint = setsPoint ? point : spec.FadeInPoint,
                FadeInDuration = setsDuration ? moved.Duration : spec.FadeInDuration,
            };

        var overrideSets = playlistOverride is { } o && (fadeOut
            ? (setsPoint && o.FadeOutPoint is not null) || (setsDuration && o.FadeOutDuration is not null)
            : (setsPoint && o.FadeInPoint is not null) || (setsDuration && o.FadeInDuration is not null));
        if (overrideSets)
        {
            return AuthorOverride(adjacency, Moved(playlistOverride!));
        }

        // The global's blanks as the global alone plays them, so nothing of the override leaks
        // into it; a playlist-only transition has no global to play, so as the playlist plays it.
        var alone = global is null ? played : _resolver.Resolve(global, null, adjacency.Outgoing.Length.TimeSpan, adjacency.Incoming.Length.TimeSpan);
        var explicitGlobal = new TransitionSpec
        {
            FadeOutPoint = global?.FadeOutPoint ?? new FadePointSpec(alone.FadeOutPoint, false),
            FadeOutDuration = global?.FadeOutDuration ?? alone.FadeOutDuration,
            FadeInPoint = global?.FadeInPoint ?? new FadePointSpec(alone.FadeInPoint, false),
            FadeInDuration = global?.FadeInDuration ?? alone.FadeInDuration,
            Curve = global?.Curve,
        };
        return Author(adjacency, Moved(explicitGlobal));
    }
}
