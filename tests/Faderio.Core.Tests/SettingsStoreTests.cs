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
    public void MissingFile_LoadsBuiltInDefaults_WithoutANotice()
    {
        var store = new SettingsStore(_path);
        Assert.Null(store.LoadNotice);
        Assert.Equal(AudioOutputMode.WasapiShared, store.Settings.OutputMode);
        Assert.Equal(UnauthoredAdjacency.HardCut, store.Settings.UnauthoredAdjacency);
        Assert.Equal(5, store.Settings.DefaultCrossfadeSeconds);
        Assert.True(store.Settings.ReplayGainEnabled);
    }

    [Fact]
    public void SavedSettings_SurviveARestart()
    {
        var settings = new PlayerSettings
        {
            OutputMode = AudioOutputMode.WasapiExclusive,
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 8,
            ReplayGainEnabled = false,
        };
        new SettingsStore(_path).Save(settings);

        var reopened = new SettingsStore(_path);
        Assert.Null(reopened.LoadNotice);
        Assert.Equal(settings, reopened.Settings);
    }

    [Fact]
    public void TheFile_NamesItsChoicesInWords()
    {
        new SettingsStore(_path).Save(new PlayerSettings { UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade });

        var json = File.ReadAllText(_path);
        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"DefaultCrossfade\"", json);
        Assert.Contains("\"WasapiShared\"", json);
    }

    [Fact]
    public void MissingKeys_FallBackToDefaults()
    {
        File.WriteAllText(_path, "{\"defaultCrossfadeSeconds\": 12}");
        var store = new SettingsStore(_path);
        Assert.Null(store.LoadNotice);
        Assert.Equal(new PlayerSettings { DefaultCrossfadeSeconds = 12 }, store.Settings);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(31, 30)]
    [InlineData(-4, 1)]
    public void AnOutOfRangeN_IsBroughtIntoRange(int written, int loaded)
    {
        File.WriteAllText(_path, $"{{\"defaultCrossfadeSeconds\": {written}}}");
        Assert.Equal(loaded, new SettingsStore(_path).Settings.DefaultCrossfadeSeconds);
    }

    [Fact]
    public void ACorruptFile_IsMovedAside_WithANotice_AndDefaultsLoad()
    {
        File.WriteAllText(_path, "{ definitely not json");

        var store = new SettingsStore(_path);

        Assert.Equal(new PlayerSettings(), store.Settings);
        Assert.NotNull(store.LoadNotice);
        Assert.Contains("corrupt", store.LoadNotice, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("settings.json.corrupt", store.LoadNotice);
        Assert.False(File.Exists(_path));
        Assert.Equal("{ definitely not json", File.ReadAllText(_path + ".corrupt")); // still recoverable
    }

    [Fact]
    public void ACorruptFile_ThatCannotBeMovedAside_IsLeftWhereItIs_AndSaysSo()
    {
        File.WriteAllText(_path, "{ definitely not json");
        using var held = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.Read); // no delete, no move

        var store = new SettingsStore(_path);

        Assert.Equal(new PlayerSettings(), store.Settings);
        Assert.Contains("could not be moved aside", store.LoadNotice);
        Assert.False(File.Exists(_path + ".corrupt"));
    }

    [Theory]
    [InlineData("{\"schemaVersion\": 42, \"defaultCrossfadeSeconds\": 9}")]
    [InlineData("{\"schemaVersion\": 2, \"outputMode\": \"Asio\"}")] // a mode this version does not know
    [InlineData("{\"schemaVersion\": 2, \"defaultCrossfadeSeconds\": 7.5}")] // a field that changed shape
    public void ANewerSchema_RaisesANotice_LoadsDefaults_AndLeavesTheFileAlone(string newer)
    {
        File.WriteAllText(_path, newer);

        var store = new SettingsStore(_path);

        Assert.Equal(new PlayerSettings(), store.Settings);
        Assert.Contains("newer", store.LoadNotice);
        Assert.Equal(newer, File.ReadAllText(_path));
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
