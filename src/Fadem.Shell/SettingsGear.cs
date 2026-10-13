using System.Drawing.Text;

namespace Fadem.Shell;

/// <summary>
/// Fadem's settings, behind a ⚙ in either host. In the plugin, a flat ⚙ at the far right of the
/// status strip, after everything else on it (round 2 issue 03); the strip's spring label gives
/// it the right end. In the standalone app, "Settings…" last in the ☰ menu, the ⚙ in the menu's
/// image margin (2026-10-13).
/// </summary>
public static class SettingsGear
{
    public const string Tip = "Fadem settings";

    /// <summary>The plugin's ⚙, which <paramref name="open"/> answers.</summary>
    public static ToolStripButton Create(Action open)
    {
        var gear = new ToolStripButton("⚙")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = new Font("Segoe UI Symbol", 11f),
            ToolTipText = Tip,
            AccessibleName = Tip,
            Margin = new Padding(6, 0, 2, 0),
        };
        gear.Click += (_, _) => open();
        return gear;
    }

    /// <summary>The standalone app's menu item, which <paramref name="open"/> answers, its ⚙
    /// drawn in <paramref name="ink"/>.</summary>
    public static ToolStripMenuItem MenuItem(Action open, Color ink)
    {
        var item = new ToolStripMenuItem("Settings…", Glyph(ink)) { AccessibleName = Tip };
        item.Click += (_, _) => open();
        return item;
    }

    /// <summary>The ⚙ at the margin's 16 px, as the menu's images are.</summary>
    private static Bitmap Glyph(Color ink)
    {
        var glyph = new Bitmap(16, 16);
        using var g = Graphics.FromImage(glyph);
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        using var font = new Font("Segoe UI Symbol", 10f);
        using var brush = new SolidBrush(ink);
        using var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("⚙", font, brush, new RectangleF(0, 0, 16, 16), centred);
        return glyph;
    }
}
