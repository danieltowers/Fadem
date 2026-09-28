using System.Diagnostics;
using Faderio.Core.Domain;

namespace Faderio.Core.Tests;

/// <summary>
/// What adding a folder leaves out (restage ticket 12), proven on both targets because net48
/// reaches it by other means (phase 5 issue 01): hidden and system entries, as Explorer hides
/// them, and a nested junction, which could loop. net8.0 skips by <c>EnumerationOptions</c> and
/// <c>LinkTarget</c>; net48 by attributes and the reparse tag. The trees are real folders, and
/// every path the add asks to resolve becomes a track without probing, so only the listing is
/// observed. Each junction is removed when its test ends: the sweep of the test folder must
/// never walk through one.
/// </summary>
public sealed class FolderScanTests : IDisposable
{
    private readonly TestEngine _tc = TestEngine.Create();
    private readonly List<string> _junctions = new();

    public void Dispose()
    {
        foreach (var junction in _junctions)
        {
            try { Directory.Delete(junction); } catch (IOException) { } // the link, never its target
        }
        _tc.Dispose();
    }

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

    private string[] Added(string root)
    {
        _tc.Engine.ResolvePath = path =>
            (new Track { FilePath = path, Title = Path.GetFileName(path), Length = Length.FromFrames(44100, 44100) }, null);
        _tc.Engine.Add(new[] { root }).Wait();
        return _tc.View.Queue.Select(r => r.Track.FilePath.Substring(root.Length + 1)).ToArray();
    }

    /// <summary>A junction at <paramref name="link"/> to <paramref name="target"/>. Unlike a
    /// symbolic link, a junction needs no privilege to make.</summary>
    private void Junction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        mklink.StandardOutput.ReadToEnd();
        mklink.WaitForExit();
        _junctions.Add(link);
        Assert.True(mklink.ExitCode == 0 && Directory.Exists(link), $"could not make a junction at {link}");
    }

    [Fact]
    public void HiddenAndSystemFiles_AreLeftOut()
    {
        var root = Tree(Path.Combine(_tc.TempDir, "Album"), "1.flac", "2.flac", "3.flac", "4.flac");
        File.SetAttributes(Path.Combine(root, "2.flac"), FileAttributes.Hidden);
        File.SetAttributes(Path.Combine(root, "3.flac"), FileAttributes.System);

        Assert.Equal(new[] { "1.flac", "4.flac" }, Added(root));
    }

    [Fact]
    public void HiddenAndSystemFolders_AreLeftOut_WithEverythingInThem()
    {
        var root = Tree(Path.Combine(_tc.TempDir, "Albums"), @"A\1.flac", @"B\1.flac", @"C\1.flac", @"D\1.flac");
        new DirectoryInfo(Path.Combine(root, "B")).Attributes |= FileAttributes.Hidden;
        new DirectoryInfo(Path.Combine(root, "C")).Attributes |= FileAttributes.System;

        Assert.Equal(new[] { @"A\1.flac", @"D\1.flac" }, Added(root));
    }

    [Fact]
    public void ANestedJunction_IsLeftOut_WhetherItLoopsOrLeadsElsewhere()
    {
        var elsewhere = Tree(Path.Combine(_tc.TempDir, "Elsewhere"), "away.flac");
        var root = Tree(Path.Combine(_tc.TempDir, "Albums"), @"A\1.flac", "top.flac");
        Junction(Path.Combine(root, "Loop"), root);
        Junction(Path.Combine(root, "Out"), elsewhere);

        Assert.Equal(new[] { "top.flac", @"A\1.flac" }, Added(root));
    }

    [Fact]
    public void AJunctionAskedForByName_IsListed()
    {
        // Only a nested link is left out: one the user picks is where they asked to add from.
        var elsewhere = Tree(Path.Combine(_tc.TempDir, "Elsewhere"), "away.flac");
        var link = Path.Combine(_tc.TempDir, "Link");
        Junction(link, elsewhere);

        Assert.Equal(new[] { "away.flac" }, Added(link));
    }
}
