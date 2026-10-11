using Fadem.Core.Persistence;
using Fadem.Core.Playback;
using Fadem.Core.Tests;

namespace Fadem.MusicBee.Tests;

/// <summary>
/// One library holder at a time, seen from the plugin (phase 5 issue 15): a library standalone
/// Fadem holds is refused at start with a notice naming Fadem, and once Fadem lets go,
/// Configure and Save on the same folder open it.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void ALibraryFademHolds_IsRefusedAtStart_NamingIt_AndOpensOnceFademLetsGo()
    {
        var (a, b) = AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        PlayerView view;
        using (new PlaybackEngine(new FakeAudioOutput(), Library.Open(LibraryFolder), new RecordingSink(), host: "Fadem"))
        {
            Start();
            _ui.Pump();

            view = _plugin.Host!.Engine.View;
            Assert.Equal(new LibraryView(LibraryFolder, "library", IsAvailable: false), view.Library);
            Assert.Empty(view.Queue);
            Assert.StartsWith("The library library could not be opened (it is open in Fadem).", Assert.Single(_ui.Messages));
        } // Fadem lets go

        Configure(chosen: LibraryFolder);
        _plugin.SaveSettings();

        view = Launched();
        Assert.Equal(new LibraryView(LibraryFolder, "library", IsAvailable: true), view.Library);
        Assert.Equal(new[] { a, b }, view.Queue.Select(r => r.Track.FilePath));
        _ui.Pump();
        Assert.Single(_ui.Messages); // nothing new
    }
}
