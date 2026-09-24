using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Settings ─────────────────────────────────────────────────────────────────────────

    /// <summary>How long a voice's base gain takes to move to a new value: long enough that the
    /// step is no click, short enough to be heard as at once.</summary>
    private static readonly TimeSpan GainRampDuration = TimeSpan.FromMilliseconds(5);

    /// <summary>Saves the settings and applies each one (build issue 12). A changed default
    /// crossfade reschedules the pending boundary if its transition has not started; one in
    /// flight plays out (the audible-time rule), and so does a fade-out point already passed: a
    /// crossfade switched on in a track's last N seconds leaves its boundary a hard cut. A
    /// ReplayGain toggle reaches every live voice at once, each ramping to its own tag's gain
    /// (build issue 13). The output mode is saved for the next launch: the engine rate is fixed
    /// at launch (ADR 0008).
    /// N out of range is brought into it. The outcome is a notice, as for authoring: saved, or
    /// not saved and why, and then nothing changes.</summary>
    public void ChangeSettings(PlayerSettings settings)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            var next = settings.Normalised();
            if (next == _settings)
            {
                return;
            }
            try
            {
                _settingsStore?.Save(next);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseNotice($"Not saved: settings. The settings file could not be written: {e.Message}");
                return;
            }
            RaiseNotice("Saved settings.");
            var previous = _settings;
            _settings = next;
            _resolver.Settings = next;
            if (previous.UnauthoredAdjacency != next.UnauthoredAdjacency ||
                previous.DefaultCrossfadeSeconds != next.DefaultCrossfadeSeconds)
            {
                RescheduleCurrentBoundary();
            }
            if (previous.ReplayGainEnabled != next.ReplayGainEnabled)
            {
                RampLiveVoicesToTheirGain();
            }
            Publish();
        });
    }

    /// <summary>Every live voice ramps to its own track's base gain under the settings now in
    /// force, from the decode position: heard one device buffer later (the audible-time rule).
    /// An untagged track's gain is 1 either way, so it does not move.</summary>
    private void RampLiveVoicesToTheirGain()
    {
        void Ramp(MixerVoice voice, Track track, TransitionMixer mixer) =>
            voice.RampBaseGain(BaseGain(track), mixer.Timeline, Frames(GainRampDuration));

        foreach (var voice in _mixer.Voices)
        {
            if (_voiceEntry.TryGetValue(voice, out var entry))
            {
                Ramp(voice, entry.Track, _mixer);
            }
        }
        if (_preview is { } run)
        {
            foreach (var voice in run.Voices.Where(v => !v.IsFinished))
            {
                Ramp(voice, run.TrackOf(voice), _previewMixer); // a preview's voices too (build issue 16)
            }
        }
    }
}
