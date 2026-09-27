using System.Text.Json;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

public sealed class JsonTransitionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonTransitionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "faderio-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Library.Open(_dir).TransitionsPath; // where a library keeps it
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static TransitionSpec FullSpec() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(12.3), FromEnd: true),
        FadeInPoint = new FadePointSpec(TimeSpan.FromSeconds(4.5), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(2.5),
        FadeInDuration = TimeSpan.FromSeconds(1.75),
        Curve = FadeCurveKind.Linear,
    };

    [Fact]
    public void SaveLoadRoundTrip_PreservesEveryParameter_IncludingFromEndFlags()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());

        var reloaded = new JsonTransitionStore(_path);
        var spec = reloaded.Get(@"C:\a.wav", @"C:\b.wav");
        Assert.NotNull(spec);
        Assert.Equal(TimeSpan.FromSeconds(12.3), spec.FadeOutPoint!.Position);
        Assert.True(spec.FadeOutPoint.FromEnd);
        Assert.Equal(TimeSpan.FromSeconds(4.5), spec.FadeInPoint!.Position);
        Assert.False(spec.FadeInPoint.FromEnd);
        Assert.Equal(TimeSpan.FromSeconds(2.5), spec.FadeOutDuration);
        Assert.Equal(TimeSpan.FromSeconds(1.75), spec.FadeInDuration);
        Assert.Equal(FadeCurveKind.Linear, spec.Curve);
    }

    [Fact]
    public void SchemaVersion_IsFirstField_InTheFile()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());
        var json = File.ReadAllText(_path);
        var trimmedStart = json.TrimStart(' ', '\r', '\n', '\t', '{');
        Assert.StartsWith("\"schemaVersion\"", trimmedStart.TrimStart());
    }

    [Fact]
    public void SchemaVersionIsSerialized_AsOne()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());
        using var doc = JsonDocument.Parse(File.ReadAllText(_path));
        Assert.Equal(1, doc.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void LookupByOrderedPair_IsDirectional()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { FadeInDuration = TimeSpan.FromSeconds(1) });

        Assert.NotNull(store.Get(@"C:\a.wav", @"C:\b.wav"));
        Assert.Null(store.Get(@"C:\b.wav", @"C:\a.wav")); // the reverse adjacency is unauthored
    }

    [Fact]
    public void WritesLeaveNoTempFile_AndTheStoreStaysValid()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());
        store.Upsert(@"C:\b.wav", @"C:\c.wav", FullSpec());

        Assert.False(File.Exists(_path + ".tmp"));

        var reloaded = new JsonTransitionStore(_path);
        Assert.NotNull(reloaded.Get(@"C:\a.wav", @"C:\b.wav"));
        Assert.NotNull(reloaded.Get(@"C:\b.wav", @"C:\c.wav"));
    }

    [Fact]
    public void UnknownFutureSchemaVersion_IsDetectedAndReported()
    {
        File.WriteAllText(_path, "{\"schemaVersion\": 99, \"transitions\": {}}");
        Assert.Throws<TransitionStoreVersionException>(() => new JsonTransitionStore(_path));
    }

    [Fact]
    public void CorruptStoreFile_IsReported_NotSilentlyIgnored()
    {
        File.WriteAllText(_path, "{ not json at all");
        Assert.Throws<InvalidDataException>(() => new JsonTransitionStore(_path));
    }

    [Fact]
    public void Remove_DeletesTheTransition()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());
        Assert.True(store.Remove(@"C:\a.wav", @"C:\b.wav"));
        Assert.Null(store.Get(@"C:\a.wav", @"C:\b.wav"));
        Assert.False(store.Remove(@"C:\a.wav", @"C:\b.wav"));
    }

    [Fact]
    public void AWriteThatFails_ChangesNothing_AndLeavesNoTempFile()
    {
        // Build issue 09: the engine reports a failed save as "not saved", so the store must
        // not keep in memory what it could not write.
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", FullSpec());
        File.SetAttributes(_path, FileAttributes.ReadOnly);
        try
        {
            var changed = FullSpec() with { FadeOutDuration = TimeSpan.FromSeconds(9) };
            Assert.Throws<UnauthorizedAccessException>(() => store.Upsert(@"C:\a.wav", @"C:\b.wav", changed));
            Assert.Throws<UnauthorizedAccessException>(() => store.Upsert(@"C:\c.wav", @"C:\d.wav", changed));
            Assert.Throws<UnauthorizedAccessException>(() => store.Remove(@"C:\a.wav", @"C:\b.wav"));

            Assert.Equal(TimeSpan.FromSeconds(2.5), store.Get(@"C:\a.wav", @"C:\b.wav")!.FadeOutDuration);
            Assert.Null(store.Get(@"C:\c.wav", @"C:\d.wav"));
            Assert.False(File.Exists(_path + ".tmp"));
        }
        finally
        {
            File.SetAttributes(_path, FileAttributes.Normal);
        }
    }
}
