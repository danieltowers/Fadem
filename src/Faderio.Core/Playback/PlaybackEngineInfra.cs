using Faderio.Core.Domain;
using NAudio.Wave;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    private void ResetForRestart()
    {
        foreach (var voice in _mixer.Voices.ToList())
        {
            voice.Source.Dispose();
            _voiceTrackIndex.Remove(voice);
        }
        _mixer.Reset();
        _currentVoice = null;
        _outgoingVoice = null;
        _pendingBoundary = null;
        _finishedRaised = false;
        _latestVisualiser = new VisualiserState(0, false, Array.Empty<VisualiserVoiceLevel>());
    }

    private void RestoreOriginalOrder()
    {
        var currentPath = Queue.CurrentTrack?.FilePath;
        Queue.SetTracks(_originalOrder);
        if (currentPath is not null)
        {
            var index = _originalOrder.ToList().FindIndex(t => t.FilePath == currentPath);
            if (index >= 0)
            {
                Queue.SetCurrent(index);
            }
        }
        if (_currentVoice is not null)
        {
            _voiceTrackIndex[_currentVoice] = Queue.CurrentIndex;
        }
    }

    private double BaseGain(Track track)
    {
        if (!_settings.ReplayGainEnabled || track.ReplayGainDb is not { } db)
        {
            return 1.0;
        }
        return ReplayGain.DbToMultiplier(ReplayGain.ClampDb(db));
    }

    private void RecreateOutput()
    {
        _output?.Dispose();
        _lockingOutput ??= new LockingOutput(this);
        _output = _outputFactory.Create(_settings.OutputMode, RaiseNotice);
        _output.Initialise(_lockingOutput, _settings.OutputMode);
        _output.Stopped += OnOutputStopped;
    }

    private void OnOutputStopped(object? sender, OutputStoppedEventArgs e)
    {
        if (e.Error is not null)
        {
            RaiseNotice($"Audio output stopped: {e.Error.Message}");
        }
    }

    private void OnBlockRendered()
    {
        var levels = new List<VisualiserVoiceLevel>();
        foreach (var voice in _mixer.Voices)
        {
            if (_voiceTrackIndex.TryGetValue(voice, out var index))
            {
                levels.Add(new VisualiserVoiceLevel(index, Math.Min(voice.CurrentLevel, 1.0)));
            }
        }
        _latestVisualiser = new VisualiserState(_mixer.Peak, _mixer.Clipping, levels);
        VisualiserStateChanged?.Invoke(this, _latestVisualiser);
    }

    private void RaiseNotice(string message) => Notice?.Invoke(this, message);

    private static long Frames(TimeSpan t) => EngineTime.Frames(t);

    internal static TimeSpan TimeSpanOf(long frames) => EngineTime.TimeSpanOf(frames);

    /// <summary>Serialises all device access through the engine lock: the output device pulls
    /// through this provider, so rendering and control calls share one lock.</summary>
    private sealed class LockingOutput : ISampleProvider
    {
        private readonly PlaybackEngine _engine;

        public LockingOutput(PlaybackEngine engine) => _engine = engine;

        public WaveFormat WaveFormat => _engine._mixer.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            lock (_engine._sync)
            {
                try
                {
                    return _engine._mixer.Read(buffer, offset, count);
                }
                catch (Exception e)
                {
                    // Errors are surfaced, never silently swallowed.
                    Array.Clear(buffer, offset, count * TransitionMixer.Channels);
                    _engine.RaiseNotice($"Audio rendering error: {e.Message}");
                    return count;
                }
            }
        }
    }
}
