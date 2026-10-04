using System.Runtime.ExceptionServices;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>Phase 6 issue 09: the queue list shows the playlist's sections, the same in both
/// hosts, on each host's runtime: each section a group of the list headed by its name, its rows
/// striped in its colour, collapsed as the engine says, and a collapse by the user told to the
/// engine.</summary>
public sealed class QueueSectionsTests
{
    [Fact]
    public void EachSection_IsAGroupHeadedByItsName_AndRowsAboveTheFirst_AreInAnUnheadedOne() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 4);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[3].Id, "End");

        sections.Apply(tc.View);

        Assert.True(list.ShowGroups);
        Assert.Equal(new[] { "", "Intro", "End" }, list.Groups.Cast<ListViewGroup>().Select(g => g.Header));
        Assert.Equal(new[] { "", "Intro", "Intro", "End" }, list.Items.Cast<ListViewItem>().Select(i => i.Group?.Header));
    });

    [Fact]
    public void RowsInASection_AreStripedInItsColour_AndOthersAreNot() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
        tc.Engine.RecolourSection(tc.View.Queue[1].Section!.Id, 4);

        sections.Apply(tc.View);

        Assert.Equal(new[] { -1, 4, 4 }, list.Items.Cast<ListViewItem>().Select(i => i.ImageIndex));
        Assert.NotNull(list.SmallImageList);
    });

    [Fact]
    public void EachSectionsHeader_ShowsASwatchOfItsColour() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 2);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[1].Id, "End");
        tc.Engine.RecolourSection(tc.View.Queue[1].Section!.Id, 6);

        sections.Apply(tc.View);

        Assert.Equal(new[] { 0, 6 }, list.Groups.Cast<ListViewGroup>().Select(g => ListGroups.TitleImage(list, g)));
    });

    [Fact]
    public void WithNoSections_TheListIsAsBefore_UngroupedAndUnstriped() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 2);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        sections.Apply(tc.View);

        tc.Engine.RemoveSection(tc.View.Queue[0].Section!.Id);
        sections.Apply(tc.View);

        Assert.False(list.ShowGroups);
        Assert.Null(list.SmallImageList);
        Assert.All(list.Items.Cast<ListViewItem>(), i => Assert.Null(i.Group));
    });

    [Fact]
    public void ACollapsedSection_IsACollapsedGroup_AndTheOthersCanCollapse() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[2].Id, "End");
        tc.Engine.CollapseSection(tc.View.Queue[2].Section!.Id, true);

        sections.Apply(tc.View);

        Assert.Equal(new[] { false, true }, list.Groups.Cast<ListViewGroup>().Select(g => ListGroups.IsCollapsed(list, g)));
        Assert.All(list.Groups.Cast<ListViewGroup>(), g => Assert.True(ListGroups.IsCollapsible(list, g)));
    });

    [Fact]
    public void AGroupTheUserCollapses_IsTold_ToTheEngine_AndExpandingItAgainToo() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 2);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        sections.Apply(tc.View);

        ListGroups.SetCollapsed(list, list.Groups[0], true); // as a click on its chevron does
        sections.Apply(tc.View);
        Assert.True(tc.View.Queue[0].Section!.Collapsed);

        sections.Apply(tc.View); // the engine's view now agrees: nothing more is told
        ListGroups.SetCollapsed(list, list.Groups[0], false);
        sections.Apply(tc.View);
        Assert.False(tc.View.Queue[0].Section!.Collapsed);
        Assert.False(ListGroups.IsCollapsed(list, list.Groups[0]));
    });

    [Fact]
    public void OverASectionsHeader_TheFirstRowOfItsSection_IsTheRowAMenuIsFor() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[2].Id, "End");
        sections.Apply(tc.View);
        var endFirst = list.Items[2].Bounds;

        Assert.Equal(tc.View.Queue[2].Id, sections.HeaderRowAt(new Point(20, endFirst.Top - 8)));
        Assert.Null(sections.HeaderRowAt(new Point(20, endFirst.Top + 2)));
    });

    /// <summary>A details list of the engine's queue, <paramref name="rows"/> tones long, each
    /// item carrying its row's id as both hosts' queues do, laid out but never shown. Its groups
    /// need common controls 6, which <see cref="VisualStyles"/> turns on as the assembly loads.</summary>
    internal static ListView QueueOf(TestEngine tc, int rows)
    {
        tc.Engine.Add(Enumerable.Range(0, rows).Select(i => TestAudio.WriteDcWav($"sections-{i}.wav", 1, 0.2))).Wait();
        var list = new ListView { View = View.Details, FullRowSelect = true, MultiSelect = true, Size = new Size(400, 300) };
        list.Columns.Add("Title", 120);
        foreach (var row in tc.View.Queue)
        {
            list.Items.Add(new ListViewItem(Path.GetFileName(row.Track.FilePath)) { Tag = row.Id });
        }
        _ = list.Handle;
        return list;
    }

    /// <summary>WinForms wants an STA thread; xunit does not give one.</summary>
    internal static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
