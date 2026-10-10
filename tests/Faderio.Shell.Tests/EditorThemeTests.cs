using System.Drawing;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Tests;
using Faderio.Shell.Theme;

namespace Faderio.Shell.Tests;

/// <summary>Studio theme issue 05: the transition editor in the palette, on each host's runtime.</summary>
public sealed class EditorThemeTests
{
    private static readonly Palette Duophonic = Palette.From(PaletteTests.Duophonic);

    [Fact]
    public void TheEditor_KeepsNoSystemColour_AndItsTicksSitOnTheirParentsGround() => RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var host = new Panel { Size = new Size(900, 700) };
        StudioTheme.Apply(host, Duophonic);
        var editor = new TransitionEditorPanel(tc.Engine, _ => { }, _ => null);

        host.Controls.Add(editor); // themed as it arrives, as in either host
        AssertThemed(editor);

        // Open on a boundary, with a refusal showing: still nothing in system colours.
        var paths = new[] { TestAudio.WriteDcWav("theme-a.wav", 5, 0.1), TestAudio.WriteDcWav("theme-b.wav", 5, 0.2) };
        tc.Add(paths);
        editor.Open(tc.AdjacencyOf(paths[0], paths[1]));
        editor._fadeOutPoint.Text = "abc";
        editor.Commit();
        editor.Refresh(tc.View);
        AssertThemed(editor);
        Assert.Equal(Duophonic.Mark, editor.ReasonFor(TransitionField.FadeOutPoint).ForeColor); // a refusal is in Mark (round 2 issue 06); Duophonic's red accent makes Mark its text, so AWarning_IsInMark_NotTheAccent holds the red
    });

    private static void AssertThemed(TransitionEditorPanel editor)
    {
        Assert.Empty(StudioThemeTests.SystemColoured(editor));
        var ticks = StudioThemeTests.All(editor).Where(c => c is RadioButton or CheckBox { Appearance: Appearance.Normal }).ToList();
        Assert.Equal(3, ticks.Count); // Global, This playlist, From end
        Assert.All(ticks, t => Assert.Equal(t.Parent!.BackColor, t.BackColor));
        Assert.Equal(Duophonic.Text, editor._title.ForeColor);
        Assert.Equal(Duophonic.Dim, editor._captureOutHint.ForeColor);
        Assert.Equal(Duophonic.Dim, editor._effective.ForeColor);
        Assert.Equal(Duophonic.Mark, editor._missing.ForeColor);
        Assert.All(new[] { editor._fadeOutLess, editor._fadeOutMore, editor._fadeOutLessSecond, editor._fadeOutMoreSecond },
            b => Assert.True(StudioTheme.IsKey(b) && b.FlatStyle == FlatStyle.Flat));
        Assert.Equal(Duophonic, editor._light.Palette);
        Assert.Equal(Duophonic.Well, editor._curve.BackColor);
    }

    [Fact]
    public void ADisabledCheckBox_IsDimOnItsGround_WithNoLightBox() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(200, 40) };
        var check = new CheckBox { Text = "From end", Bounds = new Rectangle(4, 4, 120, 24), Enabled = false };
        panel.Controls.Add(check);
        StudioTheme.Apply(panel, Duophonic);

        var pixels = Draw(check);

        Assert.DoesNotContain(pixels, Light);
        Assert.Contains(pixels, c => Near(c, Duophonic.Dim)); // its text, disabled but legible
        Assert.DoesNotContain(pixels, c => Near(c, Duophonic.Text));
    });

    [Fact]
    public void ACheckedTick_IsMarkedInTheAccent_AndItsTextIsText() => RunSta(() =>
    {
        using var panel = new Panel { Size = new Size(200, 60) };
        var check = new CheckBox { Text = "From end", Bounds = new Rectangle(4, 4, 120, 24), Checked = true };
        var radio = new RadioButton { Text = "Global", Bounds = new Rectangle(4, 30, 120, 24), Checked = true };
        panel.Controls.AddRange(new Control[] { check, radio });
        StudioTheme.Apply(panel, Duophonic);

        foreach (var tick in new ButtonBase[] { check, radio })
        {
            var pixels = Draw(tick);
            Assert.Contains(pixels, c => Near(c, Duophonic.Accent));
            Assert.Contains(pixels, c => Near(c, Duophonic.Text));
        }
    });

    /// <summary>On screen, not by DrawToBitmap: an edit control draws its frame again as it paints
    /// its text, which only a shown window does.</summary>
    [Fact]
    public void ATextBox_IsFramedInEdge_OnScreen() => RunSta(() =>
    {
        using var form = new QuietForm { Size = new Size(300, 160), StartPosition = FormStartPosition.Manual, Location = new System.Drawing.Point(-2000, -2000), ShowInTaskbar = false };
        var live = new TextBox { Bounds = new Rectangle(10, 10, 120, 24), Text = "3.5" };
        var off = new TextBox { Bounds = new Rectangle(10, 50, 120, 24), Enabled = false };
        form.Controls.AddRange(new Control[] { live, off });
        StudioTheme.Apply(form, Duophonic);
        form.Show();
        for (var i = 0; i < 20; i++)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }

        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using (var g = Graphics.FromImage(bitmap))
        {
            var dc = g.GetHdc();
            _ = PrintWindow(form.Handle, dc, PW_CLIENTONLY | PW_RENDERFULLCONTENT);
            g.ReleaseHdc(dc);
        }
        form.Close();

        foreach (var box in new[] { live, off })
        {
            Assert.Equal(Duophonic.Edge.ToArgb(), bitmap.GetPixel(box.Left, box.Top + box.Height / 2).ToArgb());
            Assert.Equal(Duophonic.Edge.ToArgb(), bitmap.GetPixel(box.Left + box.Width / 2, box.Top).ToArgb());
        }
    });

    private sealed class QuietForm : Form
    {
        protected override bool ShowWithoutActivation => true;
    }

    private const uint PW_CLIENTONLY = 1;
    private const uint PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [Fact]
    public void ADisabledDropdown_IsOnTheWell_WithNoLightFrameOrButton() => RunSta(() =>
    {
        using var form = new Form { Size = new Size(300, 120) };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Bounds = new Rectangle(10, 10, 140, 24), Enabled = false };
        combo.Items.AddRange(new object[] { "Equal-power", "Linear" });
        combo.SelectedIndex = 0;
        form.Controls.Add(combo);
        StudioTheme.Apply(form, Duophonic);
        form.CreateControl();
        _ = combo.Handle;

        var pixels = Draw(combo);

        Assert.DoesNotContain(pixels, Light);
        Assert.Contains(pixels, c => c.ToArgb() == Duophonic.Edge.ToArgb());
    });

    [Fact]
    public void TheTrafficLight_KeepsItsMeaning_AndIsOffAndRingedInThePalette() => RunSta(() =>
    {
        using var light = new TrafficLight { Size = new Size(32, 28), Palette = Duophonic };

        Assert.Equal(Duophonic.Dim, light.Lit); // stopped
        Assert.Contains(Draw(light), c => c.ToArgb() == Duophonic.Edge.ToArgb());

        light.Heard = AdjacencyHeard.Outgoing;
        Assert.Equal(TrafficLight.Colour(AdjacencyHeard.Outgoing, Palette.Studio), light.Lit); // green in any skin
        Assert.Contains(Draw(light), c => Near(c, light.Lit));
    });

    private static List<Color> Draw(Control control)
    {
        using var bitmap = new Bitmap(control.Width, control.Height);
        control.DrawToBitmap(bitmap, new Rectangle(0, 0, control.Width, control.Height));
        return Enumerable.Range(0, bitmap.Width).SelectMany(x => Enumerable.Range(0, bitmap.Height).Select(y => bitmap.GetPixel(x, y))).ToList();
    }

    /// <summary>A light surface, such as the system's window white or control grey.</summary>
    private static bool Light(Color c) => c.R > 0xB0 && c.G > 0xB0 && c.B > 0xB0;

    private static bool Near(Color a, Color b) => Math.Abs(a.R - b.R) < 24 && Math.Abs(a.G - b.G) < 24 && Math.Abs(a.B - b.B) < 24;

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
