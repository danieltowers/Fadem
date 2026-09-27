using System.Globalization;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;

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
/// by build issues 01, 07, 09, 12, 14, 15 and 17: format and parse seconds, name the fade curves,
/// the add's progress, the store open/recovery path, the output modes' names, the repeat button's
/// cycle, the notice strip's wording, the clip light's wording and the render's file name.
/// Row → adjacency went to the engine in build issue 15. The add's outcome is an engine notice
/// since build issue 01, so its summary moved to <see cref="AddReport"/>. Everything here formats
/// engine values, parses text, or picks the next state of a display control (ADR 0003's
/// addendum); nothing computes a position.
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

    /// <summary>A fade curve as the editor's list names it.</summary>
    public static string FmtCurve(FadeCurveKind curve) => curve == FadeCurveKind.Linear ? "Linear" : "Equal-power";

    /// <summary>A curve as the list names it in playlist mode, where none falls through to the
    /// global transition's.</summary>
    public static string FmtCurve(FadeCurveKind? curve) => curve is { } c ? FmtCurve(c) : "(global)";

    /// <summary>The editor's heading line when the open playlist plays something other than the
    /// global transition (playlists issue 04): each parameter of the effective transition, a blank
    /// one as "blank".</summary>
    public static string FmtEffective(string playlist, TransitionSpec? effective)
    {
        if (effective is null)
        {
            return $"{playlist} plays no transition here.";
        }
        static string Part(TimeSpan? seconds) => seconds is null ? "blank" : $"{FmtSeconds(seconds)} s";
        var fromEnd = effective.FadeOutPoint?.FromEnd == true ? " from end" : "";
        var parts = new List<string>
        {
            $"fade-out at {Part(effective.FadeOutPoint?.Position)}{fromEnd} for {Part(effective.FadeOutDuration)}",
            $"fade-in from {Part(effective.FadeInPoint?.Position)} over {Part(effective.FadeInDuration)}",
        };
        if (effective.Curve is { } curve)
        {
            parts.Add(FmtCurve(curve).ToLowerInvariant());
        }
        return $"{playlist} plays: {string.Join(", ", parts)}.";
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

    /// <summary>The clip light's text (build issue 17): dark with no clip, else the count and the
    /// latest cause, "2 clips · latest: C".</summary>
    public static string FmtClip(ClipView? clip) => $"● {ClipSummary(clip)}";

    /// <summary>The light's tooltip: what it means, and, lit, the whole of its text, which the
    /// strip may cut short, and that a click clears it.</summary>
    public static string ClipTip(ClipView? clip) =>
        clip is null
            ? "Lights when the output goes beyond full scale."
            : $"{ClipSummary(clip)}. The output went beyond full scale. Click to clear.";

    private static string ClipSummary(ClipView? clip) => clip switch
    {
        null => "No clips",
        { Count: 1 } => $"1 clip · latest: {clip.Latest.Name}",
        _ => $"{clip.Count} clips · latest: {clip.Latest.Name}",
    };

    /// <summary>A queue row's number, marked ⚠ while its file is unreadable (build issue 10).</summary>
    public static string RowLabel(int number, bool unreadable) => unreadable ? $"⚠ {number}" : number.ToString();

    /// <summary>A queue row's tooltip: why it is marked, and what a double-click does. Blank
    /// otherwise.</summary>
    public static string RowTip(bool unreadable) =>
        unreadable ? "Unreadable: its file could not be opened, so it is passed over. Double-click to try it again." : "";

    /// <summary>A queue row's editor button (interim transport 07): on every row that has a
    /// this → next, except the last, which has none even where repeat-all gives it one (D9).</summary>
    public static string EditGlyph(bool? nextAuthored, bool isLast) => !isLast && nextAuthored is not null ? "✎" : "";

    public static string PlayPauseGlyph(Transport transport) => transport == Transport.Playing ? "⏸" : "▶";

    /// <summary>The main window's time readout for the attended voice, marked "Held" while a
    /// preview holds the queue (build issue 16).</summary>
    public static string TimeReadout(VoiceView? attended, bool held) =>
        attended is null
            ? "0:00 / -0:00"
            : $"{(held ? "Held " : "")}{Fmt(attended.Playhead)} / -{FmtRemaining(attended.Remaining)}";

    /// <summary>The editor's line for a running preview: its phase, and the playhead of each voice
    /// heard. Blank with no preview.</summary>
    public static string FmtPreview(PreviewView? preview)
    {
        if (preview is null)
        {
            return "";
        }
        var phase = preview.Phase switch
        {
            PreviewPhase.PreRoll => "Pre-roll",
            PreviewPhase.Transition => "Transition",
            _ => "Tail",
        };
        var voices = new[] { ("outgoing", preview.Outgoing), ("incoming", preview.Incoming) }
            .Where(v => v.Item2 is not null)
            .Select(v => $"{v.Item1} {Fmt(v.Item2!.Value)}");
        return string.Join(" · ", voices.Prepend(phase));
    }

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

    public static string FmtOutputMode(AudioOutputMode mode) => mode switch
    {
        AudioOutputMode.WasapiExclusive => "WASAPI exclusive",
        AudioOutputMode.WaveOut => "WaveOut",
        _ => "WASAPI shared",
    };

    /// <summary>The settings dialog's note beside the chosen output mode: the mode in use, when it
    /// is not the one chosen (a fallback at launch, or a choice waiting for the next launch).
    /// Blank otherwise.</summary>
    public static string FmtModeInUse(AudioOutputMode chosen, AudioOutputMode? inUse) =>
        inUse == chosen ? "" : $"In use now: {(inUse is { } mode ? FmtOutputMode(mode) : "no audio output")}";

    /// <summary>The window title: the open playlist in front (playlists issue 03), then the open
    /// library (library issue 02), then the output mode actually in use, not the one asked for.</summary>
    public static string Title(string? playlist, LibraryView? library, AudioOutputMode? inUse, int sampleRate)
    {
        var name = library switch
        {
            null => "no library",
            { IsAvailable: false } => $"{library.Name} (could not be opened)",
            _ => library.Name,
        };
        var output = inUse is { } mode ? $"{FmtOutputMode(mode)} at {sampleRate} Hz" : "no audio output";
        var title = $"faderio — {name} · {output}";
        return playlist is null ? title : $"{playlist} — {title}";
    }

    /// <summary>"--library=&lt;folder&gt;": for smoke runs, the library to open instead of the
    /// remembered one, without asking. Null when there is none.</summary>
    public static string? ParseLibraryArgument(IEnumerable<string> args) => ParseFolderArgument(args, "--library=");

    /// <summary>"--machine=&lt;folder&gt;": for smoke runs, the folder holding this machine's
    /// settings.json and window.json instead of %APPDATA%\faderio. Null when there is none.</summary>
    public static string? ParseMachineArgument(IEnumerable<string> args) => ParseFolderArgument(args, "--machine=");

    /// <summary>The last "<paramref name="name"/>&lt;folder&gt;" given, or null.</summary>
    private static string? ParseFolderArgument(IEnumerable<string> args, string name) =>
        args.LastOrDefault(a => a.StartsWith(name, StringComparison.OrdinalIgnoreCase)) is { } arg &&
        arg.Length > name.Length
            ? arg[name.Length..]
            : null;

    /// <summary>"--unavailable=exclusive,shared": for smoke runs, the output treats these modes
    /// as if their device were taken, so the fallback chain can be seen.</summary>
    public static IReadOnlyList<AudioOutputMode> ParseUnavailableOutputs(IEnumerable<string> args)
    {
        var modes = new List<AudioOutputMode>();
        foreach (var arg in args)
        {
            if (arg.StartsWith("--unavailable=", StringComparison.OrdinalIgnoreCase))
            {
                modes.AddRange(arg["--unavailable=".Length..].Split(',').Select(ParseMode));
            }
        }
        return modes;
    }

    private static AudioOutputMode ParseMode(string name) => name.Trim().ToLowerInvariant() switch
    {
        "wasapi-exclusive" or "exclusive" => AudioOutputMode.WasapiExclusive,
        "waveout" => AudioOutputMode.WaveOut,
        _ => AudioOutputMode.WasapiShared,
    };
}
