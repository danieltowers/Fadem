using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Faderio.Core.Playback;

namespace Faderio.Core.Tests;

/// <summary>
/// Runs a piece of this assembly in a process of its own, for code that can kill the process
/// it runs in: an access violation in unsafe code is not an exception .NET can catch, so a test
/// that met one in the test host would take every other test down with it (build issue 21).
/// The test assembly is its own child: the test SDK's generated entry point is switched off
/// (<c>GenerateProgramFile</c>) and <see cref="Main"/> dispatches on its first argument.
/// A child prints <c>key=value</c> lines; the parent reads them back with the exit code.
/// </summary>
public static class ChildProcess
{
    public static int Main(string[] args)
    {
        switch (args.FirstOrDefault())
        {
            case "decode":
                Decode(args[1], args.Length > 2 ? long.Parse(args[2], CultureInfo.InvariantCulture) : null);
                return 0;
            case "play":
                Play(args[1]);
                return 0;
            case "window":
                Window(args[1], long.Parse(args[2], CultureInfo.InvariantCulture), int.Parse(args[3], CultureInfo.InvariantCulture));
                return 0;
            default:
                Console.Error.WriteLine($"Unknown child command: {string.Join(' ', args)}");
                return 2;
        }
    }

    /// <summary>Opens <paramref name="path"/> through the production decoders, seeks if asked,
    /// and reads to the end in the fake output's block size. A managed error opening it is
    /// printed as <c>error</c>: the process lived.</summary>
    private static void Decode(string path, long? seekTo)
    {
        ITrackSource source;
        try
        {
            source = TrackSources.Open(path);
        }
        catch (Exception e)
        {
            Console.WriteLine($"error={e.GetType().Name}");
            return;
        }
        using var _ = source;
        Console.WriteLine($"total={source.TotalFrames}");
        if (seekTo is { } frame)
        {
            source.Position = frame;
            Console.WriteLine($"landed={source.Position}");
        }
        Console.WriteLine($"read={TestAudio.ReadToEnd(source)}");
        Console.WriteLine($"position={source.Position}");
    }

    /// <summary>Opens <paramref name="path"/>, seeks to <paramref name="seekTo"/> and prints
    /// where it landed and the next <paramref name="frames"/> frames read (<see cref="Format"/>),
    /// for a seek that could decode a frame that is not there.</summary>
    private static void Window(string path, long seekTo, int frames)
    {
        using var source = TrackSources.Open(path);
        source.Position = seekTo;
        Console.WriteLine($"landed={source.Position}");
        var buffer = new float[frames * source.WaveFormat.Channels];
        var read = 0;
        int n;
        while (read < frames && (n = source.Read(buffer, read * source.WaveFormat.Channels, frames - read)) > 0)
        {
            read += n;
        }
        Console.WriteLine($"window={Format(buffer.AsSpan(0, read * source.WaveFormat.Channels))}");
    }

    /// <summary>Samples as one line of text that round-trips exactly, for a parent to compare
    /// with what it decoded itself.</summary>
    public static string Format(ReadOnlySpan<float> samples)
    {
        var text = new string[samples.Length];
        for (var i = 0; i < samples.Length; i++)
        {
            text[i] = samples[i].ToString("R", CultureInfo.InvariantCulture);
        }
        return string.Join(',', text);
    }

    /// <summary>Queues <paramref name="path"/> and then a 3 s 0.2 DC WAV on a test engine and
    /// plays both through: prints the notices, where the first track's audio ended and the level
    /// just after it, and the track playing at the end.</summary>
    private static void Play(string path)
    {
        long delivered;
        using (var source = TrackSources.Open(path))
        {
            delivered = source.TotalFrames;
        }
        using var tc = TestEngine.Create();
        var b = TestAudio.WriteDcWav("b.wav", 3, 0.2);
        tc.Play(path, b);
        tc.Output.Advance((int)delivered + 44100);
        Console.WriteLine($"delivered={delivered}");
        Console.WriteLine($"notices={tc.Notices.Count}");
        for (var i = 0; i < tc.Notices.Count; i++)
        {
            Console.WriteLine($"notice{i}={tc.Notices[i]}");
        }
        Console.WriteLine($"after={tc.Rendered.Sample(delivered + 10, 0).ToString(CultureInfo.InvariantCulture)}");
        Console.WriteLine($"current={tc.CurrentTrack?.FilePath}");
    }

    /// <summary>What a child process printed, and how it ended.</summary>
    public sealed record Result(int ExitCode, IReadOnlyDictionary<string, string> Values, string Output)
    {
        public long this[string key] => long.Parse(Values[key], CultureInfo.InvariantCulture);
    }

    /// <summary>Runs this assembly's <see cref="Main"/> with <paramref name="args"/> in a new
    /// process and waits for it (30 s at most).</summary>
    public static Result Run(params string[] args)
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(assembly);
        foreach (var arg in args)
        {
            start.ArgumentList.Add(arg);
        }
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Child process {string.Join(' ', args)} did not finish in 30 s.");
        }
        var values = stdout.Result
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2)
            .ToDictionary(pair => pair[0], pair => pair[1]);
        return new Result(process.ExitCode, values, stdout.Result + stderr.Result);
    }
}
