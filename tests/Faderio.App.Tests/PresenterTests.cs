using Faderio.App;
using Faderio.App.Audio;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App.Tests;

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
        Assert.Equal("faderio — WASAPI shared at 48000 Hz", Presenter.Title(AudioOutputMode.WasapiShared, 48000));
        Assert.Equal("faderio — WASAPI exclusive at 44100 Hz", Presenter.Title(AudioOutputMode.WasapiExclusive, 44100));
        Assert.Equal("faderio — WaveOut at 44100 Hz", Presenter.Title(AudioOutputMode.WaveOut, 44100));
        Assert.Equal("faderio — no audio output", Presenter.Title(null, 44100));
    }

    [Fact]
    public void OpenTransitionStore_RecoversACorruptFile_Visibly()
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-app", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transitions.json");
        File.WriteAllText(path, "{ this is not json");
        var warnings = new List<string>();

        var store = Presenter.OpenTransitionStore(path, warnings.Add);

        Assert.NotNull(store);
        Assert.Single(warnings);
        Assert.True(File.Exists(path + ".corrupt"), "the corrupt file is moved aside, not deleted");
        Directory.Delete(dir, recursive: true);
    }

    [Fact]
    public void OpenTransitionStore_ACorruptFileThatCannotBeMovedAside_IsANotice_NotACrash()
    {
        // Build issue 09: the move-aside's failure was swallowed, and the fresh store then
        // reopened the same corrupt file and threw at launch.
        var dir = Path.Combine(Path.GetTempPath(), "faderio-app", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "transitions.json");
        File.WriteAllText(path, "{ this is not json");
        var warnings = new List<string>();
        ITransitionStore store;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // no delete, so no move
        {
            store = Presenter.OpenTransitionStore(path, warnings.Add);
            store.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(1) });
        }

        var warning = Assert.Single(warnings);
        Assert.Contains("could not be moved aside", warning);
        Assert.Equal("{ this is not json", File.ReadAllText(path)); // left untouched
        Assert.False(File.Exists(path + ".corrupt"));
        Assert.True(File.Exists(path + ".recovered"), "writes go to a fresh file beside it");
        Directory.Delete(dir, recursive: true);
    }

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
}
