using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Settings ─────────────────────────────────────────────────────────────────────────

    /// <summary>How long a voice's base gain takes to move to a new value: long enough that the
    /// step is no click, short enough to be heard as at once.</summary>
    private static readonly TimeSpan GainRampDuration = TimeSpan.FromMilliseconds(5);

    /// <summary>Saves the open library's settings and applies each one (build issue 12). A
    /// changed default crossfade reschedules the pending boundary if its transition has not
    /// started; one in flight plays out (the audible-time rule), and so does a fade-out point
    /// already passed: a crossfade switched on in a track's last N seconds leaves its boundary a
    /// hard cut. A ReplayGain toggle reaches every live voice at once, each ramping to its own
    /// tag's gain (build issue 13).
    /// N out of range is brought into it. The outcome is a notice, as for authoring: saved, or
    /// not saved and why, and then nothing changes. With no library open there is nowhere to
    /// save them, so nothing changes. Settings the same as those in force are no change and
    /// no notice. Shuffle and repeat are the transport's (<see cref="SetShuffle"/>,
    /// <see cref="SetRepeat"/>, <see cref="SetEditorFollowsPlayback"/>) and the last playlist is
    /// the engine's (playlists issue 01): the ones in <paramref name="settings"/> are not used.</summary>
    public void ChangeSettings(PlayerSettings settings)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            var next = settings.Normalised() with
            {
                Shuffle = _settings.Shuffle, Repeat = _settings.Repeat, LastPlaylist = _settings.LastPlaylist,
                EditorFollowsPlayback = _settings.EditorFollowsPlayback,
            };
            var previous = _settings;
            if (next == previous || !KeepSettings(next, "settings"))
            {
                return;
            }
            RaiseNotice("Saved settings.");
            if (previous.UnauthoredAdjacency != next.UnauthoredAdjacency ||
                previous.DefaultCrossfadeSeconds != next.DefaultCrossfadeSeconds ||
                previous.BlankDurationSeconds != next.BlankDurationSeconds)
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

    /// <summary>Whether the editor follows playback: saved and put in force as
    /// <see cref="SetShuffle"/> is. Nothing plays differently; the view says what to follow to,
    /// as <see cref="PlayerView.EditorFollow"/>.</summary>
    public void SetEditorFollowsPlayback(bool on)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            if (_settings.EditorFollowsPlayback == on || !KeepSettings(_settings with { EditorFollowsPlayback = on }, "sync editor"))
            {
                return;
            }
            Publish();
        });
    }

    /// <summary>Saves <paramref name="next"/> as the open library's settings and puts them in
    /// force: they are in force once they are on disk. Not saved, a notice says why, naming
    /// <paramref name="what"/>, and nothing changes. Applying each value is the caller's.</summary>
    private bool KeepSettings(PlayerSettings next, string what)
    {
        if (_store is null)
        {
            RaiseNotice($"Not saved: {what}. {NoLibrary}");
            return false;
        }
        try
        {
            _settingsStore?.Save(next);
        }
        catch (Exception e) when (IsStoreWriteFailure(e))
        {
            RaiseNotice($"Not saved: {what}. The settings file could not be written: {e.Message}");
            return false;
        }
        _settings = next;
        _resolver.Settings = next;
        return true;
    }

    /// <summary>Saves this machine's settings (library issue 03, D11). The output mode is
    /// saved for the next launch: the engine rate is fixed at launch (ADR 0008), so nothing
    /// changes now. The library to reopen is the engine's to remember, so the one in
    /// <paramref name="settings"/> is not used. The outcome is a notice, as for
    /// <see cref="ChangeSettings"/>; settings the same as those saved are no change and no
    /// notice. With no machine settings file in this run, nothing is saved.</summary>
    public void ChangeMachineSettings(MachineSettings settings)
    {
        ThrowIfDisposed();
        Run(() =>
        {
            var saved = _machine?.Settings ?? new MachineSettings();
            var next = settings.Normalised() with { Library = saved.Library };
            if (next == saved)
            {
                return;
            }
            if (_machine is null)
            {
                RaiseNotice("Not saved: machine settings. This run keeps no machine settings file.");
                return;
            }
            try
            {
                _machine.Save(next);
            }
            catch (Exception e) when (IsStoreWriteFailure(e))
            {
                RaiseNotice($"Not saved: machine settings. The machine settings file could not be written: {e.Message}");
                return;
            }
            RaiseNotice("Saved machine settings.");
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
