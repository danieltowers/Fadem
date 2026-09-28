using System.Diagnostics;
using System.Globalization;

namespace Faderio.Core.Tests;

/// <summary>
/// Where every file a test writes goes: one folder per test process,
/// <c>%TEMP%\faderio-tests\&lt;pid&gt;-&lt;start ticks&gt;</c>, deleted when the process exits.
/// A run killed before it could delete its own folder is swept by the next run to start: a
/// folder whose process is no longer running is deleted, and one whose process is (the other
/// test assembly, or this assembly's child process) is left alone. Nothing a test writes may go
/// anywhere else in Temp: the suite writes hundreds of megabytes of audio per run.
/// </summary>
public static class TestTemp
{
    /// <summary>The folder every test process keeps its own folder in.</summary>
    public static string Parent { get; } = Path.Combine(Path.GetTempPath(), "faderio-tests");

    /// <summary>This process's folder: created on first use, deleted at exit.</summary>
    public static string Root { get; } = Create();

    private static string Create()
    {
        SweepStale(Parent);
        var root = Path.Combine(Parent, RunName(Process.GetCurrentProcess()));
        Directory.CreateDirectory(root);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => TryDelete(root);
        return root;
    }

    /// <summary>A process's folder name: its id and start time, so a reused id is not taken for
    /// the process that made the folder.</summary>
    internal static string RunName(Process process) =>
        FormattableString.Invariant($"{process.Id}-{process.StartTime.ToUniversalTime().Ticks}");

    /// <summary>Deletes every folder in <paramref name="parent"/> but those of running processes.
    /// A folder named some other way, as every run before this class named its own, is stale.</summary>
    internal static void SweepStale(string parent)
    {
        if (!Directory.Exists(parent))
        {
            return;
        }
        foreach (var folder in Directory.EnumerateDirectories(parent))
        {
            if (!IsRunning(Path.GetFileName(folder)))
            {
                TryDelete(folder);
            }
        }
    }

    private static bool IsRunning(string runName)
    {
        var parts = runName.Split('-');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ||
            !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
        {
            return false;
        }
        try
        {
            using var process = Process.GetProcessById(id);
            return process.StartTime.ToUniversalTime().Ticks == ticks;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false; // gone, or not ours to read
        }
    }

    /// <summary>Best effort, file by file: a file still held open stays, with its folders, and
    /// the next run's sweep takes it. Everything else goes.</summary>
    private static void TryDelete(string folder)
    {
        try
        {
            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                try { File.Delete(file); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            foreach (var sub in Directory.GetDirectories(folder, "*", SearchOption.AllDirectories).OrderByDescending(d => d.Length).ToArray())
            {
                try { Directory.Delete(sub); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
            Directory.Delete(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
