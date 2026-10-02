using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell;
using static Faderio.Shell.Tests.QueueSectionsTests;

namespace Faderio.Shell.Tests;

/// <summary>Phase 6 issue 09: the queue menu's Section items, the same in both hosts. Each acts
/// on the row the menu is for, as one engine call, asking a name where one is needed.</summary>
public sealed class SectionMenuTests
{
    [Fact]
    public void StartASectionHere_AsksItsName_AndStartsItAtTheRow_AndCancellingDoesNothing() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        var answers = new Queue<string?>(["Intro", null]);
        var asked = new List<string>();
        var target = tc.View.Queue[1].Id;
        var menu = new SectionMenu(tc.Engine, () => tc.View.Queue.FirstOrDefault(r => r.Id == target),
            initial => { asked.Add(initial); return answers.Dequeue(); });

        menu.Refresh();
        menu.StartHere.PerformClick();
        target = tc.View.Queue[2].Id;
        menu.Refresh();
        menu.StartHere.PerformClick();

        Assert.Equal(new[] { "", "" }, asked);
        Assert.Equal(new[] { null, "Intro", "Intro" }, tc.View.Queue.Select(r => r.Section?.Name));
        Assert.True(tc.View.Queue[1].StartsSection);
    });

    [Fact]
    public void OnARowInASection_EachItemActsOnItsSection() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 3);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[2].Id, "End");
        var target = tc.View.Queue[2].Id;
        var asked = new List<string>();
        var menu = new SectionMenu(tc.Engine, () => tc.View.Queue.FirstOrDefault(r => r.Id == target),
            initial => { asked.Add(initial); return "Finale"; });
        SectionView End() => tc.View.Queue.Single(r => r.Id == target).Section!;

        menu.Refresh();
        Assert.True(menu.Colours[End().Colour].Checked);
        Assert.False(menu.MoveDown.Enabled); // the last section
        menu.Rename.PerformClick();
        menu.Colours[6].PerformClick();
        menu.Refresh();
        menu.Collapse.PerformClick();
        menu.Refresh();
        menu.MoveUp.PerformClick();

        Assert.Equal(new[] { "End" }, asked);
        Assert.Equal(("Finale", 6, true), (End().Name, End().Colour, End().Collapsed));
        Assert.Equal(target, tc.View.Queue[0].Id);

        menu.Refresh();
        menu.Remove.PerformClick();
        Assert.Null(tc.View.Queue.Single(r => r.Id == target).Section);
    });

    [Fact]
    public void WithNoRow_OnlyNothingIsLive_AndOnARowInNoSection_OnlyStartIs() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 1);
        QueueRow? target = null;
        var menu = new SectionMenu(tc.Engine, () => target, _ => "x");

        menu.Refresh();
        Assert.False(menu.Root.Enabled);

        target = tc.View.Queue[0];
        menu.Refresh();
        Assert.True(menu.StartHere.Enabled);
        Assert.False(menu.Rename.Enabled);
        Assert.False(menu.Remove.Enabled);
        Assert.All(menu.Colours, c => Assert.False(c.Enabled));
    });

    [Fact]
    public void UnderShuffle_WhichShowsNoHeaders_CollapseAndMoveAreNotLive() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 2);
        tc.Engine.StartSection(tc.View.Queue[0].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[1].Id, "End");
        var target = tc.View.Queue[0].Id;
        var menu = new SectionMenu(tc.Engine, () => tc.View.Queue.FirstOrDefault(r => r.Id == target), _ => "x");

        tc.Engine.SetShuffle(true);
        menu.Refresh();

        Assert.False(menu.Collapse.Enabled);
        Assert.False(menu.MoveUp.Enabled || menu.MoveDown.Enabled);
        Assert.True(menu.Rename.Enabled);
    });
}
