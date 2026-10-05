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
        var rejection = Probe(FixturePath("tone-44100-16-2.flac"), out var result);

        Assert.Null(rejection);
        Assert.Equal(new AudioShape(AudioFormat.Flac, 44100, 16, 2), result!.Shape);
        Assert.Equal(88200, result.Frames);
    }

    [Fact]
    public void Mp3_IsAccepted_FromItsFirstFrameAndInfoTag()
    {
        var rejection = Probe(FixturePath("tone-cbrinfo-44100-2.mp3"), out var result);

        Assert.Null(rejection);
        Assert.Equal(new AudioShape(AudioFormat.Mp3, 44100, null, 2), result!.Shape);
        // 78 frames of 1152, less the encoder's delay (576) and padding (1080) from its valid
        // LAME tag, is the 88200 samples that were encoded (issue 18). The matrix proves the
        // trimming reader decodes exactly that (Mp3_HeaderLengthEqualsDecoded).
        Assert.Equal(88200, result.Frames);
    }

    [Theory]
    [InlineData("tone-cbrinfo-48000-1.mp3", 96000)]
    [InlineData("tone-vbr-44100-2.mp3", 88200)]
    [InlineData("tone-vbr-48000-1.mp3", 96000)]
    public void Mp3WithAValidLameTag_IssuesTheEncodedSampleCount(string fixture, long encoded)
    {
        Assert.Null(Probe(FixturePath(fixture), out var result));
        Assert.Equal(encoded, result!.Frames);
    }

    [Fact]
    public void Mp3WithALameEncoderString_IsTrimmedLikeFfmpegs()
    {
        // No encoder allowlist: a passing CRC already proves LAME's layout.
        var path = CraftedMp3.WithEncoder(FixturePath("tone-cbrinfo-44100-2.mp3"), "LAME3.99r");

        Assert.Null(Probe(path, out var result));
        Assert.Equal(88200, result!.Frames);
    }

    [Fact]
    public void BareMp3_KeepsWholeFrames()
    {
        Assert.Null(Probe(FixturePath("tone-cbrbare-44100-2.mp3"), out var result));
        Assert.Equal(78 * 1152, result!.Frames);
    }

    [Fact]
    public void Mp3TagFailingItsCrc_KeepsWholeFrames_Silently()
    {
        var path = CraftedMp3.WithBrokenTagCrc(FixturePath("tone-cbrinfo-44100-2.mp3"));

        Assert.Null(Probe(path, out var result)); // still promised: no rejection, no notice
        Assert.Equal(78 * 1152, result!.Frames);
    }

    [Theory]
    [InlineData(-20 * 1024, false)] // cut after encoding: more than 10 KiB (and 5%) short
    [InlineData(+20 * 1024, false)]
    [InlineData(-10 * 1024, true)]  // within 10 KiB, the larger allowance for a small file
    [InlineData(+10 * 1024, true)]
    public void Mp3TagMusicLength_TrimsOnlyWithinTheAllowance(int by, bool trimmed)
    {
        var fixture = FixturePath("tone-cbrinfo-44100-2.mp3");
        var path = CraftedMp3.WithMusicLength(fixture, (uint)(CraftedMp3.MusicLength(fixture) + by));

        Assert.Null(Probe(path, out var result));
        Assert.Equal(trimmed ? 88200 : 78 * 1152, result!.Frames);
    }

    [Theory]
    [InlineData(2881, 1080, 78 * 1152)]               // delay beyond 2880: whole frames
    [InlineData(576, 2305, 78 * 1152)]                // padding beyond 2304: whole frames
    [InlineData(2880, 2304, 78 * 1152 - 2880 - 2304)] // the ceilings themselves trim
    public void Mp3TagValues_TrimOnlyWithinTheirCeilings(int delay, int padding, long issued)
    {
        var path = CraftedMp3.WithDelayAndPadding(FixturePath("tone-cbrinfo-44100-2.mp3"), delay, padding);

        Assert.Null(Probe(path, out var result));
        Assert.Equal(issued, result!.Frames);
    }

    [Fact]
    public void Mp3TagClaimingMoreTrimThanTheFileHolds_KeepsWholeFrames()
    {
        // delay + padding must be less than frames x 1152. Each value here is at its ceiling and
        // in range, the tag is re-signed, and 4 frames (4608) cannot hold 2880 + 2304.
        var fourFrames = CraftedMp3.WithFrameCount(FixturePath("tone-cbrinfo-44100-2.mp3"), 4);
        var path = CraftedMp3.WithDelayAndPadding(fourFrames, 2880, 2304);

        Assert.Null(Probe(path, out var result));
        Assert.Equal(4 * 1152, result!.Frames);
    }

    [Fact]
    public void CraftingATagWithItsOwnValues_ChangesNoByte()
    {
        // Guards CraftedMp3: its CRC is the one ffmpeg wrote, so a re-signed tag that passes is
        // a real pass, not a shared mistake between the helper and the probe.
        var fixture = FixturePath("tone-cbrinfo-44100-2.mp3");

        var path = CraftedMp3.WithDelayAndPadding(fixture, 576, 1080);

        Assert.Equal(File.ReadAllBytes(fixture), File.ReadAllBytes(path));
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
