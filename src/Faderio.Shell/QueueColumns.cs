using System.ComponentModel;
using System.Runtime.InteropServices;
using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The queue list's columns in either host (playlists issue 06, D25): the shown ones from a
/// <see cref="ColumnLayout"/>, in its order and at its widths, then the ✎ button's and the
/// speaker's, which are fixed at the far right and the far left and never ones a user shows,
/// sizes or moves. A right-click on the header opens <see cref="Menu"/>, a tick per column; a
/// header dragged to a new place moves its column; a width dragged is kept. Each change is
/// written to the open library's file, a drag's once it rests. The title takes what the other
/// columns leave, so the button stays in sight at any width.
/// </summary>
internal sealed class QueueColumns : IDisposable
{
    private const int EditColumnWidth = 28;
    private const int MinTitleWidth = 100;

    /// <summary>The columns that give way when the shown ones do not fit, in the order they do
    /// (phase 3 issue 01), each to its floor. The rest never shrink: their contents are short.</summary>
    private static readonly (QueueColumn Column, int Floor)[] GiveWay =
    [
        (QueueColumn.Path, 80),
        (QueueColumn.Album, 60),
        (QueueColumn.Artist, 60),
    ];

    private readonly ListView _list;
    private readonly ColumnLayout _default;
    private readonly string _fileName;
    private readonly Action<HashSet<RowId>> _rebuildRows;
    private readonly Action _render;
    private readonly System.Windows.Forms.Timer _save;

    private ColumnLayout _layout;
    private string? _file;
    private bool _read;
    private bool _changed;
    private bool _building;
    private bool _fitting;

    /// <summary>The width this last gave each column. The list reports every width change, among
    /// them its own replay of these when its handle is made, so only a width that differs is one
    /// the user dragged.</summary>
    private readonly Dictionary<QueueColumn, int> _widthsSet = new();

    /// <param name="list">The queue's list, a details view drawn by <see cref="QueueRowStyle"/>.</param>
    /// <param name="defaultLayout">The host's columns for a library with no file of its own.</param>
    /// <param name="fileName">The host's file in the library's store folder.</param>
    /// <param name="rebuildRows">Makes every row again, keeping these selected: called with the
    /// list emptied, since each row has a cell per column shown.</param>
    /// <param name="render">Shows the engine's view again, after a change the user made.</param>
    public QueueColumns(ListView list, ColumnLayout defaultLayout, string fileName, Action<HashSet<RowId>> rebuildRows, Action render)
    {
        _list = list;
        _default = defaultLayout;
        _fileName = fileName;
        _rebuildRows = rebuildRows;
        _render = render;
        _layout = defaultLayout;
        _save = new System.Windows.Forms.Timer { Interval = 400 };
        _save.Tick += (_, _) => Save();

        Menu = new ContextMenuStrip();
        foreach (var column in ShellCompat.Values<QueueColumn>())
        {
            var item = new ToolStripMenuItem(Presenter.ColumnHeading(column))
            {
                Tag = column,
                Enabled = column != QueueColumn.Number, // # is always shown
            };
            item.Click += (_, _) => Toggle(column);
            Menu.Items.Add(item);
        }
        Menu.Opening += (_, _) =>
        {
            foreach (ToolStripMenuItem item in Menu.Items)
            {
                item.Checked = _layout[(QueueColumn)item.Tag!].Visible;
            }
        };

        Build();
        list.ColumnWidthChanging += (_, e) =>
        {
            if (e.ColumnIndex >= EditColumn) // the editor button's, or the speaker's
            {
                e.NewWidth = _list.Columns[e.ColumnIndex].Width;
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
        // its order, so # stays first, the speaker before it and the editor button last. The list
        // is not left to move it.
        list.ColumnReordered += (_, e) =>
        {
            e.Cancel = true;
            if (e.Header?.Tag is QueueColumn column)
            {
                var to = Math.Max(0, e.NewDisplayIndex - 1); // the speaker's column is shown first
                _list.BeginInvoke(new Action(() => Move(column, to)));
            }
        };
        list.Resize += (_, _) =>
        {
            if (_list.IsHandleCreated)
            {
                // Once the list has finished sizing: fitted while it sizes, it keeps a
                // horizontal scroll bar for the wider columns it had.
                _list.BeginInvoke(new Action(Fit));
            }
            else
            {
                Fit();
            }
        };
        list.ColumnWidthChanged += (_, e) =>
        {
            if (_building || _list.Columns[e.ColumnIndex].Tag is not QueueColumn column)
            {
                return;
            }
            // The user's width, kept; one the fit gives the title or takes from a column that
            // gives way is not.
            var width = _list.Columns[e.ColumnIndex].Width;
            if ((_widthsSet.TryGetValue(column, out var set) ? set : 0) != width && _layout[column].Width != width)
            {
                _widthsSet[column] = width;
                _layout = _layout.Resize(column, width);
                _changed = true;
                // A drag changes the width at every step, so the file is written once it rests.
                _save.Stop();
                _save.Start();
            }
            // The title's width is the fit's own; any other the user sets fits the rest again.
            if (column is not QueueColumn.Title && !_fitting)
            {
                Fit();
            }
        };
    }

    /// <summary>The header's menu: a tick for each column, # greyed since it always shows.</summary>
    public ContextMenuStrip Menu { get; }

    public ColumnLayout Layout => _layout;

    /// <summary>Each row's editor button (interim transport 07), after every column shown, and
    /// before only the speaker's, which is shown at the far left.</summary>
    public int EditColumn => _list.Columns.Count - 2;

    /// <summary>A row of the list for <paramref name="row"/>, the queue's
    /// <paramref name="number"/>th: # as its own text, a cell per other column shown, then the ✎
    /// and speaker cells, which <see cref="QueueRowStyle"/> fills.</summary>
    public ListViewItem Row(int number, QueueRow row)
    {
        var item = new ListViewItem(number.ToString()) { Tag = row.Id, UseItemStyleForSubItems = false };
        foreach (var column in _layout.Shown.Skip(1))
        {
            item.SubItems.Add(Presenter.QueueCell(column.Column, row.Track));
        }
        item.SubItems.Add(""); // the editor button
        item.SubItems.Add(""); // the speaker
        return item;
    }

    /// <summary>Shows <paramref name="library"/>'s layout, if it is not the one shown: the one
    /// left is written first. True when the rows were made again.</summary>
    public bool Open(LibraryView? library)
    {
        var file = ColumnLayout.FileIn(library, _fileName);
        if (_read && file == _file)
        {
            return false;
        }
        Save();
        _file = file;
        _read = true;
        Show(file is null ? _default : ColumnLayout.Load(file, _default));
        return true;
    }

    /// <summary>A tick in the header's menu: the column shows or hides, and the layout is written.</summary>
    public void Toggle(QueueColumn column) => Change(_layout.Toggle(column));

    /// <summary>A header dropped at <paramref name="displayIndex"/> among the shown columns: #
    /// stays first and the editor button last, so a drop on either lands beside it.</summary>
    public void Move(QueueColumn column, int displayIndex) => Change(_layout.Move(column, displayIndex));

    /// <summary>For the list's own menu as it opens: a right-click on the header asks for the
    /// columns rather than the rows, so that menu is cancelled and <see cref="Menu"/> shown
    /// instead. True when it was.</summary>
    public bool OpenedOnHeader(CancelEventArgs e)
    {
        if (!IsOverHeader(Cursor.Position))
        {
            return false;
        }
        // Shown once that menu's opening is over: shown from inside it, it closes at once.
        e.Cancel = true;
        var at = Cursor.Position;
        _list.BeginInvoke(new Action(() => Menu.Show(at)));
        return true;
    }

    /// <summary>Writes the layout to the open library's file, if a change is waiting to be.</summary>
    public void Save()
    {
        _save.Stop();
        if (!_changed)
        {
            return;
        }
        _changed = false;
        if (_file is not null)
        {
            _layout.Save(_file);
        }
    }

    /// <summary>Writes a width still settling.</summary>
    public void Dispose()
    {
        Save();
        _save.Dispose();
        Menu.Dispose();
    }

    private void Change(ColumnLayout layout)
    {
        if (layout.Same(_layout))
        {
            return;
        }
        Show(layout);
        _render();
        _changed = true;
        Save();
    }

    /// <summary>The rows are made again, since each has a cell per column shown, and keep their
    /// selection.</summary>
    private void Show(ColumnLayout layout)
    {
        var selected = new HashSet<RowId>(_list.SelectedItems.Cast<ListViewItem>().Select(i => (RowId)i.Tag!));
        _layout = layout;
        _list.BeginUpdate();
        _list.Items.Clear();
        Build();
        _list.EndUpdate();
        _rebuildRows(selected);
    }

    /// <summary>Makes the header's columns from the layout: the ones shown, in its order, at its
    /// widths, then the editor button and the speaker.</summary>
    private void Build()
    {
        _building = true;
        try
        {
            _list.Columns.Clear();
            _widthsSet.Clear();
            foreach (var setting in _layout.Shown)
            {
                _widthsSet[setting.Column] = setting.Width;
                var header = _list.Columns.Add(Presenter.ColumnHeading(setting.Column), setting.Width, AlignmentOf(setting.Column));
                header.Tag = setting.Column;
            }
            // The editor button (interim transport 07): fixed at the far right.
            _list.Columns.Add("", EditColumnWidth, HorizontalAlignment.Center);
            QueueRowStyle.AddMarkColumn(_list); // the speaker, shown at the far left
            _list.AllowColumnReorder = true;
        }
        finally
        {
            _building = false;
        }
        Fit();
    }

    private static HorizontalAlignment AlignmentOf(QueueColumn column) =>
        column is QueueColumn.Length or QueueColumn.ReplayGain ? HorizontalAlignment.Right : HorizontalAlignment.Left;

    /// <summary>Sizes the title to the list's width less the other columns and room for a
    /// scroll bar, which appears without resizing the list. Where that would squeeze the title
    /// below its least, Path, then Album, then Artist give way, each to its floor, and take back
    /// up to their own widths as room returns, in the reverse order. Only with every one at its
    /// floor does the list scroll sideways. With the title hidden, nothing fills: the columns
    /// keep their widths.</summary>
    private void Fit()
    {
        // A list being disposed has given its columns up already, and can still be resized:
        // with groups shown, MusicBee closing once resized it after (phase 6 issue 09).
        if (_list.IsDisposed || _list.Disposing)
        {
            return;
        }
        var headers = _list.Columns.Cast<ColumnHeader>().ToList();
        if (headers.Find(c => c.Tag is QueueColumn.Title) is not { } title)
        {
            return;
        }
        var giving = GiveWay
            .Select(g => (Header: headers.Find(c => c.Tag is QueueColumn q && q == g.Column), g.Column, Own: _layout[g.Column].Width, g.Floor))
            .Where(g => g.Header is not null)
            .ToList();
        var free = _list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth
            - headers.Where(c => c != title && !giving.Exists(g => g.Header == c)).Sum(c => c.Width);
        // What the title's floor and every own width need beyond the room there is.
        var lacking = MinTitleWidth + giving.Sum(g => g.Own) - free;
        _fitting = true;
        try
        {
            foreach (var g in giving)
            {
                // A column the user made narrower than its floor is never widened to it.
                var width = g.Own - ShellCompat.Clamp(lacking, 0, Math.Max(g.Own - g.Floor, 0));
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

    /// <summary>Whether a point on screen is on the list's header.</summary>
    private bool IsOverHeader(Point screen)
    {
        if (!_list.IsHandleCreated)
        {
            return false;
        }
        var header = NativeMethods.SendMessage(_list.Handle, NativeMethods.LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        return header != IntPtr.Zero && NativeMethods.GetWindowRect(header, out var bounds)
            && screen.X >= bounds.Left && screen.X < bounds.Right && screen.Y >= bounds.Top && screen.Y < bounds.Bottom;
    }

    private static class NativeMethods
    {
        public const int LVM_GETHEADER = 0x1000 + 31;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr window, out Rect rect);
    }
}
