using System.Text.Json;
using System.Text.Json.Serialization;
using Fadem.Core.Persistence;

namespace Fadem.App;

/// <summary>
/// The main window's place on screen (library issue 03, D11): its normal bounds and whether it
/// was maximised, in the machine's <c>window.json</c>. Shell state, not the engine's, so the shell
/// owns the file: written when the window closes, read when it opens. A file that is missing,
/// unreadable or of another shape is no geometry, and neither is one that would put the window
/// off every screen: the window then opens at its default size, centred. None of it is worth a
/// notice, so none is raised.
/// </summary>
internal sealed record WindowGeometry(int X, int Y, int Width, int Height, bool Maximised)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>How much of the title bar must be on a screen for the window to count as on
    /// it: enough to see it and drag it back.</summary>
    private const int TitleBarHeight = 30;
    private const int GrabbableWidth = 100;

    [JsonIgnore]
    public Rectangle Bounds => new(X, Y, Width, Height);

    /// <summary>Where <paramref name="form"/> is now: its normal bounds, even while maximised or
    /// minimised, so a restore after the next launch lands where it was.</summary>
    public static WindowGeometry Of(Form form)
    {
        var bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
        return new(bounds.X, bounds.Y, bounds.Width, bounds.Height, form.WindowState == FormWindowState.Maximized);
    }

    /// <summary>The geometry in <paramref name="path"/>, or null when there is none to use.</summary>
    public static WindowGeometry? Load(string path)
    {
        try
        {
            var geometry = JsonSerializer.Deserialize<WindowGeometry>(File.ReadAllText(path), JsonOptions);
            return geometry is { Width: > 0, Height: > 0 } ? geometry : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>Writes the geometry atomically. A write that fails is dropped: the next launch
    /// opens where the last good write said, or centred.</summary>
    public void Save(string path)
    {
        try
        {
            AtomicJson.Write(path, this, JsonOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Whether enough of the title bar lies on one of <paramref name="workingAreas"/> to
    /// see and grab: a screen since unplugged or rearranged leaves a window where none can.</summary>
    public bool IsOnScreen(IEnumerable<Rectangle> workingAreas)
    {
        var titleBar = new Rectangle(X, Y, Width, TitleBarHeight);
        return workingAreas.Any(area =>
        {
            var visible = Rectangle.Intersect(area, titleBar);
            return visible.Width >= Math.Min(GrabbableWidth, Width) && visible.Height >= TitleBarHeight / 2;
        });
    }
}
