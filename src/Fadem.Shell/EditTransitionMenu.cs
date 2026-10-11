using Fadem.Core.Domain;
using Fadem.Core.Playback;

namespace Fadem.Shell;

/// <summary>
/// The queue menu's three Edit transition items, the same in both hosts (phase 5 issue 11):
/// this → next, previous → this and, under repeat-one, this → this. Each lives while the
/// selected row has that adjacency, as the engine says, and opens the editor on it: one engine
/// call for the adjacencies, then the editor's Open.
/// </summary>
internal sealed class EditTransitionMenu
{
    private readonly Func<RowAdjacencies> _selected;

    public ToolStripMenuItem Next { get; } = new("Edit transition: this → next");

    public ToolStripMenuItem Previous { get; } = new("Edit transition: previous → this");

    public ToolStripMenuItem Self { get; } = new("Edit transition: this → this");

    /// <param name="selected">The one selected row's adjacencies, none for no row or several.</param>
    /// <param name="edit">Opens the editor on an adjacency.</param>
    public EditTransitionMenu(Func<RowAdjacencies> selected, Action<Adjacency> edit)
    {
        _selected = selected;
        Next.Click += (_, _) => Choose(edit, a => a.Next);
        Previous.Click += (_, _) => Choose(edit, a => a.Previous);
        Self.Click += (_, _) => Choose(edit, a => a.Self);
    }

    /// <summary>The three, in the order the menu lists them.</summary>
    public ToolStripItem[] Items => [Next, Previous, Self];

    /// <summary>On the menu's Opening: each item is live while its adjacency exists, and
    /// this → this is shown only while repeat-one gives the row one.</summary>
    public void Refresh()
    {
        var adjacencies = _selected();
        Next.Enabled = adjacencies.Next is not null;
        Previous.Enabled = adjacencies.Previous is not null;
        Self.Available = adjacencies.Self is not null;
    }

    private void Choose(Action<Adjacency> edit, Func<RowAdjacencies, Adjacency?> which)
    {
        if (which(_selected()) is { } adjacency)
        {
            edit(adjacency);
        }
    }
}
