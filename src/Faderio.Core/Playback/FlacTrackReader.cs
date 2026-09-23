using System.Collections.ObjectModel;
using NAudio.Flac;

namespace Faderio.Core.Playback;

/// <summary>
/// The production FLAC decoder: <c>BunLabs.NAudio.Flac</c>'s <see cref="FlacReader"/>, which
/// pre-scans every frame header on open and, on seek, lands on the frame before the target and
/// decodes-and-discards to the exact sample (ADR 0009). Two defects in 2.0.1 are repaired here
/// rather than worked around in the wrapper, so that <see cref="ITrackSource.Position"/> stays
/// the decoder's word (ticket 15):
/// <list type="number">
/// <item>Its byte counter is skipped on any read that reaches the end of the stream, so after a
/// file is drained <c>Position</c> stops short of <c>Length</c> by the last partial read. The
/// 96 kHz fixture showed it: 192000 frames drained, position 189630.</item>
/// <item>Its seek walks the pre-scan for the first frame starting at or after the target, so a
/// target inside the last frame matches nothing and the seek is silently a no-op.</item>
/// </list>
/// This class counts frames itself and seeks into the last frame by hand: to that frame's first
/// sample through the package's own exact seek, then forward by decoding. Everything else is
/// the package's. The pre-scan runs synchronously on open; its cost on a long file is measured
/// in ticket 15 and is the reason the add-time probe reads the header itself (ticket 12).
/// </summary>
internal sealed class FlacTrackReader : FlacReader
{
    private readonly ReadOnlyCollection<FlacFrameInformation> _frames;
    private long _positionBytes;

    public static FlacTrackReader Open(string path)
    {
        var scan = new ScanCapture();
        var stream = File.OpenRead(path);
        try
        {
            return new FlacTrackReader(stream, scan);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private FlacTrackReader(Stream stream, ScanCapture scan)
        : base(stream, FlacPreScanMode.Sync, scan.Capture)
    {
        _frames = scan.Frames
            ?? throw new InvalidDataException("The FLAC pre-scan reported no frames.");
        if (_frames.Count == 0)
        {
            throw new InvalidDataException("The FLAC stream holds no audio frames.");
        }
    }

    /// <summary>The frame table the pre-scan hands back, captured during the base constructor
    /// (a lambda there could not touch <c>this</c>).</summary>
    private sealed class ScanCapture
    {
        public ReadOnlyCollection<FlacFrameInformation>? Frames { get; private set; }

        public void Capture(FlacPreScanFinishedEventArgs e) => Frames = e.Frames;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = base.Read(buffer, offset, count);
        _positionBytes += read;
        return read;
    }

    public override long Position
    {
        // The package's seek reads Position back through this virtual getter to work out how
        // much to decode and discard, so while it seeks the getter must answer with the
        // package's own counter, not ours.
        get => _seeking ? base.Position : _positionBytes;
        set
        {
            var target = Math.Clamp(value - value % BlockAlign, 0, Length);
            var lastFrameStart = _frames[_frames.Count - 1].SampleOffset * BlockAlign;
            _seeking = true;
            try
            {
                if (target >= lastFrameStart)
                {
                    // The package's seek matches no frame here. Its seek to the last frame's
                    // own start is exact (it decodes the frame before and discards it), and
                    // from there the rest of the way is decoded and dropped.
                    base.Position = lastFrameStart;
                    Discard(target - lastFrameStart);
                }
                else
                {
                    base.Position = target;
                }
            }
            finally
            {
                _seeking = false;
            }
            _positionBytes = target;
        }
    }

    private bool _seeking;

    private void Discard(long bytes)
    {
        if (bytes <= 0)
        {
            return;
        }
        var chunk = 64 * 1024;
        chunk -= chunk % BlockAlign;
        var scratch = new byte[chunk];
        while (bytes > 0)
        {
            var wanted = (int)Math.Min(chunk, bytes);
            wanted -= wanted % BlockAlign;
            var read = base.Read(scratch, 0, wanted);
            if (read <= 0)
            {
                break;
            }
            bytes -= read;
        }
    }
}
