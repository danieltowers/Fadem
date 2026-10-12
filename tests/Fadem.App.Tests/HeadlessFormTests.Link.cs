using System.Reflection;
using Fadem.Shell;
using Fadem.Core.Queue;
using Fadem.Core.Tests;

using Point = System.Drawing.Point;

namespace Fadem.App.Tests;

/// <summary>Seam markers issue 04: the open pair as a tab on the editor, across the gap, with the
/// stub for a pair out of the queue's view.</summary>
public sealed partial class HeadlessFormTests
{
    /// <summary>Every control's window, as showing the form would make them, without showing it.</summary>
    private static void CreateWindows(Control control)
    {
        _ = control.Handle;
        foreach (Control child in control.Controls)
        {
            CreateWindows(child);
        }
    }

    private static string[] Tracks(params string[] names) =>
        names.Select(n => TestAudio.WriteDcWav(n + ".wav", 2, 0.2)).ToArray();

    private static void Raise(Control control, string handler, EventArgs e) =>
        typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(control, new object[] { e });

    [Fact]
    public void TheGap_SitsBetweenTheQueueAndTheEditor_AsTheWindowIsSized() => RunSta(() =>
    {
        using var h = new Harness();
        var gap = h.Form._link.Gap;
        var pane = h.Form._editor.Parent!;
        void Between()
        {
            Assert.Equal(PairLink.GapWidth, gap.Width);
            Assert.Equal(h.Form._queueList.Right, gap.Left);
            Assert.Equal(gap.Right, pane.Left);
        }

        Between(); // from the start, with no pair open
        h.Form.Width = h.Form.MinimumSize.Width;
        Between();
        h.Form.Width = 1600;
        Between();
    });

    [Fact]
    public void TheTab_JoinsTheFirstMatch_TheOneHeardNext_AcrossTheGap() => RunSta(() =>
    {
        using var h = new Harness();
        var t = Tracks("alpha", "beta", "gamma");
        h.Form.AddPaths(new[] { t[0], t[1], t[2], t[0], t[1] });
        CreateWindows(h.Form);
        var list = h.Form._queueList;
        var p = h.Form.Palette;

        h.Form.EditTransitionAt(0);
        h.Form.Render();
        Assert.Equal((0, 1), h.Form._link.TabRows); // nothing plays: from the top

        h.Form.PlayRowAt(2);
        h.Form.Render();
        Assert.Equal((3, 4), h.Form._link.TabRows); // heard next from row 3
        Assert.False(h.Form._link.Stubbed);

        // The gap carries the tint level with rows 3 and 4, and not level with rows 0 and 1.
        var gap = h.Form._link.Gap;
        using var drawn = new Bitmap(gap.Width, gap.Height);
        gap.DrawToBitmap(drawn, new Rectangle(Point.Empty, gap.Size));
        // In the form's terms by the controls' places: the list's window is made before the form's.
        int YOf(int row) => list.Top + (list.Height - list.ClientSize.Height) / 2 + list.Items[row].Bounds.Top + list.Items[row].Bounds.Height / 2 - gap.Top;
        var tint = QueueRowStyle.TintOf(p);
        Assert.Equal(tint.ToArgb(), drawn.GetPixel(gap.Width / 2, YOf(3)).ToArgb());
        Assert.Equal(tint.ToArgb(), drawn.GetPixel(gap.Width / 2, YOf(4)).ToArgb());
        Assert.NotEqual(tint.ToArgb(), drawn.GetPixel(gap.Width / 2, YOf(0)).ToArgb());

        // The outline goes round the pair in the queue.
        var outline = QueueRowStyle.OutlineOf(p).ToArgb();
        using var queue = QueuePixels(list);
        var count = 0;
        for (var x = 0; x < queue.Width; x++)
        {
            for (var y = 0; y < queue.Height; y++)
            {
                count += queue.GetPixel(x, y).ToArgb() == outline ? 1 : 0;
            }
        }
        Assert.True(count > 200, $"{count} pixels of the outline in the queue");
    });

    [Fact]
    public void TheStub_StandsInWhileTheTabsRowsAreOutOfView_AndAClickScrollsThemInWithoutSelecting() => RunSta(() =>
    {
        using var h = new Harness();
        var names = Enumerable.Range(1, 40).Select(i => $"t{i:00}").ToArray();
        h.Form.AddPaths(Tracks(names));
        CreateWindows(h.Form);
        var list = h.Form._queueList;
        SelectRows(list, 0);

        h.Form.EditTransitionAt(35);
        h.Form.Render();

        Assert.True(h.Form._link.Stubbed);
        var queue = h.Engine.View.Queue;
        Assert.Equal($"▼ 36–37 {Presenter.TitleOf(queue[35].Track)} → {Presenter.TitleOf(queue[36].Track)}", h.Form._link.Stub.Text);

        Raise(h.Form._link.Stub, "OnClick", EventArgs.Empty);
        h.Form.Render();

        Assert.False(h.Form._link.Stubbed);
        Assert.True(list.Items[36].Bounds.Bottom <= list.ClientSize.Height, "the pair scrolled into view");
        Assert.Equal(new[] { 0 }, list.SelectedIndices.Cast<int>()); // the selection as it was
    });

    [Fact]
    public void RepeatAllsWrap_TabsFromTheLastRow_TheFirstOutlinedAlone_AndRepeatOnesFromItsOneRow() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(Tracks("alpha", "beta", "gamma"));
        h.Engine.SetRepeat(RepeatMode.All);

        h.Form.EditTransitionAt(2); // gamma → alpha
        h.Form.Render();
        Assert.Equal((2, 2), h.Form._link.TabRows);
        Assert.Equal(0, h.Form._link.AloneRow);

        h.Engine.SetRepeat(RepeatMode.One);
        h.Form._editor.Open(h.Engine.AdjacenciesOf(h.Engine.View.Queue[1].Id).Self!);
        h.Form.Render();
        Assert.Equal((1, 1), h.Form._link.TabRows);
        Assert.Null(h.Form._link.AloneRow);
    });

    [Fact]
    public void RepeatAllsWrap_WithTheLastRowOutOfView_StubsTheTab_AndStillOutlinesTheFirstRow() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(Tracks(Enumerable.Range(1, 40).Select(i => $"w{i:00}").ToArray()));
        CreateWindows(h.Form);
        h.Engine.SetRepeat(RepeatMode.All);
        var list = h.Form._queueList;

        h.Form.EditTransitionAt(39); // the last row into the first, at the top of the view
        h.Form.Render();

        Assert.True(h.Form._link.Stubbed);
        Assert.StartsWith("▼ 40–1 ", h.Form._link.Stub.Text);
        using var drawn = QueuePixels(list);
        var outline = QueueRowStyle.OutlineOf(h.Form.Palette).ToArgb();
        var row = list.Items[0].Bounds;
        var count = Enumerable.Range(0, drawn.Width).Count(x => Enumerable.Range(row.Top, row.Height + 4).Any(y => y < drawn.Height && drawn.GetPixel(x, y).ToArgb() == outline));
        Assert.True(count > 100, $"{count} columns of the first row's outline");
    });

    [Fact]
    public void TheEditor_IsTintedWhileLinked_ItsBoxesKeepingTheirOwn_AndAHalfUnderTheMouseLightsItsRow() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(Tracks("alpha", "beta", "gamma"));
        var p = h.Form.Palette;
        var editor = h.Form._editor;
        var tint = QueueRowStyle.TintOf(p);
        var chrome = editor.BackColor;

        h.Form.EditTransitionAt(0);
        h.Form.Render();
        Assert.Equal(tint, editor.BackColor);
        Assert.Equal(tint, editor._outgoingTitle.BackColor);
        Assert.Equal(tint, editor._editGlobal.BackColor);
        Assert.Equal(p.Well, editor._fadeOutPoint.BackColor);

        var rows = h.Form._queueList.Items;
        Raise(editor._incomingTitle, "OnMouseEnter", EventArgs.Empty);
        Assert.Equal(QueueRowStyle.LitOf(p), rows[1].SubItems[1].BackColor);
        Assert.Equal(tint, rows[0].SubItems[1].BackColor);
        Raise(editor._incomingTitle, "OnMouseLeave", EventArgs.Empty);
        Assert.Equal(tint, rows[1].SubItems[1].BackColor);

        editor.Close();
        h.Form.Render();
        Assert.Equal(chrome, editor.BackColor);
        Assert.Equal(chrome, editor._outgoingTitle.BackColor);
    });
}
