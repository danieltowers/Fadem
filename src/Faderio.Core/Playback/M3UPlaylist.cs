using System.Text;
using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

/// <summary>
/// M3U with <c>#EXTINF</c>, kept interoperable (ADR 0005): transitions never travel in the
/// playlist file, they live in the store. Behind the engine (build issue 03): <c>Add</c> reads
/// one where a folder would be listed, and <c>SaveAsPlaylist</c> writes one; the library's
/// playlists are kept in it, and <c>ImportPlaylists</c> copies one in (playlists issues 01 and
/// 03). Writing is always UTF-8 with absolute paths, since library folder names carry <c>†</c>
/// and <c>⭐️</c> and the store is keyed by full path.
/// </summary>
internal static class M3UPlaylist
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>The system's ANSI code page, which is what an old <c>.m3u</c> was written in.</summary>
    private static readonly Lazy<Encoding> SystemCodePage = new(() =>
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(0);
    });

    /// <summary>Whether <paramref name="path"/> names a playlist <c>Add</c> opens, in any case.</summary>
    public static bool IsPlaylist(string path) => Extension(path) is ".m3u" or ".m3u8";

    private static string Extension(string path) => Path.GetExtension(path).ToLowerInvariant();

    /// <summary>The playlist's entries in order, each resolved to a full path: a relative entry
    /// against the playlist's own folder, never the working directory. Comments and blank lines
    /// are skipped; <c>#EXTINF</c> is ignored, since the files are the source of truth. An entry
    /// is not checked here: a missing file is rejected by the add like any other. A
    /// <c>.m3u8</c> is UTF-8; a <c>.m3u</c> is UTF-8 if it is valid UTF-8, and otherwise in the
    /// system code page. A byte-order mark wins over both. An entry that is not a path at all
    /// stays as written, so the add rejects that entry alone and the others still load.</summary>
    public static IReadOnlyList<string> Read(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var entries = new List<string>();
        using var reader = new StringReader(Text(path));
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (line.Length > 0 && !line.StartsWith('#'))
            {
                entries.Add(Resolve(line, folder));
            }
        }
        return entries;
    }

    private static string Resolve(string entry, string folder)
    {
        try
        {
            return Path.GetFullPath(entry, folder);
        }
        catch (ArgumentException)
        {
            return entry;
        }
    }

    private static string Text(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (Extension(path) == ".m3u8")
        {
            return Decode(bytes, Utf8);
        }
        try
        {
            return Decode(bytes, StrictUtf8);
        }
        catch (DecoderFallbackException)
        {
            return Decode(bytes, SystemCodePage.Value);
        }
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    /// <summary>Writes <paramref name="tracks"/> in order as UTF-8 without a byte-order mark, each
    /// as an absolute path under an <c>#EXTINF</c> in whole seconds. Written beside the target
    /// under a name of its own and renamed over it, so a failed save never leaves half a playlist
    /// and never touches another file.</summary>
    public static void Write(string path, IReadOnlyList<Track> tracks) =>
        WriteAtomically(path, writer =>
        {
            foreach (var track in tracks)
            {
                var seconds = (int)Math.Round(track.Length.TotalSeconds);
                writer.WriteLine($"#EXTINF:{seconds},{Label(track)}");
                writer.WriteLine(Path.GetFullPath(track.FilePath));
            }
        });

    /// <summary>Writes <paramref name="entries"/> as <see cref="Read"/> returns them, one per line
    /// with no <c>#EXTINF</c>, as <see cref="Write"/> writes: for an import (playlists issue 03),
    /// whose entries are not probed until it opens. Once edited, the playlist is written with
    /// them.</summary>
    public static void WriteEntries(string path, IReadOnlyList<string> entries) =>
        WriteAtomically(path, writer =>
        {
            foreach (var entry in entries)
            {
                writer.WriteLine(entry);
            }
        });

    private static void WriteAtomically(string path, Action<StreamWriter> writeEntries)
    {
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var writer = new StreamWriter(temp, append: false, Utf8))
            {
                writer.WriteLine("#EXTM3U");
                writeEntries(writer);
            }
            File.Move(temp, path, overwrite: true);
        }
        catch
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
            throw;
        }
    }

    /// <summary>"Artist - Title", or the title alone, or the file's name; on one line.</summary>
    private static string Label(Track track)
    {
        var title = string.IsNullOrEmpty(track.Title) ? Path.GetFileNameWithoutExtension(track.FilePath) : track.Title;
        var label = string.IsNullOrEmpty(track.Artist) ? title : $"{track.Artist} - {title}";
        return label.ReplaceLineEndings(" ");
    }
}
