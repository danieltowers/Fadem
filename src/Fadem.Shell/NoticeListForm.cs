
using Fadem.Shell.Theme;

namespace Fadem.Shell;

/// <summary>
/// This session's notices (build issue 09), opened from the status strip. A list of every
/// notice in the order first delivered, with its count, and below it the selected one in full,
/// since a rejection can name more files than a row can show. Closing hides it, so it reopens
/// where it was left; it closes for good with the main window.
/// </summary>
internal sealed class NoticeListForm : Form
{
    internal readonly ListView _list;
    internal readonly TextBox _detail;
    private readonly ColumnHeader _message;

    /// <param name="palette">The host's palette (studio theme issue 06), or the built-in one.</param>
    public NoticeListForm(Palette? palette = null)
    {
        Text = "Fadem — notices";
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(640, 320);
        MinimumSize = new Size(360, 200);
        KeyPreview = true;

        _list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            ShowItemToolTips = true,
        };
        _list.Columns.Add("Time", 70);
        _message = _list.Columns.Add("Notice", 480);
        _list.Columns.Add("Count", 50, HorizontalAlignment.Right);
        _list.SelectedIndexChanged += (_, _) => ShowSelected();
        _list.ClientSizeChanged += (_, _) => FitMessageColumn();

        _detail = new TextBox
        {
            Dock = DockStyle.Bottom,
            Height = 72,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
        };

        Controls.Add(_list);
        Controls.Add(_detail);
        QueueRowStyle.Draw(_list, palette ?? Palette.Studio); // a selected row raised, the header in capitals
        StudioTheme.Apply(this, palette ?? Palette.Studio);
        // A list without a handle keeps no selection, and the list is filled before it is first
        // shown, so the latest notice would open unselected.
        _ = _list.Handle;

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                Hide();
            }
        };
    }

    /// <summary>Shows the log's entries. The row the user picked stays picked as notices arrive;
    /// with none picked, or when <paramref name="selectLatest"/>, the latest notice is.</summary>
    public void ShowEntries(NoticeLog log, bool selectLatest)
    {
        var picked = selectLatest || _list.SelectedIndices.Count == 0 ? log.LatestIndex : _list.SelectedIndices[0];
        _list.BeginUpdate();
        _list.Items.Clear();
        foreach (var entry in log.Entries)
        {
            var item = new ListViewItem(entry.FirstDelivered.ToLongTimeString()) { ToolTipText = entry.Message };
            item.SubItems.Add(entry.Message);
            item.SubItems.Add(Presenter.FmtRepeats(entry));
            _list.Items.Add(item);
        }
        _list.EndUpdate();
        if (picked >= 0)
        {
            _list.Items[picked].Selected = true;
            _list.EnsureVisible(picked);
        }
        ShowSelected();
    }

    private void ShowSelected() =>
        _detail.Text = _list.SelectedItems.Count == 1 ? _list.SelectedItems[0].SubItems[1].Text : "";

    /// <summary>The notice column takes whatever width the time and count leave.</summary>
    private void FitMessageColumn()
    {
        var others = _list.Columns.Cast<ColumnHeader>().Where(c => c != _message).Sum(c => c.Width);
        _message.Width = Math.Max(120, _list.ClientSize.Width - others);
    }
}

/// <summary>A status label that takes the strip's spare width and asks for none of its own. Sized
/// to its text, a long notice would push the notice count and the add's Cancel off the strip.
/// </summary>
internal sealed class SpringLabel : ToolStripStatusLabel
{
    public SpringLabel(string text) : base(text) => Spring = true;

    public override Size GetPreferredSize(Size constrainingSize) =>
        base.GetPreferredSize(constrainingSize) with { Width = 0 };
}
