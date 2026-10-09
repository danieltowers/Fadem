using System.Runtime.ExceptionServices;
using Faderio.Shell;

namespace Faderio.Shell.Tests;

/// <summary>Round 2 issue 05: the logo at the top right, white on a dark palette and black on a
/// light one, embedded in the shell, scaled down once and never stretched.</summary>
public sealed class LogoTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BothArts_AreEmbedded_AtTheirFullSize(bool dark)
    {
        using var art = Logo.Art(dark);

        Assert.Equal(new Size(1323, 534), art.Size);
    }

    /// <summary>The wordmark, at the art's right, is white for a dark palette and black for a
    /// light one; the mark at its left is in colour in both.</summary>
    [Fact]
    public void TheDarkPalettesWordmark_IsWhite_AndTheLightOnesIsBlack()
    {
        Assert.All(Wordmark(true), c => Assert.True(c.GetBrightness() > 0.95f, $"{c}"));
        Assert.All(Wordmark(false), c => Assert.True(c.GetBrightness() < 0.05f, $"{c}"));
    }

    /// <summary>Three times the top line's 22 px (2026-10-10), in a parent with room for it.</summary>
    [Fact]
    public void WithRoom_ItIsDrawnSixtySixPixelsTall_AtTheArtsProportions() => RunSta(() =>
    {
        using var logo = new Logo(dark: true) { Dock = DockStyle.Right };
        using var column = new Panel { Size = new Size(600, logo.Room) };
        column.Controls.Add(logo);

        Assert.Equal(logo.Room - 4, logo.ArtSize.Height); // 66 at 100 %
        Assert.InRange((double)logo.ArtSize.Width / logo.ArtSize.Height, 2.5, 3.0); // the art's margin trimmed
        Assert.Equal(logo.ArtSize.Width + 4, logo.Width);
        Assert.Equal(column.Width, logo.Right);
    });

    [Fact]
    public void OnATwentySixPixelLine_ItShrinksToTwentyTwo_AndTheNameEndsBeforeIt() => RunSta(() =>
    {
        using var line = new Panel { Size = new Size(300, 26) };
        using var name = new Label { Dock = DockStyle.Fill, Text = "A track", AutoEllipsis = true };
        using var logo = new Logo(dark: true) { Dock = DockStyle.Right };
        line.Controls.Add(name);
        line.Controls.Add(logo); // docked after the name, so rightmost

        Assert.Equal(22, logo.ArtSize.Height);
        Assert.Equal(logo.ArtSize.Width + 4, logo.Width);
        Assert.Equal(line.Width, logo.Right);
        Assert.Equal(logo.Left, name.Right); // the name is cut short before it
    });

    [Fact]
    public void InAShortParent_ItShrinksToFit_AndItsSlotWithIt() => RunSta(() =>
    {
        using var line = new Panel { Size = new Size(300, 16) };
        using var logo = new Logo(dark: false) { Dock = DockStyle.Right };
        line.Controls.Add(logo);

        Assert.Equal(12, logo.ArtSize.Height);
        Assert.InRange((double)logo.ArtSize.Width / logo.ArtSize.Height, 2.5, 3.0);
        Assert.Equal(logo.ArtSize.Width + 4, logo.Width); // no wider than its art
        Assert.Equal(line.Width, logo.Right);
    });

    [Fact]
    public void ItDrawsTheArt_OnItsGround() => RunSta(() =>
    {
        using var logo = new Logo(dark: true) { Size = new Size(10, 26), BackColor = Color.Black };
        using var bitmap = new Bitmap(logo.Width, logo.Height);
        logo.DrawToBitmap(bitmap, new Rectangle(Point.Empty, logo.Size));

        var pixels = Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))).ToList();
        Assert.Contains(pixels, c => c.R > 0xE0 && c.G > 0xE0 && c.B > 0xE0); // the white art
        Assert.Equal(Color.Black.ToArgb(), bitmap.GetPixel(0, 0).ToArgb()); // the ground at its corner
    });

    /// <summary>The opaque pixels of the wordmark, the art's right half.</summary>
    private static List<Color> Wordmark(bool dark)
    {
        using var art = Logo.Art(dark);
        var pixels = new List<Color>();
        for (var y = 0; y < art.Height; y += 2)
        {
            for (var x = 560; x < art.Width; x += 2)
            {
                var c = art.GetPixel(x, y);
                if (c.A == 255)
                {
                    pixels.Add(c);
                }
            }
        }
        Assert.NotEmpty(pixels);
        return pixels;
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
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
