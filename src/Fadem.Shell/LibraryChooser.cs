using Fadem.Core.Persistence;
using Fadem.Shell.Theme;

namespace Fadem.Shell;

/// <summary>
/// The library chooser (library chooser issue 02), the same in both hosts: the known libraries,
/// most recently opened first, each by name and folder, the open one marked and one whose folder
/// is gone greyed. Open (or a double-click, or Enter) chooses the selected library, Add folder…
/// picks one not listed, and Remove from list forgets one at once, leaving its folder alone; the
/// open library cannot be removed, nor a gone one opened. It holds the rows only while shown,
/// and asks for them again after a removal: <see cref="Chosen"/> is all its caller reads.
/// </summary>
internal sealed class LibraryChooser : Form
{
    internal readonly ListView _list;
    internal readonly Label _hint;
    internal readonly Button _add;
    internal readonly Button _remove;
    internal readonly Button _open;
    internal readonly Button _cancel;
    private readonly Func<IReadOnlyList<KnownLibrary>> _known;
    private readonly Action<string> _forget;
    private readonly Func<string?, string?> _chooseFolder;
    private IReadOnlyList<KnownLibrary> _rows = [];
    private readonly Palette _palette;

    /// <param name="known">The known libraries, asked for when shown and after each removal.</param>
    /// <param name="forget">Takes a folder off the list: one engine call, or a launch's before the engine exists.</param>
    /// <param name="chooseFolder">The folder picked for Add folder…, starting where given, or null when cancelled.</param>
    /// <param name="selected">The folder to open with selected, or null for the open library.</param>
    /// <param name="palette">The host's palette (studio theme issue 06), or the built-in one.</param>
    public LibraryChooser(
        Func<IReadOnlyList<KnownLibrary>> known, Action<string> forget, Func<string?, string?> chooseFolder, string? selected = null,
        Palette? palette = null)
    {
        _palette = palette ?? Palette.Studio;
        _known = known;
        _forget = forget;
        _chooseFolder = chooseFolder;
        Text = "Choose a library";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            ShowItemToolTips = true,
            Size = new Size(560, 200),
            AccessibleName = "Known libraries",
        };
        _list.Columns.Add("Name", 130);
        _list.Columns.Add("Folder", 340);
        _list.Columns.Add("", 70);
        _list.SelectedIndexChanged += (_, _) => Enable();
        _list.ItemActivate += (_, _) => Open();

        _hint = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 6, 0, 0),
        };
        StudioTheme.SetInk(_hint, Ink.Dim);

        _add = new Button { Text = "Add folder…", AutoSize = true };
        _remove = new Button { Text = "Remove from list", AutoSize = true };
        _open = new Button { Text = "Open", AutoSize = true };
        _cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        _add.Click += (_, _) => Add();
        _remove.Click += (_, _) => Remove();
        _open.Click += (_, _) => Open();

        var left = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Left, Margin = Padding.Empty };
        left.Controls.AddRange([_add, _remove]);
        var right = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        right.Controls.AddRange([_open, _cancel]);
        var buttons = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        buttons.Controls.Add(left, 0, 0);
        buttons.Controls.Add(right, 1, 0);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_list, 0, 0);
        layout.Controls.Add(_hint, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        CancelButton = _cancel;
        QueueRowStyle.Draw(_list, _palette); // a selected row raised, the header in capitals
        StudioTheme.Apply(this, _palette);

        // A list without a handle keeps no selection, and it is filled before it is first shown.
        _ = _list.Handle;
        Fill(selected);
    }

    /// <summary>The folder chosen, or null until one is.</summary>
    public string? Chosen { get; private set; }

    /// <summary>Shows the chooser and returns the folder chosen, or null when cancelled.</summary>
    public static string? Choose(
        IWin32Window? owner, Func<IReadOnlyList<KnownLibrary>> known, Action<string> forget, string? selected = null,
        Palette? palette = null)
    {
        using var chooser = new LibraryChooser(known, forget, start => Dialogs.ChooseLibraryFolder(owner, start), selected, palette);
        if (owner is null)
        {
            // At a launch, before any window: in the taskbar and the screen's middle, or it is lost.
            chooser.ShowInTaskbar = true;
            chooser.StartPosition = FormStartPosition.CenterScreen;
        }
        return chooser.ShowDialog(owner) == DialogResult.OK ? chooser.Chosen : null;
    }

    private KnownLibrary? Selected => _list.SelectedIndices.Count == 0 ? null : _rows[_list.SelectedIndices[0]];

    private void Fill(string? selected)
    {
        _rows = _known();
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var row in _rows)
        {
            var item = new ListViewItem([row.Name, row.Folder, row.IsOpen ? "open" : row.Exists ? "" : "not found"])
            {
                ToolTipText = row.Folder,
                UseItemStyleForSubItems = false,
            };
            foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
            {
                cell.ForeColor = row.Exists ? _palette.Text : _palette.Dim;
                cell.BackColor = _palette.Well;
            }
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        var index = IndexOf(selected) ?? IndexOf(_rows.FirstOrDefault(r => r.IsOpen)?.Folder) ?? (_rows.Count > 0 ? 0 : (int?)null);
        if (index is { } i)
        {
            _list.Items[i].Selected = true;
            _list.Items[i].Focused = true;
            _list.EnsureVisible(i);
        }
        _hint.Text = _rows.Count == 0
            ? "No library has opened yet. Add folder… picks one: Fadem keeps its transitions and settings in it."
            : "Removing a library from the list leaves its folder as it is.";
        Enable();
    }

    private int? IndexOf(string? folder)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (LibraryFolders.Same(_rows[i].Folder, folder))
            {
                return i;
            }
        }
        return null;
    }

    private void Enable()
    {
        var row = Selected;
        _open.Enabled = row is { Exists: true };
        _remove.Enabled = row is { IsOpen: false };
        AcceptButton = _open.Enabled ? _open : null;
    }

    private void Open()
    {
        if (Selected is { Exists: true } row)
        {
            Choose(row.Folder);
        }
    }

    private void Add()
    {
        if (_chooseFolder(Selected?.Folder) is { } folder)
        {
            Choose(folder);
        }
    }

    private void Remove()
    {
        if (Selected is not { IsOpen: false } row)
        {
            return;
        }
        var index = _list.SelectedIndices[0];
        _forget(row.Folder);
        Fill(null);
        // The row that took its place stays selected, so a run of removals needs no reselecting.
        if (_rows.Count > 0)
        {
            _list.SelectedIndices.Clear();
            var next = Math.Min(index, _rows.Count - 1);
            _list.Items[next].Selected = true;
            _list.Items[next].Focused = true;
        }
    }

    private void Choose(string folder)
    {
        Chosen = folder;
        DialogResult = DialogResult.OK;
    }
}
