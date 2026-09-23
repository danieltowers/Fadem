using Faderio.Core.Playback;

namespace Faderio.App;

/// <summary>
/// The production <see cref="IEngineSink"/>: marshals every publication to the UI thread through
/// a <see cref="SynchronizationContext"/> created before the form exists, so nothing is dropped
/// (v2's SafeInvoke lost events that arrived before the window handle did). With a null context
/// it delivers inline, which is what the headless-form tests want.
/// </summary>
public sealed class PostingSink : IEngineSink
{
    private readonly SynchronizationContext? _context;

    public PostingSink(SynchronizationContext? context) => _context = context;

    public event Action<PlayerView>? Changed;

    public event Action<string>? Noticed;

    void IEngineSink.Changed(PlayerView view) => Deliver(() => Changed?.Invoke(view));

    void IEngineSink.Notice(string message) => Deliver(() => Noticed?.Invoke(message));

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
