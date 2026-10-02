namespace Faderio.Shell;

/// <summary>
/// The questions the shell asks through its host, the same in both hosts (phase 5 issue 11):
/// where to render an adjacency, and a section's name (phase 6 issue 09). It asked what to do
/// with unsaved edits too, until the editor saved itself (phase 6 issue 05).
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

    /// <summary>A section's name, opening on <paramref name="initial"/>: blank for a new section.
    /// Null when cancelled.</summary>
    public static string? AskSectionName(IWin32Window? owner, string initial)
    {
        using var form = new Form
        {
            Text = initial.Length == 0 ? "New section" : "Rename section",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ShowInTaskbar = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10),
        };
        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        var name = new TextBox { Text = initial, Width = 260 };
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Width = 260 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
        buttons.Controls.AddRange([cancel, ok]);
        layout.Controls.AddRange([new Label { Text = "Section name:", AutoSize = true }, name, buttons]);
        form.Controls.Add(layout);
        form.AcceptButton = ok;
        form.CancelButton = cancel;
        return form.ShowDialog(owner) == DialogResult.OK ? name.Text : null;
    }
}
