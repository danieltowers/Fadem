using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Faderio.Shell;

/// <summary>A text box's placeholder: the hint shown while it is empty. net48's
/// <see cref="TextBox"/> has no property for it, so the text is kept beside the box and drawn
/// by the native edit control's cue banner, shown while the box is empty whether or not it has
/// the focus, as net8.0-windows draws its own (<c>Compat/Net8</c>).</summary>
internal static class Placeholders
{
    private const int EM_SETCUEBANNER = 0x1501;
    private static readonly ConditionalWeakTable<TextBox, string> Texts = new();

    public static string Get(TextBox box) => Texts.TryGetValue(box, out var text) ? text : "";

    public static void Set(TextBox box, string text)
    {
        Texts.Remove(box);
        Texts.Add(box, text);
        if (box.IsHandleCreated)
        {
            Apply(box, text);
        }
        else
        {
            box.HandleCreated -= Reapply;
            box.HandleCreated += Reapply;
        }
    }

    private static void Reapply(object? sender, EventArgs e)
    {
        if (sender is TextBox box)
        {
            Apply(box, Get(box));
        }
    }

    private static void Apply(TextBox box, string text) =>
        SendMessage(box.Handle, EM_SETCUEBANNER, (IntPtr)1, text);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, string lParam);
}
