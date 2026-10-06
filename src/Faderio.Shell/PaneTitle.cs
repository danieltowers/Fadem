namespace Faderio.Shell;

/// <summary>
/// The title bar over a pane: the playlists, and in the plugin's view the queue and the editor
/// beside them (phase 6), one height and alignment so the panes' boxes start level. It reads in
/// small bold capitals, which <see cref="Theme.StudioTheme"/> sets in the accent (studio theme
/// issue 03); its text stays as given.
/// </summary>
public static class PaneTitle
{
    public const int Height = 22;

    public static Label Create(string text) => new Title { Text = text };

    /// <summary>A pane's title, drawn in capitals, its baseline a little above the pane.</summary>
    internal sealed class Title : Label
    {
        private readonly Font _font;

        public Title()
        {
            Dock = DockStyle.Top;
            Height = PaneTitle.Height;
            UseMnemonic = false;
            _font = new Font(Font.FontFamily, 7.5f, FontStyle.Bold);
            Font = _font;
        }

        protected override void OnPaint(PaintEventArgs e) =>
            TextRenderer.DrawText(e.Graphics, Text.ToUpperInvariant(), Font, new Rectangle(1, 0, Width - 1, Height - 3), ForeColor,
                TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                _font.Dispose();
            }
        }
    }
}
