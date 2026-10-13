using Fadem.App;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.App.Tests;

/// <summary>The standalone App's splash (2026-10-11), shown while it loads, as MusicBee's is: the
/// logo on the palette's well, white on a dark one and black on a light one, and the version.</summary>
public sealed class SplashTests
{
    [Fact]
    public void TheSplash_IsBorderless_Centred_AndOffTheTaskbar() => RunSta(() =>
    {
        using var splash = new Splash(Palette.Studio, "1.2.3");

        Assert.Equal(FormBorderStyle.None, splash.FormBorderStyle);
        Assert.Equal(FormStartPosition.CenterScreen, splash.StartPosition);
        Assert.False(splash.ShowInTaskbar);
        Assert.Same(AppIcon.Shared, splash.Icon);
    });

    [Fact]
    public void OnADarkPalette_TheSplashDrawsTheWhiteLogo_OnItsWell() => RunSta(() =>
    {
        using var splash = new Splash(Palette.Studio, "1.2.3");

        Assert.True(splash.DarkArt);
        Assert.Equal(Palette.Studio.Well.ToArgb(), splash.BackColor.ToArgb());
    });

    [Fact]
    public void OnALightPalette_TheSplashDrawsTheBlackLogo() => RunSta(() =>
    {
        var light = Palette.From(new SkinColours(
            unchecked((int)0xFFF0F0F0), unchecked((int)0xFFFFFFFF), 0, 0, unchecked((int)0xFF000000), 0));
        using var splash = new Splash(light, "1.2.3");

        Assert.False(splash.DarkArt);
    });

    [Fact]
    public void TheSplash_PaintsTheLogo_NotJustItsBackground() => RunSta(() =>
    {
        using var splash = new Splash(Palette.Studio, "1.2.3");
        using var bitmap = new Bitmap(splash.ClientSize.Width, splash.ClientSize.Height);

        splash.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        var colours = Enumerable.Range(0, bitmap.Width).Select(x => bitmap.GetPixel(x, bitmap.Height / 2).ToArgb()).Distinct();
        Assert.True(colours.Count() > 10, "the middle row is all background");
    });

    [Fact]
    public void UnderTheVersion_TheSplashNamesTheCopyright_AsTheAboutBoxDoes() => RunSta(() =>
    {
        using var splash = new Splash(Palette.Studio, "1.2.3");

        Assert.Equal(new[] { "Version 1.2.3", "© 2026 Daniel Towers" }, splash.Lines);
        Assert.Equal(AboutBox.Copyright, splash.Lines.Last());
    });

    [Fact]
    public void WithNoVersion_TheSplashStillNamesTheCopyright() => RunSta(() =>
    {
        using var splash = new Splash(Palette.Studio, null);

        Assert.Equal(new[] { AboutBox.Copyright }, splash.Lines);
    });

    [Theory]
    [InlineData("1.2.3", "Version 1.2.3")]
    [InlineData("1.2.3+4f2a9c1e0b", "Version 1.2.3")]
    [InlineData("1.2.3-beta.1+4f2a9c1e0b", "Version 1.2.3-beta.1")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void TheVersionLine_DropsTheBuildsCommit(string? informational, string expected)
    {
        Assert.Equal(expected, Splash.VersionLine(informational));
    }

    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
