using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Library issue 02 (D12, ADR 0011): Open library… switches worlds in place (stop, clear, reload,
/// nothing crosses), the library that opens is remembered in the machine's settings, and a
/// launch opens the command line's library, else the remembered one, else asks.
/// </summary>
public sealed class SwitchingLibrariesTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _root = Path.Combine(TestTemp.Root, "switch", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    /// <summary>Where the machine's settings.json lives: AppData's stand-in.</summary>
    private string MachinePath => Path.Combine(Folder("AppData"), "settings.json");

    private static (PlaybackEngine Engine, RecordingSink Sink, FakeAudioOutput Output) EngineOn(
        Library? library, MachineSettingsStore? machine = null)
    {
        var sink = new RecordingSink();
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, library, sink, machine: machine) { StartAddWorker = work => work() };
        return (engine, sink, output);
    }

    private static Adjacency AddPair(PlaybackEngine engine, string a, string b)
    {
        var report = engine.Add(new[] { a, b }).Result;
        return new Adjacency(report.Added[0].Track, report.Added[1].Track);
    }

    private static TransitionSpec Crossfade() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    private static int At(double seconds) => (int)(seconds * Rate);

    // ── OpenLibrary ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpenLibrary_WhilePlaying_StopsClearsAndSwapsTheStore()
    {
        var first = Folder("First");
        var second = Folder("Second");
        var (engine, sink, output) = EngineOn(Library.Open(first));
        using var _ = engine;
        var adjacency = AddPair(engine, TestAudio.WriteToneWav("switch-a.wav", 4, 1000), TestAudio.WriteToneWav("switch-b.wav", 4, 2000));
        engine.Author(adjacency, Crossfade());
        engine.Play();
        output.Advance(At(0.5));
        Assert.Equal(Transport.Playing, engine.View.Transport);

        engine.OpenLibrary(second);

        Assert.Equal(Transport.Stopped, engine.View.Transport);
        Assert.Empty(engine.View.Queue);
        Assert.Null(engine.View.Attended);
        Assert.Equal(new LibraryView(second, "Second", IsAvailable: true), engine.View.Library);
        Assert.Null(engine.Transition(adjacency).Global); // the second library's store is empty

        engine.Author(adjacency, Crossfade());

        Assert.Equal($"Saved {adjacency.Name}.", sink.Notices[^1]);
        Assert.NotNull(Library.Open(second).Transitions!.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath));
        Assert.True(File.Exists(Path.Combine(second, ".faderio", "transitions.json")));
    }

    [Fact]
    public void OpenLibrary_EndsAPreview()
    {
        var (engine, _, output) = EngineOn(Library.Open(Folder("First")));
        using var _ = engine;
        var adjacency = AddPair(engine, TestAudio.WriteToneWav("preview-a.wav", 4, 1000), TestAudio.WriteToneWav("preview-b.wav", 4, 2000));
        engine.Preview(adjacency, Crossfade());
        output.Advance(At(0.2));
        Assert.NotNull(engine.View.Preview);

        engine.OpenLibrary(Folder("Second"));

        Assert.Null(engine.View.Preview);
        Assert.Equal(Transport.Stopped, engine.View.Transport);
    }

    [Fact]
    public void OpenLibrary_SwapsTheSettings_ForTheNewLibrarys()
    {
        var first = Folder("First");
        Library.Open(first).Settings!.Save(new PlayerSettings { DefaultCrossfadeSeconds = 8 });
        var (engine, _, _) = EngineOn(Library.Open(first));
        using var _ = engine;
        Assert.Equal(8, engine.View.Settings.DefaultCrossfadeSeconds);

        engine.OpenLibrary(Folder("Second"));
        Assert.Equal(new PlayerSettings(), engine.View.Settings);

        engine.OpenLibrary(first);
        Assert.Equal(8, engine.View.Settings.DefaultCrossfadeSeconds);
    }

    [Fact]
    public void OpenLibrary_OnAFolderThatIsGone_LeavesNoLibraryOpen_AndSaysSo()
    {
        var first = Folder("First");
        var gone = Path.Combine(_root, "Gone");
        var (engine, sink, output) = EngineOn(Library.Open(first));
        using var _ = engine;
        var a = TestAudio.WriteToneWav("gone-a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("gone-b.wav", 4, 2000);
        var adjacency = AddPair(engine, a, b);
        engine.Author(adjacency, Crossfade());

        engine.OpenLibrary(gone);

        Assert.Equal(new LibraryView(gone, "Gone", IsAvailable: false), engine.View.Library);
        Assert.Equal(
            "The library Gone could not be opened (the folder does not exist). Nothing is saved until a " +
            "library opens; what is queued still plays.",
            sink.Notices[^1]);
        Assert.Null(engine.Transition(adjacency).Global); // the first library's store is not kept open
        engine.Author(adjacency, Crossfade());
        Assert.Equal($"Not saved: {adjacency.Name}. The library Gone could not be opened (the folder does not exist).", sink.Notices[^1]);

        AddPair(engine, a, b);
        engine.Play();
        output.Advance(At(0.5));
        Assert.Equal(Transport.Playing, engine.View.Transport);
    }

    // ── Remembering ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheLibraryThatOpens_IsRemembered_AndOneThatDoesNot_IsNot()
    {
        var first = Folder("First");
        var second = Folder("Second");
        var (engine, sink, _) = EngineOn(Library.Open(first), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        Assert.Equal(first, new MachineSettingsStore(MachinePath).Settings.Library);

        engine.OpenLibrary(second);
        Assert.Equal(second, new MachineSettingsStore(MachinePath).Settings.Library);

        engine.OpenLibrary(Path.Combine(_root, "Gone"));
        Assert.Equal(second, new MachineSettingsStore(MachinePath).Settings.Library);
        Assert.Single(sink.Notices); // the one saying Gone could not be opened
    }

    [Fact]
    public void AnEngineWithoutTheMachinesSettings_RemembersNothing()
    {
        var (engine, _, _) = EngineOn(Library.Open(Folder("First")));
        using var _ = engine;

        engine.OpenLibrary(Folder("Second"));

        Assert.False(File.Exists(MachinePath));
    }

    [Fact]
    public void TheMachinesOldSettingsFile_IsReadWithoutANotice_AndRewrittenWithTheLibrary()
    {
        // Until library issue 01 this file was the player's settings (D13: not migrated, not even
        // its output mode, which is the machine's again since library issue 03).
        File.WriteAllText(MachinePath, """{ "schemaVersion": 1, "outputMode": "WaveOut", "defaultCrossfadeSeconds": 9 }""");
        var machine = new MachineSettingsStore(MachinePath);
        Assert.Null(machine.LoadNotice);
        Assert.Null(machine.Settings.Library);

        var first = Folder("First");
        var (engine, sink, _) = EngineOn(Library.Open(first), machine);
        using var _ = engine;

        Assert.Empty(sink.Notices);
        var json = File.ReadAllText(MachinePath);
        Assert.Contains("\"library\"", json);
        Assert.Contains("\"outputMode\": \"WasapiShared\"", json);
        Assert.DoesNotContain("defaultCrossfadeSeconds", json);
        Assert.Equal(first, new MachineSettingsStore(MachinePath).Settings.Library);
    }

    [Fact]
    public void ACorruptMachineSettingsFile_IsMovedAside_Visibly()
    {
        File.WriteAllText(MachinePath, "{ not json");

        var (engine, sink, _) = EngineOn(null, new MachineSettingsStore(MachinePath));
        using var _ = engine;

        var notice = Assert.Single(sink.Notices);
        Assert.StartsWith("The machine settings file is corrupt (", notice);
        Assert.Contains("moved aside to settings.json.corrupt", notice);
        Assert.True(File.Exists(MachinePath + ".corrupt"));
    }

    [Fact]
    public void ChooseAuthorQuitRelaunch_OpensTheSameLibrary_WithItsTransition()
    {
        var chosen = Folder("Chosen");
        var a = TestAudio.WriteDcWav("relaunch-a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("relaunch-b.wav", 3, 0.2);

        // First launch: nothing remembered, so the user is asked, and chooses.
        var launch = LibraryLaunch.Resolve(null, new MachineSettingsStore(MachinePath).Settings.Library, Directory.Exists);
        Assert.True(launch.MustAsk);
        var (first, _, _) = EngineOn(null, new MachineSettingsStore(MachinePath));
        first.OpenLibrary(chosen);
        var adjacency = AddPair(first, a, b);
        first.Author(adjacency, Crossfade());
        first.Dispose();

        var relaunch = LibraryLaunch.Resolve(null, new MachineSettingsStore(MachinePath).Settings.Library, Directory.Exists);
        Assert.Equal(chosen, relaunch.Folder);
        var (second, _, _) = EngineOn(Library.Open(relaunch.Folder!), new MachineSettingsStore(MachinePath));
        using var _ = second;
        Assert.Equal("Chosen", second.View.Library!.Name);
        Assert.NotNull(second.Transition(adjacency));
    }

    [Fact]
    public void NoLibraryChosen_RefusesAuthoring_AndPlays()
    {
        var (engine, sink, output) = EngineOn(null, new MachineSettingsStore(MachinePath));
        using var _ = engine;
        var adjacency = AddPair(engine, TestAudio.WriteToneWav("none-a.wav", 3, 1000), TestAudio.WriteToneWav("none-b.wav", 3, 2000));

        engine.Author(adjacency, Crossfade());
        engine.Play();
        output.Advance(At(0.5));

        Assert.Null(engine.View.Library);
        Assert.Equal($"Not saved: {adjacency.Name}. No library is open.", Assert.Single(sink.Notices));
        Assert.Equal(Transport.Playing, engine.View.Transport);
        Assert.False(File.Exists(MachinePath));
    }

    // ── The launch ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\Arg", @"C:\Remembered", true, @"C:\Arg")]    // the command line wins
    [InlineData(@"C:\Arg", @"C:\Remembered", false, @"C:\Arg")]   // gone or not: a notice, not a prompt
    [InlineData(@"C:\Arg", null, false, @"C:\Arg")]
    [InlineData(null, @"C:\Remembered", true, @"C:\Remembered")]
    [InlineData("", @"C:\Remembered", true, @"C:\Remembered")]
    [InlineData(null, @"C:\Remembered", false, null)]             // deleted since: ask
    [InlineData(null, null, true, null)]                          // the first launch: ask
    [InlineData(null, "", true, null)]
    public void TheLaunch_OpensTheArgument_ElseTheRememberedFolderIfItExists_ElseAsks(
        string? argument, string? remembered, bool exists, string? expected)
    {
        var launch = LibraryLaunch.Resolve(argument, remembered, _ => exists);

        Assert.Equal(expected, launch.Folder);
        Assert.Equal(expected is null, launch.MustAsk);
    }
}
