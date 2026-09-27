using Faderio.Core.Domain;
using Faderio.Core.Playback;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App;

/// <summary>What to do with unsaved edits on leaving the editor (build issue 15).</summary>
internal enum UnsavedEdits
{
    Save,
    Discard,
    Cancel,
}

/// <summary>
/// The transition editor for one adjacency: four parameter fields, two capture buttons, a
/// from-end box on the fade-out point, the fade curve, Save, Revert, Remove transition and
/// "Render this boundary" (ticket 05's slice, completed by build issue 14). Holds the two
/// captured points as <see cref="Point"/>s, so the text boxes are a display of the value, never
/// its source of truth: v2 formatted the capture with "0.###" before saving it and quantised
/// every point to a millisecond (ticket 06). The fields are a draft; the engine converts,
/// captures and validates, and each value it refuses is shown beside its field. Leaving a draft
/// that differs from what is stored asks Save / Discard / Cancel (build issue 15). Preview plays
/// the draft on its own voices while the queue is held, and shows where it is (build issue 16).
/// <para>A selector at the top picks the layer the fields edit (D21, playlists issue 04): the
/// global transition, or the open playlist's override of it. In playlist mode each box shows the
/// global value as grey placeholder text, only what is typed is the override, and the curve list
/// has "(global)". The mode is kept for the session (D23), so the row button and the menu open
/// the editor in the last one.</para>
/// </summary>
internal sealed class TransitionEditorPanel : Panel
{
    private readonly PlaybackEngine _engine;
    private readonly Action<string> _notice;
    private readonly Func<string, string?> _chooseRenderPath;
    private readonly Func<Adjacency, UnsavedEdits> _askUnsaved;

    internal readonly Label _title;
    internal readonly RadioButton _editGlobal;
    internal readonly RadioButton _editPlaylist;
    internal readonly Label _effective;
    internal readonly Label _missing;
    internal readonly Button _removeOverride;
    internal readonly TextBox _fadeOutPoint;
    internal readonly CheckBox _fadeOutFromEnd;
    internal readonly TextBox _fadeOutDuration;
    internal readonly TextBox _fadeInPoint;
    internal readonly TextBox _fadeInDuration;
    internal readonly ComboBox _curve;
    internal readonly Button _captureOut;
    internal readonly Button _captureIn;
    internal readonly Button _fadeOutLess;
    internal readonly Button _fadeOutMore;
    internal readonly Button _fadeInLess;
    internal readonly Button _fadeInMore;
    internal readonly Button _save;
    internal readonly Button _revert;
    internal readonly Button _remove;
    internal readonly Button _render;
    internal readonly Button _preview;
    internal readonly Button _stopPreview;
    internal readonly Label _previewStatus;
    private readonly Dictionary<TransitionField, Label> _reasons = new();

    private Adjacency? _adjacency;
    private Point? _fadeOut;
    private Point? _fadeIn;
    private bool _loading;

    /// <summary>The layer last chosen, kept for the session (D23).</summary>
    private TransitionLayer _layer = TransitionLayer.Global;

    /// <summary>A curve as the list shows it; none is "(global)", in playlist mode only.</summary>
    private sealed record CurveItem(FadeCurveKind? Curve)
    {
        public override string ToString() => Presenter.FmtCurve(Curve);
    }

    public TransitionEditorPanel(
        PlaybackEngine engine, Action<string> notice,
        Func<string, string?> chooseRenderPath, Func<Adjacency, UnsavedEdits> askUnsaved)
    {
        _engine = engine;
        _notice = notice;
        _chooseRenderPath = chooseRenderPath;
        _askUnsaved = askUnsaved;

        Dock = DockStyle.Right;
        Width = 340;
        Padding = new Padding(10);
        BorderStyle = BorderStyle.FixedSingle;

        // The fields take their own height; the panel around them scrolls only when that
        // overflows (a short window, or long refusal reasons), and only up and down.
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        scroller.Controls.Add(layout);
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

        _title = new Label
        {
            Text = NoAdjacency,
            Dock = DockStyle.Top,
            Height = 26,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        var row = 0;
        _editGlobal = new RadioButton { Text = "Global", AutoSize = true, Checked = true, Margin = new Padding(3, 0, 3, 0) };
        _editPlaylist = new RadioButton
        {
            Text = PlaylistChoice(null),
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            Enabled = false,
            Margin = new Padding(3, 0, 3, 0),
        };
        var selector = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            MaximumSize = new Size(300, 0),
            Margin = new Padding(0),
        };
        selector.Controls.Add(new Label { Text = "Editing:", AutoSize = true, Margin = new Padding(3, 3, 0, 0) });
        selector.Controls.Add(_editGlobal);
        selector.Controls.Add(_editPlaylist);
        layout.Controls.Add(selector, 0, row);
        layout.SetColumnSpan(selector, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;
        // Hidden unless the open playlist plays something other than the global transition.
        _effective = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            ForeColor = SystemColors.GrayText,
            Visible = false,
            Margin = new Padding(3, 0, 3, 0),
        };
        layout.Controls.Add(_effective, 0, row);
        layout.SetColumnSpan(_effective, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;
        // Shown only on an adjacency with a gone side (phase 3 issue 02).
        _missing = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(290, 0),
            ForeColor = Color.Firebrick,
            Visible = false,
            Margin = new Padding(3, 0, 3, 6),
        };
        layout.Controls.Add(_missing, 0, row);
        layout.SetColumnSpan(_missing, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;

        _fadeOutFromEnd = new CheckBox { Text = "From end", Dock = DockStyle.Fill, Enabled = false };
        _fadeOutPoint = Field(layout, ref row, TransitionField.FadeOutPoint, "Fade-out point (s)", _fadeOutFromEnd, out _captureOut, "Capture");
        (_fadeOutLess, _fadeOutMore) = NudgeButtons(_fadeOutPoint);
        _fadeOutDuration = Field(layout, ref row, TransitionField.FadeOutDuration, "Fade-out duration (s)", null, out _, null);
        _fadeInPoint = Field(layout, ref row, TransitionField.FadeInPoint, "Fade-in point (s)", null, out _captureIn, "Capture");
        (_fadeInLess, _fadeInMore) = NudgeButtons(_fadeInPoint);
        _fadeInDuration = Field(layout, ref row, TransitionField.FadeInDuration, "Fade-in duration (s)", null, out _, null);

        _curve = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Enabled = false };
        ListCurves();
        ShowCurve(FadeCurveKind.EqualPower);
        layout.Controls.Add(new Label { Text = "Fade curve", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(_curve, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        row++;

        // Narrower than the panel with its scroll bar showing, so it never scrolls sideways.
        _save = new Button { Text = "Save", Width = 72, Height = 32, Enabled = false };
        _revert = new Button { Text = "Revert", Width = 72, Height = 32, Enabled = false };
        _remove = new Button { Text = "Remove transition", Width = 124, Height = 32, Enabled = false };
        _removeOverride = new Button { Text = "Remove override", Width = 124, Height = 32, Enabled = false, Visible = false };
        _render = new Button { Text = "Render this boundary", Width = 160, Height = 32, Enabled = false };
        _preview = new Button { Text = "Preview", Width = 110, Height = 32, Enabled = false };
        _stopPreview = new Button { Text = "Stop preview", Width = 110, Height = 32, Enabled = false };
        ButtonRow(layout, ref row, _preview, _stopPreview);
        _previewStatus = new Label { AutoSize = true, MaximumSize = new Size(300, 0), Margin = new Padding(3, 0, 3, 8), Visible = false };
        layout.Controls.Add(_previewStatus, 0, row);
        layout.SetColumnSpan(_previewStatus, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;
        ButtonRow(layout, ref row, _save, _revert, _remove);
        // Remove override sits under Remove transition in playlist mode only (D21), in Render's
        // row, so that Render stays in view at the default window size.
        ButtonRow(layout, ref row, _render, _removeOverride);

        _captureOut.Click += (_, _) => CaptureFadeOut();
        _captureIn.Click += (_, _) => CaptureFadeIn();
        _save.Click += (_, _) => Save();
        _revert.Click += (_, _) => Revert();
        _remove.Click += (_, _) => Remove();
        _removeOverride.Click += (_, _) => RemoveOverride();
        _editGlobal.CheckedChanged += (_, _) => { if (_editGlobal.Checked) SwitchLayer(TransitionLayer.Global); };
        _editPlaylist.CheckedChanged += (_, _) => { if (_editPlaylist.Checked) SwitchLayer(TransitionLayer.Playlist); };
        _render.Click += (_, _) => Render();
        _preview.Click += (_, _) => Preview();
        _stopPreview.Click += (_, _) => _engine.StopPreview();
        _fadeOutFromEnd.CheckedChanged += (_, _) => { if (!_loading) MeasureFadeOutFromOtherEnd(); };
        _fadeOutPoint.TextChanged += (_, _) => { if (_fadeOutPoint.Text != Presenter.FmtSeconds(_fadeOut)) _fadeOut = null; ShowNudges(); };
        _fadeInPoint.TextChanged += (_, _) => { if (_fadeInPoint.Text != Presenter.FmtSeconds(_fadeIn)) _fadeIn = null; ShowNudges(); };
        _fadeOutLess.Click += (_, _) => NudgePoint(fadeOut: true, -1, ShiftHeld);
        _fadeOutMore.Click += (_, _) => NudgePoint(fadeOut: true, +1, ShiftHeld);
        _fadeInLess.Click += (_, _) => NudgePoint(fadeOut: false, -1, ShiftHeld);
        _fadeInMore.Click += (_, _) => NudgePoint(fadeOut: false, +1, ShiftHeld);

        Controls.Add(scroller);
        Controls.Add(_title);
    }

    public Adjacency? Adjacency => _adjacency;

    /// <summary>A side of the open adjacency is gone, as the engine marked it (phase 3 issue 02):
    /// its transition can be removed, not changed.</summary>
    private bool Gone => _adjacency is { } a && (a.Outgoing.Gone || a.Incoming.Gone);

    /// <summary>The layer the fields edit: the one chosen, while a playlist is open.</summary>
    internal TransitionLayer Layer =>
        _layer == TransitionLayer.Playlist && _engine.View.OpenPlaylist is not null ? TransitionLayer.Playlist : TransitionLayer.Global;

    private static string PlaylistChoice(string? playlist) =>
        playlist is null ? "This playlist" : $"This playlist ({playlist})";

    /// <summary>The selector moved: unsaved edits in the layer left are settled first, as on
    /// leaving the adjacency, and Cancel puts the selector back. The fields then load the layer
    /// chosen.</summary>
    internal void SwitchLayer(TransitionLayer layer)
    {
        if (_loading || layer == _layer)
        {
            return;
        }
        if (!ConfirmLeave())
        {
            ShowLayer();
            return;
        }
        _layer = layer;
        ShowLayer();
        if (_adjacency is { } adjacency)
        {
            Load(adjacency);
        }
    }

    /// <summary>The selector, the curve list and Remove override as the layer has them.</summary>
    private void ShowLayer()
    {
        var wasLoading = _loading;
        _loading = true;
        var playlist = Layer == TransitionLayer.Playlist;
        _editGlobal.Checked = !playlist;
        _editPlaylist.Checked = playlist;
        _removeOverride.Visible = playlist;
        if (_curve.Items.Count > 0 && (_curve.Items[0] is CurveItem { Curve: null }) != playlist)
        {
            ListCurves();
        }
        _loading = wasLoading;
    }

    /// <summary>The curves the list offers: with "(global)" first in playlist mode.</summary>
    private void ListCurves()
    {
        IEnumerable<FadeCurveKind?> curves = Enum.GetValues<FadeCurveKind>().Select(c => (FadeCurveKind?)c);
        if (Layer == TransitionLayer.Playlist)
        {
            curves = curves.Prepend(null);
        }
        _curve.Items.Clear();
        _curve.Items.AddRange(curves.Select(c => (object)new CurveItem(c)).ToArray());
    }

    /// <summary>The captured fade-out point, exact, or null when the field was typed or cleared.</summary>
    internal Point? CapturedFadeOut => _fadeOut;

    /// <summary>The line beside a field that says why its value was refused; hidden otherwise.</summary>
    internal Label ReasonFor(TransitionField field) => _reasons[field];

    private TextBox Field(
        TableLayoutPanel layout, ref int row, TransitionField field, string label, Control? beside,
        out Button button, string? buttonText)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, row);
        if (beside is not null)
        {
            layout.Controls.Add(beside, 1, row);
        }
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        row++;
        var box = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(box, 0, row);
        button = new Button { Text = buttonText ?? "", Dock = DockStyle.Fill, Enabled = false, Visible = buttonText is not null };
        layout.Controls.Add(button, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row++;
        // Hidden until a value is refused; an auto-sized row with nothing visible takes no room.
        var reason = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(280, 0),
            ForeColor = Color.Firebrick,
            Visible = false,
            Margin = new Padding(3, 0, 3, 4),
        };
        layout.Controls.Add(reason, 0, row);
        layout.SetColumnSpan(reason, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        row++;
        _reasons[field] = reason;
        return box;
    }

    /// <summary>Puts − and + beside a point's box, in the box's own cell (interim transport issue
    /// 06). Points only: durations have none, by decision (D8).</summary>
    private static (Button Less, Button More) NudgeButtons(TextBox box)
    {
        var layout = (TableLayoutPanel)box.Parent!;
        var cell = layout.GetPositionFromControl(box);
        var less = new Button { Text = "−", Width = 26, Dock = DockStyle.Fill, Margin = new Padding(1, 0, 0, 0), Enabled = false };
        var more = new Button { Text = "+", Width = 26, Dock = DockStyle.Fill, Margin = new Padding(1, 0, 0, 0), Enabled = false };
        var strip = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, Margin = box.Margin, Padding = new Padding(0) };
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        strip.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 27));
        layout.Controls.Remove(box);
        box.Margin = new Padding(0, 0, 0, 0);
        strip.Controls.Add(box, 0, 0);
        strip.Controls.Add(less, 1, 0);
        strip.Controls.Add(more, 2, 0);
        layout.Controls.Add(strip, cell.Column, cell.Row);
        var tips = new ToolTip();
        tips.SetToolTip(less, "Earlier by 0.1 s (Shift: 1 s) in the number shown");
        tips.SetToolTip(more, "Later by 0.1 s (Shift: 1 s) in the number shown");
        return (less, more);
    }

    private static bool ShiftHeld => (ModifierKeys & Keys.Shift) != 0;

    /// <summary>Moves the point the field shows by a tenth of a second, or a second when
    /// <paramref name="coarse"/>, in the direction given: the number shown moves, so with From end
    /// ticked + is earlier in the track. The engine clamps it to the track; the result lands in the
    /// field as a capture does, exact. A field that is blank or not a number is left alone.</summary>
    internal void NudgePoint(bool fadeOut, int direction, bool coarse)
    {
        if (_adjacency is null || ShownPoint(fadeOut) is not { } point)
        {
            return;
        }
        var step = TimeSpan.FromSeconds(coarse ? 1 : 0.1) * Math.Sign(direction);
        var moved = _engine.Nudge(fadeOut ? _adjacency.Outgoing : _adjacency.Incoming, point, step);
        if (fadeOut)
        {
            ShowFadeOut(moved);
        }
        else
        {
            _fadeIn = moved;
            _fadeInPoint.Text = Presenter.FmtSeconds(moved);
        }
    }

    /// <summary>The point a field shows: its own, or in playlist mode, while it is empty, the
    /// global one its placeholder shows, so a nudge or a from-end tick starts from what is heard
    /// and writes the result into the override.</summary>
    private Point? ShownPoint(bool fadeOut)
    {
        var (box, captured) = fadeOut ? (_fadeOutPoint, _fadeOut) : (_fadeInPoint, _fadeIn);
        if (PointOf(box, captured).Point is { } point)
        {
            return point;
        }
        if (_adjacency is null || !ShowsPlaceholder(box))
        {
            return null;
        }
        var global = _engine.Transition(_adjacency).Global;
        return (fadeOut ? global?.FadeOutPoint : global?.FadeInPoint)?.Point;
    }

    /// <summary>An empty box showing the global value, in playlist mode.</summary>
    private static bool ShowsPlaceholder(TextBox box) => box.Text.Length == 0 && box.PlaceholderText.Length > 0;

    /// <summary>The nudge buttons live while an adjacency is open and their field shows a point,
    /// its own or the global one as a placeholder; never on a gone adjacency.</summary>
    private void ShowNudges()
    {
        var outLive = _adjacency is not null && !Gone && (PointOf(_fadeOutPoint, _fadeOut).Point is not null || ShowsPlaceholder(_fadeOutPoint));
        var inLive = _adjacency is not null && !Gone && (PointOf(_fadeInPoint, _fadeIn).Point is not null || ShowsPlaceholder(_fadeInPoint));
        _fadeOutLess.Enabled = _fadeOutMore.Enabled = outLive;
        _fadeInLess.Enabled = _fadeInMore.Enabled = inLive;
    }

    private static void ButtonRow(TableLayoutPanel layout, ref int row, params Button[] buttons)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.AddRange(buttons);
        layout.Controls.Add(flow, 0, row);
        layout.SetColumnSpan(flow, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        row++;
    }

    /// <summary>Opens the editor on an adjacency, loading what is authored for it, once any
    /// unsaved edits to the one open now are saved or discarded. Reopening the adjacency already
    /// open keeps its draft. Opening another one ends a running preview.</summary>
    public void Open(Adjacency adjacency)
    {
        if ((adjacency == _adjacency && HasUnsavedEdits) || !ConfirmLeave())
        {
            return;
        }
        if (adjacency != _adjacency)
        {
            _engine.StopPreview();
        }
        Load(adjacency);
    }

    /// <summary>Whether the editor may be left: asks Save / Discard / Cancel when there are
    /// unsaved edits, and nothing otherwise. A save that is refused stays, with its reasons shown.</summary>
    public bool ConfirmLeave()
    {
        if (!HasUnsavedEdits)
        {
            return true;
        }
        switch (_askUnsaved(_adjacency!))
        {
            case UnsavedEdits.Save:
                Save();
                return !HasUnsavedEdits;
            case UnsavedEdits.Discard:
                return true;
            default:
                return false;
        }
    }

    /// <summary>Saving would change the store: the engine says whether it holds the draft. Text
    /// that is not a number is an edit too. After Remove transition the store holds nothing, so
    /// filled fields are unsaved. On a gone adjacency nothing can be saved, so nothing is unsaved.</summary>
    internal bool HasUnsavedEdits
    {
        get
        {
            if (_adjacency is null || Gone)
            {
                return false;
            }
            var (draft, unparsed) = Draft();
            return unparsed.Count > 0 || !_engine.IsAuthored(_adjacency, draft, Layer);
        }
    }

    /// <summary>Loads the fields from the store: opening an adjacency, and Revert. A stored
    /// point loads exactly, so a draft nobody touched is the transition stored. In playlist mode
    /// the fields hold the override, and the global values are their placeholders.</summary>
    private void Load(Adjacency adjacency)
    {
        _adjacency = adjacency;
        _title.Text = adjacency.Name;
        ShowLayer();
        var layers = _engine.Transition(adjacency);
        var playlist = Layer == TransitionLayer.Playlist;
        Fill(playlist ? layers.Override : layers.Global, editing: true, playlist ? layers.Global : null);
        ShowLayers(layers);
    }

    /// <summary>What the store holds besides the fields: the global values as placeholders in
    /// playlist mode, what a blank duration plays in either mode, and the heading line when the
    /// open playlist plays something else. Read again after every save or removal, since either
    /// layer may have changed.</summary>
    private void ShowLayers(TransitionLayers layers)
    {
        var global = Layer == TransitionLayer.Playlist ? layers.Global : null;
        var open = _adjacency is not null;
        _fadeOutPoint.PlaceholderText = Presenter.FmtSeconds(global?.FadeOutPoint?.Point);
        _fadeOutDuration.PlaceholderText = open ? Presenter.FmtSeconds(layers.BlankFadeOutDuration(Layer)) : "";
        _fadeInPoint.PlaceholderText = Presenter.FmtSeconds(global?.FadeInPoint?.Point);
        _fadeInDuration.PlaceholderText = open ? Presenter.FmtSeconds(layers.BlankFadeInDuration(Layer)) : "";
        var playlist = _engine.View.OpenPlaylist;
        var differ = _adjacency is not null && playlist is not null && layers.Differ;
        _effective.Text = differ ? Presenter.FmtEffective(playlist!, layers.Effective) : "";
        _effective.Visible = differ;
        ShowNudges();
    }

    private void ShowLayers()
    {
        if (_adjacency is not null)
        {
            ShowLayers(_engine.Transition(_adjacency));
        }
    }

    /// <summary>Empties the editor, as at launch: on switching library (library issue 02), where
    /// the adjacency open belongs to the library left. Unsaved edits are the caller's to settle
    /// first, through <see cref="ConfirmLeave"/>.</summary>
    public void Close()
    {
        _adjacency = null;
        _title.Text = NoAdjacency;
        Fill(null, editing: false, global: null);
        ShowLayers(TransitionLayers.None);
    }

    private const string NoAdjacency = "No boundary selected";

    /// <summary>The fields from <paramref name="spec"/>. With <paramref name="global"/>, in playlist
    /// mode, From end follows the global point while the override has none, so the placeholder
    /// reads as the global does; and a missing curve is "(global)".</summary>
    private void Fill(TransitionSpec? spec, bool editing, TransitionSpec? global)
    {
        _loading = true;
        _fadeOutFromEnd.Checked = (spec?.FadeOutPoint ?? global?.FadeOutPoint)?.FromEnd ?? false;
        ShowCurve(Layer == TransitionLayer.Playlist ? spec?.Curve : spec?.Curve ?? FadeCurveKind.EqualPower);
        _loading = false;
        _fadeOut = spec?.FadeOutPoint?.Point;
        _fadeIn = spec?.FadeInPoint?.Point;
        _fadeOutPoint.Text = Presenter.FmtSeconds(_fadeOut);
        _fadeInPoint.Text = Presenter.FmtSeconds(_fadeIn);
        _fadeOutDuration.Text = Presenter.FmtSeconds(spec?.FadeOutDuration);
        _fadeInDuration.Text = Presenter.FmtSeconds(spec?.FadeInDuration);
        ShowReasons([]);
        // On a gone adjacency only the removals stay: the rest would act on a file not there.
        var changeable = editing && !Gone;
        foreach (var control in new Control[] { _fadeOutFromEnd, _curve, _save, _revert, _render, _preview })
        {
            control.Enabled = changeable;
        }
        _remove.Enabled = _removeOverride.Enabled = editing;
        foreach (var box in new[] { _fadeOutPoint, _fadeOutDuration, _fadeInPoint, _fadeInDuration })
        {
            box.ReadOnly = editing && !changeable;
        }
        _missing.Text = _adjacency is { } adjacency ? Presenter.FmtMissing(adjacency) : "";
        _missing.Visible = _missing.Text.Length > 0;
        ShowNudges();
    }

    /// <summary>Called on every render: the capture buttons live only while a voice is attended
    /// or a preview runs, and the preview's buttons and line follow the preview.</summary>
    public void Refresh(PlayerView view)
    {
        var choice = PlaylistChoice(view.OpenPlaylist);
        if (_editPlaylist.Text != choice)
        {
            _editPlaylist.Text = choice;
        }
        _editPlaylist.Enabled = view.OpenPlaylist is not null;
        var canCapture = _adjacency is not null && !Gone && (view.Attended is not null || view.Preview is not null);
        _captureOut.Enabled = canCapture;
        _captureIn.Enabled = canCapture;
        _preview.Text = view.Preview is null ? "Preview" : "Preview again";
        _stopPreview.Enabled = view.Preview is not null;
        _previewStatus.Text = Presenter.FmtPreview(view.Preview);
        _previewStatus.Visible = view.Preview is not null; // no room kept for it otherwise
    }

    /// <summary>A capture that captured nothing leaves the field as it was; the engine says why.</summary>
    internal void CaptureFadeOut()
    {
        if (_engine.Capture(CaptureSide.FadeOut, fromEnd: _fadeOutFromEnd.Checked) is { } point)
        {
            ShowFadeOut(point);
        }
    }

    internal void CaptureFadeIn()
    {
        if (_engine.Capture(CaptureSide.FadeIn) is { } point)
        {
            _fadeIn = point;
            _fadeInPoint.Text = Presenter.FmtSeconds(point);
        }
    }

    /// <summary>Previews the draft as it stands, or restarts a running preview with it. Text that
    /// is not a number previews nothing, as it saves nothing.</summary>
    internal void Preview()
    {
        if (_adjacency is null)
        {
            return;
        }
        if (ParsedDraft("Not previewed") is { } spec)
        {
            ShowReasons(_engine.Preview(_adjacency, spec, Layer));
        }
    }

    /// <summary>From-end ticked or unticked: the same moment, measured from the other end. The
    /// engine does the arithmetic; a field that is blank or not a number is left as it is.</summary>
    private void MeasureFadeOutFromOtherEnd()
    {
        if (_adjacency is not null && ShownPoint(fadeOut: true) is { } point)
        {
            ShowFadeOut(_engine.FromOtherEnd(_adjacency.Outgoing, point));
        }
    }

    private void ShowCurve(FadeCurveKind? curve) => _curve.SelectedIndex = _curve.Items.IndexOf(new CurveItem(curve));

    private void ShowFadeOut(Point? point)
    {
        _fadeOut = point;
        _fadeOutPoint.Text = Presenter.FmtSeconds(point);
    }

    internal void Save()
    {
        if (_adjacency is null)
        {
            return;
        }
        if (ParsedDraft("Not saved") is { } spec)
        {
            // Saved or not, the engine says which.
            ShowReasons(Layer == TransitionLayer.Playlist
                ? _engine.AuthorOverride(_adjacency, spec)
                : _engine.Author(_adjacency, spec));
            ShowLayers();
        }
    }

    /// <summary>The draft, or null when a field is not a number: then its reason is shown beside
    /// it, and the strip says the <paramref name="outcome"/> and why.</summary>
    private TransitionSpec? ParsedDraft(string outcome)
    {
        var (spec, unparsed) = Draft();
        ShowReasons(unparsed);
        if (unparsed.Count > 0)
        {
            _notice($"{outcome}: " + string.Join(" ", unparsed.Select(r => r.Reason)));
            return null;
        }
        return spec;
    }

    /// <summary>The fields as the spec Save would author, and the fields whose text is not a
    /// number of seconds.</summary>
    private (TransitionSpec Spec, IReadOnlyList<Refusal> Unparsed) Draft()
    {
        var fadeOut = PointOf(_fadeOutPoint, _fadeOut);
        var fadeIn = PointOf(_fadeInPoint, _fadeIn);
        var outDuration = Presenter.ParseSeconds(_fadeOutDuration.Text);
        var inDuration = Presenter.ParseSeconds(_fadeInDuration.Text);
        var unparsed = new (TransitionField Field, string? Error)[]
        {
            (TransitionField.FadeOutPoint, fadeOut.Error),
            (TransitionField.FadeOutDuration, outDuration.Error),
            (TransitionField.FadeInPoint, fadeIn.Error),
            (TransitionField.FadeInDuration, inDuration.Error),
        }.Where(f => f.Error is not null).Select(f => new Refusal(f.Field, f.Error!)).ToList();
        var spec = new TransitionSpec
        {
            FadeOutPoint = fadeOut.Point is { } fo ? new FadePointSpec(fo.TimeSpan, _fadeOutFromEnd.Checked) : null,
            FadeInPoint = fadeIn.Point is { } fi ? new FadePointSpec(fi.TimeSpan, FromEnd: false) : null,
            FadeOutDuration = outDuration.Point?.TimeSpan,
            FadeInDuration = inDuration.Point?.TimeSpan,
            Curve = ((CurveItem)_curve.SelectedItem!).Curve,
        };
        return (spec, unparsed);
    }

    /// <summary>Discards the draft and reloads the fields from the store.</summary>
    internal void Revert()
    {
        if (_adjacency is not null)
        {
            Load(_adjacency);
        }
    }

    /// <summary>Unauthors the adjacency and keeps the fields as an unsaved draft, so Save puts
    /// the transition back. In playlist mode it is still the global transition that goes, and
    /// the placeholders show that.</summary>
    internal void Remove()
    {
        if (_adjacency is not null)
        {
            _engine.Unauthor(_adjacency);
            ShowReasons([]);
            ShowLayers();
        }
    }

    /// <summary>Removes the open playlist's override, so it plays the global transition again,
    /// and keeps the fields as an unsaved draft, as <see cref="Remove"/> does.</summary>
    internal void RemoveOverride()
    {
        if (_adjacency is not null)
        {
            _engine.UnauthorOverride(_adjacency);
            ShowReasons([]);
            ShowLayers();
        }
    }

    private void ShowReasons(IReadOnlyList<Refusal> refusals)
    {
        foreach (var (field, label) in _reasons)
        {
            var reason = refusals.FirstOrDefault(r => r.Field == field)?.Reason;
            label.Text = reason ?? "";
            label.Visible = reason is not null;
        }
    }

    /// <summary>The captured point when the field still shows it; the typed text otherwise.</summary>
    private static SecondsField PointOf(TextBox box, Point? captured) =>
        captured is { } p && box.Text == Presenter.FmtSeconds(p)
            ? new SecondsField(p, null)
            : Presenter.ParseSeconds(box.Text);

    internal void Render()
    {
        if (_adjacency is null)
        {
            return;
        }
        var suggested = Presenter.RenderFileName(_adjacency);
        var path = _chooseRenderPath(suggested);
        if (path is null)
        {
            return;
        }
        try
        {
            var report = _engine.RenderBoundary(_adjacency, path);
            _notice($"Rendered {Presenter.Fmt(report.Rendered)} to {Path.GetFileName(report.Path)}; " +
                    $"the fade-out point is at {Presenter.FmtSeconds(Point.FromSeconds(report.FadeOutPointInFile.TotalSeconds))} s.");
        }
        catch (Exception e)
        {
            _notice("Render failed: " + e.Message);
        }
    }
}
