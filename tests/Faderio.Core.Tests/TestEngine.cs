using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Builds a full engine wired through the single seam (fake output) with a temp-dir store.
/// </summary>
public sealed class TestEngine : IDisposable
{
    public string TempDir { get; }
    public FakeAudioOutputFactory OutputFactory { get; } = new();
    public JsonTransitionStore Store { get; }
    public PlayerSettings Settings { get; private set; }
    public TransitionResolver Resolver { get; }
    public PlaybackEngine Engine { get; }
    public List<string> Notices { get; } = new();
    public int PlaybackFinishedCount { get; private set; }
    public List<Track> TrackChangedEvents { get; } = new();

    private TestEngine(string tempDir, PlayerSettings settings, Random? rng)
    {
        TempDir = tempDir;
        Directory.CreateDirectory(tempDir);
        Settings = settings;
        Store = new JsonTransitionStore(Path.Combine(tempDir, "transitions.json"));
        Resolver = new TransitionResolver(settings);
        Engine = new PlaybackEngine(
            OutputFactory, new NAudioAudioSourceFactory(), Store, Resolver, settings, rng);
        Engine.Notice += (_, message) => Notices.Add(message);
        Engine.PlaybackFinished += (_, _) => PlaybackFinishedCount++;
        Engine.TrackChanged += (_, _) => TrackChangedEvents.Add(Engine.CurrentTrack!);
    }

    public static TestEngine Create(PlayerSettings? settings = null, int? rngSeed = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "faderio-engine", Guid.NewGuid().ToString("N"));
        return new TestEngine(
            dir, settings ?? new PlayerSettings(), rngSeed is null ? null : new Random(rngSeed.Value));
    }

    /// <summary>The output currently bound to the engine (rebuilt when settings change).</summary>
    public FakeAudioOutput Output =>
        OutputFactory.Created.Count == 0
            ? throw new InvalidOperationException("No output created.")
            : OutputFactory.Created[^1];

    public RenderedAudio Rendered => new(Output.Blocks);

    public Track Track(string filePath) => new Track { FilePath = filePath };

    public void SetQueueOf(params string[] filePaths) =>
        Engine.SetQueue(filePaths.Select(p => new Track { FilePath = p }).ToList());

    public void ApplySettings(PlayerSettings settings)
    {
        Settings = settings;
        Engine.ApplySettings(settings);
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
