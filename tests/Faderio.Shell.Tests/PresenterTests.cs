using Faderio.Shell;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.Shell.Tests;

/// <summary>Seam S4 (ADR 0002): the shell's logic with no WinForms in it.</summary>
public sealed class PresenterTests
{
    [Theory]
    [InlineData(0, "0:00")]
    [InlineData(3.999, "0:03")]
    [InlineData(65, "1:05")]
    [InlineData(1560.5, "26:00")]
    public void Fmt_ShowsWholeSeconds(double seconds, string expected) =>
        Assert.Equal(expected, Presenter.Fmt(TimeSpan.FromSeconds(seconds)));

    [Theory]
    [InlineData(0.0, "0:00")]
    [InlineData(0.4, "0:01")]
    [InlineData(2.0, "0:02")]
    [InlineData(17.001, "0:18")]
    public void FmtRemaining_RoundsUp_SoItNeverReadsZeroMidTrack(double seconds, string expected) =>
        Assert.Equal(expected, Presenter.FmtRemaining(new Length(TimeSpan.FromSeconds(seconds))));

    [Fact]
    public void FmtSeconds_IsDisplayOnly_ThreeDecimals()
    {
        Assert.Equal("", Presenter.FmtSeconds((Point?)null));
        Assert.Equal("1.5", Presenter.FmtSeconds(Point.FromSeconds(1.5)));
        Assert.Equal("37.132", Presenter.FmtSeconds(Point.FromSeconds(37.1324)));
    }

    [Fact]
    public void ParseSeconds_DistinguishesUnset_Invalid_AndNegative()
    {
        // v2 returned null for all three and silently saved nothing.
        Assert.True(Presenter.ParseSeconds("").IsUnset);
        Assert.True(Presenter.ParseSeconds("   ").IsUnset);

        var invalid = Presenter.ParseSeconds("abc");
        Assert.Null(invalid.Point);
        Assert.Contains("not a number", invalid.Error);

        var negative = Presenter.ParseSeconds("-5");
        Assert.Null(negative.Point);
        Assert.Contains("negative", negative.Error);

        var ok = Presenter.ParseSeconds("1.5");
        Assert.Null(ok.Error);
        Assert.Equal(1.5, ok.Point!.Value.TotalSeconds, 9);
    }

    [Fact]
    public void FmtAdding_CountsTheFilesResolved_AndIsBlankWhenIdle()
    {
        Assert.Equal("Adding 120 of 903…", Presenter.FmtAdding(new AddProgress(120, 903)));
        Assert.Equal("", Presenter.FmtAdding(null));
    }

    [Theory]
    [InlineData(RepeatMode.Off, RepeatMode.All)]
    [InlineData(RepeatMode.All, RepeatMode.One)]
    [InlineData(RepeatMode.One, RepeatMode.Off)]
    public void NextRepeat_CyclesOffAllOne(RepeatMode shown, RepeatMode next) =>
        Assert.Equal(next, Presenter.NextRepeat(shown));

    [Fact]
    public void RepeatOne_HasItsOwnGlyph_AndEachModeIsNamed()
    {
        Assert.Equal("🔁", Presenter.RepeatGlyph(RepeatMode.Off));
        Assert.Equal("🔁", Presenter.RepeatGlyph(RepeatMode.All));
        Assert.Equal("🔁1", Presenter.RepeatGlyph(RepeatMode.One));
        Assert.Equal("Repeat: off", Presenter.RepeatTip(RepeatMode.Off));
        Assert.Equal("Repeat: all", Presenter.RepeatTip(RepeatMode.All));
        Assert.Equal("Repeat: one", Presenter.RepeatTip(RepeatMode.One));
    }

    [Fact]
    public void ShuffleTip_NamesTheState()
    {
        Assert.Equal("Shuffle: on", Presenter.ShuffleTip(true));
        Assert.Equal("Shuffle: off", Presenter.ShuffleTip(false));
    }

    [Theory]
    [InlineData(AudioOutputMode.WasapiShared, AudioOutputMode.WasapiShared, "")]
    [InlineData(AudioOutputMode.WasapiExclusive, AudioOutputMode.WasapiShared, "In use now: WASAPI shared")]
    [InlineData(AudioOutputMode.WasapiShared, null, "In use now: no audio output")]
    public void FmtModeInUse_NamesTheModeInUse_OnlyWhenItIsNotTheOneChosen(
        AudioOutputMode chosen, AudioOutputMode? inUse, string expected) =>
        Assert.Equal(expected, Presenter.FmtModeInUse(chosen, inUse));

    [Theory]
    [InlineData(AudioOutputMode.WasapiExclusive, new[] { AudioOutputMode.WasapiExclusive, AudioOutputMode.WasapiShared, AudioOutputMode.WaveOut })]
    [InlineData(AudioOutputMode.WasapiShared, new[] { AudioOutputMode.WasapiShared, AudioOutputMode.WaveOut })]
    [InlineData(AudioOutputMode.WaveOut, new[] { AudioOutputMode.WaveOut })]
    public void OutputFallbacks_RunExclusiveSharedWaveOut_FromTheModeAsked(AudioOutputMode asked, AudioOutputMode[] expected) =>
        Assert.Equal(expected, NAudioAudioOutput.Fallbacks(asked));

    [Fact]
    public void ParseUnavailableOutputs_ReadsTheSmokeArgument()
    {
        Assert.Empty(Presenter.ParseUnavailableOutputs(new[] { "--play" }));
        Assert.Equal(
            new[] { AudioOutputMode.WasapiExclusive, AudioOutputMode.WasapiShared },
            Presenter.ParseUnavailableOutputs(new[] { "--unavailable=wasapi-exclusive,shared" }));
    }

    [Fact]
    public void Title_NamesTheModeInUse_OrThatThereIsNone()
    {
        var music = new LibraryView(@"C:\Music", "Music", IsAvailable: true);
        Assert.Equal("faderio — Music · WASAPI shared at 48000 Hz", Presenter.Title(null, music, AudioOutputMode.WasapiShared, 48000));
        Assert.Equal("faderio — Music · WASAPI exclusive at 44100 Hz", Presenter.Title(null, music, AudioOutputMode.WasapiExclusive, 44100));
        Assert.Equal("faderio — Music · WaveOut at 44100 Hz", Presenter.Title(null, music, AudioOutputMode.WaveOut, 44100));
        Assert.Equal("faderio — Music · no audio output", Presenter.Title(null, music, null, 44100));
    }

    [Fact]
    public void FmtHost_IsTheLibraryAndTheModeInUse_AsTheTitleCarriesThem()
    {
        var music = new LibraryView(@"C:\Music", "Music", IsAvailable: true);
        Assert.Equal("Music · WASAPI shared at 48000 Hz", Presenter.FmtHost(music, AudioOutputMode.WasapiShared, 48000));
        Assert.Equal("no library · no audio output", Presenter.FmtHost(null, null, 44100));
        Assert.Equal("Music · WASAPI exclusive at 44100 Hz on DAC", Presenter.FmtHost(music, AudioOutputMode.WasapiExclusive, 44100, "DAC"));
        Assert.Equal("Music · no audio output", Presenter.FmtHost(music, null, 44100, "DAC"));
        Assert.Equal(
            "Locked (could not be opened) · WaveOut at 44100 Hz",
            Presenter.FmtHost(new LibraryView(@"D:\Locked", "Locked", IsAvailable: false), AudioOutputMode.WaveOut, 44100));
    }

    [Fact]
    public void Title_PutsTheOpenPlaylistInFront()
    {
        var music = new LibraryView(@"C:\Music", "Music", IsAvailable: true);
        Assert.Equal(
            "Evening — faderio — Music · WASAPI shared at 48000 Hz",
            Presenter.Title("Evening", music, AudioOutputMode.WasapiShared, 48000));
    }

    [Fact]
    public void Title_NamesTheLibrary_OrThatThereIsNone()
    {
        Assert.Equal("faderio — no library · WaveOut at 44100 Hz", Presenter.Title(null, null, AudioOutputMode.WaveOut, 44100));
        Assert.Equal(
            "faderio — Locked (could not be opened) · WaveOut at 44100 Hz",
            Presenter.Title(null, new LibraryView(@"D:\Locked", "Locked", IsAvailable: false), AudioOutputMode.WaveOut, 44100));
    }

    [Theory]
    [InlineData(new string[0], null)]
    [InlineData(new[] { "a.wav", "--play" }, null)]
    [InlineData(new[] { "--library=" }, null)]
    [InlineData(new[] { @"--library=C:\Temp\lib one" }, @"C:\Temp\lib one")]
    [InlineData(new[] { @"--LIBRARY=C:\a", @"--library=C:\b" }, @"C:\b")]
    public void ParseLibraryArgument_TakesTheLastOne(string[] args, string? expected) =>
        Assert.Equal(expected, Presenter.ParseLibraryArgument(args));

    [Theory]
    [InlineData(new[] { @"--library=C:\Temp\lib" }, null)]
    [InlineData(new[] { "--machine=" }, null)]
    [InlineData(new[] { @"--machine=C:\Temp\machine", @"--library=C:\Temp\lib" }, @"C:\Temp\machine")]
    [InlineData(new[] { @"--MACHINE=C:\a", @"--machine=C:\b" }, @"C:\b")]
    public void ParseMachineArgument_TakesTheLastOne(string[] args, string? expected) =>
        Assert.Equal(expected, Presenter.ParseMachineArgument(args));

    [Fact]
    public void NoticeLog_MergesARepeatIntoItsFirstEntry_WithACount()
    {
        var log = new NoticeLog();
        log.Add("one");
        log.Add("two");
        for (var i = 0; i < 49; i++)
        {
            log.Add("one");
        }

        Assert.Equal(new[] { "one", "two" }, log.Entries.Select(e => e.Message));
        Assert.Equal(new[] { 50, 1 }, log.Entries.Select(e => e.Count));
        Assert.Same(log.Entries[0], log.Latest); // the latest delivery, wherever its entry sits
        Assert.Equal("one ×50", Presenter.FmtNotice(log.Latest!));
        Assert.Equal("two", Presenter.FmtNotice(log.Entries[1]));
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "1 notice")]
    [InlineData(2, "2 notices")]
    public void FmtNoticeCount_CountsTheListsEntries(int count, string expected) =>
        Assert.Equal(expected, Presenter.FmtNoticeCount(count));

    private static Track Named(string name) => new() { FilePath = Path.Combine("C:\\music", name + ".flac") };

    [Fact]
    public void FmtClip_IsDarkWithNoClip_ElseTheCountAndTheLatestCause()
    {
        Assert.Equal("● No clips", Presenter.FmtClip(null));
        Assert.Equal("● 1 clip · latest: A → B",
            Presenter.FmtClip(new ClipView(1, ClipCause.Of(new Adjacency(Named("A"), Named("B"))))));
        Assert.Equal("● 2 clips · latest: C", Presenter.FmtClip(new ClipView(2, ClipCause.Of(Named("C")))));
    }

    [Fact]
    public void ClipTip_SaysWhatTheLightMeans_AndLit_HoldsItsWholeText()
    {
        Assert.Equal("Lights when the output goes beyond full scale.", Presenter.ClipTip(null));
        Assert.Equal("2 clips · latest: C. The output went beyond full scale. Click to clear.",
            Presenter.ClipTip(new ClipView(2, ClipCause.Of(Named("C")))));
    }

    [Fact]
    public void FmtEffective_NamesThePlaylist_AndEachParameter_ABlankOneAsBlank()
    {
        Assert.Equal("Evening plays: fade-out at 8 s from end for 5 s, fade-in from blank over 2.5 s, linear.",
            Presenter.FmtEffective("Evening", new TransitionSpec
            {
                FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(8), FromEnd: true),
                FadeOutDuration = TimeSpan.FromSeconds(5),
                FadeInDuration = TimeSpan.FromSeconds(2.5),
                Curve = FadeCurveKind.Linear,
            }));
        Assert.Equal("Evening plays: fade-out at blank for blank, fade-in from 0 s over blank.",
            Presenter.FmtEffective("Evening", new TransitionSpec { FadeInPoint = TimeSpan.Zero }));
    }

    [Fact]
    public void FmtCurve_WithNoCurve_IsTheGlobalOne() => Assert.Equal("(global)", Presenter.FmtCurve((FadeCurveKind?)null));

    // ── The transition manager (playlists issue 05) ─────────────────────────────────────

    private static Track TrackNamed(string path, string title = "", string artist = "") =>
        new() { FilePath = path, Title = title, Artist = artist };

    [Fact]
    public void FmtTrack_IsTitleAndArtist_TheTitleAlone_OrThePathOfAFileThatCouldNotBeProbed()
    {
        Assert.Equal("Morning — Example Band", Presenter.FmtTrack(TrackNamed(@"C:\m\morning.flac", "Morning", "Example Band")));
        Assert.Equal("Morning", Presenter.FmtTrack(TrackNamed(@"C:\m\morning.flac", "Morning")));
        Assert.Equal(@"C:\m\gone.flac", Presenter.FmtTrack(TrackNamed(@"C:\m\gone.flac")));
    }

    [Fact]
    public void FmtAttendedTrack_NamesTheAttendedVoicesTrack_AndIsBlankWhenStopped()
    {
        var track = TrackNamed(@"C:\m\morning.flac", "Morning", "Example Band");
        var voice = new VoiceView(new QueueRow(new RowId(1), track, IsCurrent: true), Playhead.Zero, new Length(TimeSpan.FromSeconds(5)));

        Assert.Equal("Morning — Example Band", Presenter.FmtAttendedTrack(voice));
        Assert.Equal("", Presenter.FmtAttendedTrack(null));
    }

    [Fact]
    public void ManagerRow_PairsEachParameterGlobalThenOverride_WithADashForTheLayerThatSetsNone()
    {
        var a = TrackNamed(@"C:\m\a.wav", "A");
        var b = TrackNamed(@"C:\m\b.wav", "B", "Band");
        var global = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(10), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(2),
            FadeInPoint = TimeSpan.Zero,
            Curve = FadeCurveKind.Linear,
        };
        var over = new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(8), FromEnd: true) };
        var effective = global with { FadeOutPoint = over.FadeOutPoint };
        var stored = new StoredAdjacency(new Adjacency(b, a), IsOutgoing: false, new TransitionLayers(global, over, effective));

        Assert.Equal(
            ["←", "B — Band", "10 | 8 from end", "2 | –", "0 | –", "", "Linear | –",
             "fade-out at 8 s from end for 2 s, fade-in from 0 s over blank, linear", ""],
            Presenter.ManagerRow(stored));
    }

    [Fact]
    public void ManagerRow_MarksAPlaylistOnlyTransition_AndPointsOutgoingWithARightArrow()
    {
        var a = TrackNamed(@"C:\m\a.wav", "A");
        var over = new TransitionSpec { FadeInDuration = TimeSpan.FromSeconds(1) };
        var stored = new StoredAdjacency(new Adjacency(a, a), IsOutgoing: true, new TransitionLayers(null, over, over));

        var row = Presenter.ManagerRow(stored);

        Assert.Equal("→", row[0]);
        Assert.Equal("– | 1", row[5]);
        Assert.Equal("✓", row[8]);
    }

    [Fact]
    public void ManagerLegend_SaysHowToReadAPair_OrThatNothingIsSaved()
    {
        Assert.Equal("Each value reads global | override in Evening. Double-click a row to open it in the editor.",
            Presenter.ManagerLegend("Evening", anyTracks: true));
        Assert.Equal("No transitions are saved in this library yet.", Presenter.ManagerLegend("Evening", anyTracks: false));
    }

    // ── Playlists issue 06: the queue's columns ─────────────────────────────────────────

    [Theory]
    [InlineData(AudioFormat.Flac, 44100, 16, "44.1 kHz / 16-bit")]
    [InlineData(AudioFormat.Wav, 48000, 24, "48 kHz / 24-bit")]
    [InlineData(AudioFormat.Flac, 96000, 24, "96 kHz / 24-bit")]
    [InlineData(AudioFormat.Mp3, 44100, null, "44.1 kHz")] // an MP3 has no bit depth
    public void FmtFormat_IsRateAndDepth(AudioFormat format, int rate, int? depth, string expected) =>
        Assert.Equal(expected, Presenter.FmtFormat(new AudioShape(format, rate, depth, 2)));

    [Fact]
    public void FmtFormat_IsBlank_ForATrackNeverProbed() => Assert.Equal("", Presenter.FmtFormat(null));

    [Theory]
    [InlineData(-7.2, "−7.2 dB")] // a true minus
    [InlineData(1.5, "+1.5 dB")]
    [InlineData(-7.25, "−7.3 dB")]
    [InlineData(0.0, "0.0 dB")]
    [InlineData(-0.04, "0.0 dB")] // no minus on what reads as nothing
    [InlineData(null, "")]        // untagged
    public void FmtReplayGain_IsTheTrackGainToATenth(double? db, string expected) =>
        Assert.Equal(expected, Presenter.FmtReplayGain(db));

    [Fact]
    public void QueueCell_FormatsEachColumnFromTheTrack()
    {
        var track = new Track
        {
            FilePath = @"D:\Music\Example Band\First Album\03 Closing Song.flac",
            Title = "Closing Song",
            Artist = "Example Band",
            Album = "First Album",
            Length = Length.FromFrames(44100 * 65, 44100),
            Format = new AudioShape(AudioFormat.Flac, 44100, 16, 2),
            ReplayGainDb = -7.2,
        };

        Assert.Equal(
            ["", "Closing Song", "Example Band", "First Album", "1:05", "44.1 kHz / 16-bit", "−7.2 dB", @"D:\Music\Example Band\First Album\03 Closing Song.flac"],
            ShellCompat.Values<QueueColumn>().Select(c => Presenter.QueueCell(c, track)));
        Assert.Equal("03 Closing Song.flac", Presenter.QueueCell(QueueColumn.Title, track with { Title = "" }));
    }

    [Fact]
    public void ColumnHeading_NamesEachColumn() =>
        Assert.Equal(["#", "Title", "Artist", "Album", "Length", "Format", "Replay gain", "Path"],
            ShellCompat.Values<QueueColumn>().Select(Presenter.ColumnHeading));
}
