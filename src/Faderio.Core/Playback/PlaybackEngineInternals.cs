using Faderio.Core.Domain;

namespace Faderio.Core.Playback;

public sealed partial class PlaybackEngine
{
    /// <summary>Authored fade-in to apply when a track is started mid-transition.</summary>
    private sealed record TransitionSpawn(TimeSpan FadeInPoint, TimeSpan FadeInDuration, FadeCurveKind Curve);

    /// <summary>A scheduled boundary: the outgoing voice's fade and the incoming voice's spawn.</summary>
    private sealed class BoundarySchedule
    {
        public required MixerVoice OutgoingVoice { get; init; }
        public required int OutgoingIndex { get; init; }
        public required int IncomingIndex { get; init; } // -1 → end of sequence (fade-out only)
        public long FadeOutTrigger { get; init; }
        public long FadeOutFrames { get; init; }
        public List<ScheduledEventHandle> Handles { get; } = new();
        public bool Triggered { get; set; }

        /// <summary>Performs the boundary's outgoing fade + incoming spawn; used both by the
        /// scheduled trigger event and directly when the outgoing voice ends exactly at the
        /// trigger (the non-overlapping shape).</summary>
        public Action? SpawnAction { get; set; }
        public bool EndOfSequence => IncomingIndex < 0;
    }

    private void StartTrack(int index, long timelineStart, TransitionSpawn? spawn, HashSet<int>? skipSet)
    {
        var track = Queue.Tracks[index];
        ITrackSource source;
        try
        {
            source = NormalizedTrackSource.Wrap(_sourceFactory.Open(track));
        }
        catch (Exception e)
        {
            RaiseNotice($"Skipped \"{Path.GetFileName(track.FilePath)}\": {e.Message}");
            SkipToNextAfter(index, timelineStart, skipSet);
            return;
        }
        if (source.TotalFrames <= 0)
        {
            source.Dispose();
            RaiseNotice($"Skipped \"{Path.GetFileName(track.FilePath)}\": file contains no audio.");
            SkipToNextAfter(index, timelineStart, skipSet);
            return;
        }

        var sourceStart = spawn is null
            ? 0
            : Math.Clamp(Frames(spawn.FadeInPoint), 0, Math.Max(source.TotalFrames - 1, 0));
        var fadeInFrames = spawn is null ? 0 : Math.Max(0, Frames(spawn.FadeInDuration));

        var voice = _mixer.AddVoice(
            source, timelineStart, sourceStart, BaseGain(track),
            fadeInFrames, spawn?.Curve ?? FadeCurveKind.EqualPower);
        _currentVoice = voice;
        _voiceTrackIndex[voice] = index;
        Queue.SetCurrent(index);
        TrackChanged?.Invoke(this, EventArgs.Empty);
        ScheduleBoundary(index, voice, timelineStart, sourceStart);
    }

    private void SkipToNextAfter(int failedIndex, long timelineStart, HashSet<int>? skipSet)
    {
        // One bad file never stops playback: skip with a non-blocking notice (issue 0014).
        skipSet ??= new HashSet<int>();
        if (!skipSet.Add(failedIndex))
        {
            FinishPlayback();
            return;
        }
        var next = Queue.IndexAfter(failedIndex, Repeat);
        if (next is null || skipSet.Contains(next.Value))
        {
            FinishPlayback();
            return;
        }
        StartTrack(next.Value, timelineStart, null, skipSet);
    }
}
