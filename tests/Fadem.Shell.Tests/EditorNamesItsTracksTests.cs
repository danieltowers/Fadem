using System.Reflection;
using System.Runtime.ExceptionServices;
using Fadem.Core.Tests;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.Shell.Tests;

/// <summary>Round 2 issue 10: each half of the editor names its track, numbered as the queue
/// numbers its row (seam markers issue 03), the outgoing one above the
/// fade-out fields and the incoming one above the fade-in fields; with no adjacency open both are
/// empty and take no height.</summary>
public sealed class EditorNamesItsTracksTests
{
    [Fact]
    public void AnAdjacencyOpen_NamesItsOutgoingTrackAboveTheFadeOut_AndItsIncomingAboveTheFadeIn() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = new[] { TestAudio.WriteDcWav("names-a.wav", 3, 0.1), TestAudio.WriteDcWav("names-b.wav", 3, 0.2) };
        tc.Add(paths);
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);

        editor.Open(tc.AdjacencyOf(paths[0], paths[1]));

        Assert.Equal($"1 · {NameOf(paths[0])}", editor._outgoingTitle.Text);
        Assert.Equal($"2 · {NameOf(paths[1])}", editor._incomingTitle.Text);
        Assert.True(IsSetVisible(editor._outgoingTitle) && IsSetVisible(editor._incomingTitle));
        // Each sits on the row just above its half's first label.
        var layout = (TableLayoutPanel)editor._outgoingTitle.Parent!;
        Assert.Equal(RowOf(layout, "Fade-out point (s)") - 1, layout.GetRow(editor._outgoingTitle));
        Assert.Equal(RowOf(layout, "Fade-in point (s)") - 1, layout.GetRow(editor._incomingTitle));
        StudioTheme.Apply(editor, Palette.Studio);
        Assert.Equal(Palette.Studio.Dim, editor._outgoingTitle.ForeColor);
        Assert.Equal(Palette.Studio.Dim, editor._incomingTitle.ForeColor);
    });

    [Fact]
    public void AnotherAdjacency_MovesTheNames_AndNoneEmptiesThem() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var paths = new[]
        {
            TestAudio.WriteDcWav("names-x.wav", 3, 0.1),
            TestAudio.WriteDcWav("names-y.wav", 3, 0.2),
            TestAudio.WriteDcWav("names-z.wav", 3, 0.3),
        };
        tc.Add(paths);
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        Assert.Equal("", editor._outgoingTitle.Text); // at launch
        Assert.False(IsSetVisible(editor._outgoingTitle) || IsSetVisible(editor._incomingTitle));

        editor.Open(tc.AdjacencyOf(paths[0], paths[1]));
        editor.Open(tc.AdjacencyOf(paths[1], paths[2]));

        Assert.Equal($"2 · {NameOf(paths[1])}", editor._outgoingTitle.Text);
        Assert.Equal($"3 · {NameOf(paths[2])}", editor._incomingTitle.Text);

        editor.Close();

        Assert.Equal("", editor._outgoingTitle.Text);
        Assert.Equal("", editor._incomingTitle.Text);
        Assert.False(IsSetVisible(editor._outgoingTitle) || IsSetVisible(editor._incomingTitle));
        Assert.Equal("", editor._titleTips.GetToolTip(editor._outgoingTitle));
    });

    /// <summary>A title longer than the editor is one line cut short with "…", whole in its tooltip.</summary>
    [Fact]
    public void ALongTitle_IsOneLineCutShort_AndWholeInItsTooltip() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var name = "names-" + new string('w', 80);
        var paths = new[] { TestAudio.WriteDcWav(name + ".wav", 3, 0.1), TestAudio.WriteDcWav("names-short.wav", 3, 0.2) };
        tc.Add(paths);
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);

        editor.Open(tc.AdjacencyOf(paths[0], paths[1]));

        var label = editor._outgoingTitle;
        Assert.True(label.AutoEllipsis);
        Assert.False(label.AutoSize);
        Assert.True(label.Height < 2 * label.Font.Height, $"the name is {label.Height} px tall: more than a line");
        Assert.True(TextRenderer.MeasureText(name, label.Font).Width > TransitionEditorPanel.DefaultWidth); // it must be cut
        Assert.Equal($"1 · {NameOf(paths[0])}", editor._titleTips.GetToolTip(label));
        Assert.Equal($"2 · {NameOf(paths[1])}", editor._titleTips.GetToolTip(editor._incomingTitle));
    });

    /// <summary>A track with no title tag is named by its file.</summary>
    private static string NameOf(string path) => Path.GetFileNameWithoutExtension(path);

    private static int RowOf(TableLayoutPanel layout, string text) =>
        layout.GetRow(layout.Controls.OfType<Label>().Single(l => l.Text == text));

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
