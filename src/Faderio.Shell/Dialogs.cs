namespace Faderio.Shell;

/// <summary>
/// The question the editor asks through its host, the same in both hosts (phase 5 issue 11):
/// where to render an adjacency. It asked what to do with unsaved edits too, until the editor
/// saved itself (phase 6 issue 05).
/// </summary>
internal static class Dialogs
{
    /// <summary>The WAV save dialog for "Render this boundary", opening on the suggested name.
    /// Null when cancelled.</summary>
    public static string? ChooseRenderPath(IWin32Window? owner, string suggestedName)
    {
        using var dialog = new SaveFileDialog
        {
            Title = "Render this boundary",
            Filter = "WAV|*.wav",
            DefaultExt = "wav",
            FileName = suggestedName,
        };
        return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileName : null;
    }
}
