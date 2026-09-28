using System.Buffers.Binary;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Faderio.Core.Tests;

/// <summary>
/// Add-time budget: how long <c>Add</c> takes to probe a file and read its tags. Measured
/// over the user's 903-file library in the restage map's "Does adding a folder need a background
/// probe?", which holds the library numbers; these budgets catch an order-of-magnitude regression
/// (a whole-file scan creeping into the probe), not a few milliseconds. The heaviest real case is
/// FLAC with megabytes of embedded cover art: the probe reads STREAMINFO only, but the tag reader
/// reads the pictures too.
///
/// Each test times its add three times and holds the fastest to the budget: a regression slows
/// every run, while the load of the rest of the suite (and the App assembly's process beside it)
/// slows only some. The collection keeps every other Core test class from running alongside.
/// </summary>
[Collection(Name)]
public sealed class AddTimeBudgetTests(ITestOutputHelper output)
{
    public const string Name = "Add-time budget";

    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class RunsAlone;

    private const int Runs = 3;

    /// <summary>Times <paramref name="add"/> <see cref="Runs"/> times: the fastest, and every time
    /// for the failure message. The times are also written to the test output, which
    /// <c>--logger "console;verbosity=detailed"</c> shows for a passing run.</summary>
    private (long Fastest, string All) FastestOf(Action add)
    {
        var times = new long[Runs];
        for (var i = 0; i < Runs; i++)
        {
            var sw = Stopwatch.StartNew();
            add();
            sw.Stop();
            times[i] = sw.ElapsedMilliseconds;
        }
        var all = string.Join(", ", times.Select(t => $"{t} ms"));
        output.WriteLine($"fastest {times.Min()} ms of {all}");
        return (times.Min(), all);
    }

    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void AddingEveryFixture_StaysWithinOneSecond()
    {
        var files = Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures"))
            .Where(f => f.EndsWith(".flac") || f.EndsWith(".mp3")).ToList();
        using var tc = TestEngine.Create();
        tc.Engine.Add([files[0]]).Wait(); // JIT and TagLib warm-up, outside the budget

        var (fastest, all) = FastestOf(() => Assert.Empty(tc.Engine.Add(files).Result.Rejected));

        Assert.True(fastest < 1000,
            $"adding {files.Count} fixtures took at best {fastest} ms ({all}); the budget is 1000 ms");
    }

    [Fact]
    public void AFlacWithEightMegabytesOfCoverArt_AddsWithin150Milliseconds_AndKeepsItsLength()
    {
        var plain = FixturePath("tone-44100-16-2.flac");
        var heavy = Path.Combine(TestTemp.Root, $"cover-{Guid.NewGuid():N}.flac");
        File.WriteAllBytes(heavy, WithPicture(File.ReadAllBytes(plain), 8 * 1024 * 1024));
        using var tc = TestEngine.Create();
        var expected = tc.Engine.Add([plain]).Result.Added.Single().Track.Length; // also the warm-up

        var (fastest, all) = FastestOf(() =>
            Assert.Equal(expected, tc.Engine.Add([heavy]).Result.Added.Single().Track.Length));

        Assert.True(fastest < 150,
            $"adding a FLAC with 8 MB of cover art took at best {fastest} ms ({all}); the budget is 150 ms");
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
