using Faderio.Core.Playback;
using Faderio.Core.Tests;
using MusicBeePlugin;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// MusicBee's selection added to the open playlist (phase 5 issue 14): the item on the main
/// panel's context menu asks MusicBee for the selected files and sends them to the engine's
/// Add, which probes each and reports the rejections together, as Add folder… does.
/// </summary>
public sealed partial class PluginTests
{
    private const string AddItem = "Add to faderio's open playlist";

    [Fact]
    public void TheSelection_IsAppendedToTheOpenPlaylist_InMusicBeesOrder_WithoutPlaying()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 2, 880);
        var d = TestAudio.WriteToneWav("musicbee-d.wav", 2, 220);
        _musicBee.Selection = new[] { d, c };

        _musicBee.ChooseOnTheSelection(AddItem);
        var view = Added(4);

        Assert.Equal(1, _musicBee.Calls.Count(c => c == "Library_QueryFilesEx domain=SelectedFiles"));
        Assert.DoesNotContain("Player_Stop", _musicBee.Calls);
        Assert.Equal(new[] { a, b, d, c }, view.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { a, b, d, c }, SavedOpenPlaylist(4));
        Assert.Equal(Transport.Stopped, _output.State);
        Assert.Equal(0, _output.FramesRendered);
        _ui.Pump();
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public void RejectedFiles_AreOneNotice_EachNamingWhatDisqualifiesIt()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var c = TestAudio.WriteToneWav("musicbee-c.wav", 2, 880);
        var text = Path.Combine(_dir, "notes.wav");
        File.WriteAllText(text, "not audio");
        var missing = Path.Combine(_dir, "missing.flac");
        _musicBee.Selection = new[] { c, text, missing };

        _musicBee.ChooseOnTheSelection(AddItem);
        var view = Added(3);

        Assert.Equal(new[] { a, b, c }, view.Queue.Select(r => r.Track.FilePath));
        _ui.Pump();
        var notice = Assert.Single(_ui.Messages);
        Assert.StartsWith("Added 1 track; rejected 2 — notes.wav: ", notice);
        Assert.Contains("; missing.flac: The file does not exist.", notice);
    }

    [Fact]
    public void NothingSelected_OrMusicBeeCannotSay_AddsNothing_Silently()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();

        _musicBee.Selection = Array.Empty<string>();
        _musicBee.ChooseOnTheSelection(AddItem);
        _musicBee.Selection = null;
        _musicBee.ChooseOnTheSelection(AddItem);

        Assert.Equal(2, _musicBee.Calls.Count(c => c == "Library_QueryFilesEx domain=SelectedFiles"));
        var view = _plugin.Host!.Engine.View;
        Assert.Null(view.Adding);
        Assert.Equal(new[] { a, b }, view.Queue.Select(r => r.Track.FilePath));
        _ui.Pump();
        Assert.Empty(_ui.Messages);
    }

    /// <summary>Waits for the add to end with <paramref name="rows"/> in the queue: its files are
    /// probed in the background.</summary>
    private PlayerView Added(int rows) =>
        Eventually(() => _plugin.Host!.Engine.View, view => view.Adding is null && view.Queue.Count == rows, "The add never ended.");

    /// <summary>The open playlist's file, once the add's last rows are written to it (the write
    /// follows the publication, off the lock, and the read may catch its replace).</summary>
    private IReadOnlyList<string> SavedOpenPlaylist(int rows) => Eventually(
        () =>
        {
            try
            {
                return File.ReadAllLines(Path.Combine(LibraryFolder, ".faderio", "playlists", "Untitled.m3u8"))
                    .Where(l => !l.StartsWith("#", StringComparison.Ordinal)).ToList();
            }
            catch (IOException)
            {
                return new List<string>(); // mid-replace: read again
            }
        },
        saved => saved.Count == rows, $"The playlist file never held {rows} rows.");
}
