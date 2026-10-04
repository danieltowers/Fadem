using Faderio.Core.Domain;
using Faderio.Core.Settings;

namespace Faderio.Core.Playback;

/// <summary>
/// ReplayGain track gain, applied in the per-track gain chain before fade multipliers so
/// envelopes stay intact. Clamped symmetrically at ±12 dB (the prototype clamped only
/// positive gain — deliberately not repeated).
/// </summary>
public static class ReplayGain
{
    public const double ClampLimitDb = 12.0;

    public static double ClampDb(double gainDb) => Compat.Clamp(gainDb, -ClampLimitDb, ClampLimitDb);

    public static double DbToMultiplier(double gainDb) => Math.Pow(10, gainDb / 20.0);

    /// <summary>The gain <paramref name="track"/> plays at under <paramref name="mode"/>, unclamped;
    /// null for unity. <paramref name="inAlbumRun"/>: the track is heard beside another of its
    /// album, which <see cref="ReplayGainMode.Smart"/> plays at album gain. A track without an
    /// album gain plays at its track gain.</summary>
    public static double? DbFor(Track track, ReplayGainMode mode, bool inAlbumRun) => mode switch
    {
        ReplayGainMode.Track => track.ReplayGainDb,
        ReplayGainMode.Album => track.AlbumReplayGainDb ?? track.ReplayGainDb,
        ReplayGainMode.Smart when inAlbumRun => track.AlbumReplayGainDb ?? track.ReplayGainDb,
        ReplayGainMode.Smart => track.ReplayGainDb,
        _ => null,
    };

    /// <summary>Whether two tracks are of one album: both name it, alike but for case.</summary>
    public static bool SameAlbum(Track a, Track b) =>
        a.Album.Length > 0 && string.Equals(a.Album, b.Album, StringComparison.OrdinalIgnoreCase);

    /// <summary>Parses a REPLAYGAIN_TRACK_GAIN tag value such as "-7.89 dB". Returns null when
    /// the tag is absent or unparseable; untagged tracks play at unity gain.</summary>
    public static double? ParseDb(string? tagValue)
    {
        if (string.IsNullOrWhiteSpace(tagValue))
        {
            return null;
        }
        var text = tagValue.Trim().Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(text, System.Globalization.NumberStyles.Float | System.Globalization.NumberStyles.AllowThousands,
                System.Globalization.CultureInfo.InvariantCulture, out var db)
            ? db
            : null;
    }
}
