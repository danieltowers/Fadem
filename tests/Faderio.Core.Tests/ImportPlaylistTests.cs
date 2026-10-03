using System.Text;
using Faderio.Core.Playback;
using Faderio.Core.Persistence;

namespace Faderio.Core.Tests;

/// <summary>
/// Playlists issue 03 (D19): Import playlist… copies each chosen M3U into the library's playlists
/// folder as <c>&lt;file name&gt;.m3u8</c>, numbered if the name is taken, its entries made
/// absolute, and opens the last one copied. The source is never touched.
/// </summary>
public sealed class ImportPlaylistTests
{
    private static string Tone(string name) => TestAudio.WriteToneWav($"pl03-{name}.wav", 2, 1000);

    private static string PlaylistPath(TestEngine tc, string name) =>
        Path.Combine(tc.Library.PlaylistsFolder, name + ".m3u8");

    private static string[] Lines(string path) => File.ReadAllLines(path, Encoding.UTF8);

    /// <summary>A playlist outside the library, as another player left it.</summary>
    private static string Outside(TestEngine tc, string fileName, params string[] lines)
    {
        var folder = Path.Combine(tc.TempDir, "elsewhere");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        File.WriteAllLines(path, lines, new UTF8Encoding(false));
        return path;
    }

    private static string[] Queued(TestEngine tc) => tc.View.Queue.Select(r => r.Track.FilePath).ToArray();

    [Fact]
    public void ARelativePathM3u_IsCopiedWithAbsolutePaths_AndOpened()
    {
        using var tc = TestEngine.Create();
        var folder = Path.Combine(tc.TempDir, "elsewhere");
        Directory.CreateDirectory(Path.Combine(folder, "songs"));
        var a = Path.Combine(folder, "songs", "a.wav");
        var b = Path.Combine(folder, "b.wav");
        File.Copy(Tone("rel-a"), a);
        File.Copy(Tone("rel-b"), b);
        var source = Outside(tc, "Evening.m3u", "#EXTM3U", "#EXTINF:2,A", @"songs\a.wav", "b.wav");

        tc.Engine.ImportPlaylists([source]).Wait();

        Assert.Equal(new[] { "#EXTM3U", a, b }, Lines(PlaylistPath(tc, "Evening")));
        Assert.Equal("Evening", tc.View.OpenPlaylist);
        Assert.Equal(new[] { a, b }, Queued(tc));
        Assert.Equal(new[] { "#EXTM3U", "#EXTINF:2,A", @"songs\a.wav", "b.wav" }, Lines(source));
        Assert.Equal("Imported Evening.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void ANameThatIsTaken_IsNumbered_AndSaysSo()
    {
        using var tc = TestEngine.Create();
        var a = Tone("clash-a");
        tc.Add(a);
        tc.Engine.RenamePlaylist("Untitled", "Evening");
        var b = Tone("clash-b");
        var source = Outside(tc, "evening.m3u8", b);

        tc.Engine.ImportPlaylists([source]).Wait();

        Assert.Equal(new[] { "Evening", "evening 2" }, tc.View.Playlists);
        Assert.Equal("evening 2", tc.View.OpenPlaylist);
        Assert.Equal(new[] { b }, Queued(tc));
        Assert.Equal(new[] { a }, M3UPlaylist.Read(PlaylistPath(tc, "Evening"))); // the one there is kept
        Assert.Equal("Imported evening.m3u8 as evening 2.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void SeveralFiles_AreAllCopied_AndTheLastOneOpens()
    {
        using var tc = TestEngine.Create();
        var (a, b) = (Tone("many-a"), Tone("many-b"));
        var first = Outside(tc, "Morning.m3u", a);
        var second = Outside(tc, "Night.m3u8", b);

        tc.Engine.ImportPlaylists([first, second]).Wait();

        Assert.Equal(new[] { "Morning", "Night" }, tc.View.Playlists); // the empty Untitled left is gone
        Assert.Equal("Night", tc.View.OpenPlaylist);
        Assert.Equal(new[] { b }, Queued(tc));
        Assert.Equal(new[] { a }, M3UPlaylist.Read(PlaylistPath(tc, "Morning")));
        Assert.Equal("Imported Morning, Night.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void AMissingFile_IsANotice_AndTheOthersAreStillImported()
    {
        using var tc = TestEngine.Create();
        var a = Tone("gone-a");
        tc.Add(a);
        var gone = Path.Combine(tc.TempDir, "elsewhere", "Gone.m3u");
        var here = Outside(tc, "Here.m3u", Tone("gone-b"));

        tc.Engine.ImportPlaylists([here, gone]).Wait();

        Assert.Equal("Here", tc.View.OpenPlaylist); // the last one that could be imported
        Assert.Equal(new[] { "Here", "Untitled" }, tc.View.Playlists);
        Assert.Equal(
            new[] { "Not imported: Gone.m3u. The file does not exist.", "Imported Here." },
            tc.Notices);
    }

    [Fact]
    public void OnlyMissingFiles_IsANotice_AndNothingChanges()
    {
        using var tc = TestEngine.Create();
        var a = Tone("none-a");
        tc.Add(a);

        tc.Engine.ImportPlaylists([Path.Combine(tc.TempDir, "Gone.m3u")]).Wait();

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Equal(new[] { "Untitled" }, tc.View.Playlists);
        Assert.Equal(new[] { a }, Queued(tc));
        Assert.Equal("Not imported: Gone.m3u. The file does not exist.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void AFileThatIsNotAPlaylist_IsRefused()
    {
        using var tc = TestEngine.Create();
        var text = Outside(tc, "notes.txt", "hello");

        tc.Engine.ImportPlaylists([text]).Wait();

        Assert.Equal(new[] { "Untitled" }, tc.View.Playlists);
        Assert.Equal("Not imported: notes.txt. Only M3U and M3U8 playlists can be imported.", Assert.Single(tc.Notices));
    }

    [Fact]
    public void ACopyThatCannotBeWritten_IsANotice_AndNothingChanges()
    {
        using var tc = TestEngine.Create();
        var a = Tone("nowrite-a");
        tc.Add(a);
        Directory.CreateDirectory(PlaylistPath(tc, "Blocked")); // a folder where the file goes, unlisted
        var source = Outside(tc, "Blocked.m3u", Tone("nowrite-b"));

        tc.Engine.ImportPlaylists([source]).Wait();

        Assert.Equal("Untitled", tc.View.OpenPlaylist);
        Assert.Equal(new[] { a }, Queued(tc));
        Assert.StartsWith("Not imported: Blocked.m3u. Its copy could not be written:", Assert.Single(tc.Notices));
    }

    [Fact]
    public void AMissingEntry_IsCopied_AndRejectedAsOpeningRejectsIt()
    {
        using var tc = TestEngine.Create();
        var a = Tone("entry-a");
        var gone = Path.Combine(tc.TempDir, "gone.wav");
        var source = Outside(tc, "Gaps.m3u8", a, gone);

        var report = tc.Engine.ImportPlaylists([source]).Result;

        Assert.Equal(new[] { a, gone }, M3UPlaylist.Read(PlaylistPath(tc, "Gaps")));
        Assert.Equal(new[] { a }, Queued(tc));
        Assert.Equal(gone, Assert.Single(report.Rejected).Path);
    }

    [Fact]
    public void WithNoLibrary_ImportIsRefused()
    {
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(new FakeAudioOutput(), (Library?)null, sink) { StartAddWorker = work => work(), StartPrepareWorker = work => work() };

        engine.ImportPlaylists([Tone("nolib-a")]).Wait();

        Assert.Null(engine.View.OpenPlaylist);
        Assert.Equal("Not imported. No library is open.", Assert.Single(sink.Notices));
    }
}
