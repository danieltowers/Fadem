using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Tests;

namespace Faderio.Shell.Tests;

/// <summary>
/// Library chooser issue 02: the shared chooser lists the known libraries as the engine gives
/// them, Open and Remove go by what each row is, and Remove asks for the list again. Built
/// headless, never shown: its buttons are pressed by raising their Click.
/// </summary>
public sealed class LibraryChooserTests : IDisposable
{
    private readonly string _root = Path.Combine(TestTemp.Root, "chooser", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static KnownLibrary Row(string folder, bool open = false, bool exists = true) =>
        new(folder, LibraryFolders.Name(folder), open, exists);

    private static LibraryChooser Chooser(
        List<KnownLibrary> rows, List<string>? forgotten = null, Func<string?, string?>? chooseFolder = null, string? selected = null) =>
        new(() => rows.ToList(),
            folder =>
            {
                forgotten?.Add(folder);
                rows.RemoveAll(r => r.Folder == folder);
            },
            chooseFolder ?? (_ => null),
            selected);

    private static void Select(LibraryChooser chooser, int index)
    {
        chooser._list.SelectedIndices.Clear();
        chooser._list.Items[index].Selected = true;
    }

    [Fact]
    public void TheRows_AreTheKnownLibraries_TheOpenOneMarkedAndSelected_AGoneOneGreyed() => RunSta(() =>
    {
        using var chooser = Chooser([Row(@"D:\Gigs"), Row(@"C:\Music\Home", open: true), Row(@"E:\Old", exists: false)]);

        var items = chooser._list.Items.Cast<ListViewItem>().ToList();
        Assert.Equal(new[] { "Gigs", "Home", "Old" }, items.Select(i => i.Text));
        Assert.Equal(new[] { @"D:\Gigs", @"C:\Music\Home", @"E:\Old" }, items.Select(i => i.SubItems[1].Text));
        Assert.Equal(new[] { "", "open", "not found" }, items.Select(i => i.SubItems[2].Text));
        Assert.Equal(Faderio.Shell.Theme.Palette.Studio.Dim, items[2].ForeColor); // dim, in the palette (studio theme issue 06)
        Assert.Equal(1, chooser._list.SelectedIndices[0]);
        Assert.StartsWith("Removing a library", chooser._hint.Text);
    });

    [Fact]
    public void Open_ChoosesTheSelectedLibrary_ButNotOneThatIsGone() => RunSta(() =>
    {
        using var chooser = Chooser([Row(@"D:\Gigs", open: true), Row(@"C:\Music\Home"), Row(@"E:\Old", exists: false)]);

        Select(chooser, 2);
        Assert.False(chooser._open.Enabled);
        Assert.True(chooser._remove.Enabled);

        Select(chooser, 1);
        Assert.True(chooser._open.Enabled);
        Click(chooser._open);

        Assert.Equal(@"C:\Music\Home", chooser.Chosen);
        Assert.Equal(DialogResult.OK, chooser.DialogResult);
    });

    [Fact]
    public void Remove_ForgetsTheSelectedLibrary_AsksForTheListAgain_AndSelectsTheNext() => RunSta(() =>
    {
        var forgotten = new List<string>();
        using var chooser = Chooser([Row(@"D:\Gigs", open: true), Row(@"C:\Music\Home"), Row(@"E:\Old")], forgotten);

        Select(chooser, 0);
        Assert.False(chooser._remove.Enabled); // the open library
        Select(chooser, 1);
        Click(chooser._remove);

        Assert.Equal(new[] { @"C:\Music\Home" }, forgotten);
        Assert.Equal(new[] { "Gigs", "Old" }, chooser._list.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal(1, chooser._list.SelectedIndices[0]);
        Assert.Null(chooser.Chosen); // still choosing
    });

    [Fact]
    public void AddFolder_ChoosesThePickedFolder_StartingFromTheSelectedOne() => RunSta(() =>
    {
        string? startedIn = null;
        using var chooser = Chooser([Row(@"D:\Gigs", open: true)], chooseFolder: start =>
        {
            startedIn = start;
            return @"F:\New";
        });

        Click(chooser._add);

        Assert.Equal(@"D:\Gigs", startedIn);
        Assert.Equal(@"F:\New", chooser.Chosen);
    });

    [Fact]
    public void AddFolder_Cancelled_ChoosesNothing() => RunSta(() =>
    {
        using var chooser = Chooser([Row(@"D:\Gigs")]);

        Click(chooser._add);

        Assert.Null(chooser.Chosen);
        Assert.Equal(DialogResult.None, chooser.DialogResult);
    });

    [Fact]
    public void NoKnownLibrary_SaysToAddAFolder() => RunSta(() =>
    {
        using var chooser = Chooser([]);

        Assert.Empty(chooser._list.Items);
        Assert.StartsWith("No library has opened yet.", chooser._hint.Text);
        Assert.False(chooser._open.Enabled);
        Assert.False(chooser._remove.Enabled);
    });

    [Fact]
    public void APendingChoice_IsSelected_OverTheOpenLibrary() => RunSta(() =>
    {
        using var chooser = Chooser([Row(@"D:\Gigs", open: true), Row(@"C:\Music\Home")], selected: @"c:\music\home\");

        Assert.Equal(1, chooser._list.SelectedIndices[0]);
    });

    /// <summary>Against a real engine: what it knows is what the chooser lists, and Remove is
    /// the engine's own call, written to the machine's settings.</summary>
    [Fact]
    public void OnARealEngine_TheChooserListsAndForgetsThroughIt() => RunSta(() =>
    {
        var a = Path.Combine(_root, "A");
        var b = Path.Combine(_root, "B");
        Directory.CreateDirectory(a);
        Directory.CreateDirectory(b);
        Directory.CreateDirectory(Path.Combine(_root, "AppData"));
        var machinePath = Path.Combine(_root, "AppData", "settings.json");
        using var engine = new PlaybackEngine(new FakeAudioOutput(), Library.Open(a), new RecordingSink(),
            machine: new MachineSettingsStore(machinePath)) { StartAddWorker = work => work(), StartPrepareWorker = work => work() };
        engine.OpenLibrary(b);
        using var chooser = new LibraryChooser(engine.KnownLibraries, engine.ForgetLibrary, _ => null);

        Assert.Equal(new[] { "B", "A" }, chooser._list.Items.Cast<ListViewItem>().Select(i => i.Text));
        Select(chooser, 1);
        Click(chooser._remove);

        Assert.Equal(new[] { "B" }, chooser._list.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal(new[] { b }, new MachineSettingsStore(machinePath).Settings.KnownLibraries);
    });

    /// <summary>Raises a button's Click as a press would, where an unshown button's own
    /// <c>PerformClick</c> does nothing.</summary>
    private static void Click(ButtonBase button)
    {
        Assert.True(button.Enabled, $"{button.Text} is disabled");
        typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(button, new object[] { EventArgs.Empty });
    }

    private static void RunSta(Action body) => QueueSectionsTests.RunSta(body);
}
