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
