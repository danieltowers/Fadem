using System.Collections.Concurrent;
using Fadem.Core.Domain;
using Fadem.Core.Playback;
using Fadem.Core.Settings;
using Fadem.Core.Tests;
using Fadem.Core.Transitions;
using Fadem.Shell.Audio;
using NAudio.Wave;

namespace Fadem.MusicBee.Tests;

/// <summary>
/// The output lets go of the device while Fadem is stopped (phase 5 issue 06, decision A), on
/// the plugin's runtime. The real output and the real engine; only NAudio's player is faked,
/// through the output's player factory, with a player that stops the way WASAPI's does: its
/// Stop joins its render thread, a stream that ends stops it from that thread, and its stop is
/// delivered where it was made, either posted to the UI thread or raised on the render thread.
/// The devices are faked too (phase 5 issue 13): named, found or not, each opening those players.
/// </summary>
public sealed class ReleasingTheDeviceTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(5);

    private readonly string _dir = Path.Combine(TestTemp.Root, "release", Guid.NewGuid().ToString("N"));
    private readonly List<JoiningPlayer> _players = new();
    private readonly List<string> _outputNotices = new();
    private readonly RecordingSink _sink = new();
    private readonly SynchronizationContext? _before = SynchronizationContext.Current;
    private UiThread? _ui;
    private bool _deviceInUse;
    private NAudioAudioOutput? _output;
    private PlaybackEngine? _engine;

    /// <summary>The devices present, the first the default; and which modes are refused.</summary>
    private readonly List<string> _present = new() { "Speakers", "DAC" };
    private readonly HashSet<AudioOutputMode> _refused = new();
    private string? _chosen;

    // ── Letting go ───────────────────────────────────────────────────────────────────────

    /// <summary>Issue 13: nothing is opened at launch, so a device another player holds then
    /// raises no notice; the rate is asked for, and the mode asked for is the one named.</summary>
    [Fact]
    public void AtLaunch_NothingIsOpened_TheRateIsAsked_AndTheModeAskedForIsNamed()
    {
        _refused.Add(AudioOutputMode.WasapiExclusive); // held by MusicBee's own player at its start
        Compose(AudioOutputMode.WasapiExclusive);

        Assert.Empty(_players);
        Assert.Equal(48000, _output!.SampleRate); // the fake devices' mix rate
        Assert.Equal(AudioOutputMode.WasapiExclusive, _output.Mode);
        Assert.Equal("Speakers", _output.Device);
        Assert.Empty(_outputNotices);
        Assert.Empty(_sink.Notices);
    }

    [Fact]
    public void AtLaunch_WaveOutAskedFor_Runs_At44100_WithoutAskingTheDevice()
    {
        Compose();

        Assert.Empty(_players);
        Assert.Equal(44100, _output!.SampleRate);
        Assert.Equal(AudioOutputMode.WaveOut, _output.Mode);
        Assert.Empty(_outputNotices);
    }

    [Fact]
    public void AtLaunch_WithNoDeviceAtAll_TheEngineIsToldNothingOpened()
    {
        _present.Clear();
        Compose();

        Assert.Null(_output!.Mode);
        Assert.Null(_output.Device);
        Assert.Contains("No audio output device was found; no audio output could be opened.", _outputNotices);
        Assert.Contains(_sink.Notices, n => n.StartsWith("Audio output lost (no audio output could be opened)."));
    }

    [Fact]
    public void Stop_LetsGoOfTheDevice_AndPlayOpensItAgainAndRenders()
    {
        var engine = Compose();
        var first = PlayOneRow(engine, seconds: 30);

        Assert.True(Returns(engine.Stop), "Stop froze.");

        Assert.True(first.Disposed, "The device is still held after Stop.");
        Assert.Equal(Transport.Stopped, engine.View.Transport);

        engine.Play();

        var second = Assert.Single(_players.Skip(1));
        Assert.True(Renders(second), "The render thread never read after Play.");
        Assert.False(second.Disposed);
        Assert.Equal(Transport.Playing, engine.View.Transport);
        Assert.Equal(44100, _output!.SampleRate); // the engine's rate is fixed at launch
        Assert.Empty(_sink.Notices);
    }

    /// <summary>The view's ⏹ after a pause and a resume (phase 5 issue 10): a Stop is a stop,
    /// with nothing attended, however the queue was playing before it.</summary>
    [Fact]
    public void Stop_AfterAPauseAndAResume_LeavesNothingAttended()
    {
        var engine = Compose(uiThread: true);
        var first = PlayOneRow(engine, seconds: 30);
        engine.Pause();
        engine.Play();
        var resumed = _players[_players.Count - 1];
        Assert.True(Renders(resumed), "The render thread never read after the resume.");

        Assert.True(Returns(engine.Stop), "Stop froze.");
        _ui!.Drain();

        Assert.Equal(Transport.Stopped, engine.View.Transport);
        Assert.Null(engine.View.Attended);
        Assert.True(resumed.Disposed, "The device is still held after Stop.");
        Assert.Empty(_sink.Notices);
    }

    [Fact]
    public void TheQueueRunningOut_LetsGoOfTheDevice_FromTheRenderThread_WithoutDeadlocking()
    {
        var engine = Compose();
        var player = PlayOneRow(engine, seconds: 1);

        Assert.True(SpinWait.SpinUntil(() => player.Disposed, Patience), "The device is still held after the queue ran out.");
        Assert.Equal(Transport.Stopped, engine.View.Transport);

        engine.Play(); // the queue from its start: a fresh player

        var again = Assert.Single(_players.Skip(1));
        Assert.True(Renders(again), "The render thread never read after Play.");
    }

    [Fact]
    public void TheQueueRunningOut_LetsGoOfTheDevice_WhenItsStopIsPostedToTheUiThread()
    {
        var engine = Compose(uiThread: true);
        var player = PlayOneRow(engine, seconds: 1);

        Assert.True(SpinWait.SpinUntil(() => player.PlaybackState == PlaybackState.Stopped, Patience), "The stream never ended.");
        Assert.False(player.Disposed); // the stop is on its way to the UI thread

        _ui!.Drain();

        Assert.True(player.Disposed, "The device is still held after its posted stop.");
        Assert.Equal(Transport.Stopped, engine.View.Transport);
    }

    [Fact]
    public void WithoutTheOption_TheDeviceIsKeptBetweenPlays()
    {
        var engine = Compose(releaseWhileStopped: false);
        var player = PlayOneRow(engine, seconds: 30);

        Assert.True(Returns(engine.Stop), "Stop froze.");
        engine.Play();

        Assert.Single(_players); // opened at launch, kept
        Assert.False(player.Disposed);
        Assert.True(Renders(player), "The render thread never read after Play.");
    }

    // ── The mode at each open (issue 13) ─────────────────────────────────────────────────

    /// <summary>A mode the device refuses falls back at once, with its notice: there is no retry.
    /// Inside MusicBee a refusal lasts as long as its own player's stream (issue 13's Sounds
    /// half), so waiting only delayed the fallback.</summary>
    [Fact]
    public void Play_FallsBackAtOnce_WhenTheModeAskedForIsRefused()
    {
        var engine = Compose(AudioOutputMode.WasapiExclusive);
        var a = TestAudio.WriteDcWav("a.wav", 30, 0.5);
        engine.Add(new[] { a }).Result.Added.Single();
        _refused.Add(AudioOutputMode.WasapiExclusive);
        var refusals = 0;
        _onRefusal = () => refusals++;

        var started = DateTime.UtcNow;
        engine.Play();

        var player = Assert.Single(_players);
        Assert.True(Renders(player), "The render thread never read after Play.");
        Assert.Equal(AudioOutputMode.WasapiShared, _output!.Mode);
        Assert.Equal(1, refusals);
        Assert.InRange(DateTime.UtcNow - started, TimeSpan.Zero, TimeSpan.FromMilliseconds(250));
        Assert.Equal("WASAPI exclusive unavailable (the device is in use); trying WASAPI shared.", Assert.Single(_outputNotices));
    }

    /// <summary>After a device loss the next Play reopens from the mode asked for, not the one
    /// a fallback left in use.</summary>
    [Fact]
    public void Reopen_AfterADeviceLoss_OpensFromTheModeAskedFor()
    {
        var engine = Compose(AudioOutputMode.WasapiExclusive, uiThread: true);
        var a = TestAudio.WriteDcWav("a.wav", 30, 0.5);
        engine.Add(new[] { a }).Result.Added.Single();
        _refused.Add(AudioOutputMode.WasapiExclusive);
        engine.Play();
        var fallen = Assert.Single(_players);
        Assert.True(Renders(fallen), "The render thread never read after Play.");
        Assert.Equal(AudioOutputMode.WasapiShared, _output!.Mode);
        _outputNotices.Clear();

        _refused.Clear();
        fallen.Fail(new InvalidOperationException("Exception from HRESULT: 0x88890004"));
        Assert.True(SpinWait.SpinUntil(() => fallen.PlaybackState == PlaybackState.Stopped, Patience), "The player never failed.");
        Posted("The failure was never posted.");
        Assert.Contains(_sink.Notices, n => n.StartsWith("Audio output lost (Exception from HRESULT: 0x88890004)."));

        engine.Play();

        var reopened = Assert.Single(_players.Skip(1));
        Assert.True(Renders(reopened), "The render thread never read after the reopen.");
        Assert.Equal(AudioOutputMode.WasapiExclusive, _output.Mode);
        Assert.Empty(_outputNotices);
    }

    // ── The device (issue 13) ────────────────────────────────────────────────────────────

    [Fact]
    public void TheDeviceChosen_IsOpened_AndNamed()
    {
        _chosen = "DAC";
        var engine = Compose(AudioOutputMode.WasapiShared);
        Assert.Equal("DAC", _output!.Device);

        var player = PlayOneRow(engine, seconds: 30);

        Assert.Equal("DAC", player.Device);
        Assert.Equal("DAC", _output.Device);
        Assert.Empty(_outputNotices);
    }

    [Fact]
    public void TheDeviceChosen_IsAskedForAtEachOpen_SoAChangeAppliesAtTheNextPlay()
    {
        var engine = Compose(AudioOutputMode.WasapiShared);
        var first = PlayOneRow(engine, seconds: 30);
        Assert.Equal("Speakers", first.Device);
        Assert.True(Returns(engine.Stop), "Stop froze.");

        _chosen = "DAC";
        engine.Play();

        var second = Assert.Single(_players.Skip(1));
        Assert.Equal("DAC", second.Device);
        Assert.Equal("DAC", _output!.Device);
    }

    [Fact]
    public void ADeviceNotThere_FallsToTheDefault_WithANotice_AndIsTriedAgainAtTheNextOpen()
    {
        _chosen = "Headphones";
        var engine = Compose(AudioOutputMode.WasapiShared);
        Assert.Equal("Speakers", _output!.Device);
        Assert.Equal("Output device \"Headphones\" was not found; using the default device.", Assert.Single(_outputNotices));

        var first = PlayOneRow(engine, seconds: 30);
        Assert.Equal("Speakers", first.Device);
        Assert.Equal(2, _outputNotices.Count);
        Assert.True(Returns(engine.Stop), "Stop froze.");

        _present.Add("Headphones"); // plugged in again
        engine.Play();

        var second = Assert.Single(_players.Skip(1));
        Assert.Equal("Headphones", second.Device);
        Assert.Equal("Headphones", _output.Device);
        Assert.Equal(2, _outputNotices.Count);
    }

    // ── A stop that arrives after the next Play ──────────────────────────────────────────

    /// <summary>A preview from a paused queue stops the device and starts it in one command
    /// (build issue 16); the stopped player's own stop arrives after.</summary>
    [Fact]
    public void APreviewStartingFromAPausedQueue_KeepsThePlayerItOpened_WhenTheOldStopArrivesAfter()
    {
        var engine = Compose(uiThread: true);
        var (a, b) = PlayTwoRows(engine);
        var playing = _players[0];
        engine.Pause();

        Assert.True(Returns(() => engine.Preview(AdjacencyOf(engine, a, b), Fade())), "Preview froze.");

        var preview = Assert.Single(_players.Skip(1));
        Assert.True(playing.Disposed, "The queue's player is still held while the preview plays.");
        Assert.False(preview.Disposed);

        Posted("The old player's stop was never posted.");

        Assert.False(preview.Disposed, "The old player's stop closed the preview's player.");
        Assert.True(Renders(preview), "The preview's player stopped rendering.");
        Assert.NotNull(engine.View.Preview);
    }

    [Fact]
    public void PlayBeforeARenderThreadStopIsDelivered_KeepsItsPlayer()
    {
        var engine = Compose(uiThread: true);
        var player = PlayOneRow(engine, seconds: 1);
        Assert.True(SpinWait.SpinUntil(() => player.PlaybackState == PlaybackState.Stopped, Patience), "The stream never ended.");

        engine.Play(); // before the UI thread has run the posted stop
        Posted("The stop was never posted.");

        Assert.False(player.Disposed, "The posted stop closed the player Play restarted.");
        Assert.Single(_players);
        Assert.True(Renders(player), "The restarted player stopped rendering.");
        Assert.Equal(Transport.Playing, engine.View.Transport);
    }

    // ── No mode opens at Play ────────────────────────────────────────────────────────────

    [Fact]
    public void Play_WhenNoModeOpens_IsANotice_AndPlaybackStaysPaused_UntilAPlayReopens()
    {
        var engine = Compose(uiThread: true);
        var a = TestAudio.WriteDcWav("a.wav", 30, 0.5);
        engine.Add(new[] { a }).Result.Added.Single();
        _deviceInUse = true;

        engine.Play();

        Assert.Contains("WaveOut unavailable (the device is in use); no audio output could be opened.", _outputNotices);
        Assert.Empty(_players); // nothing opened
        Posted("The failure was never reported.");
        Assert.Contains(_sink.Notices, n => n.StartsWith("Audio output lost (no audio output could be opened)."));
        Assert.Equal(Transport.Paused, engine.View.Transport);

        engine.Play(); // reopens, through the chain, still in vain

        Assert.Contains(_sink.Notices, n => n.StartsWith("No audio output device to play on (no audio output could be opened)."));
        Assert.Equal(Transport.Paused, engine.View.Transport);
        Assert.Empty(_players);

        _deviceInUse = false;
        engine.Play();

        var opened = Assert.Single(_players);
        Assert.True(Renders(opened), "The render thread never read once the device was free.");
        Assert.Equal(Transport.Playing, engine.View.Transport);
    }

    // ── Composition ──────────────────────────────────────────────────────────────────────

    /// <summary>The output as the plugin composes it, on a fake player, and the engine on it.
    /// With <paramref name="uiThread"/> the players post their stop to a UI thread the test
    /// drains; without one they raise it on their render thread.</summary>
    private PlaybackEngine Compose(bool releaseWhileStopped = true, bool uiThread = false) =>
        Compose(AudioOutputMode.WaveOut, releaseWhileStopped, uiThread);

    private PlaybackEngine Compose(AudioOutputMode mode, bool releaseWhileStopped = true, bool uiThread = false)
    {
        Directory.CreateDirectory(_dir);
        _ui = uiThread ? new UiThread() : null;
        SynchronizationContext.SetSynchronizationContext(_ui);
        _output = new NAudioAudioOutput(mode, _outputNotices.Add, null, releaseWhileStopped, () => _chosen, new FakeDevices(this));
        _engine = new PlaybackEngine(_output, new JsonTransitionStore(Path.Combine(_dir, "transitions.json")), _sink);
        return _engine;
    }

    /// <summary>Called on each refusal of a mode, before the output retries or falls back.</summary>
    private Action? _onRefusal;

    private IWavePlayer CreatePlayer(string device, AudioOutputMode mode)
    {
        if (_deviceInUse || _refused.Contains(mode))
        {
            _onRefusal?.Invoke();
            throw new InvalidOperationException("the device is in use");
        }
        var player = new JoiningPlayer { Device = device };
        _players.Add(player);
        return player;
    }

    /// <summary>The machine's devices as the test says: <see cref="_present"/>, the first the
    /// default, all at 48 kHz, each opening a <see cref="JoiningPlayer"/>.</summary>
    private sealed class FakeDevices : IOutputDevices
    {
        private readonly ReleasingTheDeviceTests _test;

        public FakeDevices(ReleasingTheDeviceTests test) => _test = test;

        public IReadOnlyList<string> Names() => _test._present.ToList();

        public OutputDevice? Find(string? name)
        {
            var found = name is null ? _test._present.FirstOrDefault() : _test._present.FirstOrDefault(n => n == name);
            return found is null ? null : new OutputDevice(found, () => 48000, mode => _test.CreatePlayer(found, mode));
        }
    }

    /// <summary>Plays one DC track and returns the player Play opened, rendering.</summary>
    private JoiningPlayer PlayOneRow(PlaybackEngine engine, double seconds)
    {
        var a = TestAudio.WriteDcWav("a.wav", seconds, 0.5);
        engine.Add(new[] { a }).Result.Added.Single();
        engine.Play();
        var player = _players[_players.Count - 1];
        Assert.True(Renders(player), "The render thread never read.");
        return player;
    }

    /// <summary>A playing queue of A and B, long enough for a preview of their adjacency.</summary>
    private (string A, string B) PlayTwoRows(PlaybackEngine engine)
    {
        var a = TestAudio.WriteDcWav("a.wav", 20, 0.5);
        var b = TestAudio.WriteDcWav("b.wav", 20, 0.3);
        engine.Add(new[] { a, b }).Result.Added.ToList();
        engine.Play();
        Assert.True(Renders(_players[_players.Count - 1]), "The render thread never read.");
        return (a, b);
    }

    private static Adjacency AdjacencyOf(PlaybackEngine engine, string outgoing, string incoming) =>
        new(engine.View.Queue.First(r => r.Track.FilePath == outgoing).Track,
            engine.View.Queue.First(r => r.Track.FilePath == incoming).Track);

    private static TransitionSpec Fade() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(12), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(2),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(4), FromEnd: false),
        FadeInDuration = TimeSpan.FromSeconds(2),
        Curve = FadeCurveKind.Linear,
    };

    /// <summary>Whether <paramref name="player"/> renders another block within patience.</summary>
    private static bool Renders(JoiningPlayer player)
    {
        var before = player.Blocks;
        return SpinWait.SpinUntil(() => player.Blocks > before, Patience);
    }

    /// <summary>Runs <paramref name="command"/> on its own thread, as the UI thread would, and
    /// says whether it came back. A frozen command is left parked on a background thread.</summary>
    private static bool Returns(Action command) =>
        Task.Factory.StartNew(command, TaskCreationOptions.LongRunning).Wait(Patience);

    public void Dispose()
    {
        if (_engine is not null && Returns(_engine.Dispose))
        {
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
        SynchronizationContext.SetSynchronizationContext(_before);
    }

    /// <summary>Waits for the player's thread to post its stop or failure, which it does after
    /// its state changes, and runs the post on this thread.</summary>
    private void Posted(string orElse)
    {
        Assert.True(SpinWait.SpinUntil(() => _ui!.Pending > 0, Patience), orElse);
        _ui!.Drain();
    }

    /// <summary>MusicBee's UI thread: what the players post runs when the test drains it.</summary>
    private sealed class UiThread : SynchronizationContext
    {
        private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posts = new();

        public override void Post(SendOrPostCallback d, object? state) => _posts.Enqueue((d, state));

        /// <summary>Posts queued and not yet run.</summary>
        public int Pending => _posts.Count;

        /// <summary>Runs every post so far, on the calling thread; returns how many.</summary>
        public int Drain()
        {
            var ran = 0;
            while (_posts.TryDequeue(out var post))
            {
                post.Callback(post.State);
                ran++;
            }
            return ran;
        }
    }

    /// <summary>A player with a real render thread and WASAPI's stop: <see cref="Stop"/> joins
    /// the thread, a read of nothing ends the stream from the thread, and <see cref="PlaybackStopped"/>
    /// is posted to the synchronisation context captured when the player was made, or raised
    /// on the render thread when there is none, as NAudio's players do. It renders faster than
    /// real time.</summary>
    private sealed class JoiningPlayer : IWavePlayer
    {
        private const int BlockBytes = 4410 * 2 * sizeof(float); // 100 ms of stereo float

        private readonly SynchronizationContext? _context = SynchronizationContext.Current;
        private IWaveProvider? _provider;
        private Thread? _thread;
        private volatile PlaybackState _state = PlaybackState.Stopped;
        private long _blocks;

        public bool Disposed { get; private set; }

        /// <summary>The device this player was opened on.</summary>
        public string? Device { get; set; }

        public long Blocks => Interlocked.Read(ref _blocks);

        private volatile Exception? _failure;

        /// <summary>The device fails under the player, as an unplugged one does: the render
        /// thread ends with the error, delivered as a stop would be.</summary>
        public void Fail(Exception error) => _failure = error;

        public float Volume { get; set; } = 1f;

        public PlaybackState PlaybackState => _state;

        public WaveFormat OutputWaveFormat => _provider?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

        public event EventHandler<StoppedEventArgs>? PlaybackStopped;

        public void Init(IWaveProvider waveProvider) => _provider = waveProvider;

        public void Play()
        {
            if (_state == PlaybackState.Stopped)
            {
                _thread?.Join(); // a stream that ended on its own may still be signing off
                _thread = new Thread(Render) { IsBackground = true, Name = "fake player" };
                _state = PlaybackState.Playing;
                _thread.Start();
            }
            else
            {
                _state = PlaybackState.Playing;
            }
        }

        public void Pause() => _state = PlaybackState.Paused;

        /// <summary>WASAPI's stop: joins the render thread, so from that thread it would never
        /// return.</summary>
        public void Stop()
        {
            if (_state == PlaybackState.Stopped)
            {
                return;
            }
            _state = PlaybackState.Stopped;
            _thread?.Join();
            _thread = null;
        }

        public void Dispose()
        {
            Stop();
            Disposed = true;
        }

        private void Render()
        {
            Exception? error = null;
            var buffer = new byte[BlockBytes];
            try
            {
                while (_state != PlaybackState.Stopped)
                {
                    if (_failure is { } failure)
                    {
                        _state = PlaybackState.Stopped;
                        throw failure;
                    }
                    if (_state == PlaybackState.Playing)
                    {
                        if (_provider!.Read(buffer, 0, buffer.Length) == 0)
                        {
                            _state = PlaybackState.Stopped; // end of stream
                            break;
                        }
                        Interlocked.Increment(ref _blocks);
                    }
                    Thread.Sleep(5);
                }
            }
            catch (Exception e)
            {
                error = e;
            }
            finally
            {
                var handler = PlaybackStopped;
                if (handler is not null)
                {
                    if (_context is null)
                    {
                        handler(this, new StoppedEventArgs(error));
                    }
                    else
                    {
                        _context.Post(_ => handler(this, new StoppedEventArgs(error)), null);
                    }
                }
            }
        }
    }
}
