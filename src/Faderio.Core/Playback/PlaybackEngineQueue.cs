using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── Queue & settings ──────────────────────────────────────────────────────────

    public void SetQueue(IEnumerable<Track> tracks)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            ResetForRestart();
            Queue.SetTracks(tracks);
            _originalOrder = Queue.Tracks.ToList();
        }
    }

    public void ApplySettings(PlayerSettings settings)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            var outputModeChanged = settings.OutputMode != _settings.OutputMode;
            _settings = settings;
            _resolver.Settings = settings;
            if (outputModeChanged)
            {
                RecreateOutput();
            }
        }
    }

    // ── Preview (issue 0010) ──────────────────────────────────────────────────────

    /// <summary>Previews an adjacency through its effective transition: pre-roll of the
    /// outgoing track → the authored transition in real time → at least 5s past the fade-in
    /// point, then stops. Plays on its own output; the main playback position/state is not
    /// disturbed. Returns null when the preview cannot start (a notice is raised).</summary>
    public PreviewSession? BeginPreview(Adjacency adjacency)
    {
        ThrowIfDisposed();
        lock (_sync)
        {
            StopPreview();
            try
            {
                _preview = PreviewSession.Start(
                    adjacency, _store, _resolver, _settings, _sourceFactory, _outputFactory, RaiseNotice);
            }
            catch (Exception e)
            {
                RaiseNotice($"Could not preview transition: {e.Message}");
                _preview = null;
            }
            return _preview;
        }
    }

    public void StopPreview()
    {
        lock (_sync)
        {
            _preview?.Stop();
            _preview?.Dispose();
            _preview = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            StopPreview();
            ResetForRestart();
            _output?.Dispose();
            _output = null;
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PlaybackEngine));
        }
    }
}
