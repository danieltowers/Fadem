using System.Drawing;
using System.Windows.Forms;
using Faderio.Core.Settings;
using Faderio.Shell;
using Faderio.Shell.Audio;
using Faderio.Shell.Theme;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's settings (phase 5 issue 04), opened from MusicBee's plugin preferences: the
/// library, chosen from the known libraries in the shared chooser (library chooser issue 03),
/// the output mode, which applies when MusicBee next starts,
/// as standalone faderio's applies at its next launch, and the output device (issue 13), which
/// applies when faderio next plays from stopped: MusicBee's own device, or one of this
/// machine's by name. A saved device that is not there is listed as not found, so opening the
/// dialog never loses it. Below them, whether faderio takes over MusicBee's Now Playing tab
/// (phase 6 issue 08). Last, the open library's fade defaults and ReplayGain mode, as standalone
/// faderio's Settings has them: what a boundary nobody authored plays, the blank duration, and
/// which ReplayGain tracks play at, with MusicBee's choices. They are the
/// library's, saved in it, so with none open they are shown disabled. It holds nothing once
/// closed: its caller reads <see cref="Library"/>, <see cref="OutputMode"/>,
/// <see cref="OutputDevice"/>, <see cref="NowPlayingTab"/>, <see cref="Crossfade"/>,
/// <see cref="CrossfadeSeconds"/>, <see cref="BlankSeconds"/> and <see cref="ReplayGain"/>.
/// <para>MusicBee loads every type here before the plugin's resolver exists, so no field holds
/// a Core value: the mode lists' items are boxed modes, named as they are drawn.</para>
/// </summary>
internal sealed class SettingsDialog : Form
{
    internal readonly TextBox _library;
    internal readonly Button _choose;
    internal readonly ComboBox _outputMode;
    internal readonly ComboBox _outputDevice;
    internal readonly CheckBox _nowPlayingTab;
    internal readonly RadioButton _hardCut;
    internal readonly RadioButton _crossfade;
    internal readonly NumericUpDown _crossfadeSeconds;
    internal readonly NumericUpDown _blankSeconds;
    internal readonly ComboBox _replayGain;

    /// <summary>The device each item of the list stands for: null for MusicBee's own.</summary>
    private readonly List<string?> _deviceNames = new();
    internal readonly Button _ok;
    internal readonly Button _cancel;

    /// <param name="library">The library folder to open on, or null when none is chosen.</param>
    /// <param name="mode">The output mode to open on.</param>
    /// <param name="device">The output device chosen, by name, or null for MusicBee's own.</param>
    /// <param name="devices">This machine's output devices, by name.</param>
    /// <param name="musicBeesDevice">MusicBee's own device, by name, or null when it is Windows' default.</param>
    /// <param name="nowPlayingTab">Whether faderio takes over MusicBee's Now Playing tab (phase 6 issue 08).</param>
    /// <param name="libraryOpen">Whether a library is open to hold the fade defaults.</param>
    /// <param name="crossfade">Whether a boundary nobody authored plays the default crossfade, not a hard cut.</param>
    /// <param name="crossfadeSeconds">The default crossfade's length.</param>
    /// <param name="blankSeconds">What a blank fade duration plays.</param>
    /// <param name="replayGain">Which ReplayGain tracks play at.</param>
    /// <param name="palette">The skin's palette (studio theme issue 06), or the built-in one.</param>
    public SettingsDialog(
        string? library, AudioOutputMode mode, string? device, IReadOnlyList<string> devices, string? musicBeesDevice, bool nowPlayingTab,
        bool libraryOpen, bool crossfade, int crossfadeSeconds, int blankSeconds, ReplayGainMode replayGain, Palette? palette = null)
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
        ChooseLibrary = start => Dialogs.ChooseLibraryFolder(this, start);

        // Read-only: a library is chosen, never typed, so the folder is always a full path. Out of
        // the tab order, so the dialog opens with focus on Choose…, not on the folder all selected.
        _library = new TextBox { Text = library ?? "", Width = 320, ReadOnly = true, TabStop = false, AccessibleName = "Library folder" };
        _choose = new Button { Text = "Choose…", AutoSize = true };
        _choose.Click += (_, _) => Choose();

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
            AutoSize = true,
        };

        _nowPlayingTab = new CheckBox
        {
            Text = "Show faderio in the Now Playing tab", Checked = nowPlayingTab, AutoSize = true, Margin = new Padding(0, 10, 0, 0),
        };
        var nextVisit = new Label
        {
            Text = "In place of MusicBee's own view, from the next visit to the tab.", AutoSize = true,
        };

        // One parent, so the two choices exclude each other; N sits on the crossfade's line.
        _hardCut = new RadioButton { Text = "Hard cut", AutoSize = true, Checked = !crossfade, Enabled = libraryOpen };
        _crossfade = new RadioButton { Text = "Crossfade for", AutoSize = true, Checked = crossfade, Enabled = libraryOpen };
        _crossfadeSeconds = new NumericUpDown
        {
            Minimum = PlayerSettings.MinDefaultCrossfadeSeconds,
            Maximum = PlayerSettings.MaxDefaultCrossfadeSeconds,
            Value = Math.Max(PlayerSettings.MinDefaultCrossfadeSeconds, Math.Min(PlayerSettings.MaxDefaultCrossfadeSeconds, crossfadeSeconds)),
            Width = 48,
            Enabled = libraryOpen && crossfade,
            AccessibleName = "Default crossfade seconds",
        };
        _crossfade.CheckedChanged += (_, _) => _crossfadeSeconds.Enabled = _crossfade.Checked && _crossfade.Enabled;
        var boundaries = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0, 10, 0, 0) };
        boundaries.Controls.AddRange(new Control[] { _hardCut, _crossfade, _crossfadeSeconds, Seconds() });
        boundaries.SetFlowBreak(_hardCut, true);

        _blankSeconds = new NumericUpDown
        {
            Minimum = PlayerSettings.MinBlankDurationSeconds,
            Maximum = PlayerSettings.MaxBlankDurationSeconds,
            Value = Math.Max(PlayerSettings.MinBlankDurationSeconds, Math.Min(PlayerSettings.MaxBlankDurationSeconds, blankSeconds)),
            Width = 48,
            Enabled = libraryOpen,
            AccessibleName = "Blank fade duration seconds",
        };
        var blank = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        blank.Controls.AddRange(new Control[] { _blankSeconds, Seconds() });
        _replayGain = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList, Width = 320, AccessibleName = "ReplayGain", FormattingEnabled = true,
            Enabled = libraryOpen,
        };
        _replayGain.Format += (_, e) => e.Value = Presenter.FmtReplayGainMode((ReplayGainMode)e.ListItem!);
        foreach (var each in Presenter.ReplayGainModes)
        {
            _replayGain.Items.Add(each);
        }
        _replayGain.SelectedItem = replayGain;

        var inLibrary = new Label
        {
            Text = libraryOpen ? "Saved in the open library, as standalone faderio's Settings." : "Open a library to set these.",
            AutoSize = true,
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
        folder.Controls.AddRange(new Control[] { _library, _choose });

        var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rows.Controls.Add(Caption("Library:"), 0, 0);
        rows.Controls.Add(folder, 1, 0);
        rows.Controls.Add(Caption("Output mode:"), 0, 1);
        rows.Controls.Add(_outputMode, 1, 1);
        rows.Controls.Add(Caption("Output device:"), 0, 2);
        rows.Controls.Add(_outputDevice, 1, 2);
        rows.Controls.Add(nextStart, 1, 3);
        rows.Controls.Add(_nowPlayingTab, 1, 4);
        rows.Controls.Add(nextVisit, 1, 5);
        var unauthored = Caption("Unauthored boundaries:");
        unauthored.Margin = new Padding(0, 16, 12, 0);
        rows.Controls.Add(unauthored, 0, 6);
        rows.Controls.Add(boundaries, 1, 6);
        rows.Controls.Add(Caption("Blank fade durations:"), 0, 7);
        rows.Controls.Add(blank, 1, 7);
        rows.Controls.Add(Caption("ReplayGain:"), 0, 8);
        rows.Controls.Add(_replayGain, 1, 8);
        rows.Controls.Add(inLibrary, 1, 9);
        rows.Controls.Add(buttons, 0, 10);
        rows.SetColumnSpan(buttons, 2);
        Controls.Add(rows);
        foreach (var hint in new[] { nextStart, nextVisit, inLibrary })
        {
            StudioTheme.SetInk(hint, Ink.Dim);
        }
        StudioTheme.Apply(this, palette ?? Palette.Studio);
    }

    /// <summary>The folder chosen, or null when the box is blank.</summary>
    public string? Library => string.IsNullOrWhiteSpace(_library.Text) ? null : _library.Text.Trim();

    public AudioOutputMode OutputMode => (AudioOutputMode)_outputMode.SelectedItem!;

    /// <summary>The device chosen, by name, or null for MusicBee's own.</summary>
    public string? OutputDevice => _deviceNames[_outputDevice.SelectedIndex];

    /// <summary>Whether faderio is to show in MusicBee's Now Playing tab.</summary>
    public bool NowPlayingTab => _nowPlayingTab.Checked;

    /// <summary>Whether a boundary nobody authored plays the default crossfade, not a hard cut.</summary>
    public bool Crossfade => _crossfade.Checked;

    /// <summary>The default crossfade's length, in whole seconds.</summary>
    public int CrossfadeSeconds => (int)_crossfadeSeconds.Value;

    /// <summary>What a blank fade duration plays, in whole seconds.</summary>
    public int BlankSeconds => (int)_blankSeconds.Value;

    /// <summary>Which ReplayGain tracks play at.</summary>
    public ReplayGainMode ReplayGain => (ReplayGainMode)_replayGain.SelectedItem!;

    private void AddDevice(string? name, string text)
    {
        _deviceNames.Add(name);
        _outputDevice.Items.Add(text);
    }

    /// <summary>The library chosen for Choose…, with the one chosen so far selected, or null
    /// when cancelled: the host's library chooser, else a plain folder browser. A test replaces
    /// it, since a headless dialog cannot show another.</summary>
    internal Func<string?, string?> ChooseLibrary { get; set; }

    private void Choose()
    {
        if (ChooseLibrary(Library) is { } folder)
        {
            _library.Text = folder;
        }
    }

    private static Label Seconds() => new() { Text = "s", AutoSize = true, Margin = new Padding(0, 6, 0, 0) };

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
}
