using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The transition manager (playlists issue 05, D24): every adjacency one track is in, with the
/// global transition and the open playlist's override side by side. A picker over every track in
/// the store at the top, a grid below it, a Preview and a Preview global button on each row and one
/// Stop preview. Read-only: a double-click opens the row in the editor, which edits. Modeless, and
/// reused: closing hides it. The picker and the grid are the engine's answers, asked again only
/// when <see cref="PlayerView.StoreVersion"/> moves or another track is picked.
/// </summary>
internal sealed class TransitionManagerForm : Form
{
    private readonly PlaybackEngine _engine;
    private readonly Action<Adjacency> _edit;

    internal readonly ComboBox _track;
    internal readonly Label _legend;
    internal readonly DataGridView _grid;
    internal readonly Button _stopPreview;
    internal readonly Label _previewStatus;

    /// <summary>The store version the picker and the grid were last asked at.</summary>
    private long _shownStore = -1;

    /// <summary>Opened and not closed since: rendered while so, shown or not (a headless test shows
    /// no window).</summary>
    private bool _open;

    private bool _listing;

    /// <summary>A track as the picker shows it.</summary>
    private sealed record TrackItem(Track Track)
    {
        public override string ToString() => Presenter.FmtTrack(Track);
    }

    internal const int PreviewColumn = 9;
    internal const int PreviewGlobalColumn = 10;

    /// <param name="edit">Opens the editor on an adjacency, in the mode it was left in.</param>
    public TransitionManagerForm(PlaybackEngine engine, Action<Adjacency> edit)
    {
        _engine = engine;
        _edit = edit;

        Text = "faderio — transitions";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1340, 380);
        MinimumSize = new Size(640, 240);
        KeyPreview = true;

        _track = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Width = 480,
            Margin = new Padding(4, 8, 4, 4),
            DrawMode = DrawMode.OwnerDrawFixed,
        };
        // Drawn so that a gone track reads grey, as its rows do (phase 3 issue 02).
        _track.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index >= 0 && _track.Items[e.Index] is TrackItem item)
            {
                var selected = (e.State & DrawItemState.Selected) != 0;
                var colour = item.Track.Gone && !selected ? SystemColors.GrayText : e.ForeColor;
                TextRenderer.DrawText(e.Graphics, item.ToString(), e.Font, e.Bounds, colour,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
            e.DrawFocusRectangle();
        };
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 38, WrapContents = false };
        top.Controls.Add(new Label { Text = "Track:", AutoSize = true, Margin = new Padding(8, 12, 0, 0) });
        top.Controls.Add(_track);
        _legend = new Label
        {
            Dock = DockStyle.Top,
            Height = 22,
            ForeColor = SystemColors.GrayText,
            Padding = new Padding(8, 0, 0, 0),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            RowHeadersVisible = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            BackgroundColor = SystemColors.Window,
            AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        };
        Column("", 28);
        Column("Other track", 170);
        Column("Fade-out point", 95);
        Column("Fade-out duration", 110);
        Column("Fade-in point", 90);
        Column("Fade-in duration", 105);
        Column("Curve", 140);
        // The effective transition is the longest text: it takes what the others leave, and a
        // cell cut short shows it whole in its tooltip.
        var effective = Column("Effective", 360);
        effective.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        effective.MinimumWidth = 240;
        Column("Playlist only", 80).DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
        _grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Width = 70, Text = "Preview", UseColumnTextForButtonValue = true });
        _grid.Columns.Add(new DataGridViewButtonColumn { HeaderText = "", Width = 100, Text = "Preview global", UseColumnTextForButtonValue = true });
        _grid.CellContentClick += (_, e) =>
        {
            if (e.ColumnIndex == PreviewColumn)
            {
                PreviewAt(e.RowIndex, TransitionLayer.Playlist);
            }
            else if (e.ColumnIndex == PreviewGlobalColumn)
            {
                PreviewAt(e.RowIndex, TransitionLayer.Global);
            }
        };
        _grid.CellDoubleClick += (_, e) =>
        {
            if (e.ColumnIndex is not PreviewColumn and not PreviewGlobalColumn)
            {
                EditAt(e.RowIndex);
            }
        };

        _stopPreview = new Button { Text = "Stop preview", Width = 110, Height = 28, Enabled = false, Margin = new Padding(8, 6, 4, 6) };
        _previewStatus = new Label { AutoSize = true, Margin = new Padding(4, 12, 4, 0) };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, WrapContents = false };
        bottom.Controls.Add(_stopPreview);
        bottom.Controls.Add(_previewStatus);
        _stopPreview.Click += (_, _) => _engine.StopPreview();

        _track.SelectedIndexChanged += (_, _) =>
        {
            if (!_listing)
            {
                ShowAdjacencies();
            }
        };

        Controls.Add(_grid);
        Controls.Add(_legend);
        Controls.Add(top);
        Controls.Add(bottom);

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
        VisibleChanged += (_, _) =>
        {
            _open &= Visible;
            if (Visible)
            {
                // Shown for the first time, the grid picks its first row again.
                _grid.ClearSelection();
            }
        };
    }

    private DataGridViewColumn Column(string header, int width)
    {
        var column = new DataGridViewTextBoxColumn { HeaderText = header, Width = width, SortMode = DataGridViewColumnSortMode.NotSortable };
        _grid.Columns.Add(column);
        return column;
    }

    /// <summary>The track picked, or null with none in the store.</summary>
    internal Track? PickedTrack => (_track.SelectedItem as TrackItem)?.Track;

    /// <summary>The adjacency on a grid row.</summary>
    internal Adjacency AdjacencyAt(int row) => (Adjacency)_grid.Rows[row].Tag!;

    // ── Rendering: read, render, forget ─────────────────────────────────────────────────

    /// <summary>Opens on <paramref name="track"/> when the store names it, else on the first track.</summary>
    public void Open(Track? track)
    {
        _open = true;
        ListTracks(track);
        Render(_engine.View);
    }

    /// <summary>Called on every render of the main window: the picker and the grid are asked again
    /// once the store has changed, and the preview's button and line follow the preview.</summary>
    public void Render(PlayerView view)
    {
        if (IsDisposed || !_open)
        {
            return;
        }
        if (view.StoreVersion != _shownStore)
        {
            ListTracks(PickedTrack);
        }
        var legend = Presenter.ManagerLegend(view.OpenPlaylist, _track.Items.Count > 0);
        if (_legend.Text != legend)
        {
            _legend.Text = legend;
        }
        _stopPreview.Enabled = view.Preview is not null;
        _previewStatus.Text = Presenter.FmtPreview(view.Preview);
    }

    /// <summary>The picker from the store, on <paramref name="wanted"/> if it is there, else on
    /// the first track; then the grid for the one picked.</summary>
    private void ListTracks(Track? wanted)
    {
        _shownStore = _engine.View.StoreVersion;
        var tracks = _engine.TracksInStore();
        _listing = true;
        _track.BeginUpdate();
        _track.Items.Clear();
        foreach (var track in tracks)
        {
            _track.Items.Add(new TrackItem(track));
        }
        _track.EndUpdate();
        // By path: the same file is another track once it is gone, or found again.
        var index = wanted is null ? -1 : tracks.ToList().FindIndex(t => t.FilePath == wanted.FilePath);
        _track.SelectedIndex = index >= 0 ? index : tracks.Count > 0 ? 0 : -1;
        _track.Enabled = tracks.Count > 0;
        _listing = false;
        ShowAdjacencies();
    }

    /// <summary>The grid for the picked track, one row per adjacency, as the engine orders them.</summary>
    private void ShowAdjacencies()
    {
        var picked = PickedTrack;
        var stored = picked is null ? [] : _engine.AdjacenciesInStore(picked);
        _grid.Rows.Clear();
        foreach (var adjacency in stored)
        {
            var index = _grid.Rows.Add(Presenter.ManagerRow(adjacency).Cast<object>().ToArray());
            var row = _grid.Rows[index];
            row.Tag = adjacency.Adjacency;
            row.Cells[1].ToolTipText = adjacency.Other.Gone ? Presenter.FmtGoneTip(adjacency.Other) : adjacency.Other.FilePath;
            if (adjacency.Other.Gone)
            {
                row.DefaultCellStyle.ForeColor = SystemColors.GrayText;
            }
        }
        // The grid picks its first row for itself; that highlight means nothing in a read-only
        // list, and would hide a gone row's grey.
        _grid.ClearSelection();
    }

    // ── Commands: one intent, one engine call ───────────────────────────────────────────

    /// <summary>Preview plays what the open playlist plays; Preview global plays the global
    /// transition alone. The queue is held while it runs.</summary>
    internal void PreviewAt(int row, TransitionLayer layer)
    {
        if (row >= 0 && row < _grid.Rows.Count)
        {
            _engine.PreviewStored(AdjacencyAt(row), layer);
        }
    }

    /// <summary>The editor edits: the row opens there, in the mode the editor was left in.</summary>
    internal void EditAt(int row)
    {
        if (row >= 0 && row < _grid.Rows.Count)
        {
            _edit(AdjacencyAt(row));
        }
    }
}
