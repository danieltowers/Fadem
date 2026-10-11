using Fadem.Core.Playback;
using NAudio.Wave;

namespace Fadem.Core.Tests;

/// <summary>
/// Pins <c>D</c>, where Windows' closed ACM MP3 decoder puts the encoded source's first sample
/// (issue 18). Standard decoders emit a 529-sample decoder delay ahead of it, on top of the
/// encoder's delay in the LAME tag; ACM's was unmeasured, and <see cref="Mp3TrackReader"/> trims
/// by it. Measured by correlating the raw decode (no trim) against the generator's source PCM:
/// the lossless FLAC fixture of the same pink noise, from the same <c>lavfi</c> expression and
/// seed in <c>Fixtures/generate.sh</c>. On 2026-09-24 every cell landed at exactly
/// 576 + 529 = 1105, at both rates, mono and stereo, CBR (tagged and bare) and VBR, so D is
/// stable and is 529.
/// If this goes red, D has moved (a Windows update to the codec): do not re-pin by guessing.
/// </summary>
public sealed class Mp3DecoderOffsetTests
{
    /// <summary>LAME's ENCDELAY, which ffmpeg's libmp3lame writes into every tag. A bare file
    /// records nothing, but the generator encoded it with the same delay.</summary>
    private const int EncoderDelay = 576;

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Theory, MemberData(nameof(RealInputMatrixTests.Mp3Cells), MemberType = typeof(RealInputMatrixTests))]
    public void AcmDecoderOffset_Is529_AfterTheEncodersDelay(Mp3Variant variant, int rate, int channels)
    {
        var mp3 = FixturePath($"noise-{variant.ToString().ToLowerInvariant()}-{rate}-{channels}.mp3");
        var source = TestAudio.LeftChannel(TrackSources.Open(FixturePath($"noise-{rate}-24-{channels}.flac")));
        var file = File.OpenRead(mp3);
        var raw = WaveStreamTrackSource.Create(new Mp3FileReader(file), file);
        var rawFrames = raw.TotalFrames;
        var decoded = TestAudio.LeftChannel(raw);
        Mp3Header header;
        using (var stream = File.OpenRead(mp3))
        {
            header = Mp3Header.Read(stream);
        }

        var (lag, similarity) = TestAudio.Locate(decoded, source.AsSpan(0, 4096), 0, 4000);

        Assert.True(similarity > 0.95, $"the source's first samples match nowhere; best {similarity:F3} at {lag}");
        Assert.Equal(Mp3TrackReader.DecoderDelay, lag - EncoderDelay);
        // The trimming reader's arithmetic also rests on the header's whole frames being what
        // NAudio decodes (it leaves the Info frame out), and on the tag's delay being the real one.
        Assert.Equal(header.WholeFrames, rawFrames);
        Assert.Equal(variant == Mp3Variant.CbrBare ? null : EncoderDelay, header.Gapless?.Delay);
    }
}
