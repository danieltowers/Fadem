using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The playlist sidebar (playlists issue 02, D16, D17): the library's playlists, the open one
/// bold on the attended row's yellow. A click opens one; the context menu makes a new one,
/// renames one inline (F2 and a double-click too) and deletes one after a Yes/No. Every gesture
/// is one engine call. The list is <see cref="PlayerView.Playlists"/>, brought up to date on
/// every render; the panel keeps no list of its own (placement test 3).
/// </summary>
internal sealed class PlaylistSidebar : Panel
{
    private readonly PlaybackEngine _engine;
    private readonly Action _leaveEditor;
    private readonly Font _openFont;

    internal readonly ListView _list;
    internal readonly ToolStripMenuItem _new;
    internal readonly ToolStripMenuItem _rename;
    internal readonly ToolStripMenuItem _delete;

    /// <summary>Set only while a rename the user asked for starts: the list's own slow click on a
    /// selected item would start one too, and that one is refused.</summary>
    private bool _renameAsked;

    /// <summary>While a label is being edited the list is not re-rendered, which would end the edit.</summary>
    private bool _editing;

    /// <param name="leaveEditor">Saves a box being typed in and empties the editor before the
    /// open playlist changes.</param>
    public PlaylistSidebar(PlaybackEngine engine, Action leaveEditor)
    {
        _engine = engine;
        _leaveEditor = leaveEditor;
        ConfirmDelete = ShowDeleteDialog;

        Dock = DockStyle.Left;
        Width = 200;
        Padding = new Padding(4, 0, 0, 4);

        var heading = new Label { Text = "Playlists", Dock = DockStyle.Top, Height = 22, TextAlign = ContentAlignment.BottomLeft };
        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            HeaderStyle = ColumnHeaderStyle.None,
            FullRowSelect = true,
            MultiSelect = false,
            LabelEdit = true,
        };
        _list.Columns.Add("Playlist");
        _openFont = new Font(_list.Font, FontStyle.Bold);
        Disposed += (_, _) => _openFont.Dispose();

        _new = new ToolStripMenuItem("New playlist");
        _rename = new ToolStripMenuItem("Rename") { ShortcutKeyDisplayString = "F2" };
        _delete = new ToolStripMenuItem("Delete");
        var menu = new ContextMenuStrip { Items = { _new, new ToolStripSeparator(), _rename, _delete } };
        menu.Opening += (_, _) => _rename.Enabled = _delete.Enabled = Selected is not null;
        _new.Click += (_, _) => NewPlaylist();
        _rename.Click += (_, _) => StartRename(Selected);
        _delete.Click += (_, _) => Delete(Selected);
        _list.ContextMenuStrip = menu;

        _list.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && _list.HitTest(e.Location).Item is { } item)
            {
                OpenAt(item.Index);
            }
        };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                StartRename(_list.HitTest(e.Location).Item);
            }
        };
        _list.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None)
            {
                return;
            }
            if (e.KeyCode == Keys.F2)
            {
                StartRename(Selected);
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && Selected is { } item)
            {
                OpenAt(item.Index);
                e.Handled = true;
            }
        };
        _list.BeforeLabelEdit += (_, e) =>
        {
            e.CancelEdit = !_renameAsked;
            _editing = _renameAsked;
        };
        _list.AfterLabelEdit += (_, e) => CommitRename(e);
        _list.Resize += (_, _) => FitColumn();

        Controls.Add(_list);
        Controls.Add(heading);
    }

    private ListViewItem? Selected => _list.SelectedItems.Count == 1 ? _list.SelectedItems[0] : null;

    // ── Rendering: read, render, forget ─────────────────────────────────────────────────

    /// <summary>Brings the list up to the view's playlists, touching only what differs: it runs
    /// on every tick.</summary>
    public void Render(PlayerView view)
    {
        if (_editing)
        {
            return;
        }
        var names = view.Playlists;
        if (_list.Items.Count != names.Count)
        {
            _list.BeginUpdate();
            while (_list.Items.Count > names.Count)
            {
                _list.Items.RemoveAt(_list.Items.Count - 1);
            }
            while (_list.Items.Count < names.Count)
            {
                _list.Items.Add(new ListViewItem());
            }
            _list.EndUpdate();
            FitColumn();
        }
        for (var i = 0; i < names.Count; i++)
        {
            var item = _list.Items[i];
            if (item.Text != names[i])
            {
                item.Text = names[i];
            }
            var open = names[i] == view.OpenPlaylist;
            var font = open ? _openFont : _list.Font;
            if (item.Font != font)
            {
                item.Font = font;
            }
            var back = open ? SystemColors.Info : SystemColors.Window;
            if (item.BackColor != back)
            {
                item.BackColor = back;
            }
        }
    }

    /// <summary>The one column spans the list, so a click anywhere on a row's height is on it.</summary>
    private void FitColumn()
    {
        var width = Math.Max(20, _list.ClientSize.Width - 4);
        if (_list.Columns[0].Width != width)
        {
            _list.Columns[0].Width = width;
        }
    }

    // ── Commands: one intent, one engine call ───────────────────────────────────────────

    /// <summary>Opens the playlist at <paramref name="index"/>, once the editor's unsaved edits are
    /// settled. The open one is no change, so it asks nothing.</summary>
    internal void OpenAt(int index)
    {
        if (index < 0 || index >= _list.Items.Count)
        {
            return;
        }
        var name = _list.Items[index].Text;
        if (name == _engine.View.OpenPlaylist)
        {
            return;
        }
        _leaveEditor();
        _ = _engine.OpenPlaylist(name); // its rows and any notice arrive through the sink
    }

    private void NewPlaylist()
    {
        _leaveEditor();
        _engine.NewPlaylist();
    }

    /// <summary>Starts the inline edit of a playlist's name. The label is the view's until the
    /// engine says otherwise: see <see cref="CommitRename"/>.</summary>
    private void StartRename(ListViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        _renameAsked = true;
        try
        {
            item.BeginEdit();
        }
        finally
        {
            _renameAsked = false;
        }
    }

    /// <summary>Enter sends the typed name to the engine; Esc (no label) sends nothing. The edit
    /// itself is always undone: a rename that succeeds comes back as the view's new name, and one
    /// that is refused leaves the old name, with the engine's notice saying why.</summary>
    private void CommitRename(LabelEditEventArgs e)
    {
        _editing = false;
        e.CancelEdit = true;
        if (e.Label is { } typed && e.Item >= 0 && e.Item < _list.Items.Count)
        {
            _engine.RenamePlaylist(_list.Items[e.Item].Text, typed);
        }
    }

    /// <summary>Whether the user confirms deleting this playlist. A test replaces it, since a
    /// headless form cannot show a dialog.</summary>
    internal Func<string, bool> ConfirmDelete { get; set; }

    /// <summary>The file goes for good, so it asks once. Deleting the open playlist opens another
    /// (D17), so the editor's unsaved edits are settled too.</summary>
    private void Delete(ListViewItem? item)
    {
        if (item is null)
        {
            return;
        }
        var name = item.Text;
        if (!ConfirmDelete(name))
        {
            return;
        }
        if (name == _engine.View.OpenPlaylist)
        {
            _leaveEditor();
        }
        _engine.DeletePlaylist(name);
    }

    private bool ShowDeleteDialog(string name) =>
        MessageBox.Show(
            FindForm(),
            $"Delete the playlist \"{name}\"? Its file is deleted for good.",
            "Delete playlist",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
