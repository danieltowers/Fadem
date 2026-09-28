using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Core.Transitions;

namespace Faderio.Core.Tests;

/// <summary>
/// Library issue 01 (ADR 0011): a library is a folder, and everything faderio writes for it goes
/// to <c>&lt;library&gt;/.faderio/</c>. A folder that cannot be opened is a notice: the engine
/// holds no store, refuses authoring, and still plays what is queued.
/// </summary>
public sealed class LibraryTests : IDisposable
{
    private const int Rate = 44100;
    private readonly List<string> _folders = new();

    public void Dispose()
    {
        foreach (var folder in _folders)
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    private string EmptyFolder(string name = "My Library")
    {
        var folder = Path.Combine(TestTemp.Root, "library", Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(folder);
        _folders.Add(Path.GetDirectoryName(folder)!);
        return folder;
    }

    private static (PlaybackEngine Engine, RecordingSink Sink, FakeAudioOutput Output) EngineOn(Library library)
    {
        var sink = new RecordingSink();
        var output = new FakeAudioOutput();
        var engine = new PlaybackEngine(output, library, sink) { StartAddWorker = work => work() };
        return (engine, sink, output);
    }

    private static Adjacency AddPair(PlaybackEngine engine, string a, string b)
    {
        var report = engine.Add(new[] { a, b }).Result;
        return new Adjacency(report.Added[0].Track, report.Added[1].Track);
    }

    private static TransitionSpec Crossfade() => new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(1),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(1),
    };

    private static int At(double seconds) => (int)(seconds * Rate);

    [Fact]
    public void OpeningAnEmptyFolder_MakesTheStoreFolderAndPlaylists_AndTheStoreOnlyOnTheFirstAuthoring()
    {
        var folder = EmptyFolder();
        var library = Library.Open(folder);

        Assert.True(Directory.Exists(Path.Combine(folder, ".faderio")));
        Assert.True(Directory.Exists(Path.Combine(folder, ".faderio", "playlists")));
        Assert.False(File.Exists(Path.Combine(folder, ".faderio", "transitions.json")));

        var (engine, sink, _) = EngineOn(library);
        using var _ = engine;
        var adjacency = AddPair(engine, TestAudio.WriteDcWav("lib-a.wav", 3, 0.4), TestAudio.WriteDcWav("lib-b.wav", 3, 0.2));
        engine.Author(adjacency, Crossfade());

        Assert.Equal($"Saved {adjacency.Name}.", Assert.Single(sink.Notices));
        Assert.Equal(new[] { ".faderio" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
        // settings.json remembers the Untitled the rows made (playlists issue 01).
        Assert.Equal(
            new[] { "playlists", "settings.json", "transitions.json" },
            Directory.GetFileSystemEntries(Path.Combine(folder, ".faderio")).Select(Path.GetFileName).Order());
    }

    [Fact]
    public void TheView_NamesTheLibraryByItsFolder()
    {
        var folder = EmptyFolder("Vinyl rips");
        var (engine, _, _) = EngineOn(Library.Open(folder + Path.DirectorySeparatorChar));
        using var _ = engine;

        Assert.Equal(new LibraryView(folder, "Vinyl rips", IsAvailable: true), engine.View.Library);
    }

    [Fact]
    public void AnEngineOnAStoreOfItsOwn_HasNoLibrary()
    {
        var folder = EmptyFolder();
        using var engine = new PlaybackEngine(
            new FakeAudioOutput(), new JsonTransitionStore(Path.Combine(folder, "t.json")), new RecordingSink());

        Assert.Null(engine.View.Library);
    }

    [Fact]
    public void TwoLibraries_AuthoredInTurn_EachHaveOnlyTheirOwnTransitions()
    {
        var first = EmptyFolder("First");
        var second = EmptyFolder("Second");
        var a = TestAudio.WriteDcWav("two-a.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("two-b.wav", 3, 0.2);

        foreach (var (folder, outgoing, incoming) in new[] { (first, a, b), (second, b, a) })
        {
            var (engine, _, _) = EngineOn(Library.Open(folder));
            using var _ = engine;
            engine.Author(AddPair(engine, outgoing, incoming), Crossfade());
        }

        var firstStore = Library.Open(first).Transitions!;
        var secondStore = Library.Open(second).Transitions!;
        Assert.NotNull(firstStore.Get(a, b));
        Assert.Null(firstStore.Get(b, a));
        Assert.NotNull(secondStore.Get(b, a));
        Assert.Null(secondStore.Get(a, b));
    }

    [Fact]
    public void ACorruptTransitionsFile_IsMovedAsideBesideIt_Visibly()
    {
        var folder = EmptyFolder();
        var path = Path.Combine(folder, ".faderio", "transitions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        var (engine, sink, _) = EngineOn(Library.Open(folder));
        using var _ = engine;

        Assert.Contains("moved aside to transitions.json.corrupt", Assert.Single(sink.Notices));
        Assert.Equal("{ this is not json", File.ReadAllText(path + ".corrupt"));
        Assert.True(engine.View.Library!.IsAvailable);
    }

    [Fact]
    public void ACorruptTransitionsFileThatCannotBeMovedAside_IsANotice_AndWritesGoBesideIt()
    {
        // Build issue 09: the move-aside's failure was swallowed, and the fresh store then
        // reopened the same corrupt file and threw at launch.
        var folder = EmptyFolder();
        var path = Path.Combine(folder, ".faderio", "transitions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");
        Library library;

        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) // no delete, so no move
        {
            library = Library.Open(folder);
            library.Transitions!.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(1) });
        }

        Assert.Contains("could not be moved aside", Assert.Single(library.Notices));
        Assert.Equal("{ this is not json", File.ReadAllText(path)); // left untouched
        Assert.False(File.Exists(path + ".corrupt"));
        Assert.True(File.Exists(path + ".recovered"), "writes go to a fresh file beside it");
    }

    [Fact]
    public void ATransitionsFileFromANewerSchema_IsLeftAlone_AndWritesGoBesideIt()
    {
        var folder = EmptyFolder();
        var path = Path.Combine(folder, ".faderio", "transitions.json");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string newer = """{ "schemaVersion": 99, "transitions": {} }""";
        File.WriteAllText(path, newer);

        var library = Library.Open(folder);
        library.Transitions!.Upsert(@"C:\a.wav", @"C:\b.wav", new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(1) });

        Assert.Contains("your file is left untouched", Assert.Single(library.Notices));
        Assert.Equal(newer, File.ReadAllText(path));
        Assert.True(File.Exists(path + ".recovered"));
    }

    [Fact]
    public void AReadOnlyFolder_OpensUnavailable_WithANotice_AndTheQueueStillPlays()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }
        var folder = EmptyFolder("Locked");
        var a = TestAudio.WriteToneWav("locked-a.wav", 3, 1000);
        var b = TestAudio.WriteToneWav("locked-b.wav", 3, 2000);
        Library library;
        using (DenyWrites(folder))
        {
            library = Library.Open(folder);
        }

        Assert.False(library.IsAvailable);
        Assert.False(Directory.Exists(Path.Combine(folder, ".faderio")));
        var (engine, sink, output) = EngineOn(library);
        using var _ = engine;
        Assert.Equal(new LibraryView(folder, "Locked", IsAvailable: false), engine.View.Library);
        var notice = Assert.Single(sink.Notices);
        Assert.StartsWith("The library Locked could not be opened (", notice);
        Assert.Contains(library.UnavailableReason!, notice);

        var adjacency = AddPair(engine, a, b);
        engine.Play();
        output.Advance(At(1));
        Assert.Equal(Transport.Playing, engine.View.Transport);
        Assert.True(new RenderedAudio(output.Blocks).ToneAmplitude(1000, At(1)) > 0.3, "the queue plays");

        engine.Author(adjacency, Crossfade());
        engine.Unauthor(adjacency);
        engine.ChangeSettings(new PlayerSettings { DefaultCrossfadeSeconds = 4 });

        Assert.Equal(
            new[]
            {
                $"Not saved: {adjacency.Name}. The library Locked could not be opened ({library.UnavailableReason}).",
                $"Not removed: {adjacency.Name}. The library Locked could not be opened ({library.UnavailableReason}).",
                $"Not saved: settings. The library Locked could not be opened ({library.UnavailableReason}).",
            },
            sink.Notices.Skip(1));
        Assert.Null(engine.Transition(adjacency).Global);
        Assert.Equal(new PlayerSettings(), engine.View.Settings);
    }

    [Fact]
    public void AFolderThatIsGone_OpensUnavailable()
    {
        var folder = Path.Combine(EmptyFolder(), "never made");

        var library = Library.Open(folder);

        Assert.False(library.IsAvailable);
        Assert.Null(library.Settings);
        Assert.Equal("the folder does not exist", library.UnavailableReason);
        Assert.False(Directory.Exists(folder), "opening does not make the folder");
    }

    [Fact]
    public void AnEngineWithNoLibrary_RefusesAuthoring_AndSaysSo()
    {
        var sink = new RecordingSink();
        using var engine = new PlaybackEngine(new FakeAudioOutput(), store: null, sink) { StartAddWorker = work => work() };
        var adjacency = AddPair(engine, TestAudio.WriteDcWav("none-a.wav", 3, 0.4), TestAudio.WriteDcWav("none-b.wav", 3, 0.2));

        engine.Author(adjacency, Crossfade());

        Assert.Equal($"Not saved: {adjacency.Name}. No library is open.", Assert.Single(sink.Notices));
    }

    /// <summary>Denies the current user creating anything in the folder, until disposed. The
    /// folder's read-only attribute would not do: Windows ignores it on folders.</summary>
    [SupportedOSPlatform("windows")]
    private static IDisposable DenyWrites(string folder)
    {
        var info = new DirectoryInfo(folder);
        var rule = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!,
            FileSystemRights.CreateDirectories | FileSystemRights.CreateFiles,
            AccessControlType.Deny);
        var acl = info.GetAccessControl();
        acl.AddAccessRule(rule);
        info.SetAccessControl(acl);
        return new Undo(() =>
        {
            var restored = info.GetAccessControl();
            restored.RemoveAccessRule(rule);
            info.SetAccessControl(restored);
        });
    }

    private sealed class Undo(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }
}
