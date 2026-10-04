using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// How a queue row looks in either host (phase 5 issue 11): the attended row's speaker
/// (<see cref="RowMarks"/>), an unreadable row's grey, the row's label and tip, and the editor
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

    /// <summary>The pencil's fonts: the list's own size draws it at eight pixels.</summary>
    public QueueRowStyle()
    {
        _authoredFont = new Font(_buttonFont, FontStyle.Bold);
    }

    /// <summary>Styles every row <paramref name="list"/> shows for the queue in
    /// <paramref name="view"/>, as far as both reach; <paramref name="editColumn"/> is the ✎
    /// cell's index.</summary>
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
        var back = SystemColors.Window;
        var fore = row.IsUnreadable ? SystemColors.GrayText : SystemColors.WindowText;
        var authored = row.NextAuthored == true;
        var button = item.SubItems[editColumn];
        foreach (ListViewItem.ListViewSubItem cell in item.SubItems)
        {
            if (cell.BackColor != back)
            {
                cell.BackColor = back;
            }
            var cellFore = cell == button && !authored ? SystemColors.GrayText : fore;
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
        var label = Presenter.RowLabel(index + 1, row.IsUnreadable, RowMarks.Of(row.IsCurrent, view.Transport));
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
    /// colours and font, aligned as its column, on <see cref="RowMarks.Selected"/> while its row
    /// is selected. The header is the system's.</summary>
    public static void Draw(ListView list)
    {
        list.OwnerDraw = true;
        list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        list.DrawItem += (_, _) => { }; // a details view draws by cell
        list.DrawSubItem += (_, e) =>
        {
            if (e.Item is not { } item || e.SubItem is not { } cell)
            {
                return;
            }
            // The first cell's bounds span the whole row: its column's width bounds it.
            var bounds = new Rectangle(e.Bounds.X, e.Bounds.Y, list.Columns[e.ColumnIndex].Width, e.Bounds.Height);
            var back = item.Selected ? RowMarks.Selected : cell.BackColor;
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

    /// <summary>After the list, which draws with the fonts.</summary>
    public void Dispose()
    {
        _buttonFont.Dispose();
        _authoredFont.Dispose();
    }
}
