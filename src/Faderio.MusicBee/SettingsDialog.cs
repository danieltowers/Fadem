using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's settings (phase 5 issue 04), opened from MusicBee's plugin preferences: the
/// library folder, typed or picked, the output mode, which applies when MusicBee next starts,
/// as standalone faderio's applies at its next launch, and the output device (issue 13), which
/// applies when faderio next plays from stopped: MusicBee's own device, or one of this
/// machine's by name. A saved device that is not there is listed as not found, so opening the
/// dialog never loses it. It holds nothing once closed: its caller reads <see cref="Library"/>,
/// <see cref="OutputMode"/> and <see cref="OutputDevice"/>.
/// <para>MusicBee loads every type here before the plugin's resolver exists, so no field holds
/// a Core value: the mode list's items are boxed modes, named as they are drawn.</para>
/// </summary>
internal sealed class SettingsDialog : Form
{
    internal readonly TextBox _library;
    internal readonly Button _browse;
    internal readonly ComboBox _outputMode;
    internal readonly ComboBox _outputDevice;

    /// <summary>The device each item of the list stands for: null for MusicBee's own.</summary>
    private readonly List<string?> _deviceNames = new();
    internal readonly Button _ok;
    internal readonly Button _cancel;

    /// <param name="library">The library folder to open on, or null when none is chosen.</param>
    /// <param name="mode">The output mode to open on.</param>
    /// <param name="device">The output device chosen, by name, or null for MusicBee's own.</param>
    /// <param name="devices">This machine's output devices, by name.</param>
    /// <param name="musicBeesDevice">MusicBee's own device, by name, or null when it is Windows' default.</param>
    public SettingsDialog(string? library, AudioOutputMode mode, string? device, IReadOnlyList<string> devices, string? musicBeesDevice)
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

        // The device list: MusicBee's own device first, then this machine's; one saved that is
        // not there is listed as such, so the choice is never lost by opening the dialog.
        _outputDevice = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "Output device" };
        AddDevice(null, musicBeesDevice is null ? "MusicBee's device" : $"MusicBee's device ({musicBeesDevice})");
        foreach (var name in devices)
        {
            AddDevice(name, name);
        }
        if (device is not null && !devices.Contains(device))
        {
            AddDevice(device, $"{device} (not found)");
        }
        _outputDevice.SelectedIndex = _deviceNames.IndexOf(device);
        var nextStart = new Label
        {
            Text = "The mode applies when MusicBee next starts; the device when faderio next plays from stopped.",
            AutoSize = true, ForeColor = SystemColors.GrayText,
        };

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
        rows.Controls.Add(Caption("Output device:"), 0, 2);
        rows.Controls.Add(_outputDevice, 1, 2);
        rows.Controls.Add(nextStart, 1, 3);
        rows.Controls.Add(buttons, 0, 4);
        rows.SetColumnSpan(buttons, 2);
        Controls.Add(rows);

        FormClosing += (_, e) => e.Cancel = DialogResult == DialogResult.OK && !Accepts();
    }

    /// <summary>The folder chosen, or null when the box is blank.</summary>
    public string? Library => string.IsNullOrWhiteSpace(_library.Text) ? null : _library.Text.Trim();

    public AudioOutputMode OutputMode => (AudioOutputMode)_outputMode.SelectedItem!;

    /// <summary>The device chosen, by name, or null for MusicBee's own.</summary>
    public string? OutputDevice => _deviceNames[_outputDevice.SelectedIndex];

    private void AddDevice(string? name, string text)
    {
        _deviceNames.Add(name);
        _outputDevice.Items.Add(text);
    }

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
