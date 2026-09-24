namespace Faderio.Core.Playback;

/// <summary>The voices sounding at a sample, in the order they started: the outgoing voice
/// first, then, while a transition is in flight, the incoming one.</summary>
internal readonly record struct Sounding(MixerVoice First, MixerVoice? Second);

/// <summary>A stretch of one block's samples beyond full scale, sounded by the same voices: its
/// first and last clipped timeline frames.</summary>
internal readonly record struct ClippedRun(long From, long To, Sounding Voices);

/// <summary>
/// Finds where a mixed block clips, and what sounded there (build issue 17). The mixer notes,
/// frame by frame, each voice it mixes; once the block is mixed, every sample beyond full scale is
/// attributed to the voices noted at its frame. A voice sounds from its start until its fade-out
/// completes, so both voices of a transition sound across the whole overlap, the incoming one
/// from its first frame, even where its equal-power fade-in is still at zero.
/// </summary>
internal sealed class ClipDetector
{
    private MixerVoice?[] _first = [];
    private MixerVoice?[] _second = [];

    /// <summary>Starts a block of <paramref name="count"/> frames, with nothing sounding yet.</summary>
    public void Begin(int count)
    {
        if (_first.Length < count)
        {
            _first = new MixerVoice?[count];
            _second = new MixerVoice?[count];
        }
        Array.Clear(_first, 0, count);
        Array.Clear(_second, 0, count);
    }

    /// <summary>The voice is mixed at the block's <paramref name="frame"/>. Voices are noted in the
    /// order they started; a third is not named.</summary>
    public void Sounds(long frame, MixerVoice voice)
    {
        if (_first[frame] is null)
        {
            _first[frame] = voice;
        }
        else
        {
            _second[frame] ??= voice;
        }
    }

    /// <summary>The block's clipped runs, in timeline frames from <paramref name="start"/>: a new
    /// run wherever the voices sounding at a clipped frame change.</summary>
    public List<ClippedRun> Runs(float[] buffer, int offset, int count, int channels, long start)
    {
        var runs = new List<ClippedRun>();
        for (var f = 0; f < count; f++)
        {
            if (!Clipped(buffer, offset + f * channels, channels) || _first[f] is not { } first)
            {
                continue;
            }
            var voices = new Sounding(first, _second[f]);
            if (runs.Count > 0 && runs[^1].Voices == voices)
            {
                runs[^1] = runs[^1] with { To = start + f };
            }
            else
            {
                runs.Add(new ClippedRun(start + f, start + f, voices));
            }
        }
        return runs;
    }

    /// <summary>A sample beyond full scale in any channel of the frame.</summary>
    private static bool Clipped(float[] buffer, int at, int channels)
    {
        for (var c = 0; c < channels; c++)
        {
            if (Math.Abs(buffer[at + c]) > 1f)
            {
                return true;
            }
        }
        return false;
    }
}
