using System.IO;

namespace Fadem.Core;

/// <summary>
/// The file system calls whose net48 stand-ins are real code, not a rename: the replacing move
/// behind every atomic write, and the folder listing's hidden, system and link rules. This is
/// the net8.0 build, which has them all; <c>Compat/Net48</c> holds the other.
/// </summary>
internal static class FileSystemCompat
{
    private static readonly EnumerationOptions Listing = new() { IgnoreInaccessible = false };

    /// <summary>Moves <paramref name="source"/> onto <paramref name="destination"/> in one step,
    /// replacing it if it exists.</summary>
    public static void MoveReplacing(string source, string destination) =>
        File.Move(source, destination, overwrite: true);

    /// <summary>The folder's files, leaving out hidden and system ones.</summary>
    public static IEnumerable<FileInfo> VisibleFiles(DirectoryInfo folder) => folder.EnumerateFiles("*", Listing);

    /// <summary>The folder's subfolders, leaving out hidden and system ones.</summary>
    public static IEnumerable<DirectoryInfo> VisibleFolders(DirectoryInfo folder) =>
        folder.EnumerateDirectories("*", Listing);

    /// <summary>Whether <paramref name="folder"/> is a symbolic link or a junction.</summary>
    public static bool IsLink(DirectoryInfo folder) => folder.LinkTarget is not null;
}
