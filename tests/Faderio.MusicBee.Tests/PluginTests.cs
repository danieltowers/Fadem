using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Tests;
using MusicBeePlugin;
using static MusicBeePlugin.Plugin;
using NAudio.Wave;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// The plugin through its own entry point (phase 5 issue 03, seam 2): MusicBee's API is a
/// recording fake, the output is the fake output, and the library is authored through Core's
/// own store in TestTemp. What is asserted is what comes out: the calls made to MusicBee, the
/// frames the output pulls, and the message boxes posted to MusicBee's UI thread.
/// </summary>
public sealed partial class PluginTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private static readonly TransitionSpec Crossfade = new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    private readonly string _dir = Path.Combine(TestTemp.Root, "musicbee", Guid.NewGuid().ToString("N"));
    private readonly FakeMusicBee _musicBee;
    private readonly RecordingContext _ui = new();
    private readonly FakeAudioOutput _output = new();
    private readonly Plugin _plugin;
    private readonly SynchronizationContext? _before = SynchronizationContext.Current;

    public PluginTests()
    {
        _musicBee = new FakeMusicBee(Path.Combine(_dir, "MusicBee storage"));
        _plugin = new Plugin((_, _) => _output);
    }

    private string LibraryFolder => Path.Combine(_dir, "library");

    public void Dispose()
    {
        _plugin.Close(PluginCloseReason.MusicBeeClosing);
        SynchronizationContext.SetSynchronizationContext(_before);
    }

    [Fact]
    public void Initialise_OpensTheLibraryItsSettingsName_AndItsOpenPlaylist()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);

        Start();

        var view = Launched();
        Assert.Equal(new LibraryView(LibraryFolder, "library", IsAvailable: true), view.Library);
        Assert.Equal("Untitled", view.OpenPlaylist);
        Assert.Equal(new[] { a, b }, view.Queue.Select(r => r.Track.FilePath));
        Assert.Empty(_ui.Messages);
    }

    [Fact]
    public void Initialise_AddsToolsFaderioWithPlayAndStop()
    {
        ChooseLibrary(LibraryFolder);

        Start();

        Assert.Equal(new[] { "MB_AddMenuItem mnuTools/faderio" }, _musicBee.Calls);
        Assert.Equal(new[] { "Play", "Stop" },
            _musicBee.Menus["mnuTools/faderio"].DropDownItems.Cast<System.Windows.Forms.ToolStripItem>().Select(i => i.Text));
    }

    [Fact]
    public void Play_StopsMusicBeesPlayer_BeforeTheOutputPullsAFrame()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        var outputAtPlayerStop = new List<(Transport, int)>();
        _musicBee.PlayerStopped = () => outputAtPlayerStop.Add((_output.State, _output.ReadCallCount));

        _musicBee.Choose("Play");
        _output.Advance(4410);

        Assert.Equal(new[] { (Transport.Stopped, 0) }, outputAtPlayerStop);
        Assert.Equal("Player_Stop", _musicBee.Calls.Last());
        Assert.Equal(Transport.Playing, _output.State);
        Assert.True(_output.FramesRendered > 0);
    }

    [Fact]
    public void AcrossTheAuthoredBoundary_TheOutputPullsWhatTheEngineRendersForIt()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        var view = Launched();

        _musicBee.Choose("Play");
        _output.Advance(44100 * 8); // both tracks, to the end
        var pulled = _output.Blocks.SelectMany(block => block).ToArray();

        // Mid-crossfade, at 2 s, both tones sound: the transition was not played as a hard cut.
        var heard = new RenderedAudio(_output.Blocks);
        Assert.InRange(heard.ToneAmplitude(440, 88200), 0.1, 0.4);
        Assert.InRange(heard.ToneAmplitude(660, 88200), 0.1, 0.4);

        // The render starts at the outgoing track's start: its pre-roll reaches back past it.
        var wav = Path.Combine(_dir, "boundary.wav");
        var report = Renderer.RenderBoundary(
            new Adjacency(view.Queue[0].Track, view.Queue[1].Track), Library.Open(LibraryFolder).Transitions, wav,
            settings: view.Settings, playlist: view.OpenPlaylist);
        Assert.Equal(1500, report.FadeOutPointInFile.Milliseconds);
        var rendered = ReadSamples(wav);

        Assert.True(rendered.Length >= 44100 * 2 * 3, $"the render holds only {rendered.Length / 2} frames");
        Assert.True(pulled.Length >= rendered.Length, $"the output pulled only {pulled.Length / 2} frames");
        Assert.Equal(rendered, pulled.Take(rendered.Length).ToArray());
    }

    [Fact]
    public void Stop_StopsTheOutput_AndClose_DisposesIt()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _musicBee.Choose("Play");
        _output.Advance(4410);

        _musicBee.Choose("Stop");

        Assert.Equal(Transport.Stopped, _output.State);
        Assert.False(_output.Disposed);

        _plugin.Close(PluginCloseReason.MusicBeeClosing);

        Assert.True(_output.Disposed);
        Assert.Null(_plugin.Host);
    }

    [Fact]
    public void NoLibraryChosen_IsAMessage_AndPlayPlaysNothing()
    {
        Start();

        Assert.Contains(_ui.Messages, m => m.StartsWith("No faderio library is chosen", StringComparison.Ordinal));
        _musicBee.Choose("Play");
        _output.Advance(4410);
        Assert.Equal(0, _output.FramesRendered);
    }

    [Fact]
    public void AGoneLibrary_IsAMessage_AndPlayPlaysNothing()
    {
        ChooseLibrary(Path.Combine(_dir, "gone"));

        Start();

        var message = Assert.Single(_ui.Messages);
        Assert.Contains("gone", message);
        Assert.Contains("does not exist", message);
        _musicBee.Choose("Play");
        _output.Advance(4410);
        Assert.Equal(0, _output.FramesRendered);
    }

    /// <summary>Two tones in the library's open playlist, with a crossfade authored between them,
    /// through Core's own store: as standalone faderio would have left it.</summary>
    private (string A, string B) AuthorLibrary() => AuthorLibrary(LibraryFolder, "musicbee", 440, 660);

    private static (string A, string B) AuthorLibrary(string folder, string name, int hzA, int hzB)
    {
        Directory.CreateDirectory(folder);
        var a = TestAudio.WriteToneWav($"{name}-a.wav", 4, hzA);
        var b = TestAudio.WriteToneWav($"{name}-b.wav", 4, hzB);
        var library = Library.Open(folder);
        using (var authoring = new PlaybackEngine(new FakeAudioOutput(), library, new RecordingSink()))
        {
            authoring.Add(new[] { a, b }).Wait();
        }
        library.Transitions!.Upsert(a, b, Crossfade);
        return (a, b);
    }

    /// <summary>The plugin's settings file, as its dialog leaves it.</summary>
    private void ChooseLibrary(string folder, AudioOutputMode mode = AudioOutputMode.WasapiShared)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        new MachineSettingsStore(SettingsFile).Save(new MachineSettings { Library = folder, OutputMode = mode });
    }

    /// <summary>The plugin's own machine settings, in MusicBee's persistent storage.</summary>
    private string SettingsFile => Path.Combine(_musicBee.StoragePath, "faderio", "settings.json");

    /// <summary>MusicBee loads the plugin: on its UI thread, whose context the plugin posts to.</summary>
    private void Start() => Start(_plugin);

    private void Start(Plugin plugin)
    {
        SynchronizationContext.SetSynchronizationContext(_ui);
        var about = _musicBee.Initialise(plugin);
        Assert.Equal(PluginType.General, about.Type);
    }

    /// <summary>Waits for the open playlist to finish loading, as a listener would before
    /// choosing Play: its rows are read in the background.</summary>
    private PlayerView Launched() => Launched(_plugin, LibraryFolder);

    /// <summary>Waits for <paramref name="folder"/>'s open playlist to load in
    /// <paramref name="plugin"/>.</summary>
    private static PlayerView Launched(Plugin plugin, string folder)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (true)
        {
            var view = plugin.Host!.Engine.View;
            if (view.Library?.Path == folder && view.Adding is null && view.Queue.Count > 0)
            {
                return view;
            }
            Assert.True(DateTime.UtcNow < deadline, "The open playlist never loaded.");
            Thread.Sleep(10);
        }
    }

    private static float[] ReadSamples(string wav)
    {
        using var reader = new WaveFileReader(wav);
        var provider = reader.ToSampleProvider();
        var samples = new List<float>();
        var buffer = new float[8820];
        int n;
        while ((n = provider.Read(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.Take(n));
        }
        return samples.ToArray();
    }
}
