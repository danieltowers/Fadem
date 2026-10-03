using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Builds a full engine on the fake output with a temp library folder and a
/// <see cref="RecordingSink"/>. Everything a test observes comes through the seam: the view,
/// the recorded publications, the notices and the rendered blocks.
/// </summary>
public sealed class TestEngine : IDisposable
{
    /// <summary>The library folder, deleted with the engine.</summary>
    public string TempDir { get; }
    public Library Library { get; }
    public FakeAudioOutput Output { get; }
    public JsonTransitionStore Store { get; }
    /// <summary>The library's settings.json.</summary>
    public string SettingsPath => Library.SettingsPath;
    public RecordingSink Sink { get; } = new();
    public PlaybackEngine Engine { get; }

    /// <summary>The fake's emitted frames at each publication, parallel to <c>Sink.Views</c>.</summary>
    public List<long> EmittedAtPublication { get; } = new();

    private TestEngine(string tempDir, PlayerSettings? settings, Random? rng, int latency, bool backgroundAdd)
    {
        TempDir = tempDir;
        Directory.CreateDirectory(tempDir);
        Library = Library.Open(tempDir);
        if (settings is not null)
        {
            Library.Settings!.Save(settings);
        }
        Store = Library.Transitions!;
        Output = new FakeAudioOutput { Latency = latency };
        Sink.OnChanged = _ => EmittedAtPublication.Add(Output.FramesEmitted.Count);
        // The next track opens inline: the fake clock runs faster than any background could.
        Engine = new PlaybackEngine(Output, Library, Sink, rng) { StartPrepareWorker = work => work() };
        if (!backgroundAdd)
        {
            Engine.StartAddWorker = work => work();
        }
    }

    /// <summary>The add worker runs inline unless <paramref name="backgroundAdd"/>: <c>Add</c> is
    /// then synchronous and the whole test stays on one thread. Only a test about the lock or
    /// about adding in the background needs a real worker thread.</summary>
    public static TestEngine Create(
        PlayerSettings? settings = null, int? rngSeed = null, int latency = 0, bool backgroundAdd = false)
    {
        var dir = Path.Combine(TestTemp.Root, "engine", Guid.NewGuid().ToString("N"));
        return new TestEngine(
            dir, settings, rngSeed is null ? null : new Random(rngSeed.Value), latency,
            backgroundAdd);
    }

    /// <summary>Opens the next track inline in an engine a test did not build itself, as
    /// <see cref="Create"/> does for its own: for a test outside this assembly, which cannot
    /// reach the seam. Before the engine plays.</summary>
    public static void PrepareInline(PlaybackEngine engine) => engine.StartPrepareWorker = work => work();

    public RenderedAudio Rendered => new(Output.Blocks);

    public PlayerView View => Engine.View;

    public IReadOnlyList<string> Notices => Sink.Notices;

    /// <summary>Adds files through the production probe; every path must be promised.</summary>
    public IReadOnlyList<QueueRow> Add(params string[] filePaths)
    {
        var report = Engine.Add(filePaths).Result;
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

    /// <summary>A track of a stated length for a path that need not exist: for tests that never
    /// play it, only queue, order or save it.</summary>
    public Track TrackOfLength(string filePath, double seconds) => new()
    {
        FilePath = filePath,
        Title = Path.GetFileNameWithoutExtension(filePath),
        Length = Length.FromFrames((long)Math.Round(seconds * 44100), 44100),
    };

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
