using Fadem.Core.Domain;
using Fadem.Core.Persistence;
using Fadem.Core.Playback;
using Fadem.Core.Transitions;

namespace Fadem.Core.Tests;

/// <summary>
/// ADR 0018: Fadem was called faderio, and what it wrote under that name is renamed on first
/// use, never copied and never lost: a library's <c>.faderio/</c>, the machine folder, and a
/// playlist's <c>#FADERIO-SECTION</c> lines.
/// </summary>
public sealed class FormerNameTests : IDisposable
{
    private readonly string _root = Path.Combine(TestTemp.Root, "former-name", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static readonly TransitionSpec Crossfade = new() { FadeOutDuration = TimeSpan.FromSeconds(3) };

    /// <summary>A library as faderio left it: its store under <c>.faderio/</c>, one transition
    /// authored and one playlist saved.</summary>
    private string FaderioLibrary()
    {
        var folder = Path.Combine(_root, "Vinyl rips");
        var store = Path.Combine(folder, ".faderio");
        Directory.CreateDirectory(Path.Combine(store, "playlists"));
        new JsonTransitionStore(Path.Combine(store, "transitions.json")).Upsert(@"C:\a.wav", @"C:\b.wav", Crossfade);
        File.WriteAllText(Path.Combine(store, "playlists", "Evening.m3u8"), "#EXTM3U\r\nC:\\a.wav\r\nC:\\b.wav\r\n");
        return folder;
    }

    [Fact]
    public void ALibraryFromBeforeTheRename_OpensUnderFadem_WithItsTransitionsAndPlaylists()
    {
        var folder = FaderioLibrary();

        using var library = Library.Open(folder);

        Assert.True(library.IsAvailable);
        Assert.Empty(library.Notices);
        Assert.Equal(Crossfade.FadeOutDuration, library.Transitions!.Get(@"C:\a.wav", @"C:\b.wav")!.FadeOutDuration);
        Assert.Equal(new[] { ".fadem" }, Directory.GetFileSystemEntries(folder).Select(Path.GetFileName));
        Assert.True(File.Exists(Path.Combine(folder, ".fadem", "playlists", "Evening.m3u8")));
    }

    [Fact]
    public void ALibraryWithBothFolders_OpensFadems_AndLeavesFaderiosAlone()
    {
        var folder = FaderioLibrary();
        Directory.CreateDirectory(Path.Combine(folder, ".fadem"));

        using var library = Library.Open(folder);

        Assert.True(library.IsAvailable);
        Assert.Null(library.Transitions!.Get(@"C:\a.wav", @"C:\b.wav"));
        Assert.True(File.Exists(Path.Combine(folder, ".faderio", "transitions.json")));
    }

    [Fact]
    public void ALibraryWhoseFormerFolderCannotBeRenamed_OpensUnavailable_AndLosesNothing()
    {
        // A file open in it, as a running faderio keeps its host.lock: Windows will not rename the folder.
        var folder = FaderioLibrary();
        var transitions = Path.Combine(folder, ".faderio", "transitions.json");
        Library library;

        using (new FileStream(transitions, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            library = Library.Open(folder);
        }

        Assert.False(library.IsAvailable);
        Assert.StartsWith("its .faderio folder could not be renamed to .fadem (", library.UnavailableReason);
        Assert.False(Directory.Exists(Path.Combine(folder, ".fadem")), "nothing is written under the new name");
        Assert.True(File.Exists(transitions));

        using var reopened = Library.Open(folder); // once faderio lets go
        Assert.True(reopened.IsAvailable);
        Assert.NotNull(reopened.Transitions!.Get(@"C:\a.wav", @"C:\b.wav"));
    }

    [Fact]
    public void TheMachineFolder_IsRenamedFromFaderio()
    {
        var former = Path.Combine(_root, "faderio");
        Directory.CreateDirectory(former);
        File.WriteAllText(Path.Combine(former, "settings.json"), "{}");

        var folder = FormerName.MachineFolder(_root);

        Assert.Equal(Path.Combine(_root, "fadem"), folder);
        Assert.True(File.Exists(Path.Combine(folder, "settings.json")));
        Assert.False(Directory.Exists(former));
    }

    [Fact]
    public void TheMachineFolder_IsFademsWhenThereIsNoFormerOne_AndIsNotMade()
    {
        Assert.Equal(Path.Combine(_root, "fadem"), FormerName.MachineFolder(_root));
        Assert.False(Directory.Exists(Path.Combine(_root, "fadem")));
    }

    [Fact]
    public void AMachineFolderThatCannotBeRenamed_IsUsedWhereItIs_ForThisRun()
    {
        var former = Path.Combine(_root, "faderio");
        Directory.CreateDirectory(former);
        var settings = Path.Combine(former, "settings.json");
        File.WriteAllText(settings, "{}");

        using (new FileStream(settings, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Equal(former, FormerName.MachineFolder(_root));
        }
        Assert.False(Directory.Exists(Path.Combine(_root, "fadem")));
    }

    [Fact]
    public void APlaylistWrittenByFaderio_KeepsItsSections()
    {
        Directory.CreateDirectory(_root);
        var path = Path.Combine(_root, "Evening.m3u8");
        File.WriteAllLines(path, ["#EXTM3U", "#FADERIO-SECTION:colour=3;collapsed=1", "#EXTGRP:Intro", @"C:\a.wav", @"C:\b.wav"]);

        var file = M3UPlaylist.ReadFile(path);

        Assert.Equal(new SectionHeader(0, "Intro", Colour: 3, Collapsed: true), Assert.Single(file.Sections));
    }
}
