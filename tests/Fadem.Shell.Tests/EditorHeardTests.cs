using System.Reflection;
using System.Runtime.ExceptionServices;
using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Queue;
using Fadem.Core.Tests;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.Shell.Tests;

/// <summary>Seam markers issue 03: the editor's halves are numbered as the queue numbers its rows,
/// the half heard has the speaker, and a dim line under the selector says when another track plays
/// or another comes next. These replace the traffic light (phase 6 issue 07). On each host's
/// runtime, against a real engine.</summary>
public sealed class EditorHeardTests
{
    private const int Second = 44100;

    [Fact]
    public void ThePairInTheQueue_NumbersItsHalves_AsTheQueueNumbersItsRows() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "num", 3);
        using var editor = Editor(tc);

        editor.Open(tc.AdjacencyOf(paths[1], paths[2]));
        editor.Refresh(tc.View);

        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal($"3 · {NameOf(paths[2])}", editor._incomingTitle.Text);
    });

    /// <summary>Two tracks the queue does not play one into the other: the names alone.</summary>
    [Fact]
    public void APairNotInTheQueue_NamesItsHalves_WithoutNumbers() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "nonum", 3);
        using var editor = Editor(tc);

        editor.Open(tc.AdjacencyOf(paths[0], paths[2]));
        editor.Refresh(tc.View);

        Assert.Equal(NameOf(paths[0]), editor._outgoingTitle.Text);
        Assert.Equal(NameOf(paths[2]), editor._incomingTitle.Text);
    });

    /// <summary>Repeat-one's this → this is one row: the same number twice.</summary>
    [Fact]
    public void RepeatOnesTrackIntoItself_NumbersBothHalvesTheSame() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "self", 3);
        tc.Engine.SetRepeat(RepeatMode.One);
        using var editor = Editor(tc);

        editor.Open(tc.AdjacencyOf(paths[1], paths[1]));
        editor.Refresh(tc.View);

        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal($"2 · {NameOf(paths[1])}", editor._incomingTitle.Text);
    });

    [Fact]
    public void NothingPlaying_HasNoSpeaker_AndNoLine() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "stopped", 3);
        using var editor = Editor(tc);
        StudioTheme.Apply(editor, Palette.Studio);

        editor.Open(tc.AdjacencyOf(paths[1], paths[2]));
        editor.Refresh(tc.View);

        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal($"3 · {NameOf(paths[2])}", editor._incomingTitle.Text);
        Assert.Equal(Palette.Studio.Dim, editor._outgoingTitle.ForeColor);
        Assert.Equal(Palette.Studio.Dim, editor._incomingTitle.ForeColor);
        Assert.False(IsSetVisible(editor._heardLine));
    });

    /// <summary>The light's red: the line names the track playing.</summary>
    [Fact]
    public void AnotherTrackPlaying_SaysSoUnderTheSelector_WithNoSpeaker() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "other", 3);
        using var editor = Editing(tc, paths, playing: 0);

        Assert.Equal($"Playing {NameOf(paths[0])}, not in this transition", editor._heardLine.Text);
        Assert.True(IsSetVisible(editor._heardLine));
        Assert.Equal(Palette.Studio.Dim, editor._heardLine.ForeColor);
        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal($"3 · {NameOf(paths[2])}", editor._incomingTitle.Text);
    });

    /// <summary>The light's green: the speaker on the fade-out's half, in the text colour.</summary>
    [Fact]
    public void TheOutgoingTrackPlaying_PutsTheSpeakerOnTheUpperHalf() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "out", 3);
        using var editor = Editing(tc, paths, playing: 1);

        Assert.Equal($"🔊 2 · {NameOf(paths[1])} · playing", editor._outgoingTitle.Text);
        Assert.Equal(Palette.Studio.Text, editor._outgoingTitle.ForeColor);
        Assert.Equal($"3 · {NameOf(paths[2])}", editor._incomingTitle.Text);
        Assert.Equal(Palette.Studio.Dim, editor._incomingTitle.ForeColor);
        Assert.False(IsSetVisible(editor._heardLine));
    });

    /// <summary>The light's amber: the speaker on the fade-in's half.</summary>
    [Fact]
    public void TheIncomingTrackPlaying_PutsTheSpeakerOnTheLowerHalf() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "in", 3);
        using var editor = Editing(tc, paths, playing: 2);

        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal(Palette.Studio.Dim, editor._outgoingTitle.ForeColor);
        Assert.Equal($"🔊 3 · {NameOf(paths[2])} · playing", editor._incomingTitle.Text);
        Assert.Equal(Palette.Studio.Text, editor._incomingTitle.ForeColor);
        Assert.False(IsSetVisible(editor._heardLine));
    });

    /// <summary>The light's other red (round 2 issue 08): the outgoing track plays, but into
    /// another. The line names the one that comes next.</summary>
    [Fact]
    public void TheOutgoingTrackPlaying_IntoAnother_SaysWhichPlaysNext() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = Queue(tc, "into", 4);
        using var editor = Editing(tc, paths, playing: 1);

        tc.Engine.Remove(new[] { tc.RowOf(paths[2]) });
        editor.Refresh(tc.View);

        Assert.Equal($"{NameOf(paths[3])} plays next, not {NameOf(paths[2])}", editor._heardLine.Text);
        Assert.True(IsSetVisible(editor._heardLine));
        Assert.Equal(NameOf(paths[1]), editor._outgoingTitle.Text); // no longer a pair in the queue
        Assert.Equal(NameOf(paths[2]), editor._incomingTitle.Text);
    });

    /// <summary>A queue playing the row at <paramref name="playing"/> for 2 s, and a themed
    /// editor open on the second row into the third, refreshed.</summary>
    private static TransitionEditorPanel Editing(TestEngine tc, string[] paths, int playing)
    {
        tc.Engine.PlayRow(tc.View.Queue[playing].Id);
        tc.Output.Advance(2 * Second);
        var editor = Editor(tc);
        StudioTheme.Apply(editor, Palette.Studio);
        editor.Open(tc.AdjacencyOf(paths[1], paths[2]));
        editor.Refresh(tc.View);
        return editor;
    }

    /// <summary>A queue of <paramref name="count"/> 10 s files, stopped.</summary>
    private static string[] Queue(TestEngine tc, string stem, int count)
    {
        var paths = Enumerable.Range(0, count).Select(i => TestAudio.WriteDcWav($"{stem}-{(char)('a' + i)}.wav", 10, 0.1 * (i + 1))).ToArray();
        tc.Add(paths);
        return paths;
    }

    private static TransitionEditorPanel Editor(TestEngine tc) => new(tc.Engine, _ => { }, _ => null);

    /// <summary>A track with no title tag is named by its file.</summary>
    private static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

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
