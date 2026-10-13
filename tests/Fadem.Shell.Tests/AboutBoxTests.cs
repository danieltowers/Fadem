using System.Runtime.ExceptionServices;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.Shell.Tests;

/// <summary>About Fadem (2026-10-13), opened from the logo in either host: the splash's card with
/// the version, the copyright and the author's handle.</summary>
public sealed class AboutBoxTests
{
    [Fact]
    public void ItReadsTheVersion_TheCopyright_AndTheHandle_UnderTheLogo() => RunSta(() =>
    {
        using var about = new AboutBox(Palette.Studio, "1.2.3+4f2a9c1e0b");

        Assert.Equal(new[] { "Version 1.2.3", "© 2026 Daniel Towers", "@mrdanieltowers" }, about.Lines);
        Assert.Equal("https://x.com/mrdanieltowers", AboutBox.HandleUrl);
    });

    [Fact]
    public void WithNoVersion_TheHandleIsStillItsLastLine() => RunSta(() =>
    {
        using var about = new AboutBox(Palette.Studio, null);

        Assert.Equal(new[] { "© 2026 Daniel Towers", "@mrdanieltowers" }, about.Lines);
        Assert.True(about.HandleBounds.Width > 0);
    });

    [Fact]
    public void ItIsBorderless_OverItsOwner_AndOffTheTaskbar() => RunSta(() =>
    {
        using var about = new AboutBox(Palette.Studio, "1.2.3");

        Assert.Equal(FormBorderStyle.None, about.FormBorderStyle);
        Assert.Equal(FormStartPosition.CenterParent, about.StartPosition);
        Assert.False(about.ShowInTaskbar);
        Assert.Same(AppIcon.Shared, about.Icon);
        Assert.Equal("About Fadem", about.Text);
    });

    [Fact]
    public void ItDrawsTheLogo_ForThePalette_OnItsWell() => RunSta(() =>
    {
        var light = Palette.From(new SkinColours(
            unchecked((int)0xFFF0F0F0), unchecked((int)0xFFFFFFFF), 0, 0, unchecked((int)0xFF000000), 0));
        using var dark = new AboutBox(Palette.Studio, "1.2.3");
        using var onLight = new AboutBox(light, "1.2.3");

        Assert.True(dark.DarkArt);
        Assert.Equal(Palette.Studio.Well.ToArgb(), dark.BackColor.ToArgb());
        Assert.False(onLight.DarkArt);
    });

    [Fact]
    public void TheHandle_IsUnderTheLogo_InTheAccent() => RunSta(() =>
    {
        using var about = new AboutBox(Palette.Studio, "1.2.3");
        using var bitmap = new Bitmap(about.ClientSize.Width, about.ClientSize.Height);
        about.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));

        var handle = about.HandleBounds;
        Assert.True(handle.Top > about.ClientSize.Height / 2, "the handle is the last line, under the logo");
        Assert.True(handle.Bottom < about.ClientSize.Height);
        var accent = Palette.Studio.Accent;
        var inked = Enumerable.Range(handle.Left, handle.Width)
            .SelectMany(x => Enumerable.Range(handle.Top, handle.Height).Select(y => bitmap.GetPixel(x, y)))
            .Count(c => Math.Abs(c.R - accent.R) < 24 && Math.Abs(c.G - accent.G) < 24 && Math.Abs(c.B - accent.B) < 24);
        Assert.True(inked > 20, $"only {inked} accent pixels in the handle");
    });

    [Fact]
    public void TheLogo_IsAButton_WithAHandCursor() => RunSta(() =>
    {
        using var logo = new Logo(dark: true);

        Assert.Same(Cursors.Hand, logo.Cursor);
        Assert.Equal(AccessibleRole.PushButton, logo.AccessibleRole);
        Assert.Equal("About Fadem", logo.AccessibleName);
    });

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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
