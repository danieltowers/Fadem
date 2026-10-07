using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Faderio.Core.Persistence;
using Faderio.Core.Tests;
using Faderio.Shell.Theme;

namespace Faderio.Shell.Tests;

/// <summary>Studio theme issue 06: every faderio window and menu in the palette, on each host's runtime.</summary>
public sealed class DialogThemeTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    [Fact]
    public void TheLibraryChooser_MadeWithAPalette_KeepsNoSystemColour() => RunSta(() =>
    {
        KnownLibrary[] rows = [new(@"D:\Gigs", "Gigs", false, true), new(@"E:\Old", "Old", false, false)];
        using var chooser = new LibraryChooser(() => rows, _ => { }, _ => null, palette: Duophonic);

        Assert.Empty(StudioThemeTests.SystemColoured(chooser));
        Assert.Equal(Duophonic.Chrome, chooser.BackColor);
        Assert.Equal(Duophonic.Dim, chooser._hint.ForeColor);
        Assert.Equal(Duophonic.Dim, chooser._list.Items[1].SubItems[1].ForeColor); // a gone folder, dim
        Assert.Equal(Duophonic.Text, chooser._list.Items[0].SubItems[1].ForeColor);
        Assert.True(chooser._list.OwnerDraw); // a selected row raised, not blue
    });

    [Fact]
    public void TheNoticeList_MadeWithAPalette_KeepsNoSystemColour() => RunSta(() =>
    {
        using var list = new NoticeListForm(Duophonic);

        Assert.Empty(StudioThemeTests.SystemColoured(list));
        Assert.Equal(Duophonic.Well, list._detail.BackColor);
        Assert.True(list._list.OwnerDraw);
    });

    [Fact]
    public void TheTransitionManager_HasItsGridInThePalette() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var manager = new TransitionManagerForm(tc.Engine, _ => { }, palette: Duophonic);
        var grid = manager._grid;

        Assert.Empty(StudioThemeTests.SystemColoured(manager));
        Assert.Equal(Duophonic.Well, grid.BackgroundColor);
        Assert.Equal(Duophonic.Edge, grid.GridColor);
        Assert.Equal(Duophonic.Well, grid.DefaultCellStyle.BackColor);
        Assert.Equal(Duophonic.Text, grid.DefaultCellStyle.ForeColor);
        Assert.Equal(Duophonic.Raised, grid.DefaultCellStyle.SelectionBackColor);
        Assert.False(grid.EnableHeadersVisualStyles);
        Assert.Equal(Duophonic.Dim, grid.ColumnHeadersDefaultCellStyle.ForeColor);
        Assert.True(grid.ColumnHeadersDefaultCellStyle.Font!.Bold);
        Assert.All(grid.Columns.OfType<DataGridViewButtonColumn>(), b =>
        {
            Assert.Equal(FlatStyle.Flat, b.FlatStyle);
            Assert.Equal(Duophonic.Well, b.DefaultCellStyle.BackColor); // a key is drawn on it
        });
        Assert.Equal(Duophonic.Dim, manager._legend.ForeColor);
    });

    [Fact]
    public void TheSectionNameDialog_IsThemed() => RunSta(() =>
    {
        using var form = Dialogs.SectionNameForm("Warm-up", Duophonic);

        Assert.Empty(StudioThemeTests.SystemColoured(form));
        Assert.Equal("Warm-up", form.Controls[0].Controls.OfType<TextBox>().Single().Text);
    });

    [Fact]
    public void AControlsOwnMenu_IsThemedWithIt_AndASubmenuTakesItsRenderer() => RunSta(() =>
    {
        using var panel = new Panel();
        var list = new ListView();
        var sub = new ToolStripMenuItem("Section");
        sub.DropDownItems.Add("Red");
        list.ContextMenuStrip = new ContextMenuStrip { Items = { new ToolStripMenuItem("Remove"), sub } };
        panel.Controls.Add(list);

        StudioTheme.Apply(panel, Duophonic);

        var renderer = Assert.IsType<StudioRenderer>(list.ContextMenuStrip.Renderer);
        Assert.Same(Duophonic, renderer.Palette);
        Assert.Same(renderer, sub.DropDown.Renderer);
        Assert.Equal(Duophonic.Chrome, list.ContextMenuStrip.BackColor);

        var later = new ContextMenuStrip();
        list.ContextMenuStrip = later; // a menu given later is themed as it arrives
        Assert.IsType<StudioRenderer>(later.Renderer);
    });

    [Fact]
    public void AThemedMenu_IsOnChrome_InText_WithADisabledItemDim_AndNoLightSurface() => RunSta(() =>
    {
        using var menu = new ContextMenuStrip();
        menu.Items.Add(new ToolStripMenuItem("Edit transition: this → next"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Clear queue") { Enabled = false });
        menu.Items.Add(new ToolStripMenuItem("Path") { Checked = true });
        StudioTheme.Apply(menu, Duophonic);
        menu.PerformLayout();
        menu.Size = menu.GetPreferredSize(Size.Empty);

        using var bitmap = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(bitmap, new Rectangle(0, 0, menu.Width, menu.Height));
        var pixels = Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))).ToList();

        Assert.True(pixels.Count(c => c.ToArgb() == Duophonic.Chrome.ToArgb()) > pixels.Count / 2, "the ground is not Chrome");
        Assert.Contains(pixels, c => Near(c, Duophonic.Text));
        Assert.Contains(pixels, c => Near(c, Duophonic.Dim));
        Assert.Contains(pixels, c => Near(c, Duophonic.Accent)); // the tick
        Assert.DoesNotContain(pixels, c => c.R > 0xF0 && c.G > 0xF0 && c.B > 0xF0); // no white
    });

    [Fact]
    public void AStatusStripsLink_IsTheAccent_AndItsTextDim() => RunSta(() =>
    {
        using var panel = new Panel();
        var link = new ToolStripStatusLabel("2 notices") { IsLink = true };
        var strip = new StatusStrip { Items = { new ToolStripStatusLabel("Ready."), link } };
        panel.Controls.Add(strip);

        StudioTheme.Apply(panel, Duophonic);

        Assert.Equal(Duophonic.Accent, link.LinkColor);
        Assert.Equal(Duophonic.Dim, strip.ForeColor);
        Assert.Equal(Duophonic.Chrome, strip.BackColor);
        Assert.IsType<StudioRenderer>(strip.Renderer);
    });

    [Fact]
    public void ASpinBoxAndAGroup_AreInThePalette() => RunSta(() =>
    {
        using var form = new Form { Size = new Size(300, 200) };
        var group = new GroupBox { Text = "This library", Bounds = new Rectangle(10, 10, 260, 120) };
        var spin = new NumericUpDown { Bounds = new Rectangle(10, 30, 80, 24) };
        group.Controls.Add(spin);
        form.Controls.Add(group);

        StudioTheme.Apply(form, Duophonic);

        Assert.Empty(StudioThemeTests.SystemColoured(form));
        Assert.Equal(Duophonic.Well, spin.BackColor);
        using var bitmap = new Bitmap(spin.Width, spin.Height);
        spin.DrawToBitmap(bitmap, new Rectangle(0, 0, spin.Width, spin.Height));
        var pixels = Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))).ToList();
        Assert.DoesNotContain(pixels, c => c.R > 0xC0 && c.G > 0xC0 && c.B > 0xC0 && !Near(c, Duophonic.Text)); // no light arrows' ground
        using var groupBitmap = new Bitmap(group.Width, group.Height);
        group.DrawToBitmap(groupBitmap, new Rectangle(0, 0, group.Width, group.Height));
        Assert.Contains(Enumerable.Range(0, 60).Select(x => groupBitmap.GetPixel(x, 6)), c => Near(c, Duophonic.Accent)); // its title, lit
    });

    /// <summary>On screen: the box exists only while a name is being edited, which needs a shown
    /// list. The window is off screen and never activated.</summary>
    [Fact]
    public void ANameBeingEdited_IsOnTheWell_InText() => RunSta(() => NameBox(Duophonic, (bitmap, box) =>
    {
        // Inside its frame, which stays Windows' own grey (see StudioTheme).
        var inside = Enumerable.Range(box.X + 2, box.Width - 4).SelectMany(px => Enumerable.Range(box.Y + 2, box.Height - 4).Select(py => bitmap.GetPixel(px, py))).ToList();
        Assert.True(inside.Count(c => c.ToArgb() == Duophonic.Well.ToArgb()) > inside.Count / 2, "the box is not on the Well");
        Assert.Contains(inside, c => Near(c, Duophonic.Text));
        Assert.DoesNotContain(inside, c => c.R > 0xF0 && c.G > 0xF0 && c.B > 0xF0 && !Near(c, Duophonic.Text));
    }));

    /// <summary>Opens a list's name box on a shown, off-screen, never activated window themed in
    /// <paramref name="palette"/>, and hands <paramref name="check"/> a capture of the window and
    /// the box's bounds in it. The box opens with the name selected, in the system's selection
    /// colour as in any text box: the caret goes to the start first, so its own ground shows.</summary>
    internal static void NameBox(Palette palette, Action<Bitmap, Rectangle> check)
    {
        using var form = new QuietForm
        {
            Size = new Size(300, 200), StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000), ShowInTaskbar = false,
        };
        var list = new ListView { Bounds = new Rectangle(10, 10, 200, 120), View = View.Details, LabelEdit = true, HeaderStyle = ColumnHeaderStyle.None };
        list.Columns.Add("Playlist", 180);
        list.Items.Add("Warm-up set");
        form.Controls.Add(list);
        StudioTheme.Apply(form, palette);
        form.Show();
        Pump();
        list.Focus();
        list.Items[0].BeginEdit();
        Pump();
        var edit = SendMessage(list.Handle, LVM_GETEDITCONTROL, IntPtr.Zero, IntPtr.Zero);
        Assert.NotEqual(IntPtr.Zero, edit); // the box is open
        _ = SendMessage(edit, EM_SETSEL, IntPtr.Zero, IntPtr.Zero);
        Pump();

        _ = GetWindowRect(edit, out var box);
        _ = GetWindowRect(form.Handle, out var window);
        using var bitmap = new Bitmap(window.Right - window.Left, window.Bottom - window.Top);
        using (var g = Graphics.FromImage(bitmap))
        {
            var dc = g.GetHdc();
            _ = PrintWindow(form.Handle, dc, PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }
        form.Close();
        check(bitmap, new Rectangle(box.Left - window.Left, box.Top - window.Top, box.Right - box.Left, box.Bottom - box.Top));
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

    private const int LVM_GETEDITCONTROL = 0x1018;
    private const int EM_SETSEL = 0x00B1;
    private const uint PW_RENDERFULLCONTENT = 2;

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

    internal static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 24 && Math.Abs(a.G - b.G) < 24 && Math.Abs(a.B - b.B) < 24;

    internal static void RunSta(Action body)
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
