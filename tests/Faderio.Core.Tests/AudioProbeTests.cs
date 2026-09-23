using Faderio.Core.Domain;
using Faderio.Core.Playback;
using NAudio.Wave;

namespace Faderio.Core.Tests;

/// <summary>
/// The add-time probe (ADR 0007, ADR 0010): a file is promised or rejected on its header alone,
/// and the length it issues is the one the decoder will deliver. The promised table is
/// <c>docs/supported-audio.md</c>; each rejection here is a row of its Rejected table.
/// </summary>
public sealed class AudioProbeTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static Rejection? Probe(string path, out ProbeResult? result) => AudioProbe.Probe(path, out result);

    [Theory]
    [InlineData(44100, 16, 1)]
    [InlineData(48000, 24, 2)]
    [InlineData(96000, 24, 1)]
    public void PromisedWav_IsAccepted_WithItsShapeAndLength(int rate, int bits, int channels)
    {
        var path = TestAudio.WriteWavAt("ok", 2, _ => 0.3, rate, bits, channels);

        var rejection = Probe(path, out var result);

        Assert.Null(rejection);
        Assert.Equal(new AudioShape(AudioFormat.Wav, rate, bits, channels), result!.Shape);
        Assert.Equal(2 * rate, result.Frames);
        Assert.Equal(2000, result.Length.Milliseconds);
    }

    [Fact]
    public void Flac_IsAccepted_FromItsStreamInfo()
    {
        var rejection = Probe(FixturePath("tone-1k.flac"), out var result);

        Assert.Null(rejection);
        Assert.Equal(new AudioShape(AudioFormat.Flac, 44100, 16, 2), result!.Shape);
        Assert.Equal(88200, result.Frames);
    }

    [Fact]
    public void Mp3_IsAccepted_FromItsFirstFrameAndInfoTag()
    {
        var rejection = Probe(FixturePath("tone-1k.mp3"), out var result);

        Assert.Null(rejection);
        Assert.Equal(new AudioShape(AudioFormat.Mp3, 44100, null, 2), result!.Shape);
        // 2 s at 44.1 kHz is 76.5 frames of 1152; the encoder pads to whole frames.
        Assert.InRange(result.Frames, 88200, 88200 + 1152 * 2);
    }

    [Fact]
    public void MoreThanTwoChannels_IsRejected_NamingChannels()
    {
        var path = TestAudio.WriteWavAt("quad", 1, _ => 0.3, 44100, 16, 4);

        var rejection = Probe(path, out _);

        Assert.NotNull(rejection);
        Assert.Equal(RejectedProperty.Channels, rejection!.Property);
        Assert.Equal(4, rejection.Shape!.Channels);
    }

    [Fact]
    public void EightBitWav_IsRejected_NamingBitDepth()
    {
        var path = WriteRawWav("eight", new WaveFormat(44100, 8, 2));

        var rejection = Probe(path, out _);

        Assert.Equal(RejectedProperty.BitDepth, rejection!.Property);
        Assert.Equal(8, rejection.Shape!.BitDepth);
    }

    [Fact]
    public void FloatWav_IsRejected_NamingBitDepth()
    {
        var path = WriteRawWav("float", WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));

        var rejection = Probe(path, out _);

        Assert.Equal(RejectedProperty.BitDepth, rejection!.Property);
        Assert.Equal(32, rejection.Shape!.BitDepth);
    }

    [Fact]
    public void UnpromisedRate_IsRejected_NamingSampleRate()
    {
        var path = TestAudio.WriteWavAt("slow", 1, _ => 0.3, 22050, 16, 2);

        var rejection = Probe(path, out _);

        Assert.Equal(RejectedProperty.SampleRate, rejection!.Property);
        Assert.Equal(22050, rejection.Shape!.SampleRate);
    }

    [Theory]
    [InlineData("x.ogg")]
    [InlineData("x.m4a")]
    [InlineData("x.wma")]
    [InlineData("x.aiff")]
    public void UnpromisedFormat_IsRejected_BeforeAnyDecoderIsTouched(string name)
    {
        var path = Path.Combine(TestAudio.Dir, Guid.NewGuid().ToString("N") + name);
        Directory.CreateDirectory(TestAudio.Dir);
        File.WriteAllText(path, "not audio");

        var rejection = Probe(path, out _);

        Assert.Equal(RejectedProperty.Format, rejection!.Property);
        Assert.Null(rejection.Shape);
    }

    [Fact]
    public void MissingFile_IsRejected_AsAFileProblem()
    {
        var rejection = Probe(@"C:\nowhere\missing.flac", out _);
        Assert.Equal(RejectedProperty.File, rejection!.Property);
    }

    [Fact]
    public void TheIssuedLength_IsTheHeadersClaim_EvenWhenTheFileIsShorter()
    {
        // ADR 0010: the header is what the probe reads; the decoder finds out the truth later.
        var path = TestAudio.WriteDcWav("long", 2, 0.4);
        Truncate(path, 1.0);

        var rejection = Probe(path, out var result);

        Assert.Null(rejection);
        Assert.Equal(88200, result!.Frames);
    }

    [Fact]
    public void AudioEndingBeforeItsLength_IsOneNotice_AndTheBoundaryFiresWhereTheAudioEnds()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("truncated", 2, 0.4);
        Truncate(a, 1.0);
        var b = TestAudio.WriteDcWav("b", 2, 0.2);
        tc.Play(a, b);

        Assert.Equal(2000, tc.View.Attended!.Length.Milliseconds); // what the header claimed
        tc.Output.Advance(44100 * 3);

        var notice = Assert.Single(tc.Notices);
        Assert.Contains("ends", notice);
        Assert.Contains("before its header claims", notice);
        // B started where A's audio actually ended, one second in, not at the claimed two.
        Assert.Equal(0.4, tc.Rendered.Sample(44099, 0), 2);
        Assert.Equal(0.2, tc.Rendered.Sample(44100, 0), 2);
    }

    /// <summary>Cuts a 16-bit stereo 44.1 kHz WAV's data to <paramref name="seconds"/> without
    /// touching its header, so the header lies about the length.</summary>
    private static void Truncate(string path, double seconds)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite);
        // The writer's header size is whatever precedes the two seconds of data it wrote.
        var headerBytes = stream.Length - 2L * 44100 * 4;
        stream.SetLength(headerBytes + (long)(seconds * 44100) * 4);
    }

    private static string WriteRawWav(string name, WaveFormat format)
    {
        Directory.CreateDirectory(TestAudio.Dir);
        var path = Path.Combine(TestAudio.Dir, $"{name}-{Guid.NewGuid():N}.wav");
        using var writer = new WaveFileWriter(path, format);
        var bytes = new byte[format.AverageBytesPerSecond];
        writer.Write(bytes, 0, bytes.Length);
        return path;
    }
}
