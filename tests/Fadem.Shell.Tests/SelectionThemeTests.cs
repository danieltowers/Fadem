using System.Runtime.InteropServices;
using Fadem.Core.Tests;
using Fadem.Shell.Theme;
using static Fadem.Shell.Tests.QueueSectionsTests;

namespace Fadem.Shell.Tests;

/// <summary>Studio theme issue 06's leftovers: a selected row is a raised tint wherever a list or
/// a grid has focus, never Windows' blue or its light focus frame, on each host's runtime. Windows
/// draws focus only on a shown window with focus cues on, so the window is shown off screen,
/// never activated, and told to show its cues as keyboard use would.</summary>
public sealed class SelectionThemeTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    /// <summary>The playlist sidebar's list was drawn by Windows: its selection, focused, was the
    /// dark theme's blue (<c>#0E2A41</c>) with a light frame.</summary>
    [Fact]
    public void AFocusedPlaylistsSelectedRow_IsRaised_NotWindowsBlue() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        var sidebar = new PlaylistSidebar(tc.Engine, () => { }) { Palette = Duophonic, Dock = DockStyle.Fill };
        var list = sidebar._list;
        list.Items.Add(new ListViewItem("Warm-up set") { BackColor = Duophonic.Well, ForeColor = Duophonic.Text });
        list.Items.Add(new ListViewItem("Main set") { BackColor = Duophonic.Well, ForeColor = Duophonic.Text });
        Shown(sidebar, () =>
        {
            list.Focus();
            list.Items[1].Selected = true;
            list.Items[1].Focused = true;
        }, (bitmap, origin) =>
        {
            var row = list.Items[1].Bounds;
            row.Offset(list.PointToScreen(Point.Empty).X - sidebar.PointToScreen(Point.Empty).X + origin.X,
                list.PointToScreen(Point.Empty).Y - sidebar.PointToScreen(Point.Empty).Y + origin.Y);
            var pixels = Pixels(bitmap, Rectangle.Inflate(row, -1, -1));
            Assert.True(list.Focused, "the list never had focus, so the test shows nothing");
            Assert.True(pixels.Count(c => c.ToArgb() == Duophonic.Raised.ToArgb()) > pixels.Count / 2, "the selected row is not Raised");
            Assert.DoesNotContain(pixels, c => DialogThemeTests.Near(c, Color.FromArgb(0x0E, 0x2A, 0x41))); // not the dark theme's blue
            Assert.Contains(pixels, c => DialogThemeTests.Near(c, Duophonic.Text)); // its name, readable
        });
    });

    /// <summary>The playlists keep their own row height: the queue's taller rows are the queue's.</summary>
    [Fact]
    public void ThePlaylists_KeepTheirOwnRowHeight() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var plain = new ListView { View = View.Details };
        plain.Columns.Add("x");
        plain.Items.Add("Warm-up set");
        _ = plain.Handle;
        using var sidebar = new PlaylistSidebar(tc.Engine, () => { }) { Palette = Duophonic };
        sidebar._list.Items.Add("Warm-up set");
        _ = sidebar._list.Handle;

        Assert.True(sidebar._list.OwnerDraw);
        Assert.Equal(plain.Items[0].Bounds.Height, sidebar._list.Items[0].Bounds.Height);
    });

    /// <summary>The manager's grid framed its current cell in a dotted, near white focus rectangle.</summary>
    [Fact]
    public void AFocusedGridsCurrentCell_HasNoLightFrame() => RunSta(() =>
    {
        var grid = new DataGridView
        {
            Bounds = new Rectangle(10, 10, 260, 120), RowHeadersVisible = false, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        grid.Columns.Add("arrow", "");
        grid.Columns.Add("track", "Other track");
        grid.Rows.Add("", "Quiet two");
        grid.Rows.Add("", "Gone away");
        Shown(grid, () =>
        {
            grid.Focus();
            grid.CurrentCell = grid.Rows[0].Cells[0];
        }, (bitmap, origin) =>
        {
            var cell = grid.GetCellDisplayRectangle(0, 0, false);
            cell.Offset(origin);
            var pixels = Pixels(bitmap, cell); // an empty cell: anything light in it is a frame
            Assert.True(grid.Focused, "the grid never had focus, so the test shows nothing");
            Assert.DoesNotContain(pixels, c => c.R > 0xA0 && c.G > 0xA0 && c.B > 0xA0);
        });
    });

    private static List<Color> Pixels(Bitmap bitmap, Rectangle area) =>
        Enumerable.Range(area.Left, area.Width).SelectMany(x => Enumerable.Range(area.Top, area.Height).Select(y => bitmap.GetPixel(x, y))).ToList();

    /// <summary>Puts <paramref name="control"/> on a themed, shown, off-screen window with focus
    /// cues on, runs <paramref name="arrange"/>, and hands <paramref name="check"/> a capture and the
    /// control's client origin in it.</summary>
    private static void Shown(Control control, Action arrange, Action<Bitmap, Point> check)
    {
        using var form = new QuietForm
        {
            Size = new Size(320, 220), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false,
        };
        form.Controls.Add(control);
        StudioTheme.Apply(form, Duophonic);
        form.Show();
        Pump();
        _ = SendMessage(form.Handle, WM_UPDATEUISTATE, (IntPtr)(UIS_CLEAR | (UISF_HIDEFOCUS << 16)), IntPtr.Zero);
        arrange();
        Pump();
        control.Invalidate(true);
        Pump();
        _ = GetWindowRect(form.Handle, out var window);
        var origin = control.PointToScreen(Point.Empty);
        using var bitmap = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(bitmap))
        {
            var dc = g.GetHdc();
            _ = PrintWindow(form.Handle, dc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }
        check(bitmap, new Point(origin.X - window.Left, origin.Y - window.Top));
        form.Close();
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
    private const int WM_UPDATEUISTATE = 0x0128;
    private const int UIS_CLEAR = 2;
    private const int UISF_HIDEFOCUS = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);
}
