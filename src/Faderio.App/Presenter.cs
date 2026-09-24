using System.Globalization;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App;

/// <summary>What a typed seconds field says: unset, a point, or an error the user must fix.
/// v2 returned null for "", "abc" and "-5" alike and silently saved nothing.</summary>
internal sealed record SecondsField(Point? Point, string? Error)
{
    public bool IsUnset => Point is null && Error is null;
}

/// <summary>
/// The shell's remaining logic, with no WinForms in it (ADR 0002, S4). Ticket 06's list, amended
/// by build issues 01, 07 and 09: format and parse seconds, row → adjacency with its bounds
/// check, the add's progress, the store open/recovery path, the launch-time output mode, the
/// repeat button's cycle, the notice strip's wording and the render's file name. The add's
/// outcome is an engine notice since build issue 01, so its summary moved to
/// <see cref="AddReport"/>. Everything here formats engine values, parses text, or picks the next
/// state of a display control (ADR 0003's addendum); nothing computes a position.
/// </summary>
internal static class Presenter
{
    /// <summary>The transport readout: whole seconds, "m:ss".</summary>
    public static string Fmt(TimeSpan time) =>
        time < TimeSpan.Zero ? "0:00" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    public static string Fmt(Playhead playhead) => Fmt(playhead.TimeSpan);

    public static string Fmt(Length length) => Fmt(length.TimeSpan);

    /// <summary>Remaining time rounds up: it reads "0:01" until the track is actually over, never
    /// "0:00" while sound is still playing.</summary>
    public static string FmtRemaining(Length remaining) =>
        Fmt(TimeSpan.FromSeconds(Math.Ceiling(remaining.TimeSpan.TotalSeconds - 1e-9)));

    /// <summary>A point as the editor shows it. Display only: the captured <see cref="Point"/>
    /// is what gets saved, so this rounding never reaches the store (ticket 06).</summary>
    public static string FmtSeconds(Point? point) =>
        point is null ? "" : point.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    public static string FmtSeconds(TimeSpan? time) =>
        time is null ? "" : time.Value.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture);

    /// <summary>Typed text becomes a point through <see cref="Point.FromSeconds"/> and nowhere else.</summary>
    public static SecondsField ParseSeconds(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new SecondsField(null, null);
        }
        if (!double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) &&
            !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out seconds))
        {
            return new SecondsField(null, $"\"{text.Trim()}\" is not a number of seconds.");
        }
        if (seconds < 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return new SecondsField(null, "Seconds cannot be negative.");
        }
        return new SecondsField(Point.FromSeconds(seconds), null);
    }

    /// <summary>The adjacency whose outgoing track is the row at <paramref name="rowIndex"/>, or
    /// null for the last row and for anything out of range (v2's EditTransitionAt had no check).</summary>
    public static Adjacency? AdjacencyAt(PlayerView view, int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= view.Queue.Count - 1)
        {
            return null;
        }
        return new Adjacency(view.Queue[rowIndex].Track, view.Queue[rowIndex + 1].Track);
    }

    /// <summary>The file name the render dialog suggests: "outgoing - incoming boundary.wav".</summary>
    public static string RenderFileName(Adjacency adjacency) =>
        $"{Path.GetFileNameWithoutExtension(adjacency.Outgoing.FilePath)} - " +
        $"{Path.GetFileNameWithoutExtension(adjacency.Incoming.FilePath)} boundary.wav";

    /// <summary>The running add's progress for the status strip; blank when nothing is being
    /// added. The add's outcome arrives later as a notice, which the engine words.</summary>
    public static string FmtAdding(AddProgress? adding) =>
        adding is null ? "" : $"Adding {adding.Resolved} of {adding.Total}…";

    /// <summary>A notice as the strip and the list show it: the message, and "×n" once it has
    /// been delivered more than once.</summary>
    public static string FmtNotice(NoticeEntry entry) =>
        entry.Count > 1 ? $"{entry.Message} {FmtRepeats(entry)}" : entry.Message;

    /// <summary>The list's count column: blank for a notice delivered once.</summary>
    public static string FmtRepeats(NoticeEntry entry) => entry.Count > 1 ? $"×{entry.Count}" : "";

    /// <summary>The strip's count of this session's notices, as the list holds them: a repeat
    /// is one entry. Blank when there are none.</summary>
    public static string FmtNoticeCount(int entries) => entries switch
    {
        0 => "",
        1 => "1 notice",
        _ => $"{entries} notices",
    };

    public static string PlayPauseGlyph(Transport transport) => transport == Transport.Playing ? "⏸" : "▶";

    /// <summary>The mode a press of the repeat button asks for: off → all → one → off. A display
    /// choice over the enum (build issue 07), so it lives here, not in the engine.</summary>
    public static RepeatMode NextRepeat(RepeatMode shown) => shown switch
    {
        RepeatMode.Off => RepeatMode.All,
        RepeatMode.All => RepeatMode.One,
        _ => RepeatMode.Off,
    };

    /// <summary>Off and all share a glyph and differ by the button being pressed in. Repeat-one
    /// adds a plain "1": the "1" inside 🔂 cannot be read at button size.</summary>
    public static string RepeatGlyph(RepeatMode mode) => mode == RepeatMode.One ? "🔁1" : "🔁";

    public static string RepeatTip(RepeatMode mode) => mode switch
    {
        RepeatMode.All => "Repeat: all",
        RepeatMode.One => "Repeat: one",
        _ => "Repeat: off",
    };

    public static string ShuffleTip(bool shuffle) => shuffle ? "Shuffle: on" : "Shuffle: off";

    /// <summary>"--output=wasapi-shared|wasapi-exclusive|waveout"; shared by default (ticket 05:
    /// selectable by argument, no settings dialog).</summary>
    public static AudioOutputMode ParseOutputMode(IEnumerable<string> args)
    {
        foreach (var arg in args)
        {
            if (!arg.StartsWith("--output=", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            return arg["--output=".Length..].ToLowerInvariant() switch
            {
                "wasapi-exclusive" or "exclusive" => AudioOutputMode.WasapiExclusive,
                "waveout" => AudioOutputMode.WaveOut,
                _ => AudioOutputMode.WasapiShared,
            };
        }
        return AudioOutputMode.WasapiShared;
    }

    /// <summary>Opens the transition store; a corrupt or newer-version file is reported visibly
    /// and playback continues with a fresh store, never silently. The corrupt file is moved
    /// aside, so recovery is visible on disk too. When it cannot be moved, it is left where it
    /// is and the fresh store is written beside it, as for a newer version.</summary>
    public static ITransitionStore OpenTransitionStore(string path, Action<string> warn)
    {
        try
        {
            return new JsonTransitionStore(path);
        }
        catch (TransitionStoreVersionException e)
        {
            warn(e.Message + " A fresh store will be used; your file is left untouched.");
            return new JsonTransitionStore(path + ".recovered");
        }
        catch (InvalidDataException e)
        {
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (Exception moveFailure) when (moveFailure is IOException or UnauthorizedAccessException)
            {
                warn(e.Message + $" It could not be moved aside ({moveFailure.Message}), so it is left " +
                    $"untouched and a fresh store is used in {Path.GetFileName(path)}.recovered.");
                return new JsonTransitionStore(path + ".recovered");
            }
            warn(e.Message + $" It was moved aside to {Path.GetFileName(path)}.corrupt and a fresh store is used.");
            return new JsonTransitionStore(path);
        }
    }
}
