using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Tests;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// One library holder at a time, seen from the plugin (phase 5 issue 15): a library standalone
/// faderio holds is refused at start with a notice naming faderio, and once faderio lets go,
/// Configure and Save on the same folder open it.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void ALibraryFaderioHolds_IsRefusedAtStart_NamingIt_AndOpensOnceFaderioLetsGo()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        PlayerView view;
        using (new PlaybackEngine(new FakeAudioOutput(), Library.Open(LibraryFolder), new RecordingSink(), host: "faderio"))
        {
            Start();
            _ui.Pump();

            view = _plugin.Host!.Engine.View;
            Assert.Equal(new LibraryView(LibraryFolder, "library", IsAvailable: false), view.Library);
            Assert.Empty(view.Queue);
            Assert.StartsWith("The library library could not be opened (it is open in faderio).", Assert.Single(_ui.Messages));
        } // faderio lets go

        Configure(chosen: LibraryFolder);
        _plugin.SaveSettings();

        view = Launched();
        Assert.Equal(new LibraryView(LibraryFolder, "library", IsAvailable: true), view.Library);
        Assert.Equal(new[] { a, b }, view.Queue.Select(r => r.Track.FilePath));
        _ui.Pump();
        Assert.Single(_ui.Messages); // nothing new
    }
}
