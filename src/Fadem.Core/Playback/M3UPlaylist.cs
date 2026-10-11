using System.Text;
using Fadem.Core.Domain;

namespace Fadem.Core.Playback;

/// <summary>A section's header in a playlist file (phase 6 issue 09): the index of the entry it
/// starts at, its name, and Fadem's colour and collapsed state for it. A file another player
/// wrote names its groups alone, so its colour is null.</summary>
internal sealed record SectionHeader(int Start, string Name, int? Colour = null, bool Collapsed = false);

/// <summary>A playlist file's entries in order, and the headers of its sections, in order.</summary>
internal sealed record PlaylistFile(IReadOnlyList<string> Entries, IReadOnlyList<SectionHeader> Sections)
{
    public static PlaylistFile Empty { get; } = new([], []);
}

/// <summary>
/// M3U with <c>#EXTINF</c>, kept interoperable (ADR 0005): transitions never travel in the
/// playlist file, they live in the store. Sections do: each is an <c>#EXTGRP</c> line, the
/// extended-M3U group, before its first entry, under a <c>#FADEM-SECTION</c> line with its
/// colour and whether it is collapsed, which other players pass over (ADR 0014). Behind the
/// engine (build issue 03): <c>Add</c> reads one where a folder would be listed, and <c>SaveAsPlaylist</c> writes one; the library's
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
        return CodePages.SystemAnsi();
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
    public static IReadOnlyList<string> Read(string path) => ReadFile(path).Entries;

    private const string Group = "#EXTGRP:";
    private const string FademSection = "#FADEM-SECTION:";

    /// <summary>The section line as faderio wrote it, still read (ADR 0018).</summary>
    private const string FaderioSection = "#FADERIO-SECTION:";

    /// <summary>The section prefix <paramref name="line"/> starts with, or null.</summary>
    private static string? SectionLine(string line) =>
        line.StartsWith(FademSection, StringComparison.OrdinalIgnoreCase) ? FademSection
        : line.StartsWith(FaderioSection, StringComparison.OrdinalIgnoreCase) ? FaderioSection
        : null;

    /// <summary>The playlist's entries as <see cref="Read"/> gives them, and its sections. An
    /// <c>#EXTGRP</c> starts one at the next entry. One naming the group in force with no
    /// <c>#FADEM-SECTION</c> line above it continues that group, since some players write the
    /// group above every entry; one with a blank name is passed over. A header with no entry
    /// after it before the next is dropped.</summary>
    public static PlaylistFile ReadFile(string path)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var entries = new List<string>();
        var sections = new List<SectionHeader>();
        (int? Colour, bool Collapsed)? attributes = null;
        using var reader = new StringReader(Text(path));
        while (reader.ReadLine() is { } rawLine)
        {
            var line = rawLine.Trim();
            if (SectionLine(line) is { } prefix)
            {
                attributes = Attributes(line.Substring(prefix.Length));
            }
            else if (line.StartsWith(Group, StringComparison.OrdinalIgnoreCase))
            {
                var name = line.Substring(Group.Length).Trim();
                var current = sections.Count > 0 ? sections[sections.Count - 1] : null;
                if (name.Length > 0 && (attributes is not null || current?.Name != name))
                {
                    if (current?.Start == entries.Count)
                    {
                        sections.RemoveAt(sections.Count - 1);
                    }
                    sections.Add(new SectionHeader(entries.Count, name, attributes?.Colour, attributes?.Collapsed ?? false));
                }
                attributes = null;
            }
            else if (line.Length > 0 && !line.StartsWith('#'))
            {
                entries.Add(Resolve(line, folder));
                attributes = null;
            }
        }
        if (sections.Count > 0 && sections[sections.Count - 1].Start == entries.Count)
        {
            sections.RemoveAt(sections.Count - 1);
        }
        return new PlaylistFile(entries, sections);
    }

    /// <summary>A <c>#FADEM-SECTION</c> line's <c>colour=N;collapsed=1</c>. What is not
    /// understood is passed over.</summary>
    private static (int? Colour, bool Collapsed) Attributes(string text)
    {
        int? colour = null;
        var collapsed = false;
        foreach (var pair in text.Split(';'))
        {
            var parts = pair.Split('=');
            if (parts.Length != 2)
            {
                continue;
            }
            var (key, value) = (parts[0].Trim(), parts[1].Trim());
            if (key.Equals("colour", StringComparison.OrdinalIgnoreCase) && int.TryParse(value, out var c))
            {
                colour = c;
            }
            else if (key.Equals("collapsed", StringComparison.OrdinalIgnoreCase))
            {
                collapsed = value == "1";
            }
        }
        return (colour, collapsed);
    }

    private static void WriteHeader(StreamWriter writer, SectionHeader header)
    {
        var collapsed = header.Collapsed ? ";collapsed=1" : "";
        writer.WriteLine($"{FademSection}colour={header.Colour ?? 0}{collapsed}");
        writer.WriteLine($"{Group}{header.Name}");
    }

    /// <summary>The headers of <paramref name="sections"/> by the entry they start at.</summary>
    private static Dictionary<int, SectionHeader> ByStart(IReadOnlyList<SectionHeader>? sections) =>
        (sections ?? []).GroupBy(h => h.Start).ToDictionary(g => g.Key, g => g.Last());

    private static string Resolve(string entry, string folder)
    {
        try
        {
            return Compat.GetFullPath(entry, folder);
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
    public static void Write(string path, IReadOnlyList<Track> tracks, IReadOnlyList<SectionHeader>? sections = null) =>
        WriteAtomically(path, writer =>
        {
            var headers = ByStart(sections);
            for (var i = 0; i < tracks.Count; i++)
            {
                var track = tracks[i];
                if (headers.TryGetValue(i, out var header))
                {
                    WriteHeader(writer, header);
                }
                var seconds = (int)Math.Round(track.Length.TotalSeconds);
                writer.WriteLine($"#EXTINF:{seconds},{Label(track)}");
                writer.WriteLine(Path.GetFullPath(track.FilePath));
            }
        });

    /// <summary>Writes <paramref name="entries"/> as <see cref="Read"/> returns them, one per line
    /// with no <c>#EXTINF</c>, as <see cref="Write"/> writes: for an import (playlists issue 03),
    /// whose entries are not probed until it opens. Once edited, the playlist is written with
    /// them.</summary>
    public static void WriteEntries(string path, PlaylistFile file) =>
        WriteAtomically(path, writer =>
        {
            var headers = ByStart(file.Sections);
            for (var i = 0; i < file.Entries.Count; i++)
            {
                if (headers.TryGetValue(i, out var header))
                {
                    WriteHeader(writer, header);
                }
                writer.WriteLine(file.Entries[i]);
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
            FileSystemCompat.MoveReplacing(temp, path);
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
