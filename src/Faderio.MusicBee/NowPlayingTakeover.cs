using System.Windows.Forms;

namespace Faderio.MusicBee;

/// <summary>
/// faderio in MusicBee's Now Playing tab (phase 6 issue 08, ADR 0013). No plugin API gives a
/// plugin a tab or says which tab is shown, so this watches MusicBee's main form: a tab switch
/// swaps the form's children, and on Now Playing exactly one child is visible, a view of a type
/// nested in the form's own, which MusicBee builds afresh on each visit and disposes on leaving.
/// <para>Once a change of the form's children has settled, that one view, if the setting is on,
/// has its own children hidden, and kept hidden as MusicBee shows or adds them later, and a
/// faderio view attached over them, docked to fill, so MusicBee's layout sizes it. On leaving, the view is told so before MusicBee disposes it with
/// the container. Anything else is not Now Playing, and is left alone.</para>
/// <para>It fails safe: MusicBee's view is only ever covered, never removed or disposed, so the
/// setting off, the next visit, or the plugin closing gives it back. A single visible child of a type not nested in
/// the form's is a view this rule does not know, from another MusicBee build: it is left alone
/// and reported, the caller saying so once.</para>
/// </summary>
internal sealed class NowPlayingTakeover : IDisposable
{
    private readonly Form _main;
    private readonly Func<bool> _enabled;
    private readonly Func<Control, Control> _attach;
    private readonly Action<Control> _leaving;
    private readonly Action _unrecognised;
    private Control? _taken;
    private Control? _view;

    /// <summary>MusicBee's own children of the view taken, and whether MusicBee last wanted each
    /// shown: what <see cref="Dispose"/> gives back.</summary>
    private readonly Dictionary<Control, bool> _hidden = new();
    private bool _checkPending;
    private bool _disposed;

    /// <param name="main">MusicBee's main form, on whose thread this is made.</param>
    /// <param name="enabled">Whether the setting is on, read at each visit.</param>
    /// <param name="attach">Fills MusicBee's Now Playing view with faderio's, and returns it.</param>
    /// <param name="leaving">faderio's view, as its tab is left, before MusicBee disposes it.</param>
    /// <param name="unrecognised">A view this rule does not know is shown: it was left alone.</param>
    public NowPlayingTakeover(Form main, Func<bool> enabled, Func<Control, Control> attach, Action<Control> leaving, Action unrecognised)
    {
        _main = main;
        _enabled = enabled;
        _attach = attach;
        _leaving = leaving;
        _unrecognised = unrecognised;
        _main.ControlAdded += OnChildrenChanged;
        _main.ControlRemoved += OnChildRemoved;
        CheckWhenSettled(); // MusicBee may start on the Now Playing tab
    }

    /// <summary>The plugin closes (MusicBee closing, or the plugin disabled): a view still covered
    /// is given back, its children shown as MusicBee last wanted them. faderio's view is the
    /// host's to dispose.</summary>
    public void Dispose()
    {
        _disposed = true;
        _main.ControlAdded -= OnChildrenChanged;
        _main.ControlRemoved -= OnChildRemoved;
        if (_taken is { } taken)
        {
            taken.ControlAdded -= OnOwnAdded;
            _taken = null; // the keep-hidden handlers let go
            foreach (var own in _hidden.Where(o => !o.Key.IsDisposed))
            {
                own.Key.Visible = own.Value;
            }
        }
        _hidden.Clear();
        _view = null;
    }

    private void OnChildrenChanged(object? sender, ControlEventArgs e) => CheckWhenSettled();

    /// <summary>Synchronous: MusicBee disposes the view it removed right after.</summary>
    private void OnChildRemoved(object? sender, ControlEventArgs e)
    {
        if (e.Control == _taken)
        {
            _taken.ControlAdded -= OnOwnAdded;
            _taken = null;
            _hidden.Clear();
            if (_view is { IsDisposed: false } view)
            {
                _leaving(view);
            }
            _view = null;
        }
        CheckWhenSettled();
    }

    /// <summary>A switch removes and adds several children: the rule is read once they are all in.</summary>
    private void CheckWhenSettled()
    {
        if (_checkPending || !_main.IsHandleCreated)
        {
            return;
        }
        _checkPending = true;
        _main.BeginInvoke(new Action(() =>
        {
            _checkPending = false;
            TakeOverIfNowPlaying();
        }));
    }

    /// <summary>The rule, read once the form's children have settled.</summary>
    private void TakeOverIfNowPlaying()
    {
        if (_disposed || _main.IsDisposed || !_enabled())
        {
            return;
        }
        var shown = _main.Controls.Cast<Control>().Where(c => c.Visible).ToList();
        if (shown.Count != 1 || shown[0] == _taken)
        {
            return;
        }
        var view = shown[0];
        if (view.GetType().DeclaringType != _main.GetType())
        {
            _unrecognised();
            return;
        }
        foreach (Control own in view.Controls)
        {
            KeepHidden(own);
        }
        _taken = view;
        _view = _attach(view);
        _view.BringToFront();
        view.ControlAdded += OnOwnAdded;
    }

    /// <summary>MusicBee shows some of its view's children later (the biography's scroll strip,
    /// once its text has loaded): each stays hidden while faderio covers the view.</summary>
    private void KeepHidden(Control own)
    {
        _hidden[own] = own.Visible;
        own.Visible = false;
        own.VisibleChanged += (_, _) =>
        {
            if (own.Visible && own.Parent == _taken && _taken is not null)
            {
                _hidden[own] = true;
                own.Visible = false;
            }
        };
    }

    /// <summary>A child MusicBee adds to its view after faderio's goes under faderio's, hidden.</summary>
    private void OnOwnAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control == _view)
        {
            return;
        }
        KeepHidden(e.Control);
        _view?.BringToFront();
    }
}
