namespace Fadem.Shell;

/// <summary>
/// The Fadem icon every Fadem window wears, on both hosts (2026-10-11): in the title bar, on the
/// taskbar and in Alt+Tab. Embedded in the shell, as the logo is, so the plugin's windows wear it
/// too rather than MusicBee's or WinForms' own. Made from assets/logo-mark-color.png, the logo's mark, by
/// assets/make-icon.ps1, which also gives the standalone exe and the installer the same file.
/// </summary>
internal static class AppIcon
{
    private static readonly Lazy<Icon> Loaded = new(Load);

    /// <summary>One instance for every window: a form never disposes the icon it is given, so
    /// sharing it costs nothing and loads the file once.</summary>
    public static Icon Shared => Loaded.Value;

    private static Icon Load()
    {
        const string name = "Fadem.Shell.fadem.ico";
        using var stream = typeof(AppIcon).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"The icon {name} is not embedded.");
        return new Icon(stream); // reads the whole file, so the stream may go
    }
}
