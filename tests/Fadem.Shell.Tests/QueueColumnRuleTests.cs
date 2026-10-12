using System.Runtime.InteropServices;
using Fadem.Core.Tests;
using Fadem.Shell.Theme;
using static Fadem.Shell.Tests.QueueSectionsTests;

namespace Fadem.Shell.Tests;

/// <summary>Round 2 issue 04: no rules at the columns' edges behind the queue, below its last row
/// or in the gaps between its rows, in sections and not, on a dark skin and a light one, on each
/// host's runtime. Windows' list themes draw them as the list erases its ground
/// (<c>LVP_COLUMNDETAIL</c>); the list's own erase paints it plain. The window is shown off screen
/// and never activated.</summary>
public sealed class QueueColumnRuleTests
{
    public static TheoryData<string, bool> Cases => new()
    {
        { "Duophonic", false }, { "Duophonic", true }, { "CalciumMetro", false }, { "CalciumMetro", true },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void BelowTheLastRow_TheGroundIsPlain(string skin, bool grouped) => RunSta(() => Capture(skin, grouped, (bitmap, p, list) =>
    {
        var below = Rectangle.FromLTRB(list.Client.Left + 2, list.Rows.Bottom + 2, list.Client.Right - 2, list.Client.Bottom - 2);
        AssertPlain(bitmap, below, p.Well);
    }));

    /// <summary>At each column's edge, down the rows: the cells' own ground, and the list's in the
    /// gaps between them, a pixel between rows in a section; without sections the rows meet.
    /// Section headers draw their own rule.</summary>
    [Theory]
    [InlineData("Duophonic")]
    [InlineData("CalciumMetro")]
    public void BetweenTheRowsOfSections_NoColumnRuleShows(string skin) => RunSta(() => Capture(skin, grouped: true, (bitmap, p, list) =>
    {
        foreach (var x in list.Edges)
        {
            for (var y = list.Rows.Top; y < list.Rows.Bottom; y++)
            {
                if (!list.Headers.Any(h => y >= h.Top && y < h.Bottom))
                {
                    AssertPlain(bitmap, new Rectangle(x - 1, y, 3, 1), p.Well);
                }
            }
        }
    }));

    private static void AssertPlain(Bitmap bitmap, Rectangle area, Color ground)
    {
        for (var x = area.Left; x < area.Right; x++)
        {
            for (var y = area.Top; y < area.Bottom; y++)
            {
                var c = bitmap.GetPixel(x, y);
                Assert.True(c.ToArgb() == ground.ToArgb(), $"({x}, {y}) is {c}, not the ground {ground}");
            }
        }
    }

    /// <summary>Where the list is in the capture: its client area, its rows from the first's top to
    /// the last's foot, its section headers, and its columns' right edges.</summary>
    private sealed class Geometry(Rectangle client, Rectangle rows, Rectangle[] headers, int[] edges)
    {
        public Rectangle Client => client;
        public Rectangle Rows => rows;
        public Rectangle[] Headers => headers;
        public int[] Edges => edges;
    }

    /// <summary>A themed, owner-drawn queue of four rows, columns as <c>QueueColumns</c> builds them,
    /// with "Intro" over the middle two and "foo" over the last or no sections, captured; hands
    /// <paramref name="check"/> the capture, the palette and where the list is in it.</summary>
    private static void Capture(string skin, bool grouped, Action<Bitmap, Palette, Geometry> check)
    {
        var p = Palette.From(skin switch
        {
            "Duophonic" => PaletteTests.Duophonic,
            "CalciumMetro" => PaletteTests.CalciumMetro,
            _ => throw new ArgumentException($"no skin {skin}", nameof(skin)),
        });
        using var tc = TestEngine.Create();
        using var form = new QuietForm
        {
            Size = new Size(460, 360), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        // Its columns, the seam buttons', then the speaker's shown first; headers that drag.
        var list = QueueOf(tc, 4);
        list.Columns.Add("Artist", 100);
        list.Columns.Add("Length", 60, HorizontalAlignment.Right);
        list.Columns.Add("", 28, HorizontalAlignment.Center);
        QueueRowStyle.AddMarkColumn(list);
        list.AllowColumnReorder = true;
        foreach (ListViewItem item in list.Items)
        {
            item.UseItemStyleForSubItems = false;
            item.SubItems.Add("artist");
            item.SubItems.Add("0:01");
            item.SubItems.Add("⋈");
            item.SubItems.Add("");
        }
        list.Dock = DockStyle.Fill;
        form.Controls.Add(list);
        QueueRowStyle.Draw(list, p);
        StudioTheme.Apply(form, p);
        using var sections = new QueueSections(list, tc.Engine);
        if (grouped)
        {
            tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
            tc.Engine.StartSection(tc.View.Queue[3].Id, "foo");
        }
        sections.Apply(tc.View);
        form.Show();
        Pump();

        _ = GetWindowRect(form.Handle, out var window);
        var origin = list.PointToScreen(Point.Empty);
        Rectangle Placed(Rectangle r)
        {
            r.Offset(origin.X - window.Left, origin.Y - window.Top);
            return r;
        }
        var rows = Placed(Rectangle.Union(list.Items[0].Bounds, list.Items[list.Items.Count - 1].Bounds));
        var headers = list.Groups.Cast<ListViewGroup>().Select(g => Placed(ListGroups.HeaderBounds(list, g))).ToArray();
        var edges = new List<int>();
        var x = list.Items[0].Bounds.Left;
        foreach (var column in list.Columns.Cast<ColumnHeader>().OrderBy(c => c.DisplayIndex))
        {
            x += column.Width;
            edges.Add(x + origin.X - window.Left);
        }
        using var bitmap = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(bitmap))
        {
            var dc = g.GetHdc();
            _ = PrintWindow(form.Handle, dc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }
        var client = Placed(list.ClientRectangle);
        form.Close();
        check(bitmap, p, new Geometry(client, rows, headers, edges.ToArray()));
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
