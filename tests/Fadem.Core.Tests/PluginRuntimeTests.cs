using System.Runtime.InteropServices;

namespace Fadem.Core.Tests;

/// <summary>
/// The net48 target stands in for the MusicBee plugin's runtime (phase 5 issue 01): MusicBee is a
/// 32-bit .NET Framework process. Compiled for net48 only; if it fails, the promise tests in the
/// same run were proven somewhere else.
/// </summary>
public sealed class PluginRuntimeTests
{
    [Fact]
    public void TheNet48Target_RunsIn32BitDotNetFramework()
    {
        Assert.StartsWith(".NET Framework 4.8", RuntimeInformation.FrameworkDescription);
        Assert.False(Environment.Is64BitProcess);
    }
}
