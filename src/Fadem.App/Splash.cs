using System.Diagnostics;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.App;

/// <summary>
/// The App's splash (2026-10-11), as MusicBee shows one while it loads: the logo on the palette's
/// well, white on a dark one and black on a light one, with the version below it. Shown once the
/// library is known, so never over the library chooser, and closed when the main window appears.
/// It stays up for <see cref="MinimumShown"/> at the least, so a quick launch shows it rather than
/// flashing it.
/// </summary>
internal sealed class Splash : Form
{
    /// <summary>The least time it is up for.</summary>
    public static readonly TimeSpan MinimumShown = TimeSpan.FromSeconds(1.2);

    /// <summary>Its size at 100 % display scaling, in pixels.</summary>
    private static readonly Size LogicalSize = new(520, 300);

    /// <summary>The logo's width at 100 % display scaling, in pixels.</summary>
    private const int LogicalArtWidth = 380;

    private readonly Bitmap _art;
    private readonly Font _versionFont;
    private readonly string _version;
    private readonly Color _edge;
    private readonly Color _dim;
    private readonly Stopwatch _up = new();
    private Bitmap? _scaled;

    /// <param name="informationalVersion">The App's informational version, as the build stamps it.</param>
    public Splash(Palette palette, string? informationalVersion)
    {
        DarkArt = palette.IsDark;
        _art = Logo.TrimmedArt(DarkArt);
        _version = VersionLine(informationalVersion);
        _edge = palette.Edge;
        _dim = palette.Dim;
        _versionFont = new Font("Segoe UI", 9f);

        Text = "Fadem";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        BackColor = palette.Well;
        ClientSize = new Size(Scaled(LogicalSize.Width), Scaled(LogicalSize.Height));
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
    }

    /// <summary>Whether it draws the white logo, for a dark palette.</summary>
    public bool DarkArt { get; }

    /// <summary>"Version 1.2.3" from the build's informational version, without the commit the
    /// build appends after a '+'; nothing when there is none.</summary>
    public static string VersionLine(string? informationalVersion)
    {
        if (string.IsNullOrEmpty(informationalVersion))
        {
            return "";
        }
        var plus = informationalVersion.IndexOf('+');
        return "Version " + (plus < 0 ? informationalVersion : informationalVersion.Substring(0, plus));
    }

    /// <summary>Shows it and paints it at once: the UI thread goes on loading, with no message
    /// loop to paint it until the main window runs one.</summary>
    public void ShowNow()
    {
        Show();
        Update();
        _up.Start();
    }

    /// <summary>Waits out what is left of <see cref="MinimumShown"/>, just before the main window
    /// shows.</summary>
    public void WaitOutMinimum()
    {
        var left = MinimumShown - _up.Elapsed;
        if (left > TimeSpan.Zero)
        {
            Thread.Sleep(left);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);

        var width = Math.Min(Scaled(LogicalArtWidth), ClientSize.Width - Scaled(40));
        var size = new Size(width, (int)Math.Round(width * (double)_art.Height / _art.Width));
        if (_scaled is null || _scaled.Size != size)
        {
            _scaled?.Dispose();
            _scaled = Logo.Scale(_art, size);
        }
        var lineHeight = _version.Length == 0 ? 0 : TextRenderer.MeasureText(g, _version, _versionFont).Height;
        var gap = _version.Length == 0 ? 0 : Scaled(16);
        // The logo and the version line, as one block, centred.
        var top = (ClientSize.Height - size.Height - gap - lineHeight) / 2;
        g.DrawImageUnscaled(_scaled, (ClientSize.Width - size.Width) / 2, top);
        if (_version.Length > 0)
        {
            var line = new Rectangle(0, top + size.Height + gap, ClientSize.Width, lineHeight);
            TextRenderer.DrawText(g, _version, _versionFont, line, _dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
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
            _versionFont.Dispose();
        }
    }

    private int Scaled(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96.0);
}
