using System.IO;
using System.Text.RegularExpressions;

namespace Faderio.Core.Persistence;

/// <summary>
/// The library's playlists (D14): one <c>.m3u8</c> per playlist in
/// <c>&lt;library&gt;/.faderio/playlists/</c>, named by its file name without the extension.
/// Listing the folder is listing the playlists. Names compare as Windows compares file names,
/// ignoring case. External edits of the folder are the user's problem: nothing here watches it.
/// </summary>
internal static partial class PlaylistFolder
{
    public const string Extension = ".m3u8";

    /// <summary>What a new playlist is called, then "Untitled 2", "Untitled 3"… (D15).</summary>
    public const string Untitled = "Untitled";

    public static StringComparer Names => StringComparer.OrdinalIgnoreCase;

    public static string PathOf(string folder, string name) => Path.Combine(folder, name + Extension);

    /// <summary>The playlists in <paramref name="folder"/>, sorted by name ignoring case. A folder
    /// that cannot be listed lists nothing.</summary>
    public static IReadOnlyList<string> List(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder, "*" + Extension)
                .Where(path => string.Equals(Path.GetExtension(path), Extension, StringComparison.OrdinalIgnoreCase))
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Order(Names)
                .ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Why <paramref name="name"/> cannot name a playlist file, worded for the user; null
    /// when it can.</summary>
    public static string? Refusal(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "A playlist needs a name.";
        }
        var invalid = name.Where(Path.GetInvalidFileNameChars().Contains).Distinct().ToList();
        if (invalid.Count > 0)
        {
            var shown = string.Join(" ", invalid.Select(c => char.IsControl(c) ? $"U+{(int)c:X4}" : c.ToString()));
            return $"\"{name}\" cannot name a playlist: a file name cannot contain {shown}.";
        }
        if (name.EndsWith('.'))
        {
            return $"\"{name}\" cannot name a playlist: a file name cannot end with a full stop.";
        }
        if (ReservedName().IsMatch(name))
        {
            return $"\"{name}\" cannot name a playlist: Windows reserves that name.";
        }
        if (name.Length + Extension.Length > 255)
        {
            return "That name is too long for a playlist's file.";
        }
        return null;
    }

    /// <summary>Whether <paramref name="name"/> is one the engine gave: "Untitled" or "Untitled N".
    /// An open one with no rows is deleted on switch and quit (D15).</summary>
    public static bool IsUntitled(string name) => UntitledName().IsMatch(name);

    /// <summary>"Untitled", or else "Untitled N" for the first N from 2 that no playlist has.</summary>
    public static string FirstFreeUntitled(IEnumerable<string> existing) => FirstFree(Untitled, existing);

    /// <summary><paramref name="name"/>, or else "<paramref name="name"/> N" for the first N from 2
    /// that no playlist has.</summary>
    public static string FirstFree(string name, IEnumerable<string> existing)
    {
        var taken = new HashSet<string>(existing, Names);
        if (!taken.Contains(name))
        {
            return name;
        }
        for (var n = 2; ; n++)
        {
            if (!taken.Contains($"{name} {n}"))
            {
                return $"{name} {n}";
            }
        }
    }

    [GeneratedRegex(@"^Untitled( [2-9]| [1-9][0-9]+)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UntitledName();

    [GeneratedRegex(@"^(CON|PRN|AUX|NUL|COM[0-9]|LPT[0-9])$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReservedName();
}
