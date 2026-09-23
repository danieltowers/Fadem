using System.Globalization;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
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
/// by build issue 01:
/// format and parse seconds, row → adjacency with its bounds check, the add's progress, the
/// store open/recovery path, and the launch-time output mode. The add's outcome is an engine
/// notice since build issue 01, so its summary moved to <see cref="AddReport"/>. Everything here formats engine
/// values or parses text; nothing computes a position.
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

    public static string Describe(Adjacency adjacency) =>
        $"{Path.GetFileNameWithoutExtension(adjacency.Outgoing.FilePath)} → " +
        $"{Path.GetFileNameWithoutExtension(adjacency.Incoming.FilePath)}";

    /// <summary>The running add's progress for the status strip; blank when nothing is being
    /// added. The add's outcome arrives later as a notice, which the engine words.</summary>
    public static string FmtAdding(AddProgress? adding) =>
        adding is null ? "" : $"Adding {adding.Resolved} of {adding.Total}…";

    public static string PlayPauseGlyph(Transport transport) => transport == Transport.Playing ? "⏸" : "▶";

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
    /// aside, so recovery is visible on disk too.</summary>
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
            warn(e.Message + " The corrupt file is moved aside and a fresh store is used.");
            try
            {
                File.Move(path, path + ".corrupt", overwrite: true);
            }
            catch (IOException)
            {
            }
            return new JsonTransitionStore(path);
        }
    }
}
