using Faderio.Core.Domain;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Playback;

/// <summary>
/// An on-demand preview of one adjacency (issue 0010): pre-roll of the outgoing track → the
/// effective transition in real time → at least 5s past the fade-in point, then stops.
/// Runs on its own mixer and its own audio output, so the main playback position and state
/// are never disturbed. Pre-roll is 5s by default (2s/5s/10s setting).
/// </summary>
public sealed class PreviewSession : IDisposable
{
    private readonly TransitionMixer _mixer = new();
    private readonly IAudioOutput _output;
    private readonly List<ITrackSource> _sources = new();
    private readonly Action<string> _notice;
    private bool _stopped;
    private bool _disposed;

    private PreviewSession(IAudioOutput output, Action<string> notice)
    {
        _output = output;
        _notice = notice;
    }

    /// <summary>Raised when the preview finishes on its own (the user does nothing).</summary>
    public event EventHandler? Ended;

    public static PreviewSession Start(
        Adjacency adjacency,
        ITransitionStore store,
        TransitionResolver resolver,
        PlayerSettings settings,
        IAudioSourceFactory sourceFactory,
        IAudioOutputFactory outputFactory,
        Action<string> notice)
    {
        var output = outputFactory.Create(settings.OutputMode, notice);
        var session = new PreviewSession(output, notice);
        try
        {
            session.Build(adjacency, store, resolver, settings, sourceFactory);
        }
        catch
        {
            session.Dispose();
            throw;
        }
        return session;
    }

    private void Build(
        Adjacency adjacency,
        ITransitionStore store,
        TransitionResolver resolver,
        PlayerSettings settings,
        IAudioSourceFactory sourceFactory)
    {
        var outgoing = OpenNormalized(sourceFactory, adjacency.Outgoing);
        var incoming = OpenNormalized(sourceFactory, adjacency.Incoming);
        _sources.Add(outgoing);
        _sources.Add(incoming);

        var totalOut = outgoing.TotalFrames;
        var global = store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath);
        var effective = resolver.Resolve(global, null, TimeSpanOf(totalOut), adjacency.Incoming.Duration);

        // Pre-roll is a setting offered as 2/5/10 s (design.md D3); snap to the nearest allowed.
        var requested = Math.Max(1, settings.PreviewPreRollSeconds);
        var preRollSeconds = new[] { 2, 5, 10 }.OrderBy(v => Math.Abs(v - requested)).First();
        var preRoll = Frames(TimeSpan.FromSeconds(preRollSeconds));
        var fadeOutPoint = Math.Clamp(Frames(effective.FadeOutPoint), 0, totalOut);
        var startSource = Math.Clamp(fadeOutPoint - preRoll, 0, Math.Max(totalOut - 1, 0));

        var outgoingVoice = _mixer.AddVoice(outgoing, 0, startSource, 1.0, 0, effective.Curve);

        long trigger;
        var fadeFrames = Frames(effective.FadeOutDuration);
        if (fadeFrames <= 0)
        {
            // Hard cut (or no authored outgoing fade): the outgoing track plays out, then the
            // incoming track begins — the boundary is still heard as the player default.
            trigger = totalOut - startSource;
            fadeFrames = 0;
        }
        else
        {
            trigger = fadeOutPoint - startSource;
            if (fadeOutPoint + fadeFrames >= totalOut)
            {
                fadeFrames = Math.Max(0, totalOut - fadeOutPoint); // fade completes at the track end
            }
        }
        outgoingVoice.FadeOutStart = trigger;
        outgoingVoice.FadeOutFrames = fadeFrames;
        outgoingVoice.FadeOutCurve = effective.Curve;

        var fadeInFrames = Frames(effective.FadeInDuration);
        var incomingStart = Math.Clamp(
            Frames(effective.FadeInPoint), 0, Math.Max(incoming.TotalFrames - 1, 0));
        var capturedTrigger = trigger;
        var capturedCurve = effective.Curve;
        _mixer.Schedule(trigger, () =>
        {
            _mixer.AddVoice(incoming, capturedTrigger, incomingStart, 1.0, fadeInFrames, capturedCurve);
        });

        // At least 5s past the fade-in point completes, then stops (design.md D3).
        var postFadeIn = Math.Max(Frames(TimeSpan.FromSeconds(5)), fadeInFrames);
        var stopTimeline = capturedTrigger + Frames(effective.FadeInPoint) + postFadeIn;
        _mixer.Schedule(stopTimeline, Stop);

        _output.Initialise(_mixer, settings.OutputMode);
        _output.Play();
    }

    private static ITrackSource OpenNormalized(IAudioSourceFactory factory, Track track)
    {
        try
        {
            return NormalizedTrackSource.Wrap(factory.Open(track));
        }
        catch (Exception e)
        {
            throw new InvalidOperationException(
                $"Could not open \"{Path.GetFileName(track.FilePath)}\" for preview: {e.Message}", e);
        }
    }

    /// <summary>Stops the preview (also fired automatically when it plays out).</summary>
    public void Stop()
    {
        if (_stopped)
        {
            return;
        }
        _stopped = true;
        try
        {
            _output.Stop();
        }
        catch (Exception e)
        {
            _notice($"Preview output error: {e.Message}");
        }
        Ended?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Stop();
        foreach (var source in _sources)
        {
            source.Dispose();
        }
        _sources.Clear();
        _output.Dispose();
    }

    private static long Frames(TimeSpan t) => EngineTime.Frames(t);

    private static TimeSpan TimeSpanOf(long frames) => EngineTime.TimeSpanOf(frames);
}
