using System.Collections;
using System.Text.Json;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

namespace Faderio.Core.Tests;

/// <summary>
/// Every file the engine writes is written beside its target and moved over it in one step, so a
/// write that fails leaves the old file as it was (phase 5 issue 01). net48 has no overwriting
/// move and calls MoveFileEx itself, as .NET 8's does; these run on both targets, so the guarantee is the
/// same in both hosts. The JSON writes (store and settings) and the playlist write each create a
/// missing file, replace an existing one, and survive a failure midway. Moving a corrupt file
/// aside replaces an earlier one the same way.
/// </summary>
public sealed class AtomicWriteTests : IDisposable
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    private readonly string _dir = Path.Combine(TestTemp.Root, "atomic", Guid.NewGuid().ToString("N"));

    public AtomicWriteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string[] Leftovers() => Directory.GetFiles(_dir, "*.tmp");

    // ── JSON: the store and the settings files ─────────────────────────────────────────────

    public sealed record Doc(string Name, int Count);

    /// <summary>Serialises its first property, then throws: a write that fails halfway.</summary>
    public sealed class FailsMidway
    {
        public string Name => "written first";
        public string Broken => throw new InvalidOperationException("failed midway");
    }

    [Fact]
    public void Json_AMissingFile_IsCreated()
    {
        var path = Path.Combine(_dir, "new.json");

        AtomicJson.Write(path, new Doc("a", 1), Options);

        Assert.Equal(new Doc("a", 1), JsonSerializer.Deserialize<Doc>(File.ReadAllText(path)));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void Json_AnExistingFile_IsReplaced()
    {
        var path = Path.Combine(_dir, "existing.json");
        AtomicJson.Write(path, new Doc("a", 1), Options);

        AtomicJson.Write(path, new Doc("b", 2), Options);

        Assert.Equal(new Doc("b", 2), JsonSerializer.Deserialize<Doc>(File.ReadAllText(path)));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Json_AReadOnlyFile_IsRefusedAsAccessDenied_AndLeftAsItWas()
    {
        // What the engine catches as a store that cannot be written, on both targets.
        var path = Path.Combine(_dir, "readonly.json");
        AtomicJson.Write(path, new Doc("a", 1), Options);
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            Assert.Throws<UnauthorizedAccessException>(() => AtomicJson.Write(path, new Doc("b", 2), Options));

            Assert.Equal(new Doc("a", 1), JsonSerializer.Deserialize<Doc>(File.ReadAllText(path)));
            Assert.Empty(Leftovers());
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    }

    [Fact]
    public void Json_AWriteThatFailsMidway_LeavesTheOldFileAsItWas_AndNoTempFile()
    {
        var path = Path.Combine(_dir, "kept.json");
        AtomicJson.Write(path, new Doc("a", 1), Options);
        var before = File.ReadAllBytes(path);

        Assert.Throws<InvalidOperationException>(() => AtomicJson.Write(path, new FailsMidway(), Options));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Leftovers());
    }

    // ── Playlists ───────────────────────────────────────────────────────────────────────────

    private static Track Track(string path) =>
        new() { FilePath = path, Title = Path.GetFileNameWithoutExtension(path), Length = Length.FromFrames(44100 * 3, 44100) };

    /// <summary>Yields its tracks, then throws: a playlist write that fails halfway.</summary>
    private sealed class FailsAfter(params Track[] tracks) : IReadOnlyList<Track>
    {
        public int Count => tracks.Length + 1;
        public Track this[int index] => index < tracks.Length ? tracks[index] : throw new IOException("failed midway");

        public IEnumerator<Track> GetEnumerator()
        {
            foreach (var track in tracks)
            {
                yield return track;
            }
            throw new IOException("failed midway");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Playlist_AMissingFile_IsCreated()
    {
        var path = Path.Combine(_dir, "new.m3u8");

        M3UPlaylist.Write(path, new[] { Track(@"C:\Music\a.flac") });

        Assert.Equal(new[] { @"C:\Music\a.flac" }, M3UPlaylist.Read(path));
        Assert.Empty(Leftovers());
    }

    [Fact]
    public void Playlist_AnExistingFile_IsReplaced()
    {
        var path = Path.Combine(_dir, "existing.m3u8");
        M3UPlaylist.Write(path, new[] { Track(@"C:\Music\a.flac") });

        M3UPlaylist.Write(path, new[] { Track(@"C:\Music\b.flac"), Track(@"C:\Music\c.flac") });

        Assert.Equal(new[] { @"C:\Music\b.flac", @"C:\Music\c.flac" }, M3UPlaylist.Read(path));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void Playlist_AWriteThatFailsMidway_LeavesTheOldFileAsItWas_AndNoTempFile()
    {
        var path = Path.Combine(_dir, "kept.m3u8");
        M3UPlaylist.Write(path, new[] { Track(@"C:\Music\a.flac") });
        var before = File.ReadAllBytes(path);

        Assert.Throws<IOException>(() => M3UPlaylist.Write(path, new FailsAfter(Track(@"C:\Music\b.flac"))));

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(Leftovers());
    }

    // ── Moving a corrupt file aside ─────────────────────────────────────────────────────────

    [Fact]
    public void ACorruptSettingsFile_IsMovedAside_OverAnEarlierCorruptOne()
    {
        var path = Path.Combine(_dir, "settings.json");
        File.WriteAllText(path + ".corrupt", "an earlier corrupt file");
        File.WriteAllText(path, "{ not json");

        var store = new SettingsStore(path);

        Assert.Equal(new PlayerSettings(), store.Settings);
        Assert.False(File.Exists(path));
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }

    [Fact]
    public void ACorruptStore_IsMovedAside_OverAnEarlierCorruptOne()
    {
        var library = Library.Open(_dir);
        var path = library.TransitionsPath;
        File.WriteAllText(path + ".corrupt", "an earlier corrupt file");
        File.WriteAllText(path, "{ not json");

        var reopened = Library.Open(_dir);

        Assert.True(reopened.IsAvailable);
        Assert.Contains("moved aside", Assert.Single(reopened.Notices));
        Assert.Equal("{ not json", File.ReadAllText(path + ".corrupt"));
    }
}
