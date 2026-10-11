using System.Drawing;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Tests;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.Shell.Tests;

/// <summary>Phase 6 issue 07: the light at the editor's top right is green while the open
/// transition's first track plays, amber while its second does, red while another does. On each
/// host's runtime, against a real engine.</summary>
public sealed class EditorTrafficLightTests
{
    private const int Second = 44100;

    [Fact]
    public void AnotherTrackPlaying_IsRed() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, _) = Editing(tc, playing: 0);

        Assert.True(IsSetVisible(editor._light));
        Assert.Equal(AdjacencyHeard.OtherTrack, editor._light.Heard);
        Assert.Equal("Playing a track that is not in this transition", editor._lightTip.GetToolTip(editor._light));
    });

    [Fact]
    public void TheFirstTrackPlaying_IsGreen_AndItsTooltipNamesIt() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, bc) = Editing(tc, playing: 1);

        Assert.Equal(AdjacencyHeard.Outgoing, editor._light.Heard);
        Assert.Equal($"Playing {bc.Outgoing.Title}, this transition's first track", editor._lightTip.GetToolTip(editor._light));
        Assert.Equal(editor._lightTip.GetToolTip(editor._light), editor._light.AccessibleName);
    });

    [Fact]
    public void TheSecondTrackPlaying_IsAmber() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, bc) = Editing(tc, playing: 2);

        Assert.Equal(AdjacencyHeard.Incoming, editor._light.Heard);
        Assert.Equal($"Playing {bc.Incoming.Title}, this transition's second track", editor._lightTip.GetToolTip(editor._light));
    });

    /// <summary>Round 2 issue 08: the first track playing while its next is another track is red,
    /// as another track is, since this transition is not the one about to be heard.</summary>
    [Fact]
    public void TheFirstTrackPlaying_IntoAnotherTrack_IsRed_AndItsTooltipSaysSo() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, bc) = Editing(tc, playing: 1);

        tc.Engine.Remove(new[] { tc.RowOf(bc.Incoming.FilePath) });
        editor.Refresh(tc.View);

        Assert.Equal(AdjacencyHeard.OutgoingIntoAnother, editor._light.Heard);
        Assert.Equal(TrafficLight.Colour(AdjacencyHeard.OtherTrack, Palette.Studio), editor._light.Lit);
        Assert.Equal($"Playing {bc.Outgoing.Title}, this transition's first track, but next comes another track",
            editor._lightTip.GetToolTip(editor._light));
    });

    [Fact]
    public void TheColoursAreDistinct_AndStoppedIsGrey()
    {
        var colours = new[] { AdjacencyHeard.Nothing, AdjacencyHeard.OtherTrack, AdjacencyHeard.Incoming, AdjacencyHeard.Outgoing }
            .Select(h => TrafficLight.Colour(h, Palette.Studio)).ToList();

        Assert.Equal(4, colours.Distinct().Count());
        Assert.Equal(Palette.Studio.Dim, colours[0]); // off, in the palette (studio theme issue 05)
    }

    [Fact]
    public void NoAdjacencyOpen_HidesTheLight() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        editor.Refresh(tc.View);

        Assert.False(IsSetVisible(editor._light));
    });

    /// <summary>A queue of A, B and C, the row named by <paramref name="playing"/> playing for
    /// 2 s, and the editor open on B → C and refreshed.</summary>
    private static (TransitionEditorPanel Editor, Adjacency BC) Editing(TestEngine tc, int playing)
    {
        var paths = new[]
        {
            TestAudio.WriteDcWav("light-a.wav", 10, 0.1),
            TestAudio.WriteDcWav("light-b.wav", 10, 0.2),
            TestAudio.WriteDcWav("light-c.wav", 10, 0.3),
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
