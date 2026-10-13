using System.Diagnostics;
using System.Reflection;
using Fadem.Shell.Theme;

namespace Fadem.Shell;

/// <summary>
/// About Fadem (2026-10-13), opened by a click on the logo in either host: the splash's card with
/// the version, the copyright and the author's handle, which opens the profile on X. A click anywhere
/// else, Esc, Enter, or switching away closes it.
/// </summary>
internal sealed class AboutBox : LogoCard
{
    /// <summary>"© *this year* Daniel Towers": the year is the one it is opened in.</summary>
    public static string Copyright => $"© {DateTime.Now.Year} Daniel Towers";
    public const string TwitterHandle = "@mrdanieltowers";
    public const string HandleUrl = "https://x.com/mrdanieltowers";

    /// <summary>Its size at 100 % display scaling, in pixels: the splash's, and room for two lines more.</summary>
    private static readonly Size LogicalSize = new(520, 340);

    private readonly int _handleLine;

    /// <param name="informationalVersion">The host's informational version, as the build stamps it.</param>
    public AboutBox(Palette palette, string? informationalVersion)
        : base(palette, LogicalSize, new[]
        {
            (VersionLine(informationalVersion), palette.Dim),
            (Copyright, palette.Dim),
            (TwitterHandle, palette.Accent),
        })
    {
        _handleLine = Lines.Count() - 1;
        Text = "About Fadem";
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
    }

    /// <summary>Shows it over <paramref name="owner"/> until it is closed, with the version
    /// <paramref name="host"/>'s build stamped.</summary>
    public static void Open(IWin32Window? owner, Palette palette, Assembly host)
    {
        using var about = new AboutBox(palette,
            host.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        about.ShowDialog(owner);
    }

    /// <summary>Where the handle is drawn: a click there opens <see cref="HandleUrl"/>.</summary>
    public Rectangle HandleBounds => LineBounds(_handleLine);

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = HandleBounds.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && HandleBounds.Contains(e.Location))
        {
            OpenHandle();
        }
        Close();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Escape or Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            Close();
        }
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    private static void OpenHandle()
    {
        try
        {
            Process.Start(new ProcessStartInfo(HandleUrl) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No browser to open it in: the handle is still there to read.
        }
    }
}
