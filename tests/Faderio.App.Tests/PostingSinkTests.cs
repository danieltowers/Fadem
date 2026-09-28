using Faderio.App;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Core.Transitions;

namespace Faderio.App.Tests;

/// <summary>
/// The production sink against a UI thread that is busy: publications pile up behind it, as a
/// background add's do (build issue 20). <c>Changed</c> is coalesced (ADR 0003's seam): however
/// many arrive while one is pending, the UI thread renders once, and renders the latest view.
/// The next render waits until the UI thread has been idle, so paint and input run between them.
/// </summary>
public sealed class PostingSinkTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(TestTemp.Root, "sink", Guid.NewGuid().ToString("N"));

    /// <summary>A UI thread that has not yet got round to its queue: posts wait until drained.</summary>
    private sealed class BusyContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

        public int Pending => _queue.Count;

        public override void Post(SendOrPostCallback d, object? state) => _queue.Enqueue((d, state));

        public void Drain()
        {
            while (_queue.TryDequeue(out var post))
            {
                post.Callback(post.State);
            }
        }
    }

    public PostingSinkTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch (IOException) { }
    }

    private PlaybackEngine Engine(PostingSink sink) =>
        new(new FakeAudioOutput(), new JsonTransitionStore(Path.Combine(_tempDir, "transitions.json")), sink)
        {
            StartAddWorker = work => work(),
        };

    [Fact]
    public async Task AnAddOfFiftyFiles_WhileTheUiThreadIsBusy_IsOneRenderOfTheWholeQueue()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);
        var renders = new List<PlayerView>();
        sink.Changed += renders.Add;
        using var engine = Engine(sink);
        var track = TestAudio.WriteDcWav("fifty.wav", 1, 0.2);

        await engine.Add(Enumerable.Repeat(track, 50));

        Assert.Equal(1, ui.Pending);
        ui.Drain();
        var render = Assert.Single(renders);
        Assert.Equal(50, render.Queue.Count);
        Assert.Null(render.Adding);
    }

    [Fact]
    public async Task AfterARender_TheNextWaitsUntilTheUiThreadHasBeenIdle()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);
        var renders = new List<PlayerView>();
        sink.Changed += renders.Add;
        using var engine = Engine(sink);
        var track = TestAudio.WriteDcWav("again.wav", 1, 0.2);

        await engine.Add(new[] { track });
        ui.Drain();
        await engine.Add(new[] { track, track });

        // WinForms runs a post queued during its pass over the queue in the same pass, so a
        // render posted straight after the last would keep the pump from ever running.
        Assert.Equal(0, ui.Pending);
        sink.UiThreadIdle();
        ui.Drain();
        Assert.Equal(new[] { 1, 3 }, renders.Select(v => v.Queue.Count));
    }

    [Fact]
    public void IdleWithNothingPublished_PostsNothing()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);

        sink.UiThreadIdle();

        Assert.Equal(0, ui.Pending);
    }

    [Fact]
    public async Task APublicationAfterAnIdleUiThread_IsPostedAtOnce()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);
        var renders = new List<PlayerView>();
        sink.Changed += renders.Add;
        using var engine = Engine(sink);
        var track = TestAudio.WriteDcWav("idle.wav", 1, 0.2);

        await engine.Add(new[] { track });
        ui.Drain();
        sink.UiThreadIdle();
        await engine.Add(new[] { track });

        Assert.Equal(1, ui.Pending);
        ui.Drain();
        Assert.Equal(new[] { 1, 2 }, renders.Select(v => v.Queue.Count));
    }

    [Fact]
    public async Task Notices_AreNeverCoalesced()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);
        var notices = new List<string>();
        sink.Noticed += notices.Add;
        using var engine = Engine(sink);

        await engine.Add(new[] { Path.Combine(_tempDir, "missing-1.wav") });
        await engine.Add(new[] { Path.Combine(_tempDir, "missing-2.wav") });
        ui.Drain();

        Assert.Equal(2, notices.Count);
    }

    [Fact]
    public async Task ANotice_FollowsTheViewsPublishedBeforeIt()
    {
        var ui = new BusyContext();
        var sink = new PostingSink(ui);
        var shown = new List<string>();
        sink.Changed += v => shown.Add(v.Adding is null ? "idle" : "adding");
        sink.Noticed += _ => shown.Add("notice");
        using var engine = Engine(sink);
        var track = TestAudio.WriteDcWav("order.wav", 1, 0.2);

        await engine.Add(new[] { track });
        ui.Drain();
        shown.Clear();
        // The add's views wait for the UI thread to go idle; its rejection notice must not
        // overtake them. The latest, the add ended, is shown before it.
        await engine.Add(new[] { Path.Combine(_tempDir, "missing.wav") });
        ui.Drain();

        Assert.Equal(new[] { "idle", "notice" }, shown);
    }
}
