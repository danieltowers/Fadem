using System.Drawing;
using System.Windows.Forms;
using Fadem.Shell;

namespace Fadem.MusicBee.Tests;

/// <summary>Seam markers issue 05: the open pair as a tab on the editor inside MusicBee. In a main
/// panel the gap the tab crosses sits between the queue and the editor, as in the App; in a sidebar,
/// where the editor is a page in the lists' place, there is no gap, tab or outline.</summary>
public sealed partial class PluginTests
{
    private PluginView LinkView(Panel panel)
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        Start();
        Launched();
        _plugin.OnDockablePanelCreated(panel);
        return View(panel);
    }

    [Fact]
    public void InAMainPanel_TheGap_SitsBetweenTheQueueAndTheEditor_AsThePanelIsSized() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = LinkView(panel);
        var gap = view._link.Gap;
        void Between()
        {
            Assert.True(gap.Visible);
            Assert.Same(view, gap.Parent);
            Assert.Equal(PairLink.GapWidth, gap.Width);
            Assert.Equal(view._queuePane.Right, gap.Left);
            Assert.Equal(gap.Right, view._editorPane.Left);
            Assert.Equal(TransitionEditorPanel.DefaultWidth, view._editorPane.Width); // its own control, not the pane's padding
        }

        Between(); // from the start, with no pair open
        panel.Size = new Size(1400, 800);
        Between();
        panel.Size = new Size(PluginView.NarrowWidth, 500);
        Between();
        panel.Size = new Size(285, 900); // to a sidebar and back
        panel.Size = new Size(900, 500);
        Between();
    });

    [Fact]
    public void TheSidebarLayout_StartsUnder576Px_AndShowsNoGap() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(576, 500) };
        var view = LinkView(panel);

        Assert.Equal(576, PluginView.NarrowWidth);
        Assert.Equal(DockStyle.Left, view._playlists.Dock);
        Assert.Equal(PairLink.GapWidth, view._link.Gap.Width);

        panel.Size = new Size(575, 500);

        Assert.Equal(DockStyle.Top, view._playlists.Dock);
        Assert.Equal(0, view._link.Gap.Width);
        view.EditTransitionAt(0); // the editor's page, in the lists' place, all of the view's width
        Assert.Equal(0, view._link.Gap.Width);
        Assert.Equal(view.ClientSize.Width, view._editorPane.Width);
    });

    [Fact]
    public void InAMainPanel_TheOpenPair_IsTabbedOntoATintedEditor() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = LinkView(panel);
        var tint = QueueRowStyle.TintOf(view.Palette);

        view.EditTransitionAt(0);
        view.Render();

        Assert.Equal((0, 1), view._link.TabRows);
        Assert.Equal(tint, view._editor.BackColor);
    });

    [Fact]
    public void InASidebar_TheEditorsPage_HasNoTabOrTint_AndTheQueueKeepsItsTint() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(900, 500) };
        var view = LinkView(panel);
        var tint = QueueRowStyle.TintOf(view.Palette);
        view.EditTransitionAt(0);
        view.Render();
        Assert.Equal(tint, view._editor.BackColor);

        panel.Size = new Size(285, 900); // dragged to a sidebar with the pair open: the editor's page

        Assert.Null(view._link.TabRows); // at once, not at the next tick
        Assert.NotEqual(tint, view._editor.BackColor);
        view.Render();
        Assert.Null(view._link.TabRows);
        Assert.NotEqual(tint, view._editor.BackColor);
        Assert.Equal(tint, view._queueList.Items[0].BackColor); // the queue's rows, under the page, keep it
        Assert.Equal(tint, view._queueList.Items[1].BackColor);

        panel.Size = new Size(900, 500); // back: the link with it

        view.Render();
        Assert.Equal((0, 1), view._link.TabRows);
        Assert.Equal(tint, view._editor.BackColor);
    });
}
