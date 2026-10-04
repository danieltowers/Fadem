using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Faderio.Core;

/// <summary>
/// The file system calls whose net48 stand-ins are real code, not a rename: the replacing move
/// behind every atomic write, and the folder listing's hidden, system and link rules. This is
/// the net48 build; <c>Compat/Net8</c> holds the other, and both keep the same behaviour.
/// </summary>
internal static class FileSystemCompat
{
    private const FileAttributes Skipped = FileAttributes.Hidden | FileAttributes.System;

    // The reparse tags .NET 8's LinkTarget answers for; any other reparse point (a cloud
    // placeholder, a deduplicated file) is an ordinary folder there, and so here.
    private const uint SymbolicLinkTag = 0xA000000C;
    private const uint MountPointTag = 0xA0000003;

    /// <summary>Moves <paramref name="source"/> onto <paramref name="destination"/> in one step,
    /// replacing it if it exists, as .NET 8's overwriting <c>File.Move</c> does: by MoveFileEx,
    /// which never leaves the destination missing or half written. Not
    /// <see cref="File.Replace(string, string, string)"/>, which also carries the old file's
    /// attributes and security over and took some 20 ms for a write that now takes 4.</summary>
    public static void MoveReplacing(string source, string destination)
    {
        if (!MoveFileExW(Path.GetFullPath(source), Path.GetFullPath(destination), ReplaceExisting | CopyAllowed))
        {
            var error = Marshal.GetLastWin32Error();
            var reason = new Win32Exception(error);
            throw error == AccessDenied
                ? new UnauthorizedAccessException($"Access to the path '{destination}' is denied.", reason)
                : new IOException($"Could not move {source} onto {destination}: {reason.Message}", reason);
        }
    }

    private const uint ReplaceExisting = 0x1;
    private const uint CopyAllowed = 0x2;
    private const int AccessDenied = 5;

    /// <summary>The folder's files, leaving out hidden and system ones.</summary>
    public static IEnumerable<FileInfo> VisibleFiles(DirectoryInfo folder) =>
        folder.EnumerateFiles("*").Where(f => (f.Attributes & Skipped) == 0);

    /// <summary>The folder's subfolders, leaving out hidden and system ones.</summary>
    public static IEnumerable<DirectoryInfo> VisibleFolders(DirectoryInfo folder) =>
        folder.EnumerateDirectories("*").Where(d => (d.Attributes & Skipped) == 0);

    /// <summary>Whether <paramref name="folder"/> is a symbolic link or a junction.</summary>
    public static bool IsLink(DirectoryInfo folder)
    {
        if ((folder.Attributes & FileAttributes.ReparsePoint) == 0)
        {
            return false;
        }
        var handle = FindFirstFileW(folder.FullName.TrimEnd(Path.DirectorySeparatorChar), out var found);
        if (handle == InvalidHandle)
        {
            throw new IOException($"Could not read the link at {folder.FullName}.", new Win32Exception());
        }
        FindClose(handle);
        return found.dwReserved0 is SymbolicLinkTag or MountPointTag;
    }

    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Win32FindData
    {
        public uint dwFileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
        public uint nFileSizeHigh;
        public uint nFileSizeLow;
        public uint dwReserved0;
        public uint dwReserved1;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string cFileName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
        public string cAlternateFileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindFirstFileW(string fileName, out Win32FindData findData);

    [DllImport("kernel32.dll")]
    private static extern bool FindClose(IntPtr findFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool MoveFileExW(string existingFileName, string newFileName, uint flags);
}
