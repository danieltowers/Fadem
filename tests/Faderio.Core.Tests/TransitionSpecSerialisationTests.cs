using Faderio.Core.Domain;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// The on-disk shape of transitions.json (ADR 0006, imported from v2). Two quirks ticket 05's
/// survey found: the computed IsEmpty leaked into the file, and Curve serialised as a bare
/// integer. The user's real v2 file has both, so loading that shape must keep working.
/// </summary>
public sealed class TransitionSpecSerialisationTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public TransitionSpecSerialisationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "faderio-store", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "transitions.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void IsEmpty_IsComputed_NotStored()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) });

        Assert.DoesNotContain("IsEmpty", File.ReadAllText(_path));
    }

    [Fact]
    public void Curve_IsStoredByName_NotAsAnInteger()
    {
        var store = new JsonTransitionStore(_path);
        store.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { Curve = FadeCurveKind.Linear });

        var text = File.ReadAllText(_path);
        Assert.Contains("\"Linear\"", text);
        Assert.DoesNotContain("\"Curve\": 1", text);
    }

    [Fact]
    public void V2File_WithIntegerCurveAndIsEmpty_StillLoads()
    {
        // The shape v2 actually wrote (ticket 05 survey): PascalCase parameters, "IsEmpty"
        // present, Curve as its enum ordinal.
        File.WriteAllText(_path, """
            {
              "schemaVersion": 1,
              "transitions": {
                "C:\\a.wav|C:\\b.wav": {
                  "FadeOutPoint": { "Position": "00:00:37.1320000", "FromEnd": false },
                  "FadeOutDuration": "00:00:20",
                  "Curve": 1,
                  "IsEmpty": false
                }
              },
              "playlistOverrides": {}
            }
            """);

        var spec = new JsonTransitionStore(_path).Get(@"C:\a.wav", @"C:\b.wav");

        Assert.NotNull(spec);
        Assert.Equal(TimeSpan.FromSeconds(37.132), spec.FadeOutPoint!.Position);
        Assert.Equal(TimeSpan.FromSeconds(20), spec.FadeOutDuration);
        Assert.Equal(FadeCurveKind.Linear, spec.Curve);
    }
}
