using NAudio.Wave;

namespace Fadem.Core.Playback;

/// <summary>
/// Float → 32-bit IEEE float byte adapter with an exactly-sized internal buffer. Replaces
/// NAudio's <c>SampleToWaveProvider</c> at the device boundary: that provider sizes its
/// internal float buffer with the requested <em>byte</em> count (four times the needed sample
/// count) and with the engine's render convention the mismatch corrupted everything past the
/// first quarter of each device block (audible buzz — the field-reported FLAC bug).
/// </summary>
public sealed class FloatToBytesWaveProvider : IWaveProvider
{
    private readonly ISampleProvider _source;
    private float[] _scratch = new float[0];

    public FloatToBytesWaveProvider(ISampleProvider source) => _source = source;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(byte[] buffer, int offset, int count)
    {
        var samples = count / 4;
        if (_scratch.Length < samples)
        {
            _scratch = new float[samples];
        }
        var samplesRead = _source.Read(_scratch, 0, samples);
        for (var i = 0; i < samplesRead; i++)
        {
            var bytes = BitConverter.GetBytes(_scratch[i]);
            buffer[offset + i * 4] = bytes[0];
            buffer[offset + i * 4 + 1] = bytes[1];
            buffer[offset + i * 4 + 2] = bytes[2];
            buffer[offset + i * 4 + 3] = bytes[3];
        }
        return samplesRead * 4;
    }
}


/// <summary>
/// Adapts the engine's <c>frames</c> read convention to NAudio's <c>samples</c> convention.
///
/// The engine's ISampleProviders (mixer output, ITrackSource implementations) use frames
/// semantics: <c>Read(buffer, offset, count)</c> takes <paramref name="count"/> as the number
/// of stereo frames (44.1 kHz) and returns the number of frames rendered, writing exactly
/// <c>rendered × channels</c> floats. NAudio's consumers (<c>SampleToWaveProvider</c>,
/// <c>WdlResamplingSampleProvider</c>, <c>MonoToStereoSampleProvider</c>, ...) use
/// interleaved-samples semantics: <paramref name="count"/> is the number of floats across all
/// channels and the return value is the number of floats written.
///
/// Feeding a frames-convention provider straight into an NAudio consumer makes every consumer
/// treat frames as samples: it plays only the first half of each rendered block and discards
/// the rest — audible as a periodic buzz with playback running about twice as fast (bug found
/// in the field with FLAC files; it affected every format). Wrap at every boundary between
/// the engine's convention and an NAudio consumer.
/// </summary>
internal sealed class FramesToSamplesProvider : ISampleProvider
{
    private readonly ISampleProvider _inner;
    private readonly int _channels;

    public FramesToSamplesProvider(ISampleProvider inner)
    {
        _inner = inner;
        _channels = inner.WaveFormat.Channels;
    }

    public WaveFormat WaveFormat => _inner.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        // NAudio asked for `count` samples; that is count/channels frames in the engine's
        // convention. The inner provider writes rendered × channels floats — exactly the
        // samples this adapter reports back.
        var frames = count / _channels;
        var rendered = _inner.Read(buffer, offset, frames);
        return rendered * _channels;
    }
}

/// <summary>Presents an <see cref="IFrameSource"/> (the engine's output seam) to NAudio as a
/// samples-convention <see cref="ISampleProvider"/>. The device adapters and the file writer
/// pull through this; the typed <see cref="Frames"/> parameter is where a samples count can no
/// longer be mistaken for a frames count (ADR 0003).</summary>
public sealed class FrameSourceSampleProvider : ISampleProvider
{
    private readonly IFrameSource _source;

    public FrameSourceSampleProvider(IFrameSource source)
    {
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = _source.Read(buffer, offset, new Frames(count / _source.Channels));
        return (int)frames.Count * _source.Channels;
    }
}
