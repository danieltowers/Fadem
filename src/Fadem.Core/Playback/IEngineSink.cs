namespace Fadem.Core.Playback;

/// <summary>
/// How the shell is told (ADR 0003). Calls are queued under the engine lock and delivered after
/// it is released, so a handler never runs under the lock and may re-enter the engine.
/// <see cref="Changed"/> is coalesced: at most one pending delivery, carrying the latest view.
/// Which thread calls is the output adapter's private fact; a <c>PostingSink</c> in the shell
/// marshals to the UI thread.
/// </summary>
public interface IEngineSink
{
    void Changed(PlayerView view);

    /// <summary>A non-blocking notice: a skipped file, a stale row, a device that stopped.</summary>
    void Notice(string message);
}

/// <summary>Runs handlers inline on the calling thread. The headless Form test uses it: with the
/// fake output there is no render thread, so the whole test is synchronous with no message pump.</summary>
public sealed class ImmediateSink : IEngineSink
{
    private readonly Action<PlayerView>? _changed;
    private readonly Action<string>? _notice;

    public ImmediateSink(Action<PlayerView>? changed = null, Action<string>? notice = null)
    {
        _changed = changed;
        _notice = notice;
    }

    public void Changed(PlayerView view) => _changed?.Invoke(view);

    public void Notice(string message) => _notice?.Invoke(message);
}

/// <summary>Keeps an ordered log of every published view and notice, so a test can assert
/// <em>which</em> version moved the attended row and what else that version said.</summary>
public sealed class RecordingSink : IEngineSink
{
    private readonly List<PlayerView> _views = new();
    private readonly List<string> _notices = new();

    /// <summary>Called on every publication, before it is recorded; a test hooks it to capture
    /// a fact from outside the seam, such as the fake output's emitted frames.</summary>
    public Action<PlayerView>? OnChanged { get; set; }

    public IReadOnlyList<PlayerView> Views => _views;

    public IReadOnlyList<string> Notices => _notices;

    public PlayerView? Latest => _views.Count == 0 ? null : _views[^1];

    public void Changed(PlayerView view)
    {
        OnChanged?.Invoke(view);
        _views.Add(view);
    }

    public void Notice(string message) => _notices.Add(message);
}
