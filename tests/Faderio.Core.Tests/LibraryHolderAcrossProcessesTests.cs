using Faderio.Core.Persistence;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// The crash proof of one library holder at a time (phase 5 issue 15): a host in another
/// process holds the library, this one is refused naming it, and once that process is killed,
/// as a crash would end it, the library opens here. net8.0 only: the child starts through
/// <c>dotnet</c>.
/// </summary>
public sealed class LibraryHolderAcrossProcessesTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestTemp.Root, "holder-process", Guid.NewGuid().ToString("N"), "Shared");

    public LibraryHolderAcrossProcessesTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_folder)!, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void AHostInAnotherProcess_HoldsTheLibrary_UntilItIsKilled()
    {
        using var musicBee = ChildProcess.Start("hold", _folder, "MusicBee");
        Assert.Equal(new KeyValuePair<string, string>("held", "True"), musicBee.ReadLine());

        var sink = new RecordingSink();
        using (var refused = new PlaybackEngine(new FakeAudioOutput(), Library.Open(_folder), sink, host: "faderio"))
        {
            Assert.False(refused.View.Library!.IsAvailable);
            Assert.StartsWith("The library Shared could not be opened (it is open in MusicBee).", Assert.Single(sink.Notices));
        }

        musicBee.Kill();

        var after = new RecordingSink();
        using var faderio = new PlaybackEngine(new FakeAudioOutput(), Library.Open(_folder), after, host: "faderio");
        Assert.True(faderio.View.Library!.IsAvailable);
        Assert.Empty(after.Notices);
    }
}
