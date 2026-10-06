using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Faderio.Shell.Theme;

/// <summary>
/// The studio console look (studio theme issue 02), applied to a tree of controls in a
/// <see cref="Palette"/>: the window in Chrome; lists, text boxes and dropdowns in recessed Wells;
/// every push button a rounded hardware key, top-lit, with an LED in the accent while it is on.
/// Windows' own dark list, scroll bar and title bar themes follow a dark palette. Controls added
/// later, as the views add and remove their panes, are themed as they arrive. The custom-drawn
/// parts (the queue's rows, the seek bar) take the palette themselves.
/// </summary>
internal static class StudioTheme
{
    private const int KeyRadius = 4;

    private sealed class State
    {
        public Palette Palette = Palette.Studio;
        public bool On;
    }

    /// <summary>Each themed control's palette, and a key's LED; gone with the control.</summary>
    private static readonly ConditionalWeakTable<Control, State> States = new();

    /// <summary>Themes <paramref name="root"/> and everything in it, now and as it is added.</summary>
    public static void Apply(Control root, Palette palette)
    {
        Theme(root, palette);
        if (root is Form form)
        {
            DarkTitleBar(form, palette.IsDark);
        }
    }

    /// <summary>Lights or puts out a key's LED: ⏸ while playing, say. A checked toggle lights its own.</summary>
    public static void Light(ButtonBase key, bool on)
    {
        var state = States.GetOrCreateValue(key);
        if (state.On != on)
        {
            state.On = on;
            key.Invalidate();
        }
    }

    /// <summary>Whether <paramref name="key"/>'s LED is lit.</summary>
    public static bool IsLit(ButtonBase key) =>
        key is CheckBox { Appearance: Appearance.Button, Checked: true } || (States.TryGetValue(key, out var s) && s.On);

    /// <summary>Whether <paramref name="control"/> is drawn as a key.</summary>
    public static bool IsKey(Control control) => control is Button or CheckBox { Appearance: Appearance.Button };

    private static void Theme(Control control, Palette p)
    {
        var first = !States.TryGetValue(control, out var state);
        state ??= States.GetOrCreateValue(control);
        state.Palette = p;

        control.ForeColor = p.Text;
        switch (control)
        {
            case ListView or TextBox or ComboBox or ListBox:
                control.BackColor = p.Well;
                break;
            case Label or RadioButton or CheckBox { Appearance: Appearance.Normal }:
                // On whatever it sits on, never a box of its own.
                control.BackColor = control.Parent?.BackColor ?? p.Chrome;
                break;
            default:
                control.BackColor = p.Chrome;
                break;
        }
        switch (control)
        {
            case ButtonBase key when IsKey(key):
                key.FlatStyle = FlatStyle.Flat;
                key.FlatAppearance.BorderSize = 0;
                key.FlatAppearance.MouseOverBackColor = p.Raised;
                key.FlatAppearance.MouseDownBackColor = p.Raised;
                key.FlatAppearance.CheckedBackColor = p.Raised;
                key.UseVisualStyleBackColor = false;
                if (first)
                {
                    key.Paint += PaintKey;
                    key.MouseEnter += (_, _) => key.Invalidate();
                    key.MouseLeave += (_, _) => key.Invalidate();
                    key.EnabledChanged += (_, _) => key.Invalidate();
                }
                break;
            case CheckBox or RadioButton:
                ((ButtonBase)control).FlatStyle = FlatStyle.Flat;
                ((ButtonBase)control).FlatAppearance.BorderColor = p.Dim;
                ((ButtonBase)control).FlatAppearance.CheckedBackColor = control.BackColor;
                break;
            case TextBox box:
                box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                if (first && combo.DropDownStyle == ComboBoxStyle.DropDownList)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.DrawItem += DrawComboItem;
                }
                break;
            case ToolStrip strip:
                strip.RenderMode = ToolStripRenderMode.System;
                strip.ForeColor = p.Dim;
                break;
            case PaneTitle.Title title:
                title.ForeColor = p.Accent; // a section label, lit (studio theme issue 03)
                break;
        }
        if (first)
        {
            if (NeedsNativeTheme(control))
            {
                control.HandleCreated += (_, _) => NativeTheme(control);
            }
            if (control is ListView list)
            {
                EmptyGround.Attach(list);
            }
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is { } added && States.TryGetValue(control, out var parent))
                {
                    Theme(added, parent.Palette);
                }
            };
        }
        NativeTheme(control);
        foreach (Control child in control.Controls)
        {
            Theme(child, p);
        }
        control.Invalidate();
    }

    private static bool NeedsNativeTheme(Control control) =>
        control is ListView or ComboBox or TreeView or ScrollableControl { AutoScroll: true };

    /// <summary>Windows' own dark themes, for what WinForms cannot colour: scroll bars, the list's
    /// header beyond its columns, a dropdown's frame.</summary>
    private static void NativeTheme(Control control)
    {
        if (!control.IsHandleCreated || !NeedsNativeTheme(control) || !States.TryGetValue(control, out var state))
        {
            return;
        }
        var dark = state.Palette.IsDark;
        if (control is ComboBox)
        {
            _ = SetWindowTheme(control.Handle, dark ? "DarkMode_CFD" : null, null);
            return;
        }
        _ = SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
        if (control is ListView)
        {
            var header = SendMessage(control.Handle, LVM_GETHEADER, IntPtr.Zero, IntPtr.Zero);
            if (header != IntPtr.Zero)
            {
                _ = SetWindowTheme(header, dark ? "DarkMode_ItemsView" : "ItemsView", null);
            }
        }
    }

    /// <summary>
    /// A list's ground below its last row, plain. Windows' dark list theme (<c>DarkMode_Explorer</c>)
    /// draws a two-pixel rule at every column's edge down a details view's empty part (its
    /// <c>LVP_COLUMNDETAIL</c>), and no other dark list theme exists: the names it might be fall
    /// back to the light theme, white frame and all (studio theme issue 03).
    /// </summary>
    private sealed class EmptyGround : NativeWindow
    {
        private readonly ListView _list;

        private EmptyGround(ListView list) => _list = list;

        public static void Attach(ListView list)
        {
            var ground = new EmptyGround(list);
            if (list.IsHandleCreated)
            {
                ground.AssignHandle(list.Handle);
            }
            list.HandleCreated += (_, _) => ground.AssignHandle(list.Handle);
            list.HandleDestroyed += (_, _) => ground.ReleaseHandle();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_ERASEBKGND && _list.View == View.Details)
            {
                using var g = Graphics.FromHdc(m.WParam);
                using var ground = new SolidBrush(_list.BackColor);
                g.FillRectangle(ground, _list.ClientRectangle);
                m.Result = (IntPtr)1;
                return;
            }
            base.WndProc(ref m);
        }
    }

    /// <summary>A rounded key: a top-lit face from Key, an Edge outline and a shine along its
    /// top; Raised under the pointer; the LED at its top-right while on; the glyph in Dim while
    /// disabled. The flat button WinForms drew underneath is painted over whole.</summary>
    private static void PaintKey(object? sender, PaintEventArgs e)
    {
        if (sender is not ButtonBase key || !States.TryGetValue(key, out var state))
        {
            return;
        }
        var p = state.Palette;
        var g = e.Graphics;
        g.Clear(key.Parent?.BackColor ?? p.Chrome);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, key.Width - 1, key.Height - 1);
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return;
        }
        var hover = key.Enabled && key.ClientRectangle.Contains(key.PointToClient(Cursor.Position));
        var face = hover ? p.Raised : p.Key;
        using (var path = Rounded(bounds, KeyRadius))
        {
            using (var fill = new LinearGradientBrush(bounds, Shade(face, 0.10), Shade(face, -0.06), LinearGradientMode.Vertical))
            {
                g.FillPath(fill, path);
            }
            using var edge = new Pen(p.Edge);
            g.DrawPath(edge, path);
        }
        using (var shine = new Pen(Color.FromArgb(p.IsDark ? 36 : 120, Color.White)))
        {
            g.DrawLine(shine, bounds.Left + KeyRadius, bounds.Top + 1, bounds.Right - KeyRadius, bounds.Top + 1);
        }
        if (IsLit(key))
        {
            using var led = new SolidBrush(p.Accent);
            g.FillEllipse(led, bounds.Right - 7, bounds.Top + 3, 4, 4);
        }
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, key.Text, key.Font, key.ClientRectangle, key.Enabled ? p.Text : p.Dim,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding);
    }

    /// <summary>A dropdown's box and list, on the Well; the selected item on Raised.</summary>
    private static void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (sender is not ComboBox combo || !States.TryGetValue(combo, out var state))
        {
            return;
        }
        var p = state.Palette;
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var back = new SolidBrush(selected ? p.Raised : p.Well))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }
        if (e.Index >= 0)
        {
            TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, e.Bounds,
                combo.Enabled ? p.Text : p.Dim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));
        var path = new GraphicsPath();
        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Toward white by <paramref name="by"/> when positive, toward black when negative.</summary>
    private static Color Shade(Color c, double by)
    {
        var to = by >= 0 ? 255 : 0;
        var f = Math.Abs(by);
        return Color.FromArgb((int)(c.R + (to - c.R) * f), (int)(c.G + (to - c.G) * f), (int)(c.B + (to - c.B) * f));
    }

    private static void DarkTitleBar(Form form, bool dark)
    {
        void Set()
        {
            var on = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
        }
        if (form.IsHandleCreated)
        {
            Set();
        }
        else
        {
            form.HandleCreated += (_, _) => Set();
        }
    }

    private const int LVM_GETHEADER = 0x1000 + 31;
    private const int WM_ERASEBKGND = 0x0014;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? app, string? list);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
