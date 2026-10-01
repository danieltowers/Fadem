using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// How a queue marks its attended row, the same in both hosts (phase 5 issue 10): the sounding
/// mark while it plays, and at rest while nothing sounds (interim transport 03), the sounding
/// mark's hue halfway to the window's grey, so it reads as the same mark, at rest.
/// </summary>
internal static class RowMarks
{
    public static readonly Color Sounding = SystemColors.Info;

    public static readonly Color Resting = Color.FromArgb(
        (SystemColors.Info.R + SystemColors.Control.R) / 2,
        (SystemColors.Info.G + SystemColors.Control.G) / 2,
        (SystemColors.Info.B + SystemColors.Control.B) / 2);

    /// <summary>A row's background: marked when it is the attended row, at rest while stopped.</summary>
    public static Color BackOf(bool isCurrent, Transport transport) =>
        !isCurrent ? SystemColors.Window : transport == Transport.Stopped ? Resting : Sounding;
}
