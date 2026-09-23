using System.Security.AccessControl;
using System.Security.Principal;
using Faderio.Core.Domain;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// <c>Add</c> takes folders as well as files (build issue 02; restage ticket 12): enumerated
/// recursively, only the promised extensions taken, everything else ignored silently, ordered by
/// path folder by folder in natural order. The shell never lists a directory. The trees are real
/// folders in the test's temp dir. Most tests resolve every path to a track without probing, so
/// the files can be empty and the order is all that is observed; the rejection test probes.
/// </summary>
public sealed class AddFoldersTests
{
    /// <summary>Writes each relative path under <paramref name="root"/> as an empty file.</summary>
    private static string Tree(string root, params string[] relativePaths)
    {
        Directory.CreateDirectory(root);
        foreach (var relative in relativePaths)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Array.Empty<byte>());
        }
        return root;
    }

    /// <summary>Every path the add asks to resolve becomes a one-second track, recorded in order.</summary>
    private static List<string> ResolveWithoutProbing(TestEngine tc)
    {
        var resolved = new List<string>();
        tc.Engine.ResolvePath = path =>
        {
            resolved.Add(path);
            return (new Track { FilePath = path, Title = Path.GetFileName(path), Length = Length.FromFrames(44100, 44100) }, null);
        };
        return resolved;
    }

    private static string[] Queued(TestEngine tc, string root) =>
        tc.View.Queue.Select(r => Path.GetRelativePath(root, r.Track.FilePath)).ToArray();

    [Fact]
    public void AFolderOfNestedAlbums_AddsEveryPromisedFile_Recursively_InNaturalPathOrder()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Collection"),
            @"Albums\1983 - Second Album\10 - Closer.flac",
            @"Albums\1983 - Second Album\2 - Middle.mp3",
            @"Albums\1983 - Second Album\1 - Opener.wav",
            @"Albums\1969 - First Album\CD10\1 - Late.flac",
            @"Albums\1969 - First Album\CD2\1 - Early.flac",
            @"Albums\1969 - First Album\1 - Beyond.flac",
            @"intro.mp3");

        var report = tc.Engine.Add(new[] { root }).Result;

        Assert.Equal(new[]
        {
            @"intro.mp3",                            // a folder's own files, then its subfolders
            @"Albums\1969 - First Album\1 - Beyond.flac",
            @"Albums\1969 - First Album\CD2\1 - Early.flac", // "2" before "10", for folders too
            @"Albums\1969 - First Album\CD10\1 - Late.flac",
            @"Albums\1983 - Second Album\1 - Opener.wav",
            @"Albums\1983 - Second Album\2 - Middle.mp3",
            @"Albums\1983 - Second Album\10 - Closer.flac",
        }, Queued(tc, root));
        Assert.Equal(7, report.Added.Count);
        Assert.Empty(report.Rejected);
        Assert.Null(tc.View.Adding);
    }

    [Fact]
    public void NaturalOrder_ComparesDigitRunsAsNumbers_AndLettersWithoutCase()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"),
            "track 10.wav", "Track 9.wav", "track 1.wav", "track 01b.wav", "b.wav", "A.wav",
            "t 100000000000000000000.wav", "t 99999999999999999999.wav");

        tc.Engine.Add(new[] { root }).Wait();

        Assert.Equal(new[]
        {
            "A.wav", "b.wav",
            "t 99999999999999999999.wav", "t 100000000000000000000.wav", // longer than a long
            "track 1.wav", "track 01b.wav", "Track 9.wav", "track 10.wav",
        }, Queued(tc, root));
    }

    [Fact]
    public void OtherExtensions_AreIgnoredWithoutANotice_AndNeverProbed()
    {
        using var tc = TestEngine.Create();
        var resolved = ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"),
            "01 - One.FLAC", "02 - Two.Mp3", "cover.jpg", "rip.log", "video.mkv", "notes.txt",
            "03 - Three.ogg", "04 - Four.m4a", @"Scans\back.png", "no extension");

        var report = tc.Engine.Add(new[] { root }).Result;

        Assert.Equal(new[] { "01 - One.FLAC", "02 - Two.Mp3" }, Queued(tc, root));
        Assert.Equal(2, resolved.Count);
        Assert.Empty(report.Rejected);
        Assert.Empty(tc.Notices);
    }

    [Fact]
    public void APromisedExtensionThatFailsTheProbe_IsARejection()
    {
        using var tc = TestEngine.Create();
        var root = Path.Combine(tc.TempDir, "Album");
        Directory.CreateDirectory(root);
        File.Copy(TestAudio.WriteDcWav("good.wav", 0.2, 0.1), Path.Combine(root, "01 - Good.wav"));
        File.WriteAllText(Path.Combine(root, "02 - Fake.flac"), "not a flac");
        File.WriteAllText(Path.Combine(root, "cover.jpg"), "not a picture either");

        var report = tc.Engine.Add(new[] { root }).Result;

        Assert.Equal(new[] { "01 - Good.wav" }, Queued(tc, root));
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(Path.Combine(root, "02 - Fake.flac"), rejection.Path);
        var notice = Assert.Single(tc.Notices);
        Assert.Contains("02 - Fake.flac", notice);
        Assert.DoesNotContain("cover.jpg", notice);
    }

    [Fact]
    public void AMixOfFilesAndFolders_IsAddedInTheOrderGiven()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Drop"),
            @"loose b.wav", @"Second\2.wav", @"Second\1.wav", @"loose a.wav", @"First\x.mp3");

        var report = tc.Engine.Add(new[]
        {
            Path.Combine(root, "loose b.wav"),
            Path.Combine(root, "Second"),
            Path.Combine(root, "loose a.wav"),
            Path.Combine(root, "First"),
        }).Result;

        Assert.Equal(new[] { "loose b.wav", @"Second\1.wav", @"Second\2.wav", "loose a.wav", @"First\x.mp3" },
            Queued(tc, root));
        Assert.Equal(5, report.Added.Count);
    }

    [Fact]
    public void TheSameFileAddedTwice_GivesTwoRows()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"), "1.wav", "2.wav");
        var one = Path.Combine(root, "1.wav");

        tc.Engine.Add(new[] { one, root }).Wait();
        tc.Engine.Add(new[] { one }).Wait();

        Assert.Equal(new[] { "1.wav", "1.wav", "2.wav", "1.wav" }, Queued(tc, root));
        Assert.Equal(4, tc.View.Queue.Select(r => r.Id).Distinct().Count());
    }

    [Fact]
    public void AFolder_CountsAsItsFilesOnceListed_SoProgressEndsAtTheFileCount()
    {
        using var tc = TestEngine.Create();
        var resolved = ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"), "1.wav", "2.wav", "3.wav", "cover.jpg");
        var seen = new List<AddProgress?>();
        var fake = tc.Engine.ResolvePath;
        tc.Engine.ResolvePath = path =>
        {
            seen.Add(tc.View.Adding);
            return fake(path);
        };

        tc.Engine.Add(new[] { root }).Wait();

        Assert.Equal(new AddProgress?[] { new(0, 3), new(1, 3), new(2, 3) }, seen);
        Assert.Equal(3, resolved.Count);
    }

    [Fact]
    public void AnEmptyFolder_AddsNothing_AndSaysNothing()
    {
        using var tc = TestEngine.Create();
        var root = Tree(Path.Combine(tc.TempDir, "Empty"), "cover.jpg", @"Scans\1.png");

        var report = tc.Engine.Add(new[] { root }).Result;

        Assert.Empty(report.Added);
        Assert.Empty(report.Rejected);
        Assert.Empty(tc.View.Queue);
        Assert.Empty(tc.Notices);
        Assert.Null(tc.View.Adding);
    }

    [Fact]
    public void CancelWhileAFolderIsListed_KeepsTheRowsAlreadyAdded_AndListsNoMore()
    {
        using var tc = TestEngine.Create();
        var resolved = ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Drop"), "a.wav", @"Album\1.wav", @"Album\2.wav");
        tc.Engine.ListFolder = folder =>
        {
            tc.Engine.CancelAdd(); // while the folder is being listed
            return FolderContents.List(folder);
        };

        var report = tc.Engine.Add(new[] { Path.Combine(root, "a.wav"), Path.Combine(root, "Album") }).Result;

        Assert.True(report.Cancelled);
        Assert.Equal(new[] { "a.wav" }, Queued(tc, root));
        Assert.Single(resolved);
        Assert.Null(tc.View.Adding);
    }

    [Fact]
    public void WhileTrackPlays_ListingAFolder_NeverHoldsTheLock()
    {
        using var tc = TestEngine.Create(backgroundAdd: true);
        var playing = TestAudio.WriteDcWav("playing.wav", 30, 0.4);
        tc.Play(playing);
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"), "1.wav", "2.wav");
        var pulled = false;
        tc.Engine.ListFolder = folder =>
        {
            // The audio thread pulls a block while the folder is listed. If the add held the
            // engine lock, the pull would wait for the listing, which waits for the pull.
            pulled = Task.Run(() => tc.Output.Advance(441)).Wait(TimeSpan.FromSeconds(2));
            return FolderContents.List(folder);
        };

        Assert.True(tc.Engine.Add(new[] { root }).Wait(TimeSpan.FromSeconds(10)), "the add never completed");

        Assert.True(pulled, "the audio thread waited on the listing");
        Assert.Equal(3, tc.View.Queue.Count);
        Assert.Equal(Transport.Playing, tc.View.Transport);
    }

    [Fact]
    public void ANestedFolderThatCannotBeRead_IsARejection_NotASilentGap()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Album"), @"CD1\1.wav", @"CD2\1.wav", @"CD3\1.wav");
        var locked = new DirectoryInfo(Path.Combine(root, "CD2"));
        var deny = new FileSystemAccessRule(
            WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(deny);
        locked.SetAccessControl(security);
        try
        {
            var report = tc.Engine.Add(new[] { root }).Result;

            Assert.Equal(new[] { @"CD1\1.wav", @"CD3\1.wav" }, Queued(tc, root));
            var rejection = Assert.Single(report.Rejected);
            Assert.Equal(locked.FullName, rejection.Path);
            Assert.Contains("CD2", Assert.Single(tc.Notices));
        }
        finally
        {
            security.RemoveAccessRule(deny);
            locked.SetAccessControl(security);
        }
    }

    [Fact]
    public void AFolderThatCannotBeListed_IsARejection_AndTheRestOfTheAddGoesOn()
    {
        using var tc = TestEngine.Create();
        ResolveWithoutProbing(tc);
        var root = Tree(Path.Combine(tc.TempDir, "Drop"), @"Locked\1.wav", "after.wav");
        tc.Engine.ListFolder = folder => throw new UnauthorizedAccessException($"Access to the path '{folder}' is denied.");

        var report = tc.Engine.Add(new[] { Path.Combine(root, "Locked"), Path.Combine(root, "after.wav") }).Result;

        Assert.Equal(new[] { "after.wav" }, Queued(tc, root));
        var rejection = Assert.Single(report.Rejected);
        Assert.Equal(Path.Combine(root, "Locked"), rejection.Path);
        Assert.Contains("denied", Assert.Single(tc.Notices));
    }
}
