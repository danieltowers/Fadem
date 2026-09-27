using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Library issue 03 (D11): the settings split between the machine's settings.json (the output
/// mode, the library to reopen) and the library's (what is heard). Each has its own file and its
/// own command; a switch of library swaps the library's half and leaves the machine's alone.
/// </summary>
public sealed class MachineSettingsTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "faderio-machine", Guid.NewGuid().ToString("N"));

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
        Library? library, MachineSettingsStore? machine)
    {
        var sink = new RecordingSink();
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, library, sink, machine: machine) { StartAddWorker = work => work() };
        return (engine, sink, output);
    }

    private static PlayerSettings Crossfade(int seconds) =>
        new() { UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade, DefaultCrossfadeSeconds = seconds };

    // ── The two files ────────────────────────────────────────────────────────────────────

    [Fact]
    public void TheTwoStores_RoundTripIndependently()
    {
        var library = Library.Open(Folder("Music"));
        var machine = new MachineSettingsStore(MachinePath);
        var machineSettings = new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive, Library = library.Folder };
        var librarySettings = Crossfade(12) with { ReplayGainEnabled = false };

        machine.Save(machineSettings);
        library.Settings!.Save(librarySettings);

        Assert.Equal(machineSettings, new MachineSettingsStore(MachinePath).Settings);
        Assert.Equal(librarySettings, new SettingsStore(library.SettingsPath).Settings);
        var machineJson = File.ReadAllText(MachinePath);
        Assert.Contains("\"outputMode\": \"WasapiExclusive\"", machineJson);
        Assert.DoesNotContain("defaultCrossfadeSeconds", machineJson);
        Assert.DoesNotContain("outputMode", File.ReadAllText(library.SettingsPath));
    }

    [Fact]
    public void AMissingMachineFile_LoadsTheDefaults_WithoutANotice()
    {
        var machine = new MachineSettingsStore(MachinePath);

        Assert.Null(machine.LoadNotice);
        Assert.Equal(new MachineSettings(), machine.Settings);
        Assert.Equal(AudioOutputMode.WasapiShared, machine.Settings.OutputMode);
        Assert.Null(machine.Settings.Library);
    }

    [Fact]
    public void ACorruptMachineFile_IsMovedAside_AndTheDefaultsLoad()
    {
        File.WriteAllText(MachinePath, "{ not json");

        var machine = new MachineSettingsStore(MachinePath);

        Assert.Equal(new MachineSettings(), machine.Settings);
        Assert.StartsWith("The machine settings file is corrupt (", machine.LoadNotice);
        Assert.Contains("moved aside to settings.json.corrupt", machine.LoadNotice);
        Assert.Equal("{ not json", File.ReadAllText(MachinePath + ".corrupt")); // still recoverable
        Assert.False(File.Exists(MachinePath));
    }

    [Fact]
    public void ANewerMachineFile_LoadsTheDefaults_AndIsLeftAlone()
    {
        const string newer = "{\"schemaVersion\": 9, \"outputMode\": \"Asio\"}";
        File.WriteAllText(MachinePath, newer);

        var machine = new MachineSettingsStore(MachinePath);

        Assert.Equal(new MachineSettings(), machine.Settings);
        Assert.Contains("newer faderio (schema 9)", machine.LoadNotice);
        Assert.Equal(newer, File.ReadAllText(MachinePath));
    }

    [Theory]
    [InlineData("{\"schemaVersion\": 1, \"outputMode\": \"WaveOut\", \"library\": \"D:\\\\Music\"}")] // before issue 03 (D13)
    [InlineData("{\"schemaVersion\": 2, \"outputMode\": 7, \"library\": \"D:\\\\Music\"}")]           // a mode no version knows
    public void AnOutputModeNotToBeTrusted_IsTheDefault_AndTheLibraryStays(string written)
    {
        File.WriteAllText(MachinePath, written);

        var machine = new MachineSettingsStore(MachinePath);

        Assert.Null(machine.LoadNotice);
        Assert.Equal(new MachineSettings { Library = @"D:\Music" }, machine.Settings);
    }

    // ── ChangeMachineSettings ────────────────────────────────────────────────────────────

    [Fact]
    public void ChangeMachineSettings_SavesTheOutputModeForTheNextLaunch_AndTheViewCarriesIt()
    {
        var library = Library.Open(Folder("Music"));
        var (engine, sink, output) = EngineOn(library, new MachineSettingsStore(MachinePath));
        using var _ = engine;
        engine.Add(new[] { TestAudio.WriteToneWav("machine-a.wav", 3, 1000) }).Wait();
        engine.Play();
        output.Advance(Rate);

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive });

        Assert.Equal(AudioOutputMode.WasapiExclusive, engine.View.MachineSettings.OutputMode);
        Assert.Equal(AudioOutputMode.WasapiExclusive, new MachineSettingsStore(MachinePath).Settings.OutputMode);
        Assert.Equal("Saved machine settings.", Assert.Single(sink.Notices));
        // Nothing changes now: the engine rate is fixed at launch (ADR 0008).
        Assert.Equal(Rate, engine.SampleRate);
        Assert.Equal(Transport.Playing, engine.View.Transport);
        // The library's file is not the machine's: it only remembers the open playlist.
        Assert.DoesNotContain("outputMode", File.ReadAllText(library.SettingsPath), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ChangeMachineSettings_KeepsTheRememberedLibrary_WhateverItIsGiven()
    {
        var music = Folder("Music");
        var (engine, _, _) = EngineOn(Library.Open(music), new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WaveOut, Library = @"C:\Elsewhere" });

        Assert.Equal(music, engine.View.MachineSettings.Library);
        Assert.Equal(new MachineSettings { OutputMode = AudioOutputMode.WaveOut, Library = music }, new MachineSettingsStore(MachinePath).Settings);
    }

    [Fact]
    public void ChangeMachineSettings_ToWhatIsSaved_IsNoChange_AndNoNotice()
    {
        var (engine, sink, _) = EngineOn(null, new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.ChangeMachineSettings(new MachineSettings());

        Assert.Empty(sink.Notices);
        Assert.False(File.Exists(MachinePath));
    }

    [Fact]
    public void ChangeMachineSettings_WithNoMachineFile_SavesNothing_AndSaysSo()
    {
        var (engine, sink, _) = EngineOn(Library.Open(Folder("Music")), machine: null);
        using var _ = engine;

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WaveOut });

        Assert.Equal(new MachineSettings(), engine.View.MachineSettings);
        Assert.Equal("Not saved: machine settings. This run keeps no machine settings file.", Assert.Single(sink.Notices));
    }

    [Fact]
    public void ChangeMachineSettings_ThatCannotBeWritten_ChangesNothing_AndSaysWhy()
    {
        var machine = new MachineSettingsStore(MachinePath);
        Directory.CreateDirectory(MachinePath + ".tmp"); // the atomic write's temp file cannot be made
        var (engine, sink, _) = EngineOn(null, machine);
        using var _ = engine;

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WaveOut });

        Assert.Equal(AudioOutputMode.WasapiShared, engine.View.MachineSettings.OutputMode);
        Assert.StartsWith("Not saved: machine settings. The machine settings file could not be written: ", Assert.Single(sink.Notices));
    }

    [Fact]
    public void ChangeSettings_LeavesTheMachineFileAlone()
    {
        var (engine, sink, _) = EngineOn(Library.Open(Folder("Music")), new MachineSettingsStore(MachinePath));
        using var _ = engine;
        var remembered = File.ReadAllText(MachinePath);

        engine.ChangeSettings(Crossfade(9));

        Assert.Equal("Saved settings.", Assert.Single(sink.Notices));
        Assert.Equal(remembered, File.ReadAllText(MachinePath));
    }

    // ── Switching library (the acceptance) ───────────────────────────────────────────────

    [Fact]
    public void TheDefaultCrossfade_IsEachLibrarysOwn_AcrossASwitchAndBack()
    {
        var a = Folder("A");
        var b = Folder("B");
        var (engine, _, _) = EngineOn(Library.Open(a), new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.ChangeSettings(Crossfade(12));
        engine.OpenLibrary(b);
        Assert.Equal(new PlayerSettings(), engine.View.Settings);

        engine.OpenLibrary(a);
        Assert.Equal(Crossfade(12), engine.View.Settings);
        Assert.Equal(new PlayerSettings(), new SettingsStore(Library.Open(b).SettingsPath).Settings);
    }

    [Fact]
    public void TheOutputMode_IsTheMachines_AndStaysAcrossASwitch()
    {
        var b = Folder("B");
        var (engine, _, _) = EngineOn(Library.Open(Folder("A")), new MachineSettingsStore(MachinePath));
        using var _ = engine;

        engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive });
        engine.OpenLibrary(b);

        Assert.Equal(AudioOutputMode.WasapiExclusive, engine.View.MachineSettings.OutputMode);
        Assert.Equal(new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive, Library = b }, new MachineSettingsStore(MachinePath).Settings);
    }
}
