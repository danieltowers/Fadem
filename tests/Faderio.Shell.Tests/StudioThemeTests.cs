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

    [Fact]
    public void ALightPalette_KeepsLightLists() => RunSta(() =>
    {
        var light = Palette.From(new SkinColours(
            unchecked((int)0xFFF0F0F0), unchecked((int)0xFFFFFFFF), unchecked((int)0xFFC8C8C8), unchecked((int)0xFFE6E6E6),
            unchecked((int)0xFF202020), unchecked((int)0xFF808080), unchecked((int)0xFF1F77B4)));
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
