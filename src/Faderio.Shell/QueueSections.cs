using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// How a queue list shows the playlist's sections, the same in both hosts (phase 6 issue 09).
/// Each run of rows the view heads with a section is a group of the list, headed by the
/// section's name; rows above the first section are in a group with no header. A row in a
/// section has a stripe of its colour at its left edge. A list with no section headed is as it
/// was before sections: no groups, no stripes. The list's items stay in the queue's order, one
/// per row, so an item's index is still its row's.
/// </summary>
internal sealed class QueueSections : IDisposable
{
    private readonly ListView _list;
    private readonly PlaybackEngine _engine;
    private readonly ImageList _stripes = SectionPalette.Images(new Size(4, 16));
    private readonly ImageList _swatches = SectionPalette.Images(new Size(12, 12));

    /// <summary>The groups' sections, parallel to the list's groups: null for the unheaded one.</summary>
    private readonly List<SectionView?> _shown = new();

    /// <summary>Whether each group was collapsed when last looked at, parallel to the groups: a
    /// group that is not as it was left has been collapsed or expanded by the user.</summary>
    private readonly List<bool> _seen = new();

    /// <summary>The list's window the groups were last set on: a new one has them afresh.</summary>
    private IntPtr _handle;

    /// <summary>The queue menu's Section submenu, for the row <see cref="MenuOpening"/> found.</summary>
    public SectionMenu Menu { get; }

    /// <summary>Asks a section's name; a test answers for the user.</summary>
    internal Func<string, string?> AskName { get; set; }

    /// <summary>The row the menu is for, found as it opened.</summary>
    private RowId? _menuRow;

    public QueueSections(ListView list, PlaybackEngine engine)
    {
        _list = list;
        _engine = engine;
        AskName = initial => Dialogs.AskSectionName(_list.FindForm(), initial, Theme.StudioTheme.PaletteOf(_list));
        Menu = new SectionMenu(engine, () => _engine.View.Queue.FirstOrDefault(r => r.Id == _menuRow), initial => AskName(initial));
    }

    /// <summary>Shows <paramref name="view"/>'s sections on the list, whose items are its queue's
    /// rows in order. Only what changed is touched, since a render runs on every tick.</summary>
    public void Apply(PlayerView view)
    {
        var rows = view.Queue;
        var count = Math.Min(rows.Count, _list.Items.Count);
        var anySection = rows.Any(r => r.Section is not null);
        var striped = anySection ? _stripes : null;
        if (_list.SmallImageList != striped)
        {
            _list.SmallImageList = striped;
        }
        for (var i = 0; i < count; i++)
        {
            var stripe = rows[i].Section?.Colour ?? -1;
            if (_list.Items[i].ImageIndex != stripe)
            {
                _list.Items[i].ImageIndex = stripe;
            }
        }
        ApplyGroups(rows, count);
        ApplyHeaders();
    }

    /// <summary>Each header shows its section's colour and is collapsed as its section is, but a
    /// group the user collapsed or expanded since it was last looked at is told to the engine,
    /// whose next view then agrees. The unheaded group has no header to show either by.</summary>
    private void ApplyHeaders()
    {
        var fresh = _list.Handle != _handle;
        _handle = _list.Handle;
        if (fresh)
        {
            ListGroups.SetTitleImages(_list, _swatches);
        }
        for (var i = 0; i < _shown.Count; i++)
        {
            if (_shown[i] is not { } section)
            {
                continue;
            }
            var group = _list.Groups[i];
            if (ListGroups.TitleImage(_list, group) != section.Colour)
            {
                ListGroups.SetTitleImage(_list, group, section.Colour);
            }
            var collapsed = ListGroups.IsCollapsed(_list, group);
            if (!fresh && collapsed != _seen[i])
            {
                _seen[i] = collapsed;
                _engine.CollapseSection(section.Id, collapsed);
                continue;
            }
            if (fresh || collapsed != section.Collapsed || !ListGroups.IsCollapsible(_list, group))
            {
                ListGroups.SetCollapsed(_list, group, section.Collapsed);
                _seen[i] = ListGroups.IsCollapsed(_list, group);
            }
        }
    }

    /// <summary>One group per section headed, rebuilt only when the sections or their places
    /// change.</summary>
    private void ApplyGroups(IReadOnlyList<QueueRow> rows, int count)
    {
        var grouped = rows.Any(r => r.StartsSection);
        // Each row's group, as an index into the groups wanted.
        var wanted = new List<SectionView?>();
        var groupOf = new int[count];
        for (var i = 0; i < count; i++)
        {
            if (rows[i].StartsSection || wanted.Count == 0)
            {
                wanted.Add(rows[i].StartsSection ? rows[i].Section : null);
            }
            groupOf[i] = wanted.Count - 1;
        }
        if (!grouped)
        {
            wanted.Clear();
        }
        if (!wanted.Select(Heading).SequenceEqual(_shown.Select(Heading)))
        {
            _list.BeginUpdate();
            foreach (ListViewItem item in _list.Items)
            {
                item.Group = null;
            }
            _list.Groups.Clear();
            foreach (var section in wanted)
            {
                _list.Groups.Add(new ListViewGroup(section?.Name ?? ""));
            }
            _shown.Clear();
            _shown.AddRange(wanted);
            _seen.Clear();
            _seen.AddRange(wanted.Select(_ => false));
            _handle = IntPtr.Zero; // the new groups are set afresh
            _list.EndUpdate();
        }
        for (var i = 0; i < wanted.Count; i++)
        {
            _shown[i] = wanted[i]; // the same headers, with the sections as they are now
        }
        if (_list.ShowGroups != grouped)
        {
            _list.ShowGroups = grouped;
        }
        if (!grouped)
        {
            return;
        }
        for (var i = 0; i < count; i++)
        {
            var group = _list.Groups[groupOf[i]];
            if (_list.Items[i].Group != group)
            {
                _list.Items[i].Group = group;
            }
        }
    }

    /// <summary>On the queue menu's Opening: the menu is for the section whose header is under
    /// the mouse, else for the one selected row.</summary>
    public void MenuOpening()
    {
        _menuRow = HeaderRowAt(_list.PointToClient(Cursor.Position))
            ?? (_list.SelectedItems.Count == 1 ? (RowId)_list.SelectedItems[0].Tag! : null);
        Menu.Refresh();
    }

    /// <summary>The first row of the section whose header is at <paramref name="client"/>, which a
    /// right-click there opens the menu for; null off a header.</summary>
    public RowId? HeaderRowAt(Point client) =>
        _list.ShowGroups && ListGroups.HeaderAt(_list, client) is { } group && group.Items.Count > 0
            ? group.Items.Cast<ListViewItem>().OrderBy(i => i.Index).Select(i => (RowId)i.Tag!).First()
            : null;

    /// <summary>What a group's header shows: a change to it is a new group.</summary>
    private static (SectionId?, string?) Heading(SectionView? section) => (section?.Id, section?.Name);

    public void Dispose()
    {
        _stripes.Dispose();
        _swatches.Dispose();
    }
}
