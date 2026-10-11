using System.Drawing.Drawing2D;

namespace Fadem.Shell.Theme;

/// <summary>
/// Menus and strips in a <see cref="Palette"/> (studio theme issue 06): the ground in Chrome, the
/// item under the pointer on Raised, separators and the menu's border in Edge, items in Text and
/// disabled ones in Dim, a tick and a submenu's arrow drawn in the palette rather than the system's
/// black glyphs. <see cref="StudioTheme"/> gives one to every strip and menu it themes; a submenu
/// takes its owner's.
/// </summary>
internal sealed class StudioRenderer : ToolStripProfessionalRenderer
{
    public StudioRenderer(Palette palette) : base(new Colours(palette))
    {
        Palette = palette;
        RoundedEdges = false;
    }

    public Palette Palette { get; }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        if (!e.Item.Enabled)
        {
            e.TextColor = Palette.Dim;
        }
        else if (e.ToolStrip is ToolStripDropDown)
        {
            e.TextColor = Palette.Text;
        }
        // Elsewhere, as on the status strip, the item's own colour: Dim, a link's accent.
        TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, e.TextColor, e.TextFormat);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled == false ? Palette.Dim : Palette.Text;
        base.OnRenderArrow(e);
    }

    /// <summary>A tick in the accent; an item with an image of its own, a section's swatch, is
    /// framed in it instead.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var g = e.Graphics;
        var box = Rectangle.Inflate(e.ImageRectangle, 2, 2);
        var mark = e.Item.Enabled ? Palette.Accent : Palette.Dim;
        using (var back = new SolidBrush(Palette.Raised))
        {
            g.FillRectangle(back, box);
        }
        if (e.Item is ToolStripMenuItem { Image: not null })
        {
            using var frame = new Pen(mark);
            g.DrawRectangle(frame, box.X, box.Y, box.Width - 1, box.Height - 1);
            if (e.Image is { } image)
            {
                g.DrawImage(image, e.ImageRectangle);
            }
            return;
        }
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var r = e.ImageRectangle;
        using (var pen = new Pen(mark, 2f))
        {
            g.DrawLines(pen, new[]
            {
                new PointF(r.Left + r.Width * 0.18f, r.Top + r.Height * 0.52f),
                new PointF(r.Left + r.Width * 0.42f, r.Top + r.Height * 0.76f),
                new PointF(r.Left + r.Width * 0.84f, r.Top + r.Height * 0.26f),
            });
        }
        g.SmoothingMode = smoothing;
    }

    /// <summary>The professional renderer's colours, every one a role.</summary>
    private sealed class Colours : ProfessionalColorTable
    {
        private readonly Palette _p;

        public Colours(Palette palette)
        {
            _p = palette;
            UseSystemColors = false;
        }

        public override Color ToolStripDropDownBackground => _p.Chrome;
        public override Color ImageMarginGradientBegin => _p.Chrome;
        public override Color ImageMarginGradientMiddle => _p.Chrome;
        public override Color ImageMarginGradientEnd => _p.Chrome;
        public override Color MenuBorder => _p.Edge;
        public override Color MenuItemBorder => _p.Raised;
        public override Color MenuItemSelected => _p.Raised;
        public override Color MenuItemSelectedGradientBegin => _p.Raised;
        public override Color MenuItemSelectedGradientEnd => _p.Raised;
        public override Color MenuItemPressedGradientBegin => _p.Raised;
        public override Color MenuItemPressedGradientMiddle => _p.Raised;
        public override Color MenuItemPressedGradientEnd => _p.Raised;
        public override Color MenuStripGradientBegin => _p.Chrome;
        public override Color MenuStripGradientEnd => _p.Chrome;
        public override Color SeparatorDark => _p.Edge;
        public override Color SeparatorLight => _p.Chrome;
        public override Color CheckBackground => _p.Raised;
        public override Color CheckSelectedBackground => _p.Raised;
        public override Color CheckPressedBackground => _p.Raised;
        public override Color ButtonSelectedBorder => _p.Raised;
        public override Color ButtonSelectedHighlight => _p.Raised;
        public override Color ButtonSelectedGradientBegin => _p.Raised;
        public override Color ButtonSelectedGradientMiddle => _p.Raised;
        public override Color ButtonSelectedGradientEnd => _p.Raised;
        public override Color ButtonPressedGradientBegin => _p.Raised;
        public override Color ButtonPressedGradientMiddle => _p.Raised;
        public override Color ButtonPressedGradientEnd => _p.Raised;
        public override Color StatusStripGradientBegin => _p.Chrome;
        public override Color StatusStripGradientEnd => _p.Chrome;
        public override Color ToolStripBorder => _p.Edge;
        public override Color ToolStripGradientBegin => _p.Chrome;
        public override Color ToolStripGradientMiddle => _p.Chrome;
        public override Color ToolStripGradientEnd => _p.Chrome;
        public override Color GripDark => _p.Dim;
        public override Color GripLight => _p.Edge;
    }
}
