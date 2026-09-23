using Faderio.Core.Domain;

namespace Faderio.Core.Queue;

/// <summary>
/// M3U (with EXTINF) save/load — kept interoperable per ADR 0002; transitions never travel
/// in the playlist file, they live in the store.
/// </summary>
public static class M3UPlaylist
{
    public static void Save(string path, IReadOnlyList<Track> tracks)
    {
        using var writer = new StreamWriter(path);
        writer.WriteLine("#EXTM3U");
        foreach (var track in tracks)
        {
            var seconds = (int)Math.Round(track.Length.TotalSeconds);
            var label = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} - {track.Title}";
            writer.WriteLine($"#EXTINF:{seconds},{label}");
            writer.WriteLine(track.FilePath);
        }
    }

    public static IReadOnlyList<string> Load(string path)
    {
        var filePaths = new List<string>();
        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue; // #EXTM3U / #EXTINF are ignored on load; files are the source of truth
            }
            filePaths.Add(line);
        }
        return filePaths;
    }
}
