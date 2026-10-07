using System.Drawing.Drawing2D;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Faderio.Shell.Theme;

/// <summary>What a control's text is, and so the role it is drawn in (studio theme issue 05).</summary>
internal enum Ink
{
    /// <summary>Plain text, in Text.</summary>
    Text,

    /// <summary>A hint or a secondary line, in Dim.</summary>
    Dim,

    /// <summary>A refusal or a warning, in the accent: the look's one lit colour.</summary>
    Warning,
}

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
        public bool Themed;
        public Ink Ink;
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

    /// <summary>The palette <paramref name="control"/> was themed in, or the built-in one: for a
    /// dialog or a menu made from a themed control, in its colours.</summary>
    public static Palette PaletteOf(Control? control) =>
        control is not null && States.TryGetValue(control, out var state) && state.Themed ? state.Palette : Palette.Studio;

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

    /// <summary>Draws <paramref name="control"/>'s text in <paramref name="ink"/>'s role, now and
    /// whenever it is themed: a hint in Dim, a refusal in the accent.</summary>
    public static void SetInk(Control control, Ink ink)
    {
        var state = States.GetOrCreateValue(control);
        state.Ink = ink;
        control.ForeColor = InkOf(state.Palette, ink);
    }

    private static Color InkOf(Palette p, Ink ink) => ink switch
    {
        Ink.Dim => p.Dim,
        Ink.Warning => p.Accent,
        _ => p.Text,
    };

    /// <summary>Whether <paramref name="key"/>'s LED is lit.</summary>
    public static bool IsLit(ButtonBase key) =>
        key is CheckBox { Appearance: Appearance.Button, Checked: true } || (States.TryGetValue(key, out var s) && s.On);

    /// <summary>Whether <paramref name="control"/> is drawn as a key.</summary>
    public static bool IsKey(Control control) => control is Button or CheckBox { Appearance: Appearance.Button };

    private static void Theme(Control control, Palette p)
    {
        var state = States.GetOrCreateValue(control);
        var first = !state.Themed;
        state.Themed = true;
        state.Palette = p;

        control.ForeColor = InkOf(p, state.Ink);
        switch (control)
        {
            case ListView or TextBox or ComboBox or ListBox or UpDownBase:
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
                var tick = (ButtonBase)control;
                tick.FlatStyle = FlatStyle.Flat;
                tick.FlatAppearance.BorderColor = p.Dim;
                tick.FlatAppearance.CheckedBackColor = control.BackColor;
                if (first)
                {
                    tick.Paint += PaintTick;
                    tick.EnabledChanged += (_, _) => tick.Invalidate();
                }
                break;
            case TextBox box when box.Parent is not UpDownBase:
                box.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox combo:
                combo.FlatStyle = FlatStyle.Flat;
                // One drawn by its owner already, as the manager's track picker, draws itself.
                if (first && combo.DropDownStyle == ComboBoxStyle.DropDownList && combo.DrawMode == DrawMode.Normal)
                {
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.DrawItem += DrawComboItem;
                }
                break;
            case ToolStrip strip:
                // Menus and the status strip (studio theme issue 06): a status strip's text is
                // Dim, its link the accent.
                strip.Renderer = new StudioRenderer(p);
                strip.ForeColor = strip is ToolStripDropDown ? p.Text : p.Dim;
                foreach (var label in strip.Items.OfType<ToolStripLabel>().Where(l => l.IsLink))
                {
                    label.LinkColor = label.ActiveLinkColor = label.VisitedLinkColor = p.Accent;
                    label.LinkBehavior = LinkBehavior.HoverUnderline;
                }
                break;
            case DataGridView grid:
                Grid(grid, p, first);
                break;
            case GroupBox group:
                group.FlatStyle = FlatStyle.Flat;
                if (first)
                {
                    group.Paint += PaintGroup;
                }
                break;
            case UpDownBase spin:
                spin.BorderStyle = BorderStyle.FixedSingle;
                if (first)
                {
                    spin.Paint += (_, e) => Frame(e.Graphics, spin);
                }
                break;
            case Control buttons when first && control.Parent is UpDownBase && control is not TextBoxBase:
                // A spin box's arrows: WinForms draws them in the system's light grey.
                buttons.Paint += PaintSpinButtons;
                break;
            case PaneTitle.Title title:
                title.ForeColor = p.Accent; // a section label, lit (studio theme issue 03)
                break;
            case Readout readout:
                readout.ForeColor = readout.Led ? p.Accent : p.Dim; // the LED readout (studio theme issue 04)
                break;
            case SeekBar seek:
                seek.Palette = p;
                break;
            case TrafficLight light:
                light.Palette = p; // its ring and off state (studio theme issue 05)
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
            if (control is TextBoxBase or ListView or Panel { BorderStyle: BorderStyle.FixedSingle })
            {
                Hairline.Attach(control);
            }
            if (control is ComboBox combo)
            {
                ComboFrame.Attach(combo);
            }
            control.ContextMenuStripChanged += (_, _) =>
            {
                if (control.ContextMenuStrip is { } menu && States.TryGetValue(control, out var owner))
                {
                    Theme(menu, owner.Palette);
                }
            };
            control.ControlAdded += (_, e) =>
            {
                if (e.Control is { } added && States.TryGetValue(control, out var parent))
                {
                    Theme(added, parent.Palette);
                }
            };
        }
        if (control.ContextMenuStrip is { } own)
        {
            Theme(own, p); // the control's own menu, with it
        }
        NativeTheme(control);
        foreach (Control child in control.Controls)
        {
            Theme(child, p);
        }
        control.Invalidate();
    }

    private static bool NeedsNativeTheme(Control control) =>
        control is ListView or ComboBox or TreeView or ScrollBar or TextBoxBase { Multiline: true } or ScrollableControl { AutoScroll: true };

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
    /// <para>Also the box a name is edited in, as a playlist is renamed (studio theme issue 06): the
    /// list makes it, a child edit control of its own, which asks the list for its colours. It is on
    /// the Well, in Text. Its one-pixel frame stays Windows' grey: drawing over it held only in a
    /// test run alone, and clearing its border style left the frame as it was.</para>
    /// </summary>
    private sealed class EmptyGround : Hook
    {
        private readonly ListView _list;
        private IntPtr _brush;

        private EmptyGround(ListView list) : base(list)
        {
            _list = list;
            list.Disposed += (_, _) => Release();
        }

        public static void Attach(ListView list) => _ = new EmptyGround(list);

        private void Release()
        {
            if (_brush != IntPtr.Zero)
            {
                _ = DeleteObject(_brush);
                _brush = IntPtr.Zero;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CTLCOLOREDIT && States.TryGetValue(_list, out var state))
            {
                var p = state.Palette;
                _ = SetTextColor(m.WParam, ColorTranslator.ToWin32(p.Text));
                _ = SetBkColor(m.WParam, ColorTranslator.ToWin32(p.Well));
                if (_brush == IntPtr.Zero)
                {
                    _brush = CreateSolidBrush(ColorTranslator.ToWin32(p.Well));
                }
                m.Result = _brush;
                return;
            }
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

    /// <summary>A window procedure ahead of a control's own, for as long as it has a handle.</summary>
    private abstract class Hook : NativeWindow
    {
        protected Hook(Control control)
        {
            if (control.IsHandleCreated)
            {
                AssignHandle(control.Handle);
            }
            control.HandleCreated += (_, _) => AssignHandle(control.Handle);
            control.HandleDestroyed += (_, _) => ReleaseHandle();
        }
    }

    /// <summary>
    /// A text box's, a list's or a bordered panel's one-pixel frame in Edge (studio theme issues 05
    /// and 06). Windows
    /// draws it in its window frame colour, in the non-client area that WinForms' colours do not
    /// reach, and an edit control draws it again with its text; it is drawn over each time.
    /// </summary>
    private sealed class Hairline : Hook
    {
        private readonly Control _control;

        private Hairline(Control control) : base(control) => _control = control;

        public static void Attach(Control control) => _ = new Hairline(control);

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            // An edit control draws its frame again as it paints its text.
            if (m.Msg is WM_NCPAINT or WM_PAINT)
            {
                var dc = GetWindowDC(Handle);
                try
                {
                    Draw(dc);
                }
                finally
                {
                    _ = ReleaseDC(Handle, dc);
                }
            }
            else if (m.Msg == WM_PRINT && ((long)m.LParam & PRF_NONCLIENT) != 0)
            {
                Draw(m.WParam);
            }
        }

        private bool Bordered => _control switch
        {
            TextBoxBase box => box.BorderStyle == BorderStyle.FixedSingle,
            ListView list => list.BorderStyle != BorderStyle.None,
            Panel panel => panel.BorderStyle == BorderStyle.FixedSingle,
            _ => false,
        };

        private void Draw(IntPtr dc)
        {
            if (dc == IntPtr.Zero || !Bordered || !States.TryGetValue(_control, out var state))
            {
                return;
            }
            using var g = Graphics.FromHdc(dc);
            using var edge = new Pen(state.Palette.Edge);
            g.DrawRectangle(edge, 0, 0, _control.Width - 1, _control.Height - 1);
            if (_control is ListView { BorderStyle: BorderStyle.Fixed3D })
            {
                // A list's sunken frame is two pixels: the inner one on the list's own ground.
                using var ground = new Pen(_control.BackColor);
                g.DrawRectangle(ground, 1, 1, _control.Width - 3, _control.Height - 3);
            }
        }
    }

    /// <summary>
    /// A dropdown's frame and button on the Well, with its chevron in Text, or Dim while disabled
    /// (studio theme issue 05). WinForms' flat combo draws them in system colours, a light grey
    /// frame and button once it is disabled; they are drawn over once it has.
    /// </summary>
    private sealed class ComboFrame : Hook
    {
        private readonly ComboBox _combo;

        private ComboFrame(ComboBox combo) : base(combo) => _combo = combo;

        public static void Attach(ComboBox combo) => _ = new ComboFrame(combo);

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_PAINT)
            {
                var dc = GetDC(Handle);
                try
                {
                    Draw(dc);
                }
                finally
                {
                    _ = ReleaseDC(Handle, dc);
                }
            }
            else if (m.Msg is WM_PRINT or WM_PRINTCLIENT)
            {
                Draw(m.WParam);
            }
        }

        private void Draw(IntPtr dc)
        {
            if (dc == IntPtr.Zero || _combo.FlatStyle != FlatStyle.Flat || !States.TryGetValue(_combo, out var state))
            {
                return;
            }
            var p = state.Palette;
            var w = _combo.Width;
            var h = _combo.Height;
            if (w < 8 || h < 8)
            {
                return;
            }
            using var g = Graphics.FromHdc(dc);
            using (var well = new Pen(p.Well))
            {
                g.DrawRectangle(well, 1, 1, w - 3, h - 3);
                g.DrawRectangle(well, 2, 2, w - 5, h - 5);
            }
            // The button, and the strip WinForms paints beside it.
            var arrow = SystemInformation.HorizontalScrollBarArrowWidth;
            var button = new Rectangle(w - arrow - 4, 1, arrow + 3, h - 2);
            using (var ground = new SolidBrush(p.Well))
            {
                g.FillRectangle(ground, button);
            }
            using (var edge = new Pen(p.Edge))
            {
                g.DrawRectangle(edge, 0, 0, w - 1, h - 1);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var cx = button.Left + button.Width / 2f;
            var cy = h / 2f;
            using var chevron = new Pen(_combo.Enabled ? p.Text : p.Dim, 1.5f);
            g.DrawLines(chevron, new[] { new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2) });
        }
    }

    /// <summary>A check box or radio button on its parent's ground: the box or circle on the
    /// Well with a Dim rim, its mark in the accent, the text in its ink; all of it Dim while
    /// disabled, so it reads as disabled but stays legible (studio theme issue 05). WinForms' flat
    /// one draws its box in the system's window colour, white, and greys its text its own way.</summary>
    private static void PaintTick(object? sender, PaintEventArgs e)
    {
        if (sender is not ButtonBase tick || !States.TryGetValue(tick, out var state))
        {
            return;
        }
        var p = state.Palette;
        var g = e.Graphics;
        g.Clear(tick.BackColor);
        var size = (int)Math.Round(TickSize * tick.DeviceDpi / 96.0);
        var y = (tick.Height - size) / 2;
        var box = new Rectangle(0, y, size - 1, size - 1);
        var mark = tick.Enabled ? p.Accent : p.Dim;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var well = new SolidBrush(p.Well))
        using (var rim = new Pen(tick.Enabled ? p.Dim : p.Raised))
        {
            if (tick is RadioButton radio)
            {
                g.FillEllipse(well, box);
                g.DrawEllipse(rim, box);
                if (radio.Checked)
                {
                    using var dot = new SolidBrush(mark);
                    g.FillEllipse(dot, box.Left + 3, box.Top + 3, box.Width - 6, box.Height - 6);
                }
            }
            else
            {
                g.SmoothingMode = SmoothingMode.None;
                g.FillRectangle(well, box);
                g.DrawRectangle(rim, box);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (tick is CheckBox { CheckState: not CheckState.Unchecked })
                {
                    using var pen = new Pen(mark, 2f);
                    g.DrawLines(pen, new[]
                    {
                        new PointF(box.Left + 2.5f, box.Top + size / 2f),
                        new PointF(box.Left + size * 0.42f, box.Bottom - 3f),
                        new PointF(box.Right - 2.5f, box.Top + 3f),
                    });
                }
            }
        }
        g.SmoothingMode = SmoothingMode.None;
        var text = new Rectangle(size + TickGap, 0, Math.Max(0, tick.Width - size - TickGap), tick.Height);
        var flags = TextFormatFlags.Left | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding;
        if (!tick.UseMnemonic)
        {
            flags |= TextFormatFlags.NoPrefix;
        }
        var height = TextRenderer.MeasureText(g, tick.Text, tick.Font, new Size(text.Width, int.MaxValue), flags).Height;
        text.Y = Math.Max(0, (tick.Height - height) / 2);
        text.Height = Math.Min(tick.Height, height);
        TextRenderer.DrawText(g, tick.Text, tick.Font, text, tick.Enabled ? tick.ForeColor : p.Dim, flags);
        if (tick.Focused)
        {
            ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(text, 1, 0), p.Text, tick.BackColor);
        }
    }

    /// <summary>A grid as the queue is drawn (studio theme issue 06): cells on the Well in Text, the
    /// selected row on Raised, the headings small bold capitals in Dim with Edge rules, and its
    /// buttons flat keys.</summary>
    private static void Grid(DataGridView grid, Palette p, bool first)
    {
        grid.BackgroundColor = p.Well;
        grid.GridColor = p.Edge;
        grid.BorderStyle = BorderStyle.None;
        grid.EnableHeadersVisualStyles = false;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        Cells(grid.DefaultCellStyle, p.Well, p.Text, p.Raised);
        Cells(grid.RowsDefaultCellStyle, p.Well, p.Text, p.Raised);
        Cells(grid.AlternatingRowsDefaultCellStyle, p.Well, p.Text, p.Raised);
        Cells(grid.RowHeadersDefaultCellStyle, p.Well, p.Dim, p.Well);
        Cells(grid.ColumnHeadersDefaultCellStyle, p.Well, p.Dim, p.Well);
        if (first)
        {
            grid.ColumnHeadersDefaultCellStyle.Font = new Font(grid.Font.FontFamily, 7.5f, FontStyle.Bold);
            grid.CellPainting += PaintGridCell;
        }
        foreach (DataGridViewColumn column in grid.Columns)
        {
            if (column is DataGridViewButtonColumn button)
            {
                button.FlatStyle = FlatStyle.Flat;
                Cells(button.DefaultCellStyle, p.Well, p.Text, p.Raised); // the key drawn on the row
            }
            else if (!column.DefaultCellStyle.BackColor.IsEmpty || !column.DefaultCellStyle.ForeColor.IsEmpty)
            {
                Cells(column.DefaultCellStyle, p.Well, p.Text, p.Raised);
            }
        }
    }

    private static void Cells(DataGridViewCellStyle style, Color back, Color fore, Color selected)
    {
        style.BackColor = back;
        style.ForeColor = fore;
        style.SelectionBackColor = selected;
        style.SelectionForeColor = fore;
    }

    /// <summary>A grid's heading, as the queue's header draws one; a button cell as a key.</summary>
    private static void PaintGridCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || !States.TryGetValue(grid, out var state) || e.Graphics is not { } g)
        {
            return;
        }
        var p = state.Palette;
        var bounds = e.CellBounds;
        if (e.RowIndex >= 0 && e.ColumnIndex >= 0 && grid.Columns[e.ColumnIndex] is DataGridViewButtonColumn)
        {
            e.PaintBackground(e.ClipBounds, cellsPaintSelectionBackground: true);
            var key = Rectangle.Inflate(bounds, -3, -2);
            // No hover: the grid repaints a cell only when its row changes.
            DrawKey(g, key, p, e.FormattedValue as string ?? "", e.CellStyle?.Font ?? grid.Font, grid.Enabled, hover: false, lit: false);
            e.Handled = true;
            return;
        }
        if (e.RowIndex != -1)
        {
            return;
        }
        using (var back = new SolidBrush(p.Well))
        {
            g.FillRectangle(back, bounds);
        }
        using (var rule = new Pen(p.Edge))
        {
            g.DrawLine(rule, bounds.Left, bounds.Bottom - 1, bounds.Right, bounds.Bottom - 1);
            g.DrawLine(rule, bounds.Right - 1, bounds.Top + 4, bounds.Right - 1, bounds.Bottom - 5);
        }
        var text = (e.FormattedValue as string ?? "").ToUpperInvariant();
        TextRenderer.DrawText(g, text, e.CellStyle?.Font ?? grid.Font, Rectangle.Inflate(bounds, -4, 0), p.Dim,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix);
        e.Handled = true;
    }

    /// <summary>A group box's frame in Edge and its title as a section label, small bold capitals
    /// in the accent, over what WinForms drew in the system's colours.</summary>
    private static void PaintGroup(object? sender, PaintEventArgs e)
    {
        if (sender is not GroupBox group || !States.TryGetValue(group, out var state))
        {
            return;
        }
        var p = state.Palette;
        var g = e.Graphics;
        g.Clear(group.BackColor);
        using var title = new Font(group.Font.FontFamily, 7.5f, FontStyle.Bold);
        var text = group.Text.ToUpperInvariant();
        var size = TextRenderer.MeasureText(g, text, title, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        var top = size.Height / 2;
        using (var edge = new Pen(p.Edge))
        {
            g.DrawRectangle(edge, 0, top, group.Width - 1, group.Height - top - 1);
        }
        if (text.Length > 0)
        {
            var label = new Rectangle(6, 0, size.Width + 6, size.Height);
            using (var ground = new SolidBrush(group.BackColor))
            {
                g.FillRectangle(ground, label);
            }
            TextRenderer.DrawText(g, text, title, new Point(label.X + 3, 0), group.Enabled ? p.Accent : p.Dim,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>A spin box's frame in Edge.</summary>
    private static void Frame(Graphics g, UpDownBase spin)
    {
        if (!States.TryGetValue(spin, out var state))
        {
            return;
        }
        using var edge = new Pen(state.Palette.Edge);
        g.DrawRectangle(edge, 0, 0, spin.Width - 1, spin.Height - 1);
    }

    /// <summary>A spin box's up and down arrows on the Well, in Text, or Dim while disabled.</summary>
    private static void PaintSpinButtons(object? sender, PaintEventArgs e)
    {
        if (sender is not Control buttons || !States.TryGetValue(buttons, out var state))
        {
            return;
        }
        var p = state.Palette;
        var g = e.Graphics;
        g.Clear(p.Well);
        var w = buttons.Width;
        var h = buttons.Height;
        using (var edge = new Pen(p.Edge))
        {
            g.DrawLine(edge, 0, 0, 0, h);
            g.DrawLine(edge, 0, h / 2, w, h / 2);
        }
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var cx = w / 2f + 0.5f;
        using var chevron = new Pen(buttons.Enabled ? p.Text : p.Dim, 1.25f);
        var up = h / 4f;
        var down = h * 3 / 4f;
        g.DrawLines(chevron, new[] { new PointF(cx - 3, up + 1.5f), new PointF(cx, up - 1.5f), new PointF(cx + 3, up + 1.5f) });
        g.DrawLines(chevron, new[] { new PointF(cx - 3, down - 1.5f), new PointF(cx, down + 1.5f), new PointF(cx + 3, down - 1.5f) });
    }

    /// <summary>A check box's box, and a radio button's circle, at 96 dpi; WinForms' flat one is 11.</summary>
    private const int TickSize = 13;

    /// <summary>Between the box and the text.</summary>
    private const int TickGap = 4;

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
        e.Graphics.Clear(key.Parent?.BackColor ?? p.Chrome);
        var hover = key.Enabled && key.ClientRectangle.Contains(key.PointToClient(Cursor.Position));
        DrawKey(e.Graphics, key.ClientRectangle, p, key.Text, key.Font, key.Enabled, hover, IsLit(key));
    }

    /// <summary>A key drawn in <paramref name="area"/>: on a control of its own, or in a grid's
    /// button cell.</summary>
    private static void DrawKey(Graphics g, Rectangle area, Palette p, string text, Font font, bool enabled, bool hover, bool lit)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(area.X, area.Y, area.Width - 1, area.Height - 1);
        if (bounds.Width < 4 || bounds.Height < 4)
        {
            return;
        }
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
        if (lit)
        {
            using var led = new SolidBrush(p.Accent);
            g.FillEllipse(led, bounds.Right - 7, bounds.Top + 3, 4, 4);
        }
        g.SmoothingMode = SmoothingMode.None;
        TextRenderer.DrawText(g, text, font, area, enabled ? p.Text : p.Dim,
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
    private const int WM_PAINT = 0x000F;
    private const int WM_CTLCOLOREDIT = 0x0133;
    private const int WM_NCPAINT = 0x0085;
    private const int WM_PRINT = 0x0317;
    private const int WM_PRINTCLIENT = 0x0318;
    private const long PRF_NONCLIENT = 0x02;
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr window, string? app, string? list);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern int SetTextColor(IntPtr dc, int colour);

    [DllImport("gdi32.dll")]
    private static extern int SetBkColor(IntPtr dc, int colour);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(int colour);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr gdiObject);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}
