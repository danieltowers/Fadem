using System.Text;
using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Playlists (build issue 03; restage ticket 12): <c>Add</c> opens <c>.m3u</c> and <c>.m3u8</c>
/// and appends their entries, and <c>SaveAsPlaylist</c> writes the visible order as UTF-8 with
/// absolute paths. Library folder names carry <c>†</c> and <c>⭐️</c>, so every path here does too.
/// The files are real WAVs, probed on the way in.
/// </summary>
public sealed class PlaylistTests
{
    /// <summary>A one-second WAV copied to <paramref name="relative"/> under <paramref name="root"/>.</summary>
    private static string Wav(string root, string relative)
    {
        var path = Path.Combine(root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(TestAudio.WriteDcWav(Guid.NewGuid().ToString("N") + ".wav", 1, 0.3), path);
        return path;
    }

    private static string[] QueuedPaths(TestEngine tc) => tc.View.Queue.Select(r => r.Track.FilePath).ToArray();

    [Fact]
    public void SaveThenOpen_RoundTripsTheQueue_IncludingDaggerAndStarPaths()
    {
        using var tc = TestEngine.Create();
        var dagger = Wav(tc.TempDir, @"Example Band † Live\01 - Long Song.wav");
        var star = Wav(tc.TempDir, @"⭐️ Favourites\02 - Starry Song ⭐️.wav");
        var plain = Wav(tc.TempDir, @"plain\03 - Plain Song.wav");
        tc.Add(star, dagger, plain);
        var playlist = Path.Combine(tc.TempDir, "Saved † ⭐️.m3u8");

        tc.Engine.SaveAsPlaylist(playlist);
        var report = tc.Engine.Add(new[] { playlist }).Result;

        Assert.Equal(new[] { star, dagger, plain, star, dagger, plain }, QueuedPaths(tc));
        Assert.Equal(3, report.Added.Count);
        Assert.Empty(report.Rejected);
    }

    [Fact]
    public void RelativeEntries_ResolveAgainstThePlaylistsFolder_NotTheWorkingDirectory()
    {
        using var tc = TestEngine.Create();
        var lists = Path.Combine(tc.TempDir, "Lists † here");
        var inside = Wav(lists, @"Side A\1 - Side Long.wav");
        var beside = Wav(tc.TempDir, @"⭐️ Singles\2 - Single.wav");
        var slashed = Wav(lists, @"Side B\3 - Third Song.wav");
        var loose = Wav(tc.TempDir, "loose.wav");
        var playlist = Path.Combine(lists, "mix.m3u8");
        File.WriteAllLines(playlist, new[]
        {
            "#EXTM3U",
            @"Side A\1 - Side Long.wav",
            "",
            @"..\⭐️ Singles\2 - Single.wav",
            "Side B/3 - Third Song.wav",
        }, new UTF8Encoding(false));
        Assert.NotEqual(Path.GetFullPath(lists), Path.GetFullPath(Environment.CurrentDirectory));

        tc.Engine.Add(new[] { loose, playlist, loose }).Wait();

        Assert.Equal(new[] { loose, inside, beside, slashed, loose }, QueuedPaths(tc));
    }

    [Fact]
    public void AMissingEntry_IsARejection_AndTheOthersLoad()
    {
        using var tc = TestEngine.Create();
        var first = Wav(tc.TempDir, "first.wav");
        var last = Wav(tc.TempDir, "last.wav");
        var playlist = Path.Combine(tc.TempDir, "gaps.m3u8");
        File.WriteAllLines(playlist, new[] { first, "gone † away.wav", last }, new UTF8Encoding(false));

        var report = tc.Engine.Add(new[] { playlist }).Result;

        Assert.Equal(new[] { first, last }, QueuedPaths(tc));
        var rejected = Assert.Single(report.Rejected);
        Assert.Equal(Path.Combine(tc.TempDir, "gone † away.wav"), rejected.Path);
        Assert.Equal("The file does not exist.", rejected.Detail);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains("gone † away.wav: The file does not exist.", notice);
    }

    [Fact]
    public void AnEntryThatIsNotAPath_IsARejection_AndTheOthersLoad()
    {
        using var tc = TestEngine.Create();
        var first = Wav(tc.TempDir, "first.wav");
        var last = Wav(tc.TempDir, "last.wav");
        var playlist = Path.Combine(tc.TempDir, "damaged.m3u8");
        File.WriteAllLines(playlist, new[] { first, "bro\0ken.wav", last });

        var report = tc.Engine.Add(new[] { playlist }).Result;

        Assert.Equal(new[] { first, last }, QueuedPaths(tc));
        Assert.Equal("bro\0ken.wav", Assert.Single(report.Rejected).Path);
    }

    [Fact]
    public void UnderShuffle_SaveWritesTheVisibleOrder()
    {
        using var tc = TestEngine.Create(rngSeed: 7);
        var original = Enumerable.Range(1, 8).Select(i => Path.Combine(tc.TempDir, $"{i}.wav")).ToArray();
        tc.Engine.AddTracks(original.Select(p => tc.TrackOfLength(p, 60)));
        tc.Engine.SetShuffle(true);
        var visible = QueuedPaths(tc);
        Assert.NotEqual(original, visible); // the shuffle moved something
        var playlist = Path.Combine(tc.TempDir, "shuffled.m3u8");

        tc.Engine.SaveAsPlaylist(playlist);

        Assert.Equal(visible, File.ReadAllLines(playlist).Where(l => !l.StartsWith('#')));
    }

    [Fact]
    public void Save_WritesUtf8WithoutAMark_AbsolutePaths_AndExtinfInWholeSeconds()
    {
        using var tc = TestEngine.Create();
        var one = Path.Combine(tc.TempDir, "⭐️ Stars", "one.wav");
        var two = Path.Combine(tc.TempDir, "† Daggers", "two.wav");
        var untitled = Path.Combine(tc.TempDir, "untitled.wav");
        tc.Engine.AddTracks(new[]
        {
            tc.TrackOfLength(one, 100.4) with { Title = "One", Artist = "Artist A" },
            tc.TrackOfLength(two, 12.6) with { Title = "Two" },
            tc.TrackOfLength(untitled, 3) with { Title = "" },
        });
        var playlist = Path.Combine(tc.TempDir, "queue.m3u8");

        tc.Engine.SaveAsPlaylist(playlist);

        var bytes = File.ReadAllBytes(playlist);
        Assert.False(bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }), "no byte-order mark");
        var text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        Assert.Equal(new[]
        {
            "#EXTM3U",
            "#EXTINF:100,Artist A - One", one,
            "#EXTINF:13,Two", two,
            "#EXTINF:3,untitled", untitled,
        }, text.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal("Saved 3 tracks to queue.m3u8.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void AnM3uThatIsNotUtf8_IsReadInTheSystemCodePage_AndOneThatIs_AsUtf8()
    {
        using var tc = TestEngine.Create();
        var system = CodePages.SystemAnsi();
        var cafe = Wav(tc.TempDir, @"Café Example\Song.wav");
        var dagger = Wav(tc.TempDir, @"Example Band † Live\Long Song.wav");
        var ansi = Path.Combine(tc.TempDir, "ansi.m3u");
        var ansiBytes = system.GetBytes(cafe + "\r\n");
        File.WriteAllBytes(ansi, ansiBytes);
        Assert.ThrowsAny<DecoderFallbackException>(() => new UTF8Encoding(false, true).GetString(ansiBytes));
        var utf8 = Path.Combine(tc.TempDir, "utf8.m3u");
        File.WriteAllText(utf8, dagger + "\r\n", new UTF8Encoding(false));

        var report = tc.Engine.Add(new[] { ansi, utf8 }).Result;

        Assert.Empty(report.Rejected);
        Assert.Equal(new[] { cafe, dagger }, QueuedPaths(tc));
    }

    [Fact]
    public void APlaylistNamingAPlaylist_OrItself_IsNotFollowed()
    {
        using var tc = TestEngine.Create();
        var track = Wav(tc.TempDir, "track.wav");
        var inner = Path.Combine(tc.TempDir, "inner.m3u8");
        File.WriteAllLines(inner, new[] { track });
        var outer = Path.Combine(tc.TempDir, "outer.m3u8");
        File.WriteAllLines(outer, new[] { "outer.m3u8", "inner.m3u8", "track.wav" });

        var report = tc.Engine.Add(new[] { outer }).Result;

        Assert.Equal(new[] { track }, QueuedPaths(tc));
        Assert.Equal(new[] { outer, inner }, report.Rejected.Select(r => r.Path));
        Assert.All(report.Rejected, r => Assert.Equal(RejectedProperty.Format, r.Property));
    }

    [Fact]
    public void ASaveThatFails_IsANotice_AndLeavesNoTemporaryFile()
    {
        using var tc = TestEngine.Create();
        tc.Engine.AddTracks(new[] { tc.TrackOfLength(Path.Combine(tc.TempDir, "a.wav"), 5) });
        var folder = Path.Combine(tc.TempDir, "a folder, not a file");
        Directory.CreateDirectory(folder);

        tc.Engine.SaveAsPlaylist(folder);

        Assert.StartsWith("Could not save the playlist: ", Assert.Single(tc.Notices));
        Assert.Empty(Directory.GetFiles(tc.TempDir, "*.tmp"));
    }
}
