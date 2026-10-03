using System.Reflection;
using System.Runtime.ExceptionServices;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>Phase 6 issue 05: the transition editor saves a change when it is finished, and
/// Undo and Redo, buttons and keys, walk the engine's history (issue 04). On each host's
/// runtime, against a real engine.</summary>
public sealed class EditorSavesItselfTests
{
    [Fact]
    public void ABoxSaves_OnLeavingIt_OrOnEnter_AndNotWhileTyping() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);

        editor._fadeOutDuration.Text = "0.5";
        Assert.Null(Stored(tc, ab));

        Raise(editor._fadeOutDuration, "OnLeave", EventArgs.Empty);
        Assert.Equal(TimeSpan.FromSeconds(0.5), Stored(tc, ab)!.FadeOutDuration);

        editor._fadeInDuration.Text = "0.25";
        Raise(editor._fadeInDuration, "OnKeyDown", new KeyEventArgs(Keys.Enter));
        Assert.Equal(TimeSpan.FromSeconds(0.25), Stored(tc, ab)!.FadeInDuration);
    });

    [Fact]
    public void LeavingABoxNobodyTypedIn_SavesNothing() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);

        Raise(editor._fadeOutDuration, "OnLeave", EventArgs.Empty);

        Assert.Null(Stored(tc, ab));
        Assert.Null(tc.View.NextUndo);
    });

    [Fact]
    public void Undo_PutsTheFieldAndTheStoreBack_AndRedoAgain() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);
        Type(editor._fadeOutDuration, "0.5");
        Type(editor._fadeOutDuration, "1");
        editor.Refresh(tc.View);
        Assert.True(editor._undo.Enabled);
        Assert.False(editor._redo.Enabled);

        Click(editor._undo);

        Assert.Equal("0.5", editor._fadeOutDuration.Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), Stored(tc, ab)!.FadeOutDuration);
        editor.Refresh(tc.View);
        Assert.True(editor._redo.Enabled);

        Click(editor._redo);

        Assert.Equal("1", editor._fadeOutDuration.Text);
        Assert.Equal(TimeSpan.FromSeconds(1), Stored(tc, ab)!.FadeOutDuration);
    });

    [Fact]
    public void Undo_OfAnotherAdjacencysChange_OpensThatAdjacency() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var tracks = Enumerable.Range(0, 3).Select(i => tc.TrackOfLength(Path.Combine(tc.TempDir, $"t{i}.wav"), 30)).ToArray();
        tc.Engine.AddTracks(tracks);
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        var (ab, bc) = (new Adjacency(tracks[0], tracks[1]), new Adjacency(tracks[1], tracks[2]));
        editor.Open(ab);
        Type(editor._fadeOutDuration, "0.5");
        editor.Open(bc);

        editor.UndoChange();

        Assert.Equal(ab, editor.Adjacency);
        Assert.Null(Stored(tc, ab));
        Assert.Equal("", editor._fadeOutDuration.Text);
    });

    [Fact]
    public void CtrlZ_InABoxBeingTypedIn_PutsBackTheValueFromBeforeTheTyping() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);
        Type(editor._fadeOutDuration, "0.5");
        editor._fadeOutDuration.Text = "3"; // typed, not yet left

        Assert.True(Key(editor, Keys.Control | Keys.Z));

        Assert.Equal("0.5", editor._fadeOutDuration.Text);
        Assert.Equal(TimeSpan.FromSeconds(0.5), Stored(tc, ab)!.FadeOutDuration);
        Assert.True(Key(editor, Keys.Control | Keys.Y));
        Assert.Equal("3", editor._fadeOutDuration.Text);
    });

    [Fact]
    public void TextThatIsNotANumber_ShowsItsReason_AndSavesNothing() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var notices = new List<string>();
        var (editor, ab) = Editing(tc, notices.Add);

        Type(editor._fadeOutDuration, "abc");

        Assert.Null(Stored(tc, ab));
        Assert.True(editor.ReasonFor(TransitionField.FadeOutDuration).Text.Length > 0);
        Assert.StartsWith("Not saved: ", Assert.Single(notices));
        Assert.Null(tc.View.NextUndo);
    });

    [Fact]
    public void RemoveTransition_EmptiesTheFields_AndUndoRefillsThem() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);
        Type(editor._fadeOutDuration, "0.5");

        Click(editor._remove);

        Assert.Equal("", editor._fadeOutDuration.Text);
        Assert.Null(Stored(tc, ab));
        Click(editor._undo);
        Assert.Equal("0.5", editor._fadeOutDuration.Text);
    });

    [Fact]
    public void TheEditor_HasUndoAndRedo_AndNoSaveOrRevert() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, _) = Editing(tc);

        var buttons = Descendants(editor).OfType<Button>().Select(b => b.Text).ToList();

        Assert.Contains("Undo", buttons);
        Assert.Contains("Redo", buttons);
        Assert.DoesNotContain("Save", buttons);
        Assert.DoesNotContain("Revert", buttons);
        editor.Refresh(tc.View);
        Assert.False(editor._undo.Enabled); // nothing changed yet
    });

    [Fact]
    public void AnotherCallersSave_ShowsInTheEditor_OnTheNextRender() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);
        editor.Refresh(tc.View);

        tc.Engine.Author(ab, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) }); // a drag on the seek bar
        editor.Refresh(tc.View);

        Assert.Equal("2", editor._fadeOutDuration.Text);
        Assert.False(editor.HasUnsavedEdits);
    });

    [Fact]
    public void ABoxBeingTypedIn_IsKept_WhenAnotherCallerSaves() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var (editor, ab) = Editing(tc);
        editor.Refresh(tc.View);

        editor._fadeInDuration.Text = "0.7";
        tc.Engine.Author(ab, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) });
        editor.Refresh(tc.View);

        Assert.Equal("0.7", editor._fadeInDuration.Text);
    });

    private static (TransitionEditorPanel Editor, Adjacency AB) Editing(TestEngine tc, Action<string>? notice = null)
    {
        var a = tc.TrackOfLength(Path.Combine(tc.TempDir, "a.wav"), 30);
        var b = tc.TrackOfLength(Path.Combine(tc.TempDir, "b.wav"), 30);
        tc.Engine.AddTracks(new[] { a, b });
        var editor = new TransitionEditorPanel(tc.Engine, notice ?? (_ => { }), _ => null);
        var ab = new Adjacency(a, b);
        editor.Open(ab);
        return (editor, ab);
    }

    private static TransitionSpec? Stored(TestEngine tc, Adjacency adjacency) => tc.Engine.Transition(adjacency).Global;

    /// <summary>Types into a box and leaves it, as a user would.</summary>
    private static void Type(TextBox box, string text)
    {
        box.Text = text;
        Raise(box, "OnLeave", EventArgs.Empty);
    }

    private static void Click(Button button) => Raise(button, "OnClick", EventArgs.Empty);

    private static void Raise(Control control, string handler, EventArgs e) =>
        typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { e.GetType() }, null)!
            .Invoke(control, new object[] { e });

    /// <summary>A key pressed with focus in the editor: whether the editor took it.</summary>
    private static bool Key(TransitionEditorPanel editor, Keys keys)
    {
        var args = new object[] { new Message(), keys };
        return (bool)typeof(TransitionEditorPanel).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(editor, args)!;
    }

    private static IEnumerable<Control> Descendants(Control control) =>
        control.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Descendants(c)));

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
