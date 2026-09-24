using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    // ── The clip light (build issue 17) ──────────────────────────────────────────────────

    /// <summary>A clip ends after this long with no clipped sample from the same voices: a track
    /// that clips again after a quiet stretch has clipped twice.</summary>
    private static readonly TimeSpan ClipGap = TimeSpan.FromSeconds(1);

    /// <summary>Clips rendered, heard, and the light they lit. It survives Stop and Clear; only
    /// <see cref="ClearClip"/> clears it, and nothing keeps it across a restart. Made in the
    /// constructor, at the engine's rate.</summary>
    private readonly ClipLatch _clips;

    /// <summary>Clears the clip light. Clips rendered but still in the device latch when heard,
    /// after the clear.</summary>
    public void ClearClip()
    {
        ThrowIfDisposed();
        Run(() =>
        {
            ObserveClock();
            if (_clips.Clear())
            {
                Publish();
            }
        });
    }

    /// <summary>The queue's mixer clipped: its timeline frame plays at the device frame
    /// <see cref="_queueBase"/> before it.</summary>
    private void OnClipped(ClippedRun run) => ClipRendered(run, -_queueBase);

    /// <summary>The preview's mixer clipped: its timeline zero plays at <see cref="_previewOrigin"/>.</summary>
    private void OnPreviewClipped(ClippedRun run) => ClipRendered(run, _previewOrigin);

    /// <summary>A clip rendered, named by the voices sounding at its samples: the adjacency whose
    /// transition is in flight when two overlap, else the attended track, the one voice heard.
    /// It waits for the device frame that plays it, <paramref name="toDevice"/> from its timeline
    /// frame: the view lights there, and the next clock observation publishes it.</summary>
    private void ClipRendered(ClippedRun run, long toDevice)
    {
        var (first, second) = run.Voices;
        if (first.Track is not { } track)
        {
            return;
        }
        var cause = second?.Track is { } incoming ? ClipCause.Of(new Adjacency(track, incoming)) : ClipCause.Of(track);
        if (_clips.Detect(run.From + toDevice, run.To + toDevice, run.Voices, cause))
        {
            RefreshClips();
        }
    }

    /// <summary>Latches the clips the device has played, and publishes them.</summary>
    private void HearClips()
    {
        if (_clips.LatchHeard(_output.FramesEmitted.Count))
        {
            Publish();
        }
    }

    /// <summary>Puts the latch into the view without a publication: nothing the listener hears has
    /// changed yet.</summary>
    private void RefreshClips() => _snapshot = _snapshot with { Clips = _clips.Snap() };
}
