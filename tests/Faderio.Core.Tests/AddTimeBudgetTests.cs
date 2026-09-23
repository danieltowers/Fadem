using System.Buffers.Binary;
using System.Diagnostics;

namespace Faderio.Core.Tests;

/// <summary>
/// Add-time budget: how long <c>AddTracks</c> takes to probe a file and read its tags. Measured
/// over the user's 903-file library in the restage map's "Does adding a folder need a background
/// probe?", which holds the library numbers; these budgets catch an order-of-magnitude regression
/// (a whole-file scan creeping into the probe), not a few milliseconds. The heaviest real case is
/// FLAC with megabytes of embedded cover art: the probe reads STREAMINFO only, but the tag reader
/// reads the pictures too.
/// </summary>
public sealed class AddTimeBudgetTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void AddingEveryFixture_StaysWithinOneSecond()
    {
        var files = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"))
            .Where(f => f.EndsWith(".flac") || f.EndsWith(".mp3")).ToList();
        using var tc = TestEngine.Create();
        tc.Engine.AddTracks([files[0]]); // JIT and TagLib warm-up, outside the budget

        var sw = Stopwatch.StartNew();
        var report = tc.Engine.AddTracks(files);
        sw.Stop();

        Assert.Empty(report.Rejected);
        Assert.True(sw.ElapsedMilliseconds < 1000,
            $"adding {files.Count} fixtures took {sw.ElapsedMilliseconds} ms; the budget is 1000 ms");
    }

    [Fact]
    public void AFlacWithEightMegabytesOfCoverArt_AddsWithin150Milliseconds_AndKeepsItsLength()
    {
        var plain = FixturePath("tone-44100-16-2.flac");
        var heavy = Path.Combine(Path.GetTempPath(), $"faderio-cover-{Guid.NewGuid():N}.flac");
        File.WriteAllBytes(heavy, WithPicture(File.ReadAllBytes(plain), 8 * 1024 * 1024));
        using var tc = TestEngine.Create();
        var expected = tc.Engine.AddTracks([plain]).Added.Single().Track.Length; // also the warm-up

        var sw = Stopwatch.StartNew();
        var report = tc.Engine.AddTracks([heavy]);
        sw.Stop();

        Assert.Equal(expected, report.Added.Single().Track.Length);
        Assert.True(sw.ElapsedMilliseconds < 150,
            $"adding a FLAC with 8 MB of cover art took {sw.ElapsedMilliseconds} ms; the budget is 150 ms");
    }

    /// <summary>Inserts a PICTURE metadata block of <paramref name="imageBytes"/> straight after
    /// STREAMINFO, as a tagger embedding cover art would.</summary>
    private static byte[] WithPicture(byte[] flac, int imageBytes)
    {
        const int streamInfoEnd = 4 + 4 + 34; // "fLaC", block header, STREAMINFO body
        var wasLast = (flac[4] & 0x80) != 0;
        var mime = "image/jpeg"u8.ToArray();
        var body = new byte[4 + 4 + mime.Length + 4 + 16 + 4 + imageBytes];
        var at = 0;
        void U32(int v) { BinaryPrimitives.WriteInt32BigEndian(body.AsSpan(at), v); at += 4; }
        U32(3); // front cover
        U32(mime.Length); mime.CopyTo(body, at); at += mime.Length;
        U32(0); // empty description
        U32(1000); U32(1000); U32(24); U32(0); // width, height, depth, colours
        U32(imageBytes); // the image bytes themselves are left as zeros

        var header = new byte[4];
        header[0] = (byte)((wasLast ? 0x80 : 0) | 6);
        header[1] = (byte)(body.Length >> 16); header[2] = (byte)(body.Length >> 8); header[3] = (byte)body.Length;

        var result = new byte[flac.Length + header.Length + body.Length];
        flac.AsSpan(0, streamInfoEnd).CopyTo(result);
        result[4] &= 0x7F; // STREAMINFO is no longer the last metadata block
        header.CopyTo(result, streamInfoEnd);
        body.CopyTo(result, streamInfoEnd + header.Length);
        flac.AsSpan(streamInfoEnd).CopyTo(result.AsSpan(streamInfoEnd + header.Length + body.Length));
        return result;
    }
}
