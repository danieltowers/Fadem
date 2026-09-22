using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

public sealed class JsonTransitionStoreOverrideTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public JsonTransitionStoreOverrideTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "faderio-store-ovr", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "transitions.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void PerPlaylistOverride_MergesPerParameter_WithFallThroughToGlobal()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(2), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        store.UpsertOverride("My Playlist", @"C:\a.wav", @"C:\b.wav",
            new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(4) });

        var overrideSpec = store.GetOverride("My Playlist", @"C:\a.wav", @"C:\b.wav");
        Assert.NotNull(overrideSpec);
        Assert.Equal(TimeSpan.FromSeconds(4), overrideSpec.FadeOutDuration);
        Assert.Null(overrideSpec.FadeOutPoint); // falls through to the global transition

        var global = store.Get(@"C:\a.wav", @"C:\b.wav");
        Assert.NotNull(global);
        // Merged result: override wins per present parameter; omitted ones fall through.
        Assert.Equal(global!.FadeOutPoint, overrideSpec.FadeOutPoint ?? global.FadeOutPoint);
    }

    [Fact]
    public void OverridesAreRoundTripped()
    {
        var store = new JsonTransitionStore(_path);
        store.UpsertOverride("Mix", @"C:\a.wav", @"C:\b.wav",
            new TransitionSpec { Curve = FadeCurveKind.Linear });
        var reloaded = new JsonTransitionStore(_path);
        var spec = reloaded.GetOverride("Mix", @"C:\a.wav", @"C:\b.wav");
        Assert.NotNull(spec);
        Assert.Equal(FadeCurveKind.Linear, spec.Curve);
    }

    [Fact]
    public void RemoveOverride_DeletesAndCleansUp()
    {
        var store = new JsonTransitionStore(_path);
        store.UpsertOverride("Mix", @"C:\a.wav", @"C:\b.wav", new TransitionSpec());
        Assert.True(store.RemoveOverride("Mix", @"C:\a.wav", @"C:\b.wav"));
        Assert.Null(store.GetOverride("Mix", @"C:\a.wav", @"C:\b.wav"));
        Assert.False(store.RemoveOverride("Mix", @"C:\a.wav", @"C:\b.wav"));
    }
}
