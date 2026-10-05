using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The seek bar (interim transport issue 04). A click anywhere on the travel seeks there; a drag
/// moves the thumb with the mouse and seeks once, on release; ← and → step a second and seek on
/// key-up. It speaks <see cref="Playhead"/> and <see cref="Length"/> at its edge: pixels never
/// leave it. Disabled, with no thumb, while nothing is attended. Replaces a stock TrackBar,
/// whose channel click paged by its <c>LargeChange</c> instead of jumping. Red marks, the full
/// height of the bar with a square handle hung below, show where the track's fades begin and end, as the engine resolves them;
/// between each pair a grey wedge drawn as the volume goes: rising for the fade-in, falling for
/// the fade-out. A scale runs along the groove from 0: a short tick every half second below it,
/// and every ten seconds a longer one through it, labelled m:ss above. Where half seconds would
/// fall closer than <see cref="MinTickGap"/> the short ticks step a second instead, then five;
/// a label that would overlap the one before it is left out.
/// <para>The marks and wedges are handles (phase 6 issue 11). A press within
/// <see cref="MarkReach"/> of a mark drags that mark; a press inside a wedge drags the whole
/// fade; either only above or below the middle band, the groove and the thumb's height, where a
/// press always seeks. The dragged mark or wedge follows the mouse, held only while the button is
/// down, and is raised once, on release, as playheads.</para>
/// </summary>
internal sealed class SeekBar : Control
{
    /// <summary>Half the thumb: the travel runs from here to the width less this, so the thumb
    /// is whole at both ends.</summary>
    internal const int Inset = 6;

    private const int TrackHeight = 4;
    private const int ThumbHeight = 18;
    private static readonly TimeSpan KeyStep = TimeSpan.FromSeconds(1);

    /// <summary>The scale: a minor tick every half second, coarsening to a second then five as the
    /// track lengthens; a labelled major one every ten.</summary>
    private static readonly int[] MinorTickSteps = { 500, 1000, 5000 };
    private const int MajorTickMs = 10_000;
    /// <summary>The fewest pixels between minor ticks: the finest step that keeps them this far
    /// apart is drawn, and none if even five seconds would be a smear.</summary>
    internal const int MinTickGap = 3;
    private const int LabelGap = 4;
    private readonly Font _labelFont;

    private Length? _length;
    private FadeMarks _marks = FadeMarks.None;
    private static readonly Color MarkColour = Color.FromArgb(220, 20, 20);
    private static readonly Color FadeShade = Color.FromArgb(200, 200, 200);
    /// <summary>The time scale, kept lighter than the groove so it reads as a guide.</summary>
    private static readonly Color MinorTickColour = Color.FromArgb(195, 195, 195);
    private static readonly Color MajorTickColour = Color.FromArgb(150, 150, 150);
    private static readonly Color LabelColour = Color.FromArgb(125, 125, 125);
    private bool _dragging;
    private bool _stepping;

    /// <summary>How near a mark, in pixels either way, a press takes hold of it.</summary>
    internal const int MarkReach = 4;

    /// <summary>The square handle each mark hangs from, at the foot of the bar: the line ends
    /// where it begins.</summary>
    private const int HandleSize = 8;

    /// <summary>A mark or a wedge held since the press: the mark, or the fade the wedge shows,
    /// and where the mouse pressed and now is. Null except while one is held.</summary>
    private Hold? _hold;

    private sealed class Hold
    {
        public FadeMark? Mark { get; init; }
        public FadeSide Side { get; init; }
        public int PressX { get; init; }
        public int MouseX { get; set; }
        public int Dx => MouseX - PressX;
    }

    public SeekBar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = "Seek";
        Text = "Seek"; // the window text is what UI Automation names it by; it is never drawn
        Enabled = false;
        _labelFont = new Font(Font.FontFamily, 6.5f);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _labelFont.Dispose();
        }
        base.Dispose(disposing);
    }

    /// <summary>A seek the user asked for: once per click, drag or run of arrow keys.</summary>
    public event Action<Playhead>? SeekRequested;

    /// <summary>A mark dragged and dropped: where the mark was dropped.</summary>
    public event Action<FadeMark, Playhead>? FadeMarkMoved;

    /// <summary>A wedge dragged: the fade taken where the mouse pressed, dropped where it was
    /// released.</summary>
    public event Action<FadeSide, Playhead, Playhead>? FadeMoved;

    /// <summary>Where the thumb stands: the view's playhead, or the mouse's while dragging.</summary>
    public Playhead Playhead { get; private set; }

    /// <summary>The view's attended playhead, length and fade marks, on every render. Ignored for
    /// the playhead while the user holds the thumb or an arrow key.</summary>
    public void Show(Playhead playhead, Length? length, FadeMarks? marks = null)
    {
        marks ??= FadeMarks.None;
        if (marks != _marks || length != _length)
        {
            _marks = marks;
            Invalidate();
        }
        _length = length;
        Enabled = length is not null;
        if (length is null)
        {
            _dragging = _stepping = false;
            _hold = null;
        }
        if (!_dragging && !_stepping && playhead != Playhead)
        {
            Playhead = playhead;
            Invalidate();
        }
    }

    internal Playhead PlayheadAt(int x)
    {
        var travel = Math.Max(1, Width - 2 * Inset);
        var fraction = Math.Min(1, Math.Max(0, (x - Inset) / (double)travel));
        return Playhead.FromMilliseconds((int)Math.Round(fraction * (_length?.Milliseconds ?? 0)));
    }

    private int XOf(Playhead playhead)
    {
        var length = _length?.Milliseconds ?? 0;
        var fraction = length > 0 ? Math.Min(1, Math.Max(0, playhead.Milliseconds / (double)length)) : 0;
        return Inset + (int)Math.Round(fraction * (Width - 2 * Inset));
    }

    private bool Live => Enabled && _length is not null;

    private void MoveThumb(Playhead playhead)
    {
        Playhead = playhead;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Live || e.Button != MouseButtons.Left)
        {
            return;
        }
        Focus();
        if (HandleAt(e.X, e.Y) is { } hold)
        {
            _hold = hold;
            return;
        }
        _dragging = true;
        MoveThumb(PlayheadAt(e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_hold is { } hold)
        {
            hold.MouseX = e.X;
            Invalidate();
            return;
        }
        if (_dragging)
        {
            MoveThumb(PlayheadAt(e.X));
            return;
        }
        var hover = Live ? HandleAt(e.X, e.Y) : null;
        Cursor = hover is null ? Cursors.Default : hover.Mark is null ? Cursors.SizeAll : Cursors.SizeWE;
    }

    /// <summary>The handle a press at (<paramref name="x"/>, <paramref name="y"/>) takes hold of: the
    /// nearest mark within <see cref="MarkReach"/>, else the wedge it is inside; null in the
    /// middle band, which seeks, and away from every fade.</summary>
    private Hold? HandleAt(int x, int y)
    {
        if (Math.Abs(y - Height / 2) <= ThumbHeight / 2)
        {
            return null;
        }
        var nearest = Marks()
            .Where(m => m.X is { } at && Math.Abs(at - x) <= MarkReach)
            .OrderBy(m => Math.Abs(m.X!.Value - x))
            .Select(m => (FadeMark?)m.Mark)
            .FirstOrDefault();
        if (nearest is { } mark)
        {
            return new Hold { Mark = mark, Side = mark.Side(), PressX = x, MouseX = x };
        }
        foreach (var (side, start, end) in new[]
        {
            (FadeSide.FadeIn, _marks.FadeInStart, _marks.FadeInEnd),
            (FadeSide.FadeOut, _marks.FadeOutStart, _marks.FadeOutEnd),
        })
        {
            if (start is { } from && end is { } to && x >= XOf(Playhead.At(from)) && x <= XOf(Playhead.At(to)))
            {
                return new Hold { Side = side, PressX = x, MouseX = x };
            }
        }
        return null;
    }

    /// <summary>Each mark and where it is drawn: where the view puts it, moved with the mouse
    /// while it, or its wedge, is held. Null where the view has none.</summary>
    private IEnumerable<(FadeMark Mark, int? X)> Marks()
    {
        foreach (var (mark, point) in new[]
        {
            (FadeMark.FadeInStart, _marks.FadeInStart),
            (FadeMark.FadeInEnd, _marks.FadeInEnd),
            (FadeMark.FadeOutStart, _marks.FadeOutStart),
            (FadeMark.FadeOutEnd, _marks.FadeOutEnd),
        })
        {
            int? x = point is { } p ? XOf(Playhead.At(p)) : null;
            if (x is not null && _hold is { } hold && (hold.Mark == mark || (hold.Mark is null && hold.Side == mark.Side())))
            {
                x += HeldDx(hold);
            }
            yield return (mark, x);
        }
    }

    /// <summary>How far the held mark or wedge is drawn from where the view puts it: as far as the
    /// mouse went, but the fade-out's start stops at the fade-in's end and the fade-in's end at
    /// the fade-out's start, as the engine stops them. Only the edge facing the other fade is
    /// stopped; a fade-out's end or a fade-in's start moves freely.</summary>
    private int HeldDx(Hold hold)
    {
        var dx = hold.Dx;
        int? XAt(Faderio.Core.Domain.Point? point) => point is { } p ? XOf(Playhead.At(p)) : null;
        if (XAt(_marks.FadeInEnd ?? _marks.FadeInStart) is not { } inEnd || XAt(_marks.FadeOutStart) is not { } outStart)
        {
            return dx;
        }
        if (hold.Mark is null or FadeMark.FadeOutStart && hold.Side == FadeSide.FadeOut)
        {
            return Math.Max(dx, inEnd - outStart);
        }
        if (hold.Mark is null or FadeMark.FadeInEnd && hold.Side == FadeSide.FadeIn)
        {
            return Math.Min(dx, outStart - inEnd);
        }
        return dx;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (_hold is { } hold && e.Button == MouseButtons.Left)
        {
            hold.MouseX = e.X;
            var dropX = hold.Mark is { } held ? Marks().First(m => m.Mark == held).X : null;
            _hold = null;
            Invalidate();
            var dx = HeldDx(hold);
            if (dx == 0)
            {
                return;
            }
            if (hold.Mark is { } mark)
            {
                if (dropX is { } x)
                {
                    FadeMarkMoved?.Invoke(mark, PlayheadAt(x));
                }
            }
            else
            {
                FadeMoved?.Invoke(hold.Side, PlayheadAt(hold.PressX), PlayheadAt(hold.PressX + dx));
            }
            return;
        }
        if (!_dragging || e.Button != MouseButtons.Left)
        {
            return;
        }
        _dragging = false;
        MoveThumb(PlayheadAt(e.X));
        SeekRequested?.Invoke(Playhead);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Live || _dragging || e.KeyCode is not (Keys.Left or Keys.Right))
        {
            return;
        }
        var step = e.KeyCode == Keys.Right ? KeyStep : -KeyStep;
        var target = Playhead.TimeSpan + step;
        var end = _length!.Value.TimeSpan;
        _stepping = true;
        MoveThumb(Playhead.FromMilliseconds((int)Math.Round(
            (target < TimeSpan.Zero ? TimeSpan.Zero : target > end ? end : target).TotalMilliseconds)));
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (!_stepping || e.KeyCode is not (Keys.Left or Keys.Right))
        {
            return;
        }
        _stepping = false;
        SeekRequested?.Invoke(Playhead);
        e.Handled = true;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (_stepping)
        {
            _stepping = false;
            SeekRequested?.Invoke(Playhead);
        }
    }

    /// <summary>A fade's wedge between its two marks, the full height of the bar at its loud end
    /// and nothing at its quiet one, its top edge shaped by the curve: straight for linear, bowed
    /// for equal-power, an S for the S-curve. A picture to tell them apart, not the gain.</summary>
    private void ShadeFade(Graphics g, int? start, int? end, bool rising, FadeCurveKind curve)
    {
        if (start is not { } x0 || end is not { } x1 || x1 <= x0)
        {
            return;
        }
        var bottom = Height;
        const int Steps = 24;
        var outline = new List<System.Drawing.Point> { new(rising ? x1 : x0, bottom) };
        for (var i = 0; i <= Steps; i++)
        {
            var u = i / (double)Steps;
            var loud = Shape(curve, rising ? u : 1 - u);
            outline.Add(new System.Drawing.Point(x0 + (int)Math.Round((x1 - x0) * u), (int)Math.Round(bottom * (1 - loud))));
        }
        var wedge = outline.ToArray();
        using var shade = new SolidBrush(FadeShade);
        g.FillPolygon(shade, wedge);
    }

    /// <summary>How loud a fade-in draws at <paramref name="u"/> of its way across.</summary>
    private static double Shape(FadeCurveKind curve, double u) => curve switch
    {
        FadeCurveKind.Linear => u,
        FadeCurveKind.SCurve => Math.Sin((1 - Math.Cos(u * Math.PI)) / 2 * Math.PI / 2),
        _ => Math.Sin(u * Math.PI / 2),
    };

    /// <summary>The time scale along the groove: minor ticks hang below it, major ticks cross it
    /// with their m:ss label centred above.</summary>
    private void DrawScale(Graphics g, int mid)
    {
        var length = _length!.Value.Milliseconds;
        var travel = Width - 2 * Inset;
        if (length <= 0 || travel <= 0)
        {
            return;
        }
        var step = MinorTickSteps.FirstOrDefault(ms => travel * (double)ms / length >= MinTickGap);
        if (step == 0)
        {
            step = MajorTickMs;
        }
        var below = mid + TrackHeight / 2 + 1;
        var above = mid - TrackHeight / 2 - 2;
        var labelRight = int.MinValue;
        using var minor = new Pen(MinorTickColour);
        using var major = new Pen(MajorTickColour);
        for (var ms = 0; ms <= length; ms += step)
        {
            var x = XOf(Playhead.FromMilliseconds(ms));
            if (ms % MajorTickMs != 0)
            {
                g.DrawLine(minor, x, below, x, below + 2);
                continue;
            }
            g.DrawLine(major, x, above, x, below + 5);
            var label = Presenter.Fmt(TimeSpan.FromMilliseconds(ms));
            var size = TextRenderer.MeasureText(g, label, _labelFont, Size.Empty, TextFormatFlags.NoPadding);
            var left = Math.Max(0, Math.Min(Width - size.Width, x - size.Width / 2));
            if (left < labelRight + LabelGap)
            {
                continue;
            }
            TextRenderer.DrawText(g, label, _labelFont, new System.Drawing.Point(left, above - size.Height),
                LabelColour, TextFormatFlags.NoPadding);
            labelRight = left + size.Width;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        var mid = Height / 2;
        var track = new Rectangle(Inset, mid - TrackHeight / 2, Math.Max(0, Width - 2 * Inset), TrackHeight);
        var marks = Live ? Marks().ToDictionary(m => m.Mark, m => m.X) : null;
        if (marks is not null)
        {
            ShadeFade(g, marks[FadeMark.FadeInStart], marks[FadeMark.FadeInEnd], rising: true, _marks.FadeInCurve);
            ShadeFade(g, marks[FadeMark.FadeOutStart], marks[FadeMark.FadeOutEnd], rising: false, _marks.FadeOutCurve);
        }
        using (var groove = new SolidBrush(SystemColors.ControlDark))
        {
            g.FillRectangle(groove, track);
        }
        if (!Live)
        {
            return;
        }
        DrawScale(g, mid);
        var x = XOf(Playhead);
        using (var played = new SolidBrush(SystemColors.Highlight))
        {
            g.FillRectangle(played, track with { Width = x - Inset });
        }
        using (var mark = new Pen(MarkColour, 2))
        using (var handle = new SolidBrush(MarkColour))
        {
            foreach (var at in marks!.Values)
            {
                if (at is { } markX)
                {
                    // Below the middle band, so a press on the handle drags the mark.
                    var handleTop = Height - HandleSize;
                    g.DrawLine(mark, markX, 0, markX, handleTop);
                    g.FillRectangle(handle, markX - HandleSize / 2, handleTop, HandleSize, HandleSize);
                }
            }
        }
        var thumb = new Rectangle(x - Inset, mid - ThumbHeight / 2, 2 * Inset, ThumbHeight);
        using (var fill = new SolidBrush(SystemColors.Highlight))
        {
            g.FillRectangle(fill, thumb);
        }
        if (Focused && ShowFocusCues)
        {
            ControlPaint.DrawFocusRectangle(g, ClientRectangle);
        }
    }
}
