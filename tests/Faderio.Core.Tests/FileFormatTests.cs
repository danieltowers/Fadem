using System.Text;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// A library moves freely between the hosts (phase 5 issue 01): the store, a library's
/// settings, the machine's settings and a playlist are the same bytes whichever target wrote
/// them. Each file is checked in under <c>Fixtures/formats</c>, and both targets run these: each
/// writes the checked-in bytes exactly, and reads the checked-in file and writes it back
/// unchanged. So what net48 writes, net8.0 reads, and the reverse. The paths and names carry
/// <c>†</c> and <c>⭐️</c>, as the user's library folders do.
/// </summary>
public sealed class FileFormatTests : IDisposable
{
    private const string Outgoing = @"C:\Music\Example Band † Second Album\01 Long Opener.flac";
    private const string Incoming = @"C:\Music\⭐️ Favourites\02 Short Interlude.mp3";
    private const string Playlist = "Mix † ⭐️";

    private readonly string _dir = Path.Combine(TestTemp.Root, "formats", Guid.NewGuid().ToString("N"));

    public FileFormatTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static string Fixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "formats", name);
        Assert.True(File.Exists(path), $"fixture formats/{name} is missing");
        return path;
    }

    /// <summary>The fixture copied into the test's folder, where it may be written back.</summary>
    private string Copy(string name)
    {
        var path = Path.Combine(_dir, name);
        File.Copy(Fixture(name), path);
        return path;
    }

    /// <summary>Byte for byte; compared as text first, so a failure shows where they differ.</summary>
    private static void AssertSameBytes(string expectedPath, string actualPath)
    {
        var expected = File.ReadAllBytes(expectedPath);
        var actual = File.ReadAllBytes(actualPath);
        Assert.Equal(Encoding.UTF8.GetString(expected), Encoding.UTF8.GetString(actual));
        Assert.Equal(expected, actual);
    }

    // ── The store ──────────────────────────────────────────────────────────────────────────

    private static readonly TransitionSpec Authored = new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(12.3), FromEnd: true),
        FadeInPoint = new FadePointSpec(TimeSpan.FromMilliseconds(4507), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(2.5),
        FadeInDuration = TimeSpan.FromTicks(17_500_001),
        Curve = FadeCurveKind.Linear,
    };

    private static readonly TransitionSpec Override = new() { FadeOutDuration = TimeSpan.FromSeconds(8) };

    [Fact]
    public void TheStore_IsWrittenAsTheCheckedInFile()
    {
        var path = Path.Combine(_dir, "transitions.json");
        var store = new JsonTransitionStore(path);

        store.Upsert(Outgoing, Incoming, Authored);
        store.UpsertOverride(Playlist, Outgoing, Incoming, Override);

        AssertSameBytes(Fixture("transitions.json"), path);
    }

    [Fact]
    public void TheCheckedInStore_ReadsBack_AndIsWrittenBackUnchanged()
    {
        var path = Copy("transitions.json");

        var store = new JsonTransitionStore(path);
        Assert.Equal(Authored, store.Get(Outgoing, Incoming));
        Assert.Equal(Override, store.GetOverride(Playlist, Outgoing, Incoming));
        store.Upsert(Outgoing, Incoming, Authored);

        AssertSameBytes(Fixture("transitions.json"), path);
    }

    // ── A library's settings ───────────────────────────────────────────────────────────────

    private static readonly PlayerSettings Listening = new()
    {
        UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
        DefaultCrossfadeSeconds = 7,
        BlankDurationSeconds = 3,
        DefaultCurve = FadeCurveKind.SCurve,
        ReplayGainMode = ReplayGainMode.Off,
        Shuffle = true,
        Repeat = RepeatMode.All,
        EditorFollowsPlayback = true,
        LastPlaylist = Playlist,
    };

    [Fact]
    public void ALibrarysSettings_AreWrittenAsTheCheckedInFile()
    {
        var path = Path.Combine(_dir, "settings.json");

        new SettingsStore(path).Save(Listening);

        AssertSameBytes(Fixture("settings.json"), path);
    }

    [Fact]
    public void TheCheckedInLibrarySettings_ReadBack_AndAreWrittenBackUnchanged()
    {
        var path = Copy("settings.json");

        var store = new SettingsStore(path);
        Assert.Null(store.LoadNotice);
        Assert.Equal(Listening, store.Settings);
        store.Save(store.Settings);

        AssertSameBytes(Fixture("settings.json"), path);
    }

    // ── The machine's settings ─────────────────────────────────────────────────────────────

    private static readonly MachineSettings Machine = new()
    {
        OutputMode = AudioOutputMode.WasapiExclusive,
        Library = @"C:\Music\⭐️ Favourites",
        OtherLibraries = [@"D:\Gigs", @"E:\faderio-old"],
        OutputDevice = "Speakers (C-Media(R) Audio)",
    };

    [Fact]
    public void TheMachinesSettings_AreWrittenAsTheCheckedInFile()
    {
        var path = Path.Combine(_dir, "machine.json");

        new MachineSettingsStore(path).Save(Machine);

        AssertSameBytes(Fixture("machine.json"), path);
    }

    [Fact]
    public void TheCheckedInMachineSettings_ReadBack_AndAreWrittenBackUnchanged()
    {
        var path = Copy("machine.json");

        var store = new MachineSettingsStore(path);
        Assert.Null(store.LoadNotice);
        Assert.Equal(Machine, store.Settings);
        store.Save(store.Settings);

        AssertSameBytes(Fixture("machine.json"), path);
    }

    // ── A playlist ─────────────────────────────────────────────────────────────────────────

    private static readonly Track[] Tracks =
    {
        new() { FilePath = Outgoing, Artist = "Example Band", Title = "Long Opener", Length = Length.FromFrames(44100 * 509, 44100) },
        new() { FilePath = Incoming, Title = "Short Interlude", Length = Length.FromFrames(48000 * 97 + 24000, 48000) },
    };

    [Fact]
    public void APlaylist_IsWrittenAsTheCheckedInFile()
    {
        var path = Path.Combine(_dir, "playlist.m3u8");

        M3UPlaylist.Write(path, Tracks);

        AssertSameBytes(Fixture("playlist.m3u8"), path);
    }

    [Fact]
    public void TheCheckedInPlaylist_ReadsBack_AndIsWrittenBackUnchanged()
    {
        var path = Copy("playlist.m3u8");

        Assert.Equal(new[] { Outgoing, Incoming }, M3UPlaylist.Read(path));
        M3UPlaylist.Write(path, Tracks);

        AssertSameBytes(Fixture("playlist.m3u8"), path);
    }
}
