using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Read-convention boundaries (the field-reported FLAC-buzzing bug): the engine renders in
/// frames convention, NAudio's consumers use interleaved-samples convention, and
/// <see cref="FramesToSamplesProvider"/> is the adapter between them. The device binding uses
/// <see cref="FloatToBytesWaveProvider"/> (exactly-sized buffers — NAudio's
/// SampleToWaveProvider sizes its internal float buffer with the byte count, which corrupted
/// everything past the first quarter of each device block).
/// </summary>
public class AudioConventionTests
{
    /// <summary>A frames-convention ITrackSource emitting a constant DC level.</summary>
    private sealed class DcSource : ITrackSource
    {
        private long _pos;
        private readonly WaveFormat _format;
        private readonly float _level;

        public DcSource(int sampleRate, int channels, float level)
        {
            _format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
            _level = level;
        }

        public WaveFormat WaveFormat => _format;
        public long TotalFrames { get; } = 48000;
        public long Position { get => _pos; set => _pos = value; }

        public int Read(float[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, TotalFrames - _pos);
            var samples = count * _format.Channels;
            for (var i = 0; i < samples; i++)
            {
                buffer[offset + i] = _level;
            }
            _pos += count;
            return count;
        }

        public void Dispose() { }
    }

    private static void AssertAllFramesAtLevel(float[] buffer, float level)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            Assert.Equal(level, buffer[i], precision: 2);
        }
    }

    /// <summary>Asserts every sample equals the level, tolerating the resampler's filter
    /// warm-up transient over the first few samples of a stream.</summary>
    private static void AssertSteadyStateAtLevel(float[] buffer, float level)
    {
        const int warmUpSamples = 64;
        for (var i = warmUpSamples; i < buffer.Length; i++)
        {
            Assert.Equal(level, buffer[i], precision: 2);
        }
    }

    // --- NormalizedTrackSource: every rate/channel shape must be gap-free ---

    [Fact]
    public void NormalizedSource_48kStereo_NoGaps()
    {
        using var source = NormalizedTrackSource.Wrap(new DcSource(48000, 2, 0.5f));
        var buffer = new float[4410 * 2];
        var read = source.Read(buffer, 0, 4410);
        Assert.Equal(4410, read);
        AssertSteadyStateAtLevel(buffer, 0.5f);
    }

    [Fact]
    public void NormalizedSource_48kMono_NoGaps()
    {
        using var source = NormalizedTrackSource.Wrap(new DcSource(48000, 1, 0.5f));
        var buffer = new float[4410 * 2];
        var read = source.Read(buffer, 0, 4410);
        Assert.Equal(4410, read);
        AssertSteadyStateAtLevel(buffer, 0.5f);
    }

    [Fact]
    public void NormalizedSource_44k1Mono_NoGaps()
    {
        using var source = NormalizedTrackSource.Wrap(new DcSource(44100, 1, 0.5f));
        var buffer = new float[4410 * 2];
        var read = source.Read(buffer, 0, 4410);
        Assert.Equal(4410, read);
        AssertAllFramesAtLevel(buffer, 0.5f);
    }

    [Fact]
    public void NormalizedSource_44k1Stereo_PassesThrough()
    {
        using var source = NormalizedTrackSource.Wrap(new DcSource(44100, 2, 0.5f));
        var buffer = new float[4410 * 2];
        var read = source.Read(buffer, 0, 4410);
        Assert.Equal(4410, read);
        AssertAllFramesAtLevel(buffer, 0.5f);
    }

    [Fact]
    public void NormalizedSource_MapsFramesAcrossResample()
    {
        // 48000 frames at 48 kHz = 1 s = 44100 normalized frames (within resampler rounding).
        using var source = NormalizedTrackSource.Wrap(new DcSource(48000, 2, 0.5f));
        Assert.InRange(source.TotalFrames, 44050, 44150);
        source.Position = 22050; // half a second
        Assert.InRange(source.Position, 22040, 22060);
    }

    // --- Production device boundary: bytes over the adapted engine output ---

    /// <summary>An engine-convention provider whose level encodes the frame index: a ramp of
    /// one unit per second, so dropped or repeated frames show up as discontinuities.</summary>
    private sealed class RampMixer : ISampleProvider
    {
        private long _timeline;
        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(TestAudio.SampleRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            for (var f = 0; f < count; f++)
            {
                var value = (float)((_timeline + f) / (double)TestAudio.SampleRate);
                buffer[offset + f * 2] = value;
                buffer[offset + f * 2 + 1] = value;
            }
            _timeline += count;
            return count;
        }
    }

    /// <summary>Pulls from the engine exactly the way a real device does: the production chain
    /// (frames→samples adapter, then the exact-sized float→bytes converter) in small (10 ms)
    /// chunks, decoding the bytes back to floats.</summary>
    private static List<float> PullThroughDevice(ISampleProvider source, int chunks, int msPerChunk = 10)
    {
        var device = new FloatToBytesWaveProvider(new FramesToSamplesProvider(source));
        var output = new List<float>();
        var bytes = new byte[msPerChunk * 44100 / 1000 * 2 * 4]; // stereo float
        for (var i = 0; i < chunks; i++)
        {
            var read = device.Read(bytes, 0, bytes.Length);
            for (var s = 0; s < read / 4; s++)
            {
                output.Add(BitConverter.ToSingle(bytes, s * 4));
            }
        }
        return output;
    }

    [Fact]
    public void DevicePull_ThroughAdapter_RendersEveryFrameOnce()
    {
        var output = PullThroughDevice(new RampMixer(), 1000);
        Assert.True(output.Count >= TestAudio.SampleRate, "A second of audio must be rendered.");

        // The ramp must be continuous per channel (interleaved L,R): consecutive frames of one
        // channel advance by 1/44100. With the old frames-as-samples bug, half of every block
        // was dropped — boundary jumps of ~0.005 per 10 ms.
        var expectedStep = 1.0 / TestAudio.SampleRate;
        for (var i = 2; i < output.Count; i++)
        {
            var step = output[i] - output[i - 2];
            Assert.InRange(step, expectedStep / 2, expectedStep * 2);
        }

        // Every frame rendered exactly once: after one second of device time the ramp reads
        // ~1.0, not ~2.0 (double speed). Interleaved: frame 44099 sits at sample 2*44099+1.
        var oneSecond = 2 * (TestAudio.SampleRate - 1) + 1;
        Assert.InRange(output[oneSecond], 0.95, 1.05);
    }

    // --- The engine end to end, through the production device shape ---

    [Fact]
    public void EngineOutput_ThroughAdapter_PlaysFullLengthAtCorrectSpeed()
    {
        // A ramp rising 0 → 0.9 across the full 2 s track: never clips 16-bit, and the bug's
        // signature (dropping half of every pull) caps the audible ramp near 0.45.
        var path = TestAudio.WriteWav("convention-ramp", 2.0, f => 0.9 * f / 88200.0);
        using var harness = TestEngine.Create();
        harness.SetQueueOf(path);
        harness.Engine.Play();

        var output = PullThroughDevice(harness.Output.Source!, 300); // 3 s of device time
        Assert.True(output.Count >= TestAudio.SampleRate, "A second of audio must be rendered.");

        // Continuous per channel (interleaved L,R) while the ramp is sounding; after the track
        // ends the engine renders silence, which the scan simply runs past.
        var expectedStep = 1.8 / 88200.0; // two frames of ramp per stride
        var peak = 0.0;
        for (var i = 2; i < output.Count; i += 2)
        {
            if (output[i] >= 0.88)
            {
                break; // reached the end of the ramp
            }
            var step = output[i] - output[i - 2];
            // 16-bit WAV quantization (LSB = 3.05e-5) swamps the nominal step; the bug's
            // signature (drops of ~441 frames per 10 ms pull, ~0.005 jumps) is far larger.
            Assert.InRange(step, -0.0001, expectedStep + 0.0001);
            peak = Math.Max(peak, output[i]);
        }

        // Full length at correct speed: the ramp reaches ~0.9 (the track's full 2 s). With the
        // old frames-as-samples bug only half of each block was played, capping it near 0.45.
        Assert.InRange(peak, 0.85, 0.95);
    }
}
