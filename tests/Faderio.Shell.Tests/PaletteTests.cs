using System.Drawing;
using Faderio.Core.Tests;
using Faderio.Shell.Theme;

namespace Faderio.Shell.Tests;

/// <summary>
/// Studio theme issue 01: the palette a skin's answers give. The Duophonic answers are those the
/// user's MusicBee gave a probe on 2026-10-06; MusicBee answers 0 where the skin has no colour.
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
        Chrome: Argb(0x232323), Well: Argb(0x1C1C1C), Edge: Argb(0x121212), Key: Argb(0x2C2C2C),
        Text: Argb(0xE1E1E1), Dim: Argb(0x6D6D6D), Accent: Argb(0xCD5C5C));

    private static string Hex(Color c) => Palette.Hex(c);

    [Fact]
    public void Duophonic_GivesItsOwnColours_WithItsRedAsTheAccent()
    {
        var p = Palette.From(Duophonic);

        Assert.Equal(
            ["#232323", "#1C1C1C", "#121212", "#2C2C2C", "#E1E1E1", "#6D6D6D", "#CD5C5C"],
            new[] { p.Chrome, p.Well, p.Edge, p.Key, p.Text, p.Dim, p.Accent }.Select(Hex));
        Assert.True(p.IsDark);
        Assert.Equal("#3D3D3D", Hex(p.Raised)); // the key, 8% toward white
        Assert.Equal(Hex(p.Text), Hex(p.Mark)); // a red accent: the marks stand apart in the text colour
    }

    [Fact]
    public void AGreyAccent_IsAmber()
    {
        var p = Palette.From(Duophonic with { Accent = Argb(0x9A9A9A) });

        Assert.Equal("#FFB000", Hex(p.Accent));
        Assert.Equal("#E0453A", Hex(p.Mark));
    }

    [Fact]
    public void ASkinThatAnswersNothing_IsTheBuiltInStudioPalette()
    {
        var p = Palette.From(SkinColours.None);

        Assert.True(p.Same(Palette.Studio));
        Assert.Equal("#2A2D33", Hex(p.Chrome));
        Assert.Equal("#FFB000", Hex(p.Accent));
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
        var light = Palette.From(new SkinColours(Argb(0xF0F0F0), Argb(0xFFFFFF), Argb(0xC8C8C8), Argb(0xE6E6E6), Argb(0xDDDDDD), Argb(0x888888), Argb(0x1F77B4)));
        Assert.Equal("#000000", Hex(light.Text));
    }

    [Fact]
    public void ALightSkin_GivesALightPalette_WhoseAccentStillReads()
    {
        var p = Palette.From(new SkinColours(Argb(0xF0F0F0), Argb(0xFFFFFF), Argb(0xC8C8C8), Argb(0xE6E6E6), Argb(0x202020), Argb(0x808080), Argb(0xFFD54F)));

        Assert.False(p.IsDark);
        Assert.True(p.Raised.GetBrightness() < p.Key.GetBrightness()); // raised is darker on a light skin
        Assert.True(p.Accent.GetBrightness() < 0.5f); // a pale yellow is pulled toward the text until it reads on white
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

        Assert.Equal("#CD5C5C", Hex(Palette.ForMachine(_dir).Accent));
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
