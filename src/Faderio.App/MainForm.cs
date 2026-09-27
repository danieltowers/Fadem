using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;

namespace Faderio.App;

/// <summary>
/// The WinForms shell for the slice (ticket 05): add files or a folder, or drop either on the
/// list (build issue 02), import a playlist into the library or export the open one (playlists
/// issue 03, which replaced build issue 03's open and save), a
/// multi-select list whose rows drag to a new place (build issue 05), a context menu that opens
/// the editor on this → next, previous → this or, under repeat-one, this → this (build issue
/// 15) and a button at each row's far right that opens this → next (interim transport 07), with
/// Remove and Clear queue (build issue 04; Delete removes too), transport with a
/// shuffle toggle and a repeat button (build issue 07), a seek bar in milliseconds, the
/// transition editor, a status strip showing the latest notice and a count that opens this
/// session's notice list (build issue 09), a Settings… dialog (build issue 12), and the clip
/// light, which a click clears (build issue 17), Open library… with the library named in the
/// title (library issue 02), the window opening where it was closed (library issue 03), and the
/// playlist sidebar on the left (playlists issue 02), the open playlist named above the queue
/// and in the title (playlists issue 03), and Transitions…, which opens the transition manager,
/// as the queue's menu does on a row's track (playlists issue 05), and the queue's columns, chosen
/// from the header's menu, dragged into order and sized, kept per library (playlists issue 06). It
/// renders <see cref="PlayerView"/>s and issues commands; it holds no engine state between calls
/// (the placement rule in CLAUDE.md). The seek bar's Maximum is the track length in milliseconds,
/// so no scaling exists to get wrong (ticket 06).
/// </summary>
internal sealed class MainForm : Form
{
    private readonly PlaybackEngine _engine;
    private readonly Func<AudioOutputMode?> _outputModeInUse;
    private readonly string? _windowFile;

    internal readonly ListView _queueList;
    internal readonly SeekBar _seek;
    internal readonly Button _back5;
    internal readonly Button _forward5;
    internal readonly Label _time;
    internal readonly Button _addFiles;
    internal readonly Button _addFolder;
    internal readonly Button _importPlaylist;
    internal readonly Button _exportPlaylist;
    internal readonly Button _openTransitions;
    internal readonly Button _settings;
    internal readonly Button _openLibrary;
    internal readonly Button _prev;
    internal readonly Button _playPause;
    internal readonly Button _next;
    internal readonly Button _transitionNow;
    internal readonly CheckBox _shuffle;
    internal readonly CheckBox _repeat;
    internal readonly ToolTip _tips;
    internal readonly ToolStripStatusLabel _status;
    internal readonly ToolStripStatusLabel _noticeCount;
    internal readonly ToolStripStatusLabel _addProgress;
    internal readonly ToolStripButton _cancelAdd;
    internal readonly ToolStripStatusLabel _clip;
    internal readonly TransitionEditorPanel _editor;
    internal readonly PlaylistSidebar _playlists;
    internal readonly PlaylistNameBar _playlistName;
    internal readonly ToolStripMenuItem _editNext;
    internal readonly ToolStripMenuItem _editPrevious;
    internal readonly ToolStripMenuItem _editSelf;
    internal readonly ToolStripMenuItem _transitionsForTrack;
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
    internal TransitionManagerForm? _manager;

    private long _shownQueue = -1;

    /// <summary>The header's menu: a tick for each column (playlists issue 06).</summary>
    internal readonly ContextMenuStrip _columnMenu;
    private readonly System.Windows.Forms.Timer _saveColumns;

    /// <summary>The columns shown and where their layout is kept: view state, the shell's own, as
    /// the window's geometry is. The file is the open library's, or none without one.</summary>
    private ColumnLayout _columns;
    private string? _columnsFile;
    private bool _columnsRead;
    private bool _columnsChanged;
    private bool _buildingColumns;

    /// <summary>The width the shell last gave each column. The list reports every width change,
    /// among them its own replay of these when its handle is made, so only a width that differs
    /// is one the user dragged.</summary>
    private readonly Dictionary<QueueColumn, int> _widthsSet = new();

    /// <param name="outputModeInUse">The mode the output opened in, or null when none could be:
    /// the output adapter's, for the settings dialog.</param>
    /// <param name="windowFile">The machine's window.json: where the window opens, and where it
    /// is written when the window closes. Null keeps no geometry: the window opens centred.</param>
    public MainForm(PlaybackEngine engine, PostingSink sink, Func<AudioOutputMode?> outputModeInUse, string? windowFile = null)
    {
        _engine = engine;
        _outputModeInUse = outputModeInUse;
        _windowFile = windowFile;
        ChooseFiles = ShowFilesDialog;
        ChooseFolders = ShowFolderDialog;
        ChoosePlaylists = ShowPlaylistDialog;
        ChooseExportPath = ShowExportDialog;
        ConfirmClear = ShowClearDialog;
        ChooseLibrary = () => ShowLibraryDialog(this);
        AskUnsavedEdits = ShowUnsavedDialog;
        ShowSettings = dialog => dialog.ShowDialog(this) == DialogResult.OK;
        StartDrag = data => _queueList!.DoDragDrop(data, DragDropEffects.Move);
        ShowNoticeList = ShowModeless;
        ShowTransitionManager = ShowModeless;

        Text = "faderio";
        // The sidebar's 200 px on top of what the queue and the editor need: at the least width the
        // queue still shows the default columns, the row buttons in view.
        MinimumSize = new Size(960, 480);
        Size = new Size(1240, 600);
        StartPosition = FormStartPosition.CenterScreen;

        _status = new SpringLabel("Ready.") { TextAlign = ContentAlignment.MiddleLeft };
        _noticeCount = new ToolStripStatusLabel { Available = false, IsLink = true, ToolTipText = "Show this session's notices" };
        _addProgress = new ToolStripStatusLabel { Available = false };
        _cancelAdd = new ToolStripButton("Cancel") { Available = false, DisplayStyle = ToolStripItemDisplayStyle.Text };
        // The clip light (build issue 17): dark until the engine latches a clip.
        _clip = new ToolStripStatusLabel { ForeColor = SystemColors.GrayText, Margin = new Padding(8, 3, 0, 2) };
        var statusStrip = new StatusStrip { Items = { _status, _noticeCount, _addProgress, _cancelAdd, _clip }, ShowItemToolTips = true };

        _addFiles = new Button { Text = "Add files…", Width = 90, Height = 28, Margin = new Padding(4, 8, 4, 0) };
        _addFolder = new Button { Text = "Add folder…", Width = 90, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        _importPlaylist = new Button { Text = "Import playlist…", Width = 112, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        _exportPlaylist = new Button { Text = "Export playlist…", Width = 112, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        _openTransitions = new Button { Text = "Transitions…", Width = 96, Height = 28, Margin = new Padding(16, 8, 4, 0) };
        _openLibrary = new Button { Text = "Open library…", Width = 100, Height = 28, Margin = new Padding(16, 8, 4, 0) };
        _settings = new Button { Text = "Settings…", Width = 90, Height = 28, Margin = new Padding(0, 8, 4, 0) };
        // The queue's own commands get a row of their own, so the seek bar keeps its travel.
        var queueBar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, WrapContents = false };
        queueBar.Controls.AddRange(new Control[] { _addFiles, _addFolder, _importPlaylist, _exportPlaylist, _openTransitions, _openLibrary, _settings });
        var symbols = new Font("Segoe UI Symbol", 9f);
        _prev = new Button { Text = "⏮", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _playPause = new Button { Text = "▶", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _next = new Button { Text = "⏭", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // Arrows draw small in this font; at 13 pt the wave arrow reads as clearly as ⏭.
        _transitionNow = new Button { Text = "⤳", Font = new Font("Segoe UI Symbol", 13f), Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // Push buttons that stay pressed in while on. They never check themselves: what they
        // show is always the view's, set in Render.
        _shuffle = new CheckBox { Text = "🔀", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(10, 8, 2, 4) };
        _repeat = new CheckBox { Text = "🔁", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        _tips = new ToolTip();
        _tips.SetToolTip(_transitionNow, "Transition now");
        // Either side of the seek bar (interim transport issue 05): fixed five-second jumps.
        _back5 = new Button { Text = "−5 s", Width = 48, Height = 28, Margin = new Padding(8, 8, 0, 4), Enabled = false };
        _forward5 = new Button { Text = "+5 s", Width = 48, Height = 28, Margin = new Padding(0, 8, 2, 4), Enabled = false };
        _tips.SetToolTip(_back5, "Back 5 seconds");
        _tips.SetToolTip(_forward5, "Forward 5 seconds");
        _seek = new SeekBar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 6, 8, 4),
        };
        _time = new Label
        {
            Text = "0:00 / -0:00",
            Width = 170,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
        };
        // A table, not anchors: the seek bar takes whatever width is left between the buttons
        // and the readout, at any window size.
        var transport = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 10, RowCount = 1 };
        foreach (var _ in Enumerable.Range(0, 7))
        {
            transport.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        transport.Controls.Add(_prev, 0, 0);
        transport.Controls.Add(_playPause, 1, 0);
        transport.Controls.Add(_next, 2, 0);
        transport.Controls.Add(_transitionNow, 3, 0);
        transport.Controls.Add(_shuffle, 4, 0);
        transport.Controls.Add(_repeat, 5, 0);
        transport.Controls.Add(_back5, 6, 0);
        transport.Controls.Add(_seek, 7, 0);
        transport.Controls.Add(_forward5, 8, 0);
        transport.Controls.Add(_time, 9, 0);

        _queueList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            AllowDrop = true,
            ShowItemToolTips = true,
        };
        // The columns (playlists issue 06): the default four until the library's own layout is
        // read, at the first render.
        _columns = ColumnLayout.Default;
        BuildColumns();
        _queueList.ColumnWidthChanging += (_, e) =>
        {
            if (e.ColumnIndex == EditColumn)
            {
                e.NewWidth = _queueList.Columns[EditColumn].Width;
                e.Cancel = true;
            }
            else if (e.NewWidth < ColumnLayout.MinWidth)
            {
                // Dragged shut, a column is hidden without a tick to say so; the header menu hides.
                e.NewWidth = ColumnLayout.MinWidth;
                e.Cancel = true;
            }
        };
        // A header dragged to a new place: the layout moves it and the columns are made again in
        // its order, so # stays first and the editor button last. The list is not left to move it.
        _queueList.ColumnReordered += (_, e) =>
        {
            e.Cancel = true;
            if (e.Header?.Tag is QueueColumn column)
            {
                var to = e.NewDisplayIndex;
                BeginInvoke(() => MoveColumn(column, to));
            }
        };
        _columnMenu = new ContextMenuStrip();
        foreach (var column in Enum.GetValues<QueueColumn>())
        {
            var item = new ToolStripMenuItem(Presenter.ColumnHeading(column))
            {
                Tag = column,
                Enabled = column != QueueColumn.Number, // # is always shown
            };
            item.Click += (_, _) => ToggleColumn(column);
            _columnMenu.Items.Add(item);
        }
        _columnMenu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in _columnMenu.Items)
            {
                item.Checked = _columns[(QueueColumn)item.Tag!].Visible;
            }
        };
        _saveColumns = new System.Windows.Forms.Timer { Interval = 400 };
        _saveColumns.Tick += (_, _) => SaveColumns();
        // The title takes what the other columns leave, so the button stays at the far right
        // at any width, not scrolled out of sight.
        _queueList.Resize += (_, _) =>
        {
            if (_queueList.IsHandleCreated)
            {
                // Once the list has finished sizing: fitted while it sizes, it keeps a
                // horizontal scroll bar for the wider columns it had.
                _queueList.BeginInvoke(FitColumns);
            }
            else
            {
                FitColumns();
            }
        };
        _queueList.ColumnWidthChanged += (_, e) =>
        {
            if (_buildingColumns || _queueList.Columns[e.ColumnIndex].Tag is not QueueColumn column)
            {
                return;
            }
            // The user's width, kept; one the fit gives the title or takes from the artist is not.
            var width = _queueList.Columns[e.ColumnIndex].Width;
            if (_widthsSet.GetValueOrDefault(column) != width && _columns[column].Width != width)
            {
                _widthsSet[column] = width;
                _columns = _columns.Resize(column, width);
                _columnsChanged = true;
                // A drag changes the width at every step, so the file is written once it rests.
                _saveColumns.Stop();
                _saveColumns.Start();
            }
            if (column is not QueueColumn.Title and not QueueColumn.Artist)
            {
                FitColumns();
            }
        };
        // The list's own size draws the pencil at eight pixels; this size fits the row.
        _buttonFont = new Font("Segoe UI Symbol", 11f);
        _authoredFont = new Font(_buttonFont, FontStyle.Bold);
        Disposed += (_, _) =>
        {
            // After the list, which draws with them.
            _buttonFont.Dispose();
            _authoredFont.Dispose();
        };

        _editNext = new ToolStripMenuItem("Edit transition: this → next");
        _editPrevious = new ToolStripMenuItem("Edit transition: previous → this");
        _editSelf = new ToolStripMenuItem("Edit transition: this → this");
        _transitionsForTrack = new ToolStripMenuItem("Transitions for this track…");
        _removeRows = new ToolStripMenuItem("Remove") { ShortcutKeyDisplayString = "Del" };
        _clearQueue = new ToolStripMenuItem("Clear queue");
        var menu = new ContextMenuStrip
        {
            Items = { _editNext, _editPrevious, _editSelf, _transitionsForTrack, new ToolStripSeparator(), _removeRows, _clearQueue },
        };
        menu.Opening += (_, e) =>
        {
            // The list shows its menu for a right-click on the header too: there, the columns'.
            if (IsOverHeader(Cursor.Position))
            {
                // Shown once this menu's opening is over: shown from inside it, it closes at once.
                e.Cancel = true;
                var at = Cursor.Position;
                BeginInvoke(() => _columnMenu.Show(at));
                return;
            }
            var adjacencies = SelectedAdjacencies();
            _editNext.Enabled = adjacencies.Next is not null;
            _editPrevious.Enabled = adjacencies.Previous is not null;
            _editSelf.Available = adjacencies.Self is not null; // shown while repeat-one is on
            _transitionsForTrack.Enabled = _queueList.SelectedItems.Count == 1;
            _removeRows.Enabled = _queueList.SelectedItems.Count > 0;
            _clearQueue.Enabled = _queueList.Items.Count > 0;
        };
        _editNext.Click += (_, _) => EditSelected(t => t.Next);
        _editPrevious.Click += (_, _) => EditSelected(t => t.Previous);
        _editSelf.Click += (_, _) => EditSelected(t => t.Self);
        _transitionsForTrack.Click += (_, _) => OpenTransitions();
        _removeRows.Click += (_, _) => RemoveSelected();
        _clearQueue.Click += (_, _) => ClearQueue();
        _queueList.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && e.Modifiers == Keys.None)
            {
                RemoveSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.F2 && e.Modifiers == Keys.None)
            {
                _playlistName!.StartRename(); // the queue is the open playlist: F2 renames it (D18)
                e.Handled = true;
            }
        };
        _queueList.ContextMenuStrip = menu;
        _queueList.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && EditButtonAt(e.Location) is { } index)
            {
                EditTransitionAt(index);
            }
        };
        // A double-click on the button is two presses of it, not a request to play the row.
        _queueList.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && EditButtonAt(e.Location) is null)
            {
                PlaySelected();
            }
        };
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

        _editor = new TransitionEditorPanel(engine, ShowNotice, ChooseRenderPath, a => AskUnsavedEdits(a));
        _playlists = new PlaylistSidebar(engine, LeaveEditor);
        _playlistName = new PlaylistNameBar(engine);

        // Docking order: the side panels and bottom strip first, then the name over the queue,
        // between them, and the fill-docked list last.
        Controls.Add(_queueList);
        Controls.Add(_playlistName);
        Controls.Add(_playlists);
        Controls.Add(_editor);
        Controls.Add(statusStrip);
        Controls.Add(transport);
        Controls.Add(queueBar);

        _addFiles.Click += (_, _) => AddChosen(ChooseFiles);
        _addFolder.Click += (_, _) => AddChosen(ChooseFolders);
        _importPlaylist.Click += (_, _) => ImportPlaylists();
        _exportPlaylist.Click += (_, _) => ExportPlaylist();
        _openTransitions.Click += (_, _) => OpenTransitions();
        _openLibrary.Click += (_, _) => OpenLibrary();
        _settings.Click += (_, _) => EditSettings();
        _cancelAdd.Click += (_, _) => _engine.CancelAdd();
        _status.Click += (_, _) => OpenNotices();
        _noticeCount.Click += (_, _) => OpenNotices();
        _clip.Click += (_, _) => _engine.ClearClip();
        _playPause.Click += (_, _) => _engine.TogglePlayPause(SingleSelectedRow());
        _prev.Click += (_, _) => _engine.Previous();
        _next.Click += (_, _) => _engine.Next();
        _transitionNow.Click += (_, _) => _engine.TransitionNow();
        // The press asks for the state after the one the view holds now, not the one last shown.
        _shuffle.Click += (_, _) => _engine.SetShuffle(!_engine.View.Shuffle);
        _repeat.Click += (_, _) => _engine.SetRepeat(Presenter.NextRepeat(_engine.View.Repeat));
        _seek.SeekRequested += playhead => _engine.Seek(playhead);
        _back5.Click += (_, _) => _engine.SeekBy(TimeSpan.FromSeconds(-5));
        _forward5.Click += (_, _) => _engine.SeekBy(TimeSpan.FromSeconds(5));

        sink.Changed += Render;
        sink.Noticed += ShowNotice;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Render(_engine.View);
        _timer.Start();

        FormClosing += (_, e) =>
        {
            // Closing the window closes the editor, so unsaved edits ask first.
            if (!_editor.ConfirmLeave())
            {
                e.Cancel = true;
                return;
            }
            _timer.Stop();
            SaveColumns(); // a width still settling
            _saveColumns.Dispose();
            _tips.Dispose();
            if (_windowFile is not null)
            {
                WindowGeometry.Of(this).Save(_windowFile);
            }
            _engine.Dispose();
        };

        PlaceWindow();
        Render(_engine.View);
    }

    /// <summary>Opens the window where it was last closed (library issue 03), if that is still on
    /// a screen; else at the default size, centred.</summary>
    private void PlaceWindow()
    {
        if (_windowFile is null || WindowGeometry.Load(_windowFile) is not { } placed ||
            !placed.IsOnScreen(Screen.AllScreens.Select(screen => screen.WorkingArea)))
        {
            return;
        }
        StartPosition = FormStartPosition.Manual;
        Bounds = placed.Bounds;
        if (placed.Maximised)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    // ── Rendering: read, render, forget ─────────────────────────────────────────────────

    internal void Render() => Render(_engine.View);

    internal void Render(PlayerView view)
    {
        if (IsDisposed)
        {
            return;
        }
        var title = Presenter.Title(view.OpenPlaylist, view.Library, _outputModeInUse(), _engine.SampleRate);
        if (Text != title)
        {
            Text = title;
        }
        _playPause.Text = Presenter.PlayPauseGlyph(view.Transport);
        _transitionNow.Enabled = view.CanTransitionNow;
        _shuffle.Checked = view.Shuffle;
        _repeat.Checked = view.Repeat != RepeatMode.Off;
        _repeat.Text = Presenter.RepeatGlyph(view.Repeat);
        ShowTip(_shuffle, Presenter.ShuffleTip(view.Shuffle));
        ShowTip(_repeat, Presenter.RepeatTip(view.Repeat));

        var columnsFile = ColumnLayout.FileIn(view.Library);
        if (!_columnsRead || columnsFile != _columnsFile)
        {
            // Another library, or the first render: the layout left is written, and this one read.
            SaveColumns();
            _columnsFile = columnsFile;
            _columnsRead = true;
            ShowColumns(columnsFile is null ? ColumnLayout.Default : ColumnLayout.Load(columnsFile));
            _shownQueue = -1;
        }
        if (view.QueueVersion != _shownQueue)
        {
            RebuildRows(view, SelectedRows());
            _shownQueue = view.QueueVersion;
        }
        var editColumn = EditColumn;
        for (var i = 0; i < _queueList.Items.Count && i < view.Queue.Count; i++)
        {
            var row = view.Queue[i];
            var item = _queueList.Items[i];
            // Render runs on every tick: each row's look is set only when it changes (build issue 20).
            var back = !row.IsCurrent ? SystemColors.Window
                : view.Transport == Transport.Stopped ? RestingMarker
                : SystemColors.Info;
            var fore = row.IsUnreadable ? SystemColors.GrayText : SystemColors.WindowText;
            // Each cell is styled on its own, for the editor button's: bold while its adjacency
            // is authored, grey and plain while it is not.
            var authored = row.NextAuthored == true;
            foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
            {
                if (cell.BackColor != back)
                {
                    cell.BackColor = back;
                }
                var cellFore = cell == item.SubItems[editColumn] && !authored ? SystemColors.GrayText : fore;
                if (cell.ForeColor != cellFore)
                {
                    cell.ForeColor = cellFore;
                }
            }
            var button = item.SubItems[editColumn];
            var glyph = Presenter.EditGlyph(row.NextAuthored, isLast: i == view.Queue.Count - 1);
            if (button.Text != glyph)
            {
                button.Text = glyph;
            }
            var font = authored ? _authoredFont : _buttonFont;
            if (button.Font != font)
            {
                button.Font = font;
            }
            var label = Presenter.RowLabel(i + 1, row.IsUnreadable);
            if (item.Text != label)
            {
                item.Text = label;
                item.ToolTipText = Presenter.RowTip(row.IsUnreadable);
            }
        }

        var attended = view.Attended;
        _seek.Show(attended?.Playhead ?? Playhead.Zero, attended?.Length);
        _back5.Enabled = _forward5.Enabled = _seek.Enabled;
        _time.Text = Presenter.TimeReadout(attended, held: view.Preview is not null);

        _addProgress.Text = Presenter.FmtAdding(view.Adding);
        _addProgress.Available = view.Adding is not null;
        _cancelAdd.Available = view.Adding is not null;
        ShowClip(view.Clip);

        _editor.Refresh(view);
        _playlists.Render(view);
        _playlistName.Render(view);
        _manager?.Render(view);
    }

    /// <summary>The clip light: dark, or lit red with the count and the latest cause. Set only when
    /// its text changes, as Render runs on every tick. It is kept short enough that a long cause
    /// never pushes the notice count off the strip; the tooltip has it whole.</summary>
    private void ShowClip(ClipView? clip)
    {
        var text = Presenter.FmtClip(clip);
        if (_clip.Text == text)
        {
            return;
        }
        _clip.Text = text;
        _clip.ToolTipText = Presenter.ClipTip(clip);
        _clip.BackColor = clip is null ? Color.Empty : Color.Firebrick;
        _clip.ForeColor = clip is null ? SystemColors.GrayText : Color.White;
        _clip.AutoSize = true;
        var width = Math.Min(_clip.GetPreferredSize(Size.Empty).Width, MaxClipWidth);
        _clip.AutoSize = false;
        _clip.Width = width;
    }

    private const int MaxClipWidth = 320;

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
    private void RebuildRows(PlayerView view, IReadOnlySet<RowId> selected)
    {
        _queueList.BeginUpdate();
        // A row keeps its selection wherever it moves to.
        var shown = _columns.Shown;
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
            // # is the row's own text; each other column shown is a cell, then the editor button.
            var item = new ListViewItem((++kept).ToString()) { Tag = row.Id, UseItemStyleForSubItems = false };
            foreach (var column in shown.Skip(1))
            {
                item.SubItems.Add(Presenter.QueueCell(column.Column, row.Track));
            }
            item.SubItems.Add(""); // the editor button, drawn by Render
            _queueList.Items.Add(item);
            item.Selected = selected.Contains(row.Id);
        }
        _queueList.EndUpdate();
    }

    private IReadOnlySet<RowId> SelectedRows() =>
        _queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToHashSet();

    // ── Columns (playlists issue 06): shell view state, kept per library ────────────────

    /// <summary>Makes the header's columns from the layout: the ones shown, in its order, at its
    /// widths, then the editor button.</summary>
    private void BuildColumns()
    {
        _buildingColumns = true;
        try
        {
            _queueList.Columns.Clear();
            _widthsSet.Clear();
            foreach (var setting in _columns.Shown)
            {
                _widthsSet[setting.Column] = setting.Width;
                var header = _queueList.Columns.Add(Presenter.ColumnHeading(setting.Column), setting.Width, AlignmentOf(setting.Column));
                header.Tag = setting.Column;
            }
            // The editor button (interim transport 07): fixed at the far right, never one of the
            // columns a user shows, sizes or moves.
            _queueList.Columns.Add("", 28, HorizontalAlignment.Center);
            _queueList.AllowColumnReorder = true;
        }
        finally
        {
            _buildingColumns = false;
        }
        FitColumns();
    }

    private static HorizontalAlignment AlignmentOf(QueueColumn column) =>
        column is QueueColumn.Length or QueueColumn.ReplayGain ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>Shows <paramref name="layout"/>: the rows are made again, since each has a cell per
    /// column shown, and keep their selection.</summary>
    private void ShowColumns(ColumnLayout layout)
    {
        var selected = SelectedRows();
        _columns = layout;
        _queueList.BeginUpdate();
        _queueList.Items.Clear();
        BuildColumns();
        _queueList.EndUpdate();
        RebuildRows(_engine.View, selected);
        _shownQueue = _engine.View.QueueVersion;
    }

    /// <summary>A tick in the header's menu: the column shows or hides, and the layout is written.</summary>
    internal void ToggleColumn(QueueColumn column) => ChangeColumns(_columns.Toggle(column));

    /// <summary>A header dropped at <paramref name="displayIndex"/>: # stays first and the editor
    /// button last, so a drop on either lands beside it.</summary>
    internal void MoveColumn(QueueColumn column, int displayIndex) => ChangeColumns(_columns.Move(column, displayIndex));

    private void ChangeColumns(ColumnLayout layout)
    {
        if (layout.Same(_columns))
        {
            return;
        }
        ShowColumns(layout);
        Render();
        _columnsChanged = true;
        SaveColumns();
    }

    /// <summary>Writes the layout to the open library's columns.json, if a change is waiting to be.</summary>
    internal void SaveColumns()
    {
        _saveColumns.Stop();
        if (!_columnsChanged)
        {
            return;
        }
        _columnsChanged = false;
        if (_columnsFile is not null)
        {
            _columns.Save(_columnsFile);
        }
    }

    /// <summary>Whether a point on screen is on the queue's header, where a right-click asks for
    /// the columns rather than the rows.</summary>
    private bool IsOverHeader(System.Drawing.Point screen)
    {
        if (!_queueList.IsHandleCreated)
        {
            return false;
        }
        var header = NativeMethods.SendMessage(_queueList.Handle, NativeMethods.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        return header != IntPtr.Zero && NativeMethods.GetWindowRect(header, out var bounds)
            && screen.X >= bounds.Left && screen.X < bounds.Right && screen.Y >= bounds.Top && screen.Y < bounds.Bottom;
    }

    private static class NativeMethods
    {
        public const int LVM_GETHEADER = 0x1000 + 31;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);
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

    /// <summary>Shows the transition manager. A test replaces it, as <see cref="ShowNoticeList"/>.</summary>
    internal Action<Form> ShowTransitionManager { get; set; }

    /// <summary>Shows a window of this one's that stays open beside it, and brings it forward.</summary>
    private void ShowModeless(Form window)
    {
        if (!window.Visible)
        {
            window.Show(this);
        }
        window.Activate();
    }

    /// <summary>Opens the transition manager (playlists issue 05) on the selected row's track, from
    /// Transitions… and from the queue's menu; on the store's first track when no one row is
    /// selected, or its track is in no transition. One manager, reused.</summary>
    private void OpenTransitions()
    {
        if (_manager is null || _manager.IsDisposed)
        {
            _manager = new TransitionManagerForm(_engine, EditFromManager);
        }
        var row = SingleSelectedRow();
        _manager.Open(_engine.View.Queue.FirstOrDefault(r => r.Id == row)?.Track);
        ShowTransitionManager(_manager);
    }

    /// <summary>A double-click in the manager: the editor opens on that adjacency, in its mode,
    /// and this window comes forward to show it.</summary>
    private void EditFromManager(Adjacency adjacency)
    {
        Edit(adjacency);
        if (Visible)
        {
            Activate();
        }
    }

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

    /// <summary>The playlists picked in the Import playlist dialog, or none. The engine copies them
    /// into the library and opens the last; the shell never copies a file.</summary>
    internal Func<IReadOnlyList<string>> ChoosePlaylists { get; set; }

    /// <summary>The path picked in the Export playlist dialog, which opens on the file name given,
    /// or null.</summary>
    internal Func<string, string?> ChooseExportPath { get; set; }

    /// <summary>Whether the user confirms clearing a queue of this many rows. A test replaces
    /// it, since a headless form cannot show a dialog.</summary>
    internal Func<int, bool> ConfirmClear { get; set; }

    /// <summary>Starts a drag of the selected rows. A test replaces it, since a headless form
    /// cannot enter the modal drag loop.</summary>
    internal Action<IDataObject> StartDrag { get; set; }

    /// <summary>Shows the settings dialog and says whether the user pressed OK. A test replaces
    /// it, since a headless form cannot show a dialog.</summary>
    internal Func<SettingsForm, bool> ShowSettings { get; set; }

    /// <summary>The folder picked in the Open library dialog, or null. A test replaces it, since a
    /// headless form cannot show a dialog.</summary>
    internal Func<string?> ChooseLibrary { get; set; }

    /// <summary>Switches library (library issue 02): unsaved edits are settled first, then one
    /// call. The editor empties, since what it had open belongs to the library left.</summary>
    private void OpenLibrary()
    {
        if (!_editor.ConfirmLeave() || ChooseLibrary() is not { } folder)
        {
            return;
        }
        _editor.Close();
        _engine.OpenLibrary(folder);
    }

    /// <summary>Before the open playlist changes (playlists issue 02): unsaved edits are settled,
    /// then the editor empties, as what it had open was the queue left. False when the user
    /// cancels.</summary>
    private bool LeaveEditor()
    {
        if (!_editor.ConfirmLeave())
        {
            return false;
        }
        _editor.Close();
        return true;
    }

    /// <summary>Asks for a library folder, starting in Music (D12): at the first launch, before
    /// this window exists, and from Open library…. Null when cancelled.</summary>
    internal static string? ShowLibraryDialog(IWin32Window? owner)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a library folder: faderio keeps its transitions and settings in it",
            UseDescriptionForTitle = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    /// <summary>The dialog opens on the view's settings; OK sends each group the user changed to
    /// its own command, one call per group (library issue 03).</summary>
    private void EditSettings()
    {
        var view = _engine.View;
        using var dialog = new SettingsForm(view.MachineSettings, view.Settings, _outputModeInUse());
        if (ShowSettings(dialog))
        {
            if (dialog.MachineChanged)
            {
                _engine.ChangeMachineSettings(dialog.MachineChoice);
            }
            if (dialog.LibraryChanged)
            {
                _engine.ChangeSettings(dialog.LibraryChoice);
            }
        }
    }

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
            Title = "Import playlists into the library",
            Filter = "Playlists (M3U, M3U8)|*.m3u;*.m3u8",
            Multiselect = true,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : Array.Empty<string>();
    }

    private string? ShowExportDialog(string fileName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Export the playlist",
            Filter = "UTF-8 playlist (M3U8)|*.m3u8",
            DefaultExt = "m3u8",
            FileName = fileName,
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileName : null;
    }

    /// <summary>Import opens a playlist (D19), so unsaved edits are settled once something was
    /// picked; then one call for all of them.</summary>
    private void ImportPlaylists()
    {
        var paths = ChoosePlaylists();
        if (paths.Count > 0 && LeaveEditor())
        {
            _ = _engine.ImportPlaylists(paths); // the rows and the notice arrive through the sink
        }
    }

    /// <summary>The dialog opens on "&lt;open playlist&gt;.m3u8". The engine writes the queue as it
    /// is shown and says how it went; the library's own copy is not touched.</summary>
    private void ExportPlaylist()
    {
        if (ChooseExportPath($"{_engine.View.OpenPlaylist ?? "Playlist"}.m3u8") is { } path)
        {
            _engine.SaveAsPlaylist(path);
        }
    }

    /// <summary>Returns at once; rows, progress and the closing notice arrive through the sink.
    /// The task is for the launch arguments, which must wait for the rows before --play.</summary>
    internal Task AddPaths(IEnumerable<string> paths) => _engine.Add(paths);

    /// <summary>The attended row's mark while nothing sounds (interim transport 03): the sounding
    /// mark's hue, halfway to the window's grey, so it reads as the same mark, at rest.</summary>
    internal static readonly Color RestingMarker = Color.FromArgb(
        (SystemColors.Info.R + SystemColors.Control.R) / 2,
        (SystemColors.Info.G + SystemColors.Control.G) / 2,
        (SystemColors.Info.B + SystemColors.Control.B) / 2);

    internal static RowId RowOf(ListViewItem item) => (RowId)item.Tag!;

    /// <summary>The queue's last column: each row's editor button (interim transport 07), after
    /// every column shown (playlists issue 06).</summary>
    internal int EditColumn => _queueList.Columns.Count - 1;

    private const int MinTitleWidth = 100;
    private const int MinArtistWidth = 60;

    /// <summary>Sizes the title to the list's width less the other columns and room for a
    /// scroll bar, which appears without resizing the list. Where that would squeeze the title
    /// below its least, the artist gives way first, and takes back up to its own width as room
    /// returns. With the title hidden, nothing fills: the columns keep their widths.</summary>
    private void FitColumns()
    {
        var headers = _queueList.Columns.Cast<ColumnHeader>().ToList();
        if (headers.Find(c => c.Tag is QueueColumn.Title) is not { } title)
        {
            return;
        }
        var artist = headers.Find(c => c.Tag is QueueColumn.Artist);
        var free = _queueList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth
            - headers.Where(c => c != title && c != artist).Sum(c => c.Width);
        if (artist is not null)
        {
            var artistWidth = Math.Min(Math.Max(free - MinTitleWidth, MinArtistWidth), _columns[QueueColumn.Artist].Width);
            _widthsSet[QueueColumn.Artist] = artistWidth;
            if (artist.Width != artistWidth)
            {
                artist.Width = artistWidth;
            }
            free -= artistWidth;
        }
        var titleWidth = Math.Max(MinTitleWidth, free);
        _widthsSet[QueueColumn.Title] = titleWidth;
        if (title.Width != titleWidth)
        {
            title.Width = titleWidth;
        }
    }

    private readonly Font _buttonFont;
    private readonly Font _authoredFont;

    /// <summary>The row whose editor button is at <paramref name="location"/>, or null: a click
    /// anywhere else on a row, or on a row that shows no button, is not a press of it.</summary>
    internal int? EditButtonAt(System.Drawing.Point location) =>
        _queueList.HitTest(location) is { Item: { } item, SubItem: { } cell }
            && cell == item.SubItems[EditColumn] && cell.Text.Length > 0
            ? item.Index
            : null;

    /// <summary>The row ▶ hands the engine: the selected one when exactly one is, else none.</summary>
    private RowId? SingleSelectedRow() =>
        _queueList.SelectedItems.Count == 1 ? RowOf(_queueList.SelectedItems[0]) : null;

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

    /// <summary>The editor's adjacencies for the row at <paramref name="rowIndex"/>, from the
    /// engine; none for a row out of range.</summary>
    private RowAdjacencies AdjacenciesAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _queueList.Items.Count
            ? _engine.AdjacenciesOf(RowOf(_queueList.Items[rowIndex]))
            : RowAdjacencies.None;

    /// <summary>The one selected row's adjacencies; none for no row or several.</summary>
    private RowAdjacencies SelectedAdjacencies() =>
        _queueList.SelectedIndices.Count == 1 ? AdjacenciesAt(_queueList.SelectedIndices[0]) : RowAdjacencies.None;

    private void EditSelected(Func<RowAdjacencies, Adjacency?> which) => Edit(which(SelectedAdjacencies()));

    /// <summary>Opens the editor on the row's this → next, as the menu does.</summary>
    internal void EditTransitionAt(int rowIndex) => Edit(AdjacenciesAt(rowIndex).Next);

    private void Edit(Adjacency? adjacency)
    {
        if (adjacency is not null)
        {
            _editor.Open(adjacency);
        }
    }

    /// <summary>Asks what to do with the editor's unsaved edits before it leaves them.</summary>
    internal Func<Adjacency, UnsavedEdits> AskUnsavedEdits { get; set; }

    private UnsavedEdits ShowUnsavedDialog(Adjacency adjacency)
    {
        var save = new TaskDialogButton("Save");
        var discard = new TaskDialogButton("Discard");
        var page = new TaskDialogPage
        {
            Caption = "Unsaved transition",
            Heading = $"Save your changes to {adjacency.Name}?",
            Text = "The editor has changes that are not saved. Discard loses them.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { save, discard, TaskDialogButton.Cancel },
            DefaultButton = save,
        };
        var chosen = TaskDialog.ShowDialog(this, page);
        return chosen == save ? UnsavedEdits.Save : chosen == discard ? UnsavedEdits.Discard : UnsavedEdits.Cancel;
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
