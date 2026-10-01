using Faderio.Core.Domain;

namespace Faderio.Shell;

/// <summary>
/// The questions the editor asks through its host, the same in both hosts (phase 5 issue 11):
/// what to do with unsaved edits on leaving the editor, and where to render an adjacency. The
/// unsaved-edits question is a task dialog on net8.0-windows and a Yes / No / Cancel message
/// box on net48, which has no task dialog (<c>Compat</c>): the brief's TaskDialog swap.
/// </summary>
internal static partial class Dialogs
{
    /// <summary>Asks what to do with the editor's unsaved edits to <paramref name="adjacency"/>
    /// before it leaves them.</summary>
    public static UnsavedEdits AskUnsavedEdits(IWin32Window? owner, Adjacency adjacency) =>
        ShowUnsavedEdits(owner, $"Save your changes to {adjacency.Name}?");

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
