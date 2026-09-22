using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public SettingsStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "faderio-settings", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "settings.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void MissingFile_LoadsBuiltInDefaults_WithoutWarnings()
    {
        var result = new SettingsStore(_path).Load();
        Assert.False(result.HasWarning);
        Assert.Equal(AudioOutputMode.WasapiShared, result.Settings.OutputMode);
        Assert.Equal(DefaultAdjacencyBehaviour.HardCut, result.Settings.DefaultAdjacencyBehaviour);
        Assert.Equal(SkipBehaviour.AbandonTransition, result.Settings.SkipBehaviour);
        Assert.Equal(5, result.Settings.PreviewPreRollSeconds);
    }

    [Fact]
    public void RoundTrip_PreservesAllSettings()
    {
        var store = new SettingsStore(_path);
        var settings = new PlayerSettings
        {
            OutputMode = AudioOutputMode.WasapiExclusive,
            DefaultAdjacencyBehaviour = DefaultAdjacencyBehaviour.GlobalDefaultCrossfade,
            DefaultCrossfadeDuration = TimeSpan.FromSeconds(8),
            SkipBehaviour = SkipBehaviour.LetOutgoingFadeFinish,
            ReplayGainEnabled = false,
            PreviewPreRollSeconds = 10,
            VisualiserVisible = false,
        };
        store.Save(settings);

        var result = new SettingsStore(_path).Load();
        Assert.False(result.HasWarning);
        Assert.Equal(settings, result.Settings);
    }

    [Fact]
    public void MissingKeys_FallBackToDefaults()
    {
        File.WriteAllText(_path, "{}");
        var result = new SettingsStore(_path).Load();
        Assert.False(result.HasWarning);
        Assert.Equal(new PlayerSettings(), result.Settings);
    }

    [Fact]
    public void CorruptFile_ProducesAVisibleWarning_AndResetsToDefaults()
    {
        File.WriteAllText(_path, "{ definitely not json");
        var result = new SettingsStore(_path).Load();
        Assert.True(result.HasWarning);
        Assert.Contains("corrupt", result.Warning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(new PlayerSettings(), result.Settings);
    }

    [Fact]
    public void UnknownSchemaVersion_IsReported_AndResetsToDefaults()
    {
        File.WriteAllText(_path, "{\"schemaVersion\": 42}");
        var result = new SettingsStore(_path).Load();
        Assert.True(result.HasWarning);
        Assert.Equal(new PlayerSettings(), result.Settings);
    }
}

public sealed class ReplayGainTests
{
    [Theory]
    [InlineData("-7.89 dB", -7.89)]
    [InlineData("+1.2 dB", 1.2)]
    [InlineData("-3.5db", -3.5)]
    [InlineData("  2.0  ", 2.0)]
    public void ParseDb_ReadsTagValues(string text, double expected)
    {
        Assert.Equal(expected, ReplayGain.ParseDb(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void ParseDb_ReturnsNull_ForMissingOrGarbageTags(string? text)
    {
        Assert.Null(ReplayGain.ParseDb(text));
    }

    [Fact]
    public void Clamping_IsSymmetric_AtPlusAndMinusTwelveDb()
    {
        Assert.Equal(12.0, ReplayGain.ClampDb(24.0));
        Assert.Equal(-12.0, ReplayGain.ClampDb(-24.0));
        Assert.Equal(5.0, ReplayGain.ClampDb(5.0));
        Assert.Equal(-12.0, ReplayGain.ClampDb(-12.0));
    }

    [Fact]
    public void DbToMultiplier_ConvertsToLinearGain()
    {
        Assert.Equal(1.0, ReplayGain.DbToMultiplier(0), 10);
        Assert.Equal(2.0, ReplayGain.DbToMultiplier(6.0206), 3);
    }
}
