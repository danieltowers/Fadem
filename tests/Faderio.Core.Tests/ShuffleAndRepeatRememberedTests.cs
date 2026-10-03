using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Library issue 04 (D11): shuffle and repeat are the library's, kept in its settings.json.
/// <c>SetShuffle</c> and <c>SetRepeat</c> save as a side effect, in force once on disk; a library
/// that opens brings its own before anything enters the queue; <c>ChangeSettings</c> keeps the
/// ones in force.
/// </summary>
public sealed class ShuffleAndRepeatRememberedTests : IDisposable
{
    private readonly string _root = Path.Combine(TestTemp.Root, "remembered", Guid.NewGuid().ToString("N"));

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

    private static (PlaybackEngine Engine, RecordingSink Sink) EngineOn(Library? library, int? rngSeed = null)
    {
        var sink = new RecordingSink();
        var rng = rngSeed is null ? null : new Random(rngSeed.Value);
        var engine = new PlaybackEngine(new FakeAudioOutput(), library, sink, rng) { StartAddWorker = work => work(), StartPrepareWorker = work => work() };
        return (engine, sink);
    }

    // ── Persisted on toggle ──────────────────────────────────────────────────────────────

    [Fact]
    public void SetShuffle_SavesTheLibrarysSettingsFile_AndTellsNothing()
    {
        using var tc = TestEngine.Create();

        tc.Engine.SetShuffle(true);

        Assert.True(tc.View.Shuffle);
        Assert.True(tc.View.Settings.Shuffle);
        Assert.True(new SettingsStore(tc.SettingsPath).Settings.Shuffle);
        Assert.Empty(tc.Notices); // the toggle shows it; a notice per click would be noise
    }

    [Fact]
    public void SetRepeat_SavesTheLibrarysSettingsFile()
    {
        using var tc = TestEngine.Create();

        tc.Engine.SetRepeat(RepeatMode.One);

        Assert.Equal(RepeatMode.One, tc.View.Repeat);
        Assert.Equal(RepeatMode.One, new SettingsStore(tc.SettingsPath).Settings.Repeat);
        Assert.Contains("\"repeat\": \"One\"", File.ReadAllText(tc.SettingsPath));
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void TheToggles_LeaveTheListeningSettingsAlone()
    {
        var listening = new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade, DefaultCrossfadeSeconds = 9, ReplayGainEnabled = false,
        };
        using var tc = TestEngine.Create(listening);

        tc.Engine.SetShuffle(true);
        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.Equal(listening with { Shuffle = true, Repeat = RepeatMode.All }, new SettingsStore(tc.SettingsPath).Settings);
    }

    [Fact]
    public void ASettingsFileThatCannotBeWritten_LeavesTheToggleAsItWas_AndSaysWhy()
    {
        using var tc = TestEngine.Create();
        Directory.CreateDirectory(tc.SettingsPath); // a folder where the file should be: every write fails
        tc.Add(TestAudio.WriteToneWav("a.wav", 2, 1000), TestAudio.WriteToneWav("b.wav", 2, 2000));
        var queue = tc.View.Queue.Select(r => r.Id).ToList();

        tc.Engine.SetShuffle(true);
        tc.Engine.SetRepeat(RepeatMode.All);

        Assert.False(tc.View.Shuffle);
        Assert.Equal(RepeatMode.Off, tc.View.Repeat);
        Assert.Equal(queue, tc.View.Queue.Select(r => r.Id));
        Assert.Collection(
            tc.Notices,
            n => Assert.StartsWith("Not saved: the playlist to reopen at launch. The settings file could not be written", n),
            n => Assert.StartsWith("Not saved: shuffle. The settings file could not be written", n),
            n => Assert.StartsWith("Not saved: repeat. The settings file could not be written", n));
    }

    [Fact]
    public void WithNoLibraryOpen_TheTogglesAreNotSaved_AndNothingChanges()
    {
        var (engine, sink) = EngineOn(null);
        using var _ = engine;

        engine.SetShuffle(true);
        engine.SetRepeat(RepeatMode.All);

        Assert.False(engine.View.Shuffle);
        Assert.Equal(RepeatMode.Off, engine.View.Repeat);
        Assert.Equal(["Not saved: shuffle. No library is open.", "Not saved: repeat. No library is open."], sink.Notices);
    }

    [Fact]
    public void ChangeSettings_KeepsTheShuffleAndRepeatInForce()
    {
        using var tc = TestEngine.Create();
        tc.Engine.SetShuffle(true);
        tc.Engine.SetRepeat(RepeatMode.All);

        // The Settings… dialog does not show them, so what it sends has the defaults.
        tc.Engine.ChangeSettings(new PlayerSettings { ReplayGainEnabled = false });

        Assert.True(tc.View.Shuffle);
        Assert.Equal(RepeatMode.All, tc.View.Repeat);
        var saved = new SettingsStore(tc.SettingsPath).Settings;
        Assert.True(saved.Shuffle);
        Assert.Equal(RepeatMode.All, saved.Repeat);
        Assert.False(saved.ReplayGainEnabled);
    }

    [Fact]
    public void ChangeSettings_OnlyDifferingInShuffleOrRepeat_IsNoChange()
    {
        using var tc = TestEngine.Create();
        tc.Engine.SetShuffle(true);

        tc.Engine.ChangeSettings(new PlayerSettings());

        Assert.True(tc.View.Shuffle);
        Assert.Empty(tc.Notices);
    }

    // ── Restored on open ─────────────────────────────────────────────────────────────────

    [Fact]
    public void ShuffleOn_QuitAndRelaunch_IsStillOn()
    {
        var folder = Folder("Library");
        var (first, _) = EngineOn(Library.Open(folder));
        first.SetShuffle(true);
        first.SetRepeat(RepeatMode.One);
        first.Dispose();

        var (relaunched, sink) = EngineOn(Library.Open(folder));
        using var _ = relaunched;

        Assert.True(relaunched.View.Shuffle);
        Assert.Equal(RepeatMode.One, relaunched.View.Repeat);
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void ARestoredShuffle_OrdersTheQueueFromTheStart_AndOffRestoresTheAddedOrder()
    {
        using var tc = TestEngine.Create(new PlayerSettings { Shuffle = true }, rngSeed: 7);
        var paths = Enumerable.Range(0, 8)
            .Select(i => TestAudio.WriteToneWav($"restored-{i}.wav", 1, 500 + 100 * i))
            .ToArray();

        tc.Add(paths);

        Assert.True(tc.View.Shuffle);
        Assert.Equal(paths.Order(), tc.View.Queue.Select(r => r.Track.FilePath).Order());
        Assert.NotEqual(paths, tc.View.Queue.Select(r => r.Track.FilePath));

        tc.Engine.SetShuffle(false);

        Assert.Equal(paths, tc.View.Queue.Select(r => r.Track.FilePath));
    }

    [Fact]
    public void ARestoredRepeat_DecidesWhatFollowsTheLastRow()
    {
        using var tc = TestEngine.Create(new PlayerSettings { Repeat = RepeatMode.All });
        var rows = tc.Add(TestAudio.WriteToneWav("wrap-a.wav", 2, 1000), TestAudio.WriteToneWav("wrap-b.wav", 2, 2000));

        tc.Engine.PlayRow(rows[1].Id);

        Assert.Equal(new Adjacency(rows[1].Track, rows[0].Track), tc.View.NextAdjacency);
    }

    [Fact]
    public void AHandEditedRepeatNoVersionKnows_LoadsAsOff()
    {
        var folder = Folder("Edited");
        var library = Library.Open(folder);
        File.WriteAllText(library.SettingsPath, """{ "schemaVersion": 1, "shuffle": true, "repeat": 7 }""");

        var settings = new SettingsStore(library.SettingsPath).Settings;

        Assert.True(settings.Shuffle);
        Assert.Equal(RepeatMode.Off, settings.Repeat);
    }

    // ── Independent per library ──────────────────────────────────────────────────────────

    [Fact]
    public void RepeatAllInA_SwitchToB_IsBsOwn_BackToA_IsRepeatAll()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, sink) = EngineOn(Library.Open(a));
        using var _ = engine;

        engine.SetRepeat(RepeatMode.All);
        engine.SetShuffle(true);
        engine.OpenLibrary(b);

        Assert.Equal(RepeatMode.Off, engine.View.Repeat);
        Assert.False(engine.View.Shuffle);

        engine.OpenLibrary(a);

        Assert.Equal(RepeatMode.All, engine.View.Repeat);
        Assert.True(engine.View.Shuffle);
        Assert.Equal(new PlayerSettings(), new SettingsStore(Library.Open(b).SettingsPath).Settings); // B was never written
        Assert.Empty(sink.Notices);
    }

    [Fact]
    public void ALibraryThatCannotBeOpened_BringsTheDefaults()
    {
        var (engine, _) = EngineOn(Library.Open(Folder("Good")));
        using var __ = engine;
        engine.SetRepeat(RepeatMode.One);

        engine.OpenLibrary(Path.Combine(_root, "Gone")); // no such folder

        Assert.Equal(RepeatMode.Off, engine.View.Repeat);
        Assert.False(engine.View.Shuffle);
    }
}
