using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Settings;

namespace Fadem.Core.Tests;

/// <summary>
/// The ReplayGain modes, as MusicBee offers them: Track plays each track at its track gain, Album
/// at its album gain, and Smart at its album gain only where the queue plays it beside another of
/// its album with shuffle off. A track without an album gain plays at its track gain.
/// </summary>
public sealed class ReplayGainModeTests
{
    private const int Rate = 44100;
    private const double TrackDb = -6.0;
    private const double AlbumDb = -2.0;

    private static int At(double seconds) => (int)(seconds * Rate);

    private static double Level(double db) => 0.4 * ReplayGain.DbToMultiplier(db);

    /// <summary>The level the first track plays at, queued before <paramref name="next"/>'s album,
    /// under <paramref name="mode"/>.</summary>
    private static double FirstTrackLevel(
        ReplayGainMode mode, string nextAlbum, double? albumDb = AlbumDb, bool shuffle = false)
    {
        using var tc = TestEngine.Create(new PlayerSettings { ReplayGainMode = mode, Shuffle = shuffle }, rngSeed: 1);
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track>
        {
            tc.TrackOf(a, TrackDb, albumDb, album: "First Album"),
            tc.TrackOf(b, TrackDb, albumDb, album: nextAlbum),
        });
        tc.Engine.PlayRow(tc.View.Queue[0].Id);
        tc.Output.Advance(At(1));
        return tc.Rendered.DcLevel(At(1));
    }

    [Fact]
    public void Track_PlaysTheTrackGain() =>
        Assert.Equal(Level(TrackDb), FirstTrackLevel(ReplayGainMode.Track, "First Album"), 3);

    [Fact]
    public void Album_PlaysTheAlbumGain() =>
        Assert.Equal(Level(AlbumDb), FirstTrackLevel(ReplayGainMode.Album, "Second Album"), 3);

    [Fact]
    public void Album_WithNoAlbumGain_PlaysTheTrackGain() =>
        Assert.Equal(Level(TrackDb), FirstTrackLevel(ReplayGainMode.Album, "First Album", albumDb: null), 3);

    [Fact]
    public void Off_PlaysAtUnity() =>
        Assert.Equal(0.4, FirstTrackLevel(ReplayGainMode.Off, "First Album"), 3);

    [Fact]
    public void Smart_BesideATrackOfItsAlbum_PlaysTheAlbumGain() =>
        Assert.Equal(Level(AlbumDb), FirstTrackLevel(ReplayGainMode.Smart, "first album"), 3);

    [Fact]
    public void Smart_BesideAnotherAlbum_PlaysTheTrackGain() =>
        Assert.Equal(Level(TrackDb), FirstTrackLevel(ReplayGainMode.Smart, "Second Album"), 3);

    [Fact]
    public void Smart_UnderShuffle_PlaysTheTrackGain() =>
        Assert.Equal(Level(TrackDb), FirstTrackLevel(ReplayGainMode.Smart, "First Album", shuffle: true), 3);

    [Fact]
    public void ChangingTheMode_MidTrack_TakesThePlayingTrackToItsNewGain()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("a.wav", 3, 0.4);
        tc.Engine.AddTracks(new List<Track> { tc.TrackOf(a, TrackDb, AlbumDb, album: "First Album") });
        tc.Engine.Play();
        tc.Output.Advance(At(1));
        Assert.Equal(Level(TrackDb), tc.Rendered.DcLevel(At(1)), 3);

        tc.Engine.ChangeSettings(tc.View.Settings with { ReplayGainMode = ReplayGainMode.Album });
        tc.Output.Advance(At(0.05));

        Assert.Equal(Level(AlbumDb), tc.Rendered.DcLevel(At(1.02)), 3);
    }
}
