using System.Text.Json;
using Fadem.Core.Persistence;

namespace Fadem.Shell.Theme;

/// <summary>
/// What a skin says, before any rule: each as MusicBee answers <c>Setting_GetSkinElementColour</c>,
/// an ARGB value, or 0 where the skin does not answer (studio theme issue 01). The plugin reads
/// them; a host with no skin has none. <paramref name="Bars"/> is the other colour MusicBee may draw
/// its bars in (light-skin fix, 2026-10-08). No accent: Fadem's is its own blue on every skin
/// (round 2 issue 07).
/// </summary>
internal sealed record SkinColours(int Chrome, int Well, int Edge, int Key, int Text, int Dim, int Bars = 0)
{
    /// <summary>No skin at all: every role falls back.</summary>
    public static SkinColours None { get; } = new(0, 0, 0, 0, 0, 0);
}

/// <summary>
/// The colours the studio look is drawn in (studio theme spec, "The colours"): ten roles, all but
/// the accent from the skin. Made from a skin's answers by <see cref="From"/>, whose rules keep any
/// skin usable: a role the skin leaves out falls back to the one it derives from, text without
/// contrast goes to white or black, and the accent, Windows' selection blue on every skin (round 2
/// issue 07), is pulled toward the text on a well it does not read on. <see cref="Studio"/> is the built-in one,
/// for a host with no skin. Kept as <c>palette.json</c> in the App's machine folder, where the
/// plugin writes what it last read so the App on the same machine looks alike.
/// </summary>
internal sealed record Palette(
    Color Chrome, Color Well, Color Edge, Color Key, Color Text, Color Dim,
    Color Raised, Color Accent, Color Fade, Color Mark)
{
    public const string FileName = "palette.json";

    /// <summary>The accent: Windows' default selection blue, which the seek bar drew in before
    /// the studio theme. Fixed, so it never follows Windows' settings or the skin.</summary>
    private static readonly Color Blue = Color.FromArgb(0x00, 0x78, 0xD7);
    private static readonly Color Red = Color.FromArgb(0xE0, 0x45, 0x3A);

    /// <summary>The built-in palette: the prototype's studio console in graphite.</summary>
    public static Palette Studio { get; } = From(new SkinColours(
        Argb(0x2A2D33), Argb(0x18191D), Argb(0x0A0A0C), Argb(0x3A3F47), Argb(0xD8DCE2), Argb(0x7C8490)));

    /// <summary>Whether the wells are dark: the native dark scroll bars and title bar follow it.</summary>
    public bool IsDark => Luminance(Well) < 0.18;

    /// <summary>The palette a skin's answers give.</summary>
    public static Palette From(SkinColours skin)
    {
        if (skin.Chrome == 0 && skin.Well == 0 && skin.Text == 0)
        {
            // Nothing to go on: no skin, or one that answers nothing.
            return Studio;
        }
        var chrome = Answered(skin.Chrome) ?? Answered(skin.Well) ?? Color.FromArgb(0x2A, 0x2D, 0x33);
        var dark = Luminance(chrome) < 0.18;
        // MusicBee draws the strip above the panel in one of two colours, by skin: on every skin
        // measured it is the lighter of them on a light skin and the darker on a dark one.
        if (Answered(skin.Bars) is { } bars && (dark ? Luminance(bars) < Luminance(chrome) : Luminance(bars) > Luminance(chrome)))
        {
            chrome = bars;
        }
        var well = Answered(skin.Well) ?? Shade(chrome, dark ? -0.15 : 0.04);
        var edge = Answered(skin.Edge) ?? Shade(chrome, -0.45);
        var key = Answered(skin.Key) ?? Shade(chrome, dark ? 0.08 : -0.06);
        var text = Answered(skin.Text) ?? (dark ? Color.White : Color.Black);
        if (Contrast(text, well) < 4.5)
        {
            text = Contrast(Color.White, well) >= Contrast(Color.Black, well) ? Color.White : Color.Black;
        }
        var dim = Answered(skin.Dim) ?? Mix(text, well, 0.5);
        if (Contrast(dim, well) < 2)
        {
            dim = Mix(text, well, 0.5);
        }
        var accent = Blue;
        for (var i = 0; i < 10 && Contrast(accent, well) < 3; i++)
        {
            accent = Mix(text, accent, 0.2); // a fifth of the way toward the text
        }
        var raised = Shade(key, dark ? 0.08 : -0.08);
        var fade = Mix(accent, chrome, 0.3);
        // The marks stand apart from the accent, which draws the played part beside them.
        var mark = HueDistance(accent, Red) < 30 ? text : Red;
        return new Palette(chrome, well, edge, key, text, dim, raised, accent, fade, mark);
    }

    /// <summary>The standalone App's palette: the one the plugin last wrote to this machine's
    /// folder, or the built-in one; the built-in one too for a run that keeps no machine folder.</summary>
    public static Palette ForMachine(string? machineFolder) =>
        machineFolder is not null && Load(Path.Combine(machineFolder, FileName)) is { } written ? written : Studio;

    /// <summary>The palette in <paramref name="path"/>, or null when there is none to use.</summary>
    public static Palette? Load(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path));
            if (file is null)
            {
                return null;
            }
            Color Role(string name) => file.TryGetValue(name, out var hex) && Parse(hex) is { } c ? c : throw new FormatException(name);
            return new Palette(Role("chrome"), Role("well"), Role("edge"), Role("key"), Role("text"), Role("dim"),
                Role("raised"), Role("accent"), Role("fade"), Role("mark"));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or FormatException)
        {
            return null;
        }
    }

    /// <summary>Writes the palette atomically. A write that fails is dropped: the App keeps the
    /// last good one, or the built-in.</summary>
    public void Save(string path)
    {
        var file = new Dictionary<string, string>
        {
            ["chrome"] = Hex(Chrome), ["well"] = Hex(Well), ["edge"] = Hex(Edge), ["key"] = Hex(Key), ["text"] = Hex(Text),
            ["dim"] = Hex(Dim), ["raised"] = Hex(Raised), ["accent"] = Hex(Accent), ["fade"] = Hex(Fade), ["mark"] = Hex(Mark),
        };
        try
        {
            AtomicJson.Write(path, file, new JsonSerializerOptions { WriteIndented = true });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Whether two palettes draw alike: their roles, not where they came from.</summary>
    public bool Same(Palette other) =>
        Chrome.ToArgb() == other.Chrome.ToArgb() && Well.ToArgb() == other.Well.ToArgb() && Edge.ToArgb() == other.Edge.ToArgb()
        && Key.ToArgb() == other.Key.ToArgb() && Text.ToArgb() == other.Text.ToArgb() && Dim.ToArgb() == other.Dim.ToArgb()
        && Raised.ToArgb() == other.Raised.ToArgb() && Accent.ToArgb() == other.Accent.ToArgb()
        && Fade.ToArgb() == other.Fade.ToArgb() && Mark.ToArgb() == other.Mark.ToArgb();

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color? Parse(string hex) =>
        hex.Length == 7 && hex[0] == '#' && int.TryParse(hex.Substring(1), System.Globalization.NumberStyles.HexNumber, null, out var rgb)
            ? FromRgb(rgb)
            : null;

    private static int Argb(int rgb) => unchecked((int)0xFF000000) | rgb;

    private static Color FromRgb(int rgb) => Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF);

    /// <summary>An answer, opaque; or null where the skin gave none (MusicBee answers 0).</summary>
    private static Color? Answered(int argb) => argb == 0 ? null : FromRgb(argb & 0xFFFFFF);

    /// <summary>Toward white by <paramref name="by"/> when positive, toward black when negative.</summary>
    private static Color Shade(Color c, double by) => by >= 0 ? Mix(Color.White, c, by) : Mix(Color.Black, c, -by);

    /// <summary><paramref name="a"/> at <paramref name="amount"/> over <paramref name="b"/>.</summary>
    private static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
        (int)Math.Round(b.R + (a.R - b.R) * amount), (int)Math.Round(b.G + (a.G - b.G) * amount), (int)Math.Round(b.B + (a.B - b.B) * amount));

    /// <summary>WCAG relative luminance.</summary>
    private static double Luminance(Color c)
    {
        static double Channel(int v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    /// <summary>WCAG contrast ratio, 1 to 21.</summary>
    private static double Contrast(Color a, Color b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static double HueDistance(Color a, Color b)
    {
        var d = Math.Abs(a.GetHue() - b.GetHue());
        return Math.Min(d, 360 - d);
    }
}
