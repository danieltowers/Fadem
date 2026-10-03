using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// One library holder at a time (phase 5 issue 15): the engine that opens a library for a host
/// holds it, through <c>.faderio/host.lock</c> kept open, and another host's engine opens it
/// unavailable with a notice naming the holder. Letting go, by opening another library or by
/// disposing, frees it; a lock file nobody holds open does not block.
/// </summary>
public sealed class LibraryHolderTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _root = Path.Combine(TestTemp.Root, "holder", Guid.NewGuid().ToString("N"));

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

    private static string LockFile(string folder) => Path.Combine(folder, Library.StoreFolderName, "host.lock");

    /// <summary>The lock file's text, read beside its holder's open handle.</summary>
    private static string Holder(string folder)
    {
        using var stream = new FileStream(LockFile(folder), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Trim();
    }

    private static (PlaybackEngine Engine, RecordingSink Sink, FakeAudioOutput Output) EngineOn(string folder, string? host)
    {
        var sink = new RecordingSink();
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, Library.Open(folder), sink, host: host) { StartAddWorker = work => work(), StartPrepareWorker = work => work() };
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

    [Fact]
    public void AHeldLibrary_OpensUnavailableForAnotherHost_NamingTheHolder_AndStillPlays()
    {
        var folder = Folder("Shared");
        var a = TestAudio.WriteToneWav("held-a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("held-b.wav", 3, 2000);
        using var faderio = EngineOn(folder, "faderio").Engine;
        Assert.Equal("faderio", Holder(folder));

        var (musicBee, sink, output) = EngineOn(folder, "MusicBee");
        using var _ = musicBee;

        Assert.Equal(new LibraryView(folder, "Shared", IsAvailable: false), musicBee.View.Library);
        Assert.Equal(
            "The library Shared could not be opened (it is open in faderio). Nothing is saved until a " +
            "library opens; what is queued still plays.",
            Assert.Single(sink.Notices));
        Assert.Equal("faderio", Holder(folder)); // the holder's, still

        var adjacency = AddPair(musicBee, a, b);
        musicBee.Play();
        output.Advance(Rate);
        Assert.Equal(Transport.Playing, musicBee.View.Transport);
        Assert.True(new RenderedAudio(output.Blocks).ToneAmplitude(1000, Rate) > 0.3, "the queue plays");

        musicBee.Author(adjacency, Crossfade());

        Assert.Equal($"Not saved: {adjacency.Name}. The library Shared could not be opened (it is open in faderio).", sink.Notices.Last());
        Assert.True(faderio.View.Library!.IsAvailable, "the holder keeps its library");
    }

    [Fact]
    public void DisposingTheHolder_FreesTheLibrary_ForTheOtherHostsNextOpen()
    {
        var folder = Folder("Shared");
        var faderio = EngineOn(folder, "faderio").Engine;
        var (musicBee, sink, _) = EngineOn(folder, "MusicBee");
        using var _ = musicBee;
        Assert.False(musicBee.View.Library!.IsAvailable);

        faderio.Dispose();

        Assert.False(File.Exists(LockFile(folder)), "the lock file goes with the holder");
        var before = sink.Notices.Count;
        musicBee.OpenLibrary(folder);
        Assert.True(musicBee.View.Library!.IsAvailable);
        Assert.Equal(before, sink.Notices.Count);
        Assert.Equal("MusicBee", Holder(folder));
    }

    [Fact]
    public void OpeningAnotherLibrary_LetsGoOfTheOneLeft()
    {
        var first = Folder("First");
        var second = Folder("Second");
        using var faderio = EngineOn(first, "faderio").Engine;

        faderio.OpenLibrary(second);

        Assert.False(File.Exists(LockFile(first)));
        Assert.Equal("faderio", Holder(second));
        using var musicBee = EngineOn(first, "MusicBee").Engine;
        Assert.True(musicBee.View.Library!.IsAvailable);
    }

    [Fact]
    public void TheSameFolderOpenedAgain_ByItsHolder_StaysOpen()
    {
        var folder = Folder("Shared");
        var (faderio, sink, _) = EngineOn(folder, "faderio");
        using var _ = faderio;

        faderio.OpenLibrary(folder);

        Assert.True(faderio.View.Library!.IsAvailable);
        Assert.Empty(sink.Notices);
        Assert.Equal("faderio", Holder(folder));
    }

    [Fact]
    public void AnEngineWithNoHostName_AndLibraryOpenAlone_HoldNothing()
    {
        var folder = Folder("Shared");
        using var renderer = EngineOn(folder, host: null).Engine;
        var reading = Library.Open(folder);
        Assert.True(reading.IsAvailable);
        Assert.False(File.Exists(LockFile(folder)));

        using var faderio = EngineOn(folder, "faderio").Engine;

        Assert.True(faderio.View.Library!.IsAvailable);
        Assert.True(Library.Open(folder).IsAvailable, "a reader is not refused by a holder");
    }

    [Fact]
    public void ALockFileNobodyHolds_DoesNotBlock_AndBecomesThisHosts()
    {
        var folder = Folder("Copied");
        Directory.CreateDirectory(Path.Combine(folder, Library.StoreFolderName));
        File.WriteAllText(LockFile(folder), "MusicBee\n");

        var (faderio, sink, _) = EngineOn(folder, "faderio");
        using var _ = faderio;

        Assert.True(faderio.View.Library!.IsAvailable);
        Assert.Empty(sink.Notices);
        Assert.Equal("faderio", Holder(folder));
    }

    [Fact]
    public void AHolderWhoseNameCannotBeRead_IsAnotherHost()
    {
        var folder = Folder("Shared");
        Library.Open(folder); // makes .faderio
        using var someoneElse = new FileStream(LockFile(folder), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

        var (faderio, sink, _) = EngineOn(folder, "faderio");
        using var _ = faderio;

        Assert.False(faderio.View.Library!.IsAvailable);
        Assert.StartsWith("The library Shared could not be opened (it is open in another host).", Assert.Single(sink.Notices));
    }

    [Fact]
    public void AFolderThatCannotBeOpened_IsNotHeld()
    {
        var folder = Path.Combine(_root, "never made");

        using var faderio = EngineOn(folder, "faderio").Engine;

        Assert.False(faderio.View.Library!.IsAvailable);
        Assert.False(Directory.Exists(folder));
    }
}
