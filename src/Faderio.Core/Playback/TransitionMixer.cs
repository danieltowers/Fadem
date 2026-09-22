using Faderio.Core.Domain;
using NAudio.Wave;

namespace Faderio.Core.Playback;

/// <summary>A voice: one track source playing in the mix, with its own gain chain.</summary>
public sealed class MixerVoice
{
    internal MixerVoice(
        ITrackSource source, long timelineStart, long sourceStart,
        double baseGain, long fadeInFrames, FadeCurveKind fadeInCurve)
    {
        Source = source;
        TimelineStart = timelineStart;
        SourceStart = sourceStart;
        BaseGain = baseGain;
        FadeInFrames = fadeInFrames;
        FadeInCurve = fadeInCurve;
    }

    internal ITrackSource Source { get; }
    internal long TimelineStart { get; }
    internal long SourceStart { get; }
    internal double BaseGain { get; set; } = 1.0;
    internal long FadeInFrames { get; set; }
    internal FadeCurveKind FadeInCurve { get; set; } = FadeCurveKind.EqualPower;
    internal long? FadeOutStart { get; set; }
    internal long FadeOutFrames { get; set; }
    internal FadeCurveKind FadeOutCurve { get; set; } = FadeCurveKind.EqualPower;
    internal bool IsFinished { get; set; }

    /// <summary>Set when the voice is removed deliberately (skip/seek/stop); its end must not
    /// trigger the engine's advance logic.</summary>
    internal bool EndSuppressed { get; set; }

    /// <summary>Current fade envelope (0–1), as observed over the last rendered block.
    /// This is what the visualiser shows — level data from the engine, not UI polling.</summary>
    public double CurrentLevel { get; internal set; }

    internal double EnvelopeAt(long timeline) => TransitionMixer.Envelope(this, timeline);

    internal void CancelFadeIn() => FadeInFrames = 0;

    internal void CancelFadeOut() => FadeOutStart = null;
}

/// <summary>Handle for a scheduled timeline event; cancellable before it fires.</summary>
public sealed class ScheduledEventHandle
{
    internal ScheduledEventHandle(long timeline) => Timeline = timeline;

    internal long Timeline { get; }
    internal bool Cancelled { get; set; }
    internal bool Fired { get; set; }
}

/// <summary>
/// The transition mixer: mixes the currently active track voices with per-sample gain,
/// applies fade envelopes exactly on the sample-clock timeline, fires scheduled events at
/// authored sample positions, and tracks peak/level state for the visualiser.
///
/// All trigger timing is driven from the mixer's sample position — never a wall-clock or UI
/// timer. Unlike the prototype: no silence-pump buffers (silence is rendered directly when
/// there is nothing to mix), completed voices are cleaned up exactly once, and there is no
/// exception swallowing.
/// </summary>
public sealed class TransitionMixer : ISampleProvider
{
    public const int SampleRate = 44100;
    public const int Channels = 2;

    private static readonly WaveFormat OutputFormat =
        WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, Channels);

    private readonly List<MixerVoice> _voices = new();
    private readonly List<ScheduledEventHandle> _events = new();
    private readonly Dictionary<ScheduledEventHandle, Action> _handleActions = new();
    private float[] _scratch = new float[0];

    private long _timeline;

    public WaveFormat WaveFormat => OutputFormat;

    /// <summary>Frames rendered since the last reset — the mixer's sample position.</summary>
    public long Timeline => _timeline;

    /// <summary>Peak absolute sample value of the last rendered block (post-gain).</summary>
    public double Peak { get; private set; }

    /// <summary>Sticky clip flag: true once output has reached or exceeded full scale.</summary>
    public bool Clipping { get; private set; }

    /// <summary>Raised (on the rendering thread) when a voice's source is exhausted, with the
    /// exact timeline frame at which it ended — hard-cut boundaries chain sample-accurately.</summary>
    public event Action<MixerVoice, long>? VoiceEnded;

    /// <summary>Raised after each rendered block — the engine's per-block observable state
    /// hook for the visualiser.</summary>
    public event Action? BlockRendered;

    public IReadOnlyList<MixerVoice> Voices => _voices;

    public MixerVoice AddVoice(
        ITrackSource source,
        long timelineStart,
        long sourceStart,
        double baseGain = 1.0,
        long fadeInFrames = 0,
        FadeCurveKind fadeInCurve = FadeCurveKind.EqualPower)
    {
        var voice = new MixerVoice(source, timelineStart, sourceStart, baseGain, fadeInFrames, fadeInCurve);
        source.Position = sourceStart;
        _voices.Add(voice);
        return voice;
    }

    /// <summary>Schedules an action to fire exactly when the timeline reaches
    /// <paramref name="timeline"/> frames (sample-accurate trigger).</summary>
    public ScheduledEventHandle Schedule(long timeline, Action action)
    {
        var handle = new ScheduledEventHandle(timeline);
        _events.Add(handle);
        _handleActions[handle] = action;
        _events.Sort((a, b) => a.Timeline.CompareTo(b.Timeline));
        return handle;
    }

    public void Cancel(ScheduledEventHandle handle)
    {
        handle.Cancelled = true;
        _handleActions.Remove(handle);
        _events.Remove(handle);
    }

    /// <summary>Removes a voice immediately. Its end is reported through <see cref="VoiceEnded"/>
    /// so owners can clean up exactly once.</summary>
    public void RemoveVoice(MixerVoice voice)
    {
        if (voice.IsFinished || !_voices.Remove(voice))
        {
            return;
        }
        voice.IsFinished = true;
        voice.EndSuppressed = true;
        VoiceEnded?.Invoke(voice, _timeline);
    }

    /// <summary>Clears all voices, events and the timeline. Sources are not disposed — the
    /// engine owns them.</summary>
    public void Reset()
    {
        _voices.Clear();
        _events.Clear();
        _handleActions.Clear();
        _timeline = 0;
        Peak = 0;
        Clipping = false;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var start = _timeline;
        var needed = count * Channels;
        if (_scratch.Length < needed)
        {
            _scratch = new float[needed];
        }

        Array.Clear(buffer, offset, needed);

        // Fire every event whose timeline falls inside this block before mixing. This is exact:
        // fade events set absolute fade positions evaluated per sample, and spawned voices
        // begin rendering at their exact TimelineStart within the block.
        var firedAny = true;
        while (firedAny)
        {
            firedAny = false;
            foreach (var handle in _events.ToList())
            {
                if (handle.Timeline < start + count)
                {
                    Fire(handle);
                    firedAny = true;
                }
            }
        }

        // Mix each active voice over its contiguous run. When a voice's source is exhausted
        // mid-block its end is reported immediately, so the engine can chain the next voice
        // at the exact sample where the previous one ran out (hard-cut boundaries are
        // sample-accurate too).
        var processed = new HashSet<MixerVoice>();
        var progressed = true;
        while (progressed)
        {
            progressed = false;
            foreach (var voice in _voices.ToList())
            {
                if (processed.Contains(voice) || voice.IsFinished)
                {
                    continue;
                }

                var runStart = Math.Max(voice.TimelineStart - start, 0);
                if (runStart >= count)
                {
                    continue; // starts beyond this block
                }

                var framesToRender = (int)(count - runStart);
                var read = voice.Source.Read(_scratch, 0, framesToRender);
                var voiceLevel = 0.0;

                for (var f = 0; f < read; f++)
                {
                    var timeline = start + runStart + f;
                    var envelope = Envelope(voice, timeline);
                    if (envelope > voiceLevel)
                    {
                        voiceLevel = envelope;
                    }
                    var gain = voice.BaseGain * envelope;
                    var target = offset + (runStart + f) * Channels;
                    var source0 = f * Channels;
                    for (var c = 0; c < Channels; c++)
                    {
                        buffer[target + c] += (float)(_scratch[source0 + c] * gain);
                    }
                }

                voice.CurrentLevel = voiceLevel;
                processed.Add(voice);
                progressed = true;

                if (read < framesToRender || voice.Source.Position >= voice.Source.TotalFrames)
                {
                    // The source is exhausted. This must be detected even when the track ends
                    // exactly at a block boundary (read == requested).
                    voice.IsFinished = true;
                    _voices.Remove(voice);
                    if (!voice.EndSuppressed)
                    {
                        VoiceEnded?.Invoke(voice, start + runStart + read);
                    }
                }
            }
        }

        // Peak/clip measurement over the rendered block.
        var blockPeak = 0.0;
        for (var i = offset; i < offset + needed; i++)
        {
            var abs = Math.Abs(buffer[i]);
            if (abs > blockPeak)
            {
                blockPeak = abs;
            }
        }
        Peak = blockPeak;
        if (blockPeak >= 1.0)
        {
            Clipping = true;
        }

        _timeline = start + count;
        BlockRendered?.Invoke();
        return count;
    }

    private void Fire(ScheduledEventHandle handle)
    {
        _events.Remove(handle);
        if (_handleActions.Remove(handle, out var action) && !handle.Cancelled)
        {
            handle.Fired = true;
            action();
        }
    }

    internal static double Envelope(MixerVoice voice, long timeline)
    {
        double gain = 1.0;

        if (voice.FadeInFrames > 0)
        {
            var into = timeline - voice.TimelineStart;
            if (into < 0)
            {
                return 0.0;
            }
            if (into < voice.FadeInFrames)
            {
                gain *= FadeCurves.FadeInGain(voice.FadeInCurve, into / (double)voice.FadeInFrames);
            }
        }

        if (voice.FadeOutStart is { } fadeOutStart)
        {
            var into = timeline - fadeOutStart;
            if (into >= 0)
            {
                gain *= into < voice.FadeOutFrames
                    ? FadeCurves.FadeOutGain(voice.FadeOutCurve, into / (double)voice.FadeOutFrames)
                    : 0.0;
            }
        }

        return gain;
    }
}
