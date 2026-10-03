using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The seek bar (interim transport issue 04). A click anywhere on the travel seeks there; a drag
/// moves the thumb with the mouse and seeks once, on release; ← and → step a second and seek on
/// key-up. It speaks <see cref="Playhead"/> and <see cref="Length"/> at its edge: pixels never
/// leave it. Disabled, with no thumb, while nothing is attended. Replaces a stock TrackBar,
/// whose channel click paged by its <c>LargeChange</c> instead of jumping. Red marks, the full
/// height of the bar, show where the track's fades begin and end, as the engine resolves them;
/// between each pair a grey wedge drawn as the volume goes: rising for the fade-in, falling for
/// the fade-out. A scale runs along the groove from 0: a short tick every half second below it,
/// and every ten seconds a longer one through it, labelled m:ss above. Where half seconds would
/// fall closer than <see cref="MinTickGap"/> the short ticks step a second instead, then five;
/// a label that would overlap the one before it is left out.
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
        _dragging = true;
        MoveThumb(PlayheadAt(e.X));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging)
        {
            MoveThumb(PlayheadAt(e.X));
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
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
    /// and nothing at its quiet one.</summary>
    private void ShadeFade(Graphics g, Faderio.Core.Domain.Point? start, Faderio.Core.Domain.Point? end, bool rising)
    {
        if (start is not { } from || end is not { } to)
        {
            return;
        }
        var (x0, x1) = (XOf(Playhead.At(from)), XOf(Playhead.At(to)));
        if (x1 <= x0)
        {
            return;
        }
        var bottom = Height;
        var wedge = rising
            ? new[] { new System.Drawing.Point(x0, bottom), new System.Drawing.Point(x1, 0), new System.Drawing.Point(x1, bottom) }
            : new[] { new System.Drawing.Point(x0, 0), new System.Drawing.Point(x0, bottom), new System.Drawing.Point(x1, bottom) };
        using var shade = new SolidBrush(FadeShade);
        g.FillPolygon(shade, wedge);
    }

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
        if (Live)
        {
            ShadeFade(g, _marks.FadeInStart, _marks.FadeInEnd, rising: true);
            ShadeFade(g, _marks.FadeOutStart, _marks.FadeOutEnd, rising: false);
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
        {
            foreach (var point in new[] { _marks.FadeInStart, _marks.FadeInEnd, _marks.FadeOutStart, _marks.FadeOutEnd })
            {
                if (point is { } p)
                {
                    var at = XOf(Playhead.At(p));
                    g.DrawLine(mark, at, 0, at, Height);
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
