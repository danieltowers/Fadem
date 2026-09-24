using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Settings ─────────────────────────────────────────────────────────────────────────

    /// <summary>Saves the settings and applies each one (build issue 12). A changed default
    /// crossfade reschedules the pending boundary if its transition has not started; one in
    /// flight plays out (the audible-time rule), and so does a fade-out point already passed: a
    /// crossfade switched on in a track's last N seconds leaves its boundary a hard cut. The
    /// output mode is saved for the next launch: the engine rate is fixed at launch (ADR 0008).
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
            Publish();
        });
    }
}
