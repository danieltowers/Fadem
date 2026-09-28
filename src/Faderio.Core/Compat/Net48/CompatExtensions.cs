using System.Buffers;
using System.Globalization;
using System.IO;
using System.Text;

namespace Faderio.Core;

/// <summary>
/// Instance members net8.0 has and net48 lacks, as extensions of the same name and shape, so a
/// call reads the same on both targets. Compiled for net48 only; on net8.0 the real members
/// are found first. Each behaves as the member it stands in for.
/// </summary>
internal static class CompatExtensions
{
    public static int Read(this Stream stream, Span<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            var read = stream.Read(rented, 0, buffer.Length);
            rented.AsSpan(0, read).CopyTo(buffer);
            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public static int ReadAtLeast(this Stream stream, Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
    {
        var total = 0;
        while (total < minimumBytes)
        {
            var read = stream.Read(buffer.Slice(total));
            if (read == 0)
            {
                if (throwOnEndOfStream)
                {
                    throw new EndOfStreamException();
                }
                return total;
            }
            total += read;
        }
        return total;
    }

    public static string GetString(this Encoding encoding, ReadOnlySpan<byte> bytes) => encoding.GetString(bytes.ToArray());

    public static int Compare(this CompareInfo compareInfo, ReadOnlySpan<char> a, ReadOnlySpan<char> b, CompareOptions options) =>
        compareInfo.Compare(a.ToString(), b.ToString(), options);

    public static TValue? GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key)
        where TKey : notnull =>
        dictionary.TryGetValue(key, out var value) ? value : default;

    public static bool TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key))
        {
            return false;
        }
        dictionary.Add(key, value);
        return true;
    }

    public static bool Remove<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, out TValue value)
        where TKey : notnull =>
        dictionary.TryGetValue(key, out value!) && dictionary.Remove(key);

    public static IOrderedEnumerable<T> Order<T>(this IEnumerable<T> source, IComparer<T>? comparer = null) =>
        source.OrderBy(item => item, comparer);

    public static bool StartsWith(this string text, char value) => text.Length > 0 && text[0] == value;

    public static bool EndsWith(this string text, char value) => text.Length > 0 && text[text.Length - 1] == value;

    /// <summary>Every CR, LF, CRLF, NEL, LS, FF and PS becomes <paramref name="replacement"/>.</summary>
    public static string ReplaceLineEndings(this string text, string replacement)
    {
        var result = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            switch (text[i])
            {
                case '\r':
                    if (i + 1 < text.Length && text[i + 1] == '\n')
                    {
                        i++;
                    }
                    result.Append(replacement);
                    break;
                case '\n' or '\f' or '\u0085' or '\u2028' or '\u2029':
                    result.Append(replacement);
                    break;
                default:
                    result.Append(text[i]);
                    break;
            }
        }
        return result.ToString();
    }

    public static string Replace(this string text, string oldValue, string? newValue, StringComparison comparison)
    {
        if (oldValue.Length == 0)
        {
            throw new ArgumentException("String cannot be of zero length.", nameof(oldValue));
        }
        var result = new StringBuilder(text.Length);
        var from = 0;
        int at;
        while ((at = text.IndexOf(oldValue, from, comparison)) >= 0)
        {
            result.Append(text, from, at - from).Append(newValue);
            from = at + oldValue.Length;
        }
        return result.Append(text, from, text.Length - from).ToString();
    }
}
