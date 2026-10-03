using System.Drawing;
using System.Reflection;
using Faderio.Shell;
using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Shell.Tests;

/// <summary>Interim transport issue 04: the seek bar's own behaviour, off any form. Mouse and key
/// events are raised through the control's own handlers, as a user's would be.</summary>
public sealed class SeekBarTests
{
    private static readonly Length FiveSeconds = Length.FromFrames(44100 * 5, 44100);

    private static SeekBar Bar(int width = 500)
    {
        var bar = new SeekBar { Width = width, Height = 30 };
        bar.Show(Playhead.FromMilliseconds(1000), FiveSeconds);
        return bar;
    }

    private static void Raise(Control control, string handler, EventArgs args) =>
        typeof(Control).GetMethod(handler, BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, new object[] { args });

    /// <summary>Within 20 ms: a pixel of a 500 px bar is about 10 ms of a 5 s track.</summary>
    private static void Near(int expected, int actual) => Assert.InRange(actual, expected - 20, expected + 20);

    private static MouseEventArgs At(int x) => new(MouseButtons.Left, 1, x, 15, 0);

    [Fact]
    public void TheFadeMarks_AreDrawnInRed_WhereTheyFallOnTheTravel()
    {
        using var bar = Bar();
        bar.Show(Playhead.FromMilliseconds(1000), FiveSeconds,
            new FadeMarks(Faderio.Core.Domain.Point.FromSeconds(0.5), Faderio.Core.Domain.Point.FromSeconds(1.5), Faderio.Core.Domain.Point.FromSeconds(3.5), Faderio.Core.Domain.Point.FromSeconds(4.5)));

        using var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));

        bool RedAt(double seconds)
        {
            var x = SeekBar.Inset + (int)Math.Round(seconds / 5 * (bar.Width - 2 * SeekBar.Inset));
            var pixel = picture.GetPixel(x, 3); // above the groove and the thumb
            return pixel.R > 180 && pixel.G < 80 && pixel.B < 80;
        }
        Assert.True(RedAt(0.5));
        Assert.True(RedAt(1.5));
        Assert.True(RedAt(3.5));
        Assert.True(RedAt(4.5));
        Assert.False(RedAt(2.5));
    }

    [Fact]
    public void BetweenTheMarks_EachFadeIsShadedGrey_AsAWedge_RisingIn_AndFallingOut()
    {
        using var bar = Bar();
        bar.Show(Playhead.FromMilliseconds(0), FiveSeconds,
            new FadeMarks(Faderio.Core.Domain.Point.FromSeconds(0.5), Faderio.Core.Domain.Point.FromSeconds(1.5),
                Faderio.Core.Domain.Point.FromSeconds(3.5), Faderio.Core.Domain.Point.FromSeconds(4.5)));

        using var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));

        bool GreyAt(double seconds, int y)
        {
            var x = SeekBar.Inset + (int)Math.Round(seconds / 5 * (bar.Width - 2 * SeekBar.Inset));
            var pixel = picture.GetPixel(x, y);
            return pixel.R == pixel.G && pixel.G == pixel.B && pixel.R < 225 && pixel.R > 150;
        }
        const int top = 4;
        // The fade-in rises: low near its start, full height near its end.
        Assert.False(GreyAt(0.6, top));
        Assert.True(GreyAt(1.4, top));
        Assert.True(GreyAt(0.6, bar.Height - 3));
        // The fade-out falls: full height near its start, low near its end.
        Assert.True(GreyAt(3.6, top));
        Assert.False(GreyAt(4.4, top));
        Assert.True(GreyAt(4.4, bar.Height - 3));
        // Nothing between the fades.
        Assert.False(GreyAt(2.5, top));
        Assert.False(GreyAt(2.5, bar.Height - 3));
    }

    /// <summary>The bar drawn with nothing on it but the scale, and whether a pixel is marked.</summary>
    private static Func<double, int, bool> Scale(double seconds)
    {
        using var bar = new SeekBar { Width = 500, Height = 30 };
        var length = Length.FromFrames((long)(44100 * seconds), 44100);
        bar.Show(Playhead.Zero, length);
        var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));
        return (at, y) =>
        {
            var x = SeekBar.Inset + (int)Math.Round(at / seconds * (bar.Width - 2 * SeekBar.Inset));
            return picture.GetPixel(x, y).R < 225; // the background is 240
        };
    }

    [Fact]
    public void TheScale_TicksEveryHalfSecond_BelowTheGroove_AndEveryTenThroughIt()
    {
        var marked = Scale(20);
        const int minorBand = 19, majorOnly = 22, aboveGroove = 12; // the groove is 13–16
        Assert.True(marked(5, minorBand));
        Assert.True(marked(5.5, minorBand));
        Assert.False(marked(5.25, minorBand));
        Assert.False(marked(5, majorOnly));
        Assert.False(marked(5, aboveGroove));
        Assert.True(marked(10, majorOnly));
        Assert.True(marked(10, aboveGroove));
        Assert.True(marked(20, majorOnly));
    }

    [Fact]
    public void EachTenSeconds_IsLabelled_AboveTheGroove()
    {
        var marked = Scale(20);
        bool Labelled(double at) => Enumerable.Range(0, 10).Any(y =>
            Enumerable.Range(-6, 13).Any(dx => marked(at + dx * 20.0 / 488, y)));
        Assert.True(Labelled(10));
        Assert.False(Labelled(5));
    }

    [Fact]
    public void OnALongerTrack_TheShortTicksStepASecond()
    {
        var marked = Scale(120); // half a second is two pixels, a second four
        Assert.True(marked(61, 19));
        Assert.True(marked(62, 19));
        Assert.False(marked(61.5, 19));
    }

    [Fact]
    public void OnALongTrack_TheShortTicksStepFiveSeconds()
    {
        var marked = Scale(600); // a second is under a pixel, five seconds four
        Assert.True(marked(305, 19));
        Assert.False(marked(305, 22));
        Assert.False(marked(302, 19));
        Assert.True(marked(310, 22));
    }

    [Fact]
    public void OnAVeryLongTrack_ShortTicksTooCloseToSee_AreLeftOut()
    {
        var marked = Scale(1500); // five seconds is under two pixels; ten seconds is three
        Assert.True(marked(750, 22));
        Assert.True(marked(760, 22));
        Assert.False(marked(752.5, 19));
    }

    [Fact]
    public void PixelsMapToPlayheads_AcrossTheTravel()
    {
        var bar = Bar();
        Assert.Equal(0, bar.PlayheadAt(SeekBar.Inset).Milliseconds);
        Assert.Equal(0, bar.PlayheadAt(-40).Milliseconds);
        Assert.Equal(5000, bar.PlayheadAt(bar.Width - SeekBar.Inset).Milliseconds);
        Assert.Equal(5000, bar.PlayheadAt(bar.Width + 40).Milliseconds);
        Near(2500, bar.PlayheadAt(bar.Width / 2).Milliseconds);
    }

    [Fact]
    public void AClick_SeeksOnce_ToWhereItLanded()
    {
        var bar = Bar();
        var seeks = new List<Playhead>();
        bar.SeekRequested += seeks.Add;

        var threeQuarters = SeekBar.Inset + (bar.Width - 2 * SeekBar.Inset) * 3 / 4;
        Raise(bar, "OnMouseDown", At(threeQuarters));
        Raise(bar, "OnMouseUp", At(threeQuarters));

        var only = Assert.Single(seeks);
        Near(3750, only.Milliseconds);
    }

    [Fact]
    public void ADrag_MovesTheThumb_AndSeeksOnlyOnRelease()
    {
        var bar = Bar();
        var seeks = new List<Playhead>();
        bar.SeekRequested += seeks.Add;

        Raise(bar, "OnMouseDown", At(bar.Width / 4));
        Raise(bar, "OnMouseMove", At(bar.Width / 2));
        bar.Show(Playhead.FromMilliseconds(1100), FiveSeconds); // the tick while dragging
        Assert.Empty(seeks);
        Near(2500, bar.Playhead.Milliseconds); // the thumb follows the mouse, not the tick

        Raise(bar, "OnMouseUp", At(bar.Width / 2));
        Near(2500, Assert.Single(seeks).Milliseconds);

        bar.Show(Playhead.FromMilliseconds(2600), FiveSeconds);
        Assert.Equal(2600, bar.Playhead.Milliseconds); // released: it follows the view again
    }

    [Fact]
    public void ArrowKeys_Step1s_AndSeekOnKeyUp()
    {
        var bar = Bar();
        var seeks = new List<Playhead>();
        bar.SeekRequested += seeks.Add;

        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Right));
        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Right)); // held: repeats
        Assert.Empty(seeks);
        Raise(bar, "OnKeyUp", new KeyEventArgs(Keys.Right));
        Assert.Equal(3000, Assert.Single(seeks).Milliseconds);

        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Left));
        Raise(bar, "OnKeyUp", new KeyEventArgs(Keys.Left));
        Assert.Equal(0, seeks[seeks.Count - 1].Milliseconds); // clamped at the start
    }

    [Fact]
    public void WithNothingAttended_ItIsDisabled_AndNeverSeeks()
    {
        var bar = new SeekBar { Width = 500, Height = 30 };
        var seeks = new List<Playhead>();
        bar.SeekRequested += seeks.Add;
        bar.Show(Playhead.Zero, null);

        Assert.False(bar.Enabled);
        Raise(bar, "OnMouseDown", At(250));
        Raise(bar, "OnMouseUp", At(250));
        Raise(bar, "OnKeyDown", new KeyEventArgs(Keys.Right));
        Raise(bar, "OnKeyUp", new KeyEventArgs(Keys.Right));
        Assert.Empty(seeks);

        bar.Show(Playhead.Zero, FiveSeconds);
        Assert.True(bar.Enabled);
    }
}
