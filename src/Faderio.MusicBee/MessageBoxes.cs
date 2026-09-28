using System.Windows.Forms;

namespace Faderio.MusicBee;

/// <summary>
/// Shows the plugin's messages as message boxes owned by MusicBee's main window. Each is posted
/// to the UI thread's context captured at start-up, so the engine's threads never wait on one,
/// and the message travels as the post's state.
/// </summary>
internal sealed class MessageBoxes
{
    private readonly SynchronizationContext _ui;
    private readonly Func<IntPtr> _owner;

    public MessageBoxes(SynchronizationContext ui, Func<IntPtr> owner)
    {
        _ui = ui;
        _owner = owner;
    }

    public void Show(string message) => _ui.Post(ShowNow, message);

    private void ShowNow(object? message) =>
        MessageBox.Show(new Owner(_owner()), (string)message!, "faderio", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private sealed class Owner : IWin32Window
    {
        public Owner(IntPtr handle) => Handle = handle;

        public IntPtr Handle { get; }
    }
}
