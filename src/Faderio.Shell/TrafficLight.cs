using System.Drawing.Drawing2D;
using Faderio.Core.Playback;
using Faderio.Shell.Theme;

namespace Faderio.Shell;

/// <summary>
/// The editor's traffic light (phase 6 issue 07): which of the open adjacency's tracks is heard.
/// Green, amber and red keep their meaning in any skin; its off state, while stopped, and the
/// ring around it come from the palette (studio theme issue 05). Painted, not a glyph, so that
/// it can be as large as the row allows without the font's line height clipping it.
/// </summary>
internal sealed class TrafficLight : Control
{
    private AdjacencyHeard _heard;
    private Palette _palette = Palette.Studio;

    public TrafficLight()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        TabStop = false;
    }

    /// <summary>The light's diameter in pixels: three times the "●" glyph it was first drawn
    /// with, inside the heading's 28 px row, whose extra 2 px the curve row gave up.</summary>
    public const float Diameter = 27f;

    /// <summary>What the light shows, as the engine says.</summary>
    public AdjacencyHeard Heard
    {
        get => _heard;
        set
        {
            if (_heard != value)
            {
                _heard = value;
                Invalidate();
            }
        }
    }

    /// <summary>The colours its off state and ring are drawn in; set by the theme.</summary>
    public Palette Palette
    {
        get => _palette;
        set
        {
            _palette = value;
            Invalidate();
        }
    }

    /// <summary>The colour lit for <paramref name="heard"/>: green while the outgoing track is
    /// heard going into the incoming one, amber while only the incoming one is, red while another
    /// track is or the outgoing one goes into another (round 2 issue 08), the palette's Dim while
    /// stopped.</summary>
    public static Color Colour(AdjacencyHeard heard, Palette palette) => heard switch
    {
        AdjacencyHeard.Outgoing => Color.FromArgb(0x2E, 0xA0, 0x43),
        AdjacencyHeard.Incoming => Color.FromArgb(0xF0, 0xA0, 0x00),
        AdjacencyHeard.OtherTrack or AdjacencyHeard.OutgoingIntoAnother => Color.FromArgb(0xD0, 0x30, 0x30),
        _ => palette.Dim,
    };

    /// <summary>The colour it is lit in now.</summary>
    public Color Lit => Colour(_heard, _palette);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var ground = new SolidBrush(Parent?.BackColor ?? _palette.Chrome))
        {
            g.FillRectangle(ground, ClientRectangle);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var x = (Width - Diameter) / 2f;
        var y = (Height - Diameter) / 2f;
        using (var brush = new SolidBrush(Lit))
        {
            g.FillEllipse(brush, x, y, Diameter, Diameter);
        }
        // A bezel in the edge colour, the light set into the panel.
        using var ring = new Pen(_palette.Edge, 2f);
        g.DrawEllipse(ring, x + 1, y + 1, Diameter - 2, Diameter - 2);
    }
}
