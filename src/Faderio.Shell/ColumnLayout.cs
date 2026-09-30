using System.Text.Json;
using System.Text.Json.Serialization;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>The queue's columns a user can show (playlists issue 06, D25). The row button at the
/// far right is not one of them.</summary>
internal enum QueueColumn
{
    Number,
    Title,
    Artist,
    Album,
    Length,
    Format,
    ReplayGain,
    Path,
}

/// <summary>One column's place in the layout: whether it shows, and the width it was given.</summary>
internal sealed record ColumnSetting(QueueColumn Column, bool Visible, int Width);

/// <summary>
/// The queue's columns (playlists issue 06, D25): every column in its order, shown or not, each
/// with its width, in the library's <c>.faderio/columns.json</c>. View state, not the engine's, so
/// the shell owns the file, as it does <c>window.json</c>: read when a library opens, written on
/// every change. A file that is missing, unreadable or of another shape is the default four, and
/// none of that is worth a notice. <c>#</c> is always shown and always first, since the list draws
/// its first column from each row's own text.
/// </summary>
internal sealed class ColumnLayout
{
    public const string FileName = "columns.json";

    /// <summary>A width below this is a column dragged shut, which the header menu hides instead.</summary>
    public const int MinWidth = 24;

    private const int MaxWidth = 4000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    public ColumnLayout(IReadOnlyList<ColumnSetting> columns) => Columns = Normalise(columns);

    public IReadOnlyList<ColumnSetting> Columns { get; }

    private static readonly ColumnSetting[] DefaultColumns =
    [
        new(QueueColumn.Number, true, 48),
        new(QueueColumn.Title, true, 320),
        new(QueueColumn.Artist, true, 200),
        new(QueueColumn.Album, false, 200),
        new(QueueColumn.Length, true, 70),
        new(QueueColumn.Format, false, 110),
        new(QueueColumn.ReplayGain, false, 84),
        new(QueueColumn.Path, false, 360),
    ];

    /// <summary>#, Title, Artist and Length shown; the rest in the order the header menu lists them.</summary>
    public static ColumnLayout Default { get; } = new(DefaultColumns);

    /// <summary>The columns shown, left to right.</summary>
    public IReadOnlyList<ColumnSetting> Shown => Columns.Where(c => c.Visible).ToList();

    public ColumnSetting this[QueueColumn column] => Columns.First(c => c.Column == column);

    /// <summary>Shows a hidden column where its place in the order puts it, or hides a shown one.
    /// # stays.</summary>
    public ColumnLayout Toggle(QueueColumn column) =>
        column == QueueColumn.Number ? this
        : new(Columns.Select(c => c.Column == column ? c with { Visible = !c.Visible } : c).ToList());

    /// <summary>Moves a shown column to <paramref name="shownIndex"/> among the shown columns,
    /// counting # as the first: before the column now there, or after the last. Hidden columns keep
    /// their places relative to the rest. # does not move, and nothing moves before it.</summary>
    public ColumnLayout Move(QueueColumn column, int shownIndex)
    {
        if (column == QueueColumn.Number || !this[column].Visible)
        {
            return this;
        }
        var moving = this[column];
        var rest = Columns.Where(c => c.Column != column).ToList();
        var shown = rest.Where(c => c.Visible).ToList();
        var index = ShellCompat.Clamp(shownIndex, 1, shown.Count);
        var at = index < shown.Count ? rest.IndexOf(shown[index]) : rest.IndexOf(shown[shown.Count - 1]) + 1;
        rest.Insert(at, moving);
        return new(rest);
    }

    public ColumnLayout Resize(QueueColumn column, int width) =>
        new(Columns.Select(c => c.Column == column ? c with { Width = width } : c).ToList());

    /// <summary>Whether two layouts show the same columns in the same order at the same widths.</summary>
    public bool Same(ColumnLayout other) => Columns.SequenceEqual(other.Columns);

    /// <summary>Where <paramref name="library"/> keeps its layout; none without a library that
    /// opened, whose columns are the default and are not written.</summary>
    public static string? FileIn(LibraryView? library) =>
        library is { IsAvailable: true } ? System.IO.Path.Combine(library.Path, Library.StoreFolderName, FileName) : null;

    /// <summary>The layout in <paramref name="path"/>, or the default when there is none to use.</summary>
    public static ColumnLayout Load(string path)
    {
        try
        {
            var file = JsonSerializer.Deserialize<ColumnsFile>(File.ReadAllText(path), JsonOptions);
            return file?.Columns is { Count: > 0 } columns && columns.All(c => c is not null) ? new(columns) : Default;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return Default;
        }
    }

    /// <summary>Writes the layout atomically. A write that fails is dropped: the library opens on
    /// the last good write, or the default.</summary>
    public void Save(string path)
    {
        try
        {
            AtomicJson.Write(path, new ColumnsFile(Columns), JsonOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Every column once, # first and shown, each width in range; a column the file does
    /// not name joins at the end, hidden, as one a later version adds would.</summary>
    private static IReadOnlyList<ColumnSetting> Normalise(IReadOnlyList<ColumnSetting> columns)
    {
        var defaults = DefaultColumns.ToDictionary(c => c.Column, c => c.Width);
        var seen = new HashSet<QueueColumn>();
        var result = new List<ColumnSetting>();
        foreach (var c in columns.Where(c => ShellCompat.IsDefined(c.Column) && seen.Add(c.Column)))
        {
            result.Add(c with { Width = c.Width is >= MinWidth and <= MaxWidth ? c.Width : defaults[c.Column] });
        }
        foreach (var missing in ShellCompat.Values<QueueColumn>().Where(seen.Add))
        {
            result.Add(new(missing, false, defaults[missing]));
        }
        var number = result.First(c => c.Column == QueueColumn.Number) with { Visible = true };
        result.RemoveAll(c => c.Column == QueueColumn.Number);
        result.Insert(0, number);
        return result;
    }

    private sealed record ColumnsFile(IReadOnlyList<ColumnSetting> Columns);
}
