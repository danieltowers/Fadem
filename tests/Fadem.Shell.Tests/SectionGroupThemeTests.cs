using System.Runtime.InteropServices;
using Fadem.Core.Tests;
using Fadem.Shell.Theme;
using static Fadem.Shell.Tests.QueueSectionsTests;

namespace Fadem.Shell.Tests;

/// <summary>Studio theme issue 07: the queue's section headers in the palette, not Windows' blue,
/// on each host's runtime. Windows draws a header only on a shown list, so the window is shown off
/// screen and never activated.</summary>
public sealed class SectionGroupThemeTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    [Fact]
    public void ASectionsHeader_IsItsNameInText_WithItsSwatch_AndADimChevron() => RunSta(() => Headers((bitmap, headers, _) =>
    {
        var intro = Pixels(bitmap, headers[0]);
        Assert.Contains(intro, c => DialogThemeTests.Near(c, Duophonic.Text)); // the name
        Assert.Contains(intro, c => DialogThemeTests.Near(c, SectionPalette.ColourOf(2))); // its swatch
        Assert.Contains(intro, c => DialogThemeTests.Near(c, Duophonic.Dim)); // the chevron
        Assert.True(intro.Count(c => c.ToArgb() == Duophonic.Well.ToArgb()) > intro.Count / 2, "the header is not on the Well");
        Assert.Equal(Duophonic.Text.ToArgb(), Ink(intro, SectionPalette.ColourOf(2)).ToArgb()); // not Windows' blue
    }));

    [Fact]
    public void ACollapsedSectionsHeader_IsInThePaletteToo() => RunSta(() => Headers((bitmap, headers, _) =>
    {
        var foo = Pixels(bitmap, headers[1]);
        Assert.Contains(foo, c => DialogThemeTests.Near(c, SectionPalette.ColourOf(1)));
        Assert.Contains(foo, c => DialogThemeTests.Near(c, Duophonic.Dim)); // its chevron
        Assert.Equal(Duophonic.Text.ToArgb(), Ink(foo, SectionPalette.ColourOf(1)).ToArgb());
    }));

    /// <summary>A row in a section has a stripe of its colour at its left edge, its full height;
    /// a row in no section has none. The queue draws its own rows, so it draws the stripe too.</summary>
    [Fact]
    public void ARowInASection_HasAStripeOfItsColour_AtItsLeftEdge() => RunSta(() => Headers((bitmap, _, rows) =>
    {
        static List<Color> Edge(Bitmap bitmap, Rectangle row) => Pixels(bitmap, new Rectangle(row.Left, row.Top, 3, row.Height));
        Assert.All(Edge(bitmap, rows[1]), c => Assert.True(DialogThemeTests.Near(c, SectionPalette.ColourOf(2)), $"{c} is not Intro's green"));
        Assert.All(Edge(bitmap, rows[2]), c => Assert.True(DialogThemeTests.Near(c, SectionPalette.ColourOf(2)), $"{c} is not Intro's green"));
        Assert.DoesNotContain(Edge(bitmap, rows[0]), c => DialogThemeTests.Near(c, SectionPalette.ColourOf(2)));
        Assert.DoesNotContain(Pixels(bitmap, new Rectangle(rows[1].Left + 8, rows[1].Top, 20, rows[1].Height)),
            c => DialogThemeTests.Near(c, SectionPalette.ColourOf(2))); // a stripe, not a block
    }));

    /// <summary>The colour most of a header is inked in, off the Well (and its Edge rule) and off
    /// the swatch: Text for the palette's name, where Windows' theme inks it in its blue
    /// (<c>#1B3276</c> on Duophonic's Well). ClearType's fringes are too near that blue to look
    /// for it pixel by pixel; the name's stems outnumber them.</summary>
    private static Color Ink(List<Color> header, Color swatch) =>
        header.Where(c => !DialogThemeTests.Near(c, Duophonic.Well) && !DialogThemeTests.Near(c, swatch))
            .GroupBy(c => c.ToArgb()).OrderByDescending(g => g.Count()).First().First();

    private static List<Color> Pixels(Bitmap bitmap, Rectangle area) =>
        Enumerable.Range(area.Left, area.Width).SelectMany(x => Enumerable.Range(area.Top, area.Height).Select(y => bitmap.GetPixel(x, y))).ToList();

    /// <summary>A themed, owner-drawn queue of four rows with an unheaded row, "Intro" (green) over
    /// two rows and "foo" (red, collapsed) over the last, as the hosts make it, captured; hands
    /// <paramref name="check"/> the capture, the two headers' bounds in it and the three shown rows'.</summary>
    private static void Headers(Action<Bitmap, Rectangle[], Rectangle[]> check)
    {
        using var tc = TestEngine.Create();
        using var form = new QuietForm
        {
            Size = new Size(420, 320), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        var list = QueueOf(tc, 4);
        list.Dock = DockStyle.Fill;
        form.Controls.Add(list);
        QueueRowStyle.Draw(list, Duophonic);
        StudioTheme.Apply(form, Duophonic);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
        tc.Engine.StartSection(tc.View.Queue[3].Id, "foo");
        tc.Engine.RecolourSection(tc.View.Queue[1].Section!.Id, 2);
        tc.Engine.RecolourSection(tc.View.Queue[3].Section!.Id, 1);
        tc.Engine.CollapseSection(tc.View.Queue[3].Section!.Id, true);
        sections.Apply(tc.View);
        form.Show();
        Pump();

        _ = GetWindowRect(form.Handle, out var window);
        var origin = list.PointToScreen(Point.Empty);
        var headers = new[] { list.Groups[1], list.Groups[2] }.Select(g =>
        {
            var bounds = ListGroups.HeaderBounds(list, g);
            bounds.Offset(origin.X - window.Left, origin.Y - window.Top);
            return bounds;
        }).ToArray();
        Assert.All(headers, h => Assert.False(h.IsEmpty));
        var rows = Enumerable.Range(0, 3).Select(i =>
        {
            var bounds = list.Items[i].Bounds;
            bounds.Offset(origin.X - window.Left, origin.Y - window.Top);
            return bounds;
        }).ToArray();
        using var bitmap = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(bitmap))
        {
            var dc = g.GetHdc();
            _ = PrintWindow(form.Handle, dc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }
        form.Close();
        check(bitmap, headers, rows);
    }

    private static void Pump()
    {
        for (var i = 0; i < 15; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }

    private sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private const uint PW_RENDERFULLCONTENT = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
