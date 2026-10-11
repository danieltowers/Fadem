using System.Runtime.ExceptionServices;
using Fadem.Core.Domain;
using Fadem.Core.Settings;
using Fadem.Core.Tests;
using Fadem.Shell;

namespace Fadem.Shell.Tests;

/// <summary>Round 2 issue 14: the editor's curve list shows the library's default curve for a
/// global transition with no curve of its own, so a new transition is saved with it; one saved
/// with a curve shows its own. A change of the default shows at once, and writes nothing. On each
/// host's runtime, against a real engine.</summary>
public sealed class EditorDefaultCurveTests
{
    [Fact]
    public void ANewTransition_ShowsTheDefaultCurve() => RunSta(() =>
    {
        using var tc = TestEngine.Create(new PlayerSettings { DefaultCurve = FadeCurveKind.SCurve });
        var ab = Pair(tc);
        using var editor = Editor(tc);

        editor.Open(ab);

        Assert.Equal("S-curve", editor._curve.Text);
    });

    [Fact]
    public void ATransitionSavedWithACurve_ShowsItsOwn() => RunSta(() =>
    {
        using var tc = TestEngine.Create(new PlayerSettings { DefaultCurve = FadeCurveKind.SCurve });
        var ab = Pair(tc);
        tc.Engine.Author(ab, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(1), Curve = FadeCurveKind.Linear });
        using var editor = Editor(tc);

        editor.Open(ab);

        Assert.Equal("Linear", editor._curve.Text);
    });

    [Fact]
    public void ANewDefault_ShowsAtOnce_AndWritesNothing() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var ab = Pair(tc);
        using var editor = Editor(tc);
        editor.Open(ab);
        Assert.Equal("Equal-power", editor._curve.Text);

        tc.Engine.ChangeSettings(new PlayerSettings { DefaultCurve = FadeCurveKind.Linear, BlankDurationSeconds = 8 });
        editor.Refresh(tc.View); // the next render, in either host

        Assert.Equal("Linear", editor._curve.Text);
        Assert.Equal("8", Placeholders.Get(editor._fadeOutDuration)); // the blank duration follows too
        Assert.Null(tc.Engine.Transition(ab).Global);
    });

    /// <summary>A curve picked with a refused point keeps the pick: the draft is not the store's.</summary>
    [Fact]
    public void ARefusedSave_KeepsTheCurvePicked() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var ab = Pair(tc);
        using var editor = Editor(tc);
        editor.Open(ab);

        editor._fadeOutPoint.Text = "999"; // past the track's end
        editor._curve.SelectedIndex = editor._curve.FindStringExact("Linear");

        Assert.Null(tc.Engine.Transition(ab).Global); // refused
        Assert.Equal("Linear", editor._curve.Text);
    });

    private static TransitionEditorPanel Editor(TestEngine tc) => new(tc.Engine, _ => { }, _ => null);

    private static Adjacency Pair(TestEngine tc)
    {
        var a = TestAudio.WriteDcWav("curve-a.wav", 3, 0.1);
        var b = TestAudio.WriteDcWav("curve-b.wav", 3, 0.2);
        tc.Add(a, b);
        return tc.AdjacencyOf(a, b);
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
