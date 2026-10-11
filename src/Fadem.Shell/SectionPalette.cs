using Fadem.Core.Playback;

namespace Fadem.Shell;

/// <summary>
/// How a section's colour, an index into the engine's palette of
/// <see cref="SectionView.Colours"/>, is drawn and named, the same in both hosts (phase 6 issue
/// 09): distinct at a glance, and dark enough to read against a white row.
/// </summary>
internal static class SectionPalette
{
    private static readonly (Color Colour, string Name)[] Entries =
    [
        (Color.FromArgb(0x1F, 0x77, 0xB4), "Blue"),
        (Color.FromArgb(0xD6, 0x27, 0x28), "Red"),
        (Color.FromArgb(0x2C, 0xA0, 0x2C), "Green"),
        (Color.FromArgb(0xFF, 0x7F, 0x0E), "Orange"),
        (Color.FromArgb(0x94, 0x67, 0xBD), "Purple"),
        (Color.FromArgb(0x17, 0xBE, 0xCF), "Teal"),
        (Color.FromArgb(0xE3, 0x77, 0xC2), "Pink"),
        (Color.FromArgb(0x7F, 0x7F, 0x7F), "Grey"),
    ];

    /// <summary>The colour a section's palette index is drawn in.</summary>
    public static Color ColourOf(int colour) => Entries[colour].Colour;

    /// <summary>The colour's name, for the menu.</summary>
    public static string NameOf(int colour) => Entries[colour].Name;

    /// <summary>A block of <paramref name="colour"/> the size given: a row's stripe, a header's
    /// or a menu's swatch.</summary>
    public static Bitmap Swatch(int colour, Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using var graphics = Graphics.FromImage(bitmap);
        using var brush = new SolidBrush(ColourOf(colour));
        graphics.FillRectangle(brush, 0, 0, size.Width, size.Height);
        return bitmap;
    }

    /// <summary>Every colour's swatch at <paramref name="size"/>, in the palette's order.</summary>
    public static ImageList Images(Size size)
    {
        var images = new ImageList { ImageSize = size, ColorDepth = ColorDepth.Depth24Bit };
        for (var colour = 0; colour < SectionView.Colours; colour++)
        {
            images.Images.Add(Swatch(colour, size));
        }
        return images;
    }
}
