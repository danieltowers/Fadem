namespace Fadem.Shell;

/// <summary>
/// Fadem's settings, in either host: a flat ⚙ at the far right of the status strip, after
/// everything else on it (round 2 issue 03). The strip's spring label gives it the right end.
/// </summary>
public static class SettingsGear
{
    public const string Tip = "Fadem settings";

    /// <summary>The ⚙, which <paramref name="open"/> answers.</summary>
    public static ToolStripButton Create(Action open)
    {
        var gear = new ToolStripButton("⚙")
        {
            DisplayStyle = ToolStripItemDisplayStyle.Text,
            Font = new Font("Segoe UI Symbol", 11f),
            ToolTipText = Tip,
            AccessibleName = Tip,
            Margin = new Padding(6, 0, 2, 0),
        };
        gear.Click += (_, _) => open();
        return gear;
    }
}
