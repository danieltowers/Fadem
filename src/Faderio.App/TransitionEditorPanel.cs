using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App;

/// <summary>
/// The transition editor for one adjacency: four parameter fields, two capture buttons, Save and
/// one "Render this boundary" button (ticket 05's slice; nothing from its deferral list). Holds
/// the two captured points as <see cref="Point"/>s, so the text boxes are a display of the
/// value, never its source of truth: v2 formatted the capture with "0.###" before saving it and
/// quantised every point to a millisecond (ticket 06).
/// </summary>
internal sealed class TransitionEditorPanel : Panel
{
    private readonly PlaybackEngine _engine;
    private readonly ITransitionStore _store;
    private readonly Action<string> _notice;
    private readonly Func<string, string?> _chooseRenderPath;

    internal readonly Label _title;
    internal readonly TextBox _fadeOutPoint;
    internal readonly TextBox _fadeOutDuration;
    internal readonly TextBox _fadeInPoint;
    internal readonly TextBox _fadeInDuration;
    internal readonly Button _captureOut;
    internal readonly Button _captureIn;
    internal readonly Button _save;
    internal readonly Button _render;

    private Adjacency? _adjacency;
    private Point? _fadeOut;
    private Point? _fadeIn;

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

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = false,
        };
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

        var row = 0;
        _fadeOutPoint = Field(layout, ref row, "Fade-out point (s)", out _captureOut, "Capture");
        _fadeOutDuration = Field(layout, ref row, "Fade-out duration (s)", out _, null);
        _fadeInPoint = Field(layout, ref row, "Fade-in point (s)", out _captureIn, "Capture");
        _fadeInDuration = Field(layout, ref row, "Fade-in duration (s)", out _, null);

        _save = new Button { Text = "Save", Width = 120, Height = 32, Enabled = false };
        _render = new Button { Text = "Render this boundary", Width = 160, Height = 32, Enabled = false };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, Height = 44, WrapContents = false };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(_render);
        layout.Controls.Add(buttons, 0, row);
        layout.SetColumnSpan(buttons, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        _captureOut.Click += (_, _) => CaptureFadeOut();
        _captureIn.Click += (_, _) => CaptureFadeIn();
        _save.Click += (_, _) => Save();
        _render.Click += (_, _) => Render();
        _fadeOutPoint.TextChanged += (_, _) => { if (_fadeOutPoint.Text != Presenter.FmtSeconds(_fadeOut)) _fadeOut = null; };
        _fadeInPoint.TextChanged += (_, _) => { if (_fadeInPoint.Text != Presenter.FmtSeconds(_fadeIn)) _fadeIn = null; };

        Controls.Add(layout);
        Controls.Add(_title);
    }

    public Adjacency? Adjacency => _adjacency;

    /// <summary>The captured fade-out point, exact, or null when the field was typed or cleared.</summary>
    internal Point? CapturedFadeOut => _fadeOut;

    private static TextBox Field(TableLayoutPanel layout, ref int row, string label, out Button button, string? buttonText)
    {
        layout.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft }, 0, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        row++;
        var box = new TextBox { Dock = DockStyle.Fill };
        layout.Controls.Add(box, 0, row);
        button = new Button { Text = buttonText ?? "", Dock = DockStyle.Fill, Enabled = false, Visible = buttonText is not null };
        layout.Controls.Add(button, 1, row);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        row++;
        return box;
    }

    /// <summary>Opens the editor on an adjacency, loading what is authored for it.</summary>
    public void Edit(Adjacency adjacency)
    {
        _adjacency = adjacency;
        _title.Text = adjacency.Name;
        var spec = _engine.Transition(adjacency);
        _fadeOut = spec?.FadeOutPoint is { FromEnd: false } fo ? Point.FromSeconds(fo.Position.TotalSeconds) : null;
        _fadeIn = spec?.FadeInPoint is { } fi ? Point.FromSeconds(fi.Position.TotalSeconds) : null;
        _fadeOutPoint.Text = Presenter.FmtSeconds(_fadeOut);
        _fadeInPoint.Text = Presenter.FmtSeconds(_fadeIn);
        _fadeOutDuration.Text = Presenter.FmtSeconds(spec?.FadeOutDuration);
        _fadeInDuration.Text = Presenter.FmtSeconds(spec?.FadeInDuration);
        _save.Enabled = true;
        _render.Enabled = true;
    }

    /// <summary>Called on every render: the capture buttons live only while a voice is attended.</summary>
    public void Refresh(PlayerView view)
    {
        var canCapture = _adjacency is not null && view.Attended is not null;
        _captureOut.Enabled = canCapture;
        _captureIn.Enabled = canCapture;
    }

    internal void CaptureFadeOut()
    {
        _fadeOut = _engine.Capture();
        _fadeOutPoint.Text = Presenter.FmtSeconds(_fadeOut);
    }

    internal void CaptureFadeIn()
    {
        _fadeIn = _engine.Capture();
        _fadeInPoint.Text = Presenter.FmtSeconds(_fadeIn);
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
        var error = fadeOut.Error ?? fadeIn.Error ?? outDuration.Error ?? inDuration.Error;
        if (error is not null)
        {
            _notice("Not saved: " + error);
            return;
        }
        var spec = new TransitionSpec
        {
            FadeOutPoint = fadeOut.Point is { } fo ? new FadePointSpec(fo.TimeSpan, FromEnd: false) : null,
            FadeInPoint = fadeIn.Point is { } fi ? new FadePointSpec(fi.TimeSpan, FromEnd: false) : null,
            FadeOutDuration = outDuration.Point?.TimeSpan,
            FadeInDuration = inDuration.Point?.TimeSpan,
        };
        _engine.Author(_adjacency, spec); // saved or not, the engine says which
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
