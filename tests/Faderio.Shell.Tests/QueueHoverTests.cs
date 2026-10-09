using System.Runtime.InteropServices;
using Faderio.Core.Tests;
using Faderio.Shell.Theme;
using static Faderio.Shell.Tests.QueueSectionsTests;

namespace Faderio.Shell.Tests;

/// <summary>In groups Windows fades the row and the header under the mouse in and out, by its own
/// drawing and into buffers with an alpha channel, so a drawn queue's rows flickered blank as the
/// mouse crossed them (2026-10-09). On each host's runtime: no row is hot, so nothing fades, and a
/// row or header drawn into such a buffer is opaque, GDI's text included.</summary>
public sealed class QueueHoverTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    [Fact]
    public void TheMouseOverARow_LeavesNoRowHot() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var list = QueueOf(tc, 4);
        QueueRowStyle.Draw(list, Duophonic);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
        sections.Apply(tc.View);

        foreach (var row in new[] { 0, 2 })
        {
            var at = new Point(60, list.Items[row].Bounds.Top + 5);
            SendMessage(list.Handle, WM_MOUSEMOVE, IntPtr.Zero, (IntPtr)((at.Y << 16) | at.X));
            Assert.Equal(-1, (int)SendMessage(list.Handle, LVM_GETHOTITEM, IntPtr.Zero, IntPtr.Zero));
        }
    });

    [Fact]
    public void ARowAndAHeader_DrawnIntoABufferWithAlpha_AreOpaque() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var form = new Form { Size = new Size(420, 320), StartPosition = FormStartPosition.Manual, Location = new Point(-2000, -2000), ShowInTaskbar = false };
        var list = QueueOf(tc, 4);
        list.Dock = DockStyle.Fill;
        form.Controls.Add(list);
        QueueRowStyle.Draw(list, Duophonic);
        StudioTheme.Apply(form, Duophonic);
        using var sections = new QueueSections(list, tc.Engine);
        tc.Engine.StartSection(tc.View.Queue[1].Id, "Intro");
        sections.Apply(tc.View);
        form.Show();
        for (var i = 0; i < 10; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }

        var size = list.ClientSize;
        var info = new BITMAPINFO { biSize = 40, biWidth = size.Width, biHeight = -size.Height, biPlanes = 1, biBitCount = 32 };
        var dib = CreateDIBSection(IntPtr.Zero, ref info, 0, out var bits, IntPtr.Zero, 0);
        var dc = CreateCompatibleDC(IntPtr.Zero);
        var old = SelectObject(dc, dib);
        // A zeroed 32-bit buffer, all transparent, which the list draws itself into.
        SendMessage(list.Handle, WM_PRINTCLIENT, dc, (IntPtr)(PRF_CLIENT | PRF_ERASEBKGND));
        var pixels = new int[size.Width * size.Height];
        Marshal.Copy(bits, pixels, 0, pixels.Length);
        SelectObject(dc, old);
        DeleteDC(dc);
        DeleteObject(dib);

        int Unseen(Rectangle r) =>
            Enumerable.Range(r.Top, r.Height).SelectMany(y => Enumerable.Range(r.Left, Math.Min(r.Right, size.Width) - r.Left)
                .Select(x => pixels[y * size.Width + x])).Count(p => ((p >> 24) & 0xFF) != 0xFF);
        Assert.Equal(0, Unseen(list.Items[0].Bounds)); // a row in no section
        Assert.Equal(0, Unseen(list.Items[1].Bounds)); // a row in Intro
        Assert.Equal(0, Unseen(ListGroups.HeaderBounds(list, list.Groups[1])));
        form.Close();
    });

    private const int WM_MOUSEMOVE = 0x0200;
    private const int LVM_GETHOTITEM = 0x1000 + 61;
    private const int WM_PRINTCLIENT = 0x0318;
    private const int PRF_CLIENT = 0x4;
    private const int PRF_ERASEBKGND = 0x8;

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFO
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
        public int colors;
    }

    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFO info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
}
