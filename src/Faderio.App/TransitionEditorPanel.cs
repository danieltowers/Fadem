using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App;

/// <summary>
/// The transition editor for one adjacency: four parameter fields, two capture buttons, a
/// from-end box on the fade-out point, the fade curve, Save, Revert, Remove transition and
/// "Render this boundary" (ticket 05's slice, completed by build issue 14). Holds the two
/// captured points as <see cref="Point"/>s, so the text boxes are a display of the value, never
/// its source of truth: v2 formatted the capture with "0.###" before saving it and quantised
/// every point to a millisecond (ticket 06). The fields are a draft; the engine converts,
/// captures and validates, and each value it refuses is shown beside its field.
/// </summary>
internal sealed class TransitionEditorPanel : Panel
{
    private readonly PlaybackEngine _engine;
    private readonly ITransitionStore _store;
    private readonly Action<string> _notice;
    private readonly Func<string, string?> _chooseRenderPath;

    internal readonly Label _title;
    internal readonly TextBox _fadeOutPoint;
    internal readonly CheckBox _fadeOutFromEnd;
    internal readonly TextBox _fadeOutDuration;
    internal readonly TextBox _fadeInPoint;
    internal readonly TextBox _fadeInDuration;
    internal readonly ComboBox _curve;
    internal readonly Button _captureOut;
    internal readonly Button _captureIn;
    internal readonly Button _save;
    internal readonly Button _revert;
    internal readonly Button _remove;
    internal readonly Button _render;
    private readonly Dictionary<TransitionField, Label> _reasons = new();

    private Adjacency? _adjacency;
    private Point? _fadeOut;
    private Point? _fadeIn;
    private bool _loading;

    /// <summary>A curve as the list shows it.</summary>
    private sealed record CurveItem(FadeCurveKind Curve)
    {
        public override string ToString() => Presenter.FmtCurve(Curve);
    }

    public TransitionEditorPanel(
        PlaybackEngine engine, ITransitionStore store, Action<string> notice,
        Func<string, string?> chooseRenderPath)
    {
        _engine = engine;
        _store = store;
        _notice = notice;
        _chooseRenderPath = chooseRenderPath;

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
            Text = "No boundary selected",
            Dock = DockStyle.Top,
            Height = 44,
            Font = new Font(Font, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
        };

        _fadeOutFromEnd = new CheckBox { Text = "From end", Dock = DockStyle.Fill, Enabled = false };
        var row = 0;
        _fadeOutPoint = Field(layout, ref row, TransitionField.FadeOutPoint, "Fade-out point (s)", _fadeOutFromEnd, out _captureOut, "Capture");
        _fadeOutDuration = Field(layout, ref row, TransitionField.FadeOutDuration, "Fade-out duration (s)", null, out _, null);
        _fadeInPoint = Field(layout, ref row, TransitionField.FadeInPoint, "Fade-in point (s)", null, out _captureIn, "Capture");
        _fadeInDuration = Field(layout, ref row, TransitionField.FadeInDuration, "Fade-in duration (s)", null, out _, null);

        _curve = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill, Enabled = false };
        _curve.Items.AddRange(Enum.GetValues<FadeCurveKind>().Select(c => (object)new CurveItem(c)).ToArray());
        ShowCurve(FadeCurveKind.EqualPower);
        layout.Controls.Add(new Label { Text = "Fade curve", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        layout.Controls.Add(_curve, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        row++;

        // Narrower than the panel with its scroll bar showing, so it never scrolls sideways.
        _save = new Button { Text = "Save", Width = 72, Height = 32, Enabled = false };
        _revert = new Button { Text = "Revert", Width = 72, Height = 32, Enabled = false };
        _remove = new Button { Text = "Remove transition", Width = 124, Height = 32, Enabled = false };
        _render = new Button { Text = "Render this boundary", Width = 160, Height = 32, Enabled = false };
        ButtonRow(layout, ref row, _save, _revert, _remove);
        ButtonRow(layout, ref row, _render);

        _captureOut.Click += (_, _) => CaptureFadeOut();
        _captureIn.Click += (_, _) => CaptureFadeIn();
        _save.Click += (_, _) => Save();
        _revert.Click += (_, _) => Revert();
        _remove.Click += (_, _) => Remove();
        _render.Click += (_, _) => Render();
        _fadeOutFromEnd.CheckedChanged += (_, _) => { if (!_loading) MeasureFadeOutFromOtherEnd(); };
        _fadeOutPoint.TextChanged += (_, _) => { if (_fadeOutPoint.Text != Presenter.FmtSeconds(_fadeOut)) _fadeOut = null; };
        _fadeInPoint.TextChanged += (_, _) => { if (_fadeInPoint.Text != Presenter.FmtSeconds(_fadeIn)) _fadeIn = null; };

        Controls.Add(scroller);
        Controls.Add(_title);
    }

    public Adjacency? Adjacency => _adjacency;

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

    private static void ButtonRow(TableLayoutPanel layout, ref int row, params Button[] buttons)
    {
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        flow.Controls.AddRange(buttons);
        layout.Controls.Add(flow, 0, row);
        layout.SetColumnSpan(flow, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        row++;
    }

    /// <summary>Opens the editor on an adjacency, loading what is authored for it.</summary>
    public void Edit(Adjacency adjacency)
    {
        _adjacency = adjacency;
        _title.Text = adjacency.Name;
        var spec = _engine.Transition(adjacency);
        _loading = true;
        _fadeOutFromEnd.Checked = spec?.FadeOutPoint?.FromEnd ?? false;
        ShowCurve(spec?.Curve ?? FadeCurveKind.EqualPower);
        _loading = false;
        _fadeOut = spec?.FadeOutPoint is { } fo ? Point.FromSeconds(fo.Position.TotalSeconds) : null;
        _fadeIn = spec?.FadeInPoint is { } fi ? Point.FromSeconds(fi.Position.TotalSeconds) : null;
        _fadeOutPoint.Text = Presenter.FmtSeconds(_fadeOut);
        _fadeInPoint.Text = Presenter.FmtSeconds(_fadeIn);
        _fadeOutDuration.Text = Presenter.FmtSeconds(spec?.FadeOutDuration);
        _fadeInDuration.Text = Presenter.FmtSeconds(spec?.FadeInDuration);
        ShowReasons([]);
        foreach (var control in new Control[] { _fadeOutFromEnd, _curve, _save, _revert, _remove, _render })
        {
            control.Enabled = true;
        }
    }

    /// <summary>Called on every render: the capture buttons live only while a voice is attended.</summary>
    public void Refresh(PlayerView view)
    {
        var canCapture = _adjacency is not null && view.Attended is not null;
        _captureOut.Enabled = canCapture;
        _captureIn.Enabled = canCapture;
    }

    internal void CaptureFadeOut() => ShowFadeOut(_engine.Capture(fromEnd: _fadeOutFromEnd.Checked));

    internal void CaptureFadeIn()
    {
        _fadeIn = _engine.Capture();
        _fadeInPoint.Text = Presenter.FmtSeconds(_fadeIn);
    }

    /// <summary>From-end ticked or unticked: the same moment, measured from the other end. The
    /// engine does the arithmetic; a field that is blank or not a number is left as it is.</summary>
    private void MeasureFadeOutFromOtherEnd()
    {
        if (_adjacency is not null && PointOf(_fadeOutPoint, _fadeOut).Point is { } point)
        {
            ShowFadeOut(_engine.FromOtherEnd(_adjacency.Outgoing, point));
        }
    }

    private void ShowCurve(FadeCurveKind curve) => _curve.SelectedIndex = _curve.Items.IndexOf(new CurveItem(curve));

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
        ShowReasons(unparsed);
        if (unparsed.Count > 0)
        {
            _notice("Not saved: " + string.Join(" ", unparsed.Select(r => r.Reason)));
            return;
        }
        var spec = new TransitionSpec
        {
            FadeOutPoint = fadeOut.Point is { } fo ? new FadePointSpec(fo.TimeSpan, _fadeOutFromEnd.Checked) : null,
            FadeInPoint = fadeIn.Point is { } fi ? new FadePointSpec(fi.TimeSpan, FromEnd: false) : null,
            FadeOutDuration = outDuration.Point?.TimeSpan,
            FadeInDuration = inDuration.Point?.TimeSpan,
            Curve = ((CurveItem)_curve.SelectedItem!).Curve,
        };
        ShowReasons(_engine.Author(_adjacency, spec)); // saved or not, the engine says which
    }

    /// <summary>Discards the draft and reloads the fields from the store.</summary>
    internal void Revert()
    {
        if (_adjacency is not null)
        {
            Edit(_adjacency);
        }
    }

    /// <summary>Unauthors the adjacency and keeps the fields as an unsaved draft, so Save puts
    /// the transition back.</summary>
    internal void Remove()
    {
        if (_adjacency is not null)
        {
            _engine.Unauthor(_adjacency);
            ShowReasons([]);
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
            var report = Renderer.RenderBoundary(_adjacency, _store, path);
            _notice($"Rendered {Presenter.Fmt(report.Rendered)} to {Path.GetFileName(report.Path)}; " +
                    $"the fade-out point is at {Presenter.FmtSeconds(Point.FromSeconds(report.FadeOutPointInFile.TotalSeconds))} s.");
        }
        catch (Exception e)
        {
            _notice("Render failed: " + e.Message);
        }
    }
}
