using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Point = System.Drawing.Point;

namespace Faderio.Shell;

/// <summary>
/// Drag and drop on a queue list, the same in both hosts (phase 6 issue 02; build issue 05
/// first, in the App alone). The selected rows drag to a new place: a line shows where they will
/// land, a drag held at the top or bottom edge scrolls the list, and the drop is one
/// <see cref="PlaybackEngine.Move"/>. Files dragged, from Explorer or MusicBee's track list, show
/// the same line, and their drop goes to the host's handler with the row they land before
/// (issue 03); anything else is refused. The list's items carry their
/// <see cref="RowId"/> in <see cref="ListViewItem.Tag"/>, as both hosts' queues do.
/// </summary>
internal sealed class QueueDrag : IDisposable
{
    private const string RowsFormat = "faderio.rows";

    private readonly ListView _list;
    private readonly PlaybackEngine _engine;
    private readonly System.Windows.Forms.Timer _scroll;

    /// <summary>Whether the drag under way is of rows, which can end a section, or of files.</summary>
    private bool _draggingRows;

    /// <summary>Where dragged rows will land. A ListView draws no insertion mark in details
    /// view, so it is a thin panel over the list.</summary>
    internal Panel DropLine { get; }

    /// <summary>Starts a drag of the selected rows. A test replaces it, since a headless list
    /// cannot enter the modal drag loop.</summary>
    internal Action<IDataObject> StartDrag { get; set; }

    /// <param name="dropFiles">The host's add of files dropped, in the order dropped, before the
    /// row given, or at the end for none.</param>
    public QueueDrag(ListView list, PlaybackEngine engine, Action<string[], RowId?> dropFiles)
    {
        _list = list;
        _engine = engine;
        StartDrag = data => _list.DoDragDrop(data, DragDropEffects.Move);
        _list.AllowDrop = true;
        DropLine = new Panel { Height = 2, BackColor = SystemColors.Highlight, Visible = false };
        _list.Controls.Add(DropLine);
        // A drag raises DragOver only when the mouse moves, so a drag held still at an edge is
        // scrolled on a timer.
        _scroll = new System.Windows.Forms.Timer { Interval = 60 };
        _scroll.Tick += (_, _) =>
        {
            var client = _list.PointToClient(Cursor.Position);
            ScrollNearEdge(client);
            ShowDropLine(client, _draggingRows);
        };
        _list.ItemDrag += (_, _) => DragSelectedRows();
        _list.DragEnter += (_, e) => DragOver(e);
        _list.DragOver += (_, e) => DragOver(e);
        _list.DragLeave += (_, _) => EndRowDrag();
        _list.DragDrop += (_, e) =>
        {
            EndRowDrag();
            if (RowsOf(e) is { } rows)
            {
                MoveRows(rows, ClientPoint(e));
            }
            else if (e.Data?.GetData(DataFormats.FileDrop) is string[] paths)
            {
                dropFiles(paths, RowBefore(DropIndex(ClientPoint(e))));
            }
        };
    }

    public void Dispose() => _scroll.Dispose();

    /// <summary>The row whose group a drop of rows at the cursor ends, or null: over the lower
    /// half of the last row of a section's group, with the next section's header below it, rows
    /// land after it, in its section (phase 6 issue 09), not before the next row, in the next.</summary>
    internal int? EndOfGroupAt(Point client)
    {
        var index = DropIndex(client);
        if (!_list.ShowGroups || index == 0 || index >= _list.Items.Count
            || RowAtHeight(client.Y) is not { } item || item.Index != index - 1)
        {
            return null;
        }
        return _list.Items[index].Group != item.Group ? item.Index : null;
    }

    /// <summary>The list position a drop at the cursor lands in: before the row under it when
    /// over its upper half, after it when over its lower half, and after the last row when
    /// below them all. Only the height counts: past the last column is still that row.</summary>
    internal int DropIndex(Point client)
    {
        if (RowAtHeight(client.Y) is { } item)
        {
            var bounds = item.Bounds;
            return client.Y < bounds.Top + bounds.Height / 2 ? item.Index : item.Index + 1;
        }
        return _list.TopItem is { } top && client.Y < top.Bounds.Top ? top.Index : _list.Items.Count;
    }

    private void DragSelectedRows()
    {
        var rows = _list.SelectedItems.Cast<ListViewItem>().Select(i => (RowId)i.Tag!).ToArray();
        if (rows.Length > 0)
        {
            StartDrag(new DataObject(RowsFormat, new DraggedRows(_list, rows)));
            EndRowDrag();
        }
    }

    /// <summary>Rows move and files copy in, both showing where they will land; anything else
    /// is refused.</summary>
    private void DragOver(DragEventArgs e)
    {
        e.Effect = RowsOf(e) is not null ? DragDropEffects.Move
            : e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy
            : DragDropEffects.None;
        if (e.Effect == DragDropEffects.None)
        {
            EndRowDrag();
            return;
        }
        _draggingRows = RowsOf(e) is not null;
        _scroll.Start();
        ShowDropLine(ClientPoint(e), _draggingRows);
    }


    /// <summary>The rows dragged from this list, or null: rows never leave the list they came
    /// from, so another view's rows are refused.</summary>
    private RowId[]? RowsOf(DragEventArgs e) =>
        e.Data?.GetData(RowsFormat) is DraggedRows dragged && dragged.Source == _list ? dragged.Rows : null;

    private void EndRowDrag()
    {
        _scroll.Stop();
        DropLine.Visible = false;
    }

    private Point ClientPoint(DragEventArgs e) => _list.PointToClient(new Point(e.X, e.Y));

    private ListViewItem? RowAtHeight(int y) => _list.HitTest(2, y).Item;

    /// <summary>A drag held near the top or bottom edge scrolls the list a row at a time, so a
    /// row can be dragged past what is in view.</summary>
    private void ScrollNearEdge(Point client)
    {
        if (_list.TopItem is not { } top)
        {
            return;
        }
        var margin = top.Bounds.Height;
        if (client.Y < top.Bounds.Top + margin && top.Index > 0)
        {
            _list.EnsureVisible(top.Index - 1);
        }
        else if (client.Y > _list.ClientSize.Height - margin
            && RowAtHeight(_list.ClientSize.Height - 1) is { } bottom
            && bottom.Index + 1 < _list.Items.Count)
        {
            _list.EnsureVisible(bottom.Index + 1);
        }
    }

    private void ShowDropLine(Point client, bool rows)
    {
        var count = _list.Items.Count;
        if (count == 0)
        {
            DropLine.Visible = false;
            return;
        }
        var index = DropIndex(client);
        var y = rows && EndOfGroupAt(client) is { } end ? _list.Items[end].Bounds.Bottom - 1
            : index < count ? _list.Items[index].Bounds.Top - 1
            : _list.Items[count - 1].Bounds.Bottom - 1;
        DropLine.SetBounds(0, y, _list.ClientSize.Width, DropLine.Height);
        DropLine.Visible = true;
        DropLine.BringToFront();
    }

    /// <summary>One drop, one call: the drop position is passed as the row the dragged rows
    /// land before, or none for the end, or the row they land after where they end its section.
    /// Where they land, and what that does to playback, is the engine's.</summary>
    private void MoveRows(IReadOnlyList<RowId> rows, Point client)
    {
        if (EndOfGroupAt(client) is { } end)
        {
            _engine.MoveAfter(rows, (RowId)_list.Items[end].Tag!);
        }
        else
        {
            _engine.Move(rows, RowBefore(DropIndex(client)));
        }
    }

    /// <summary>The row at a drop index, which a drop lands before; none past the last row.</summary>
    private RowId? RowBefore(int index) => index < _list.Items.Count ? (RowId)_list.Items[index].Tag! : null;

    /// <summary>What a row drag carries: the rows, and the list they came from.</summary>
    private sealed class DraggedRows(ListView source, RowId[] rows)
    {
        public ListView Source { get; } = source;

        public RowId[] Rows { get; } = rows;
    }
}
