using System.Drawing;
using System.Runtime.ExceptionServices;
using Faderio.Shell.Theme;

namespace Faderio.Shell.Tests;

/// <summary>Studio theme issue 02: a tree of controls in the studio look, drawn from a palette.</summary>
public sealed class StudioThemeTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    /// <summary>Every control in <paramref name="root"/>, itself included.</summary>
    internal static IEnumerable<Control> All(Control root) =>
        new[] { root }.Concat(root.Controls.Cast<Control>().SelectMany(All));

    /// <summary>The controls whose own colours are still the system's.</summary>
    internal static string[] SystemColoured(Control root) =>
        All(root).Where(c => c.BackColor.IsSystemColor || c.ForeColor.IsSystemColor)
            .Select(c => $"{c.GetType().Name} '{c.Text}'").ToArray();

    [Fact]
    public void ATree_IsInChromeAndWells_WithNoSystemColourLeft() => RunSta(() =>
    {
        using var panel = new Panel();
        var list = new ListView();
        var box = new TextBox();
        var label = new Label { Text = "Fade-out point (s)" };
        var check = new CheckBox { Text = "From end" };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
        panel.Controls.AddRange(new Control[] { list, box, label, check, combo, new Button { Text = "Preview" } });

        StudioTheme.Apply(panel, Duophonic);

        Assert.Empty(SystemColoured(panel));
        Assert.Equal(Duophonic.Chrome, panel.BackColor);
        Assert.Equal(Duophonic.Well, list.BackColor);
        Assert.Equal(Duophonic.Well, box.BackColor);
        Assert.Equal(Duophonic.Well, combo.BackColor);
        Assert.Equal(panel.BackColor, label.BackColor); // on what it sits on, never a box of its own
        Assert.Equal(panel.BackColor, check.BackColor);
        Assert.Equal(DrawMode.OwnerDrawFixed, combo.DrawMode);
    });

    [Fact]
    public void AControlAddedLater_IsThemedAsItArrives() => RunSta(() =>
    {
        using var panel = new Panel();
        StudioTheme.Apply(panel, Duophonic);
        var pane = new Panel();
        var key = new Button { Text = "Close editor" };
        pane.Controls.Add(key);

        panel.Controls.Add(pane);

        Assert.Empty(SystemColoured(panel));
        Assert.Equal(FlatStyle.Flat, key.FlatStyle);
    });

    [Fact]
    public void AKey_LightsItsLedInTheAccent_OnlyWhileOn() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(100, 60) };
        var key = new Button { Text = "⏸", Bounds = new Rectangle(10, 10, 36, 28) };
        var toggle = new CheckBox { Appearance = Appearance.Button, Text = "🔁", Bounds = new Rectangle(50, 10, 36, 28) };
        panel.Controls.AddRange(new Control[] { key, toggle });
        StudioTheme.Apply(panel, Duophonic);

        Assert.False(LedLit(key));
        StudioTheme.Light(key, true);
        Assert.True(LedLit(key));
        StudioTheme.Light(key, false);
        Assert.False(LedLit(key));

        Assert.False(LedLit(toggle));
        toggle.Checked = true; // a checked toggle lights its own
        Assert.True(LedLit(toggle));
    });

    /// <summary>In Text since round 2 issue 06: the accent is kept for what marks a state.</summary>
    [Fact]
    public void APaneTitle_IsASectionLabel_InText_InSmallBoldCapitals() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(200, 60) };
        var title = PaneTitle.Create("Playlists");
        panel.Controls.Add(title);

        StudioTheme.Apply(panel, Duophonic);

        Assert.Equal(Duophonic.Text, title.ForeColor);
        Assert.True(title.Font.Bold);
        Assert.True(title.Font.Size < panel.Font.Size);
        Assert.Equal("Playlists", title.Text); // drawn in capitals, named as given
        using var bitmap = new Bitmap(title.Width, title.Height);
        title.DrawToBitmap(bitmap, new Rectangle(0, 0, title.Width, title.Height));
        Assert.Contains(Pixels(bitmap), c => Near(c, Duophonic.Text));
        Assert.DoesNotContain(Pixels(bitmap), c => Near(c, Duophonic.Accent));
    });

    [Fact]
    public void ADarkList_HasNoColumnRulesDownItsEmptyPart() => RunSta(() =>
    {
        using var form = new Form { Size = new Size(400, 300) };
        var list = new ListView { Dock = DockStyle.Fill, View = View.Details };
        list.Columns.Add("A", 60);
        list.Columns.Add("B", 80);
        list.Columns.Add("C", 60);
        list.Items.Add(new ListViewItem(new[] { "1", "two", "3" }));
        form.Controls.Add(list);
        StudioTheme.Apply(form, Duophonic);
        form.CreateControl();
        _ = list.Handle;

        using var bitmap = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(bitmap, new Rectangle(0, 0, list.Width, list.Height));

        // A row of pixels well below the one row, inside the frame: nothing but the Well.
        var y = list.Height - 20;
        var off = Enumerable.Range(4, list.Width - 30).Select(x => bitmap.GetPixel(x, y)).Where(c => !Near(c, Duophonic.Well)).ToList();
        Assert.Empty(off);
    });

    /// <summary>Round 2 issue 06: a refusal or warning is in Mark, the seek bar's red, not the
    /// accent. On the built-in palette the two differ (on a red-accented one Mark is the text).</summary>
    [Fact]
    public void AWarning_IsInMark_NotTheAccent() => RunSta(() =>
    {
        using var panel = new Panel();
        var warning = new Label { Text = "Not saved" };
        panel.Controls.Add(warning);
        StudioTheme.SetInk(warning, Ink.Warning);

        StudioTheme.Apply(panel, Palette.Studio);

        Assert.Equal(Palette.Studio.Mark, warning.ForeColor);
        Assert.NotEqual(Palette.Studio.Accent, warning.ForeColor);
    });

    // ── Studio theme issue 04: the LED readout ──────────────────────────────────────────

    /// <summary>The readout in Text since round 2 issue 06.</summary>
    [Fact]
    public void TheReadout_IsTheLedFaceInText_AndTheSecondsUnderItAreDim() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(400, 60) };
        var time = Readout.Time();
        var seconds = Readout.Seconds();
        time.SetBounds(0, 0, 180, 28);
        seconds.Dock = DockStyle.None;
        seconds.SetBounds(0, 28, 180, 18);
        panel.Controls.AddRange(new Control[] { time, seconds });

        StudioTheme.Apply(panel, Duophonic);
        time.Text = "0:14 / -3:03";

        Assert.Equal(Duophonic.Text, time.ForeColor);
        Assert.Equal(Duophonic.Dim, seconds.ForeColor);
        Assert.Equal("Consolas", time.DrawnFont.Name);
        Assert.Equal(Readout.LedSize, time.DrawnFont.Size); // it fits whole at full size
        Assert.True(TextRenderer.MeasureText(time.Text, time.DrawnFont).Height <= time.Height, "the readout is clipped");
        Assert.True(seconds.Font.Size < 10);
    });

    [Fact]
    public void TheReadout_DrawsSmaller_WhereItWouldNotFit_SoItIsNeverCutShort() => RunSta(() =>
    {
        using var time = Readout.Time();
        time.Dock = DockStyle.None;
        time.SetBounds(0, 0, 110, 28); // the plugin's column in a sidebar, as it was
        time.Text = "Held 1:02:03 / -1:02:03";

        var font = time.DrawnFont;

        Assert.True(font.Size < Readout.LedSize);
        Assert.True(font.Size >= 8);
        Assert.Equal("Consolas", font.Name);
        Assert.True(TextRenderer.MeasureText(time.Text, font, Size.Empty, TextFormatFlags.NoPadding).Width <= time.Width || font.Size == 8,
            "the readout is cut short");
    });

    private static IEnumerable<Color> Pixels(Bitmap bitmap) =>
        Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y)));

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 24 && Math.Abs(a.G - b.G) < 24 && Math.Abs(a.B - b.B) < 24;

    [Fact]
    public void ALightPalette_KeepsLightLists() => RunSta(() =>
    {
        var light = Palette.From(new SkinColours(
            unchecked((int)0xFFF0F0F0), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFC8C8C8), unchecked((int)0xFFE6E6E6),
            unchecked((int)0xFF202020), unchecked((int)0xFF808080)));
        using var panel = new Panel();
        var list = new ListView();
        panel.Controls.Add(list);

        StudioTheme.Apply(panel, light);

        Assert.False(light.IsDark);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF).ToArgb(), list.BackColor.ToArgb());
        Assert.Empty(SystemColoured(panel));
    });

    /// <summary>Whether the LED's pixel, near the key's top-right, is the accent.</summary>
    private static bool LedLit(ButtonBase key)
    {
        using var bitmap = new Bitmap(key.Width, key.Height);
        key.DrawToBitmap(bitmap, new Rectangle(0, 0, key.Width, key.Height));
        var led = bitmap.GetPixel(key.Width - 1 - 5, 5);
        return Math.Abs(led.R - Duophonic.Accent.R) < 24 && Math.Abs(led.G - Duophonic.Accent.G) < 24 && Math.Abs(led.B - Duophonic.Accent.B) < 24;
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
