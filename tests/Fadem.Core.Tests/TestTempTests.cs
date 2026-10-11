using System.Diagnostics;

namespace Fadem.Core.Tests;

/// <summary>The suite's own housekeeping (<see cref="TestTemp"/>): a run's files never outlive
/// it for long, and one run never deletes another's while it runs.</summary>
public sealed class TestTempTests
{
    [Fact]
    public void TheSweep_DeletesTheFoldersOfRunsThatAreGone_AndKeepsARunningOnes()
    {
        var parent = Path.Combine(TestTemp.Root, "sweep", Guid.NewGuid().ToString("N"));
        var running = Path.Combine(parent, TestTemp.RunName(Process.GetCurrentProcess()));
        var gone = Path.Combine(parent, $"{int.MaxValue}-0"); // no process has this id
        var reused = Path.Combine(parent, $"{Process.GetCurrentProcess().Id}-1"); // this id, another start
        var older = Path.Combine(parent, Guid.NewGuid().ToString("N")); // as runs were named before
        foreach (var folder in new[] { running, gone, reused, older })
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "a.wav"), "");
        }

        TestTemp.SweepStale(parent);

        Assert.True(Directory.Exists(running));
        Assert.False(Directory.Exists(gone));
        Assert.False(Directory.Exists(reused));
        Assert.False(Directory.Exists(older));
    }

    [Fact]
    public void EveryRun_WritesUnderItsOwnFolder_InTheSuitesParent()
    {
        Assert.Equal(TestTemp.Parent, Path.GetDirectoryName(TestTemp.Root));
        Assert.StartsWith(TestTemp.Root, TestAudio.Dir);
        Assert.Equal(TestTemp.RunName(Process.GetCurrentProcess()), Path.GetFileName(TestTemp.Root));
    }
}
