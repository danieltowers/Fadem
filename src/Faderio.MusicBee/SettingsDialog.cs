using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's settings (phase 5 issue 04), opened from MusicBee's plugin preferences: the
/// library folder, typed or picked, and the output mode, which applies when MusicBee next
/// starts, as standalone faderio's applies at its next launch. It holds nothing once closed:
/// its caller reads <see cref="Library"/> and <see cref="OutputMode"/>.
/// <para>MusicBee loads every type here before the plugin's resolver exists, so no field holds
/// a Core value: the mode list's items are boxed modes, named as they are drawn.</para>
/// </summary>
internal sealed class SettingsDialog : Form
{
    internal readonly TextBox _library;
    internal readonly Button _browse;
    internal readonly ComboBox _outputMode;
    internal readonly Button _ok;
    internal readonly Button _cancel;

    /// <param name="library">The library folder to open on, or null when none is chosen.</param>
    /// <param name="mode">The output mode to open on.</param>
    public SettingsDialog(string? library, AudioOutputMode mode)
    {
        Text = "faderio";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        ChooseFolder = ShowFolderDialog;

        _library = new TextBox { Text = library ?? "", Width = 320, AccessibleName = "Library folder" };
        _browse = new Button { Text = "Browse…", AutoSize = true };
        _browse.Click += (_, _) => Browse();

        _outputMode = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Output mode", FormattingEnabled = true,
        };
        _outputMode.Format += (_, e) => e.Value = OutputModeNames.Of((AudioOutputMode)e.ListItem!);
        foreach (AudioOutputMode each in Enum.GetValues(typeof(AudioOutputMode)))
        {
            _outputMode.Items.Add(each);
        }
        _outputMode.SelectedItem = mode;
        var nextStart = new Label { Text = "Applies when MusicBee next starts.", AutoSize = true, ForeColor = SystemColors.GrayText };

        _ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Width = 80 };
        _cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Width = 80 };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 12, 0, 0),
        };
        buttons.Controls.AddRange(new Control[] { _cancel, _ok });
        AcceptButton = _ok;
        CancelButton = _cancel;

        var folder = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        folder.Controls.AddRange(new Control[] { _library, _browse });

        var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rows.Controls.Add(Caption("Library folder:"), 0, 0);
        rows.Controls.Add(folder, 1, 0);
        rows.Controls.Add(Caption("Output mode:"), 0, 1);
        rows.Controls.Add(_outputMode, 1, 1);
        rows.Controls.Add(nextStart, 1, 2);
        rows.Controls.Add(buttons, 0, 3);
        rows.SetColumnSpan(buttons, 2);
        Controls.Add(rows);

        FormClosing += (_, e) => e.Cancel = DialogResult == DialogResult.OK && !Accepts();
    }

    /// <summary>The folder chosen, or null when the box is blank.</summary>
    public string? Library => string.IsNullOrWhiteSpace(_library.Text) ? null : _library.Text.Trim();

    public AudioOutputMode OutputMode => (AudioOutputMode)_outputMode.SelectedItem!;

    /// <summary>The folder picked in the Browse dialog, or null when cancelled. A test replaces
    /// it, since a headless dialog cannot show another.</summary>
    internal Func<string?, string?> ChooseFolder { get; set; }

    private void Browse()
    {
        if (ChooseFolder(Library) is { } folder)
        {
            _library.Text = folder;
        }
    }

    /// <summary>Whether OK can close: a typed folder must be a full path. One that does not
    /// exist is let through, and the engine says so when it tries to open it.</summary>
    private bool Accepts()
    {
        if (Library is not { } folder)
        {
            return true;
        }
        try
        {
            if (Path.IsPathRooted(folder) && Path.GetFullPath(folder) is not null)
            {
                return true;
            }
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
        }
        MessageBox.Show(this, $"\"{folder}\" is not a folder's full path.", "faderio", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        return false;
    }

    private string? ShowFolderDialog(string? start)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose a library folder: faderio keeps its transitions and settings in it",
            SelectedPath = start ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        };
        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.SelectedPath : null;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
}
