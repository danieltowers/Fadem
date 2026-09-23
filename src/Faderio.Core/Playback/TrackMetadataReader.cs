using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

/// <summary>
/// Reads track metadata and ReplayGain tags via TagLibSharp: ID3v2 (TXXX), Vorbis/Xiph
/// comments (FLAC) and APE tags. Tracks without ReplayGain tags play at unity gain.
/// </summary>
internal static class TrackMetadataReader
{
    public const string ReplayGainTrackGainKey = "REPLAYGAIN_TRACK_GAIN";

    /// <summary>Title, artist and ReplayGain from the tags. Length is not read here: the probe
    /// issues it from the stream header (ADR 0010), so a track has exactly one length.</summary>
    public static Track Read(string filePath, Length length)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            var tag = file.Tag;
            return new Track
            {
                FilePath = filePath,
                Title = string.IsNullOrWhiteSpace(tag.Title) ? Path.GetFileNameWithoutExtension(filePath) : tag.Title,
                Artist = tag.FirstPerformer ?? "",
                Length = length,
                ReplayGainDb = ReadReplayGainDb(file),
            };
        }
        catch (Exception e) when (e is TagLib.CorruptFileException or TagLib.UnsupportedFormatException)
        {
            // Tags are a convenience; a file whose tags cannot be read still plays under its name.
            return new Track { FilePath = filePath, Title = Path.GetFileNameWithoutExtension(filePath), Length = length };
        }
    }

    /// <summary>Reads REPLAYGAIN_TRACK_GAIN from whichever tag family the file carries.</summary>
    public static double? ReadReplayGainDb(TagLib.File file)
    {
        var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
        if (id3v2 is not null)
        {
            // REPLAYGAIN_TRACK_GAIN lives in a TXXX user-text frame, keyed by description. v2 asked
            // for it as a four-letter frame id and threw on every file carrying an ID3v2 tag; no
            // v2 test reached this line through a real file.
            var frame = TagLib.Id3v2.UserTextInformationFrame.Get(id3v2, ReplayGainTrackGainKey, false);
            var parsed = ReplayGain.ParseDb(frame?.Text is { Length: > 0 } text ? text[0] : null);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        var xiph = file.GetTag(TagLib.TagTypes.Xiph) as TagLib.Ogg.XiphComment;
        if (xiph is not null)
        {
            var fields = xiph.GetField(ReplayGainTrackGainKey);
            var parsed = ReplayGain.ParseDb(fields is { Length: > 0 } ? fields[0] : null);
            if (parsed is not null)
            {
                return parsed;
            }
        }

        var ape = file.GetTag(TagLib.TagTypes.Ape) as TagLib.Ape.Tag;
        if (ape is not null)
        {
            var item = ape.GetItem(ReplayGainTrackGainKey);
            var parsed = ReplayGain.ParseDb(item?.ToString());
            if (parsed is not null)
            {
                return parsed;
            }
        }

        return null;
    }
}
