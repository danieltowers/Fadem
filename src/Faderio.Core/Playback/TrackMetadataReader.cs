using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

/// <summary>
/// Reads track metadata and ReplayGain tags via TagLibSharp: ID3v2 (TXXX), Vorbis/Xiph
/// comments (FLAC and Ogg), and APE tags. Tracks without ReplayGain tags play at unity gain.
/// </summary>
public static class TrackMetadataReader
{
    public const string ReplayGainTrackGainKey = "REPLAYGAIN_TRACK_GAIN";

    public static Track Read(string filePath)
    {
        using var file = TagLib.File.Create(filePath);
        var tag = file.Tag;
        var duration = file.Properties?.Duration ?? TimeSpan.Zero;

        return new Track
        {
            FilePath = filePath,
            Title = tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
            Artist = tag.FirstPerformer ?? "",
            Duration = duration,
            ReplayGainDb = ReadReplayGainDb(file),
        };
    }

    /// <summary>Reads REPLAYGAIN_TRACK_GAIN from whichever tag family the file carries.</summary>
    public static double? ReadReplayGainDb(TagLib.File file)
    {
        var id3v2 = file.GetTag(TagLib.TagTypes.Id3v2) as TagLib.Id3v2.Tag;
        if (id3v2 is not null)
        {
            var parsed = ReplayGain.ParseDb(id3v2.GetTextAsString(ReplayGainTrackGainKey));
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
