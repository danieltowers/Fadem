namespace Fadem.Shell;

/// <summary>
/// The time readout in either host (studio theme issue 04): <see cref="Time"/>, "0:14 / -3:03",
/// is the LED readout, large in a monospaced face, which <see cref="Theme.StudioTheme"/> sets in
/// Text (the accent until round 2 issue 06); <see cref="Seconds"/>, the line under it, stays
/// small and is set in Dim. The time is drawn at <see cref="LedSize"/> where it fits, and smaller where it does not, as in the
/// plugin's sidebar or on an hour-long track, so it is never cut short.
/// </summary>
internal sealed class Readout : Label
{
    public const string LedFace = "Consolas";

    public const float LedSize = 15f;

    /// <summary>The smallest the time is drawn, however little room it has.</summary>
    private const float SmallestSize = 8f;

    /// <summary>The time's fonts by size, made as a narrower width or a longer time asks.</summary>
    private readonly Dictionary<float, Font> _sizes = new();

    /// <summary>Whether this is the LED readout, rather than the seconds under it.</summary>
    public bool Led { get; }

    private Readout(bool led)
    {
        Led = led;
        AutoSize = false;
        Dock = DockStyle.Fill;
        Margin = new Padding(0);
        UseMnemonic = false;
        if (led)
        {
            Font = SizedFont(LedSize);
            TextAlign = ContentAlignment.BottomLeft;
        }
        else
        {
            TextAlign = ContentAlignment.TopLeft;
        }
    }

    public static Readout Time() => new(led: true) { Text = "0:00 / -0:00" };

    public static Readout Seconds() => new(led: false) { Text = Presenter.SecondsReadout(null) };

    /// <summary>The font the time is drawn in now: the LED size, or smaller to fit the width.</summary>
    internal Font DrawnFont
    {
        get
        {
            var width = TextWidth(LedSize);
            if (width <= ClientSize.Width || width == 0)
            {
                return SizedFont(LedSize);
            }
            // From the proportional size, then down by half points, since hinted glyphs do not
            // scale exactly, to the smallest.
            var size = Math.Max(SmallestSize, (float)Math.Floor(LedSize * ClientSize.Width / width * 2) / 2);
            while (size > SmallestSize && TextWidth(size) > ClientSize.Width)
            {
                size -= 0.5f;
            }
            return SizedFont(size);
        }
    }

    private int TextWidth(float size) =>
        TextRenderer.MeasureText(Text, SizedFont(size), Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

    protected override void OnPaint(PaintEventArgs e)
    {
        if (!Led)
        {
            base.OnPaint(e);
            return;
        }
        TextRenderer.DrawText(e.Graphics, Text, DrawnFont, ClientRectangle, ForeColor,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
    }

    private Font SizedFont(float size)
    {
        if (!_sizes.TryGetValue(size, out var font))
        {
            font = new Font(LedFace, size);
            _sizes[size] = font;
        }
        return font;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            foreach (var font in _sizes.Values)
            {
                font.Dispose();
            }
            _sizes.Clear();
        }
    }
}
