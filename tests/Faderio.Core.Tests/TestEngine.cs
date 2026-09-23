using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Builds a full engine on the fake output with a temp-dir JSON store and a
/// <see cref="RecordingSink"/>. Everything a test observes comes through the seam: the view,
/// the recorded publications, the notices and the rendered blocks.
/// </summary>
public sealed class TestEngine : IDisposable
{
    public string TempDir { get; }
    public FakeAudioOutput Output { get; }
    public JsonTransitionStore Store { get; }
    public PlayerSettings Settings { get; }
    public RecordingSink Sink { get; } = new();
    public PlaybackEngine Engine { get; }

    /// <summary>The fake's emitted frames at each publication, parallel to <c>Sink.Views</c>.</summary>
    public List<long> EmittedAtPublication { get; } = new();

    private TestEngine(string tempDir, PlayerSettings settings, Random? rng, int latency)
    {
        TempDir = tempDir;
        Directory.CreateDirectory(tempDir);
        Settings = settings;
        Store = new JsonTransitionStore(Path.Combine(tempDir, "transitions.json"));
        Output = new FakeAudioOutput { Latency = latency };
        Sink.OnChanged = _ => EmittedAtPublication.Add(Output.FramesEmitted.Count);
        Engine = new PlaybackEngine(Output, Store, Sink, settings, rng);
    }

    public static TestEngine Create(PlayerSettings? settings = null, int? rngSeed = null, int latency = 0)
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-engine", Guid.NewGuid().ToString("N"));
        return new TestEngine(
            dir, settings ?? new PlayerSettings(), rngSeed is null ? null : new Random(rngSeed.Value), latency);
    }

    public RenderedAudio Rendered => new(Output.Blocks);

    public PlayerView View => Engine.View;

    public IReadOnlyList<string> Notices => Sink.Notices;

    /// <summary>Adds files through the production probe; every path must be promised.</summary>
    public IReadOnlyList<QueueRow> Add(params string[] filePaths)
    {
        var report = Engine.AddTracks(filePaths);
        if (report.Rejected.Count > 0)
        {
            throw new InvalidOperationException("Rejected: " + string.Join("; ", report.Rejected));
        }
        return report.Added;
    }

    /// <summary>Adds and starts playing, the way the shell's add-then-play does.</summary>
    public IReadOnlyList<QueueRow> Play(params string[] filePaths)
    {
        var rows = Add(filePaths);
        Engine.Play();
        return rows;
    }

    /// <summary>A track with an engine-issued length, built without the probe: for tests that
    /// need ReplayGain or a deliberate length.</summary>
    public Track TrackOf(string filePath, double? replayGainDb = null)
    {
        using var source = TrackSources.Open(filePath);
        return new Track
        {
            FilePath = filePath,
            Title = Path.GetFileNameWithoutExtension(filePath),
            Length = Length.FromFrames(source.TotalFrames, source.WaveFormat.SampleRate),
            ReplayGainDb = replayGainDb,
        };
    }

    public RowId RowOf(string filePath) =>
        View.Queue.First(r => r.Track.FilePath == filePath).Id;

    public Adjacency AdjacencyOf(string outgoing, string incoming) =>
        new(View.Queue.First(r => r.Track.FilePath == outgoing).Track,
            View.Queue.First(r => r.Track.FilePath == incoming).Track);

    /// <summary>The attended voice's track, or the current row when stopped.</summary>
    public Track? CurrentTrack =>
        View.Attended?.Row.Track ?? View.Queue.FirstOrDefault(r => r.IsCurrent)?.Track;

    public Playhead Playhead => View.Attended?.Playhead ?? Playhead.Zero;

    /// <summary>How many times a playback run ended on its own: published transitions from a
    /// non-stopped transport to Stopped that no Stop() command caused.</summary>
    public int PlaybackFinishedCount
    {
        get
        {
            var count = 0;
            var previous = Transport.Stopped;
            foreach (var view in Sink.Views)
            {
                if (view.Transport == Transport.Stopped && previous != Transport.Stopped)
                {
                    count++;
                }
                previous = view.Transport;
            }
            return count;
        }
    }

    /// <summary>Every row the attended voice moved to, in publication order.</summary>
    public List<Track> AttendedChanges
    {
        get
        {
            var changes = new List<Track>();
            RowId? last = null;
            foreach (var view in Sink.Views)
            {
                var row = view.Attended?.Row;
                if (row is not null && row.Id != last)
                {
                    changes.Add(row.Track);
                }
                last = row?.Id;
            }
            return changes;
        }
    }

    public void Dispose()
    {
        Engine.Dispose();
        try
        {
            Directory.Delete(TempDir, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
