using System.IO;

namespace Fadem.Core;

/// <summary>
/// What both targets share of the file system calls: the retry around the replacing move.
/// </summary>
internal static partial class FileSystemCompat
{
    /// <summary>How long a held destination is waited for before the write fails.</summary>
    private static readonly TimeSpan HeldFor = TimeSpan.FromMilliseconds(250);

    /// <summary>Moves <paramref name="source"/> onto <paramref name="destination"/> in one step,
    /// replacing it if it exists. Windows refuses to replace a file another handle has open, in
    /// any sharing mode: a reader holding it a moment (a test polling it, an antivirus scan,
    /// another player) once cost a playlist write. So a refusal is tried again until
    /// <see cref="HeldFor"/> has passed, and only then thrown; the source is kept until it lands.</summary>
    public static void MoveReplacing(string source, string destination)
    {
        var deadline = DateTime.UtcNow + HeldFor;
        while (true)
        {
            try
            {
                MoveReplacingOnce(source, destination);
                return;
            }
            catch (Exception e) when (IsRefused(e) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(10);
            }
        }
    }

    /// <summary>Whether the move was refused because the destination is held: access denied,
    /// or Windows' sharing or lock violation.</summary>
    private static bool IsRefused(Exception e) =>
        e is UnauthorizedAccessException || (e is IOException && (e.HResult & 0xFFFF) is 32 or 33);
}
