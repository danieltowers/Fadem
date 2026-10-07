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
    public void BetweenTheMarks_EachFadeIsShadedInTheFadeColour_AsAWedge_RisingIn_AndFallingOut()
    {
        using var bar = Bar();
        bar.Show(Playhead.FromMilliseconds(0), FiveSeconds,
            new FadeMarks(Faderio.Core.Domain.Point.FromSeconds(0.5), Faderio.Core.Domain.Point.FromSeconds(1.5),
                Faderio.Core.Domain.Point.FromSeconds(3.5), Faderio.Core.Domain.Point.FromSeconds(4.5)));

        using var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));

        bool ShadedAt(double seconds, int y)
        {
            var x = SeekBar.Inset + (int)Math.Round(seconds / 5 * (bar.Width - 2 * SeekBar.Inset));
            return picture.GetPixel(x, y).ToArgb() == bar.Palette.Fade.ToArgb();
        }
        const int top = 4;
        // The fade-in rises: low near its start, full height near its end.
        Assert.False(ShadedAt(0.6, top));
        Assert.True(ShadedAt(1.4, top));
        Assert.True(ShadedAt(0.6, bar.Height - 3));
        // The fade-out falls: full height near its start, low near its end.
        Assert.True(ShadedAt(3.6, top));
        Assert.False(ShadedAt(4.4, top));
        Assert.True(ShadedAt(4.4, bar.Height - 3));
        // Nothing between the fades.
        Assert.False(ShadedAt(2.5, top));
        Assert.False(ShadedAt(2.5, bar.Height - 3));
    }

    /// <summary>The bar drawn with nothing on it but the scale, and whether a pixel is marked.</summary>
    private static Func<double, int, bool> Scale(double seconds)
    {
        using var bar = new SeekBar { Width = 500, Height = 30 };
        var length = Length.FromFrames((long)(44100 * seconds), 44100);
        bar.Show(Playhead.Zero, length);
        var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));
        var ground = bar.Palette.Chrome.ToArgb();
        return (at, y) =>
        {
            var x = SeekBar.Inset + (int)Math.Round(at / seconds * (bar.Width - 2 * SeekBar.Inset));
            return picture.GetPixel(x, y).ToArgb() != ground;
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

    // ── Dragging the fades (phase 6 issue 11) ────────────────────────────────────────────

    /// <summary>Marks at 0.5, 1.5, 3.5 and 4.5 s of the 5 s track: a fade-in, then a fade-out.</summary>
    private static SeekBar Faded()
    {
        var bar = Bar();
        bar.Show(Playhead.FromMilliseconds(1000), FiveSeconds,
            new FadeMarks(Faderio.Core.Domain.Point.FromSeconds(0.5), Faderio.Core.Domain.Point.FromSeconds(1.5),
                Faderio.Core.Domain.Point.FromSeconds(3.5), Faderio.Core.Domain.Point.FromSeconds(4.5)));
        return bar;
    }

    [Fact]
    public void InAPalette_ThePlayedPartAndThumbAreTheAccent_TheWedgesTheFade_AndNoSystemColourIsDrawn()
    {
        var duophonic = Faderio.Shell.Theme.Palette.From(PaletteTests.Duophonic);
        using var bar = Faded(); // playing at 1 s, a fade-in from 0.5 to 1.5 s and out from 3.5 to 4.5
        bar.Palette = duophonic;
        using var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));
        int At(double seconds, int y) => picture.GetPixel(X(bar, seconds), y).ToArgb();
        const int groove = 15;

        Assert.Equal(duophonic.Accent.ToArgb(), At(0.25, groove)); // played
        Assert.Equal(duophonic.Accent.ToArgb(), At(1.0, groove - 7)); // the thumb, above the groove
        Assert.Equal(duophonic.Edge.ToArgb(), At(2.5, groove)); // still to play
        Assert.Equal(duophonic.Fade.ToArgb(), At(1.4, Top)); // inside the fade-in's wedge
        Assert.Equal(duophonic.Mark.ToArgb(), At(3.5, Top));
        Assert.Equal(duophonic.Chrome.ToArgb(), At(2.5, Top)); // the ground
        var stock = new[] { SystemColors.Highlight, SystemColors.ControlDark, SystemColors.Control }.Select(c => c.ToArgb()).ToHashSet();
        Assert.DoesNotContain(Enumerable.Range(0, picture.Width).SelectMany(x => Enumerable.Range(0, picture.Height).Select(y => picture.GetPixel(x, y).ToArgb())),
            stock.Contains);
    }

    private static int X(SeekBar bar, double seconds) =>
        SeekBar.Inset + (int)Math.Round(seconds / 5 * (bar.Width - 2 * SeekBar.Inset));

    /// <summary>Above the middle band (the groove and the thumb, 6–24 of 30) and below it.</summary>
    private const int Top = 3, Bottom = 27;

    private static MouseEventArgs At(int x, int y) => new(MouseButtons.Left, 1, x, y, 0);

    private sealed class Raised
    {
        public List<Playhead> Seeks { get; } = new();
        public List<(FadeMark Mark, Playhead To)> Marks { get; } = new();
        public List<(FadeSide Side, Playhead From, Playhead To)> Fades { get; } = new();

        public Raised(SeekBar bar)
        {
            bar.SeekRequested += Seeks.Add;
            bar.FadeMarkMoved += (mark, to) => Marks.Add((mark, to));
            bar.FadeMoved += (side, from, to) => Fades.Add((side, from, to));
        }

        public bool None => Seeks.Count == 0 && Marks.Count == 0 && Fades.Count == 0;
    }

    [Fact]
    public void EachMark_HangsFromAnEightPixelSquareHandle_AtTheFootOfTheBar()
    {
        var bar = Faded();
        using var picture = new Bitmap(bar.Width, bar.Height);
        bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));
        bool RedAt(int x, int y)
        {
            var pixel = picture.GetPixel(x, y);
            return pixel.R > 180 && pixel.G < 80 && pixel.B < 80;
        }
        foreach (var at in new[] { 0.5, 1.5, 3.5, 4.5 })
        {
            var x = X(bar, at);
            var handle = Enumerable.Range(x - 6, 13).Where(px => RedAt(px, bar.Height - 1)).ToList();
            Assert.Equal(8, handle.Count); // the handle's width, flush with the bottom
            Assert.Equal(8, Enumerable.Range(0, bar.Height).Count(y => RedAt(x + 3, y))); // and its height: square
            Assert.True(RedAt(x + 3, bar.Height - 8), $"the handle at {at} s does not reach up to the line");
            Assert.False(RedAt(x + 3, bar.Height - 9), $"the handle at {at} s is taller than 8 px");
            Assert.False(RedAt(x + 3, Top), $"the line at {at} s is as wide as the handle");
        }
    }

    [Fact]
    public void APressNearAMark_DragsIt_AndRaisesTheMarkOnRelease_WithTheDrop()
    {
        var bar = Faded();
        var raised = new Raised(bar);

        Raise(bar, "OnMouseDown", At(X(bar, 3.5) + 2, Top));
        Raise(bar, "OnMouseMove", At(X(bar, 3.0) + 2, Top));
        Assert.True(raised.None); // nothing before release

        Raise(bar, "OnMouseUp", At(X(bar, 3.0) + 2, Top));
        var (mark, to) = Assert.Single(raised.Marks);
        Assert.Equal(FadeMark.FadeOutStart, mark);
        Near(3000, to.Milliseconds); // where the mark was dropped, not where the mouse was
        Assert.Empty(raised.Seeks);
        Assert.Empty(raised.Fades);
        Assert.Equal(1000, bar.Playhead.Milliseconds); // the thumb never moved
    }

    [Fact]
    public void EachMark_IsFoundByItsOwnPlace()
    {
        var bar = Faded();
        var raised = new Raised(bar);
        foreach (var at in new[] { 0.5, 1.5, 3.5, 4.5 })
        {
            Raise(bar, "OnMouseDown", At(X(bar, at), Bottom));
            Raise(bar, "OnMouseUp", At(X(bar, at) - 10, Bottom));
        }
        Assert.Equal(new[] { FadeMark.FadeInStart, FadeMark.FadeInEnd, FadeMark.FadeOutStart, FadeMark.FadeOutEnd },
            raised.Marks.Select(m => m.Mark));
    }

    [Fact]
    public void TheFadeOut_StopsWhereTheFadeInEnds_AndTheFadeInWhereTheFadeOutStarts()
    {
        var bar = Faded(); // in 0.5–1.5 s, out 3.5–4.5 s
        var raised = new Raised(bar);

        Raise(bar, "OnMouseDown", At(X(bar, 3.5), Top));
        Raise(bar, "OnMouseUp", At(X(bar, 0.2), Top));
        Raise(bar, "OnMouseDown", At(X(bar, 4.0), Bottom));
        Raise(bar, "OnMouseUp", At(X(bar, 0.2), Bottom));
        Raise(bar, "OnMouseDown", At(X(bar, 1.5), Top));
        Raise(bar, "OnMouseUp", At(X(bar, 4.8), Top));

        Near(1500, raised.Marks[0].To.Milliseconds); // the fade-out's start, at the fade-in's end
        Near(4000 - 2000, raised.Fades[0].To.Milliseconds); // its wedge moved 2 s, as far as it could
        Near(3500, raised.Marks[1].To.Milliseconds); // the fade-in's end, at the fade-out's start
    }

    [Fact]
    public void APressInAWedge_OutsideTheBand_SlidesTheFade_WithGrabAndDrop()
    {
        var bar = Faded();
        var raised = new Raised(bar);

        Raise(bar, "OnMouseDown", At(X(bar, 4.0), Bottom));
        Raise(bar, "OnMouseMove", At(X(bar, 3.0), Bottom));
        Assert.True(raised.None);
        Raise(bar, "OnMouseUp", At(X(bar, 3.0), Bottom));

        var (side, from, to) = Assert.Single(raised.Fades);
        Assert.Equal(FadeSide.FadeOut, side);
        Near(4000, from.Milliseconds);
        Near(3000, to.Milliseconds);
        Assert.Empty(raised.Seeks);
        Assert.Empty(raised.Marks);
    }

    [Fact]
    public void APressInTheMiddleBand_Seeks_EvenOnAMarkOrAWedge()
    {
        var bar = Faded();
        var raised = new Raised(bar);

        Raise(bar, "OnMouseDown", At(X(bar, 3.5), 15));
        Raise(bar, "OnMouseUp", At(X(bar, 3.5), 15));
        Raise(bar, "OnMouseDown", At(X(bar, 1.0), 22));
        Raise(bar, "OnMouseUp", At(X(bar, 1.0), 22));

        Assert.Equal(2, raised.Seeks.Count);
        Near(3500, raised.Seeks[0].Milliseconds);
        Assert.Empty(raised.Marks);
        Assert.Empty(raised.Fades);
    }

    [Fact]
    public void AClickOnAMark_ThatDoesNotMove_RaisesNothing()
    {
        var bar = Faded();
        var raised = new Raised(bar);

        Raise(bar, "OnMouseDown", At(X(bar, 1.5), Top));
        Raise(bar, "OnMouseUp", At(X(bar, 1.5), Top));

        Assert.True(raised.None);
    }

    [Fact]
    public void TheCursor_IsSizeWEOverAMark_MoveOverAWedge_AndPlainElsewhere()
    {
        var bar = Faded();

        Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, X(bar, 4.5) - 3, Top, 0));
        Assert.Equal(Cursors.SizeWE, bar.Cursor);
        Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, X(bar, 1.0), Bottom, 0));
        Assert.Equal(Cursors.SizeAll, bar.Cursor);
        Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, X(bar, 4.5), 15, 0));
        Assert.Equal(Cursors.Default, bar.Cursor);
        Raise(bar, "OnMouseMove", new MouseEventArgs(MouseButtons.None, 0, X(bar, 2.5), Top, 0));
        Assert.Equal(Cursors.Default, bar.Cursor);
    }

    [Fact]
    public void WhileDragged_TheMarkIsDrawnUnderTheMouse_AndOnReleaseWhereTheViewPutsIt()
    {
        var bar = Faded();
        bool RedAt(double seconds)
        {
            using var picture = new Bitmap(bar.Width, bar.Height);
            bar.DrawToBitmap(picture, new Rectangle(0, 0, bar.Width, bar.Height));
            var pixel = picture.GetPixel(X(bar, seconds), Top);
            return pixel.R > 180 && pixel.G < 80 && pixel.B < 80;
        }

        Raise(bar, "OnMouseDown", At(X(bar, 4.5), Top));
        Raise(bar, "OnMouseMove", At(X(bar, 4.0), Top));
        Assert.True(RedAt(4.0));
        Assert.False(RedAt(4.5));

        Raise(bar, "OnMouseUp", At(X(bar, 4.0), Top));
        Assert.True(RedAt(4.5)); // forgotten: the view's marks until the next render says otherwise
        Assert.False(RedAt(4.0));
    }
}
