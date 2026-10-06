using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// How a queue row looks in either host (phase 5 issue 11): the attended row's speaker
/// (<see cref="RowMarks"/>) in a column of its own, shown at the far left but kept last, after
/// the ✎ cell, so every other cell keeps its index (<see cref="AddMarkColumn"/>); an unreadable
/// row's grey, the row's label and tip, and the editor
/// button cell, ✎ on every row but the last, bold while this → next is authored and grey and
/// plain while it is not. A render runs on every tick, so each cell's look is set only when it
/// changes. The fonts are this style's: the list's own size draws the pencil at eight pixels.
/// <see cref="Draw"/> has the list drawn by this style, so a selected row is
/// <see cref="RowMarks.Selected"/>'s grey rather than the system's blue.
/// </summary>
internal sealed class QueueRowStyle : IDisposable
{
    private readonly Font _buttonFont = new("Segoe UI Symbol", 11f);
    private readonly Font _authoredFont;
    private readonly Font _markFont = new("Segoe UI Symbol", 12f);

    /// <summary>The speaker's column: wide enough for its glyph, and no wider.</summary>
    public const int MarkColumnWidth = 26;

    /// <summary>The rows' height: tall enough for the speaker.</summary>
    private const int RowHeight = 22;

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
    /// in the palette, or is the system's with none.</summary>
    public static void Draw(ListView list, Theme.Palette? palette = null)
    {
        list.OwnerDraw = true;
        // An image list's height sets the rows': the list has no other.
        var rows = new ImageList { ImageSize = new Size(1, RowHeight) };
        list.SmallImageList = rows;
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
            var back = item.Selected ? palette?.Raised ?? RowMarks.Selected : cell.BackColor;
            using (var brush = new SolidBrush(back))
            {
                e.Graphics.FillRectangle(brush, bounds);
            }
            var align = list.Columns[e.ColumnIndex].TextAlign switch
            {
                HorizontalAlignment.Right => TextFormatFlags.Right,
                HorizontalAlignment.Center => TextFormatFlags.HorizontalCenter,
                _ => TextFormatFlags.Left,
            };
            // A centred cell is the ✎ button's: narrow, and never cut short.
            var centred = align == TextFormatFlags.HorizontalCenter;
            var text = centred ? bounds : Rectangle.Inflate(bounds, -4, 0);
            TextRenderer.DrawText(
                e.Graphics, cell.Text, cell.Font ?? list.Font, text, cell.ForeColor, back,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine
                | (centred ? TextFormatFlags.NoPadding : TextFormatFlags.EndEllipsis));
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
