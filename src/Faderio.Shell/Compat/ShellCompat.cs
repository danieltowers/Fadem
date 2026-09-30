namespace Faderio.Shell;

/// <summary>Stand-ins for static members net8.0 has and net48 lacks, for the shell as Core's
/// <c>Compat</c> is for the engine (phase 5 issues 01 and 09): each compiles on both targets
/// and behaves as the member it replaces, so shell code calls these on both with no
/// <c>#if</c>.</summary>
internal static class ShellCompat
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

    /// <summary>As <c>Enum.IsDefined&lt;T&gt;</c>.</summary>
    public static bool IsDefined<T>(T value) where T : struct, Enum => Enum.IsDefined(typeof(T), value);

    /// <summary>As <c>Enum.GetValues&lt;T&gt;</c>.</summary>
    public static T[] Values<T>() where T : struct, Enum => (T[])Enum.GetValues(typeof(T));
}
