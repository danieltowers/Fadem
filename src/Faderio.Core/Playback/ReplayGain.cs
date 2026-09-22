namespace Faderio.Core.Playback;

/// <summary>
/// ReplayGain track gain, applied in the per-track gain chain before fade multipliers so
/// envelopes stay intact. Clamped symmetrically at ±12 dB (the prototype clamped only
/// positive gain — deliberately not repeated).
/// </summary>
public static class ReplayGain
{
    public const double ClampLimitDb = 12.0;

    public static double ClampDb(double gainDb) => Math.Clamp(gainDb, -ClampLimitDb, ClampLimitDb);

    public static double DbToMultiplier(double gainDb) => Math.Pow(10, gainDb / 20.0);

    /// <summary>Parses a REPLAYGAIN_TRACK_GAIN tag value such as "-7.89 dB". Returns null when
    /// the tag is absent or unparseable; untagged tracks play at unity gain.</summary>
    public static double? ParseDb(string? tagValue)
    {
        if (string.IsNullOrWhiteSpace(tagValue))
        {
            return null;
        }
        var text = tagValue.Trim().Replace("dB", "", StringComparison.OrdinalIgnoreCase).Trim();
        return double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var db)
            ? db
            : null;
    }
}
