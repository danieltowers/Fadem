using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace Faderio.Shell;

/// <summary>
/// The product's logo, at the top right of either host (round 2 issue 05): white on a dark
/// palette, black on a light one. The art is embedded in the shell, so neither host ships a file
/// for it. Scaled down once to the height it is drawn at, with high-quality interpolation, and
/// drawn pixel for pixel, so it is crisp at any display scaling and never stretched. The art's
/// transparent margin is trimmed first, so the height is all logo, <see cref="LogicalHeight"/>
/// scaled for the display, or less where its parent is shorter. Docked at a side, it takes the
/// art's width as its parent lays out, before the docking, so a name beside it gets the rest.
/// </summary>
internal sealed class Logo : Control
{
    /// <summary>The logo's height at 100 % display scaling, in pixels: three times the top line's
    /// 22, as the user asked (2026-10-10), so the hosts give it a column of its own.</summary>
    public const int LogicalHeight = 66;

    /// <summary>The room kept above and below it, and at its right.</summary>
    private const int Inset = 2;

    private readonly Bitmap _art;
    private Bitmap? _scaled;
    private Control? _parent;

    public Logo(bool dark)
    {
        using (var art = Art(dark))
        {
            _art = Trimmed(art);
        }
        IsDark = dark;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        TabStop = false;
        AccessibleName = "Fadem";
        AccessibleRole = AccessibleRole.Graphic;
        FitWidth();
    }

    /// <summary>Whether this is the white art, for a dark palette.</summary>
    public bool IsDark { get; }

    /// <summary>The size the art is drawn at, in pixels: <see cref="LogicalHeight"/> scaled for
    /// the display, or less where the control is shorter.</summary>
    public Size ArtSize
    {
        get
        {
            var height = Math.Max(1, Math.Min(Scaled(LogicalHeight), ClientSize.Height - 2 * Inset));
            return new Size(WidthAt(height), height);
        }
    }

    /// <summary>The height a parent gives it for the art at its full height, insets included.</summary>
    public int Room => Scaled(LogicalHeight) + 2 * Inset;

    /// <summary>The embedded art for a dark palette (white) or a light one (black).</summary>
    internal static Bitmap Art(bool dark)
    {
        var name = dark ? "Faderio.Shell.logo-white.png" : "Faderio.Shell.logo-black.png";
        using var stream = typeof(Logo).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The logo {name} is not embedded.");
        // A copy, so the image does not hold the stream it was read from.
        using var read = Image.FromStream(stream);
        return new Bitmap(read);
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        if (_parent is not null)
        {
            _parent.Layout -= ParentLaying;
        }
        _parent = Parent;
        if (_parent is not null)
        {
            _parent.Layout += ParentLaying;
        }
        FitWidth();
    }

    protected override void OnDockChanged(EventArgs e)
    {
        base.OnDockChanged(e);
        FitWidth();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Invalidate();
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        FitWidth();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.Clear(BackColor);
        var size = ArtSize;
        if (_scaled is null || _scaled.Size != size)
        {
            _scaled?.Dispose();
            _scaled = Scale(_art, size);
        }
        var y = (ClientSize.Height - size.Height) / 2;
        e.Graphics.DrawImageUnscaled(_scaled, ClientSize.Width - Inset - size.Width, y);
    }

    protected override void Dispose(bool disposing)
    {
        // The base first: it leaves the parent, which fits the width to the art once more.
        base.Dispose(disposing);
        if (disposing)
        {
            if (_parent is not null)
            {
                _parent.Layout -= ParentLaying;
            }
            _scaled?.Dispose();
            _art.Dispose();
        }
    }

    /// <summary>The parent's Layout event comes before its docking, so the width is right when the
    /// docking places this.</summary>
    private void ParentLaying(object? sender, LayoutEventArgs e) => FitWidth();

    /// <summary>Wide enough for the art at the height it will be drawn: docked at a side, the
    /// parent's; otherwise its full height.</summary>
    private void FitWidth()
    {
        var room = Parent is { } p && Dock is DockStyle.Left or DockStyle.Right ? p.DisplayRectangle.Height : int.MaxValue;
        var width = WidthAt(Math.Max(1, Math.Min(Scaled(LogicalHeight), room - 2 * Inset))) + 2 * Inset;
        if (Width != width)
        {
            Width = width;
        }
    }

    private int Scaled(int pixels) => (int)Math.Round(pixels * DeviceDpi / 96.0);

    private int WidthAt(int height) => (int)Math.Round(height * (double)_art.Width / _art.Height);

    /// <summary>The art without its transparent margin.</summary>
    private static Bitmap Trimmed(Bitmap art)
    {
        int left = art.Width, top = art.Height, right = -1, bottom = -1;
        var data = art.LockBits(new Rectangle(Point.Empty, art.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            var row = new int[art.Width];
            for (var y = 0; y < art.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, art.Width);
                for (var x = 0; x < art.Width; x++)
                {
                    if ((row[x] >>> 24) > 8)
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
        }
        finally
        {
            art.UnlockBits(data);
        }
        return right < 0 ? new Bitmap(art) : art.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), PixelFormat.Format32bppArgb);
    }

    private static Bitmap Scale(Image art, Size size)
    {
        var scaled = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(scaled);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.CompositingQuality = CompositingQuality.HighQuality;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var wrap = new ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY); // no grey fringe at the edges
        g.DrawImage(art, new Rectangle(Point.Empty, size), 0, 0, art.Width, art.Height, GraphicsUnit.Pixel, wrap);
        return scaled;
    }
}
