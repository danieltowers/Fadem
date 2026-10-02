using System.Runtime.ExceptionServices;
using Faderio.Core.Domain;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>Ticking From end in the editor, then previewing (asked 2026-10-02, phase 6). Ticking
/// re-expresses the same moment from the track's end (ADR 0003), so the preview's fade begins
/// where it did;
/// rendered through the chain on the fake output. The outgoing track is DC at 0.4 and the
/// incoming at 0.1, with linear fades, so the output first leaves 0.4 where the fade begins.</summary>
public sealed class FromEndPreviewTests
{
    private const int Rate = 44100;

    [Fact]
    public void TickingFromEnd_ThenPreviewing_FadesAtTheSameMoment() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("fromend-a.wav", 10, 0.4);
        var b = TestAudio.WriteDcWav("fromend-b.wav", 10, 0.1);
        tc.Engine.Add(new[] { a, b }).Wait();
        using var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);
        editor.Open(tc.AdjacencyOf(a, b));
        editor._curve.SelectedIndex = editor._curve.FindStringExact("Linear");
        foreach (var (box, text) in new[] { (editor._fadeOutPoint, "7"), (editor._fadeOutDuration, "1"), (editor._fadeInPoint, "0"), (editor._fadeInDuration, "1") })
        {
            box.Text = text;
        }
        editor.Commit();

        var fromStart = FadeBeginsInPreview(tc, editor);
        editor._fadeOutFromEnd.Checked = true;
        var fromEnd = FadeBeginsInPreview(tc, editor);

        Assert.Equal("3", editor._fadeOutPoint.Text);
        Assert.True(tc.Engine.Transition(tc.AdjacencyOf(a, b)).Global!.FadeOutPoint!.FromEnd);
        Assert.Equal(5.0, fromStart, 2); // 5 s of pre-roll before the 7 s point
        Assert.Equal(fromStart, fromEnd, 3);
    });

    /// <summary>Previews the editor's fields and returns where, in seconds from the preview's
    /// start, the output first leaves the outgoing track's level.</summary>
    private static double FadeBeginsInPreview(TestEngine tc, TransitionEditorPanel editor)
    {
        tc.Engine.StopPreview();
        var from = tc.Output.Blocks.Count;
        editor.Preview();
        Assert.NotNull(tc.View.Preview);
        tc.Output.Advance(Rate * 7);
        var left = tc.Output.Blocks.Skip(from).SelectMany(block => block).Where((_, i) => i % 2 == 0).ToArray();
        var start = Array.FindIndex(left, s => Math.Abs(s) > 0.01f); // the preview's first sound
        var level = start >= 0 ? left[start] : 0;
        var frame = start < 0 ? -1 : Array.FindIndex(left, start, s => Math.Abs(s - level) > 1e-4);
        Assert.True(start >= 0 && frame >= 0, "the preview never left the outgoing level");
        return (double)(frame - start) / Rate;
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
