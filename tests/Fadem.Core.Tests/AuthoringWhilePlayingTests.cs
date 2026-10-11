using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>
/// A save while playing never makes rendering wait on the disk. Measured before the fix
/// (60 saves of a 22 KB store, rendering 10 ms blocks beside them): a save held the engine lock
/// for its whole file write, about 3 ms on net8.0 and 20 ms (up to 70) on net48 x86, where
/// File.Replace was slow, and rendering waited that long on it, against a device buffer of
/// 50–150 ms. Now the change is made under the lock and written once the lock is released.
/// </summary>
public sealed class AuthoringWhilePlayingTests
{
    private const int Rate = 44100;

    private static TransitionSpec Crossfade(double outSeconds) => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(outSeconds), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    /// <summary>Renders one block on another thread while the store's file is being written,
    /// and says whether it finished: a render waiting on the lock cannot.</summary>
    private static bool RendersDuringTheWrite(TestEngine tc, Action save)
    {
        var rendered = false;
        tc.Store.Writing = () =>
        {
            var render = new Thread(() => tc.Output.Advance(441));
            render.Start();
            rendered = render.Join(TimeSpan.FromSeconds(5));
        };
        try
        {
            save();
        }
        finally
        {
            tc.Store.Writing = null;
        }
        return rendered;
    }

    [Fact]
    public void Rendering_GoesOn_WhileASaveIsWritten()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Play(a, b);
        tc.Output.Advance(Rate / 2);
        var adjacency = tc.AdjacencyOf(a, b);

        Assert.True(RendersDuringTheWrite(tc, () => tc.Engine.Author(adjacency, Crossfade(2))), "Author");
        Assert.True(RendersDuringTheWrite(tc, () => tc.Engine.Undo()), "Undo");
        Assert.True(RendersDuringTheWrite(tc, () => tc.Engine.Redo()), "Redo");
        Assert.True(RendersDuringTheWrite(tc,
            () => tc.Engine.MoveFadeMark(tc.RowOf(a), FadeMark.FadeOutStart, Playhead.FromMilliseconds(3000))),
            "MoveFadeMark");
        Assert.True(RendersDuringTheWrite(tc, () => tc.Engine.Unauthor(adjacency)), "Unauthor");
    }

    [Fact]
    public void AFadeOutPointDecodedButNotYetHeard_FadesFromWhereDecodingIs()
    {
        // The device holds 0.3 s: at 1.5 s decoded, 1.2 s is heard. A fade-out point dropped at
        // 1.35 s is ahead of the listener, so it fades now, not as a hard cut at the track's end.
        using var tc = TestEngine.Create(latency: Rate * 3 / 10);
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Play(a, b);
        tc.Output.Advance(Rate * 3 / 2);

        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(1.35));
        tc.Output.Advance(Rate * 2);

        // Rendered frames: the fade runs from 1.5 s to 2.5 s.
        Assert.True(tc.Rendered.ToneAmplitude(2000, Rate * 2) > 0.1, "b is fading in by 2.0 s");
        Assert.True(tc.Rendered.ToneAmplitude(1000, Rate * 26 / 10) < 0.05, "a is gone by 2.6 s");
    }

    [Fact]
    public void AFadeOutPointAlreadyHeard_IsStillAHardCut()
    {
        using var tc = TestEngine.Create(latency: Rate * 3 / 10);
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Play(a, b);
        tc.Output.Advance(Rate * 3 / 2);

        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade(1.1)); // heard at 1.2 s: behind the listener
        tc.Output.Advance(Rate * 2);

        Assert.True(tc.Rendered.ToneAmplitude(2000, Rate * 3) < 0.05, "no b before a ends");
        Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, Rate * 3), 2);
    }

    [Fact]
    public void ASaveThatCannotBeWritten_LeavesNothingToUndo()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        tc.Add(a, b);
        var adjacency = tc.AdjacencyOf(a, b);
        tc.Engine.Author(adjacency, Crossfade(2));
        var file = tc.Library.TransitionsPath;
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            tc.Engine.Author(adjacency, Crossfade(1));

            Assert.Equal(TimeSpan.FromSeconds(2), tc.Store.Get(a, b)!.FadeOutPoint!.Position);
            Assert.Equal(new HistoryStep(adjacency, null, false), tc.View.NextUndo); // the first save only
            Assert.StartsWith("Not saved: ", tc.Notices[tc.Notices.Count - 1]);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
