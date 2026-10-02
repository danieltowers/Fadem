namespace Faderio.Shell;

/// <summary>
/// The title bar over a pane: the playlists, and in the plugin's view the queue and the editor
/// beside them (phase 6), one height and alignment so the panes' boxes start level.
/// </summary>
public static class PaneTitle
{
    public const int Height = 22;

    public static Label Create(string text) =>
        new() { Text = text, Dock = DockStyle.Top, Height = Height, TextAlign = ContentAlignment.BottomLeft };
}
