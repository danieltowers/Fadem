using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Fadem.Core.Domain;
using Fadem.Core.Playback;

using Point = System.Drawing.Point;

namespace Fadem.Shell;

/// <summary>
/// The open pair as a tab on the editor (seam markers issue 04, G3 soft): the pair's tint runs
/// straight from its rows across a 16 px gap into the editor, tinted whole, and one outline goes
/// round both, 2 px with 8 px corners, broken where it crosses a seam button. The tab joins the
/// engine's first match, the one heard next; while its rows are out of the queue's view a stub at
/// the queue's top or bottom edge stands in for them, and a click on it scrolls them into view.
/// <para>No window spans the queue, the gap and the editor, so each paints its piece of one shape,
/// worked out in the window's terms from where the controls are: the list over its rows (in its
/// off-screen paint), the list's frame and scroll bar level with the tab, the gap, the stub and
/// the editor's frame. Whenever the shape moves, as the list scrolls or the window is sized, every
/// piece is drawn again, so they meet. Nothing here computes from engine values: the rows are the
/// engine's, by <see cref="PlaybackEngine.RowPairsOf"/>, numbered as the queue numbers them, and the
/// positions the controls'. Which of the two rows the list draws higher is where the outline goes.</para>
/// <para>Decision 3 of the spec: rows between the pair's two are inside its outline, keeping their
/// own colours; repeat-all's last → first outlines each row on its own, the tab from the last;
/// repeat-one's this → this outlines its one row; a collapsed section's header stands in for the
/// rows it hides.</para>
/// </summary>
internal sealed class PairLink
{
    /// <summary>The gap between the queue and the editor's pane, there whether or not a pair is
    /// open, so the queue's columns never jump (decision 7).</summary>
    public const int GapWidth = 16;

    /// <summary>How round the outline's corners are, outside and in.</summary>
    private const float Radius = 8f;

    private readonly ListView _list;
    private readonly QueueRowStyle _rows;
    private readonly TransitionEditorPanel _editor;

    /// <summary>The rows the tab joins, as list indices, first to last: the first match's two,
    /// with any between; one row under repeat-one, or repeat-all's last. Null with none.</summary>
    private (int From, int To)? _span;

    /// <summary>Repeat-all's first row, the wrap's incoming, outlined on its own; or none.</summary>
    private int? _alone;

    /// <summary>The stub's text, above and below.</summary>
    private (string Above, string Below) _stubText = ("", "");

    /// <summary>The shape as every piece last drew it, or none.</summary>
    private Shape? _shape;

    private bool _updatePending;
    private readonly FrameStrip _frame;

    /// <param name="render">Draws the window again: the mouse on a half of the editor lights its
    /// row.</param>
    public PairLink(ListView list, QueueRowStyle rows, TransitionEditorPanel editor, Action render)
    {
        _list = list;
        _rows = rows;
        _editor = editor;
        Gap = new GapPanel(this) { Dock = DockStyle.Right, Width = GapWidth };
        Stub = new StubButton(this);
        _rows.Overlay = PaintList;
        _editor.Frame = g => Paint(g, _editor, band: false);
        _editor.HalfHovered += side =>
        {
            _rows.LitHalf = side;
            render();
        };
        _frame = new FrameStrip(this);
        foreach (var moved in new Control[] { _list, Gap, _editor })
        {
            moved.SizeChanged += (_, _) => Update();
        }
    }

    /// <summary>The gap, for the host to dock between the queue and the editor's pane.</summary>
    public Control Gap { get; }

    /// <summary>The stub, over the queue while the tab's rows are out of its view.</summary>
    internal Control Stub { get; }

    /// <summary>The rows the tab joins, first to last, or none: for a test.</summary>
    internal (int From, int To)? TabRows => _span;

    /// <summary>Repeat-all's first row, outlined on its own, or none: for a test.</summary>
    internal int? AloneRow => _alone;

    /// <summary>Whether the stub stands in for the tab's rows, out of the queue's view.</summary>
    internal bool Stubbed => _shape?.Stubbed == true;

    private Theme.Palette? Palette => _rows.Palette;

    /// <summary>Called on every render, after the queue's rows: where the editor's adjacency is in
    /// the queue, from the engine, the first of <paramref name="open"/> taking the tab.</summary>
    public void Show(PlayerView view, IReadOnlyList<RowPair> open)
    {
        (int, int)? span = null;
        int? alone = null;
        if (_editor.Adjacency is not null && open.Count > 0
            && IndexOf(view, open[0].Outgoing) is { } outgoing && IndexOf(view, open[0].Incoming) is { } incoming)
        {
            // The incoming row drawn above the outgoing, repeat-all's last → first: the tab from the
            // last row, the first outlined alone.
            span = outgoing <= incoming ? (outgoing, incoming) : (outgoing, outgoing);
            alone = outgoing <= incoming ? null : incoming;
            var (outNumber, inNumber) = (Presenter.RowNumber(view.Queue, open[0].Outgoing)!.Value, Presenter.RowNumber(view.Queue, open[0].Incoming)!.Value);
            var (outTrack, inTrack) = (view.Queue[outgoing].Track, view.Queue[incoming].Track);
            _stubText = (Presenter.FmtPairStub(true, outNumber, inNumber, outTrack, inTrack),
                Presenter.FmtPairStub(false, outNumber, inNumber, outTrack, inTrack));
        }
        _span = span;
        _alone = alone;
        _editor.Tint(span is null ? null : QueueRowStyle.TintOf(Palette));
        Update();
    }

    private static int? IndexOf(PlayerView view, RowId row)
    {
        for (var i = 0; i < view.Queue.Count; i++)
        {
            if (view.Queue[i].Id == row)
            {
                return i;
            }
        }
        return null;
    }

    // ── The shape ──────────────────────────────────────────────────────────────────────

    /// <summary>The link in the window's client terms. <see cref="Source"/> is what the tab leaves
    /// from: the tab's rows as far as they show, to the list's right edge, or the stub.
    /// <see cref="Tab"/> is the tint's band, level with it, from where the rows' cells end to the
    /// editor, or empty where the two are not level. <see cref="Header"/> is a collapsed section's
    /// header standing in among the rows, drawn in the tint by the theme, which the band leaves be.
    /// <see cref="StubText"/> is the stub's, while it stands in.</summary>
    private sealed record Shape(
        Rectangle Source, Rectangle Tab, Rectangle Editor, Rectangle Alone, Rectangle Header, bool Stubbed, string StubText);

    /// <summary>Where the link is now, from the controls, or none while nothing is open or the
    /// controls are not yet windows.</summary>
    private Shape? Compute()
    {
        if (_span is not { } span || !_list.IsHandleCreated || span.To >= _list.Items.Count)
        {
            return null;
        }
        static Rectangle InRoot(Control c, Rectangle r)
        {
            r.Offset(Origin(c));
            return r;
        }
        var rows = Rectangle.FromLTRB(0, HeaderBottom(_list), _list.ClientSize.Width, _list.ClientSize.Height);
        var editor = InRoot(_editor, _editor.ClientRectangle);
        var (bounds, cellsRight, header) = Bounds(span.From, span.To);
        var visible = Rectangle.Intersect(bounds, rows);
        Rectangle source;
        int bandFrom;
        var stubbed = visible.Height <= 2;
        var above = stubbed && bounds.Top < rows.Top;
        var text = !stubbed ? "" : above ? _stubText.Above : _stubText.Below;
        if (stubbed)
        {
            // Never wider than the rows leave room for: a long name ends in "…".
            var size = new Size(Math.Min(TextRenderer.MeasureText(text, _list.Font).Width + 20, Math.Max(80, rows.Width - 40)), 24);
            var stub = new Rectangle(rows.Right - size.Width, above ? rows.Top + 2 : rows.Bottom - size.Height - 2, size.Width, size.Height);
            source = InRoot(_list, stub);
            bandFrom = source.Right;
            header = Rectangle.Empty;
        }
        else
        {
            source = InRoot(_list, Rectangle.FromLTRB(visible.Left, visible.Top, rows.Right, visible.Bottom));
            bandFrom = InRoot(_list, new Rectangle(Math.Min(cellsRight, rows.Right), 0, 0, 0)).X;
            header = header.IsEmpty ? header : InRoot(_list, Rectangle.Intersect(header, rows));
        }
        var top = Math.Max(source.Top, editor.Top);
        var bottom = Math.Min(source.Bottom, editor.Bottom);
        var tab = bottom - top >= 4 ? Rectangle.FromLTRB(bandFrom, top, editor.Left + 2, bottom) : Rectangle.Empty;
        var alone = Rectangle.Empty;
        if (_alone is { } row && row < _list.Items.Count)
        {
            var (own, _, _) = Bounds(row, row);
            var shown = Rectangle.Intersect(own, rows);
            alone = shown.Height > 2 ? InRoot(_list, Rectangle.FromLTRB(shown.Left, shown.Top, rows.Right, shown.Bottom)) : Rectangle.Empty;
        }
        return new Shape(source, tab, editor, alone, header, stubbed, text);
    }

    /// <summary>Rows <paramref name="from"/> to <paramref name="to"/> as the list shows them, in its
    /// client terms: each row, or the header of the collapsed section hiding it; where the rows'
    /// cells end; and the headers standing in among them.</summary>
    private (Rectangle Bounds, int CellsRight, Rectangle Header) Bounds(int from, int to)
    {
        Rectangle bounds = Rectangle.Empty, header = Rectangle.Empty;
        var cellsRight = 0;
        for (var i = from; i <= to; i++)
        {
            var item = _list.Items[i];
            Rectangle r;
            if (_list.ShowGroups && item.Group is { } group && ListGroups.IsCollapsed(_list, group))
            {
                r = ListGroups.HeaderBounds(_list, group);
                header = header.IsEmpty ? r : Rectangle.Union(header, r);
            }
            else
            {
                r = item.Bounds;
                cellsRight = Math.Max(cellsRight, r.Right);
            }
            bounds = bounds.IsEmpty ? r : Rectangle.Union(bounds, r);
        }
        return (bounds, cellsRight == 0 ? bounds.Right : cellsRight, header);
    }

    /// <summary>Where <paramref name="control"/>'s client area starts in its window's: by the
    /// controls' own places, each in its parent's client area, so it holds before any is shown.</summary>
    private static Point Origin(Control control)
    {
        var at = Point.Empty;
        for (var c = control; c.Parent is { } parent; c = parent)
        {
            var inset = Inset(c);
            at.Offset(c.Left + inset.X, c.Top + inset.Y);
        }
        return at;
    }

    /// <summary>Where <paramref name="control"/>'s client area starts within its bounds: past its
    /// frame.</summary>
    private static Point Inset(Control control)
    {
        if (control.IsHandleCreated && GetWindowRect(control.Handle, out var window))
        {
            var client = control.PointToScreen(Point.Empty);
            return new Point(client.X - window.Left, client.Y - window.Top);
        }
        return new Point((control.Width - control.ClientSize.Width) / 2, (control.Height - control.ClientSize.Height) / 2);
    }

    /// <summary>Where the list's rows start: under its column header.</summary>
    private static int HeaderBottom(ListView list)
    {
        var header = SendMessage(list.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
        return header != IntPtr.Zero && IsWindowVisible(header) && GetWindowRect(header, out var r) ? r.Bottom - r.Top : 0;
    }

    /// <summary>Works the shape out again; where it has moved, places the stub and has every piece
    /// drawn again.</summary>
    private void Update()
    {
        _updatePending = false;
        var shape = Compute();
        if (shape == _shape)
        {
            return;
        }
        _shape = shape;
        PlaceStub(shape);
        _list.Invalidate(); // a scroll moved the old outline with the rows; this draws it whole
        _frame.Redraw();
        Gap.Invalidate();
        _editor.Invalidate();
        Stub.Invalidate();
    }

    /// <summary>After a piece found the shape moved while drawing: once, when the drawing is done.</summary>
    private void UpdateSoon()
    {
        if (_updatePending || !_list.IsHandleCreated)
        {
            return;
        }
        _updatePending = true;
        _list.BeginInvoke(new Action(Update));
    }

    // ── The pieces ─────────────────────────────────────────────────────────────────────

    /// <summary>Draws the link's piece on <paramref name="piece"/>, whose client area
    /// <paramref name="g"/> draws on: the band where <paramref name="band"/>, and the outline,
    /// broken at <paramref name="holes"/> (window terms). False while there is no link.</summary>
    private bool Paint(Graphics g, Control piece, bool band, IEnumerable<Rectangle>? holes = null) =>
        Paint(g, Origin(piece), band, holes);

    private bool Paint(Graphics g, Point origin, bool band, IEnumerable<Rectangle>? holes = null)
    {
        if (_shape is not { } shape)
        {
            return false;
        }
        var state = g.Save();
        g.TranslateTransform(-origin.X, -origin.Y);
        g.PixelOffsetMode = PixelOffsetMode.Half; // a 2 px line on whole pixels
        if (band && !shape.Tab.IsEmpty)
        {
            using var tint = new SolidBrush(QueueRowStyle.TintOf(Palette));
            using var clip = new Region(shape.Tab);
            clip.Exclude(shape.Header); // drawn in the tint by the theme, chevron and all
            g.FillRegion(tint, clip);
        }
        foreach (var hole in holes ?? [])
        {
            g.ExcludeClip(hole);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(QueueRowStyle.OutlineOf(Palette), 2f);
        foreach (var outline in Outlines(shape))
        {
            using (outline)
            {
                g.DrawPath(pen, outline);
            }
        }
        g.Restore(state);
        return true;
    }

    /// <summary>The outlines, each a closed path: the tab's rows and the editor as one shape, the
    /// tab joining them; or, where the two are not level, each on its own. Repeat-all's first row
    /// on its own besides.</summary>
    private static IEnumerable<GraphicsPath> Outlines(Shape shape)
    {
        var (s, e) = (shape.Source, shape.Editor);
        if (shape.Tab.IsEmpty)
        {
            yield return Rounded(Box(s));
            yield return Rounded(Box(e));
        }
        else
        {
            // Each line 2 px wide, just inside the edge it follows.
            float left = s.Left + 1, top = shape.Tab.Top + 1, bottom = shape.Tab.Bottom - 1;
            float edge = e.Left + 1, right = e.Right - 1, eTop = e.Top + 1, eBottom = e.Bottom - 1;
            yield return Rounded(
                new PointF(left, top), new PointF(edge, top), new PointF(edge, eTop), new PointF(right, eTop),
                new PointF(right, eBottom), new PointF(edge, eBottom), new PointF(edge, bottom), new PointF(left, bottom));
        }
        if (!shape.Alone.IsEmpty)
        {
            yield return Rounded(Box(shape.Alone));
        }
    }

    private static PointF[] Box(Rectangle r) =>
    [
        new PointF(r.Left + 1, r.Top + 1), new PointF(r.Right - 1, r.Top + 1),
        new PointF(r.Right - 1, r.Bottom - 1), new PointF(r.Left + 1, r.Bottom - 1),
    ];

    /// <summary>A closed path through <paramref name="points"/>, each corner rounded: the line stops
    /// short of the corner and curves round it, by no more than half of either side.</summary>
    private static GraphicsPath Rounded(params PointF[] points)
    {
        var path = new GraphicsPath();
        var n = points.Length;
        for (var i = 0; i < n; i++)
        {
            var at = points[i];
            PointF Toward(PointF to)
            {
                var dx = to.X - at.X;
                var dy = to.Y - at.Y;
                var length = (float)Math.Sqrt(dx * dx + dy * dy);
                var r = Math.Min(Radius, length / 2);
                return length == 0 ? at : new PointF(at.X + dx / length * r, at.Y + dy / length * r);
            }
            var a = Toward(points[(i + n - 1) % n]);
            var b = Toward(points[(i + 1) % n]);
            if (path.PointCount > 0)
            {
                path.AddLine(path.GetLastPoint(), a);
            }
            path.AddBezier(a, at, at, b);
        }
        path.CloseFigure();
        return path;
    }

    /// <summary>The list's piece, over its rows: the band past the cells and the outline, broken
    /// where it crosses a seam button showing on its top or bottom edge. A scroll moves the rows
    /// before they are drawn, so the shape is worked out again here.</summary>
    private void PaintList(Graphics g)
    {
        if (Compute() != _shape)
        {
            UpdateSoon();
        }
        if (_shape is null)
        {
            return; // stubbed, the outline round the stub is under it, and repeat-all's first row still shows
        }
        var origin = Origin(_list);
        var holes = new List<Rectangle>();
        foreach (var row in new[] { _span?.From - 1, _span?.To, _alone - 1, _alone }.OfType<int>())
        {
            if (_rows.SeamButtonBounds(_list, row) is { } button)
            {
                button.Offset(origin);
                button.Inflate(1, 1);
                holes.Add(button);
            }
        }
        Paint(g, origin, band: true, holes);
    }

    /// <summary>The stub: in the tint, the pair named, its outline the tab's.</summary>
    private void PaintStub(Control stub, Graphics g)
    {
        using (var tint = new SolidBrush(QueueRowStyle.TintOf(Palette)))
        {
            g.FillRectangle(tint, stub.ClientRectangle);
        }
        TextRenderer.DrawText(g, stub.Text, _list.Font, Rectangle.FromLTRB(10, 0, stub.Width, stub.Height),
            Palette?.Text ?? SystemColors.WindowText,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        Paint(g, stub, band: false);
    }

    /// <summary>Shows the stub where the shape has it, over the list, or hides it.</summary>
    private void PlaceStub(Shape? shape)
    {
        if (shape is not { Stubbed: true } || _list.Parent is not { } parent)
        {
            Stub.Visible = false;
            return;
        }
        if (Stub.Parent != parent)
        {
            parent.Controls.Add(Stub);
        }
        var origin = Origin(parent);
        var bounds = shape.Source;
        bounds.Offset(-origin.X, -origin.Y);
        Stub.Bounds = bounds;
        Stub.Text = shape.StubText;
        Stub.Visible = true;
        Stub.BringToFront();
    }

    /// <summary>A click on the stub: the tab's rows scroll into view, the selection left as it was.
    /// A collapsed section's header standing in for them is scrolled to, never expanded.</summary>
    internal void ScrollIntoView()
    {
        if (_span is not { } span || !_list.IsHandleCreated || span.To >= _list.Items.Count)
        {
            return;
        }
        var (bounds, _, header) = Bounds(span.From, span.To);
        if (header.IsEmpty)
        {
            _list.EnsureVisible(span.To);
            _list.EnsureVisible(span.From);
        }
        else
        {
            var rows = Rectangle.FromLTRB(0, HeaderBottom(_list), _list.ClientSize.Width, _list.ClientSize.Height);
            var by = bounds.Top < rows.Top ? bounds.Top - rows.Top - QueueRowStyle.RowHeight : bounds.Bottom - rows.Bottom + QueueRowStyle.RowHeight;
            _ = SendMessage(_list.Handle, LVM_SCROLL, IntPtr.Zero, (IntPtr)by);
        }
        Update();
    }

    /// <summary>The gap between the queue and the editor's pane: the tab across it.</summary>
    private sealed class GapPanel : Control
    {
        private readonly PairLink _link;

        public GapPanel(PairLink link)
        {
            _link = link;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            _link.Paint(e.Graphics, this, band: true);
        }
    }

    /// <summary>The stub for the tab's rows out of the queue's view.</summary>
    private sealed class StubButton : Control
    {
        private readonly PairLink _link;

        public StubButton(PairLink link)
        {
            _link = link;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Visible = false;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e) => _link.PaintStub(this, e.Graphics);

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _link.ScrollIntoView();
        }
    }

    /// <summary>
    /// The list's piece outside its rows: its frame and scroll bar, level with the tab, which the
    /// band and the outline's edges run over. Windows draws them outside the client area, and
    /// draws the scroll bar again as it moves, so the piece is drawn again after each. Hooked last,
    /// once the list's other hooks are (the theme's hairline frame among them), so it is drawn
    /// over them.
    /// </summary>
    private sealed class FrameStrip : NativeWindow
    {
        private readonly PairLink _link;

        public FrameStrip(PairLink link)
        {
            _link = link;
            var list = link._list;
            void Attach() => list.BeginInvoke(new Action(() =>
            {
                if (list.IsHandleCreated && Handle != list.Handle)
                {
                    AssignHandle(list.Handle);
                }
            }));
            if (list.IsHandleCreated)
            {
                Attach();
            }
            list.HandleCreated += (_, _) => Attach();
            list.HandleDestroyed += (_, _) => ReleaseHandle();
        }

        /// <summary>Has Windows draw the frame and scroll bar again, and so this piece.</summary>
        public void Redraw()
        {
            if (Handle != IntPtr.Zero)
            {
                _ = RedrawWindow(Handle, IntPtr.Zero, IntPtr.Zero, RDW_FRAME | RDW_INVALIDATE);
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (_link._shape is not { Tab.IsEmpty: false })
            {
                return; // no band crosses the frame
            }
            switch (m.Msg)
            {
                case WM_PRINT when ((long)m.LParam & PRF_NONCLIENT) != 0:
                    Draw(m.WParam);
                    break;
                case WM_NCPAINT or WM_PAINT or WM_VSCROLL or WM_HSCROLL or WM_MOUSEWHEEL or WM_NCMOUSEMOVE
                    or WM_NCMOUSELEAVE or WM_NCLBUTTONDOWN or WM_NCLBUTTONUP or WM_CAPTURECHANGED or WM_WINDOWPOSCHANGED:
                    var dc = GetWindowDC(Handle);
                    try
                    {
                        Draw(dc);
                    }
                    finally
                    {
                        _ = ReleaseDC(Handle, dc);
                    }
                    break;
            }
        }

        private void Draw(IntPtr dc)
        {
            var list = _link._list;
            if (dc == IntPtr.Zero)
            {
                return;
            }
            var inset = Inset(list);
            var origin = Origin(list);
            using var g = Graphics.FromHdc(dc);
            // The client area is the list's own piece: only the frame and scroll bar here.
            g.ExcludeClip(new Rectangle(inset, list.ClientSize));
            _link.Paint(g, new Point(origin.X - inset.X, origin.Y - inset.Y), band: true);
        }

        private const int WM_WINDOWPOSCHANGED = 0x0047;
        private const int WM_NCPAINT = 0x0085;
        private const int WM_PAINT = 0x000F;
        private const int WM_NCMOUSEMOVE = 0x00A0;
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int WM_NCLBUTTONUP = 0x00A2;
        private const int WM_HSCROLL = 0x0114;
        private const int WM_VSCROLL = 0x0115;
        private const int WM_MOUSEWHEEL = 0x020A;
        private const int WM_CAPTURECHANGED = 0x0215;
        private const int WM_NCMOUSELEAVE = 0x02A2;
        private const int WM_PRINT = 0x0317;
        private const int PRF_NONCLIENT = 0x02;
        private const uint RDW_INVALIDATE = 0x0001;
        private const uint RDW_FRAME = 0x0400;

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

        [DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr hwnd, IntPtr rect, IntPtr region, uint flags);
    }

    private const int LVM_FIRST = 0x1000;
    private const int LVM_SCROLL = LVM_FIRST + 20;
    private const int LVM_GETHEADER = LVM_FIRST + 31;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
