using Faderio.Core.Domain;
using Faderio.Core.Playback;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Build issue 09: a store that cannot be written is a notice, never an exception, so
/// <c>Author</c> and <c>Unauthor</c> stay total. A failed save changes nothing: not the store,
/// not the pending boundary.
/// </summary>
public sealed class StoreFailureTests
{
    private const int Rate = 44100;

    /// <summary>Holds transitions in memory and refuses every write, as a full disk or a
    /// read-only file does.</summary>
    private sealed class FailingStore : ITransitionStore
    {
        public Exception Failure { get; set; } = new IOException("The disk is full.");

        public TransitionSpec? Get(string outgoingPath, string incomingPath) => null;

        public void Upsert(string outgoingPath, string incomingPath, TransitionSpec spec) => throw Failure;

        public bool Remove(string outgoingPath, string incomingPath) => throw Failure;

        public TransitionSpec? GetOverride(string playlistName, string outgoingPath, string incomingPath) => null;

        public void UpsertOverride(string playlistName, string outgoingPath, string incomingPath, TransitionSpec partial) =>
            throw Failure;

        public bool RemoveOverride(string playlistName, string outgoingPath, string incomingPath) => throw Failure;

        public void RenamePlaylist(string oldName, string newName) => throw Failure;

        public void RemovePlaylist(string playlistName) => throw Failure;
    }

    private static TransitionSpec Crossfade() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    /// <summary>A written file's name as notices word it (the writer numbers each file).</summary>
    private static string Name(string path) => Path.GetFileNameWithoutExtension(path);

    private static (PlaybackEngine Engine, RecordingSink Sink, FakeAudioOutput Output) EngineOn(ITransitionStore store)
    {
        var sink = new RecordingSink();
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, store, sink) { StartAddWorker = work => work() };
        return (engine, sink, output);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    [InlineData(typeof(NotSupportedException))]
    public void Author_WithAStoreThatCannotBeWritten_RaisesANotice_AndDoesNotThrow(Type failure)
    {
        var store = new FailingStore { Failure = (Exception)Activator.CreateInstance(failure, "Access denied.")! };
        var (engine, sink, _) = EngineOn(store);
        using var _ = engine;
        var a = TestAudio.WriteDcWav("alpha.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 3, 0.2);
        engine.Add(new[] { a, b }).Wait();
        var adjacency = new Adjacency(engine.View.Queue[0].Track, engine.View.Queue[1].Track);

        engine.Author(adjacency, Crossfade());

        var notice = Assert.Single(sink.Notices);
        Assert.StartsWith($"Not saved: {Name(a)} → {Name(b)}.", notice);
        Assert.Contains("Access denied.", notice);
    }

    [Fact]
    public void Unauthor_WithAStoreThatCannotBeWritten_RaisesANotice_AndDoesNotThrow()
    {
        var (engine, sink, _) = EngineOn(new FailingStore());
        using var _ = engine;
        var a = TestAudio.WriteDcWav("alpha.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 3, 0.2);
        engine.Add(new[] { a, b }).Wait();
        var adjacency = new Adjacency(engine.View.Queue[0].Track, engine.View.Queue[1].Track);

        engine.Unauthor(adjacency);

        var notice = Assert.Single(sink.Notices);
        Assert.StartsWith($"Not removed: {Name(a)} → {Name(b)}.", notice);
        Assert.Contains("The disk is full.", notice);
    }

    [Fact]
    public void Author_Succeeding_SaysSo()
    {
        // The outcome is the engine's to report, so a failure can never follow a "Saved".
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteDcWav("alpha.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 3, 0.2);
        tc.Add(a, b);

        tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade());
        tc.Engine.Unauthor(tc.AdjacencyOf(a, b));

        Assert.Equal(new[] { $"Saved {Name(a)} → {Name(b)}.", $"Removed {Name(a)} → {Name(b)}." }, tc.Notices);
    }

    [Fact]
    public void Author_OnAReadOnlyStoreFile_RaisesANotice_AndLeavesTheBoundaryAsItWas()
    {
        using var tc = TestEngine.Create();
        var a = TestAudio.WriteToneWav("a.wav", 4, 1000);
        var b = TestAudio.WriteToneWav("b.wav", 4, 2000);
        var c = TestAudio.WriteToneWav("c.wav", 4, 3000);
        tc.Store.Upsert(b, c, Crossfade()); // the file exists before it is made read-only
        var file = tc.Library.TransitionsPath;
        File.SetAttributes(file, FileAttributes.ReadOnly);
        try
        {
            tc.Play(a, b, c); // no transition authored: the boundary is a hard cut at 4.0 s
            tc.Output.Advance(Rate / 2);

            tc.Engine.Author(tc.AdjacencyOf(a, b), Crossfade());
            tc.Output.Advance(Rate); // to 1.5 s

            var notice = Assert.Single(tc.Notices);
            Assert.StartsWith($"Not saved: {Name(a)} → {Name(b)}.", notice);
            Assert.Null(tc.Store.Get(a, b)); // nothing half-saved
            Assert.Equal(0.4, tc.Rendered.ToneAmplitude(1000, 66150), 2); // A plays on, unfaded
            Assert.True(tc.Rendered.ToneAmplitude(2000, 66150) < 0.05);
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }
    }
}
