using System.Windows.Forms;

namespace Fadem.MusicBee;

/// <summary>
/// MusicBee's UI thread, as a context to post to (phase 5 issue 10). MusicBee initialises its
/// plugins on a worker thread at start, so the context current in <c>Initialise</c> is not the
/// UI thread's: a post through it would run on the thread pool, and a control made there could
/// not be parented to MusicBee's panel. Each post goes instead through MusicBee's main window,
/// found by its handle at the time of the post, which marshals it to the thread that owns the
/// window. With no such window (a headless test, or before it exists) the post goes to the
/// context given as the fallback.
/// </summary>
internal sealed class MainWindowContext : SynchronizationContext
{
    private readonly Func<IntPtr> _mainWindow;
    private readonly SynchronizationContext _fallback;

    public MainWindowContext(Func<IntPtr> mainWindow, SynchronizationContext fallback)
    {
        _mainWindow = mainWindow;
        _fallback = fallback;
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        if (Window() is { } window)
        {
            try
            {
                window.BeginInvoke(d, state);
                return;
            }
            catch (InvalidOperationException)
            {
                // The window went as the post was made: the fallback has it.
            }
        }
        _fallback.Post(d, state);
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        if (Window() is { } window)
        {
            window.Invoke(d, state);
            return;
        }
        _fallback.Send(d, state);
    }

    public override SynchronizationContext CreateCopy() => this;

    /// <summary>MusicBee's main window as a control, when its handle names one made in this
    /// process and still alive.</summary>
    private Control? Window()
    {
        var handle = _mainWindow();
        return handle == IntPtr.Zero ? null : Control.FromHandle(handle) is { IsDisposed: false, IsHandleCreated: true } window ? window : null;
    }
}
