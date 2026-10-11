using Fadem.Shell;
using Fadem.Shell.Theme;
using Fadem.Core.Domain;
using Fadem.Core.Settings;
using Fadem.Shell.Audio;

namespace Fadem.App;

/// <summary>
/// The Settings… dialog (build issue 12), in two groups (library issue 03, D11). "This machine":
/// the output mode, with a note that it applies at the next launch and, when it is not the mode
/// in use (a fallback, or a choice waiting for the next launch), the mode in use beside it.
/// "This library": what unauthored boundaries play, what a transition's blank fade duration plays,
/// the default fade curve (round 2 issue 14), and the ReplayGain mode. It opens on the view's
/// settings and hands back each group's choice, <see cref="MachineChoice"/> and
/// <see cref="LibraryChoice"/>; on OK the caller sends each group the user changed to its own
/// engine command. It holds
/// nothing once closed.
/// </summary>
internal sealed class SettingsForm : Form
{
    internal readonly GroupBox _machineGroup;
    internal readonly GroupBox _libraryGroup;
    internal readonly ComboBox _outputMode;
    internal readonly Label _nextLaunch;
    internal readonly Label _inUse;
    internal readonly RadioButton _hardCut;
    internal readonly RadioButton _crossfade;
    internal readonly NumericUpDown _seconds;
    internal readonly NumericUpDown _blankSeconds;
    internal readonly ComboBox _defaultCurve;
    internal readonly ComboBox _replayGain;
    internal readonly Button _ok;
    internal readonly Button _cancel;
    private readonly MachineSettings _shownMachine;
    private readonly PlayerSettings _shownLibrary;

    /// <summary>A mode as the list shows it.</summary>
    private sealed record ModeItem(AudioOutputMode Mode)
    {
        public override string ToString() => OutputModeNames.Of(Mode);
    }

    /// <summary>A curve as the list shows it, named as the editor names it.</summary>
    private sealed record CurveItem(FadeCurveKind Curve)
    {
        public override string ToString() => Presenter.FmtCurve(Curve);
    }

    /// <summary>A ReplayGain mode as the list shows it.</summary>
    private sealed record GainItem(ReplayGainMode Mode)
    {
        public override string ToString() => Presenter.FmtReplayGainMode(Mode);
    }

    /// <param name="machine">This machine's settings to open on: the view's.</param>
    /// <param name="shown">The library's settings to open on: the view's.</param>
    /// <param name="inUse">The mode the output opened in, or null when none could be.</param>
    /// <param name="palette">The host's palette (studio theme issue 06), or the built-in one.</param>
    public SettingsForm(MachineSettings machine, PlayerSettings shown, AudioOutputMode? inUse, Palette? palette = null)
    {
        _shownMachine = machine;
        _shownLibrary = shown;
        Text = "Settings";
        Icon = AppIcon.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);

        _outputMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
        _outputMode.Items.AddRange(Enum.GetValues<AudioOutputMode>().Select(m => (object)new ModeItem(m)).ToArray());
        _outputMode.SelectedIndex = _outputMode.Items.IndexOf(new ModeItem(machine.OutputMode));
        _nextLaunch = new Label { Text = "Applies at the next launch.", AutoSize = true };
        StudioTheme.SetInk(_nextLaunch, Ink.Dim);
        _inUse = new Label { Text = Presenter.FmtModeInUse(machine.OutputMode, inUse), AutoSize = true };

        // One parent, so the two choices exclude each other; N sits on the crossfade's line.
        _hardCut = new RadioButton { Text = "Hard cut", AutoSize = true, Checked = shown.UnauthoredAdjacency == UnauthoredAdjacency.HardCut };
        _crossfade = new RadioButton { Text = "Crossfade for", AutoSize = true, Checked = shown.UnauthoredAdjacency == UnauthoredAdjacency.DefaultCrossfade };
        _seconds = new NumericUpDown
        {
            Minimum = PlayerSettings.MinDefaultCrossfadeSeconds,
            Maximum = PlayerSettings.MaxDefaultCrossfadeSeconds,
            Value = shown.DefaultCrossfadeSeconds,
            Width = 48,
            Enabled = _crossfade.Checked,
        };
        _crossfade.CheckedChanged += (_, _) => _seconds.Enabled = _crossfade.Checked;
        var boundaries = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Margin = new Padding(0) };
        boundaries.Controls.AddRange(new Control[]
        {
            _hardCut, _crossfade, _seconds, new Label { Text = "s", AutoSize = true, Margin = new Padding(0, 6, 0, 0) },
        });
        boundaries.SetFlowBreak(_hardCut, true);

        _blankSeconds = new NumericUpDown
        {
            Minimum = PlayerSettings.MinBlankDurationSeconds,
            Maximum = PlayerSettings.MaxBlankDurationSeconds,
            Value = shown.BlankDurationSeconds,
            Width = 48,
        };
        var blank = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0) };
        blank.Controls.AddRange(new Control[]
        {
            _blankSeconds, new Label { Text = "s", AutoSize = true, Margin = new Padding(0, 6, 0, 0) },
        });

        _defaultCurve = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160, AccessibleName = "Default fade curve" };
        _defaultCurve.Items.AddRange(Enum.GetValues<FadeCurveKind>().Select(c => (object)new CurveItem(c)).ToArray());
        _defaultCurve.SelectedIndex = _defaultCurve.Items.IndexOf(new CurveItem(shown.DefaultCurve));

        _replayGain = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 300, AccessibleName = "ReplayGain" };
        _replayGain.Items.AddRange(Presenter.ReplayGainModes.Select(m => (object)new GainItem(m)).ToArray());
        _replayGain.SelectedIndex = _replayGain.Items.IndexOf(new GainItem(shown.ReplayGainMode));

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

        var machineRows = Rows();
        machineRows.Controls.Add(Caption("Output mode:"), 0, 0);
        machineRows.Controls.Add(_outputMode, 1, 0);
        machineRows.Controls.Add(_nextLaunch, 1, 1);
        machineRows.Controls.Add(_inUse, 1, 2);
        _machineGroup = Group("This machine", machineRows);

        var libraryRows = Rows();
        libraryRows.Controls.Add(Caption("Unauthored boundaries:"), 0, 0);
        libraryRows.Controls.Add(boundaries, 1, 0);
        libraryRows.Controls.Add(Caption("Blank fade durations:"), 0, 1);
        libraryRows.Controls.Add(blank, 1, 1);
        libraryRows.Controls.Add(Caption("Default fade curve:"), 0, 2);
        libraryRows.Controls.Add(_defaultCurve, 1, 2);
        libraryRows.Controls.Add(Caption("ReplayGain:"), 0, 3);
        libraryRows.Controls.Add(_replayGain, 1, 3);
        _libraryGroup = Group("This library", libraryRows);

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 1, Dock = DockStyle.Fill };
        layout.Controls.Add(_machineGroup, 0, 0);
        layout.Controls.Add(_libraryGroup, 0, 1);
        layout.Controls.Add(buttons, 0, 2);
        Controls.Add(layout);
        StudioTheme.Apply(this, palette ?? Palette.Studio);
    }

    /// <summary>Whether the machine group now says other than it opened on: only then does OK
    /// send it.</summary>
    public bool MachineChanged => MachineChoice.OutputMode != _shownMachine.OutputMode;

    /// <summary>Whether the library group now says other than it opened on: only then does OK
    /// send it.</summary>
    public bool LibraryChanged => LibraryChoice != _shownLibrary;

    /// <summary>What the machine group says now, whole: one engine call takes it. The output
    /// device is not this form's (phase 5 issue 13: standalone Fadem opens the default
    /// device), so the value it opened on is kept.</summary>
    public MachineSettings MachineChoice => _shownMachine with
    {
        OutputMode = ((ModeItem)_outputMode.SelectedItem!).Mode,
    };

    /// <summary>What the library group says now, whole: one engine call takes it.</summary>
    public PlayerSettings LibraryChoice => new()
    {
        UnauthoredAdjacency = _crossfade.Checked ? UnauthoredAdjacency.DefaultCrossfade : UnauthoredAdjacency.HardCut,
        DefaultCrossfadeSeconds = (int)_seconds.Value,
        BlankDurationSeconds = (int)_blankSeconds.Value,
        DefaultCurve = ((CurveItem)_defaultCurve.SelectedItem!).Curve,
        ReplayGainMode = ((GainItem)_replayGain.SelectedItem!).Mode,
    };

    /// <summary>A group's rows: a caption, then its controls.</summary>
    private static TableLayoutPanel Rows()
    {
        var rows = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return rows;
    }

    private static GroupBox Group(string text, Control rows)
    {
        var group = new GroupBox
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            Padding = new Padding(8, 4, 8, 8),
            Margin = new Padding(0, 0, 0, 8),
        };
        group.Controls.Add(rows);
        return group;
    }

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
}
