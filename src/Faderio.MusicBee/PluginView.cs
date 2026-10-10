using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Shell;

namespace Faderio.MusicBee;

/// <summary>
/// faderio's view inside MusicBee (phase 5 issue 10), the plugin's own, composed from the shared
/// controls: the playlist sidebar on the left, the open playlist's queue in the centre, the
/// attended track named above the transport, the transport with faderio's own Stop, and a strip
/// at the bottom with the latest notice, a count that opens the session's notice list, and the
/// library and output mode this host plays with. It renders <see cref="PlayerView"/>s and issues
/// commands; it holds no engine state between calls and computes nothing from engine values
/// (the placement rule in CLAUDE.md). Starting sound stops MusicBee's player first, as the Tools
/// menu's Play does: never two players at once, nor two on one device.
/// <para>The shared transition editor sits in it too (phase 5 issue 11), opened from the queue's
/// ✎ and its menu: docked right and always shown in a main panel, and in a sidebar a page that
/// takes the lists' place while an adjacency is open, under a bar that closes it. Its preview
/// sounds through the plugin's output, so it stops MusicBee's player first as ▶ does.</para>
/// <para>The shared transition manager is the view's too (phase 5 issue 12), opened from the
/// queue's menu as a window owned by MusicBee's: a double-click on its rows opens the editor
/// here, and its previews stop MusicBee's player first as the editor's do.</para>
/// <para>MusicBee makes and disposes the panel this sits in; the engine outlives it, so the
/// notice log is the composition's, handed in, and only rendered here.</para>
/// </summary>
internal sealed class PluginView : UserControl
{
    private readonly PlaybackEngine _engine;
    private readonly NoticeLog _notices;
    private readonly Func<AudioOutputMode?> _outputModeInUse;
    private readonly Func<string?> _outputDeviceInUse;
    private readonly Action _stopMusicBeesPlayer;
    private readonly System.Windows.Forms.Timer _timer;
    internal readonly ToolTip _tips;
    private readonly QueueRowStyle _rowStyle = new();
    internal readonly Panel _editorBar;

    /// <summary>The queue and the editor, each under a title bar as the playlists are, so the
    /// three panes start level (phase 6). The editor's title shows only in a main panel: in a
    /// sidebar the editor's page has the Close bar over it.</summary>
    internal readonly Panel _queuePane;
    internal readonly Panel _editorPane;
    internal readonly Label _editorTitle;
    internal readonly QueueDrag _queueDrag;
    internal readonly QueueSections _sections;

    internal readonly PlaylistSidebar _playlists;
    internal readonly ListView _queueList;
    internal readonly Label _attendedTrack;
    internal readonly Logo _logo;
    private readonly Panel _header;
    private readonly Panel _rows;
    private readonly Panel _trackLine;
    private readonly FlowLayoutPanel _buttons;
    internal readonly Button _prev;
    internal readonly Button _playPause;
    internal readonly Button _stop;
    internal readonly Button _next;
    internal readonly Button _transitionNow;
    internal readonly Button _cueTransition;
    internal readonly CheckBox _shuffle;
    internal readonly CheckBox _repeat;
    internal readonly CheckBox _syncEditor;
    internal readonly Button _back5;
    internal readonly Button _forward5;
    internal readonly SeekBar _seek;
    internal readonly Readout _time;
    internal readonly Readout _seconds;
    internal readonly ToolStripStatusLabel _status;
    internal readonly ToolStripStatusLabel _noticeCount;
    internal readonly ToolStripStatusLabel _host;
    internal readonly ToolStripButton _openSettings;
    internal readonly EditTransitionMenu _editMenu;
    internal readonly ToolStripMenuItem _openTransitions;
    internal readonly ToolStripMenuItem _removeRows;
    internal readonly ToolStripMenuItem _clearQueue;
    internal readonly TransitionEditorPanel _editor;
    internal readonly Button _closeEditor;
    internal NoticeListForm? _noticeList;
    internal TransitionManagerForm? _manager;

    private long _shownQueue = -1;

    /// <summary>The queue's columns, as the App's: a right-click on the header ticks which show,
    /// and a header drags to a new place. The speaker at the far left and ✎ at the far right stay.
    /// The plugin keeps its own layout in each library, apart from the App's.</summary>
    internal readonly QueueColumns _columns;

    internal const string ColumnsFileName = "musicbee-columns.json";

    /// <summary>The colours MusicBee's skin gives this view (studio theme issue 01).</summary>
    internal Faderio.Shell.Theme.Palette Palette { get; }

    /// <summary>The ✎ column, after the queue's own: the editor button on every row but the last.</summary>
    internal int EditColumn => _columns.EditColumn;

    /// <param name="notices">The session's notices, the composition's: it outlives this view.</param>
    /// <param name="notice">The host's one notice route, for the editor's own: the log and every strip.</param>
    /// <param name="outputModeInUse">The mode the output opened in, or null when none could be.</param>
    /// <param name="outputDeviceInUse">The device the output is on, by name, or null when none was found.</param>
    /// <param name="stopMusicBeesPlayer">MusicBee's <c>Player_Stop</c>, called before faderio starts to sound.</param>
    /// <param name="openSettings">Opens faderio's settings dialog, as MusicBee's Configure does, and applies its OK.</param>
    public PluginView(
        PlaybackEngine engine, NoticeLog notices, Action<string> notice, Func<AudioOutputMode?> outputModeInUse,
        Func<string?> outputDeviceInUse, Action stopMusicBeesPlayer, Action openSettings, Faderio.Shell.Theme.Palette? palette = null)
    {
        Palette = palette ?? Faderio.Shell.Theme.Palette.Studio;
        _engine = engine;
        _notices = notices;
        _outputModeInUse = outputModeInUse;
        _outputDeviceInUse = outputDeviceInUse;
        _stopMusicBeesPlayer = stopMusicBeesPlayer;
        ConfirmClear = ShowClearDialog;
        ShowNoticeList = ShowModeless;
        ShowTransitionManager = ShowModeless;

        Dock = DockStyle.Fill;
        // A pixel at the top for the rule between MusicBee's strip and the view (OnPaint).
        Padding = new Padding(0, 1, 0, 0);
        // Never ambient: unset, a dark skin's panel would turn the view's labels and buttons
        // dark-on-dark. The studio theme sets both from the skin's palette, at the end.

        _status = new SpringLabel("Ready.") { TextAlign = ContentAlignment.MiddleLeft };
        _noticeCount = new ToolStripStatusLabel { Available = false, IsLink = true, ToolTipText = "Show this session's notices" };
        // Where MusicBee's window has no title of faderio's: the library, the mode in use and the
        // device. Cut short in a narrow panel, whole in its tooltip.
        _host = new ToolStripStatusLabel { ForeColor = Palette.Dim, Margin = new Padding(8, 3, 0, 2), AutoSize = false, TextAlign = ContentAlignment.MiddleLeft };
        // faderio's settings without MusicBee's Preferences: a flat ⚙ at the strip's far right
        // (round 2 issue 03).
        _openSettings = SettingsGear.Create(openSettings);
        var statusStrip = new StatusStrip { Items = { _status, _noticeCount, _host, _openSettings }, ShowItemToolTips = true, SizingGrip = false };

        var symbols = new Font("Segoe UI Symbol", 9f);
        // Sized so the eight fit MusicBee's sidebar as shipped (284 px) on one row; narrower, the
        // row wraps.
        _prev = new Button { Text = "⏮", Font = symbols, Width = 31, Height = 28, Margin = new Padding(4, 6, 1, 4) };
        _playPause = new Button { Text = "▶", Font = symbols, Width = 31, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        // Stop is this host's: a guest in MusicBee's process lets go of the device while stopped.
        _stop = new Button { Text = "⏹", Font = symbols, Width = 31, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        _next = new Button { Text = "⏭", Font = symbols, Width = 31, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        _transitionNow = new Button { Text = "⤳", Font = new Font("Segoe UI Symbol", 13f), Width = 31, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        _cueTransition = new Button { Text = "⏩", Font = symbols, Width = 31, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        // Hidden: shuffle and repeat are not what faderio is for, but kept wired up.
        _shuffle = new CheckBox { Text = "🔀", Visible = false, Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 35, Height = 28, Margin = new Padding(4, 6, 1, 4) };
        _repeat = new CheckBox { Text = "🔁", Visible = false, Font = symbols, Appearance = Appearance.Button, AutoCheck = false, TextAlign = ContentAlignment.MiddleCenter, Width = 35, Height = 28, Margin = new Padding(1, 6, 1, 4) };
        _back5 = new Button { Text = "−5 s", Width = 48, Height = 28, Enabled = false };
        _forward5 = new Button { Text = "+5 s", Width = 48, Height = 28, Enabled = false };
        // The editor's (round 2 issue 02): at the right of its pane title, or on its Close bar in
        // a sidebar, where the title is hidden (PlaceEditor). Its own font, not the title's.
        _syncEditor = new CheckBox { Text = "Sync editor", AutoCheck = false, AutoSize = true, Dock = DockStyle.Right, Font = Font };
        _tips = new ToolTip();
        _tips.SetToolTip(_syncEditor, Presenter.SyncEditorTip);
        _tips.SetToolTip(_stop, "Stop, and let go of the audio device");
        _tips.SetToolTip(_transitionNow, "Transition now");
        _tips.SetToolTip(_cueTransition, "Cue transition: play from 5 s before it");
        _tips.SetToolTip(_back5, "Back 5 seconds");
        _tips.SetToolTip(_forward5, "Forward 5 seconds");
        _seek = new SeekBar();
        // The LED readout (studio theme issue 04), and the same in seconds under it, to read
        // against the editor's fade points. In a sidebar's narrow column the time draws smaller.
        _time = Readout.Time();
        _seconds = Readout.Seconds();
        _attendedTrack = new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 11f),
            Padding = new Padding(6, 4, 6, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        // Two rows, so the transport fits a sidebar as well as the main panel: the buttons, then
        // the seek bar with its jumps stacked after it, +5 s over −5 s, and the readout. A table,
        // not anchors: the seek bar takes whatever width is left at any panel size.
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true };
        buttons.Controls.AddRange(new Control[] { _prev, _playPause, _stop, _next, _transitionNow, _cueTransition, _shuffle, _repeat });
        var seekRow = new TableLayoutPanel { Dock = DockStyle.Top, Height = 50, ColumnCount = 3, RowCount = 1 };
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        seekRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        seekRow.ColumnStyles.Add(_readoutColumn);
        _forward5.Height = _back5.Height = 23;
        _forward5.Margin = new Padding(0, 0, 0, 1);
        _back5.Margin = new Padding(0, 1, 0, 0);
        var jumps = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.Left, Margin = new Padding(0, 1, 2, 1),
        };
        jumps.Controls.Add(_forward5, 0, 0);
        jumps.Controls.Add(_back5, 0, 1);
        _seek.Margin = new Padding(6, 8, 6, 8);
        _seek.Dock = DockStyle.Fill;
        seekRow.Controls.Add(_seek, 0, 0);
        seekRow.Controls.Add(jumps, 1, 0);
        // Its own height, within the row's 50 px: left to itself the table asks for 100.
        var times = new TableLayoutPanel { Dock = DockStyle.Fill, Height = 46, ColumnCount = 1, RowCount = 2, Margin = new Padding(3, 2, 3, 2) };
        times.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        times.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        times.Controls.Add(_time, 0, 0);
        times.Controls.Add(_seconds, 0, 1);
        seekRow.Controls.Add(times, 2, 0);

        _queueList = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = true,
            HideSelection = false,
            ShowItemToolTips = true,
        };
        QueueRowStyle.Draw(_queueList, Palette); // a selected row raised, not blue
        _rowStyle.Palette = Palette;
        // Rows drag to a new place, as in the App (phase 6 issue 02). Files dropped from
        // MusicBee's track list, or from Explorer, are both a shell drag of the files (issue 01),
        // and land where they are dropped (issue 03).
        _queueDrag = new QueueDrag(_queueList, engine, (paths, before) => _ = _engine.Add(paths, before)) { Palette = Palette };
        // #, Title, Artist, Length and Track gain until the library's own layout is read, at the
        // first render; Artist narrower than the App's, for a sidebar.
        _columns = new QueueColumns(
            _queueList, ColumnLayout.Default.Toggle(QueueColumn.ReplayGain).Resize(QueueColumn.Artist, 140), ColumnsFileName,
            ColumnsRebuilt, () => Render(_engine.View));
        _editMenu = new EditTransitionMenu(SelectedAdjacencies, Edit);
        // The manager lists the whole store, not the queue, so the item is always live: on the
        // one selected row's track, else the store's first (the App's Transitions… button).
        _openTransitions = new ToolStripMenuItem("Transitions…");
        _removeRows = new ToolStripMenuItem("Remove") { ShortcutKeyDisplayString = "Del" };
        _clearQueue = new ToolStripMenuItem("Clear queue");
        _sections = new QueueSections(_queueList, engine);
        var menu = new ContextMenuStrip();
        menu.Items.AddRange(_editMenu.Items);
        menu.Items.AddRange(new ToolStripItem[]
        {
            _openTransitions, new ToolStripSeparator(), _sections.Menu.Root, new ToolStripSeparator(), _removeRows, _clearQueue,
        });
        menu.Opening += (_, e) =>
        {
            // A right-click on the header asks for the columns rather than the rows.
            if (_columns.OpenedOnHeader(e))
            {
                return;
            }
            _editMenu.Refresh();
            _sections.MenuOpening();
            _removeRows.Enabled = _queueList.SelectedItems.Count > 0;
            _clearQueue.Enabled = _queueList.Items.Count > 0;
        };
        _openTransitions.Click += (_, _) => OpenTransitions();
        _removeRows.Click += (_, _) => RemoveSelected();
        _clearQueue.Click += (_, _) => ClearQueue();
        _queueList.ContextMenuStrip = menu;
        _queueList.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None)
            {
                return;
            }
            if (e.KeyCode == Keys.Delete)
            {
                RemoveSelected();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _queueList.SelectedIndices.Count == 1)
            {
                PlayRowAt(_queueList.SelectedIndices[0]);
                e.Handled = true;
            }
        };
        _queueList.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && QueueRowStyle.EditButtonAt(_queueList, e.Location, EditColumn) is { } index)
            {
                EditTransitionAt(index);
            }
        };
        // A double-click on the button is not a request to play the row.
        _queueList.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left && QueueRowStyle.EditButtonAt(_queueList, e.Location, EditColumn) is null
                && _queueList.HitTest(e.Location).Item is { } item)
            {
                PlayRowAt(item.Index);
            }
        };

        // The editor's preview sounds through the plugin's output: MusicBee's player stops first,
        // unless faderio already sounds and so already has the device.
        _editor = new TransitionEditorPanel(
            engine, notice, suggested => Dialogs.ChooseRenderPath(FindForm(), suggested),
            beforeSounding: StopMusicBeesPlayerIfSilent);
        _editor.Dock = DockStyle.Fill;
        // In a sidebar the editor is a page over the lists: following playback never opens it
        // there, so the queue is not taken away mid-listen; it follows while the page is shown.
        _editor.CanFollow = () => ClientSize.Width >= NarrowWidth || _editor.Adjacency is not null;
        _editorTitle = PaneTitle.Create("Transition editor");
        // The playlists end 4 px above the strip; the queue and the editor end level with them.
        _editorPane = new Panel { Padding = new Padding(0, 0, 0, 4) };
        _editorPane.Controls.Add(_editor);
        _editorPane.Controls.Add(_editorTitle);
        _queuePane = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 4) };
        _queuePane.Controls.Add(_queueList);
        _queuePane.Controls.Add(PaneTitle.Create("Queue"));
        _closeEditor = new Button { Text = "Close editor", AutoSize = true, Height = 26, Dock = DockStyle.Right, Margin = new Padding(0) };
        _editorBar = new Panel { Dock = DockStyle.Top, Height = 30, Padding = new Padding(4, 2, 4, 2), Visible = false };
        _editorBar.Controls.Add(_closeEditor);
        _closeEditor.Click += (_, _) => CloseEditor();
        _playlists = new PlaylistSidebar(engine, LeaveEditor) { Palette = Palette };

        // Docking order: the strip and the editor's bar first, then the playlists, then the
        // editor (at the right, or filling the lists' place), and the list last.
        Controls.Add(_queuePane);
        Controls.Add(_editorPane);
        Controls.Add(_playlists);
        Controls.Add(_editorBar);
        Controls.Add(statusStrip);
        Controls.Add(seekRow);
        _trackLine = new Panel { Dock = DockStyle.Top, Height = 26 };
        _trackLine.Controls.Add(_attendedTrack);
        _buttons = buttons;
        // Placed by FitHeader, not docked.
        _rows = new Panel();
        _rows.Controls.Add(buttons);
        _rows.Controls.Add(_trackLine);
        // The logo at the top right (round 2 issue 05): in a main panel a column of its own beside
        // the track's line and the buttons; in a sidebar on the track's line, as tall as the line
        // allows, so the buttons keep the sidebar's width (FitWidth).
        _logo = new Logo(Palette.IsDark) { Dock = DockStyle.Right };
        _header = new Panel { Dock = DockStyle.Top };
        _header.Controls.Add(_rows);
        Controls.Add(_header);
        buttons.SizeChanged += (_, _) => FitHeader();
        _header.Resize += (_, _) => FitHeader();
        Resize += (_, _) => FitWidth();
        FitWidth();

        _status.Click += (_, _) => OpenNotices();
        _noticeCount.Click += (_, _) => OpenNotices();
        _playPause.Click += (_, _) => TogglePlayPause();
        _stop.Click += (_, _) => _engine.Stop();
        _prev.Click += (_, _) => _engine.Previous();
        _next.Click += (_, _) => _engine.Next();
        _transitionNow.Click += (_, _) => _engine.TransitionNow();
        _cueTransition.Click += (_, _) =>
        {
            StopMusicBeesPlayerIfSilent(); // paused, it starts to sound
            _engine.CueTransition();
        };
        // The press asks for the state after the one the view holds now, not the one last shown.
        _shuffle.Click += (_, _) => _engine.SetShuffle(!_engine.View.Shuffle);
        _repeat.Click += (_, _) => _engine.SetRepeat(Presenter.NextRepeat(_engine.View.Repeat));
        _syncEditor.Click += (_, _) => _engine.SetEditorFollowsPlayback(!_engine.View.Settings.EditorFollowsPlayback);
        _seek.SeekRequested += SeekTo;
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

        // The playhead moves between publications: the seek bar and the readout follow it on a
        // timer, as standalone faderio's do.
        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => Render(_engine.View);
        _timer.Start();
        Disposed += (_, _) =>
        {
            _timer.Dispose();
            _tips.Dispose();
            _rowStyle.Dispose(); // after the list, which draws with its fonts
            _columns.Dispose(); // a width still settling is written
            _sections.Dispose(); // after the list, which draws its stripes
            _noticeList?.Dispose();
            _manager?.Dispose();
        };

        Faderio.Shell.Theme.StudioTheme.Apply(this, Palette); // the studio look, in the skin's colours
        Faderio.Shell.Theme.StudioTheme.Apply(_columns.Menu, Palette); // the header's menu, shown by itself (studio theme issue 06)
        ShowNotices();
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
        Faderio.Shell.Theme.StudioTheme.Light(_playPause, view.Transport == Transport.Playing);
        _transitionNow.Enabled = view.CanTransitionNow;
        _cueTransition.Enabled = view.CanCueTransition;
        _shuffle.Checked = view.Shuffle;
        _repeat.Checked = view.Repeat != RepeatMode.Off;
        _repeat.Text = Presenter.RepeatGlyph(view.Repeat);
        _syncEditor.Checked = view.Settings.EditorFollowsPlayback;
        ShowTip(_shuffle, Presenter.ShuffleTip(view.Shuffle));
        ShowTip(_repeat, Presenter.RepeatTip(view.Repeat));

        if (_columns.Open(view.Library))
        {
            // Another library, or the first render: the layout left is written, and this one read.
            _shownQueue = -1;
        }
        if (view.QueueVersion != _shownQueue)
        {
            RebuildRows(view, _queueList.SelectedItems.Cast<ListViewItem>().Select(RowOf).ToHashSet());
            _shownQueue = view.QueueVersion;
        }
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
        var host = Presenter.FmtHost(view.Library, _outputModeInUse(), _engine.SampleRate, _outputDeviceInUse());
        if (_host.Text != host)
        {
            _host.Text = host;
            _host.ToolTipText = host;
            FitWidth();
        }

        _editor.Refresh(view);
        _playlists.Render(view);
        _manager?.Render(view);
    }

    /// <summary>The strip shows the log's latest notice and how many the log holds; the list,
    /// if open, every one. Called by the composition as each notice is delivered, and once at
    /// construction for those delivered before this view existed.</summary>
    internal void ShowNotices()
    {
        if (IsDisposed || _notices.Latest is not { } latest)
        {
            return;
        }
        _status.Text = Presenter.FmtNotice(latest);
        _status.ToolTipText = _status.Text; // in full, where the strip clips it
        _noticeCount.Text = Presenter.FmtNoticeCount(_notices.Entries.Count);
        _noticeCount.Available = true;
        if (_noticeList is { IsDisposed: false } list)
        {
            list.ShowEntries(_notices, selectLatest: false);
        }
    }

    /// <summary>Render runs on every tick, and resetting a tip that is showing would close it.</summary>
    private void ShowTip(Control control, string tip)
    {
        if (_tips.GetToolTip(control) != tip)
        {
            _tips.SetToolTip(control, tip);
        }
    }

    /// <summary>Rows the list already shows in the same places are kept and only the new ones
    /// made, as the App's list does: a background add publishes once per file.</summary>
    private void RebuildRows(PlayerView view, HashSet<RowId> selected)
    {
        _queueList.BeginUpdate();
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
            var item = _columns.Row(++kept, row); // the ✎ and speaker cells styled at render
            _queueList.Items.Add(item);
            item.Selected = selected.Contains(row.Id);
        }
        _queueList.EndUpdate();
    }

    /// <summary>Below this width the panel is a sidebar: the playlists go above the queue, not
    /// beside it, so the queue keeps its columns.</summary>
    internal const int NarrowWidth = 560;

    /// <summary>The readout's column: the App's width in a main panel, where the LED readout is
    /// whole at full size, and narrower in a sidebar, where it draws smaller and the seek bar
    /// keeps its travel (studio theme issue 04).</summary>
    private readonly ColumnStyle _readoutColumn = new(SizeType.Absolute, WideReadout);

    private const int WideReadout = 180, NarrowReadout = 120;

    /// <summary>Lays the view out for its width: the playlists beside the queue in a main panel,
    /// above it in a sidebar; the editor at the right in a main panel, always, and in a sidebar
    /// a page in the lists' place while an adjacency is open, under its Close bar; the host line
    /// at its own width, or half the strip's when that is less, so the notice keeps the rest; the
    /// readout's column wide in a main panel, narrow in a sidebar.</summary>
    /// <summary>A one-pixel rule in Edge across the top (round 2 issue 01), so MusicBee's strip and
    /// the view do not run together. Painted here, in the padding the controls dock under.</summary>
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var rule = new Pen(Palette.Edge);
        e.Graphics.DrawLine(rule, 0, 0, Width, 0);
    }

    private void FitWidth()
    {
        var narrow = ClientSize.Width < NarrowWidth;
        var dock = narrow ? DockStyle.Top : DockStyle.Left;
        if (_playlists.Dock != dock)
        {
            _playlists.Dock = dock;
            if (narrow)
            {
                _playlists.Height = 110;
            }
            else
            {
                _playlists.Width = 200;
            }
        }
        var readout = narrow ? NarrowReadout : WideReadout;
        if (_readoutColumn.Width != readout)
        {
            _readoutColumn.Width = readout;
        }
        PlaceEditor();
        // On the track's line docked last, so rightmost, past the name; in its column placed by
        // FitHeader.
        Hold(_logo, narrow ? _trackLine : _header, narrow ? DockStyle.Right : DockStyle.None);
        FitHeader();
        var preferred = TextRenderer.MeasureText(_host.Text, _host.Font).Width + 8;
        _host.Width = Math.Max(0, Math.Min(preferred, ClientSize.Width / 2));
    }

    /// <summary>The track's line over the buttons, and where the logo has its column, as tall as
    /// the logo and the logo at their right. Placed here, not docked: the header's docking does
    /// not run again for a change made inside its own layout (the buttons' height changes there),
    /// and left to it the logo kept the track's line's width when it moved into its column, its
    /// art cut at the left until the next resize (2026-10-10).</summary>
    private void FitHeader()
    {
        var column = _logo.Parent == _header;
        var rows = _trackLine.Height + _buttons.Height;
        var height = column ? Math.Max(rows, _logo.Room) : rows;
        if (_header.Height != height)
        {
            _header.Height = height;
        }
        var room = _header.DisplayRectangle;
        var logoWidth = column ? _logo.WidthFor(room.Height) : 0;
        if (column)
        {
            Place(_logo, new Rectangle(room.Right - logoWidth, room.Top, logoWidth, room.Height));
        }
        Place(_rows, new Rectangle(room.Left, room.Top, room.Width - logoWidth, room.Height));
    }

    private static void Place(Control control, Rectangle bounds)
    {
        if (control.Bounds != bounds)
        {
            control.Bounds = bounds;
        }
    }

    /// <summary>Puts the editor where the view's width wants it: at the right in a main panel,
    /// as a page in the lists' place in a sidebar while an adjacency is open, hidden there
    /// otherwise.</summary>
    private void PlaceEditor()
    {
        if (ClientSize.Width >= NarrowWidth)
        {
            _editorPane.Dock = DockStyle.Right;
            _editorPane.Width = TransitionEditorPanel.DefaultWidth;
            _editorPane.Visible = _editorTitle.Visible = true;
            _editorBar.Visible = false;
            Hold(_syncEditor, _editorTitle, DockStyle.Right);
            _playlists.Visible = _queuePane.Visible = true;
            return;
        }
        // A sidebar can be short (MusicBee stacks its panels), so the editor and the lists do
        // not share it: the editor is a page in their place, scrolling inside, with the transport
        // still above it for capture by ear.
        var open = _editor.Adjacency is not null;
        _editorPane.Dock = DockStyle.Fill;
        _editorTitle.Visible = false;
        _editorBar.Visible = open;
        Hold(_syncEditor, _editorBar, DockStyle.Left);
        _editorPane.Visible = open;
        _playlists.Visible = _queuePane.Visible = !open;
    }

    /// <summary>Puts <paramref name="control"/> in <paramref name="parent"/>, docked to
    /// <paramref name="side"/>, unless it is there already.</summary>
    private static void Hold(Control control, Control parent, DockStyle side)
    {
        if (control.Parent != parent)
        {
            parent.Controls.Add(control);
        }
        control.Dock = side;
    }

    /// <summary>The columns were made again, and the list emptied: every row is made again.</summary>
    private void ColumnsRebuilt(HashSet<RowId> selected)
    {
        RebuildRows(_engine.View, selected);
        _shownQueue = _engine.View.QueueVersion;
    }

    private static RowId RowOf(ListViewItem item) => (RowId)item.Tag!;

    /// <summary>The one selected row, or null for none or several.</summary>
    private RowId? SingleSelectedRow() =>
        _queueList.SelectedItems.Count == 1 ? RowOf(_queueList.SelectedItems[0]) : null;

    /// <summary>The one selected row's track as the published queue has it, or null for no row
    /// or several. A loop, not a lambda: a closure over a <see cref="RowId"/> would give this
    /// assembly a class with a Core value type in a field, which MusicBee's reflection over the
    /// plugin cannot load before the resolver exists (LoadingTests).</summary>
    private Track? SelectedTrack()
    {
        var row = SingleSelectedRow();
        foreach (var queued in _engine.View.Queue)
        {
            if (queued.Id == row)
            {
                return queued.Track;
            }
        }
        return null;
    }

    // ── Commands: one intent, one engine call ───────────────────────────────────────────

    /// <summary>▶ while faderio is silent starts sound, so MusicBee's player is stopped first;
    /// ⏸ starts nothing and leaves it alone.</summary>
    private void TogglePlayPause()
    {
        StopMusicBeesPlayerIfSilent();
        _engine.TogglePlayPause(SingleSelectedRow());
    }

    /// <summary>A row's double-click, or Enter on it: that row plays, after MusicBee's player stops.</summary>
    internal void PlayRowAt(int index)
    {
        if (index < 0 || index >= _queueList.Items.Count)
        {
            return;
        }
        _stopMusicBeesPlayer();
        _engine.PlayRow(RowOf(_queueList.Items[index]));
    }

    internal void SeekTo(Playhead playhead) => _engine.Seek(playhead);

    /// <summary>Before faderio starts to sound, from ▶, ⏩ or a preview: MusicBee's player stops
    /// unless faderio sounds already and so already has the device.</summary>
    private void StopMusicBeesPlayerIfSilent()
    {
        if (!_engine.View.IsSounding)
        {
            _stopMusicBeesPlayer();
        }
    }

    // ── The editor ──────────────────────────────────────────────────────────────────────

    /// <summary>The one selected row's adjacencies, from the engine; none for no row or several.</summary>
    private RowAdjacencies SelectedAdjacencies() =>
        _queueList.SelectedIndices.Count == 1 ? AdjacenciesAt(_queueList.SelectedIndices[0]) : RowAdjacencies.None;

    /// <summary>The editor's adjacencies for the row at <paramref name="index"/>, from the
    /// engine; none for a row out of range.</summary>
    private RowAdjacencies AdjacenciesAt(int index) =>
        index >= 0 && index < _queueList.Items.Count ? _engine.AdjacenciesOf(RowOf(_queueList.Items[index])) : RowAdjacencies.None;

    /// <summary>✎ on a row: the editor opens on its this → next, as the menu's first item does.</summary>
    internal void EditTransitionAt(int index) => Edit(AdjacenciesAt(index).Next);

    /// <summary>Opens the editor on an adjacency, and shows it where a sidebar hides it.</summary>
    private void Edit(Adjacency? adjacency)
    {
        if (adjacency is null)
        {
            return;
        }
        _editor.Open(adjacency);
        PlaceEditor(); // shown now, in a sidebar
    }

    /// <summary>Saves a box being typed in and empties the editor: before the open playlist
    /// changes under it, and from the sidebar's Close.</summary>
    private void LeaveEditor()
    {
        _editor.Commit();
        _editor.Close();
        PlaceEditor();
    }

    /// <summary>The Close editor bar: the lists come back.</summary>
    internal void CloseEditor() => LeaveEditor();

    /// <summary>Opens the editor on <paramref name="adjacency"/>: the Now Playing tab's next view,
    /// on what its last had open (phase 6 issue 08).</summary>
    internal void OpenEditor(Adjacency adjacency) => Edit(adjacency);

    /// <summary>Saves a box being typed in, as leaving it would, and says what the editor has
    /// open: the Now Playing tab is left, and MusicBee disposes the view.</summary>
    internal Adjacency? CommitEditor()
    {
        _editor.Commit();
        return _editor.Adjacency;
    }

    // ── The transition manager ──────────────────────────────────────────────────────────

    /// <summary>Shows the transition manager beside MusicBee's window. A test replaces it.</summary>
    internal Action<Form> ShowTransitionManager { get; set; }

    /// <summary>Opens the transition manager on the one selected row's track, else on the
    /// store's first track, as the App's Transitions… does. One manager, reused; its previews
    /// stop MusicBee's player first when faderio is silent, as the editor's do.</summary>
    private void OpenTransitions()
    {
        if (_manager is null || _manager.IsDisposed)
        {
            _manager = new TransitionManagerForm(_engine, EditFromManager, beforeSounding: StopMusicBeesPlayerIfSilent, palette: Palette);
        }
        _manager.Open(SelectedTrack());
        ShowTransitionManager(_manager);
    }

    /// <summary>A double-click in the manager: the editor opens on that adjacency, shown where
    /// a sidebar hides it, and MusicBee's window comes forward to show it.</summary>
    private void EditFromManager(Adjacency adjacency)
    {
        Edit(adjacency);
        if (FindForm() is { Visible: true } form)
        {
            form.Activate();
        }
    }

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

    /// <summary>Whether the user confirms clearing a queue of this many rows. A test replaces
    /// it, since a headless view cannot show a dialog.</summary>
    internal Func<int, bool> ConfirmClear { get; set; }

    private bool ShowClearDialog(int count) =>
        MessageBox.Show(
            FindForm(),
            $"Remove all {count} rows from the queue? This cannot be undone.",
            "Clear queue",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.OK;

    /// <summary>Shows the notice list beside MusicBee's window. A test replaces it.</summary>
    internal Action<Form> ShowNoticeList { get; set; }

    private void ShowModeless(Form window)
    {
        if (!window.Visible)
        {
            window.Show(FindForm());
        }
        window.Activate();
    }

    /// <summary>Opens this session's notice list on the latest notice; one list, reused.</summary>
    private void OpenNotices()
    {
        if (_notices.Latest is null)
        {
            return;
        }
        if (_noticeList is null || _noticeList.IsDisposed)
        {
            _noticeList = new NoticeListForm(Palette);
        }
        _noticeList.ShowEntries(_notices, selectLatest: true);
        ShowNoticeList(_noticeList);
    }
}
