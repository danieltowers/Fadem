using Faderio.Shell;
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
/// from the header's menu, dragged into order and sized, kept per library (playlists issue 06), and
/// the attended track named above the transport. It
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
    internal readonly Label _seconds;
    internal readonly Label _attendedTrack;
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
    internal readonly Button _cueTransition;
    internal readonly CheckBox _shuffle;
    internal readonly CheckBox _repeat;
    internal readonly CheckBox _syncEditor;
    internal readonly ToolTip _tips;
    internal readonly ToolStripStatusLabel _status;
    internal readonly ToolStripStatusLabel _noticeCount;
    internal readonly ToolStripStatusLabel _addProgress;
    internal readonly ToolStripButton _cancelAdd;
    internal readonly ToolStripStatusLabel _clip;
    internal readonly TransitionEditorPanel _editor;
    internal readonly PlaylistSidebar _playlists;
    internal readonly PlaylistNameBar _playlistName;
    internal readonly EditTransitionMenu _editMenu;
    internal readonly ToolStripMenuItem _transitionsForTrack;
    internal readonly ToolStripMenuItem _removeRows;
    internal readonly ToolStripMenuItem _clearQueue;
    private readonly QueueDrag _queueDrag;
    internal readonly QueueSections _sections;
    private readonly System.Windows.Forms.Timer _timer;

    /// <summary>The line showing where dragged rows will land.</summary>
    internal Panel _dropLine => _queueDrag.DropLine;

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
        ChooseLibrary = () => LibraryChooser.Choose(this, _engine.KnownLibraries, _engine.ForgetLibrary);
        ShowSettings = dialog => dialog.ShowDialog(this) == DialogResult.OK;
        ShowNoticeList = ShowModeless;
        ShowTransitionManager = ShowModeless;

        Text = "faderio";
        // The sidebar's 200 px on top of what the queue and the editor need: at the least width the
        // queue still shows the default columns, the row buttons in view.
        MinimumSize = new Size(960, 480);
        // Tall enough for the whole editor, Render last, without its scroll bar.
        Size = new Size(1240, 660);
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
        _cueTransition = new Button { Text = "⏩", Font = symbols, Width = 36, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // Push buttons that stay pressed in while on. They never check themselves: what they
        // show is always the view's, set in Render.
        _shuffle = new CheckBox { Text = "🔀", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(10, 8, 2, 4) };
        _repeat = new CheckBox { Text = "🔁", Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 44, Height = 28, Margin = new Padding(2, 8, 2, 4) };
        // A plain checkbox, unlike the toggles: it plays nothing, it only moves the editor.
        _syncEditor = new CheckBox { Text = "Sync editor", AutoCheck = false, AutoSize = true, Margin = new Padding(10, 12, 2, 4) };
        _tips = new ToolTip();
        _tips.SetToolTip(_syncEditor, Presenter.SyncEditorTip);
        _tips.SetToolTip(_transitionNow, "Transition now");
        _tips.SetToolTip(_cueTransition, "Cue transition: play from 5 s before it");
        // Stacked after the seek bar, +5 s over −5 s, as in the plugin (interim transport issue
        // 05): fixed five-second jumps.
        _back5 = new Button { Text = "−5 s", Width = 48, Height = 23, Margin = new Padding(0, 1, 0, 0), Enabled = false };
        _forward5 = new Button { Text = "+5 s", Width = 48, Height = 23, Margin = new Padding(0, 0, 0, 1), Enabled = false };
        _tips.SetToolTip(_back5, "Back 5 seconds");
        _tips.SetToolTip(_forward5, "Forward 5 seconds");
        _seek = new SeekBar
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 8, 8, 8),
        };
        _time = new Label
        {
            Text = "0:00 / -0:00",
            Width = 170,
            TextAlign = ContentAlignment.BottomLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        // The same in seconds, under it, to read against the editor's fade points.
        _seconds = new Label
        {
            Text = Presenter.SecondsReadout(null),
            TextAlign = ContentAlignment.TopLeft,
            AutoSize = false,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
        };
        // Its own height, within the transport's 50 px: left to itself the table asks for 100.
        var times = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 36, ColumnCount = 1, RowCount = 2, Margin = new Padding(3, 7, 3, 7) };
        times.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        times.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        times.Controls.Add(_time, 0, 0);
        times.Controls.Add(_seconds, 0, 1);
        // The attended track's name, on its own line above the transport. It keeps its height
        // while stopped, so the transport does not jump when playback starts.
        _attendedTrack = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font(Font.FontFamily, 11f),
            Padding = new Padding(6, 4, 6, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, // and the whole name as its tooltip when cut short
            UseMnemonic = false,
        };
        // A table, not anchors: the seek bar takes whatever width is left between the buttons
        // and the readout, at any window size.
        var transport = new TableLayoutPanel { Dock = DockStyle.Top, Height = 50, ColumnCount = 11, RowCount = 1 };
        foreach (var _ in Enumerable.Range(0, 8))
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
        transport.Controls.Add(_cueTransition, 4, 0);
        transport.Controls.Add(_shuffle, 5, 0);
        transport.Controls.Add(_repeat, 6, 0);
        transport.Controls.Add(_syncEditor, 7, 0);
        var jumps = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 1, 2, 1),
        };
        jumps.Controls.Add(_forward5, 0, 0);
        jumps.Controls.Add(_back5, 0, 1);
        transport.Controls.Add(_seek, 8, 0);
        transport.Controls.Add(jumps, 9, 0);
        transport.Controls.Add(times, 10, 0);

        _queueList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
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
            // The user's width, kept; one the fit gives the title or takes from a column that
            // gives way is not.
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
            // The title's width is the fit's own; any other the user sets fits the rest again.
            if (column is not QueueColumn.Title && !_fitting)
            {
                FitColumns();
            }
        };
        Disposed += (_, _) => _rowStyle.Dispose(); // after the list, which draws with its fonts

        _editMenu = new EditTransitionMenu(SelectedAdjacencies, Edit);
        _transitionsForTrack = new ToolStripMenuItem("Transitions for this track…");
        _removeRows = new ToolStripMenuItem("Remove") { ShortcutKeyDisplayString = "Del" };
        _clearQueue = new ToolStripMenuItem("Clear queue");
        _sections = new QueueSections(_queueList, engine);
        Disposed += (_, _) => _sections.Dispose();
        var menu = new ContextMenuStrip
        {
            Items =
            {
                _editMenu.Next, _editMenu.Previous, _editMenu.Self, _transitionsForTrack, new ToolStripSeparator(),
                _sections.Menu.Root, new ToolStripSeparator(), _removeRows, _clearQueue,
            },
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
            _editMenu.Refresh();
            _sections.MenuOpening();
            _transitionsForTrack.Enabled = _queueList.SelectedItems.Count == 1;
            _removeRows.Enabled = _queueList.SelectedItems.Count > 0;
            _clearQueue.Enabled = _queueList.Items.Count > 0;
        };
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
        // Rows drag to a new place, and dropped files land where they are dropped (shared with
        // the plugin, phase 6 issues 02 and 03).
        _queueDrag = new QueueDrag(_queueList, engine, (paths, before) => _ = _engine.Add(paths, before));

        _editor = new TransitionEditorPanel(engine, ShowNotice, suggested => Dialogs.ChooseRenderPath(this, suggested));
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
        Controls.Add(_attendedTrack);
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
        _cueTransition.Click += (_, _) => _engine.CueTransition();
        // The press asks for the state after the one the view holds now, not the one last shown.
        _shuffle.Click += (_, _) => _engine.SetShuffle(!_engine.View.Shuffle);
        _repeat.Click += (_, _) => _engine.SetRepeat(Presenter.NextRepeat(_engine.View.Repeat));
        _syncEditor.Click += (_, _) => _engine.SetEditorFollowsPlayback(!_engine.View.Settings.EditorFollowsPlayback);
        _seek.SeekRequested += playhead => _engine.Seek(playhead);
        // A mark or wedge dragged on the seek bar (phase 6 issue 11): the attended row's fade.
        _seek.FadeMarkMoved += (mark, to) =>
        {
            if (_engine.View.Attended is { } attended)
            {
                _engine.MoveFadeMark(attended.Row.Id, mark, to);
            }
        };
        _seek.FadeMoved += (side, from, to) =>
        {
            if (_engine.View.Attended is { } attended)
            {
                _engine.MoveFade(attended.Row.Id, side, from, to);
            }
        };
        _back5.Click += (_, _) => _engine.SeekBy(TimeSpan.FromSeconds(-5));
        _forward5.Click += (_, _) => _engine.SeekBy(TimeSpan.FromSeconds(5));

        sink.Changed += Render;
        sink.Noticed += ShowNotice;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Render(_engine.View);
        _timer.Start();

        FormClosing += (_, e) =>
        {
            _editor.Commit(); // a box being typed in, as leaving it would
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
        _cueTransition.Enabled = view.CanCueTransition;
        _shuffle.Checked = view.Shuffle;
        _repeat.Checked = view.Repeat != RepeatMode.Off;
        _repeat.Text = Presenter.RepeatGlyph(view.Repeat);
        _syncEditor.Checked = view.Settings.EditorFollowsPlayback;
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
        // Render runs on every tick: each row's look is set only when it changes (build issue 20).
        _rowStyle.Apply(_queueList, view, EditColumn);
        _sections.Apply(view);

        var attended = view.Attended;
        _seek.Show(attended?.Playhead ?? Playhead.Zero, attended?.Length, attended?.Marks);
        _back5.Enabled = _forward5.Enabled = _seek.Enabled;
        _time.Text = Presenter.TimeReadout(attended, held: view.Preview is not null);
        _seconds.Text = Presenter.SecondsReadout(attended);
        var attendedTrack = Presenter.FmtAttendedTrack(attended);
        if (_attendedTrack.Text != attendedTrack)
        {
            _attendedTrack.Text = attendedTrack;
        }

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
    internal Action<IDataObject> StartDrag
    {
        get => _queueDrag.StartDrag;
        set => _queueDrag.StartDrag = value;
    }

    /// <summary>Shows the settings dialog and says whether the user pressed OK. A test replaces
    /// it, since a headless form cannot show a dialog.</summary>
    internal Func<SettingsForm, bool> ShowSettings { get; set; }

    /// <summary>The folder chosen in the library chooser, or null. A test replaces it, since a
    /// headless form cannot show a dialog.</summary>
    internal Func<string?> ChooseLibrary { get; set; }

    /// <summary>Switches library (library issue 02): a box being typed in is saved first, then
    /// one call. The editor empties, since what it had open belongs to the library left.</summary>
    private void OpenLibrary()
    {
        _editor.Commit();
        if (ChooseLibrary() is not { } folder)
        {
            return;
        }
        _editor.Close();
        _engine.OpenLibrary(folder);
    }

    /// <summary>Before the open playlist changes (playlists issue 02): a box being typed in is
    /// saved, then the editor empties, as what it had open was the queue left.</summary>
    private void LeaveEditor()
    {
        _editor.Commit();
        _editor.Close();
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
                _editor.ShowLayers(); // what a blank duration plays may have changed
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

    /// <summary>Import opens a playlist (D19), so the editor is left once something was picked;
    /// then one call for all of them.</summary>
    private void ImportPlaylists()
    {
        var paths = ChoosePlaylists();
        if (paths.Count > 0)
        {
            LeaveEditor();
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

    internal static RowId RowOf(ListViewItem item) => (RowId)item.Tag!;

    /// <summary>The queue's last column: each row's editor button (interim transport 07), after
    /// every column shown (playlists issue 06).</summary>
    internal int EditColumn => _queueList.Columns.Count - 1;

    private const int MinTitleWidth = 100;

    /// <summary>The columns that give way when the shown ones do not fit, in the order they do
    /// (phase 3 issue 01), each to its floor. The rest never shrink: their contents are short.</summary>
    private static readonly (QueueColumn Column, int Floor)[] GiveWay =
    [
        (QueueColumn.Path, 80),
        (QueueColumn.Album, 60),
        (QueueColumn.Artist, 60),
    ];

    /// <summary>Set while the fit gives widths, so their changes do not fit again.</summary>
    private bool _fitting;

    /// <summary>Sizes the title to the list's width less the other columns and room for a
    /// scroll bar, which appears without resizing the list. Where that would squeeze the title
    /// below its least, Path, then Album, then Artist give way, each to its floor, and take back
    /// up to their own widths as room returns, in the reverse order. Only with every one at its
    /// floor does the list scroll sideways. With the title hidden, nothing fills: the columns
    /// keep their widths.</summary>
    private void FitColumns()
    {
        var headers = _queueList.Columns.Cast<ColumnHeader>().ToList();
        if (headers.Find(c => c.Tag is QueueColumn.Title) is not { } title)
        {
            return;
        }
        var giving = GiveWay
            .Select(g => (Header: headers.Find(c => c.Tag is QueueColumn q && q == g.Column), g.Column, Own: _columns[g.Column].Width, g.Floor))
            .Where(g => g.Header is not null)
            .ToList();
        var free = _queueList.ClientSize.Width - SystemInformation.VerticalScrollBarWidth
            - headers.Where(c => c != title && !giving.Exists(g => g.Header == c)).Sum(c => c.Width);
        // What the title's floor and every own width need beyond the room there is.
        var lacking = MinTitleWidth + giving.Sum(g => g.Own) - free;
        _fitting = true;
        try
        {
            foreach (var g in giving)
            {
                // A column the user made narrower than its floor is never widened to it.
                var width = g.Own - Math.Clamp(lacking, 0, Math.Max(g.Own - g.Floor, 0));
                lacking -= g.Own - width;
                Give(g.Header!, g.Column, width);
                free -= width;
            }
            Give(title, QueueColumn.Title, Math.Max(MinTitleWidth, free));
        }
        finally
        {
            _fitting = false;
        }
    }

    /// <summary>Gives <paramref name="header"/> a width the fit chose, which is never the user's.</summary>
    private void Give(ColumnHeader header, QueueColumn column, int width)
    {
        _widthsSet[column] = width;
        if (header.Width != width)
        {
            header.Width = width;
        }
    }

    /// <summary>The rows' look, shared with the plugin's queue (phase 5 issue 11).</summary>
    private readonly QueueRowStyle _rowStyle = new();

    /// <summary>The row whose editor button is at <paramref name="location"/>, or null: a click
    /// anywhere else on a row, or on a row that shows no button, is not a press of it.</summary>
    internal int? EditButtonAt(System.Drawing.Point location) => QueueRowStyle.EditButtonAt(_queueList, location, EditColumn);

    /// <summary>The row ▶ hands the engine: the selected one when exactly one is, else none.</summary>
    private RowId? SingleSelectedRow() =>
        _queueList.SelectedItems.Count == 1 ? RowOf(_queueList.SelectedItems[0]) : null;

    /// <summary>The selected rows go in one call; what that does to playback is the engine's.</summary>
    private void RemoveSelected() =>
        _engine.Remove(_queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToList());

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

    /// <summary>Opens the editor on the row's this → next, as the menu does.</summary>
    internal void EditTransitionAt(int rowIndex) => Edit(AdjacenciesAt(rowIndex).Next);

    private void Edit(Adjacency? adjacency)
    {
        if (adjacency is not null)
        {
            _editor.Open(adjacency);
        }
    }
}
