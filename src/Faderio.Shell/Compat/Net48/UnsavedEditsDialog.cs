namespace Faderio.Shell;

internal static partial class Dialogs
{
    /// <summary>net48 has no task dialog: a message box whose Yes saves and No discards, said
    /// in its text since its buttons cannot be renamed.</summary>
    private static UnsavedEdits ShowUnsavedEdits(IWin32Window? owner, string heading)
    {
        var chosen = MessageBox.Show(
            owner,
            heading + "\n\nThe editor has changes that are not saved. Yes saves them, No discards them.",
            "Unsaved transition",
            MessageBoxButtons.YesNoCancel,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button1);
        return chosen == DialogResult.Yes ? UnsavedEdits.Save : chosen == DialogResult.No ? UnsavedEdits.Discard : UnsavedEdits.Cancel;
    }
}
