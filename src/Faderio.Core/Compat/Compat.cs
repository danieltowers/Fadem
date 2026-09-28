using System.Diagnostics;
using System.IO;

namespace Faderio.Core;

/// <summary>
/// Stand-ins for static members net8.0 has and net48 lacks (phase 5, ADR 0012). Each compiles on
/// both targets and behaves as the net8.0 member it replaces, so engine code calls these on
/// both and needs no <c>#if</c>.
/// </summary>
internal static class Compat
{
    /// <summary>As <c>Math.Clamp</c>.</summary>
    public static int Clamp(int value, int min, int max)
    {
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than {max}.");
        }
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>As <c>Math.Clamp</c>.</summary>
    public static long Clamp(long value, long min, long max)
    {
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than {max}.");
        }
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>As <c>Math.Clamp</c>: a NaN value stays NaN.</summary>
    public static double Clamp(double value, double min, double max)
    {
        if (min > max)
        {
            throw new ArgumentException($"'{min}' cannot be greater than {max}.");
        }
        return value < min ? min : value > max ? max : value;
    }

    /// <summary>As <c>Enum.IsDefined&lt;T&gt;</c>.</summary>
    public static bool IsDefined<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value);

    /// <summary>As <c>char.IsAsciiDigit</c>: 0 to 9 only, never another script's digits.</summary>
    public static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    /// <summary>As <c>Stopwatch.GetElapsedTime</c>: the time since a
    /// <see cref="Stopwatch.GetTimestamp"/>.</summary>
    public static TimeSpan ElapsedSince(long timestamp) =>
        new((long)((Stopwatch.GetTimestamp() - timestamp) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));

    /// <summary>As <c>new Guid(ReadOnlySpan&lt;byte&gt;)</c>.</summary>
    public static Guid GuidFrom(ReadOnlySpan<byte> bytes) => new(bytes.ToArray());

    /// <summary>As <c>Path.TrimEndingDirectorySeparator</c>: one trailing separator goes, unless
    /// the path is a root.</summary>
    public static string TrimEndingDirectorySeparator(string path) =>
        path.Length > (Path.GetPathRoot(path)?.Length ?? 0) && IsSeparator(path[path.Length - 1])
            ? path.Substring(0, path.Length - 1)
            : path;

    /// <summary>As <c>Path.GetFullPath(path, basePath)</c> on Windows: a relative
    /// <paramref name="path"/> resolves against <paramref name="basePath"/>, a root-relative one
    /// (<c>\x</c>) against its root, and a drive-relative one (<c>C:x</c>) against it when the
    /// drives match.</summary>
    public static string GetFullPath(string path, string basePath)
    {
        if (IsFullyQualified(path))
        {
            return Path.GetFullPath(path);
        }
        if (path.Length == 0)
        {
            return basePath;
        }
        string combined;
        if (IsSeparator(path[0]))
        {
            combined = Join(Path.GetPathRoot(basePath) ?? "", path.Substring(1));
        }
        else if (path.Length >= 2 && IsDriveLetter(path[0]) && path[1] == ':')
        {
            combined = string.Equals(path.Substring(0, 2), Path.GetPathRoot(basePath)?.TrimEnd('\\', '/'), StringComparison.Ordinal)
                ? Join(basePath, path.Substring(2))
                : path.Insert(2, @"\");
        }
        else
        {
            combined = Join(basePath, path);
        }
        return Path.GetFullPath(combined);
    }

    private static bool IsFullyQualified(string path) =>
        path.Length >= 2 && (IsSeparator(path[0])
            ? path[1] == '?' || IsSeparator(path[1])
            : path.Length >= 3 && path[1] == ':' && IsSeparator(path[2]) && IsDriveLetter(path[0]));

    private static string Join(string first, string second) =>
        first.Length == 0 || second.Length == 0 || IsSeparator(first[first.Length - 1]) || IsSeparator(second[0])
            ? first + second
            : first + @"\" + second;

    private static bool IsSeparator(char c) => c is '\\' or '/';

    private static bool IsDriveLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';
}
