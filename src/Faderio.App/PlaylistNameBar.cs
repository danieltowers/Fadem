using Faderio.Core.Playback;

namespace Faderio.App;

/// <summary>
/// The open playlist's name above the queue, in a larger font (playlists issue 03, D18). A
/// double-click on it, or F2 in the queue, turns it into a text box: Enter renames the playlist
/// with one engine call, Esc puts the name back, and leaving the box commits it as the sidebar's
/// in-place edit does. The name shown is always <see cref="PlayerView.OpenPlaylist"/>: a rename
/// that succeeds comes back as the view's new name, and one that is refused leaves the old one,
/// with the engine's notice saying why.
/// </summary>
internal sealed class PlaylistNameBar : Panel
{
    private readonly PlaybackEngine _engine;
    private readonly Font _nameFont;
    private readonly ToolTip _tip;

    internal readonly Label _name;
    internal readonly TextBox _edit;

    /// <summary>The playlist being renamed, while the text box is up; null otherwise.</summary>
    private string? _renaming;

    public PlaylistNameBar(PlaybackEngine engine)
    {
        _engine = engine;

        Dock = DockStyle.Top;
        Height = 36;
        Padding = new Padding(4, 4, 4, 2);

        _nameFont = new Font(Font.FontFamily, 13f);
        _name = new Label
        {
            Dock = DockStyle.Fill,
            Font = _nameFont,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            UseMnemonic = false,
        };
        _edit = new TextBox { Dock = DockStyle.Fill, Font = _nameFont, Visible = false };
        _tip = new ToolTip();
        _tip.SetToolTip(_name, "The open playlist. Double-click or press F2 in the queue to rename it.");
        Disposed += (_, _) =>
        {
            _tip.Dispose();
            _nameFont.Dispose();
        };

        _name.DoubleClick += (_, _) => StartRename();
        _edit.KeyDown += (_, e) =>
        {
            if (e.Modifiers != Keys.None || e.KeyCode is not (Keys.Enter or Keys.Escape))
            {
                return;
            }
            EndRename(commit: e.KeyCode == Keys.Enter);
            e.Handled = true;
            e.SuppressKeyPress = true; // no beep
        };
        _edit.Leave += (_, _) => EndRename(commit: true);

        Controls.Add(_edit);
        Controls.Add(_name);
    }

    /// <summary>Whether the name is being edited.</summary>
    internal bool IsRenaming => _renaming is not null;

    // ── Rendering: read, render, forget ─────────────────────────────────────────────────

    public void Render(PlayerView view)
    {
        if (IsRenaming)
        {
            return; // a render would overwrite what is being typed
        }
        var text = view.OpenPlaylist ?? "No playlist open";
        if (_name.Text != text)
        {
            _name.Text = text;
        }
        var color = view.OpenPlaylist is null ? SystemColors.GrayText : SystemColors.ControlText;
        if (_name.ForeColor != color)
        {
            _name.ForeColor = color;
        }
    }

    // ── Commands: one intent, one engine call ───────────────────────────────────────────

    /// <summary>Turns the name into a text box holding it, all selected. With no playlist open
    /// there is nothing to rename.</summary>
    internal void StartRename()
    {
        if (IsRenaming || _engine.View.OpenPlaylist is not { } name)
        {
            return;
        }
        _renaming = name;
        _edit.Text = name;
        _edit.Visible = true;
        _name.Visible = false;
        _edit.Focus();
        _edit.SelectAll();
    }

    /// <summary>Puts the label back; on a commit, sends the typed name to the engine.</summary>
    private void EndRename(bool commit)
    {
        if (_renaming is not { } old)
        {
            return; // hiding the box leaves it, which ends it again
        }
        _renaming = null;
        var typed = _edit.Text;
        _name.Visible = true;
        _edit.Visible = false;
        if (commit)
        {
            _engine.RenamePlaylist(old, typed);
        }
        Render(_engine.View);
    }
}
