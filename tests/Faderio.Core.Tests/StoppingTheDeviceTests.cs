using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Every command that stops the device, on a device whose stop joins its render thread the way
/// WASAPI's does (build issue 19). The render thread is caught just past its stopped check, on
/// its way into the engine, so each stop meets the interleaving that froze the window: the
/// command holding the engine lock while it waits for the render thread, and the render thread
/// waiting for that lock.
/// </summary>
public sealed class StoppingTheDeviceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "faderio-engine", Guid.NewGuid().ToString("N"));
    private readonly JoiningAudioOutput _output = new();
    private readonly PlaybackEngine _engine;

    public StoppingTheDeviceTests()
    {
        Directory.CreateDirectory(_dir);
        _engine = new PlaybackEngine(
            _output, new JsonTransitionStore(Path.Combine(_dir, "transitions.json")), new RecordingSink())
        {
            StartAddWorker = work => work(),
        };
    }

    [Fact]
    public void Stop_WhileTheRenderThreadWaitsToRender_Returns()
    {
        PlayOneRow();

        Assert.True(Returns(_engine.Stop), "Stop froze.");
        Assert.Equal(Transport.Stopped, _engine.View.Transport);
    }

    [Fact]
    public void Clear_WhileTheRenderThreadWaitsToRender_Returns()
    {
        PlayOneRow();

        Assert.True(Returns(_engine.Clear), "Clear froze.");
        Assert.Empty(_engine.View.Queue);
    }

    [Fact]
    public void RemovingTheOnlyPlayingRow_WhileTheRenderThreadWaitsToRender_Returns()
    {
        var row = PlayOneRow();

        Assert.True(Returns(() => _engine.Remove(new[] { row })), "Remove froze.");
        Assert.Equal(Transport.Stopped, _engine.View.Transport);
    }

    [Fact]
    public void SkippingPastTheLastRow_WhileTheRenderThreadWaitsToRender_Returns()
    {
        PlayOneRow();

        Assert.True(Returns(_engine.Next), "Next froze.");
        Assert.Equal(Transport.Stopped, _engine.View.Transport);
    }

    [Fact]
    public void Dispose_WhileTheRenderThreadWaitsToRender_Returns()
    {
        PlayOneRow();

        Assert.True(Returns(_engine.Dispose), "Dispose froze.");
    }

    [Fact]
    public void PlayingAgain_AfterAStop_Renders()
    {
        PlayOneRow();
        Assert.True(Returns(_engine.Stop), "Stop froze.");

        _engine.Play();

        Assert.True(_output.RenderOneBlock(Patience), "The render thread never read after Play.");
        Assert.Equal(Transport.Playing, _engine.View.Transport);
    }

    private RowId PlayOneRow()
    {
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.5);
        var row = _engine.Add(new[] { a }).Result.Added.Single().Id;
        _engine.Play();
        Assert.True(_output.RenderOneBlock(Patience), "The render thread never read.");
        return row;
    }

    /// <summary>Runs <paramref name="command"/> on its own thread, as the UI thread would, and
    /// says whether it came back. A frozen command is left parked on a background thread.</summary>
    private static bool Returns(Action command) =>
        Task.Factory.StartNew(command, TaskCreationOptions.LongRunning).Wait(Patience);

    public void Dispose()
    {
        if (Returns(_engine.Dispose))
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>
    /// A device with a real render thread and WASAPI's stop: <see cref="Stop"/> joins the
    /// thread. Between blocks the thread is parked just past its stopped check, so a stop always
    /// finds it about to call <see cref="IFrameSource.Read"/>. A stop asked for from inside a
    /// read ends the stream instead, since a thread cannot join itself.
    /// </summary>
    private sealed class JoiningAudioOutput : IAudioOutput
    {
        private const int BlockFrames = 4410;

        private readonly SemaphoreSlim _proceed = new(0);
        private readonly SemaphoreSlim _rendered = new(0);
        private IFrameSource? _source;
        private Thread? _thread;
        private volatile bool _stopRequested;
        private long _framesRendered;

        public int SampleRate => 44100;

        public Transport State { get; private set; } = Transport.Stopped;

        public Frames FramesEmitted =>
            State == Transport.Stopped ? Frames.Zero : new Frames(Interlocked.Read(ref _framesRendered));

        public event Action<Exception?>? Stopped;

        public void Initialise(IFrameSource source) => _source = source;

        public void Play()
        {
            State = Transport.Playing;
            if (_thread is not null)
            {
                return;
            }
            _stopRequested = false;
            _thread = new Thread(RenderLoop) { IsBackground = true, Name = "fake render" };
            _thread.Start();
        }

        public void Pause() => State = Transport.Paused;

        public void Stop()
        {
            State = Transport.Stopped;
            Interlocked.Exchange(ref _framesRendered, 0);
            _stopRequested = true;
            if (_thread is null || Thread.CurrentThread == _thread)
            {
                return;
            }
            _proceed.Release(); // the parked thread was already past its check
            _thread.Join();
            _thread = null;
        }

        /// <summary>Lets the render thread through for one block and waits until it is parked
        /// again.</summary>
        public bool RenderOneBlock(TimeSpan timeout)
        {
            _proceed.Release();
            return _rendered.Wait(timeout);
        }

        public void Dispose()
        {
            Stop();
            Stopped?.Invoke(null);
        }

        private void RenderLoop()
        {
            var buffer = new float[BlockFrames * 2];
            while (!_stopRequested)
            {
                _proceed.Wait(); // parked: past the stopped check, about to read
                var read = _source!.Read(buffer, 0, new Frames(BlockFrames));
                Interlocked.Add(ref _framesRendered, read.Count);
                _rendered.Release();
            }
        }
    }
}
