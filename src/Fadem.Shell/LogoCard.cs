using Fadem.Shell.Theme;

namespace Fadem.Shell;

/// <summary>
/// A borderless card with the logo on the palette's well, white on a dark one and black on a
/// light one, and a few lines of text centred under it, edged in the palette's edge: the App's
/// splash (2026-10-11) and the About box both hosts open from the logo (2026-10-13).
/// </summary>
internal class LogoCard : Form
{
    /// <summary>The logo's width at 100 % display scaling, in pixels.</summary>
    private const int LogicalArtWidth = 380;

    private readonly Bitmap _art;
    private readonly Font _font;
    private readonly IReadOnlyList<(string Text, Color Colour)> _lines;
    private readonly Rectangle[] _lineBounds;
    private readonly Color _edge;
    private Bitmap? _scaled;

    /// <param name="logicalSize">Its size at 100 % display scaling, in pixels.</param>
    /// <param name="lines">The lines under the logo, top to bottom; an empty one is left out.</param>
    protected LogoCard(Palette palette, Size logicalSize, IEnumerable<(string Text, Color Colour)> lines)
    {
        DarkArt = palette.IsDark;
        _art = Logo.TrimmedArt(DarkArt);
        _lines = lines.Where(l => l.Text.Length > 0).ToList();
        _lineBounds = new Rectangle[_lines.Count];
        _edge = palette.Edge;
        _font = new Font("Segoe UI", 9f);

        Text = "Fadem";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        BackColor = palette.Well;
        ClientSize = new Size(Scaled(logicalSize.Width), Scaled(logicalSize.Height));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    /// <summary>Whether it draws the white logo, for a dark palette.</summary>
    public bool DarkArt { get; }

    /// <summary>The lines under the logo, top to bottom.</summary>
    public IEnumerable<string> Lines => _lines.Select(l => l.Text);

    /// <summary>"Version 1.2.3" from a build's informational version, without the commit the
    /// build appends after a '+'; nothing when there is none.</summary>
    public static string VersionLine(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        {
            return "";
        }
        var plus = informationalVersion!.IndexOf('+');
        return "Version " + (plus < 0 ? informationalVersion : informationalVersion.Substring(0, plus));
    }

    /// <summary>Where the line at <paramref name="index"/> is drawn, as wide as its text.</summary>
    protected Rectangle LineBounds(int index)
    {
        PlaceLines();
        return _lineBounds[index];
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        var top = PlaceLines();
        g.DrawImageUnscaled(_scaled!, (ClientSize.Width - _scaled!.Width) / 2, top);
        for (var i = 0; i < _lines.Count; i++)
        {
            TextRenderer.DrawText(g, _lines[i].Text, _font, _lineBounds[i], _lines[i].Colour,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.NoPadding);
        }

        using var edge = new Pen(_edge);
        g.DrawRectangle(edge, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _scaled?.Dispose();
            _art.Dispose();
            _font.Dispose();
        }
    }

    protected int Scaled(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96.0);

    /// <summary>Scales the art to the card and places the lines under it, the logo and the lines
    /// centred as one block; the logo's top.</summary>
    private int PlaceLines()
    {
        var width = Math.Min(Scaled(LogicalArtWidth), ClientSize.Width - Scaled(40));
        var size = new Size(width, (int)Math.Round(width * (double)_art.Height / _art.Width));
        if (_scaled is null || _scaled.Size != size)
        {
            _scaled?.Dispose();
            _scaled = Logo.Scale(_art, size);
        }
        var measured = _lines.Select(l => TextRenderer.MeasureText(l.Text, _font, Size.Empty, TextFormatFlags.NoPadding)).ToList();
        var gap = _lines.Count == 0 ? 0 : Scaled(16);
        var spacing = Scaled(4);
        var linesHeight = measured.Sum(m => m.Height) + spacing * Math.Max(0, _lines.Count - 1);
        var top = (ClientSize.Height - size.Height - gap - linesHeight) / 2;
        var y = top + size.Height + gap;
        for (var i = 0; i < _lines.Count; i++)
        {
            _lineBounds[i] = new Rectangle((ClientSize.Width - measured[i].Width) / 2, y, measured[i].Width, measured[i].Height);
            y += measured[i].Height + spacing;
        }
        return top;
    }
}
