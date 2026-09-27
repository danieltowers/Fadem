using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>What an added track carries for the queue's columns (playlists issue 06): the album
/// from its tags, beside the title, artist and ReplayGain read in the same pass, and the format the
/// add-time probe already read from its header. Display only: nothing here changes playback.</summary>
public sealed class TrackMetadataTests
{
    private static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    [Fact]
    public void ATaggedFlac_CarriesItsAlbum_BesideTitleArtistAndReplayGain()
    {
        using var tc = TestEngine.Create();
        var path = TestAudio.WriteTaggedFlac("tagged", "First Album", "-7.20 dB");

        var track = Assert.Single(tc.Add(path)).Track;

        Assert.Equal("Quiet Song", track.Title);
        Assert.Equal("Example Band", track.Artist);
        Assert.Equal("First Album", track.Album);
        Assert.Equal(-7.2, track.ReplayGainDb!.Value, 6);
    }

    [Fact]
    public void AFileWhoseTagsNameNoAlbum_HasAnEmptyAlbum()
    {
        using var tc = TestEngine.Create();
        var tagged = TestAudio.WriteTaggedFlac("tagged", album: null, replayGain: null);
        var untagged = TestAudio.WriteDcWav("untagged.wav", 1, 0.2); // no tags at all

        var rows = tc.Add(tagged, untagged);

        Assert.Equal(["", ""], rows.Select(r => r.Track.Album));
    }

    [Theory]
    [InlineData("tone-44100-16-2.flac", AudioFormat.Flac, 44100, 16, 2)]
    [InlineData("tone-96000-24-1.flac", AudioFormat.Flac, 96000, 24, 1)]
    [InlineData("tone-cbrinfo-48000-1.mp3", AudioFormat.Mp3, 48000, null, 1)]
    public void TheProbesFormat_LandsOnTheTrack(string fixture, AudioFormat format, int rate, int? depth, int channels)
    {
        using var tc = TestEngine.Create();

        var track = Assert.Single(tc.Add(FixturePath(fixture))).Track;

        Assert.Equal(new AudioShape(format, rate, depth, channels), track.Format);
    }

    [Fact]
    public void AWav_CarriesItsFormat_Too()
    {
        using var tc = TestEngine.Create();
        var path = TestAudio.WriteWavAt("shape", 1, _ => 0.2, 48000, 24, 1);

        var track = Assert.Single(tc.Add(path)).Track;

        Assert.Equal(new AudioShape(AudioFormat.Wav, 48000, 24, 1), track.Format);
    }

    [Fact]
    public void AFileWhoseTagsCannotBeRead_StillCarriesTheFormatItWasGiven()
    {
        // Tags are a convenience: TagLib refuses this file, and the track still has its name,
        // length and the probe's format, with no album.
        using var tc = TestEngine.Create();
        var path = Path.Combine(tc.TempDir, "Unreadable Tags.flac");
        File.WriteAllText(path, "not a flac file at all");
        var shape = new AudioShape(AudioFormat.Flac, 44100, 16, 2);

        var track = TrackMetadataReader.Read(path, Length.FromFrames(44100, 44100), shape);

        Assert.Equal("Unreadable Tags", track.Title);
        Assert.Equal("", track.Album);
        Assert.Equal(shape, track.Format);
    }
}
