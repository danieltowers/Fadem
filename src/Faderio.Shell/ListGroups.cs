using System.Reflection;
using System.Runtime.InteropServices;

namespace Faderio.Shell;

/// <summary>
/// What a list's groups can do natively that WinForms does not expose on both runtimes (phase 6
/// issue 09): collapse and a title image, on Windows since Vista, which net48 has no member for. Each is the
/// ListView message on the group's native id, so it behaves alike under net48 and net8.0, whose
/// own <c>CollapsedState</c> is left at its default so that WinForms never sends a state of its
/// own over this one. The ids also name the groups to custom draw, which the studio theme draws
/// headers by (studio theme issue 07).
/// </summary>
internal static class ListGroups
{
    private const int LVM_FIRST = 0x1000;
    private const int LVM_GETGROUPSTATE = LVM_FIRST + 92;
    private const int LVM_SETIMAGELIST = LVM_FIRST + 3;
    private const int LVM_GETIMAGELIST = LVM_FIRST + 2;
    private const int LVM_SETGROUPINFO = LVM_FIRST + 147;
    private const int LVM_GETGROUPINFO = LVM_FIRST + 149;
    private const int LVSIL_GROUPHEADER = 3;
    private const uint LVGF_TITLEIMAGE = 0x1000;
    private const int LVM_GETGROUPRECT = LVM_FIRST + 98;
    private const int LVGGR_HEADER = 1;
    private const uint LVGF_STATE = 0x4;
    private const uint LVGS_COLLAPSED = 0x1;
    private const uint LVGS_COLLAPSIBLE = 0x8;

    /// <summary>WinForms keeps the native id of a group to itself, on both runtimes.</summary>
    private static readonly PropertyInfo IdProperty =
        typeof(ListViewGroup).GetProperty("ID", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static int IdOf(ListViewGroup group) => (int)IdProperty.GetValue(group)!;

    public static bool IsCollapsed(ListView list, ListViewGroup group) => (State(list, group, LVGS_COLLAPSED) & LVGS_COLLAPSED) != 0;

    public static bool IsCollapsible(ListView list, ListViewGroup group) => (State(list, group, LVGS_COLLAPSIBLE) & LVGS_COLLAPSIBLE) != 0;

    /// <summary>Makes <paramref name="group"/> collapsible, with its chevron, and collapses or
    /// expands it.</summary>
    public static void SetCollapsed(ListView list, ListViewGroup group, bool collapsed)
    {
        var info = new LVGROUP
        {
            cbSize = (uint)Marshal.SizeOf<LVGROUP>(),
            mask = LVGF_STATE,
            stateMask = LVGS_COLLAPSIBLE | LVGS_COLLAPSED,
            state = LVGS_COLLAPSIBLE | (collapsed ? LVGS_COLLAPSED : 0),
        };
        SendMessage(list.Handle, LVM_SETGROUPINFO, (IntPtr)IdOf(group), ref info);
    }

    /// <summary>Gives the list's group headers <paramref name="images"/> to show by index.</summary>
    public static void SetTitleImages(ListView list, ImageList images) =>
        SendMessage(list.Handle, LVM_SETIMAGELIST, (IntPtr)LVSIL_GROUPHEADER, images.Handle);

    /// <summary>The index of the image <paramref name="group"/>'s header shows, or -1 for none.</summary>
    public static int TitleImage(ListView list, ListViewGroup group)
    {
        var info = new LVGROUP { cbSize = (uint)Marshal.SizeOf<LVGROUP>(), mask = LVGF_TITLEIMAGE, iTitleImage = -1 };
        SendMessage(list.Handle, LVM_GETGROUPINFO, (IntPtr)IdOf(group), ref info);
        return info.iTitleImage;
    }

    /// <summary>Shows the image at <paramref name="index"/> in <paramref name="group"/>'s header.</summary>
    public static void SetTitleImage(ListView list, ListViewGroup group, int index)
    {
        var info = new LVGROUP { cbSize = (uint)Marshal.SizeOf<LVGROUP>(), mask = LVGF_TITLEIMAGE, iTitleImage = index };
        SendMessage(list.Handle, LVM_SETGROUPINFO, (IntPtr)IdOf(group), ref info);
    }

    /// <summary>The native image list the group headers show from, or none.</summary>
    public static IntPtr TitleImages(ListView list) =>
        SendMessage(list.Handle, LVM_GETIMAGELIST, (IntPtr)LVSIL_GROUPHEADER, IntPtr.Zero);

    /// <summary>The group with the native id <paramref name="id"/>, as custom draw names it, or null.</summary>
    public static ListViewGroup? WithId(ListView list, int id) =>
        list.Groups.Cast<ListViewGroup>().FirstOrDefault(group => IdOf(group) == id);

    /// <summary><paramref name="group"/>'s header in the list's client area, or empty.</summary>
    public static Rectangle HeaderBounds(ListView list, ListViewGroup group)
    {
        // The message's rectangle names the part asked for in its top on the way in.
        var rect = new RECT { Top = LVGGR_HEADER };
        return SendMessage(list.Handle, LVM_GETGROUPRECT, (IntPtr)IdOf(group), ref rect) != IntPtr.Zero
            ? Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom)
            : Rectangle.Empty;
    }

    /// <summary>The group whose header is at <paramref name="client"/>, or null.</summary>
    public static ListViewGroup? HeaderAt(ListView list, Point client) =>
        list.Groups.Cast<ListViewGroup>().FirstOrDefault(group => HeaderBounds(list, group).Contains(client));

    private static uint State(ListView list, ListViewGroup group, uint mask) =>
        (uint)SendMessage(list.Handle, LVM_GETGROUPSTATE, (IntPtr)IdOf(group), (IntPtr)mask);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct LVGROUP
    {
        public uint cbSize;
        public uint mask;
        public IntPtr pszHeader;
        public int cchHeader;
        public IntPtr pszFooter;
        public int cchFooter;
        public int iGroupId;
        public uint stateMask;
        public uint state;
        public uint uAlign;
        public IntPtr pszSubtitle;
        public uint cchSubtitle;
        public IntPtr pszTask;
        public uint cchTask;
        public IntPtr pszDescriptionTop;
        public uint cchDescriptionTop;
        public IntPtr pszDescriptionBottom;
        public uint cchDescriptionBottom;
        public int iTitleImage;
        public int iExtendedImage;
        public int iFirstItem;
        public uint cItems;
        public IntPtr pszSubsetTitle;
        public uint cchSubsetTitle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVGROUP lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref RECT lParam);
}
