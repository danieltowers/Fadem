using Fadem.Core.Playback;

namespace Fadem.Shell;

/// <summary>
/// The queue menu's Section submenu, the same in both hosts (phase 6 issue 09). Every item acts
/// on the row the menu is for, as one engine call: start a section there, or rename, recolour,
/// collapse, move or remove the row's section. A name is asked through the host, which owns the
/// dialog's parent.
/// </summary>
internal sealed class SectionMenu
{
    private readonly PlaybackEngine _engine;
    private readonly Func<QueueRow?> _target;
    private readonly Func<string, string?> _askName;

    public ToolStripMenuItem Root { get; } = new("Section");

    public ToolStripMenuItem StartHere { get; } = new("Start a section here…");

    public ToolStripMenuItem Rename { get; } = new("Rename section…");

    public ToolStripMenuItem Colour { get; } = new("Colour");

    /// <summary>The palette's colours, in its order: the section's own is checked.</summary>
    public ToolStripMenuItem[] Colours { get; }

    public ToolStripMenuItem Collapse { get; } = new("Collapse section");

    public ToolStripMenuItem MoveUp { get; } = new("Move section up");

    public ToolStripMenuItem MoveDown { get; } = new("Move section down");

    public ToolStripMenuItem Remove { get; } = new("Remove section");

    /// <param name="target">The row the menu is for: the one selected, or the first of a section
    /// whose header was right-clicked; none for no row or several.</param>
    /// <param name="askName">Asks a section's name, opening on the one given; null when cancelled.</param>
    public SectionMenu(PlaybackEngine engine, Func<QueueRow?> target, Func<string, string?> askName)
    {
        _engine = engine;
        _target = target;
        _askName = askName;
        Colours = Enumerable.Range(0, SectionView.Colours)
            .Select(c => new ToolStripMenuItem(SectionPalette.NameOf(c), SectionPalette.Swatch(c, new Size(12, 12))))
            .ToArray();
        for (var c = 0; c < Colours.Length; c++)
        {
            var colour = c;
            Colours[c].Click += (_, _) => OnSection(s => _engine.RecolourSection(s.Id, colour));
        }
        Colour.DropDownItems.AddRange(Colours);
        Root.DropDownItems.AddRange(
            [StartHere, new ToolStripSeparator(), Rename, Colour, Collapse, MoveUp, MoveDown, Remove]);

        StartHere.Click += (_, _) =>
        {
            if (_target() is { } row && _askName("") is { } name)
            {
                _engine.StartSection(row.Id, name);
            }
        };
        Rename.Click += (_, _) => OnSection(s =>
        {
            if (_askName(s.Name) is { } name)
            {
                _engine.RenameSection(s.Id, name);
            }
        });
        Collapse.Click += (_, _) => OnSection(s => _engine.CollapseSection(s.Id, !s.Collapsed));
        MoveUp.Click += (_, _) => OnSection(s => _engine.MoveSection(s.Id, -1));
        MoveDown.Click += (_, _) => OnSection(s => _engine.MoveSection(s.Id, +1));
        Remove.Click += (_, _) => OnSection(s => _engine.RemoveSection(s.Id));
    }

    /// <summary>On the menu's Opening: live as far as the row the menu is for allows. Under
    /// shuffle no header is drawn, so there is nothing to collapse, and the engine says no
    /// section moves.</summary>
    public void Refresh()
    {
        var row = _target();
        var section = row?.Section;
        Root.Enabled = row is not null;
        StartHere.Enabled = row is not null && !row.StartsSection;
        foreach (var item in new[] { Rename, Colour, Remove })
        {
            item.Enabled = section is not null;
        }
        Collapse.Enabled = section is not null && !_engine.View.Shuffle;
        for (var c = 0; c < Colours.Length; c++)
        {
            Colours[c].Enabled = section is not null;
            Colours[c].Checked = section?.Colour == c;
        }
        Collapse.Text = section?.Collapsed == true ? "Expand section" : "Collapse section";
        MoveUp.Enabled = section?.CanMoveUp == true;
        MoveDown.Enabled = section?.CanMoveDown == true;
    }

    private void OnSection(Action<SectionView> act)
    {
        if (_target()?.Section is { } section)
        {
            act(section);
        }
    }
}
