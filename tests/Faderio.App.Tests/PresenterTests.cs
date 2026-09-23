using Faderio.App;
using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;

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

    private static PlayerView ViewOf(params string[] paths)
    {
        var rows = paths.Select((p, i) => new QueueRow(new RowId(i + 1), new Track { FilePath = p }, i == 0)).ToList();
        return new PlayerView(1, 1, Transport.Stopped, rows, null, null, false, RepeatMode.Off);
    }

    [Fact]
    public void AdjacencyAt_IsNullForTheLastRow_AndOutOfRange()
    {
        var view = ViewOf(@"C:\a.wav", @"C:\b.wav", @"C:\c.wav");

        var first = Presenter.AdjacencyAt(view, 0)!;
        Assert.Equal(@"C:\a.wav", first.Outgoing.FilePath);
        Assert.Equal(@"C:\b.wav", first.Incoming.FilePath);
        Assert.NotNull(Presenter.AdjacencyAt(view, 1));
        Assert.Null(Presenter.AdjacencyAt(view, 2));  // the last row has no "next"
        Assert.Null(Presenter.AdjacencyAt(view, -1));
        Assert.Null(Presenter.AdjacencyAt(view, 3));
        Assert.Null(Presenter.AdjacencyAt(ViewOf(), 0));
    }

    [Fact]
    public void FmtAdding_CountsTheFilesResolved_AndIsBlankWhenIdle()
    {
        Assert.Equal("Adding 120 of 903…", Presenter.FmtAdding(new AddProgress(120, 903)));
        Assert.Equal("", Presenter.FmtAdding(null));
    }

    [Theory]
    [InlineData("", AudioOutputMode.WasapiShared)]
    [InlineData("--output=wasapi-shared", AudioOutputMode.WasapiShared)]
    [InlineData("--output=wasapi-exclusive", AudioOutputMode.WasapiExclusive)]
    [InlineData("--output=WaveOut", AudioOutputMode.WaveOut)]
    [InlineData("--output=nonsense", AudioOutputMode.WasapiShared)]
    public void ParseOutputMode_ReadsTheArgument(string arg, AudioOutputMode expected) =>
        Assert.Equal(expected, Presenter.ParseOutputMode(arg.Length == 0 ? Array.Empty<string>() : new[] { arg }));

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
}
