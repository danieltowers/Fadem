using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Fadem.Core.Playback;

namespace Fadem.Shell;

/// <summary>
/// How a queue row looks in either host (phase 5 issue 11): the attended row's speaker
/// (<see cref="RowMarks"/>) in a column of its own, shown at the far left but kept last, after
/// the ✎ cell, so every other cell keeps its index (<see cref="AddMarkColumn"/>); an unreadable
/// row's grey, the row's label and tip, and the editor
/// button cell, ✎ on every row but the last, bold while this → next is authored and grey and
/// plain while it is not. A render runs on every tick, so each cell's look is set only when it
/// changes. The fonts are this style's: the list's own size draws the pencil at eight pixels.
/// <see cref="Draw"/> has the list drawn by this style, so a selected row is
/// <see cref="RowMarks.Selected"/>'s grey rather than the system's blue, and each row's image,
/// a section's stripe, at its left edge (studio theme issue 07).
/// </summary>
internal sealed class QueueRowStyle : IDisposable
{
    private readonly Font _buttonFont = new("Segoe UI Symbol", 11f);
    private readonly Font _authoredFont;
    private readonly Font _markFont = new("Segoe UI Symbol", 12f);

    /// <summary>The speaker's column: wide enough for its glyph, and no wider.</summary>
    public const int MarkColumnWidth = 26;

    /// <summary>The rows' height: tall enough for the speaker. A section's stripes are as tall.</summary>
    public const int RowHeight = 22;

    /// <summary>The colours rows are drawn in (studio theme issue 02), or none for the system's.</summary>
    public Theme.Palette? Palette { get; set; }

    /// <summary>The pencil's fonts: the list's own size draws it at eight pixels.</summary>
    public QueueRowStyle()
    {
        _authoredFont = new Font(_buttonFont, FontStyle.Bold);
    }

    /// <summary>Styles every row <paramref name="list"/> shows for the queue in
    /// <paramref name="view"/>, as far as both reach; <paramref name="editColumn"/> is the ✎
    /// cell's index, and the speaker's cell is the one after it.</summary>
    public void Apply(ListView list, PlayerView view, int editColumn)
    {
        for (var i = 0; i < list.Items.Count && i < view.Queue.Count; i++)
        {
            Apply(list.Items[i], view.Queue[i], i, view, editColumn);
        }
    }

    /// <summary>Styles <paramref name="item"/>, the list's row at <paramref name="index"/>, for
    /// <paramref name="row"/>.</summary>
    public void Apply(ListViewItem item, QueueRow row, int index, PlayerView view, int editColumn)
    {
        var back = Palette?.Well ?? SystemColors.Window;
        var dim = Palette?.Dim ?? SystemColors.GrayText;
        // The attended row, the one that plays, reads at a glance in the accent (studio theme issue 03).
        var fore = row.IsUnreadable ? dim
            : row.IsCurrent && Palette is { } p ? p.Accent
            : Palette?.Text ?? SystemColors.WindowText;
        var authored = row.NextAuthored == true;
        var button = item.SubItems[editColumn];
        foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
        {
            if (cell.BackColor != back)
            {
                cell.BackColor = back;
            }
            // ✎ is the plain text when authored, on the playing row too, and dim when not.
            var cellFore = cell != button ? fore : authored && !row.IsUnreadable ? Palette?.Text ?? SystemColors.WindowText : dim;
            if (cell.ForeColor != cellFore)
            {
                cell.ForeColor = cellFore;
            }
        }
        var glyph = Presenter.EditGlyph(row.NextAuthored, isLast: index == view.Queue.Count - 1);
        if (button.Text != glyph)
        {
            button.Text = glyph;
        }
        var font = authored ? _authoredFont : _buttonFont;
        if (button.Font != font)
        {
            button.Font = font;
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

    /// <summary>The row whose editor button is at <paramref name="location"/> in
    /// <paramref name="list"/>, or null: a click anywhere else on a row, or on a row that shows
    /// no button, is not a press of it.</summary>
    public static int? EditButtonAt(ListView list, Point location, int editColumn) =>
        list.HitTest(location) is { Item: { } item, SubItem: { } cell }
            && cell == item.SubItems[editColumn] && cell.Text.Length > 0
            ? item.Index
            : null;

    /// <summary>Has <paramref name="list"/>, a details view, drawn here: each cell in its own
    /// colours and font, aligned as its column, on the palette's Raised (or
    /// <see cref="RowMarks.Selected"/> with none) while its row is selected. The header is drawn
    /// in the palette, or is the system's with none. With <paramref name="ownRowHeight"/> the rows
    /// keep the list's own height (the playlists', studio theme issue 06), not the queue's.</summary>
    public static void Draw(ListView list, Theme.Palette? palette = null, bool ownRowHeight = false)
    {
        list.OwnerDraw = true;
        _ = new BufferedPaint(list);
        _ = new NoHotRow(list);
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
            using var buffer = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(buffer))
            {
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
                // A centred cell is the ✎ button's: narrow, and never cut short.
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
    /// </summary>
    private sealed class BufferedPaint : NativeWindow
    {
        private readonly ListView _list;

        public BufferedPaint(ListView list)
        {
            _list = list;
            if (list.IsHandleCreated)
            {
                AssignHandle(list.Handle);
            }
            list.HandleCreated += (_, _) => AssignHandle(list.Handle);
            list.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
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

    /// <summary>Adds the speaker's column after every other, the ✎ button's included, and shows it
    /// first: at the far left, while each other cell keeps its index.</summary>
    public static void AddMarkColumn(ListView list)
    {
        var mark = list.Columns.Add("", MarkColumnWidth, HorizontalAlignment.Center);
        mark.DisplayIndex = 0;
    }

    /// <summary>After the list, which draws with the fonts.</summary>
    public void Dispose()
    {
        _buttonFont.Dispose();
        _authoredFont.Dispose();
        _markFont.Dispose();
    }
}
