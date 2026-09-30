using Faderio.Shell;
using Faderio.Core.Tests;

namespace Faderio.Shell.Tests;

/// <summary>
/// Playlists issue 06: the queue's column layout is the shell's, per library. # is always shown and
/// first; the file round-trips order, visibility and widths, and a bad one is the default four.
/// </summary>
public sealed class ColumnLayoutTests : IDisposable
{
    private readonly string _dir = Path.Combine(TestTemp.Root, "columns", Guid.NewGuid().ToString("N"));

    private string FilePath => Path.Combine(_dir, ColumnLayout.FileName);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static QueueColumn[] Shown(ColumnLayout layout) => layout.Shown.Select(c => c.Column).ToArray();

    [Fact]
    public void TheDefault_ShowsTheFour()
    {
        Assert.Equal([QueueColumn.Number, QueueColumn.Title, QueueColumn.Artist, QueueColumn.Length], Shown(ColumnLayout.Default));
        Assert.Equal(ShellCompat.Values<QueueColumn>(), ColumnLayout.Default.Columns.Select(c => c.Column));
    }

    [Fact]
    public void Toggle_ShowsAHiddenColumnAtItsPlace_AndHidesItAgain_ButNeverNumber()
    {
        var shown = ColumnLayout.Default.Toggle(QueueColumn.Album);

        Assert.Equal([QueueColumn.Number, QueueColumn.Title, QueueColumn.Artist, QueueColumn.Album, QueueColumn.Length], Shown(shown));
        Assert.True(shown.Toggle(QueueColumn.Album).Same(ColumnLayout.Default));
        Assert.Same(ColumnLayout.Default, ColumnLayout.Default.Toggle(QueueColumn.Number));
    }

    [Theory]
    [InlineData(2, "Number Title Album Artist Length")]
    [InlineData(4, "Number Title Artist Length Album")]
    [InlineData(5, "Number Title Artist Length Album")] // on the row button
    [InlineData(0, "Number Album Title Artist Length")] // before #
    public void Move_PutsAShownColumnAtItsNewPlace_BetweenNumberAndTheEnd(int to, string expected)
    {
        var layout = ColumnLayout.Default.Toggle(QueueColumn.Album);

        Assert.Equal(expected, string.Join(" ", Shown(layout.Move(QueueColumn.Album, to))));
    }

    [Fact]
    public void Move_LeavesNumberAndHiddenColumnsWhereTheyAre()
    {
        Assert.Same(ColumnLayout.Default, ColumnLayout.Default.Move(QueueColumn.Number, 3));
        Assert.Same(ColumnLayout.Default, ColumnLayout.Default.Move(QueueColumn.Path, 1));
    }

    [Fact]
    public void TheFile_RoundTripsOrderVisibilityAndWidths()
    {
        var layout = ColumnLayout.Default.Toggle(QueueColumn.Album).Move(QueueColumn.Album, 2)
            .Toggle(QueueColumn.ReplayGain).Resize(QueueColumn.Album, 150).Resize(QueueColumn.Artist, 90);

        layout.Save(FilePath);
        var loaded = ColumnLayout.Load(FilePath);

        Assert.Equal(layout.Columns, loaded.Columns);
        Assert.Contains("\"replayGain\"", File.ReadAllText(FilePath)); // names, not numbers, on disk
    }

    [Theory]
    [InlineData(null)]                              // missing
    [InlineData("")]                                // empty
    [InlineData("{ not json")]                      // corrupt
    [InlineData("[1, 2, 3]")]                       // another shape
    [InlineData("{\"columns\": []}")]               // none
    [InlineData("{\"columns\": [{\"column\": \"year\", \"visible\": true, \"width\": 60}]}")] // a column this version lacks
    public void AMissingOrCorruptFile_IsTheDefaultFour(string? contents)
    {
        if (contents is not null)
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(FilePath, contents);
        }

        Assert.True(ColumnLayout.Load(FilePath).Same(ColumnLayout.Default));
    }

    [Fact]
    public void AFileNamingSomeColumns_KeepsThem_WithNumberFirstAndShown_AndTheRestHiddenAfter()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(FilePath, """
            {"columns": [
              {"column": "path", "visible": true, "width": 500},
              {"column": "number", "visible": false, "width": 48},
              {"column": "title", "visible": true, "width": 0},
              {"column": "path", "visible": false, "width": 10}
            ]}
            """);

        var loaded = ColumnLayout.Load(FilePath);

        Assert.Equal([QueueColumn.Number, QueueColumn.Path, QueueColumn.Title], Shown(loaded));
        Assert.Equal(500, loaded[QueueColumn.Path].Width); // the first naming wins
        Assert.Equal(ColumnLayout.Default[QueueColumn.Title].Width, loaded[QueueColumn.Title].Width); // 0 is no width
        Assert.Equal(ShellCompat.Values<QueueColumn>().Length, loaded.Columns.Count);
    }
}
