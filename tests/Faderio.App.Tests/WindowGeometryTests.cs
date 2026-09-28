using Faderio.App;
using Faderio.Core.Tests;

namespace Faderio.App.Tests;

/// <summary>
/// Library issue 03: window.json is the shell's. A saved place counts only while enough of its
/// title bar is on a screen to see and drag; the file round-trips, and a bad one is no geometry.
/// </summary>
public sealed class WindowGeometryTests : IDisposable
{
    private static readonly Rectangle[] OneScreen = { new(0, 0, 1920, 1040) };
    private static readonly Rectangle[] TwoScreens = { new(0, 0, 1920, 1040), new(1920, 0, 2560, 1400) };
    private readonly string _dir = Path.Combine(TestTemp.Root, "geometry", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Theory]
    [InlineData(100, 100, true)]      // well inside
    [InlineData(2500, 200, true)]     // on the second screen
    [InlineData(1870, 200, true)]     // across the join, mostly on the second
    [InlineData(-900, 200, true)]     // hanging off the left, 140 px of title bar showing
    [InlineData(-980, 200, false)]    // hanging off the left, only 60 px showing
    [InlineData(5000, 200, false)]    // right of every screen: a monitor since unplugged
    [InlineData(100, -25, false)]     // title bar above the top edge
    [InlineData(100, 1030, false)]    // below the taskbar line, 10 px showing
    public void IsOnScreen_WhenEnoughTitleBarIsOnAScreenToGrab(int x, int y, bool expected)
    {
        var geometry = new WindowGeometry(x, y, 1040, 600, Maximised: false);
        Assert.Equal(expected, geometry.IsOnScreen(TwoScreens));
    }

    [Fact]
    public void AWindowPartlyOffTheOnlyScreen_WithTooLittleShowing_IsOff()
    {
        Assert.False(new WindowGeometry(1860, 100, 1040, 600, Maximised: false).IsOnScreen(OneScreen));
        Assert.True(new WindowGeometry(1800, 100, 1040, 600, Maximised: false).IsOnScreen(OneScreen));
    }

    [Fact]
    public void TheFile_RoundTrips()
    {
        var path = Path.Combine(_dir, "window.json");
        var geometry = new WindowGeometry(12, 34, 1100, 700, Maximised: true);

        geometry.Save(path);

        Assert.Equal(geometry, WindowGeometry.Load(path));
        var json = File.ReadAllText(path);
        Assert.Contains("\"maximised\": true", json);
        Assert.DoesNotContain("bounds", json); // the five values, nothing derived from them
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{ not json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"x\": 1, \"y\": 2, \"width\": 0, \"height\": 600}")]
    public void AMissingOrBadFile_IsNoGeometry(string? contents)
    {
        var path = Path.Combine(_dir, "window.json");
        if (contents is not null)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(path, contents);
        }

        Assert.Null(WindowGeometry.Load(path));
    }
}
