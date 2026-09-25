using System.Globalization;
using Faderio.Core.Playback;
using NAudio.Flac;

namespace Faderio.Core.Tests;

/// <summary>The frame table the decoder package's pre-scan builds is wrong in two ways that real
/// files meet (build issue 22). It never looks for a frame in a file's last 16 bytes, so a final
/// frame shorter than that is lost: 110 of 245 FLACs in the author's music folder end in one, a
/// block of digital silence, and each raised ADR 0010's notice at every play. And it takes any
/// bytes that parse as a frame header for a frame, as it did in 5 of those 245: every seek after
/// the phantom frame landed late by its length. <c>FlacTrackReader</c> repairs both. The files
/// are whole, but a seek near a phantom frame could decode it, so those seeks run in a child
/// process (<see cref="ChildProcess"/>).</summary>
public sealed class FlacFrameTableTests
{
    /// <summary>Pink noise, then digital silence to 20 whole 4608-sample blocks; the silent
    /// frames are 14 bytes each (<c>Fixtures/generate.sh</c>).</summary>
    private const string SilentTail = "silent-tail-44100-16-2.flac";
    private const long SilentTailFrames = 20 * 4608;

    /// <summary>White noise stored verbatim, with a valid frame header of the same format written
    /// into the audio of frame 15 (<c>Fixtures/generate.sh</c>).</summary>
    private const string FalseHeader = "false-header-44100-16-1.flac";
    private const long FalseHeaderFrames = 88200;

    // ── A final frame under 16 bytes ─────────────────────────────────────────────────────

    [Fact]
    public void TheSilentTailFixture_EndsInAFrameThePackagesPreScanNeverFinds()
    {
        var frames = PackageFrameTable(SilentTail);

        Assert.Equal(FileLength(SilentTail) - 2 * 14, frames[^1].StreamOffset);
        Assert.Equal(SilentTailFrames - 4608, frames[^1].SampleOffset + frames[^1].Header.BlockSize);
    }

    [Fact]
    public void AFileEndingInAFrameUnder16Bytes_OpensToItsHeadersLength_AndDecodesInFull()
    {
        using var source = TrackSources.Open(FixturePath(SilentTail));

        Assert.Equal(SilentTailFrames, source.TotalFrames);
        Assert.Equal(SilentTailFrames, TestAudio.ReadToEnd(source));
        Assert.Equal(SilentTailFrames, source.Position);
    }

    [Fact]
    public void ASeekIntoAFinalFrameUnder16Bytes_Lands_AndReadsToTheEnd()
    {
        using var source = TrackSources.Open(FixturePath(SilentTail));

        source.Position = SilentTailFrames - 1000;

        Assert.Equal(SilentTailFrames - 1000, source.Position);
        Assert.Equal(1000, TestAudio.ReadToEnd(source));
    }

    [Fact]
    public void AFileEndingInAFrameUnder16Bytes_PlaysWithNoNotice_AndTheNextTrackStartsWhereItEnds()
    {
        var child = ChildProcess.Run("play", FixturePath(SilentTail));

        AssertLived(child);
        Assert.Equal(SilentTailFrames, child["delivered"]);
        Assert.Equal(0, child["notices"]);
        Assert.Equal(0.2, double.Parse(child.Values["after"], CultureInfo.InvariantCulture), 2);
        Assert.StartsWith("b-", Path.GetFileName(child.Values["current"])); // the DC WAV
    }

    // ── A false frame header ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheFalseHeaderFixture_HasAFrameThePackagesPreScanInvented()
    {
        var frames = PackageFrameTable(FalseHeader);

        Assert.Equal(20 + 1, frames.Count);
        var invented = frames[16];
        Assert.Equal(9, invented.Header.FrameNumber);
        Assert.Equal(256, invented.Header.BlockSize);
        Assert.Equal(16, frames[17].Header.FrameNumber);
        Assert.Equal(16 * 4608 + 256, frames[17].SampleOffset); // what the package seeks frame 16 by
        Assert.Equal(FalseHeaderFrames + 256, frames[^1].SampleOffset + frames[^1].Header.BlockSize);
    }

    [Fact]
    public void AFileWithAFalseFrameHeader_OpensToItsHeadersLength_AndDecodesInFull()
    {
        using var source = TrackSources.Open(FixturePath(FalseHeader));

        Assert.Equal(FalseHeaderFrames, source.TotalFrames);
        Assert.Equal(FalseHeaderFrames, TestAudio.ReadToEnd(source));
        Assert.Equal(FalseHeaderFrames, source.Position);
    }

    [Theory]
    [InlineData(1000)]              // before the false header, where the package's table is right
    [InlineData(70000)]             // in the frame that holds it
    [InlineData(16 * 4608 + 10)]    // the frame after, where the package landed on the phantom
    [InlineData(80000)]             // later, where the package's table is 256 samples late
    [InlineData(88200 - 300)]       // the last frame
    public void ASeekInAFileWithAFalseFrameHeader_LandsOnTheSampleASequentialDecodeHasThere(long target)
    {
        const int window = 256;
        var expected = SequentialDecode(FalseHeader).AsSpan((int)target, window);

        var child = ChildProcess.Run("window", FixturePath(FalseHeader),
            target.ToString(CultureInfo.InvariantCulture), window.ToString(CultureInfo.InvariantCulture));

        AssertLived(child);
        Assert.Equal(target, child["landed"]);
        Assert.Equal(ChildProcess.Format(expected), child.Values["window"]);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────────────

    private static void AssertLived(ChildProcess.Result child) =>
        Assert.True(child.ExitCode == 0, $"The child exited with {child.ExitCode}:\n{child.Output}");

    private static string FixturePath(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);

    private static long FileLength(string fixture) => new FileInfo(FixturePath(fixture)).Length;

    /// <summary>Every sample of a mono fixture, read from the start without a seek, which the
    /// package's frame table plays no part in.</summary>
    private static float[] SequentialDecode(string fixture)
    {
        using var source = TrackSources.Open(FixturePath(fixture));
        var samples = new float[source.TotalFrames];
        var read = 0;
        int n;
        while (read < samples.Length && (n = source.Read(samples, read, samples.Length - read)) > 0)
        {
            read += n;
        }
        return samples;
    }

    /// <summary>The package's own pre-scan of a fixture, as it seeks by it.</summary>
    private static List<FlacFrameInformation> PackageFrameTable(string fixture)
    {
        IReadOnlyList<FlacFrameInformation>? frames = null;
        using var stream = File.OpenRead(FixturePath(fixture));
        using var reader = new FlacReader(stream, FlacPreScanMode.Sync, e => frames = e.Frames);
        return [.. frames!];
    }
}
