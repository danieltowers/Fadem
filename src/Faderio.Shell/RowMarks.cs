using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// How a queue marks its attended row, the same in both hosts (phase 5 issue 10): a speaker in
/// a column of its own at the far left, sounding while it plays or is paused, and at rest while nothing
/// sounds (interim transport 03). The row itself stays the window's white; selected rows are a
/// darker grey than the window, not the system's blue.
/// </summary>
internal static class RowMarks
{
    public const string Sounding = "🔊";

    public const string Resting = "🔈";

    /// <summary>A selected row's background.</summary>
    public static readonly Color Selected = Color.FromArgb(200, 200, 200);

    /// <summary>A row's mark: the speaker for the attended row, sounding or at rest; none for the others.</summary>
    public static string? Of(bool isCurrent, Transport transport) =>
        !isCurrent ? null : transport == Transport.Stopped ? Resting : Sounding;
}
