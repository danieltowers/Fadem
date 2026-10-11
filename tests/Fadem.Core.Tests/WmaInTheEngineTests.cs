using Fadem.Core.Domain;
using Fadem.Core.Persistence;
using Fadem.Core.Playback;

namespace Fadem.Core.Tests;

/// <summary>Issue 12 through the engine: WMA is added, from a folder too, with the length its
/// index counted, kept in the library's store for every later open, and plays.</summary>
public sealed class WmaInTheEngineTests : IDisposable
{
    private const int Rate = 44100;
    private readonly string _root = Path.Combine(TestTemp.Root, "wma", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    private string Folder(string name)
    {
        var folder = Path.Combine(_root, name);
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static (PlaybackEngine Engine, FakeAudioOutput Output) EngineOn(Library library)
    {
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, library, new RecordingSink())
        {
            StartAddWorker = work => work(),
            StartPrepareWorker = work => work(),
        };
        return (engine, output);
    }

    [Fact]
    public void AFolderAdd_QueuesItsWma_WithTheDecodedLength_AndKeepsTheIndexInTheLibrary()
    {
        var music = Folder("Music");
        File.Copy(Fixture("noise-windows-44100-2.wma"), Path.Combine(music, "01 - Noise.wma"));
        var library = Library.Open(Folder("Library"));
        var (engine, _) = EngineOn(library);

        var report = engine.Add(new[] { music }).Result;

        var row = Assert.Single(report.Added);
        Assert.Empty(report.Rejected);
        Assert.Equal(Length.FromFrames(90112, Rate), row.Track.Length); // the codec tail included (ADR 0010)
        Assert.Single(Directory.GetFiles(Path.Combine(library.StoreFolder, "wma-index"), "*.wmaindex"));
        engine.Dispose();
    }

    [Fact]
    public void AWma_Plays()
    {
        var (engine, output) = EngineOn(Library.Open(Folder("Library")));
        engine.Add(new[] { Fixture("tone-windows-44100-2.wma") }).Wait();

        engine.Play();
        output.Advance(Rate);

        var left = Enumerable.Range(Rate / 2, Rate / 10).Select(f => (float)new RenderedAudio(output.Blocks).Sample(f, 0)).ToList();
        Assert.True(TestAudio.Purity(left, 0, Rate / 10, 1000, Rate) > 0.99);
        engine.Dispose();
    }

    [Fact]
    public void WmaLossless_At96kHz24Bit_IsQueued_WithTheSourcesLength()
    {
        var (engine, _) = EngineOn(Library.Open(Folder("Library")));

        var report = engine.Add(new[] { Fixture("tone-windowslossless-96000-24-2.wma") }).Result;

        var row = Assert.Single(report.Added);
        Assert.Equal(Length.FromFrames(192000, 96000), row.Track.Length);
        engine.Dispose();
    }

    [Fact]
    public void WmaPro_IsRejected_NamingItsCodec()
    {
        var (engine, _) = EngineOn(Library.Open(Folder("Library")));

        var report = engine.Add(new[] { Fixture("tone-windowspro-44100-2.wma") }).Result;

        Assert.Empty(report.Added);
        var rejected = Assert.Single(report.Rejected);
        Assert.Equal(RejectedProperty.Codec, rejected.Property);
        engine.Dispose();
    }
}
