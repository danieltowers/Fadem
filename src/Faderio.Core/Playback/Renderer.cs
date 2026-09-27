using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

/// <summary>What a render produced: the file, how much of it there is, and where in the file
/// the outgoing track's fade-out point sits.</summary>
public sealed record RenderReport(string Path, Length Rendered, Length FadeOutPointInFile);

/// <summary>
/// Renders one authored boundary to a WAV file through the production engine on a
/// <see cref="WavFileAudioOutput"/>: pre-roll of the outgoing track, the transition, and a tail
/// of the incoming track past its fade-in point. One button in the editor, no options (ticket 06):
/// this is the artifact the user listens to without launching the app, and the input to the
/// slice's measured assertion.
/// </summary>
public static class Renderer
{
    public static readonly TimeSpan DefaultPreRoll = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan DefaultTail = TimeSpan.FromSeconds(5);

    public static RenderReport RenderBoundary(
        Adjacency adjacency, ITransitionStore? store, string wavPath,
        TimeSpan? preRoll = null, TimeSpan? tail = null, int sampleRate = 44100)
    {
        var pre = preRoll ?? DefaultPreRoll;
        var after = tail ?? DefaultTail;

        using var output = new WavFileAudioOutput(wavPath, sampleRate);
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(output, store, sink);

        var report = engine.Add(new[] { adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath }).GetAwaiter().GetResult();
        if (report.Rejected.Count > 0)
        {
            throw new InvalidOperationException("Cannot render: " + string.Join("; ", report.Rejected));
        }
        var outgoingRow = report.Added[0];
        var incomingRow = report.Added[1];

        // Where the outgoing track starts fading, resolved the way the engine resolves it.
        var resolver = new TransitionResolver(new Settings.PlayerSettings());
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
