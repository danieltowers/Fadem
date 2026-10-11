using Fadem.Core.Playback;
using Fadem.Core.Queue;
using Fadem.Core.Settings;

namespace Fadem.Core.Tests;

/// <summary>
/// Sync editor: the transport's box saves whether the editor follows playback, as shuffle is
/// saved, and the view says what it follows to: the attended row's this → next, none on the last
/// row even under repeat-all (D9), and none when stopped.
/// </summary>
public sealed class EditorFollowsPlaybackTests
{
    private const int Second = 44100;

    [Fact]
    public void SetEditorFollowsPlayback_SavesTheLibrarysSettingsFile_AndTellsNothing()
    {
        using var tc = TestEngine.Create();

        tc.Engine.SetEditorFollowsPlayback(true);

        Assert.True(tc.View.Settings.EditorFollowsPlayback);
        Assert.True(new SettingsStore(tc.SettingsPath).Settings.EditorFollowsPlayback);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void ChangeSettings_KeepsTheOneInForce()
    {
        using var tc = TestEngine.Create();
        tc.Engine.SetEditorFollowsPlayback(true);

        tc.Engine.ChangeSettings(tc.View.Settings with { EditorFollowsPlayback = false, BlankDurationSeconds = 7 });

        Assert.True(tc.View.Settings.EditorFollowsPlayback);
        Assert.Equal(7, tc.View.Settings.BlankDurationSeconds);
    }

    [Fact]
    public void Stopped_FollowsToNothing()
    {
        using var tc = TestEngine.Create();
        Queue(tc);

        Assert.Null(tc.View.EditorFollow);
    }

    [Fact]
    public void Playing_FollowsToTheAttendedRowsThisToNext_AndOnAsTheNextTrackComesIn()
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        Assert.Equal(tc.AdjacencyOf(a, b), tc.View.EditorFollow);

        tc.Output.Advance(3 * Second); // past A's end: a hard cut into B

        Assert.Equal(b, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(tc.AdjacencyOf(b, c), tc.View.EditorFollow);
    }

    [Theory]
    [InlineData(RepeatMode.Off)]
    [InlineData(RepeatMode.All)]
    public void OnTheLastRow_FollowsToNothing(RepeatMode repeat)
    {
        using var tc = TestEngine.Create();
        var (_, _, c) = Queue(tc);
        tc.Engine.SetRepeat(repeat);
        tc.Engine.PlayRow(tc.RowOf(c));
        tc.Output.Advance(Second);

        Assert.Null(tc.View.EditorFollow);
    }

    /// <summary>A, B and C, each 3 s.</summary>
    private static (string A, string B, string C) Queue(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("follow-a.wav", 3, 0.1);
        var b = TestAudio.WriteDcWav("follow-b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("follow-c.wav", 3, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }
}
