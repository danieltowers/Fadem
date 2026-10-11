using System.Reflection;
using System.Runtime.ExceptionServices;
using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Tests;
using Fadem.Shell;

namespace Fadem.Shell.Tests;

/// <summary>Phase 6 issue 06: each Capture button reads the open transition's own track, is live
/// only while that track is heard and names it in its tooltip. The line under its field that said
/// what to play while it was greyed is hidden (round 2 issue 11), kept for a later return. On each
/// host's runtime, against a real engine.</summary>
public sealed class EditorCapturesOwnTracksTests
{
    private const int Second = 44100;

    [Fact]
    public void AnotherTrackPlaying_BothCapturesGreyed_NoHintShown_AndAClickDoesNothing() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, bc) = Editing(tc, playing: 0);

        Assert.False(editor._captureOut.Enabled);
        Assert.False(editor._captureIn.Enabled);
        Assert.False(IsSetVisible(editor._captureOutHint));
        Assert.False(IsSetVisible(editor._captureInHint));

        Click(editor._captureOut);

        Assert.Equal("", editor._fadeOutPoint.Text);
        Assert.Null(tc.Engine.Transition(bc).Global);
    });

    [Fact]
    public void TheOutgoingTrackPlaying_ItsCaptureIsLive_NamedInItsTooltip_AndWritesItsPlayhead() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, bc) = Editing(tc, playing: 1);

        Assert.True(editor._captureOut.Enabled);
        Assert.Equal($"Capture {bc.Outgoing.Title}'s playhead", editor._captureTips.GetToolTip(editor._captureOut));
        Assert.False(IsSetVisible(editor._captureOutHint));
        Assert.False(editor._captureIn.Enabled);
        Assert.False(IsSetVisible(editor._captureInHint));

        Click(editor._captureOut);

        Assert.Equal("2", editor._fadeOutPoint.Text);
        Assert.Equal(TimeSpan.FromSeconds(2), tc.Engine.Transition(bc).Global!.FadeOutPoint!.Position);
    });

    /// <summary>Round 2 issue 09: Render this boundary is hidden, but still built and wired, so it
    /// comes live with a boundary open as it did and can come back with one line.</summary>
    [Fact]
    public void RenderThisBoundary_IsHidden_ButStillComesLive() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, _) = Editing(tc, playing: 0);

        Assert.False(IsSetVisible(editor._render));
        Assert.True(editor._render.Enabled);
    });

    [Fact]
    public void NoAdjacencyOpen_ShowsNoHint() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var empty = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        empty.Refresh(tc.View);
        Assert.False(IsSetVisible(empty._captureOutHint));
        Assert.False(IsSetVisible(empty._captureInHint));
    });

    /// <summary>A queue of A, B and C, the row named by <paramref name="playing"/> playing for
    /// 2 s, and the editor open on B → C and refreshed.</summary>
    private static (TransitionEditorPanel Editor, Adjacency BC) Editing(TestEngine tc, int playing)
    {
        var paths = new[]
        {
            TestAudio.WriteDcWav("capture-a.wav", 10, 0.1),
            TestAudio.WriteDcWav("capture-b.wav", 10, 0.2),
            TestAudio.WriteDcWav("capture-c.wav", 10, 0.3),
        };
        tc.Add(paths);
        tc.Engine.PlayRow(tc.View.Queue[playing].Id);
        tc.Output.Advance(2 * Second);
        var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        var bc = tc.AdjacencyOf(paths[1], paths[2]);
        editor.Open(bc);
        editor.Refresh(tc.View);
        return (editor, bc);
    }

    private static void Click(Button button) =>
        typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });

    /// <summary>Whether the control itself is set visible. <see cref="Control.Visible"/> also
    /// asks every parent, and a panel that is never shown has none visible. net48's GetState takes
    /// the flag as an int, net8.0's as an enum.</summary>
    private static bool IsSetVisible(Control control)
    {
        var getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var type = getState.GetParameters()[0].ParameterType;
        var visible = type.IsEnum ? Enum.ToObject(type, 0x2) : 0x2; // States.Visible
        return (bool)getState.Invoke(control, new[] { visible })!;
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
