using System.Globalization;

namespace Faderio.Core.Playback;

/// <summary>
/// What adding a folder queues from it (build issue 02; restage ticket 12): every file with a
/// promised extension, recursively, folder by folder. A folder's own files come first, then each
/// subfolder in turn, each level in <see cref="NaturalOrder"/>. Everything else is left out
/// without a word. Hidden and system entries are left out too, as Explorer hides them, and so is
/// a nested symbolic link or junction, which could loop. A nested folder that cannot be read is
/// returned as itself, in its place, so the add tries it, fails, and rejects it with the reason;
/// the folder asked for failing to list throws, which the add reports the same way.
/// </summary>
internal static class FolderContents
{
    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = false };

    public static IReadOnlyList<string> List(string folder)
    {
        var paths = new List<string>();
        Walk(new DirectoryInfo(folder), paths, nested: false);
        return paths;
    }

    private static void Walk(DirectoryInfo folder, List<string> paths, bool nested)
    {
        List<string> files;
        List<DirectoryInfo> subfolders;
        try
        {
            files = folder.EnumerateFiles("*", Listing)
                .Where(f => SupportedAudio.IsPromisedExtension(f.Name))
                .OrderBy(f => f.Name, NaturalOrder.Instance)
                .Select(f => f.FullName)
                .ToList();
            subfolders = folder.EnumerateDirectories("*", Listing)
                .Where(d => d.LinkTarget is null)
                .OrderBy(d => d.Name, NaturalOrder.Instance)
                .ToList();
        }
        catch (Exception e) when (nested && e is IOException or UnauthorizedAccessException)
        {
            paths.Add(folder.FullName);
            return;
        }
        paths.AddRange(files);
        foreach (var subfolder in subfolders)
        {
            Walk(subfolder, paths, nested: true);
        }
    }
}

/// <summary>
/// Orders names the way a person reads them: a run of digits compares as a number, so "2 - …"
/// comes before "10 - …", and the text between runs compares without case. Numbers of any length
/// compare correctly. Names equal in that reading ("1" and "01") fall back to ordinal order, so
/// the order is total and the same on every run.
/// </summary>
internal sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    private static readonly CompareInfo Text = CultureInfo.InvariantCulture.CompareInfo;

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return x is null ? (y is null ? 0 : -1) : 1;
        }
        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            var a = Run(x, ref i);
            var b = Run(y, ref j);
            var digits = char.IsAsciiDigit(a[0]);
            var order = digits && char.IsAsciiDigit(b[0])
                ? CompareNumbers(a, b)
                : Text.Compare(a, b, CompareOptions.IgnoreCase);
            if (order != 0)
            {
                return order;
            }
        }
        var rest = (x.Length - i).CompareTo(y.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(x, y);
    }

    /// <summary>The run of digits, or of anything else, starting at <paramref name="at"/>.</summary>
    private static ReadOnlySpan<char> Run(string s, ref int at)
    {
        var start = at;
        var digits = char.IsAsciiDigit(s[at]);
        while (at < s.Length && char.IsAsciiDigit(s[at]) == digits)
        {
            at++;
        }
        return s.AsSpan(start, at - start);
    }

    private static int CompareNumbers(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.SequenceCompareTo(b);
    }
}
