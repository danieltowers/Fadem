using NAudio.Wave;

namespace Faderio.Core.Tests;

/// <summary>Generates synthetic WAV input for the engine tests (real NAudio decoders read it).</summary>
public static class TestAudio
{
    public const int SampleRate = 44100;

    public static string Dir { get; } =
        Path.Combine(Path.GetTempPath(), "faderio-tests", Guid.NewGuid().ToString("N"));

    private static int _fileCounter;

    public static string WriteWav(
        string name,
        double seconds,
        Func<long, double>? generator = null,
        int sampleRate = SampleRate)
    {
        Directory.CreateDirectory(Dir);
        // Unique per call: test classes run in parallel and must not collide on names.
        var path = Path.Combine(Dir, $"{Path.GetFileNameWithoutExtension(name)}-{Interlocked.Increment(ref _fileCounter)}.wav");
        var frames = (long)Math.Round(seconds * sampleRate);
        using var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 2));
        const int chunk = 4410;
        var buffer = new float[chunk * 2];
        for (var start = 0; start < frames; start += chunk)
        {
            var n = (int)Math.Min(chunk, frames - start);
            for (var f = 0; f < n; f++)
            {
                var value = generator is null ? 0.4 : generator(start + f);
                buffer[f * 2] = (float)value;
                buffer[f * 2 + 1] = (float)value;
            }
            writer.WriteSamples(buffer, 0, n * 2);
        }
        return path;
    }

    /// <summary>A constant-level (DC) track — exact for sample-level amplitude assertions.</summary>
    public static string WriteDcWav(string name, double seconds, double level) =>
        WriteWav(name, seconds, _ => level);

    /// <summary>A sine tone — separable from other voices by frequency (Goertzel in tests).</summary>
    public static string WriteToneWav(string name, double seconds, double freq, double amplitude = 0.4)
    {
        var w = 2.0 * Math.PI * freq / SampleRate;
        return WriteWav(name, seconds, f => amplitude * Math.Sin(w * f));
    }
}

/// <summary>Rendered output captured by the fake output, indexed by absolute frame.</summary>
public sealed class RenderedAudio
{
    private readonly List<float[]> _blocks;

    public RenderedAudio(List<float[]> blocks) => _blocks = blocks;

    public long Frames => _blocks.Sum(b => b.Length / 2);

    public double Sample(long frame, int channel)
    {
        var remaining = frame;
        foreach (var block in _blocks)
        {
            var blockFrames = block.Length / 2;
            if (remaining < blockFrames)
            {
                return block[remaining * 2 + channel];
            }
            remaining -= blockFrames;
        }
        throw new ArgumentOutOfRangeException(nameof(frame), "Frame beyond rendered output.");
    }

    /// <summary>Amplitude of a tone of the given frequency over the window ending at
    /// <paramref name="endFrameExclusive"/> (Goertzel). 0 for a window that has not rendered.</summary>
    public double ToneAmplitude(double freq, long endFrameExclusive, int window = 512)
    {
        var start = endFrameExclusive - window;
        if (start < 0 || endFrameExclusive > Frames)
        {
            return 0.0;
        }
        double w = 2.0 * Math.PI * freq / TestAudio.SampleRate;
        var coeff = 2.0 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        for (var i = start; i < endFrameExclusive; i++)
        {
            var s = Sample(i, 0) + coeff * s1 - s2;
            s2 = s1;
            s1 = s;
        }
        var power = s1 * s1 + s2 * s2 - coeff * s1 * s2;
        return 2.0 * Math.Sqrt(Math.Max(power, 0)) / window;
    }

    /// <summary>Average absolute sample value of the frame window ending at
    /// <paramref name="endFrameExclusive"/> — the rendered level of a DC test signal.</summary>
    public double DcLevel(long endFrameExclusive, int window = 256)
    {
        var start = Math.Max(0, endFrameExclusive - window);
        if (start >= endFrameExclusive || endFrameExclusive > Frames)
        {
            return 0.0;
        }
        double sum = 0;
        for (var i = start; i < endFrameExclusive; i++)
        {
            sum += Math.Abs(Sample(i, 0));
        }
        return sum / (endFrameExclusive - start);
    }
}
