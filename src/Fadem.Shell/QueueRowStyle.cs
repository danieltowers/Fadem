using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Fadem.Core.Domain;
using Fadem.Core.Playback;

using Point = System.Drawing.Point;

namespace Fadem.Shell;

/// <summary>
/// How a queue row looks in either host (phase 5 issue 11): the attended row's speaker
/// (<see cref="RowMarks"/>) in a column of its own, shown at the far left but kept last, after
/// the seam buttons' cell, so every other cell keeps its index (<see cref="AddMarkColumn"/>); an
/// unreadable row's grey, the row's label and tip, and the seam buttons (seam markers issue 02).
/// A seam button sits on the line between a row and the next, half in each row's cell: ⋈ where
/// the row's this → next is saved; + where it is not, shown only while the mouse is on the row or
/// the button; filled in the accent where the editor has that pair open, whose rows are tinted.
/// A render runs on every tick, so each cell's look is set only when it changes.
/// <see cref="Draw"/> has the list drawn by this style, so a selected row is
/// <see cref="RowMarks.Selected"/>'s grey rather than the system's blue, and each row's image,
/// a section's stripe, at its left edge (studio theme issue 07).
/// </summary>
internal sealed class QueueRowStyle : IDisposable
{
    private readonly Font _markFont = new("Segoe UI Symbol", 12f);
    private readonly ToolTip _tip = new();

    /// <summary>The speaker's column: wide enough for its glyph, and no wider.</summary>
    public const int MarkColumnWidth = 26;

    /// <summary>The rows' height: tall enough for the speaker. A section's stripes are as tall.</summary>
    public const int RowHeight = 22;

    /// <summary>How far toward the accent the open pair's rows are tinted (G3 soft, the prototype's
    /// round 7).</summary>
    private const double TintAmount = 0.16;

    /// <summary>How far toward the accent the outline round the open pair and the editor is.</summary>
    private const double OutlineAmount = 0.6;

    /// <summary>How much further toward the accent a row is while the mouse is on its half of the
    /// editor (seam markers issue 04).</summary>
    private const double LitAmount = 0.35;

    /// <summary>The open pair's tint: the Well, 16 % toward the accent.</summary>
    public static Color TintOf(Theme.Palette? palette) => TowardAccent(palette, palette?.Well ?? SystemColors.Window, TintAmount);

    /// <summary>The outline round the open pair and the editor: the Well, 60 % toward the accent.</summary>
    public static Color OutlineOf(Theme.Palette? palette) => TowardAccent(palette, palette?.Well ?? SystemColors.Window, OutlineAmount);

    /// <summary>A row of the open pair while the mouse is on its half of the editor.</summary>
    public static Color LitOf(Theme.Palette? palette) => TowardAccent(palette, TintOf(palette), LitAmount);

    private static Color TowardAccent(Theme.Palette? palette, Color from, double amount) =>
        Theme.Palette.Mix(palette?.Accent ?? SystemColors.Highlight, from, amount);

    /// <summary>The colours rows are drawn in (studio theme issue 02), or none for the system's.</summary>
    public Theme.Palette? Palette { get; set; }

    /// <summary>The adjacency a row's seam opens, from the engine: the tooltip names its two tracks.</summary>
    public Func<int, Adjacency?>? SeamAdjacency { get; set; }

    /// <summary>The seam buttons' cell, as the last <see cref="Apply(ListView, PlayerView, int, IReadOnlyList{RowPair})"/>
    /// gave it; none before.</summary>
    private int _editColumn = -1;

    /// <summary>The rows whose seam is the editor's open pair: their buttons are filled.</summary>
    private HashSet<int> _openSeams = [];

    /// <summary>The seam under the mouse, and the row it is on.</summary>
    private int? _hoverSeam, _hoverRow;

    /// <summary>The rows tinted, as the last render gave them: a collapsed section's header stands
    /// in for those it hides.</summary>
    private HashSet<int> _tinted = [];

    /// <summary>The editor's half under the mouse, which lights its row of the first match (seam
    /// markers issue 04); none.</summary>
    public FadeSide? LitHalf { get; set; }

    /// <summary>Drawn over the list once its rows are, into the same off-screen paint: the outline
    /// round the open pair (seam markers issue 04).</summary>
    public Action<Graphics>? Overlay { get; set; }

    /// <summary>Styles every row <paramref name="list"/> shows for the queue in
    /// <paramref name="view"/>, as far as both reach; <paramref name="editColumn"/> is the seam
    /// buttons' cell, and the speaker's cell is the one after it. <paramref name="open"/> is where
    /// the editor's adjacency is in the queue, from the engine: each pair's rows are tinted, and
    /// each pair's seam filled.</summary>
    public void Apply(ListView list, PlayerView view, int editColumn, IReadOnlyList<RowPair> open)
    {
        var index = new Dictionary<RowId, int>(view.Queue.Count);
        for (var i = 0; i < view.Queue.Count; i++)
        {
            index[view.Queue[i].Id] = i;
        }
        var tinted = new HashSet<int>();
        var seams = new HashSet<int>();
        int? lit = null;
        if (LitHalf is { } half && open.Count > 0 && index.TryGetValue(half == FadeSide.FadeOut ? open[0].Outgoing : open[0].Incoming, out var litRow))
        {
            lit = litRow;
        }
        foreach (var pair in open)
        {
            if (index.TryGetValue(pair.Outgoing, out var outgoing) && index.TryGetValue(pair.Incoming, out var incoming))
            {
                tinted.Add(outgoing);
                tinted.Add(incoming);
                if (outgoing != incoming)
                {
                    seams.Add(outgoing); // this → this is no seam between rows
                }
            }
        }
        _editColumn = editColumn;
        _tinted = tinted;
        if (!seams.SetEquals(_openSeams))
        {
            foreach (var seam in seams.Union(_openSeams).ToList())
            {
                InvalidateSeam(list, seam);
            }
            _openSeams = seams;
        }
        for (var i = 0; i < list.Items.Count && i < view.Queue.Count; i++)
        {
            Apply(list.Items[i], view.Queue[i], i, view, editColumn, tinted.Contains(i), lit == i);
        }
    }

    /// <summary>Styles <paramref name="item"/>, the list's row at <paramref name="index"/>, for
    /// <paramref name="row"/>; <paramref name="tinted"/> while it is in the editor's open pair, and
    /// <paramref name="lit"/> while the mouse is on its half of the editor.</summary>
    public void Apply(ListViewItem item, QueueRow row, int index, PlayerView view, int editColumn, bool tinted = false, bool lit = false)
    {
        var well = Palette?.Well ?? SystemColors.Window;
        var back = lit ? LitOf(Palette) : tinted ? TintOf(Palette) : well;
        var dim = Palette?.Dim ?? SystemColors.GrayText;
        // The attended row, the one that plays, reads at a glance in the accent (studio theme issue
        // 03); on the open pair's tint, in the text colour.
        var fore = row.IsUnreadable ? dim
            : row.IsCurrent && !tinted && Palette is { } p ? p.Accent
            : Palette?.Text ?? SystemColors.WindowText;
        foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
        {
            if (cell.BackColor != back)
            {
                cell.BackColor = back;
            }
            if (cell.ForeColor != fore)
            {
                cell.ForeColor = fore;
            }
        }
        // The seam's glyph is drawn as a button (DrawSeams), never as text; the cell's text says
        // which button, for a click and for a screen reader.
        var button = item.SubItems[editColumn];
        var glyph = Presenter.SeamGlyph(row.NextAuthored, isLast: index == view.Queue.Count - 1);
        if (button.Text != glyph)
        {
            button.Text = glyph;
            // The list draws again only this row's cell; the button's lower half is the next row's.
            if (item.ListView is { } list)
            {
                InvalidateSeam(list, index);
            }
        }
        if (editColumn + 1 < item.SubItems.Count)
        {
            var markCell = item.SubItems[editColumn + 1];
            var mark = RowMarks.Of(row.IsCurrent, view.Transport) ?? "";
            if (markCell.Text != mark)
            {
                markCell.Text = mark;
            }
            if (markCell.Font != _markFont)
            {
                markCell.Font = _markFont;
            }
        }
        var label = Presenter.RowLabel(index + 1, row.IsUnreadable);
        if (item.Text != label)
        {
            item.Text = label;
            item.ToolTipText = Presenter.RowTip(row.IsUnreadable);
        }
    }

    /// <summary>The row whose seam button is at <paramref name="location"/> in
    /// <paramref name="list"/>, or null. The seam buttons' column is all buttons: a cell's lower
    /// half is its row's seam into the next, its upper half the row above's. A seam whose rows a
    /// section's header parts is in its upper row's cell only. A point anywhere else on a row, or
    /// on a row that has no seam there, is no press of one.</summary>
    public static int? EditButtonAt(ListView list, Point location, int editColumn)
    {
        if (list.HitTest(location) is not { Item: { } item, SubItem: { } cell } || cell != item.SubItems[editColumn])
        {
            return null;
        }
        var lower = location.Y >= cell.Bounds.Top + cell.Bounds.Height / 2;
        var row = lower ? item.Index : item.Index - 1;
        return row >= 0 && (lower || Touching(list, row)) && list.Items[row].SubItems[editColumn].Text.Length > 0
            ? row
            : null;
    }

    /// <summary>Whether row <paramref name="index"/> and the next sit one on the other, with no
    /// section's header between them.</summary>
    private static bool Touching(ListView list, int index) =>
        index + 1 < list.Items.Count && list.Items[index].Group == list.Items[index + 1].Group;

    /// <summary>The pixels the list leaves between row <paramref name="index"/> and the next, where
    /// they touch: none in a plain list, one in sections.</summary>
    private static int GapBelow(ListView list, int index) =>
        Touching(list, index) ? Math.Max(0, Math.Min(4, list.Items[index + 1].Bounds.Top - list.Items[index].Bounds.Bottom)) : 0;

    /// <summary>The mouse on <paramref name="list"/>: the seam under it shows its button raised,
    /// with the hand and a tooltip, and the row under it shows its seam's + (none where off).
    /// Only the buttons that come or go are drawn again: the whole list, on every row the mouse
    /// crossed, flickered (the prototype's round 8).</summary>
    private void Hover(ListView list, Point? location)
    {
        var seam = location is { } at && _editColumn >= 0 && _editColumn < list.Columns.Count
            ? EditButtonAt(list, at, _editColumn) : null;
        var row = location is { } on ? list.HitTest(on).Item?.Index : null;
        if (seam == _hoverSeam && row == _hoverRow)
        {
            return;
        }
        var changed = new[] { _hoverSeam, _hoverRow, seam, row };
        var seamChanged = seam != _hoverSeam;
        _hoverSeam = seam;
        _hoverRow = row;
        var cursor = seam is null ? Cursors.Default : Cursors.Hand;
        if (list.Cursor != cursor)
        {
            list.Cursor = cursor;
        }
        if (seamChanged)
        {
            var tip = seam is { } s && SeamAdjacency?.Invoke(s) is { } adjacency
                ? Presenter.SeamTip(adjacency, list.Items[s].SubItems[_editColumn].Text == Presenter.SeamGlyph(true, false))
                : "";
            if (_tip.GetToolTip(list) != tip)
            {
                _tip.SetToolTip(list, tip);
            }
        }
        foreach (var index in changed.OfType<int>().Distinct())
        {
            InvalidateSeam(list, index);
        }
    }

    /// <summary>Draws row <paramref name="index"/>'s seam button again: the seam cells of that row
    /// and the next, where its halves are.</summary>
    private void InvalidateSeam(ListView list, int index)
    {
        foreach (var r in new[] { index, index + 1 })
        {
            if (r >= 0 && r < list.Items.Count && _editColumn >= 0 && _editColumn < list.Columns.Count)
            {
                list.Invalidate(list.Items[r].SubItems[_editColumn].Bounds);
            }
        }
    }

    /// <summary>A seam cell's buttons: the lower half of the seam above, at the cell's top, and the
    /// upper half of its own row's, at its bottom. A seam whose rows a section's header parts is
    /// whole in its upper row's cell, a little shorter, under the half from above. Where the list
    /// leaves a gap between two rows (in sections, a pixel), the seam's line is the gap's top,
    /// which the upper row's cell takes in.</summary>
    private void DrawSeams(Graphics g, Rectangle area, ListView list, ListViewItem item)
    {
        var i = item.Index;
        var cx = area.Left + area.Width / 2;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (i > 0 && Touching(list, i - 1) && Shown(list, i - 1))
        {
            SeamButton(g, SeamBox(cx, area.Top - GapBelow(list, i - 1), touching: true), list, i - 1);
        }
        if (Shown(list, i))
        {
            SeamButton(g, SeamBox(cx, area.Bottom, Touching(list, i)), list, i);
        }
        g.SmoothingMode = SmoothingMode.Default;
    }

    /// <summary>Whether row <paramref name="index"/>'s seam shows its button: saved, open, or under
    /// the mouse, its own or its row.</summary>
    private bool Shown(ListView list, int index)
    {
        var glyph = list.Items[index].SubItems[_editColumn].Text;
        return glyph.Length > 0
            && (glyph == Presenter.SeamGlyph(true, false) || _openSeams.Contains(index) || _hoverSeam == index || _hoverRow == index);
    }

    /// <summary>Row <paramref name="index"/>'s seam button in <paramref name="list"/>'s client area
    /// while it shows, or none: the outline round the open pair breaks there, so the button sits
    /// on it (seam markers issue 04). Where <see cref="DrawSeams"/> draws it.</summary>
    public Rectangle? SeamButtonBounds(ListView list, int index)
    {
        if (_editColumn < 0 || _editColumn >= list.Columns.Count || index < 0 || index >= list.Items.Count || !Shown(list, index))
        {
            return null;
        }
        var cell = list.Items[index].SubItems[_editColumn].Bounds;
        return SeamBox(cell.Left + list.Columns[_editColumn].Width / 2, cell.Bottom, Touching(list, index));
    }

    /// <summary>A seam button centred at <paramref name="cx"/> on the seam's <paramref name="line"/>,
    /// half in each row; whole above the line, and a little shorter, where a section's header
    /// parts the two rows.</summary>
    private static Rectangle SeamBox(int cx, int line, bool touching) =>
        touching ? new Rectangle(cx - 10, line - 8, 20, 16) : new Rectangle(cx - 10, line - 14, 20, 13);

    /// <summary>Whether <paramref name="group"/>'s header stands in for a tinted row: collapsed,
    /// over a row of the open pair (seam markers issue 04).</summary>
    private bool TintsHeader(ListView list, ListViewGroup group) =>
        _tinted.Count > 0 && list.IsHandleCreated && ListGroups.IsCollapsed(list, group)
        && group.Items.Cast<ListViewItem>().Any(item => _tinted.Contains(item.Index));

    /// <summary>One seam button in <paramref name="box"/>: a key, raised under the mouse, or filled
    /// in the accent where its pair is open; ⋈ in it where saved, + where not.</summary>
    private void SeamButton(Graphics g, Rectangle box, ListView list, int index)
    {
        var authored = list.Items[index].SubItems[_editColumn].Text == Presenter.SeamGlyph(true, false);
        var open = _openSeams.Contains(index);
        var hovered = _hoverSeam == index;
        var well = Palette?.Well ?? SystemColors.Window;
        var accent = Palette?.Accent ?? SystemColors.Highlight;
        var text = Palette?.Text ?? SystemColors.WindowText;
        var dim = Palette?.Dim ?? SystemColors.GrayText;
        var fill = open ? accent : hovered ? Palette?.Raised ?? SystemColors.ControlLight : authored ? Palette?.Key ?? SystemColors.Control : well;
        var edge = open ? accent : hovered ? text : authored ? Palette?.Edge ?? SystemColors.ControlDark : Theme.Palette.Mix(dim, well, 0.6);
        using (var path = Rounded(box, 4))
        {
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(brush, path);
            }
            using var pen = new Pen(edge, 1f);
            g.DrawPath(pen, path);
        }
        float cx = box.Left + box.Width / 2f, cy = box.Top + box.Height / 2f;
        if (authored)
        {
            // A crossfade's mark, ⋈: the outgoing track's level falling to the middle, the
            // incoming's rising from it, over it.
            var ink = open ? Color.White : accent;
            using var outgoing = new SolidBrush(Color.FromArgb(150, ink));
            using var incoming = new SolidBrush(Color.FromArgb(230, ink));
            g.FillPolygon(outgoing, new[] { new PointF(cx - 7, cy - 5), new PointF(cx, cy), new PointF(cx - 7, cy + 5) });
            g.FillPolygon(incoming, new[] { new PointF(cx + 7, cy - 5), new PointF(cx, cy), new PointF(cx + 7, cy + 5) });
        }
        else
        {
            using var mark = new Pen(open ? Color.White : hovered ? text : dim, 1.8f);
            g.DrawLine(mark, cx - 4, cy, cx + 4, cy);
            g.DrawLine(mark, cx, cy - 4, cx, cy + 4);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Has <paramref name="list"/>, a details view, drawn here: each cell in its own
    /// colours and font, aligned as its column, on the palette's Raised (or
    /// <see cref="RowMarks.Selected"/> with none) while its row is selected. The header is drawn
    /// in the palette, or is the system's with none. With <paramref name="ownRowHeight"/> the rows
    /// keep the list's own height (the playlists', studio theme issue 06), not the queue's. With
    /// <paramref name="queue"/>, the queue's own style, its seam buttons' cells are drawn as
    /// buttons and follow the mouse.</summary>
    public static void Draw(ListView list, Theme.Palette? palette = null, bool ownRowHeight = false, QueueRowStyle? queue = null)
    {
        list.OwnerDraw = true;
        _ = new BufferedPaint(list, queue);
        _ = new NoHotRow(list);
        if (queue is not null)
        {
            Theme.StudioTheme.SetHeaderGround(list, group => queue.TintsHeader(list, group) ? TintOf(queue.Palette) : null);
            list.MouseMove += (_, e) => queue.Hover(list, e.Location);
            list.MouseLeave += (_, _) => queue.Hover(list, null);
        }
        // An image list's height sets the rows': the list has no other.
        var rows = new ImageList { ImageSize = new Size(1, RowHeight) };
        if (!ownRowHeight)
        {
            list.SmallImageList = rows;
        }
        // The header's headings: small bold capitals (studio theme issue 03).
        var headings = new Font(list.Font.FontFamily, 7.5f, FontStyle.Bold);
        list.Disposed += (_, _) =>
        {
            rows.Dispose();
            headings.Dispose();
        };
        list.DrawColumnHeader += (_, e) =>
        {
            if (palette is null)
            {
                e.DrawDefault = true;
                return;
            }
            DrawHeader(e, headings, palette);
        };
        list.DrawItem += (_, _) => { }; // a details view draws by cell
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item is not { } item || e.SubItem is not { } cell)
            {
                return;
            }
            // The first cell's bounds span the whole row, from wherever the row starts: its left is
            // after every column shown before it, and its column's width bounds it.
            var column = list.Columns[e.ColumnIndex];
            var left = e.ColumnIndex != 0 ? e.Bounds.X
                : item.Bounds.X + list.Columns.Cast<ColumnHeader>().Where(c => c.DisplayIndex < column.DisplayIndex).Sum(c => c.Width);
            var bounds = new Rectangle(left, e.Bounds.Y, column.Width, e.Bounds.Height);
            if (bounds.Width <= 0 || bounds.Height <= 0)
            {
                return;
            }
            // The cell is drawn on an opaque bitmap, then copied to the list. In groups Windows fades
            // the row under the mouse in and out, drawing it into a buffer with an alpha channel;
            // GDI's text leaves that alpha at nought there, so a row drawn straight onto it showed
            // no text while it faded, a flicker (2026-10-09).
            // A seam cell takes in the gap under its row too, where the list leaves one, so a seam
            // button is whole across it.
            var below = queue is not null && e.ColumnIndex == queue._editColumn ? GapBelow(list, item.Index) : 0;
            using var buffer = new Bitmap(bounds.Width, bounds.Height + below, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(buffer))
            {
                if (below > 0)
                {
                    using var ground = new SolidBrush(palette?.Well ?? list.BackColor);
                    g.FillRectangle(ground, 0, bounds.Height, bounds.Width, below);
                }
                DrawCell(g, new Rectangle(Point.Empty, bounds.Size));
            }
            e.Graphics.DrawImageUnscaled(buffer, bounds.Location);

            void DrawCell(Graphics g, Rectangle area)
            {
                var back = item.Selected ? palette?.Raised ?? RowMarks.Selected : cell.BackColor;
                using (var brush = new SolidBrush(back))
                {
                    g.FillRectangle(brush, area);
                }
                // The row's image, a section's stripe in the queue, at the row's left edge and its full
                // height: drawn here, since the list draws nothing of a row itself. The row is a pixel
                // taller than its image; the image list's own stretch takes that pixel from the next
                // image, so the image's own bitmap is stretched, pixel for pixel.
                if (column.DisplayIndex == 0 && item.ImageIndex >= 0 && list.SmallImageList is { } images
                    && item.ImageIndex < images.Images.Count)
                {
                    using var image = images.Images[item.ImageIndex];
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.DrawImage(image, new Rectangle(area.Left, area.Top, image.Width, area.Height));
                    g.InterpolationMode = InterpolationMode.Default;
                    g.PixelOffsetMode = PixelOffsetMode.Default;
                }
                var align = list.Columns[e.ColumnIndex].TextAlign switch
                {
                    HorizontalAlignment.Right => TextFormatFlags.Right,
                    HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                    _ => TextFormatFlags.Left,
                };
                if (queue is not null && e.ColumnIndex == queue._editColumn)
                {
                    queue.DrawSeams(g, area, list, item);
                    return;
                }
                // A centred cell is narrow, the speaker's: never cut short.
                var centred = align == TextFormatFlags.HorizontalCenter;
                var text = centred ? area : Rectangle.Inflate(area, -4, 0);
                TextRenderer.DrawText(
                    g, cell.Text, cell.Font ?? list.Font, text, cell.ForeColor, back,
                    align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                    | (centred ? TextFormatFlags.NoPadding : TextFormatFlags.EndEllipsis));
            }
        };
    }

    /// <summary>A header cell on the Well: its heading in small bold capitals in Dim, an Edge rule
    /// under it and between columns.</summary>
    private static void DrawHeader(DrawListViewColumnHeaderEventArgs e, Font headings, Theme.Palette palette)
    {
        using (var back = new SolidBrush(palette.Well))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }
        using (var rule = new Pen(palette.Edge))
        {
            e.Graphics.DrawLine(rule, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            e.Graphics.DrawLine(rule, e.Bounds.Right - 1, e.Bounds.Top + 4, e.Bounds.Right - 1, e.Bounds.Bottom - 5);
        }
        var align = e.Header?.TextAlign switch
        {
            HorizontalAlignment.Right => TextFormatFlags.Right,
            HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
            _ => TextFormatFlags.Left,
        };
        TextRenderer.DrawText(e.Graphics, e.Header?.Text.ToUpperInvariant(), headings, Rectangle.Inflate(e.Bounds, -4, 0), palette.Dim,
            align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
    }

    /// <summary>
    /// Keeps Windows from tracking the row under the mouse (2026-10-09). The rows are drawn here and
    /// show no hover, but in groups Windows fades the row and the header under the mouse in and out
    /// by its own drawing, its blue hover without the row's text, so a row flickered blank as the
    /// mouse crossed it. Refusing <c>LVN_HOTTRACK</c> leaves no row hot and nothing to fade.
    /// </summary>
    private sealed class NoHotRow : NativeWindow
    {
        public NoHotRow(ListView list)
        {
            if (list.IsHandleCreated)
            {
                AssignHandle(list.Handle);
            }
            list.HandleCreated += (_, _) => AssignHandle(list.Handle);
            list.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_REFLECT_NOTIFY && Marshal.ReadInt32(m.LParam, 2 * IntPtr.Size) == LVN_HOTTRACK)
            {
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }

        private const int WM_REFLECT_NOTIFY = 0x204E;
        private const int LVN_HOTTRACK = -121;
    }

    /// <summary>
    /// Paints the list off screen and copies it on in one go (2026-10-12). Painted straight on, a
    /// repaint showed the ground first, the rows wiped, then each cell drawn over it, a flicker as
    /// rows changed or the mouse moved. The list's own erase, the theme's plain ground included,
    /// and its own paint, the cells' owner drawing included, go to a bitmap
    /// (<c>WM_PRINTCLIENT</c>). Windows' own buffering (<c>LVS_EX_DOUBLEBUFFER</c>) is not used: it
    /// erases without <c>WM_ERASEBKGND</c>, and its theme draws rules at the columns' edges again.
    /// The queue's <see cref="Overlay"/> is drawn over every print of the list, on screen and in a
    /// copy alike.
    /// </summary>
    private sealed class BufferedPaint : NativeWindow
    {
        private readonly ListView _list;
        private readonly QueueRowStyle? _queue;

        public BufferedPaint(ListView list, QueueRowStyle? queue)
        {
            _list = list;
            _queue = queue;
            if (list.IsHandleCreated)
            {
                AssignHandle(list.Handle);
            }
            list.HandleCreated += (_, _) => AssignHandle(list.Handle);
            list.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_PRINTCLIENT && _queue?.Overlay is { } overlay && m.WParam != IntPtr.Zero)
            {
                base.WndProc(ref m);
                using var g = Graphics.FromHdc(m.WParam);
                overlay(g);
                return;
            }
            if (m.Msg != WM_PAINT || m.WParam != IntPtr.Zero || _list.View != View.Details)
            {
                base.WndProc(ref m);
                return;
            }
            var size = _list.ClientSize;
            // Not BeginPaint: it would erase the window itself, on screen, before the copy.
            var region = CreateRectRgn(0, 0, 0, 0);
            try
            {
                _ = GetUpdateRgn(Handle, region, false);
                _ = ValidateRect(Handle, IntPtr.Zero);
                if (size.Width <= 0 || size.Height <= 0 || GetRgnBox(region, out var p) == NULLREGION)
                {
                    return;
                }
                var dc = GetDC(Handle);
                try
                {
                    _ = SelectClipRgn(dc, region);
                    // The header, and a name being edited in place, are windows of their own and
                    // paint themselves: the copy leaves them be.
                    for (var child = GetWindow(Handle, GW_CHILD); child != IntPtr.Zero; child = GetWindow(child, GW_HWNDNEXT))
                    {
                        if (IsWindowVisible(child) && GetWindowRect(child, out var c))
                        {
                            var r = _list.RectangleToClient(Rectangle.FromLTRB(c.Left, c.Top, c.Right, c.Bottom));
                            _ = ExcludeClipRect(dc, r.Left, r.Top, r.Right, r.Bottom);
                        }
                    }
                    var memory = CreateCompatibleDC(dc);
                    var bitmap = CreateCompatibleBitmap(dc, size.Width, size.Height);
                    var old = SelectObject(memory, bitmap);
                    try
                    {
                        // Erased by whoever erases the list, the theme's plain ground, then painted.
                        _ = SendMessage(Handle, WM_ERASEBKGND, memory, IntPtr.Zero);
                        _ = SendMessage(Handle, WM_PRINTCLIENT, memory, (IntPtr)PRF_CLIENT);
                        _ = BitBlt(dc, p.Left, p.Top, p.Right - p.Left, p.Bottom - p.Top, memory, p.Left, p.Top, SRCCOPY);
                    }
                    finally
                    {
                        _ = SelectObject(memory, old);
                        _ = DeleteObject(bitmap);
                        _ = DeleteDC(memory);
                    }
                }
                finally
                {
                    _ = ReleaseDC(Handle, dc);
                }
            }
            finally
            {
                _ = DeleteObject(region);
            }
            m.Result = IntPtr.Zero;
        }

        private const int WM_PAINT = 0x000F;
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_PRINTCLIENT = 0x0318;
        private const int PRF_CLIENT = 0x04;
        private const int GW_HWNDNEXT = 2;
        private const int GW_CHILD = 5;
        private const int SRCCOPY = 0x00CC0020;
        private const int NULLREGION = 1;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern int GetUpdateRgn(IntPtr hwnd, IntPtr region, bool erase);

        [DllImport("user32.dll")]
        private static extern bool ValidateRect(IntPtr hwnd, IntPtr rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern int GetRgnBox(IntPtr region, out RECT box);

        [DllImport("gdi32.dll")]
        private static extern int SelectClipRgn(IntPtr dc, IntPtr region);

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, int relation);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        private static extern int ExcludeClipRect(IntPtr dc, int left, int top, int right, int bottom);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr obj);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("gdi32.dll")]
        private static extern bool BitBlt(IntPtr dc, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);
    }

    /// <summary>Adds the speaker's column after every other, the seam buttons' included, and shows
    /// it first: at the far left, while each other cell keeps its index.</summary>
    public static void AddMarkColumn(ListView list)
    {
        var mark = list.Columns.Add("", MarkColumnWidth, HorizontalAlignment.Center);
        mark.DisplayIndex = 0;
    }

    /// <summary>After the list, which draws with the font.</summary>
    public void Dispose()
    {
        _markFont.Dispose();
        _tip.Dispose();
    }
}
