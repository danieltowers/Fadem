using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Transitions;

namespace Faderio.App;

/// <summary>
/// The WinForms shell for the slice (ticket 05): add files or a folder, or drop either on the
/// list (build issue 02), open a playlist or save the queue as one (build issue 03), a
/// multi-select list whose rows drag to a new place (build issue 05), a context menu for "Edit
/// transition (this → next)", Remove and Clear queue (build issue 04; Delete removes too),
/// transport with a shuffle toggle and a repeat button (build issue 07), a seek bar in
/// milliseconds, the transition editor, and a status strip showing the latest notice and a count
/// that opens this session's notice list (build issue 09). It renders
/// <see cref="PlayerView"/>s and issues commands; it holds no engine state between calls (the
/// placement rule in CLAUDE.md). The seek bar's Maximum is the track length
/// in milliseconds, so no scaling exists to get wrong (ticket 06).
/// </summary>
internal sealed class MainForm : Form
{
    private readonly PlaybackEngine _engine;

    internal readonly ListView _queueList;
    internal readonly TrackBar _seek;
    internal readonly Label _time;
    internal readonly Button _addFiles;
    internal readonly Button _addFolder;
    internal readonly Button _openPlaylist;
    internal readonly Button _saveAsPlaylist;
    internal readonly Button _prev;
    internal readonly Button _playPause;
    internal readonly Button _stop;
    internal readonly Button _next;
    internal readonly Button _transitionNow;
    internal readonly CheckBox _shuffle;
    internal readonly CheckBox _repeat;
    internal readonly ToolTip _tips;
    internal readonly ToolStripStatusLabel _status;
    internal readonly ToolStripStatusLabel _noticeCount;
    internal readonly ToolStripStatusLabel _addProgress;
    internal readonly ToolStripButton _cancelAdd;
    internal readonly TransitionEditorPanel _editor;
    internal readonly ToolStripMenuItem _editTransition;
    internal readonly ToolStripMenuItem _removeRows;
    internal readonly ToolStripMenuItem _clearQueue;
    internal readonly Panel _dropLine;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly System.Windows.Forms.Timer _dragScroll;

    /// <summary>What a drag of rows carries: their ids, private to this window.</summary>
    private const string RowsFormat = "faderio.rows";

    /// <summary>Notices already delivered, as the strip and the list show them: formatting of
    /// messages, not engine state (build issue 09).</summary>
    private readonly NoticeLog _notices = new();
    internal NoticeListForm? _noticeList;

    private long _shownQueue = -1;
    private bool _seekDragging;

    public MainForm(PlaybackEngine engine, ITransitionStore store, PostingSink sink)
    {
        _engine = engine;
        ChooseFiles = ShowFilesDialog;
        ChooseFolders = ShowFolderDialog;
        ChoosePlaylists = ShowPlaylistDialog;
        ChoosePlaylistSavePath = ShowSavePlaylistDialog;
        ConfirmClear = ShowClearDialog;
        StartDrag = data => _queueList!.DoDragDrop(data, DragDropEffects.Move);
        ShowNoticeList = list =>
        {
            if (!list.Visible)
            {
                list.Show(this);
            }
            list.Activate();
        };

        Text = "faderio";
        MinimumSize = new Size(760, 480);
        Size = new Size(1040, 600);
        StartPosition = FormStartPosition.CenterScreen;

        _status = new SpringLabel("Ready.") { TextAlign = ContentAlignment.MiddleLeft };
        _noticeCount = new ToolStripStatusLabel { Available = false, IsLink = true, ToolTipText = "Show this session's notices" };
        _addProgress = new ToolStripStatusLabel { Available = false };
        _cancelAdd = new ToolStripButton("Cancel") { Available = false, DisplayStyle = ToolStripItemDisplayStyle.Text };
        var statusStrip = new StatusStrip { Items = { _status, _noticeCount, _addProgress, _cancelAdd }, ShowItemToolTips = true };

        _addFiles = new Button { Text = "Add files…", Width = 90, Height = 28, Margin = new Padding(4, 8, 4, 0) };
        _addFolder = new Button { Text = "Add folder…", Width = 90, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        _openPlaylist = new Button { Text = "Open playlist…", Width = 104, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        _saveAsPlaylist = new Button { Text = "Save as playlist…", Width = 116, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        // The queue's own commands get a row of their own, so the seek bar keeps its travel.
        var queueBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        queueBar.Controls.AddRange(new Control[] { _addFiles, _addFolder, _openPlaylist, _saveAsPlaylist });
        var symbols = new Font("Segoe UI Symbol", 9f);
        _prev = new Button { Text = "⏮", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _playPause = new Button { Text = "▶", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _stop = new Button { Text = "⏹", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _next = new Button { Text = "⏭", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // Arrows draw small in this font; at 13 pt the wave arrow reads as clearly as ⏭.
        _transitionNow = new Button { Text = "⤳", Font = new Font("Segoe UI Symbol", 13f), Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // Push buttons that stay pressed in while on. They never check themselves: what they
        // show is always the view's, set in Render.
        _shuffle = new CheckBox { Text = "🔀", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(10, 8, 2, 4) };
        _repeat = new CheckBox { Text = "🔁", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _tips = new ToolTip();
        _tips.SetToolTip(_transitionNow, "Transition now");
        _seek = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 0,
            TickStyle = TickStyle.None,
            Margin = new Padding(8, 6, 8, 0),
        };
        _time = new Label
        {
            Text = "0:00 / -0:00",
            Width = 130,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
        };
        // A table, not anchors: the seek bar takes whatever width is left between the buttons
        // and the readout, at any window size.
        var transport = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 9, RowCount = 1 };
        foreach (var _ in Enumerable.Range(0, 7))
        {
            transport.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        transport.Controls.Add(_prev, 0, 0);
        transport.Controls.Add(_playPause, 1, 0);
        transport.Controls.Add(_stop, 2, 0);
        transport.Controls.Add(_next, 3, 0);
        transport.Controls.Add(_transitionNow, 4, 0);
        transport.Controls.Add(_shuffle, 5, 0);
        transport.Controls.Add(_repeat, 6, 0);
        transport.Controls.Add(_seek, 7, 0);
        transport.Controls.Add(_time, 8, 0);

        _queueList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            AllowDrop = true,
        };
        _queueList.Columns.Add("#", 36);
        _queueList.Columns.Add("Title", 320);
        _queueList.Columns.Add("Artist", 200);
        _queueList.Columns.Add("Length", 70, HorizontalAlignment.Right);

        _editTransition = new ToolStripMenuItem("Edit transition (this → next)");
        _removeRows = new ToolStripMenuItem("Remove") { ShortcutKeyDisplayString = "Del" };
        _clearQueue = new ToolStripMenuItem("Clear queue");
        var menu = new ContextMenuStrip
        {
            Items = { _editTransition, new ToolStripSeparator(), _removeRows, _clearQueue },
        };
        menu.Opening += (_, _) =>
        {
            _editTransition.Enabled = SelectedAdjacency() is not null;
            _removeRows.Enabled = _queueList.SelectedItems.Count > 0;
            _clearQueue.Enabled = _queueList.Items.Count > 0;
        };
        _editTransition.Click += (_, _) => EditSelectedTransition();
        _removeRows.Click += (_, _) => RemoveSelected();
        _clearQueue.Click += (_, _) => ClearQueue();
        _queueList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
            {
                RemoveSelected();
                e.Handled = true;
            }
        };
        _queueList.ContextMenuStrip = menu;
        _queueList.DoubleClick += (_, _) => PlaySelected();
        // The drop line: where dragged rows will land. A ListView draws no insertion mark in
        // details view, so it is a thin panel over the list.
        _dropLine = new Panel { Height = 2, BackColor = SystemColors.Highlight, Visible = false };
        _queueList.Controls.Add(_dropLine);
        // A drag raises DragOver only when the mouse moves, so a drag held still at an edge is
        // scrolled on a timer.
        _dragScroll = new System.Windows.Forms.Timer { Interval = 60 };
        _dragScroll.Tick += (_, _) =>
        {
            var client = _queueList.PointToClient(Cursor.Position);
            ScrollNearEdge(client);
            ShowDropLine(DropIndex(client));
        };
        _queueList.ItemDrag += (_, _) => DragSelectedRows();
        _queueList.DragEnter += (_, e) => DragOverList(e);
        _queueList.DragOver += (_, e) => DragOverList(e);
        _queueList.DragLeave += (_, _) => EndRowDrag();
        _queueList.DragDrop += (_, e) =>
        {
            EndRowDrag();
            if (e.Data?.GetData(RowsFormat) is RowId[] rows)
            {
                MoveRows(rows, DropIndex(ClientPoint(e)));
            }
            else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                _ = AddPaths(paths); // files and folders alike, in the order dropped
            }
        };

        _editor = new TransitionEditorPanel(engine, store, ShowNotice, ChooseRenderPath);

        // Docking order: right-docked editor and bottom strip first, the fill-docked list last.
        Controls.Add(_queueList);
        Controls.Add(_editor);
        Controls.Add(statusStrip);
        Controls.Add(transport);
        Controls.Add(queueBar);

        _addFiles.Click += (_, _) => AddChosen(ChooseFiles);
        _addFolder.Click += (_, _) => AddChosen(ChooseFolders);
        _openPlaylist.Click += (_, _) => AddChosen(ChoosePlaylists);
        _saveAsPlaylist.Click += (_, _) => SaveAsPlaylist();
        _cancelAdd.Click += (_, _) => _engine.CancelAdd();
        _status.Click += (_, _) => OpenNotices();
        _noticeCount.Click += (_, _) => OpenNotices();
        _playPause.Click += (_, _) => _engine.TogglePlayPause();
        _prev.Click += (_, _) => _engine.Previous();
        _stop.Click += (_, _) => _engine.Stop();
        _next.Click += (_, _) => _engine.Next();
        _transitionNow.Click += (_, _) => _engine.TransitionNow();
        // The press asks for the state after the one the view holds now, not the one last shown.
        _shuffle.Click += (_, _) => _engine.SetShuffle(!_engine.View.Shuffle);
        _repeat.Click += (_, _) => _engine.SetRepeat(Presenter.NextRepeat(_engine.View.Repeat));
        _seek.MouseDown += (_, _) => _seekDragging = true;
        _seek.MouseUp += (_, _) =>
        {
            _seekDragging = false;
            _engine.Seek(Playhead.FromMilliseconds(_seek.Value)); // straight through, no scaling
        };

        sink.Changed += Render;
        sink.Noticed += ShowNotice;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Render(_engine.View);
        _timer.Start();

        FormClosing += (_, _) =>
        {
            _timer.Stop();
            _tips.Dispose();
            _engine.Dispose();
        };

        Render(_engine.View);
    }

    // ── Rendering: read, render, forget ─────────────────────────────────────────────────

    internal void Render() => Render(_engine.View);

    internal void Render(PlayerView view)
    {
        if (IsDisposed)
        {
            return;
        }
        _playPause.Text = Presenter.PlayPauseGlyph(view.Transport);
        _transitionNow.Enabled = view.CanTransitionNow;
        _shuffle.Checked = view.Shuffle;
        _repeat.Checked = view.Repeat != RepeatMode.Off;
        _repeat.Text = Presenter.RepeatGlyph(view.Repeat);
        ShowTip(_shuffle, Presenter.ShuffleTip(view.Shuffle));
        ShowTip(_repeat, Presenter.RepeatTip(view.Repeat));

        if (view.QueueVersion != _shownQueue)
        {
            RebuildRows(view);
            _shownQueue = view.QueueVersion;
        }
        for (var i = 0; i < _queueList.Items.Count && i < view.Queue.Count; i++)
        {
            var current = view.Queue[i].IsCurrent && view.Transport != Transport.Stopped;
            _queueList.Items[i].BackColor = current ? SystemColors.Info : SystemColors.Window;
        }

        var attended = view.Attended;
        _seek.Maximum = attended?.Length.Milliseconds ?? 0;
        if (!_seekDragging)
        {
            _seek.Value = Math.Clamp(attended?.Playhead.Milliseconds ?? 0, _seek.Minimum, _seek.Maximum);
        }
        _time.Text = attended is null
            ? "0:00 / -0:00"
            : $"{Presenter.Fmt(attended.Playhead)} / -{Presenter.FmtRemaining(attended.Remaining)}";

        _addProgress.Text = Presenter.FmtAdding(view.Adding);
        _addProgress.Available = view.Adding is not null;
        _cancelAdd.Available = view.Adding is not null;

        _editor.Refresh(view);
    }

    /// <summary>Render runs on every tick, and resetting a tip that is showing would close it.</summary>
    private void ShowTip(Control control, string tip)
    {
        if (_tips.GetToolTip(control) != tip)
        {
            _tips.SetToolTip(control, tip);
        }
    }

    /// <summary>A background add publishes once per file, so rows the list already shows in
    /// the same places are kept and only the new ones are made: a whole library would otherwise
    /// rebuild the list once per file, and the window would stop answering.</summary>
    private void RebuildRows(PlayerView view)
    {
        _queueList.BeginUpdate();
        // A row keeps its selection wherever it moves to.
        var selected = _queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToHashSet();
        var kept = 0;
        while (kept < _queueList.Items.Count && kept < view.Queue.Count && RowOf(_queueList.Items[kept]) == view.Queue[kept].Id)
        {
            kept++;
        }
        while (_queueList.Items.Count > kept)
        {
            _queueList.Items.RemoveAt(_queueList.Items.Count - 1);
        }
        foreach (var row in view.Queue.Skip(kept))
        {
            var item = new ListViewItem((++kept).ToString()) { Tag = row.Id };
            item.SubItems.Add(string.IsNullOrEmpty(row.Track.Title) ? Path.GetFileName(row.Track.FilePath) : row.Track.Title);
            item.SubItems.Add(row.Track.Artist);
            item.SubItems.Add(Presenter.Fmt(row.Track.Length));
            _queueList.Items.Add(item);
            item.Selected = selected.Contains(row.Id);
        }
        _queueList.EndUpdate();
    }

    /// <summary>Every notice, the engine's and the shell's own, goes into the session's list;
    /// the strip shows the latest and how many the list holds.</summary>
    private void ShowNotice(string message)
    {
        if (IsDisposed)
        {
            return;
        }
        _notices.Add(message);
        _status.Text = Presenter.FmtNotice(_notices.Latest!);
        _status.ToolTipText = _status.Text; // in full, where the strip clips it
        _noticeCount.Text = Presenter.FmtNoticeCount(_notices.Entries.Count);
        _noticeCount.Available = true;
        if (_noticeList is { IsDisposed: false } list)
        {
            list.ShowEntries(_notices, selectLatest: false);
        }
    }

    /// <summary>Shows a list window. A test replaces it, since a headless form shows no window.</summary>
    internal Action<Form> ShowNoticeList { get; set; }

    /// <summary>Opens this session's notice list on the latest notice; one list, reused. The
    /// list hides when the user closes it, but a close from elsewhere disposes it.</summary>
    private void OpenNotices()
    {
        if (_notices.Latest is null)
        {
            return;
        }
        if (_noticeList is null || _noticeList.IsDisposed)
        {
            _noticeList = new NoticeListForm();
        }
        _noticeList.ShowEntries(_notices, selectLatest: true);
        ShowNoticeList(_noticeList);
    }

    // ── Commands: one intent, one engine call ───────────────────────────────────────────

    /// <summary>The paths picked in the Add files dialog, or none. A test replaces it, since a
    /// headless form cannot show a dialog.</summary>
    internal Func<IReadOnlyList<string>> ChooseFiles { get; set; }

    /// <summary>The folder picked in the Add folder dialog, or none (one at a time: the dialog
    /// picks several only from .NET 9; dropping takes any number). The engine lists them; the
    /// shell only passes them on.</summary>
    internal Func<IReadOnlyList<string>> ChooseFolders { get; set; }

    /// <summary>The playlists picked in the Open playlist dialog, or none. Opening one is adding
    /// it: the engine reads it and appends its tracks; there is no "replace the queue".</summary>
    internal Func<IReadOnlyList<string>> ChoosePlaylists { get; set; }

    /// <summary>The path picked in the Save as playlist dialog, or null.</summary>
    internal Func<string?> ChoosePlaylistSavePath { get; set; }

    /// <summary>Whether the user confirms clearing a queue of this many rows. A test replaces
    /// it, since a headless form cannot show a dialog.</summary>
    internal Func<int, bool> ConfirmClear { get; set; }

    /// <summary>Starts a drag of the selected rows. A test replaces it, since a headless form
    /// cannot enter the modal drag loop.</summary>
    internal Action<IDataObject> StartDrag { get; set; }

    private void AddChosen(Func<IReadOnlyList<string>> choose)
    {
        var paths = choose();
        if (paths.Count > 0)
        {
            _ = AddPaths(paths);
        }
    }

    private IReadOnlyList<string> ShowFilesDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Add audio files",
            Filter = "Promised audio (WAV, FLAC, MP3)|*.wav;*.flac;*.mp3|All files|*.*",
            Multiselect = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : Array.Empty<string>();
    }

    private IReadOnlyList<string> ShowFolderDialog()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Add every WAV, FLAC and MP3 in a folder and its subfolders",
            UseDescriptionForTitle = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? new[] { dialog.SelectedPath } : Array.Empty<string>();
    }

    private IReadOnlyList<string> ShowPlaylistDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Open playlist",
            Filter = "Playlists (M3U, M3U8)|*.m3u;*.m3u8|All files|*.*",
            Multiselect = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : Array.Empty<string>();
    }

    private string? ShowSavePlaylistDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Save the queue as a playlist",
            Filter = "UTF-8 playlist (M3U8)|*.m3u8",
            DefaultExt = "m3u8",
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>The engine writes the file and says how it went; the shell never reads the queue
    /// to write it.</summary>
    private void SaveAsPlaylist()
    {
        if (ChoosePlaylistSavePath() is { } path)
        {
            _engine.SaveAsPlaylist(path);
        }
    }

    /// <summary>Returns at once; rows, progress and the closing notice arrive through the sink.
    /// The task is for the launch arguments, which must wait for the rows before --play.</summary>
    internal Task AddPaths(IEnumerable<string> paths) => _engine.Add(paths);

    internal static RowId RowOf(ListViewItem item) => (RowId)item.Tag!;

    /// <summary>The selected rows go in one call; what that does to playback is the engine's.</summary>
    private void RemoveSelected() =>
        _engine.Remove(_queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToList());

    private void DragSelectedRows()
    {
        var rows = _queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToArray();
        if (rows.Length > 0)
        {
            StartDrag(new DataObject(RowsFormat, rows));
            EndRowDrag();
        }
    }

    private void EndRowDrag()
    {
        _dragScroll.Stop();
        _dropLine.Visible = false;
    }

    /// <summary>Rows move and show where they will land; files copy in; anything else is
    /// refused.</summary>
    private void DragOverList(DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(RowsFormat) == true)
        {
            e.Effect = DragDropEffects.Move;
            _dragScroll.Start();
            ShowDropLine(DropIndex(ClientPoint(e)));
        }
        else
        {
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        }
    }

    private System.Drawing.Point ClientPoint(DragEventArgs e) =>
        _queueList.PointToClient(new System.Drawing.Point(e.X, e.Y));

    /// <summary>The list position a drop at the cursor lands in: before the row under it when
    /// over its upper half, after it when over its lower half, and after the last row when
    /// below them all. Only the height counts: past the last column is still that row.</summary>
    private int DropIndex(System.Drawing.Point client)
    {
        if (RowAtHeight(client.Y) is { } item)
        {
            var bounds = item.Bounds;
            return client.Y < bounds.Top + bounds.Height / 2 ? item.Index : item.Index + 1;
        }
        return _queueList.TopItem is { } top && client.Y < top.Bounds.Top ? top.Index : _queueList.Items.Count;
    }

    private ListViewItem? RowAtHeight(int y) => _queueList.HitTest(2, y).Item;

    /// <summary>A drag held near the top or bottom edge scrolls the list a row at a time, so a
    /// row can be dragged past what is in view.</summary>
    private void ScrollNearEdge(System.Drawing.Point client)
    {
        if (_queueList.TopItem is not { } top)
        {
            return;
        }
        var margin = top.Bounds.Height;
        if (client.Y < top.Bounds.Top + margin && top.Index > 0)
        {
            _queueList.EnsureVisible(top.Index - 1);
        }
        else if (client.Y > _queueList.ClientSize.Height - margin
            && RowAtHeight(_queueList.ClientSize.Height - 1) is { } bottom
            && bottom.Index + 1 < _queueList.Items.Count)
        {
            _queueList.EnsureVisible(bottom.Index + 1);
        }
    }

    private void ShowDropLine(int index)
    {
        var count = _queueList.Items.Count;
        if (count == 0)
        {
            _dropLine.Visible = false;
            return;
        }
        var y = index < count ? _queueList.Items[index].Bounds.Top - 1 : _queueList.Items[count - 1].Bounds.Bottom - 1;
        _dropLine.SetBounds(0, y, _queueList.ClientSize.Width, _dropLine.Height);
        _dropLine.Visible = true;
        _dropLine.BringToFront();
    }

    /// <summary>One drop, one call: the drop position is passed as the row the dragged rows
    /// land before, or none for the end. Where they land, and what that does to playback, is
    /// the engine's.</summary>
    private void MoveRows(IReadOnlyList<RowId> rows, int index) =>
        _engine.Move(rows, index < _queueList.Items.Count ? RowOf(_queueList.Items[index]) : null);

    /// <summary>There is no undo, so a queue of more than one row asks first.</summary>
    private void ClearQueue()
    {
        var count = _engine.View.Queue.Count;
        if (count > 1 && !ConfirmClear(count))
        {
            return;
        }
        _engine.Clear();
    }

    private bool ShowClearDialog(int count) =>
        MessageBox.Show(
            this,
            $"Remove all {count} rows from the queue? This cannot be undone.",
            "Clear queue",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.OK;

    private void PlaySelected()
    {
        if (_queueList.SelectedItems.Count == 1)
        {
            _engine.PlayRow(RowOf(_queueList.SelectedItems[0]));
        }
    }

    internal void PlayRowAt(int index) => _engine.PlayRow(RowOf(_queueList.Items[index]));

    internal Adjacency? SelectedAdjacency() =>
        _queueList.SelectedIndices.Count == 1 ? Presenter.AdjacencyAt(_engine.View, _queueList.SelectedIndices[0]) : null;

    internal Adjacency? AdjacencyAt(int rowIndex) => Presenter.AdjacencyAt(_engine.View, rowIndex);

    private void EditSelectedTransition()
    {
        if (SelectedAdjacency() is { } adjacency)
        {
            _editor.Edit(adjacency);
        }
    }

    internal void EditTransitionAt(int rowIndex)
    {
        if (AdjacencyAt(rowIndex) is { } adjacency)
        {
            _editor.Edit(adjacency);
        }
    }

    private string? ChooseRenderPath(string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Render this boundary",
            Filter = "WAV|*.wav",
            DefaultExt = "wav",
            FileName = suggestedName,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }
}

/// <summary>A status label that takes the strip's spare width and asks for none of its own. Sized
/// to its text, a long notice would push the notice count and the add's Cancel off the strip.
/// </summary>
internal sealed class SpringLabel : ToolStripStatusLabel
{
    public SpringLabel(string text) : base(text) => Spring = true;

    public override Size GetPreferredSize(Size constrainingSize) =>
        base.GetPreferredSize(constrainingSize) with { Width = 0 };
}
