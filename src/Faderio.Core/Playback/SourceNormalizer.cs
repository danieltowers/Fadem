using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Faderio.Core.Playback;

/// <summary>
/// Wraps a track source so every voice feeds the mixer in the engine's fixed format
/// (44.1 kHz stereo float): mono is duplicated, other sample rates are resampled.
/// Frame positions are expressed in the normalized (44.1 kHz) domain; seeking maps back.
/// </summary>
public sealed class NormalizedTrackSource : ITrackSource
{
    public const int EngineSampleRate = 44100;
    public const int EngineChannels = 2;

    private readonly ITrackSource _inner;
    private readonly ISampleProvider _pipeline;
    private readonly double _framesPerInnerFrame;
    private float[] _scratch = new float[0];
    private bool _disposed;

    private NormalizedTrackSource(ITrackSource inner, ISampleProvider pipeline, double framesPerInnerFrame)
    {
        _inner = inner;
        _pipeline = pipeline;
        _framesPerInnerFrame = framesPerInnerFrame;
    }

    public static ITrackSource Wrap(ITrackSource source)
    {
        var rate = source.WaveFormat.SampleRate;
        var channels = source.WaveFormat.Channels;
        if (rate == EngineSampleRate && channels == EngineChannels)
        {
            return source;
        }

        // The track source speaks the engine's frames convention; everything downstream in the
        // NAudio pipeline speaks interleaved samples. Adapt once at the boundary (see
        // FramesToSamplesProvider for why this matters).
        ISampleProvider pipeline = new FramesToSamplesProvider(source);
        var pipelineRate = rate;
        if (channels == 1)
        {
            pipeline = new MonoToStereoSampleProvider(pipeline);
        }

        double framesPerInnerFrame;
        if (pipelineRate != EngineSampleRate)
        {
            pipeline = new WdlResamplingSampleProvider(pipeline, EngineSampleRate);
            framesPerInnerFrame = (double)EngineSampleRate / pipelineRate;
        }
        else
        {
            framesPerInnerFrame = 1.0;
        }

        return new NormalizedTrackSource(source, pipeline, framesPerInnerFrame);
    }

    public WaveFormat WaveFormat { get; } =
        WaveFormat.CreateIeeeFloatWaveFormat(EngineSampleRate, EngineChannels);

    public long TotalFrames => (long)Math.Round(_inner.TotalFrames * _framesPerInnerFrame);

    /// <summary>Position in normalized frames; mapped back to the file's own frames on seek.
    /// One inner frame is <c>_framesPerInnerFrame</c> normalized frames, so the getter multiplies
    /// and the setter divides — the same direction as <see cref="TotalFrames"/>. v2 had these
    /// inverted, so Position outran TotalFrames by (rate/44100)² and every file above 44.1 kHz
    /// ended early (ticket 01, defect A).</summary>
    public long Position
    {
        get => (long)Math.Round(_inner.Position * _framesPerInnerFrame);
        set => _inner.Position = (long)Math.Round(value / _framesPerInnerFrame);
    }

    /// <summary>Frames in, frames out (the engine's convention): asks the NAudio pipeline for
    /// count × channels interleaved samples and converts the answer back to frames.</summary>
    public int Read(float[] buffer, int offset, int count)
    {
        var samples = count * EngineChannels;
        if (_scratch.Length < samples)
        {
            _scratch = new float[samples];
        }
        var samplesRead = _pipeline.Read(_scratch, 0, samples);
        var frames = samplesRead / EngineChannels;
        Array.Copy(_scratch, 0, buffer, offset, frames * EngineChannels);
        return frames;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _inner.Dispose();
    }
}

/// <summary>Duplicates a mono float stream into both stereo channels. NAudio samples
/// convention: count is interleaved samples, the return value is samples written.</summary>
public sealed class MonoToStereoSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private float[] _scratch = new float[4096];

    public MonoToStereoSampleProvider(ISampleProvider source)
    {
        if (source.WaveFormat.Channels != 1)
        {
            throw new ArgumentException("Source must be mono.", nameof(source));
        }
        _source = source;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var frames = count / 2;
        if (_scratch.Length < frames)
        {
            _scratch = new float[frames];
        }
        var read = _source.Read(_scratch, 0, frames);
        for (var f = 0; f < read; f++)
        {
            var sample = _scratch[f];
            buffer[offset + f * 2] = sample;
            buffer[offset + f * 2 + 1] = sample;
        }
        return read * 2;
    }
}
