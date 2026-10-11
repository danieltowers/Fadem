using System.Windows.Forms;
using Fadem.Core.Settings;
using Fadem.Shell;

namespace Fadem.MusicBee.Tests;

/// <summary>
/// Library chooser issue 03: Configure's Choose… shows the shared chooser on the plugin's own
/// known libraries, the choice waiting for MusicBee's Save as before, and a removal written at
/// once. The chooser is built for real and never shown: the test presses its buttons.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void ChooseShowsTheKnownLibraries_AndTheOneChosen_OpensAtSave_FirstInTheList() => RunSta(() =>
    {
        AuthorLibrary();
        AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.Host!.Engine.OpenLibrary(OtherFolder); // known now, and open
        _plugin.Host.Engine.OpenLibrary(LibraryFolder);

        string[]? listed = null;
        int? selected = null;
        _plugin.Host.ShowLibraryChooser = (chooser, _) =>
        {
            listed = chooser._list.Items.Cast<ListViewItem>().Select(i => i.SubItems[1].Text).ToArray();
            selected = chooser._list.SelectedIndices[0];
            chooser._list.SelectedIndices.Clear();
            chooser._list.Items[1].Selected = true;
            Click(chooser._open);
            return chooser.DialogResult == DialogResult.OK;
        };
        string? line = null;
        _plugin.ShowSettings = dialog =>
        {
            Click(dialog._choose);
            line = dialog._library.Text;
            return true;
        };
        Assert.True(_plugin.Configure(IntPtr.Zero));

        Assert.Equal(new[] { LibraryFolder, OtherFolder }, listed);
        Assert.Equal(0, selected); // the open library
        Assert.Equal(OtherFolder, line);
        Assert.Equal(LibraryFolder, _plugin.Host.Engine.View.Library!.Path); // waits for Save

        _plugin.SaveSettings();

        Launched(_plugin, OtherFolder);
        Assert.Equal(new[] { OtherFolder, LibraryFolder }, new MachineSettingsStore(SettingsFile).Settings.KnownLibraries);
    });

    [Fact]
    public void ChooseAgain_SelectsTheChoiceWaitingForSave() => RunSta(() =>
    {
        AuthorLibrary();
        AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.Host!.Engine.OpenLibrary(OtherFolder);
        _plugin.Host.Engine.OpenLibrary(LibraryFolder);
        Configure(chosen: OtherFolder);

        string? selected = null;
        _plugin.Host.ShowLibraryChooser = (chooser, _) =>
        {
            selected = chooser._list.SelectedItems[0].SubItems[1].Text;
            return false;
        };
        Configure(read: dialog => Click(dialog._choose));

        Assert.Equal(OtherFolder, selected);
    });

    [Fact]
    public void RemoveFromList_IsWrittenAtOnce_EvenWhenPreferencesAreCancelled() => RunSta(() =>
    {
        AuthorLibrary();
        AuthorLibrary(OtherFolder, "musicbee-other", 550, 770);
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.Host!.Engine.OpenLibrary(OtherFolder);
        _plugin.Host.Engine.OpenLibrary(LibraryFolder);

        _plugin.Host.ShowLibraryChooser = (chooser, _) =>
        {
            chooser._list.SelectedIndices.Clear();
            chooser._list.Items[1].Selected = true;
            Click(chooser._remove);
            return false;
        };
        Configure(ok: false, read: dialog => Click(dialog._choose));

        Assert.Equal(new[] { LibraryFolder }, new MachineSettingsStore(SettingsFile).Settings.KnownLibraries);
        Assert.True(Directory.Exists(OtherFolder));
    });
}
