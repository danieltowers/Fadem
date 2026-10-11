using Fadem.Core.Playback;
using NAudio.Wave;

namespace Fadem.Core.Tests;

/// <summary>Generates synthetic WAV input for the engine tests (real NAudio decoders read it).</summary>
public static class TestAudio
{
    public const int SampleRate = 44100;

    public static string Dir { get; } =
        Path.Combine(TestTemp.Root, "audio", Guid.NewGuid().ToString("N"));

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

    /// <summary>A WAV in an arbitrary rate, depth and channel count — the real-input matrix's
    /// WAV cells (ticket 06, seam S2). 24-bit is written with an extensible header, as ffmpeg
    /// and most tools do; that header is what exposed defect B (ticket 01).</summary>
    public static string WriteWavAt(
        string name, double seconds, Func<long, double> generator,
        int sampleRate, int bitsPerSample, int channels)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"{Path.GetFileNameWithoutExtension(name)}-{Interlocked.Increment(ref _fileCounter)}.wav");
        var frames = (long)Math.Round(seconds * sampleRate);
        WaveFormat format = bitsPerSample == 16
            ? new WaveFormat(sampleRate, 16, channels)
            : new WaveFormatExtensible(sampleRate, bitsPerSample, channels);
        using var writer = new WaveFileWriter(path, format);
        const int chunk = 4410;
        var buffer = new float[chunk * channels];
        for (var start = 0L; start < frames; start += chunk)
        {
            var n = (int)Math.Min(chunk, frames - start);
            for (var f = 0; f < n; f++)
            {
                var value = (float)generator(start + f);
                for (var c = 0; c < channels; c++)
                {
                    buffer[f * channels + c] = value;
                }
            }
            writer.WriteSamples(buffer, 0, n * channels);
        }
        return path;
    }

    /// <summary>Pink noise, <paramref name="frames"/> long, peak-normalised to
    /// <paramref name="peak"/>: the seek-landing signal (ticket 02). Every window of it is
    /// unlike every other, so a block read after a seek matches one place in a sequential
    /// decode; a tone would match every cycle. Paul Kellet's three-pole filter over seeded
    /// white noise, so the same seed is the same file. Mirrors ffmpeg's <c>anoisesrc</c> for
    /// the checked-in fixtures.</summary>
    /// <summary>A copy of the 2 s 44.1 kHz 16-bit stereo tone FLAC, tagged as a user's library
    /// would be: title "Quiet Song", artist "Example Band", and the album and ReplayGain track and album gains
    /// given, if any (playlists issue 06).</summary>
    public static string WriteTaggedFlac(string name, string? album, string? replayGain, string? albumGain = null)
    {
        Directory.CreateDirectory(Dir);
        var path = Path.Combine(Dir, $"{Path.GetFileNameWithoutExtension(name)}-{Interlocked.Increment(ref _fileCounter)}.flac");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone-44100-16-2.flac"), path);
        using var file = TagLib.File.Create(path);
        file.Tag.Title = "Quiet Song";
        file.Tag.Performers = ["Example Band"];
        file.Tag.Album = album;
        if (replayGain is not null)
        {
            var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true);
            xiph.SetField(TrackMetadataReader.ReplayGainTrackGainKey, replayGain);
        }
        if (albumGain is not null)
        {
            var xiph = (TagLib.Ogg.XiphComment)file.GetTag(TagLib.TagTypes.Xiph, create: true);
            xiph.SetField(TrackMetadataReader.ReplayGainAlbumGainKey, albumGain);
        }
        file.Save();
        return path;
    }

    public static double[] PinkNoise(long frames, int seed = 42, double peak = 0.5)
    {
        var rng = new Random(seed);
        var samples = new double[frames];
        double b0 = 0, b1 = 0, b2 = 0, max = 0;
        for (var i = 0; i < frames; i++)
        {
            var white = rng.NextDouble() * 2 - 1;
            b0 = 0.99765 * b0 + white * 0.0990460;
            b1 = 0.96300 * b1 + white * 0.2965164;
            b2 = 0.57000 * b2 + white * 1.0526913;
            samples[i] = b0 + b1 + b2 + white * 0.1848;
            max = Math.Max(max, Math.Abs(samples[i]));
        }
        for (var i = 0; i < frames; i++)
        {
            samples[i] *= peak / max;
        }
        return samples;
    }

    /// <summary>Reads a source to its end in 4096-frame pulls and disposes it; returns the left
    /// channel.</summary>
    internal static float[] LeftChannel(Fadem.Core.Playback.ITrackSource source)
    {
        using (source)
        {
            var channels = source.WaveFormat.Channels;
            var buffer = new float[4096 * channels];
            var left = new List<float>();
            int n;
            while ((n = source.Read(buffer, 0, 4096)) > 0)
            {
                for (var f = 0; f < n; f++)
                {
                    left.Add(buffer[f * channels]);
                }
            }
            return left.ToArray();
        }
    }

    /// <summary>Where <paramref name="block"/> occurs in <paramref name="reference"/>: the offset
    /// with the highest cosine similarity over every possible alignment, and that similarity
    /// (1.0 is an exact match, noise-level is about 0.1). Ticket 02's cross-correlation, made
    /// permanent: with a sequential decode as the reference and the first block read after a
    /// seek as the needle, the offset is where the seek landed, whatever the decoder's gain or
    /// timeline. Searches offsets <paramref name="from"/> to <paramref name="to"/>, or the
    /// whole reference when they are omitted, so a wildly wrong landing is reported as where it
    /// went, not as "no match".</summary>
    public static (int Offset, double Similarity) Locate(
        IReadOnlyList<float> reference, ReadOnlySpan<float> block, int from = 0, int to = int.MaxValue)
    {
        var n = block.Length;
        var haystack = reference as float[] ?? reference.ToArray();
        var needle = block.ToArray();
        var needleEnergy = Dot(needle, needle);
        // Prefix sums of squares, so the energy of any reference window is one subtraction.
        var prefix = new double[haystack.Length + 1];
        for (var i = 0; i < haystack.Length; i++)
        {
            prefix[i + 1] = prefix[i] + (double)haystack[i] * haystack[i];
        }
        var best = -1;
        var bestScore = double.NegativeInfinity;
        var last = Math.Min(to, haystack.Length - n);
        for (var k = Math.Max(0, from); k <= last; k++)
        {
            var energy = prefix[k + n] - prefix[k];
            if (energy <= 1e-12)
            {
                continue; // silence matches nothing
            }
            var dot = System.Numerics.Vector.IsHardwareAccelerated
                ? Dot(haystack.AsSpan(k, n), needle)
                : DotAt(haystack, k, needle);
            var score = dot / Math.Sqrt(energy * needleEnergy);
            if (score > bestScore)
            {
                bestScore = score;
                best = k;
            }
        }
        return (best, bestScore);
    }

    /// <summary><see cref="Dot"/> where there is no SIMD, net48 x86: Vector&lt;float&gt;'s
    /// software fallback, and Span's indexer there, made Locate most of the WMA tests' time.
    /// Plain arrays, which that JIT compiles to a tight loop.</summary>
    private static double DotAt(float[] haystack, int offset, float[] needle)
    {
        double s0 = 0, s1 = 0, s2 = 0, s3 = 0;
        var j = 0;
        for (; j + 4 <= needle.Length; j += 4)
        {
            s0 += haystack[offset + j] * needle[j];
            s1 += haystack[offset + j + 1] * needle[j + 1];
            s2 += haystack[offset + j + 2] * needle[j + 2];
            s3 += haystack[offset + j + 3] * needle[j + 3];
        }
        for (; j < needle.Length; j++)
        {
            s0 += haystack[offset + j] * needle[j];
        }
        return s0 + s1 + s2 + s3;
    }

    private static double Dot(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var va = System.Runtime.InteropServices.MemoryMarshal.Cast<float, System.Numerics.Vector<float>>(a);
        var vb = System.Runtime.InteropServices.MemoryMarshal.Cast<float, System.Numerics.Vector<float>>(b.Slice(0, a.Length));
        var acc = System.Numerics.Vector<float>.Zero;
        for (var v = 0; v < va.Length; v++)
        {
            acc += va[v] * vb[v];
        }
        var i = va.Length * System.Numerics.Vector<float>.Count;
        double sum = System.Numerics.Vector.Dot(acc, System.Numerics.Vector<float>.One);
        for (; i < a.Length; i++)
        {
            sum += (double)a[i] * b[i];
        }
        return sum;
    }

    /// <summary>Fraction of a window's energy sitting at <paramref name="freq"/>: 1.0 is a pure
    /// tone, and the buzzing bug or silence collapse it. Goertzel at an exact bin, so the window
    /// must hold a whole number of cycles. Ticket 01's purity measure, made permanent.</summary>
    public static double Purity(IReadOnlyList<float> mono, int start, int frames, double freq, int sampleRate)
    {
        var w = 2.0 * Math.PI * freq / sampleRate;
        var coeff = 2.0 * Math.Cos(w);
        double s1 = 0, s2 = 0, total = 0;
        for (var i = start; i < start + frames; i++)
        {
            double x = mono[i];
            total += x * x;
            var s = x + coeff * s1 - s2;
            s2 = s1;
            s1 = s;
        }
        var binPower = s1 * s1 + s2 * s2 - coeff * s1 * s2; // |X[k]|²
        // Parseval, for a real signal concentrated in one bin: 2·|X[k]|² = N·Σx².
        return total <= 0 ? 0.0 : 2.0 * binPower / (frames * total);
    }

    /// <summary>Reads <paramref name="source"/> to its end in the fake output's block size and
    /// returns the frames it delivered.</summary>
    internal static long ReadToEnd(ITrackSource source)
    {
        var buffer = new float[4410 * source.WaveFormat.Channels];
        long read = 0;
        int n;
        while ((n = source.Read(buffer, 0, 4410)) > 0)
        {
            read += n;
        }
        return read;
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
