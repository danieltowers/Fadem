using Faderio.Core.Settings;

namespace Faderio.App;

/// <summary>
/// The Settings… dialog (build issue 12): the output mode, with a note that it applies at the
/// next launch and, when it is not the mode in use (a fallback, or a choice waiting for the next
/// launch), the mode in use beside it; what unauthored boundaries play; and ReplayGain. It opens on the view's settings and hands back
/// <see cref="Chosen"/>; the caller sends it to the engine in one call on OK. It holds nothing
/// once closed.
/// </summary>
internal sealed class SettingsForm : Form
{
    internal readonly ComboBox _outputMode;
    internal readonly Label _nextLaunch;
    internal readonly Label _inUse;
    internal readonly RadioButton _hardCut;
    internal readonly RadioButton _crossfade;
    internal readonly NumericUpDown _seconds;
    internal readonly CheckBox _replayGain;
    internal readonly Button _ok;
    internal readonly Button _cancel;

    /// <summary>A mode as the list shows it.</summary>
    private sealed record ModeItem(AudioOutputMode Mode)
    {
        public override string ToString() => Presenter.FmtOutputMode(Mode);
    }

    /// <param name="shown">The settings to open on: the view's.</param>
    /// <param name="inUse">The mode the output opened in, or null when none could be.</param>
    public SettingsForm(PlayerSettings shown, AudioOutputMode? inUse)
    {
        Text = "Settings";
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
        _outputMode.SelectedIndex = _outputMode.Items.IndexOf(new ModeItem(shown.OutputMode));
        _nextLaunch = new Label { Text = "Applies at the next launch.", AutoSize = true, ForeColor = SystemColors.GrayText };
        _inUse = new Label { Text = Presenter.FmtModeInUse(shown.OutputMode, inUse), AutoSize = true };

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

        _replayGain = new CheckBox { Text = "Apply ReplayGain track gain", AutoSize = true, Checked = shown.ReplayGainEnabled };

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

        var layout = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, Dock = DockStyle.Fill };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(Caption("Output mode:"), 0, 0);
        layout.Controls.Add(_outputMode, 1, 0);
        layout.Controls.Add(_nextLaunch, 1, 1);
        layout.Controls.Add(_inUse, 1, 2);
        layout.Controls.Add(Caption("Unauthored boundaries:"), 0, 3);
        layout.Controls.Add(boundaries, 1, 3);
        layout.Controls.Add(_replayGain, 1, 4);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        Controls.Add(layout);
    }

    /// <summary>What the dialog says now, whole: one engine call takes it.</summary>
    public PlayerSettings Chosen => new()
    {
        OutputMode = ((ModeItem)_outputMode.SelectedItem!).Mode,
        UnauthoredAdjacency = _crossfade.Checked ? UnauthoredAdjacency.DefaultCrossfade : UnauthoredAdjacency.HardCut,
        DefaultCrossfadeSeconds = (int)_seconds.Value,
        ReplayGainEnabled = _replayGain.Checked,
    };

    private static Label Caption(string text) =>
        new() { Text = text, AutoSize = true, Margin = new Padding(0, 6, 12, 0) };
}
