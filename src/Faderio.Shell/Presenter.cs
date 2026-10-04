using System.Globalization;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.Shell;

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
    public static string FmtEffective(string playlist, TransitionSpec? effective) =>
        effective is null ? $"{playlist} plays no transition here." : $"{playlist} plays: {FmtTransition(effective)}.";

    /// <summary>Each parameter of a transition in a phrase, a blank one as "blank": the editor's
    /// heading line and the manager's Effective column.</summary>
    public static string FmtTransition(TransitionSpec spec)
    {
        static string Part(TimeSpan? seconds) => seconds is null ? "blank" : $"{FmtSeconds(seconds)} s";
        var fromEnd = spec.FadeOutPoint?.FromEnd == true ? " from end" : "";
        var parts = new List<string>
        {
            $"fade-out at {Part(spec.FadeOutPoint?.Position)}{fromEnd} for {Part(spec.FadeOutDuration)}",
            $"fade-in from {Part(spec.FadeInPoint?.Position)} over {Part(spec.FadeInDuration)}",
        };
        if (spec.Curve is { } curve)
        {
            parts.Add(FmtCurve(curve).ToLowerInvariant());
        }
        return string.Join(", ", parts);
    }

    /// <summary>A track as the transition manager names it (playlists issue 05): "title — artist",
    /// the title alone with no artist, and the path when its file could not be probed.</summary>
    public static string FmtTrack(Track track) =>
        track.Gone ? "⚠ " + track.FilePath
        : string.IsNullOrEmpty(track.Title) ? track.FilePath
        : string.IsNullOrEmpty(track.Artist) ? track.Title
        : $"{track.Title} — {track.Artist}";

    /// <summary>A Capture button's tooltip, naming the track whose playhead it reads (phase 6
    /// issue 06).</summary>
    public static string FmtCaptureTip(Track track) => $"Capture {TitleOf(track)}'s playhead";

    /// <summary>The line under a greyed Capture button's field: what to play so it can capture. A
    /// preview plays both of the boundary's tracks, so it always serves.</summary>
    public static string FmtCaptureHint(Track track) => $"Play {TitleOf(track)} or preview to capture";

    /// <summary>The editor's traffic light, in words (phase 6 issue 07): its tooltip and its
    /// accessible name.</summary>
    public static string FmtHeard(AdjacencyHeard heard, Adjacency adjacency) => heard switch
    {
        AdjacencyHeard.Outgoing => $"Playing {TitleOf(adjacency.Outgoing)}, this transition's first track",
        AdjacencyHeard.Incoming => $"Playing {TitleOf(adjacency.Incoming)}, this transition's second track",
        AdjacencyHeard.OtherTrack => "Playing a track that is not in this transition",
        _ => "Nothing is playing",
    };

    private static string TitleOf(Track track) =>
        string.IsNullOrEmpty(track.Title) ? Path.GetFileNameWithoutExtension(track.FilePath) : track.Title;

    /// <summary>A gone track's tooltip in the manager (phase 3 issue 02).</summary>
    public static string FmtGoneTip(Track track) => $"Not found where the store says: {track.FilePath}";

    /// <summary>The editor's line under the heading when a side of its adjacency is gone (phase 3
    /// issue 02), naming each gone file; blank when both were found.</summary>
    public static string FmtMissing(Adjacency adjacency)
    {
        var gone = new[] { adjacency.Outgoing, adjacency.Incoming }
            .Where(t => t.Gone).Select(t => Path.GetFileName(t.FilePath)).Distinct().ToList();
        return gone.Count == 0 ? ""
            : $"{string.Join(" and ", gone)} {(gone.Count == 1 ? "is" : "are")} missing: this transition can be removed, not changed.";
    }

    /// <summary>The manager's cells for one adjacency: the direction from the picked track, the
    /// other track, a "global | override" pair for each parameter and the curve, the effective
    /// transition and the playlist-only mark.</summary>
    public static IReadOnlyList<string> ManagerRow(StoredAdjacency stored)
    {
        var (global, over) = (stored.Layers.Global, stored.Layers.Override);
        return
        [
            stored.IsOutgoing ? "→" : "←",
            FmtTrack(stored.Other),
            FmtLayerPair(FmtPoint(global?.FadeOutPoint), FmtPoint(over?.FadeOutPoint)),
            FmtLayerPair(FmtSeconds(global?.FadeOutDuration), FmtSeconds(over?.FadeOutDuration)),
            FmtLayerPair(FmtPoint(global?.FadeInPoint), FmtPoint(over?.FadeInPoint)),
            FmtLayerPair(FmtSeconds(global?.FadeInDuration), FmtSeconds(over?.FadeInDuration)),
            FmtLayerPair(global?.Curve is { } g ? FmtCurve(g) : "", over?.Curve is { } o ? FmtCurve(o) : ""),
            stored.Layers.Effective is { } effective ? FmtTransition(effective) : "",
            stored.PlaylistOnly ? "✓" : "",
        ];
    }

    /// <summary>"global | override", "–" for the layer that does not set it; blank when neither does.</summary>
    public static string FmtLayerPair(string global, string over) =>
        global.Length == 0 && over.Length == 0 ? "" : $"{(global.Length == 0 ? "–" : global)} | {(over.Length == 0 ? "–" : over)}";

    /// <summary>A stored point in seconds, "from end" when measured from the end; blank when unset.</summary>
    public static string FmtPoint(FadePointSpec? point) =>
        point is null ? "" : FmtSeconds(point.Point) + (point.FromEnd ? " from end" : "");

    /// <summary>The line above the manager's grid: how to read a pair, or that the store is empty.</summary>
    public static string ManagerLegend(string? playlist, bool anyTracks) =>
        !anyTracks ? "No transitions are saved in this library yet."
        : playlist is null ? "Each value reads global | override. Double-click a row to open it in the editor."
        : $"Each value reads global | override in {playlist}. Double-click a row to open it in the editor.";

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

    /// <summary>A queue row's number, marked ⚠ while its file is unreadable (build issue 10), after
    /// <paramref name="mark"/>, the attended row's speaker, if any.</summary>
    public static string RowLabel(int number, bool unreadable, string? mark = null) =>
        (mark is null ? "" : mark + " ") + (unreadable ? $"⚠ {number}" : number.ToString());

    /// <summary>A queue column's heading, as the header and its menu name it (playlists issue 06).</summary>
    public static string ColumnHeading(QueueColumn column) => column switch
    {
        QueueColumn.Number => "#",
        QueueColumn.ReplayGain => "Replay gain",
        _ => column.ToString(),
    };

    /// <summary>A queue row's cell in <paramref name="column"/>. # is the row's label, not the
    /// track's, so it has none here.</summary>
    public static string QueueCell(QueueColumn column, Track track) => column switch
    {
        QueueColumn.Title => string.IsNullOrEmpty(track.Title) ? Path.GetFileName(track.FilePath) : track.Title,
        QueueColumn.Artist => track.Artist,
        QueueColumn.Album => track.Album,
        QueueColumn.Length => Fmt(track.Length),
        QueueColumn.Format => FmtFormat(track.Format),
        QueueColumn.ReplayGain => FmtReplayGain(track.ReplayGainDb),
        QueueColumn.Path => track.FilePath,
        _ => "",
    };

    /// <summary>The Format column (D25): "44.1 kHz / 16-bit"; an MP3 has no bit depth, so its rate
    /// alone. Blank for a track never probed.</summary>
    public static string FmtFormat(AudioShape? format) =>
        format is null ? ""
        : (format.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + " kHz"
          + (format.BitDepth is { } depth ? $" / {depth}-bit" : "");

    /// <summary>The ReplayGain modes in the order the settings list them, MusicBee's order.</summary>
    public static IReadOnlyList<ReplayGainMode> ReplayGainModes { get; } =
        [ReplayGainMode.Track, ReplayGainMode.Album, ReplayGainMode.Smart, ReplayGainMode.Off];

    /// <summary>A ReplayGain mode as the settings list it.</summary>
    public static string FmtReplayGainMode(ReplayGainMode mode) => mode switch
    {
        ReplayGainMode.Track => "Track",
        ReplayGainMode.Album => "Album",
        ReplayGainMode.Smart => "Smart (album gain on an album's run of tracks)",
        _ => "Off",
    };

    /// <summary>The Replay gain column (D25): the track gain to a tenth, "−7.2 dB" with a true minus
    /// and "+1.5 dB" with a plus; blank when untagged. Display only: playback reads the value.</summary>
    public static string FmtReplayGain(double? db)
    {
        if (db is not { } value)
        {
            return "";
        }
        var tenths = Math.Round(value, 1, MidpointRounding.AwayFromZero);
        var sign = tenths < 0 ? "−" : tenths > 0 ? "+" : "";
        return $"{sign}{Math.Abs(tenths).ToString("0.0", CultureInfo.InvariantCulture)} dB";
    }

    /// <summary>A queue row's tooltip: why it is marked, and what a double-click does. Blank
    /// otherwise.</summary>
    public static string RowTip(bool unreadable) =>
        unreadable ? "Unreadable: its file could not be opened, so it is passed over. Double-click to try it again." : "";

    /// <summary>A queue row's editor button (interim transport 07): on every row that has a
    /// this → next, except the last, which has none even where repeat-all gives it one (D9).</summary>
    public static string EditGlyph(bool? nextAuthored, bool isLast) => !isLast && nextAuthored is not null ? "✎" : "";

    public static string PlayPauseGlyph(Transport transport) => transport == Transport.Playing ? "⏸" : "▶";

    /// <summary>The main window's time readout for the attended voice, marked "Held" while a
    /// preview holds the queue (build issue 16). The remaining side is the engine's countdown,
    /// so the two tick together.</summary>
    public static string TimeReadout(VoiceView? attended, bool held) =>
        attended is null
            ? "0:00 / -0:00"
            : $"{(held ? "Held " : "")}{Fmt(attended.Playhead)} / -{Fmt(attended.Countdown)}";

    /// <summary>The line under the time readout: the attended voice's playhead and length in
    /// seconds, to the tenth, so it reads against the fade points the editor shows in seconds.
    /// Cut down to the tenth, as the m:ss readout cuts to the second.</summary>
    public static string SecondsReadout(VoiceView? attended) =>
        attended is null
            ? "0.0 / 0.0"
            : $"{Tenths(attended.Playhead.TimeSpan)} / {Tenths(attended.Length.TimeSpan)}";

    private static string Tenths(TimeSpan time) =>
        (Math.Floor(time.TotalSeconds * 10 + 1e-9) / 10).ToString("0.0", CultureInfo.InvariantCulture);

    /// <summary>The line above the transport: the attended voice's track, named as
    /// <see cref="FmtTrack"/> names it, so it is the track the readout and the seek bar are about.
    /// Paused, it still names it; blank only when stopped, as nothing is attended then.</summary>
    public static string FmtAttendedTrack(VoiceView? attended) =>
        attended is null ? "" : FmtTrack(attended.Row.Track);

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

    /// <summary>The Sync editor box's tooltip: what following playback does.</summary>
    public const string SyncEditorTip = "Each track that starts opens the editor on its transition into the next";

    public static string ShuffleTip(bool shuffle) => shuffle ? "Shuffle: on" : "Shuffle: off";

    /// <summary>The settings dialog's note beside the chosen output mode: the mode in use, when it
    /// is not the one chosen (a fallback at launch, or a choice waiting for the next launch).
    /// Blank otherwise.</summary>
    public static string FmtModeInUse(AudioOutputMode chosen, AudioOutputMode? inUse) =>
        inUse == chosen ? "" : $"In use now: {(inUse is { } mode ? OutputModeNames.Of(mode) : "no audio output")}";

    /// <summary>The window title: the open playlist in front (playlists issue 03), then
    /// <see cref="FmtHost"/>: the open library and the output mode in use.</summary>
    public static string Title(string? playlist, LibraryView? library, AudioOutputMode? inUse, int sampleRate)
    {
        var title = $"faderio — {FmtHost(library, inUse, sampleRate)}";
        return playlist is null ? title : $"{playlist} — {title}";
    }

    /// <summary>What this host plays with: the open library (library issue 02), then the output
    /// mode actually in use, not the one asked for, at its rate, and on which device when the
    /// host names one (phase 5 issue 13). The App's title carries it; the plugin's strip shows
    /// it, since MusicBee's window has no title of faderio's (phase 5 issue 10).</summary>
    public static string FmtHost(LibraryView? library, AudioOutputMode? inUse, int sampleRate, string? device = null)
    {
        var name = library switch
        {
            null => "no library",
            { IsAvailable: false } => $"{library.Name} (could not be opened)",
            _ => library.Name,
        };
        var output = inUse is { } mode ? $"{OutputModeNames.Of(mode)} at {sampleRate} Hz" : "no audio output";
        if (inUse is not null && device is not null)
        {
            output += $" on {device}";
        }
        return $"{name} · {output}";
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
            ? arg.Substring(name.Length)
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
                modes.AddRange(arg.Substring("--unavailable=".Length).Split(',').Select(ParseMode));
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
