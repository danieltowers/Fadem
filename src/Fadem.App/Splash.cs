using System.Diagnostics;
using Fadem.Shell;
using Fadem.Shell.Theme;

namespace Fadem.App;

/// <summary>
/// The App's splash (2026-10-11), as MusicBee shows one while it loads: the logo card with the
/// version below the logo and the copyright under it, the About box's (2026-10-13). Shown once the library is known, so never over the library chooser,
/// and closed when the main window appears. It stays up for <see cref="MinimumShown"/> at the
/// least, so a quick launch shows it rather than flashing it.
/// </summary>
internal sealed class Splash : LogoCard
{
    /// <summary>The least time it is up for.</summary>
    public static readonly TimeSpan MinimumShown = TimeSpan.FromSeconds(1.2);

    /// <summary>Its size at 100 % display scaling, in pixels.</summary>
    private static readonly Size LogicalSize = new(520, 300);

    private readonly Stopwatch _up = new();

    /// <param name="informationalVersion">The App's informational version, as the build stamps it.</param>
    public Splash(Palette palette, string? informationalVersion)
        : base(palette, LogicalSize, new[] { (VersionLine(informationalVersion), palette.Dim), (AboutBox.Copyright, palette.Dim) })
    {
        StartPosition = FormStartPosition.CenterScreen;
    }

    /// <summary>Shows it and paints it at once: the UI thread goes on loading, with no message
    /// loop to paint it until the main window runs one.</summary>
    public void ShowNow()
    {
        Show();
        Update();
        _up.Start();
    }

    /// <summary>Waits out what is left of <see cref="MinimumShown"/>, just before the main window
    /// shows.</summary>
    public void WaitOutMinimum()
    {
        var left = MinimumShown - _up.Elapsed;
        if (left > TimeSpan.Zero)
        {
            Thread.Sleep(left);
        }
    }
}
