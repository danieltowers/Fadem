using System.Collections.Immutable;

namespace Faderio.Core.Playback;

/// <summary>
/// The clip light's latch (build issue 17). A clip is rendered before it is heard: it waits here
/// at the device frame that will play it and latches once the device has emitted that frame
/// (ADR 0001). Clipped samples sounded by the same voices within <c>gap</c> frames of the last
/// are part of the same clip, so a transition or a track that clips throughout is one clip,
/// and a track that clips again after a quiet stretch is another. Engine-internal, used under
/// the engine lock; frames are the device's.
/// </summary>
internal sealed class ClipLatch
{
    private readonly long _gap;
    private readonly List<UnheardClip> _unheard = new();
    private ClipView? _heard;

    /// <summary>The voices of the last clip detected, and the device frame of its last clipped
    /// sample so far.</summary>
    private (Sounding Voices, long LastAt)? _last;

    public ClipLatch(long gap) => _gap = gap;

    /// <summary>A run of clipped samples by <paramref name="voices"/>, which the listener will hear
    /// from the device frame <paramref name="from"/> to <paramref name="to"/>. True when it
    /// begins a new clip.</summary>
    public bool Detect(long from, long to, Sounding voices, ClipCause cause)
    {
        var continues = _last is { } last && last.Voices == voices && from - last.LastAt <= _gap;
        _last = (voices, to);
        if (!continues)
        {
            _unheard.Add(new UnheardClip(from, cause));
        }
        return !continues;
    }

    /// <summary>Latches every clip the device has emitted. True when one was.</summary>
    public bool LatchHeard(long emitted)
    {
        var heard = _unheard.TakeWhile(c => c.HeardAt < emitted).Count();
        if (heard == 0)
        {
            return false;
        }
        _heard = UnheardClip.Latch(_heard, _unheard.Take(heard));
        _unheard.RemoveRange(0, heard);
        return true;
    }

    /// <summary>The device stopped: what it still held is never heard, and its count starts again,
    /// so the next clipped sample begins a new clip. A queue held from pause resumes after what
    /// the device dropped, and a clip there is heard for the first time.</summary>
    public void DeviceStopped()
    {
        _unheard.Clear();
        _last = null;
    }

    /// <summary>Clears the light. A clip rendered but not yet heard latches after the clear: the
    /// listener hears it after clearing. True when anything was lit.</summary>
    public bool Clear()
    {
        if (_unheard.Count == 0)
        {
            _last = null; // clipping that goes on is heard after the clear: it lights again
        }
        var lit = _heard is not null;
        _heard = null;
        return lit;
    }

    public ClipSnap Snap() => new(_heard, _unheard.ToImmutableArray());
}

/// <summary>A rendered clip waiting for the device frame that plays it.</summary>
internal sealed record UnheardClip(long HeardAt, ClipCause Cause)
{
    /// <summary>The light after <paramref name="clips"/> are heard, in order, on top of
    /// <paramref name="lit"/>: each adds one to the count and becomes the latest.</summary>
    public static ClipView? Latch(ClipView? lit, IEnumerable<UnheardClip> clips) =>
        clips.Aggregate(lit, (view, clip) => new ClipView((view?.Count ?? 0) + 1, clip.Cause));
}

/// <summary>The latch frozen for the view: the clips heard, and those still in the device, read at
/// the device's count like a playhead, so the view lights at the frame the listener hears.</summary>
internal sealed record ClipSnap(ClipView? Heard, ImmutableArray<UnheardClip> Unheard)
{
    public ClipView? At(long emitted) => UnheardClip.Latch(Heard, Unheard.TakeWhile(c => c.HeardAt < emitted));
}
