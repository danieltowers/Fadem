using Fadem.Core.Persistence;
using Fadem.Core.Playback;
using Fadem.Core.Settings;

namespace Fadem.Core.Tests;

/// <summary>
/// Library chooser issue 01: the machine's settings know every library that has opened, most
/// recently opened first, and the user can take one off the list. The chooser lists them from
/// one query, the open one marked and each looked for on disk.
/// </summary>
public sealed class KnownLibrariesTests : IDisposable
{
    private readonly string _root = Path.Combine(TestTemp.Root, "known", Guid.NewGuid().ToString("N"));

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

    private static (PlaybackEngine Engine, RecordingSink Sink) EngineOn(Library? library, MachineSettingsStore? machine, string? host = null)
    {
        var sink = new RecordingSink();
        var engine = new PlaybackEngine(new FakeAudioOutput(), library, sink, machine: machine, host: host)
        {
            StartAddWorker = work => work(), StartPrepareWorker = work => work(),
        };
        return (engine, sink);
    }

    private IReadOnlyList<string> Saved => new MachineSettingsStore(MachinePath).Settings.KnownLibraries;

    // ── The file ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AFileFromBeforeTheList_KnowsItsLibrary_AndOneWithNeither_KnowsNone()
    {
        File.WriteAllText(MachinePath, """{ "schemaVersion": 2, "library": "D:\\Music" }""");
        Assert.Equal(new[] { @"D:\Music" }, Saved);

        File.WriteAllText(MachinePath, """{ "schemaVersion": 2 }""");
        Assert.Empty(Saved);
    }

    [Fact]
    public void TheList_DropsBlanksRepeatsAndTheLibraryItself()
    {
        File.WriteAllText(MachinePath, """
            { "schemaVersion": 2, "library": "D:\\Music",
              "otherLibraries": ["d:\\music\\", " ", "E:\\Gigs", "e:\\gigs", null, "F:\\Old"] }
            """);

        Assert.Equal(new[] { @"D:\Music", @"E:\Gigs", @"F:\Old" }, Saved);
    }

    [Fact]
    public void TheList_RoundTrips_AndSettingsReadBackEqualThoseSaved()
    {
        var saved = new MachineSettings { Library = @"D:\Music", OtherLibraries = [@"E:\Gigs", @"F:\Old"] };
        new MachineSettingsStore(MachinePath).Save(saved);

        Assert.Equal(saved, new MachineSettingsStore(MachinePath).Settings);
        Assert.Contains("\"otherLibraries\"", File.ReadAllText(MachinePath));
    }

    // ── Opening ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OpeningALibrary_PutsItFirst_AndOneOpenedAgainMovesUp()
    {
        var a = Folder("A");
        var b = Folder("B");
        var c = Folder("C");
        var (engine, _) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.OpenLibrary(b);
        engine.OpenLibrary(c);
        Assert.Equal(new[] { c, b, a }, Saved);

        engine.OpenLibrary(a);
        Assert.Equal(new[] { a, c, b }, Saved);
        Assert.Equal(a, new MachineSettingsStore(MachinePath).Settings.Library);
    }

    [Fact]
    public void AFolderThatCannotBeOpened_IsNotListed()
    {
        var a = Folder("A");
        var (engine, sink) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.OpenLibrary(Path.Combine(_root, "Gone"));

        Assert.Equal(new[] { a }, Saved);
        Assert.Single(sink.Notices); // the one saying Gone could not be opened
    }

    [Fact]
    public void ALibraryAnotherHostHolds_IsNotListed()
    {
        var a = Folder("A");
        var held = Folder("Held");
        using var musicBee = Library.Open(held);
        musicBee.Hold("MusicBee");
        var (engine, _) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath), host: "Fadem");
        using var _ = engine;

        engine.OpenLibrary(held);

        Assert.Equal(new[] { a }, Saved);
    }

    [Fact]
    public void ChangingTheOutputMode_KeepsTheList()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, _) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        engine.OpenLibrary(b);

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WaveOut });

        Assert.Equal(new[] { b, a }, Saved);
        Assert.Equal(AudioOutputMode.WaveOut, new MachineSettingsStore(MachinePath).Settings.OutputMode);
    }

    // ── ForgetLibrary ────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForgetLibrary_TakesItOffTheList_AndLeavesItsFolderAlone()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, sink) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        engine.OpenLibrary(b);

        engine.ForgetLibrary(a.ToUpperInvariant() + Path.DirectorySeparatorChar); // as the user's path may differ

        Assert.Equal(new[] { b }, Saved);
        Assert.Equal(new[] { b }, engine.KnownLibraries().Select(k => k.Folder));
        Assert.True(Directory.Exists(Path.Combine(a, Library.StoreFolderName)));
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void ForgetLibrary_OnTheOpenLibraryOrAnUnknownFolder_ChangesNothing()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, sink) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        engine.OpenLibrary(b);
        var before = File.ReadAllText(MachinePath);

        engine.ForgetLibrary(b);
        engine.ForgetLibrary(Folder("Never"));

        Assert.Equal(before, File.ReadAllText(MachinePath));
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void ForgetLibrary_OnTheOneALaunchReopens_LeavesALaunchToAsk()
    {
        // Gone, so nothing opened: the remembered library is not the open one.
        var gone = Path.Combine(_root, "Gone");
        new MachineSettingsStore(MachinePath).Save(new MachineSettings { Library = gone, OtherLibraries = [@"E:\Gigs"] });
        var (engine, _) = EngineOn(null, new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.ForgetLibrary(gone);

        var settings = new MachineSettingsStore(MachinePath).Settings;
        Assert.Null(settings.Library);
        Assert.Equal(new[] { @"E:\Gigs" }, settings.KnownLibraries);
        Assert.True(LibraryLaunch.Resolve(null, settings.Library, Directory.Exists).MustAsk);
    }

    // ── KnownLibraries ───────────────────────────────────────────────────────────────────

    [Fact]
    public void KnownLibraries_MarksTheOpenOne_AndOnesWhoseFolderIsGone()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, _) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        engine.OpenLibrary(b);
        Directory.Delete(a, recursive: true);

        Assert.Equal(
            new[] { new KnownLibrary(b, "B", IsOpen: true, Exists: true), new KnownLibrary(a, "A", IsOpen: false, Exists: false) },
            engine.KnownLibraries());

        Directory.CreateDirectory(a); // looked for again on each call
        Assert.True(engine.KnownLibraries()[1].Exists);
    }

    [Fact]
    public void KnownLibraries_WithoutAMachineSettingsFile_IsTheOpenLibraryAlone()
    {
        var a = Folder("A");
        var (engine, _) = EngineOn(Library.Open(a), machine: null);
        using var _ = engine;

        Assert.Equal(new[] { new KnownLibrary(a, "A", IsOpen: true, Exists: true) }, engine.KnownLibraries());
    }

    [Fact]
    public void TheList_BeforeThereIsAnEngine_IsTheSameQuery()
    {
        var settings = new MachineSettings { Library = Path.Combine(_root, "Gone"), OtherLibraries = [Folder("B")] };

        var listed = KnownLibrary.List(settings, open: null, Directory.Exists);

        Assert.Equal(new[] { false, true }, listed.Select(k => k.Exists));
        Assert.All(listed, k => Assert.False(k.IsOpen));
        Assert.Equal(new[] { "Gone", "B" }, listed.Select(k => k.Name));
    }
}
