using System.Runtime.ExceptionServices;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>Sync editor: while it is on, each track the listener comes to opens the editor on its
/// transition into the next, once, so the user can open another in between; the last track leaves
/// the editor where it was. On each host's runtime, against a real engine.</summary>
public sealed class EditorFollowsPlaybackTests
{
    private const int Second = 44100;

    [Fact]
    public void Off_TheEditorStaysWhereItIs() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        Queue(tc);
        using var editor = Editor(tc);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        editor.Refresh(tc.View);

        Assert.Null(editor.Adjacency);
    });

    [Fact]
    public void On_TheEditorOpensOnThePlayingTracksTransition_AndFollowsTheNextTrackIn() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (a, b, c) = Queue(tc);
        using var editor = Editor(tc);
        tc.Engine.SetEditorFollowsPlayback(true);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        editor.Refresh(tc.View);
        Assert.Equal(tc.AdjacencyOf(a, b), editor.Adjacency);

        tc.Output.Advance(3 * Second); // into B
        editor.Refresh(tc.View);
        Assert.Equal(tc.AdjacencyOf(b, c), editor.Adjacency);
    });

    [Fact]
    public void BetweenTracks_TheUserCanOpenAnother() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        using var editor = Editor(tc);
        tc.Engine.SetEditorFollowsPlayback(true);
        tc.Engine.Play();
        tc.Output.Advance(Second);
        editor.Refresh(tc.View);

        editor.Open(tc.AdjacencyOf(b, c));
        tc.Output.Advance(Second / 10);
        editor.Refresh(tc.View);

        Assert.Equal(tc.AdjacencyOf(b, c), editor.Adjacency);
    });

    [Fact]
    public void TheLastTrack_LeavesTheEditorWhereItWas() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (_, b, c) = Queue(tc);
        using var editor = Editor(tc);
        tc.Engine.SetEditorFollowsPlayback(true);
        tc.Engine.PlayRow(tc.RowOf(b));
        tc.Output.Advance(Second);
        editor.Refresh(tc.View);

        tc.Output.Advance(3 * Second); // into C, the last
        editor.Refresh(tc.View);

        Assert.Equal(c, tc.View.Attended!.Row.Track.FilePath);
        Assert.Equal(tc.AdjacencyOf(b, c), editor.Adjacency);
    });

    [Fact]
    public void AHostThatSaysNot_KeepsTheEditorShut() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        Queue(tc);
        using var editor = Editor(tc);
        editor.CanFollow = () => false;
        tc.Engine.SetEditorFollowsPlayback(true);
        tc.Engine.Play();
        tc.Output.Advance(Second);

        editor.Refresh(tc.View);

        Assert.Null(editor.Adjacency);
    });

    private static TransitionEditorPanel Editor(TestEngine tc) => new(tc.Engine, _ => { }, _ => null);

    /// <summary>A, B and C, each 3 s, queued and stopped.</summary>
    private static (string A, string B, string C) Queue(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("sync-a.wav", 3, 0.1);
        var b = TestAudio.WriteDcWav("sync-b.wav", 3, 0.2);
        var c = TestAudio.WriteDcWav("sync-c.wav", 3, 0.3);
        tc.Add(a, b, c);
        return (a, b, c);
    }

    /// <summary>WinForms wants an STA thread; xunit does not give one.</summary>
    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
