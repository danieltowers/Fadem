namespace Faderio.Shell;

internal static partial class Dialogs
{
    /// <summary>The task dialog, as standalone faderio has always asked it.</summary>
    private static UnsavedEdits ShowUnsavedEdits(IWin32Window? owner, string heading)
    {
        var save = new TaskDialogButton("Save");
        var discard = new TaskDialogButton("Discard");
        var page = new TaskDialogPage
        {
            Caption = "Unsaved transition",
            Heading = heading,
            Text = "The editor has changes that are not saved. Discard loses them.",
            Icon = TaskDialogIcon.Warning,
            Buttons = { save, discard, TaskDialogButton.Cancel },
            DefaultButton = save,
        };
        var chosen = owner is null ? TaskDialog.ShowDialog(page) : TaskDialog.ShowDialog(owner, page);
        return chosen == save ? UnsavedEdits.Save : chosen == discard ? UnsavedEdits.Discard : UnsavedEdits.Cancel;
    }
}
