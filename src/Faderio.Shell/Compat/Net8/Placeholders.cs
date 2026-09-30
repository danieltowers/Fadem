namespace Faderio.Shell;

/// <summary>A text box's placeholder: the hint shown while it is empty. net8.0-windows has it
/// on <see cref="TextBox"/>; net48 draws it through the control's cue banner
/// (<c>Compat/Net48</c>).</summary>
internal static class Placeholders
{
    public static string Get(TextBox box) => box.PlaceholderText;

    public static void Set(TextBox box, string text) => box.PlaceholderText = text;
}
