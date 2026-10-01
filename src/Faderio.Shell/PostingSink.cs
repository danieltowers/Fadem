using Faderio.Core.Playback;

namespace Faderio.Shell;

/// <summary>
/// The production <see cref="IEngineSink"/>: marshals every publication to the UI thread through
/// a <see cref="SynchronizationContext"/> created before the form exists, so nothing is dropped
/// (v2's SafeInvoke lost events that arrived before the window handle did). With a null context
/// it delivers inline, which is what the headless-form tests want.
/// </summary>
/// <remarks>
/// <c>Changed</c> is coalesced, as the seam requires: one pending render, which shows whatever
/// view is latest when the UI thread reaches it. The next is posted only once the UI thread has
/// been idle (<see cref="UiThreadIdle"/>). A background add publishes once per file, and WinForms
/// runs every pending post in one pass without pumping, posts queued during the pass included:
/// one render per file held a library's add for seconds, and even one at a time kept the pump
/// from running until the add ended (build issue 20). Native modal loops (moving the window, a
/// message box, a file dialog) raise no idle, so pushed renders wait for them to end; the form's
/// timer renders meanwhile. Notices are all delivered, in order, each after the views published
/// before it.
/// </remarks>
internal sealed class PostingSink : IEngineSink
{
    private readonly SynchronizationContext? _context;

    /// <summary>The view the next render shows; null once it has been taken.</summary>
    private PlayerView? _pending;

    /// <summary>1 from posting a render until the UI thread is next idle.</summary>
    private int _posted;

    public PostingSink(SynchronizationContext? context) => _context = context;

    public event Action<PlayerView>? Changed;

    public event Action<string>? Noticed;

    void IEngineSink.Changed(PlayerView view)
    {
        if (_context is null)
        {
            Changed?.Invoke(view);
            return;
        }
        Interlocked.Exchange(ref _pending, view);
        PostRenderIfPending();
    }

    void IEngineSink.Notice(string message) => Deliver(() =>
    {
        RenderPending(); // so no view published before this notice shows after it
        Noticed?.Invoke(message);
    });

    /// <summary>The UI thread has run out of messages (<c>Application.Idle</c>): a view published
    /// since the last render is posted now.</summary>
    public void UiThreadIdle()
    {
        Interlocked.Exchange(ref _posted, 0);
        PostRenderIfPending();
    }

    /// <summary>Both callers fence their write first (a volatile write could pass the read that
    /// follows it), so a view published as the UI thread goes idle is posted by one or the other.</summary>
    private void PostRenderIfPending()
    {
        if (Volatile.Read(ref _pending) is not null && Interlocked.CompareExchange(ref _posted, 1, 0) == 0)
        {
            _context!.Post(_ => RenderPending(), null);
        }
    }

    private void RenderPending()
    {
        if (Interlocked.Exchange(ref _pending, null) is { } view)
        {
            Changed?.Invoke(view);
        }
    }

    private void Deliver(Action action)
    {
        if (_context is null)
        {
            action();
        }
        else
        {
            _context.Post(_ => action(), null);
        }
    }
}
