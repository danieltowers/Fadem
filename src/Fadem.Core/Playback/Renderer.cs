using Fadem.Core.Domain;
using Fadem.Core.Queue;
using Fadem.Core.Settings;
using Fadem.Core.Transitions;

namespace Fadem.Core.Playback;

/// <summary>What a render produced: the file, how much of it there is, and where in the file
/// the outgoing track's fade-out point sits.</summary>
public sealed record RenderReport(string Path, Length Rendered, Length FadeOutPointInFile);

/// <summary>
/// Renders one authored boundary to a WAV file through the production engine on a
/// <see cref="WavFileAudioOutput"/>: pre-roll of the outgoing track, the transition, and a tail
/// of the incoming track past its fade-in point. One button in the editor, no options (ticket 06):
/// this is the artifact the user listens to without launching the app, and the input to the
/// slice's measured assertion. Rendered under the library's settings as the caller passes them,
/// so an unauthored boundary renders as it plays, with the default crossfade when opted in, and
/// blank durations and ReplayGain as set. Shuffle and repeat are the transport's, not the
/// boundary's: a render always goes from the outgoing track into the incoming one. With no
/// settings passed, their defaults. Given a playlist, the boundary renders as that playlist plays
/// it: its override laid over the global transition, parameter by parameter.
/// </summary>
public static class Renderer
{
    /// <summary>The store as one playlist plays it, read-only: each pair's transition is the
    /// global one with the playlist's override laid over it, and there are no overrides beside
    /// it. The render's engine opens no playlist of its own, so it never touches the playlists or
    /// their overrides; this is how it hears one.</summary>
    private sealed class AsPlayedIn(ITransitionStore store, string playlist) : ITransitionStore
    {
        public TransitionSpec? Get(string outgoingPath, string incomingPath) =>
            TransitionResolver.Merge(store.Get(outgoingPath, incomingPath), store.GetOverride(playlist, outgoingPath, incomingPath));

        public IReadOnlyList<(string Outgoing, string Incoming)> Pairs() =>
            store.Pairs().Concat(store.OverridePairs(playlist)).Distinct().ToList();

        public TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath) => null;

        public IReadOnlyList<(string Outgoing, string Incoming)> OverridePairs(string playlistName) => [];

        public void Upsert(string outgoingPath, string incomingPath, TransitionSpec spec) => throw ReadOnly();

        public bool Remove(string outgoingPath, string incomingPath) => throw ReadOnly();

        public void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial) =>
            throw ReadOnly();

        public bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath) => throw ReadOnly();

        public void RenamePlaylist(string oldName, string newName) => throw ReadOnly();

        public void RemovePlaylist(string playlistName) => throw ReadOnly();

        public bool Holding { get; set; }

        public void WriteHeld()
        {
        }

        public void DropHeld()
        {
        }

        private static InvalidOperationException ReadOnly() => new("A render never writes the store.");
    }

    public static readonly TimeSpan DefaultPreRoll = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultTail = TimeSpan.FromSeconds(5);

    public static RenderReport RenderBoundary(
        Adjacency adjacency, ITransitionStore? store, string wavPath,
        TimeSpan? preRoll = null, TimeSpan? tail = null, int sampleRate = 44100, PlayerSettings? settings = null,
        string? playlist = null)
    {
        if (store is not null && playlist is not null)
        {
            store = new AsPlayedIn(store, playlist);
        }
        settings = (settings ?? new PlayerSettings()).Normalised() with { Shuffle = false, Repeat = RepeatMode.Off };
        var pre = preRoll ?? DefaultPreRoll;
        var after = tail ?? DefaultTail;

        using var output = new WavFileAudioOutput(wavPath, sampleRate);
        var sink = new RecordingSink();
        // A file is written faster than it would play: the incoming track is opened inline, or
        // the boundary would be reached before a background open had finished.
        using var engine = new PlaybackEngine(output, store, sink, settings) { StartPrepareWorker = work => work() };

        var report = engine.Add(new[] { adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath }).GetAwaiter().GetResult();
        if (report.Rejected.Count > 0)
        {
            throw new InvalidOperationException("Cannot render: " + string.Join("; ", report.Rejected));
        }
        var outgoingRow = report.Added[0];
        var incomingRow = report.Added[1];

        // Where the outgoing track starts fading, resolved the way the engine resolves it.
        var resolver = new TransitionResolver(settings);
        var effective = resolver.Resolve(
            store?.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath), null,
            outgoingRow.Track.Length.TimeSpan, incomingRow.Track.Length.TimeSpan);
        var fadeOutPoint = effective.IsHardCut ? outgoingRow.Track.Length.TimeSpan : effective.FadeOutPoint;
        var start = fadeOutPoint - pre < TimeSpan.Zero ? TimeSpan.Zero : fadeOutPoint - pre;

        engine.PlayRow(outgoingRow.Id);
        engine.Seek(Playhead.FromMilliseconds((int)Math.Round(start.TotalMilliseconds)));

        // Enough for the pre-roll, the longer fade, and the tail; stop early if playback ends.
        var budget = (fadeOutPoint - start) + effective.FadeOutDuration + effective.FadeInDuration + after
                     + TimeSpan.FromSeconds(1);
        var budgetFrames = Ticks.FramesOf(budget, sampleRate);
        var block = new Frames(4410);
        var tailFrames = Ticks.FramesOf(after, sampleRate);
        long? handedOverAt = null;
        while (output.FramesWritten < budgetFrames && engine.View.Transport != Transport.Stopped)
        {
            output.Advance(block);
            var view = engine.View;
            if (handedOverAt is null && view.Attended?.Row.Id == incomingRow.Id && view.Incoming is null)
            {
                handedOverAt = output.FramesWritten; // the outgoing voice has fallen silent
            }
            if (handedOverAt is { } at && output.FramesWritten >= at + tailFrames)
            {
                break; // the handoff has settled
            }
        }
        engine.Stop();

        return new RenderReport(
            wavPath,
            Length.FromFrames(output.FramesWritten, sampleRate),
            new Length(fadeOutPoint - start));
    }
}
