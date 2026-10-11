using System.Drawing;
using Fadem.Core.Tests;
using Fadem.Shell.Theme;

namespace Fadem.Shell.Tests;

/// <summary>
/// Studio theme issue 01: the palette a skin's answers give. The Duophonic answers are those the
/// user's MusicBee gave a probe on 2026-10-06, as the plugin maps them: its bars' colour as the
/// chrome, the border as the wells, and no edge of its own. MusicBee answers 0 where the skin has
/// no colour. The skins' accents are not among the answers: Fadem's accent is its own blue
/// (round 2 issue 07).
/// </summary>
public sealed class PaletteTests : IDisposable
{
    private readonly string _dir = Path.Combine(TestTemp.Root, "palette", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static int Argb(int rgb) => unchecked((int)0xFF000000) | rgb;

    internal static readonly SkinColours Duophonic = new(
        Chrome: Argb(0x1C1C1C), Well: Argb(0x121212), Edge: 0, Key: Argb(0x2C2C2C),
        Text: Argb(0xE1E1E1), Dim: Argb(0x6D6D6D), Bars: Argb(0x202020));

    /// <summary>Two light skins, as the portable MusicBee answered a probe on 2026-10-08. MusicBee
    /// draws the strip above the panel in <c>#EBEBEB</c> on CalciumMETRO and in white on
    /// MusicBee3; MusicBee3 gives no text colour.</summary>
    internal static readonly SkinColours CalciumMetro = new(
        Chrome: Argb(0xBEBEBE), Well: Argb(0xD7D7D7), Edge: 0, Key: Argb(0xFFFFFF),
        Text: Argb(0x787878), Dim: Argb(0x8C8C8C), Bars: Argb(0xEBEBEB));

    internal static readonly SkinColours MusicBee3 = new(
        Chrome: Argb(0xFFFFFF), Well: Argb(0xDFDFDF), Edge: 0, Key: Argb(0xFFFFFF),
        Text: 0, Dim: Argb(0x969696), Bars: Argb(0xE6E6E6));

    private static string Hex(Color c) => Palette.Hex(c);

    [Fact]
    public void Duophonic_GivesItsOwnColours_WithTheBlueAsTheAccent()
    {
        var p = Palette.From(Duophonic);

        Assert.Equal(
            ["#1C1C1C", "#121212", "#0F0F0F", "#2C2C2C", "#E1E1E1", "#6D6D6D", "#0078D7"],
            new[] { p.Chrome, p.Well, p.Edge, p.Key, p.Text, p.Dim, p.Accent }.Select(Hex));
        Assert.True(p.IsDark);
        Assert.Equal("#3D3D3D", Hex(p.Raised)); // the key, 8% toward white
        Assert.Equal("#E0453A", Hex(p.Mark)); // red: the blue is nowhere near it
    }

    /// <summary>Round 2 issue 07: the accent is Windows' selection blue whatever the skin, dark or
    /// light, with no deeper variant on a light one; the three skins' wells all read it.</summary>
    [Fact]
    public void TheAccent_IsTheBlue_OnEverySkin()
    {
        Assert.All(new[] { Duophonic, CalciumMetro, MusicBee3, SkinColours.None },
            skin => Assert.Equal("#0078D7", Hex(Palette.From(skin).Accent)));
        Assert.Equal("#0078D7", Hex(Palette.Studio.Accent));
    }

    /// <summary>On a well the blue does not read on (under 3:1), it is pulled a fifth at a time
    /// toward the text until it does.</summary>
    [Fact]
    public void OnAWellTheBlueDoesNotReadOn_ItIsPulledTowardTheText()
    {
        var p = Palette.From(Duophonic with { Well = Argb(0x0060B0), Text = Argb(0xFFFFFF) });

        Assert.NotEqual("#0078D7", Hex(p.Accent));
        Assert.True(p.Accent.GetBrightness() > Color.FromArgb(0x00, 0x78, 0xD7).GetBrightness()); // toward the white text
    }

    /// <summary>Light skins: their grey text is pushed to black on their wells.</summary>
    [Fact]
    public void ALightSkinsText_IsPushedToBlackOnItsWell()
    {
        var calcium = Palette.From(CalciumMetro);
        var musicBee3 = Palette.From(MusicBee3);

        Assert.False(calcium.IsDark);
        Assert.Equal("#000000", Hex(calcium.Text)); // its grey text, pushed to black on its well
        Assert.Equal("#000000", Hex(musicBee3.Text)); // none given
    }

    /// <summary>The chrome meets the strip MusicBee draws above the panel. That strip is element 2's
    /// colour on some skins and element 0's (state 1) on others; on every skin measured it is the
    /// lighter of the two on a light skin and the darker on a dark one.</summary>
    [Fact]
    public void TheChrome_IsTheLighterBarOnALightSkin_AndTheDarkerOnADarkOne()
    {
        Assert.Equal("#EBEBEB", Hex(Palette.From(CalciumMetro).Chrome));
        Assert.Equal("#FFFFFF", Hex(Palette.From(MusicBee3).Chrome));
        Assert.Equal("#1C1C1C", Hex(Palette.From(Duophonic).Chrome));
    }

    [Fact]
    public void ASkinThatAnswersNothing_IsTheBuiltInStudioPalette()
    {
        var p = Palette.From(SkinColours.None);

        Assert.True(p.Same(Palette.Studio));
        Assert.Equal("#2A2D33", Hex(p.Chrome));
        Assert.Equal("#0078D7", Hex(p.Accent));
    }

    [Fact]
    public void RolesTheSkinLeavesOut_FallBackToThoseTheyDeriveFrom()
    {
        var p = Palette.From(Duophonic with { Well = 0, Edge = 0, Key = 0, Dim = 0 });

        Assert.True(p.IsDark);
        Assert.NotEqual(Hex(p.Chrome), Hex(p.Well)); // a well, darker than the chrome
        Assert.True(p.Well.GetBrightness() < p.Chrome.GetBrightness());
        Assert.True(p.Edge.GetBrightness() < p.Chrome.GetBrightness());
        Assert.True(p.Key.GetBrightness() > p.Chrome.GetBrightness());
        Assert.NotEqual("#000000", Hex(p.Dim));
    }

    [Fact]
    public void TextWithoutContrast_IsPushedToWhiteOrBlack()
    {
        Assert.Equal("#FFFFFF", Hex(Palette.From(Duophonic with { Text = Argb(0x333333) }).Text));
        var light = Palette.From(new SkinColours(Argb(0xF0F0F0), Argb(0xFFFFFF), Argb(0xC8C8C8), Argb(0xE6E6E6), Argb(0xDDDDDD), Argb(0x888888)));
        Assert.Equal("#000000", Hex(light.Text));
    }

    [Fact]
    public void ALightSkin_GivesALightPalette()
    {
        var p = Palette.From(new SkinColours(Argb(0xF0F0F0), Argb(0xFFFFFF), Argb(0xC8C8C8), Argb(0xE6E6E6), Argb(0x202020), Argb(0x808080)));

        Assert.False(p.IsDark);
        Assert.True(p.Raised.GetBrightness() < p.Key.GetBrightness()); // raised is darker on a light skin
        Assert.Equal("#0078D7", Hex(p.Accent)); // the same blue, which reads on white
    }

    [Fact]
    public void APalette_RoundTripsThroughItsFile()
    {
        var path = Path.Combine(_dir, Palette.FileName);
        var p = Palette.From(Duophonic);

        p.Save(path);

        Assert.True(Palette.Load(path)!.Same(p));
    }

    [Fact]
    public void TheApp_UsesThePaletteThePluginLastWrote_ElseTheBuiltIn()
    {
        Assert.Same(Palette.Studio, Palette.ForMachine(null));
        Assert.Same(Palette.Studio, Palette.ForMachine(_dir)); // nothing written yet

        Palette.From(Duophonic).Save(Path.Combine(_dir, Palette.FileName));

        Assert.Equal("#1C1C1C", Hex(Palette.ForMachine(_dir).Chrome));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not json")]
    [InlineData("{\"chrome\": \"#232323\"}")]
    [InlineData("{\"chrome\":\"red\",\"well\":\"#1C1C1C\",\"edge\":\"#121212\",\"key\":\"#2C2C2C\",\"text\":\"#E1E1E1\",\"dim\":\"#6D6D6D\",\"raised\":\"#3D3D3D\",\"accent\":\"#CD5C5C\",\"fade\":\"#5C3434\",\"mark\":\"#E1E1E1\"}")]
    public void AFileThatIsMissingOrBroken_LoadsAsNone(string? contents)
    {
        var path = Path.Combine(_dir, Palette.FileName);
        if (contents is not null)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, contents);
        }

        Assert.Null(Palette.Load(path));
    }
}
