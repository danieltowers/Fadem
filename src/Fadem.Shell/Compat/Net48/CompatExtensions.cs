namespace Fadem.Shell;

/// <summary>Instance members net8.0 has and net48 lacks, as extension methods, so the shell
/// reads the same on both targets.</summary>
internal static class CompatExtensions
{
    public static void Deconstruct<TKey, TValue>(this KeyValuePair<TKey, TValue> pair, out TKey key, out TValue value)
    {
        key = pair.Key;
        value = pair.Value;
    }
}
