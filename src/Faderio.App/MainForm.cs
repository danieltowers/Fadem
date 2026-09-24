using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.App;

/// <summary>
/// The WinForms shell for the slice (ticket 05): add files or a folder, or drop either on the
/// list (build issue 02), open a playlist or save the queue as one (build issue 03), a
/// multi-select list, a context menu for "Edit transition (this → next)", Remove and Clear queue
/// (build issue 04; Delete removes too), transport, a seek bar in milliseconds, and the
/// transition editor. It renders <see cref="PlayerView"/>s and issues commands; it holds no engine state
/// between calls (the placement rule in CLAUDE.md). The seek bar's Maximum is the track length
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
    internal readonly ToolStripStatusLabel _status;
    internal readonly ToolStripStatusLabel _addProgress;
    internal readonly ToolStripButton _cancelAdd;
    internal readonly TransitionEditorPanel _editor;
    internal readonly ToolStripMenuItem _editTransition;
    internal readonly ToolStripMenuItem _removeRows;
    internal readonly ToolStripMenuItem _clearQueue;
    private readonly System.Windows.Forms.Timer _timer;

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

        Text = "faderio";
        MinimumSize = new Size(760, 480);
        Size = new Size(1040, 600);
        StartPosition = FormStartPosition.CenterScreen;

        _status = new ToolStripStatusLabel("Ready.") { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
        _addProgress = new ToolStripStatusLabel { Available = false };
        _cancelAdd = new ToolStripButton("Cancel") { Available = false, DisplayStyle = ToolStripItemDisplayStyle.Text };
        var statusStrip = new StatusStrip { Items = { _status, _addProgress, _cancelAdd } };

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
        var transport = new TableLayoutPanel { Dock = DockStyle.Top, Height = 44, ColumnCount = 6, RowCount = 1 };
        foreach (var _ in Enumerable.Range(0, 4))
        {
            transport.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        }
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        transport.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
        transport.Controls.Add(_prev, 0, 0);
        transport.Controls.Add(_playPause, 1, 0);
        transport.Controls.Add(_stop, 2, 0);
        transport.Controls.Add(_next, 3, 0);
        transport.Controls.Add(_seek, 4, 0);
        transport.Controls.Add(_time, 5, 0);

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
        _queueList.DragEnter += (_, e) =>
            e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
        _queueList.DragDrop += (_, e) =>
        {
            if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
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
        _playPause.Click += (_, _) => _engine.TogglePlayPause();
        _prev.Click += (_, _) => _engine.Previous();
        _stop.Click += (_, _) => _engine.Stop();
        _next.Click += (_, _) => _engine.Next();
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

    /// <summary>A background add publishes once per file, so rows the list already shows in
    /// the same places are kept and only the new ones are made: a whole library would otherwise
    /// rebuild the list once per file, and the window would stop answering.</summary>
    private void RebuildRows(PlayerView view)
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
            var item = new ListViewItem((++kept).ToString()) { Tag = row.Id };
            item.SubItems.Add(string.IsNullOrEmpty(row.Track.Title) ? Path.GetFileName(row.Track.FilePath) : row.Track.Title);
            item.SubItems.Add(row.Track.Artist);
            item.SubItems.Add(Presenter.Fmt(row.Track.Length));
            _queueList.Items.Add(item);
        }
        _queueList.EndUpdate();
    }

    private void ShowNotice(string message)
    {
        if (!IsDisposed)
        {
            _status.Text = message;
        }
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
