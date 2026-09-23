using System.Reflection;
using System.Runtime.ExceptionServices;
using Faderio.App;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App.Tests;

/// <summary>
/// Seam S5 (ADR 0002): the real <see cref="MainForm"/> on an STA thread, never shown, driven by
/// the fake output and an inline sink. Controls are poked, not pixels: a click is the control's
/// own click handler, and the assertions are the control values a user would see.
/// </summary>
public sealed class HeadlessFormTests
{
    private sealed class Harness : IDisposable
    {
        public string TempDir { get; } = Path.Combine(Path.GetTempPath(), "faderio-app", Guid.NewGuid().ToString("N"));
        public FakeAudioOutput Output { get; }
        public JsonTransitionStore Store { get; }
        public PlaybackEngine Engine { get; }
        public MainForm Form { get; }

        public Harness(int latency = 0)
        {
            Directory.CreateDirectory(TempDir);
            Output = new FakeAudioOutput { Latency = latency };
            Store = new JsonTransitionStore(Path.Combine(TempDir, "transitions.json"));
            var sink = new PostingSink(null); // inline: no message pump in a headless test
            Engine = new PlaybackEngine(Output, Store, sink);
            Form = new MainForm(Engine, Store, sink);
        }

        public void Dispose()
        {
            Form.Dispose();
            Engine.Dispose();
            try { Directory.Delete(TempDir, recursive: true); } catch (IOException) { }
        }
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

    /// <summary>Fires the control's own click handler. <c>PerformClick</c> refuses on a form that
    /// was never shown, and showing one is the thing a headless test must not do.</summary>
    private static void Click(Button button)
    {
        Assert.True(button.Enabled, $"{button.Text} is disabled");
        var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        onClick.Invoke(button, new object[] { EventArgs.Empty });
    }

    [Fact]
    public void AddTwoFiles_ShowsTwoRows_WithTitlesAndLengths() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 2, 0.2);

        h.Form.AddPaths(new[] { a, b });

        Assert.Equal(2, h.Form._queueList.Items.Count);
        Assert.Equal(Path.GetFileNameWithoutExtension(a), h.Form._queueList.Items[0].SubItems[1].Text);
        Assert.Equal("0:03", h.Form._queueList.Items[0].SubItems[3].Text);
        Assert.Equal("0:02", h.Form._queueList.Items[1].SubItems[3].Text);
        Assert.Equal("Added 2 tracks.", h.Form._status.Text);
    });

    [Fact]
    public void ARejectedFile_LeavesTheListAlone_AndNamesTheReason() => RunSta(() =>
    {
        using var h = new Harness();
        var quad = TestAudio.WriteWavAt("quad", 1, _ => 0.3, 44100, 16, 4);

        h.Form.AddPaths(new[] { quad });

        Assert.Empty(h.Form._queueList.Items);
        Assert.Contains("4 channels", h.Form._status.Text);
        Assert.Contains("supported-audio.md", h.Form._status.Text);
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
    });

    [Fact]
    public void PlayRow_ThenAdvance_TheSeekBarAndReadout_FollowThePlayhead() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });

        h.Form.PlayRowAt(1);
        Assert.Equal(Transport.Playing, h.Engine.View.Transport);
        Assert.Equal("⏸", h.Form._playPause.Text);

        h.Output.Advance(44100 * 3);
        h.Form.Render(); // the display timer's tick

        Assert.Equal(5000, h.Form._seek.Maximum);           // milliseconds, no scaling
        Assert.Equal(3000, h.Form._seek.Value);
        Assert.Equal("0:03 / -0:02", h.Form._time.Text);
        Assert.Equal(SystemColors.Info, h.Form._queueList.Items[1].BackColor);
        Assert.Equal(SystemColors.Window, h.Form._queueList.Items[0].BackColor);
    });

    [Fact]
    public void Capture_WritesThePlayheadIntoTheField_AndKeepsTheExactPoint() => RunSta(() =>
    {
        using var h = new Harness(latency: 4410);
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        Assert.Contains("alpha", h.Form._editor._title.Text);
        Assert.False(h.Form._editor._captureOut.Enabled); // nothing attended yet

        h.Form.PlayRowAt(0);
        h.Output.Advance(66150 + 4410); // audible 1.5 s, rendered 1.6 s
        h.Form.Render();

        Click(h.Form._editor._captureOut);

        Assert.Equal("1.5", h.Form._editor._fadeOutPoint.Text);
        var captured = h.Form._editor.CapturedFadeOut!.Value;
        Assert.Equal(h.Engine.View.Attended!.Playhead.TimeSpan, captured.TimeSpan);
    });

    [Fact]
    public void Save_AuthorsTheAdjacency_AndConfirmsInTheStatusStrip() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "1.5";
        h.Form._editor._fadeOutDuration.Text = "0.5";
        h.Form._editor._fadeInPoint.Text = "0";
        h.Form._editor._fadeInDuration.Text = "0.5";

        Click(h.Form._editor._save);

        var spec = h.Store.Get(a, b)!;
        Assert.Equal(TimeSpan.FromSeconds(1.5), spec.FadeOutPoint!.Position);
        Assert.False(spec.FadeOutPoint.FromEnd);
        Assert.Equal(TimeSpan.FromSeconds(0.5), spec.FadeOutDuration);
        Assert.Equal(TimeSpan.Zero, spec.FadeInPoint!.Position);
        Assert.StartsWith("Saved alpha", h.Form._editor._title.Text.Length > 0 ? h.Form._status.Text : "");
    });

    [Fact]
    public void Save_WithABadNumber_SavesNothing_AndSaysWhy() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "abc";

        Click(h.Form._editor._save);

        Assert.Null(h.Store.Get(a, b));
        Assert.StartsWith("Not saved:", h.Form._status.Text);
    });

    [Fact]
    public void EditTransition_IsOfferedOnEveryRowButTheLast() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a, b });

        Assert.NotNull(h.Form.AdjacencyAt(0));
        Assert.Null(h.Form.AdjacencyAt(1));
        h.Form.EditTransitionAt(1);
        Assert.Equal("No boundary selected", h.Form._editor._title.Text);
    });

    [Fact]
    public void TheCapturedPoint_IsWhatGetsSaved_NotTheDisplayedText() => RunSta(() =>
    {
        // The field shows three decimals; the store gets the frame-exact point (ticket 06).
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form.PlayRowAt(0);
        h.Output.Advance(12345); // 0.27993 s: not a whole millisecond
        h.Form.Render();

        Click(h.Form._editor._captureOut);
        Click(h.Form._editor._save);

        Assert.Equal("0.28", h.Form._editor._fadeOutPoint.Text);
        var saved = h.Store.Get(a, b)!.FadeOutPoint!.Position;
        Assert.Equal(12345, Ticks.FramesOf(saved, 44100));
    });
}
