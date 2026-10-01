using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.MusicBee;

namespace MusicBeePlugin;

/// <summary>
/// The class MusicBee loads (phase 5 issue 03): a second shell on faderio's engine. It plays the
/// open playlist of the library its settings name, through its own output, from its view in a
/// panel MusicBee makes (issue 10) or from a Tools → faderio menu. MusicBee's player never plays
/// it: a start stops MusicBee's first.
/// <para>The engine's dependencies load from a private folder through a handler that
/// <see cref="Initialise"/> registers. MusicBee loads every type in this assembly before calling
/// it, so no type here may need Core to load: none derives from a Core type, implements a Core
/// interface, is a delegate over Core types or holds a Core value type in a field, a lambda's
/// captured local included (proven by LoadingTests). And no method MusicBee calls first touches
/// a Core type: everything that does is in <see cref="Host"/>, reached only through
/// <see cref="Start"/>.</para>
/// </summary>
public partial class Plugin
{
    private readonly PluginInfo _about = new();
    private readonly Func<AudioOutputMode, Action<string>, IAudioOutput>? _outputs;
    private MusicBeeApiInterface _api;
    private SynchronizationContext? _ui;
    private MessageBoxes? _boxes;
    private Host? _host;
    private bool _idleHooked;

    /// <summary>Why the engine could not be started, or null: repeated when a command is chosen.</summary>
    private string? _failure;

    public Plugin()
    {
        ShowSettings = dialog => dialog.ShowDialog(new Window(_api.MB_GetWindowHandle())) == DialogResult.OK;
    }

    /// <summary>For tests: the output each start opens, in place of the device.</summary>
    internal Plugin(Func<AudioOutputMode, Action<string>, IAudioOutput> outputs)
        : this() => _outputs = outputs;

    /// <summary>Shows the settings dialog, owned by MusicBee's main window, and says whether the
    /// user pressed OK. A test replaces it, since a headless plugin cannot show a dialog.</summary>
    internal Func<SettingsDialog, bool> ShowSettings { get; set; }

    /// <summary>The engine and its output, once started; null before, after <see cref="Close"/>,
    /// and when it could not be started.</summary>
    internal Host? Host => _host;

    public PluginInfo Initialise(IntPtr apiInterfacePtr)
    {
        _api.Initialise(apiInterfacePtr);
        _about.PluginInfoVersion = PluginInfoVersion;
        _about.Name = "faderio";
        _about.Description = "Plays faderio playlists with their authored transitions.";
        _about.Author = "faderio";
        _about.TargetApplication = "faderio"; // the header MusicBee draws for the panel
        _about.Type = PluginType.PanelView; // a plugin with a panel element (issue 10)
        _about.VersionMajor = 0;
        _about.VersionMinor = 1;
        _about.Revision = 0;
        _about.MinInterfaceVersion = MinInterfaceVersion;
        _about.MinApiRevision = MinApiRevision;
        _about.ReceiveNotifications = ReceiveNotificationFlags.StartupOnly;
        _about.ConfigurationPanelHeight = 0;

        PrivateDependencies.Register();

        // Publications are posted to MusicBee's UI thread, never rendered or shown from the
        // engine's threads. At start MusicBee calls this on a worker thread, so the UI thread is
        // reached through the main window, not through the context current here.
        _ui = new MainWindowContext(() => _api.MB_GetWindowHandle(), SynchronizationContext.Current ?? new SynchronizationContext());
        _boxes = new MessageBoxes(_ui, () => _api.MB_GetWindowHandle());
        // The menu stays beside the view: commands a MusicBee hotkey can be bound to.
        if (_api.MB_AddMenuItem("mnuTools/faderio", null, null) is ToolStripMenuItem menu)
        {
            menu.DropDownItems.Add("Play", null, OnPlay);
            menu.DropDownItems.Add("Stop", null, OnStop);
        }

        try
        {
            Start();
        }
        catch (Exception e)
        {
            // MusicBee still starts: the plugin says why it cannot play, and again at each command.
            _failure = $"faderio could not start: {e.Message}";
            _boxes.Show(_failure);
        }
        return _about;
    }

    /// <summary>MusicBee's plugin preferences, faderio's Configure (issue 04). With no panel asked
    /// for, MusicBee lends none, and the plugin shows its own dialog. True: handled, so MusicBee
    /// shows nothing of its own. What OK chose waits for <see cref="SaveSettings"/>.</summary>
    public bool Configure(IntPtr panelHandle)
    {
        Ready()?.Configure(ShowSettings);
        return true;
    }

    /// <summary>MusicBee's preferences are saved or applied: what the dialog chose is sent.</summary>
    public void SaveSettings() => _host?.SaveSettings();

    /// <summary>MusicBee has a panel for faderio, placed by the user in View → Arrange Panels:
    /// this method's presence is what tells MusicBee there is one. The view fills it; −1 asks
    /// MusicBee to size the view to the panel. MusicBee disposes the panel when the user removes
    /// it from the layout, and the view with it. With no engine, the panel says why. At start
    /// MusicBee calls this on its worker thread with a panel of the UI thread's, so the view is
    /// made on the panel's own thread.</summary>
    public int OnDockablePanelCreated(Control panel)
    {
        if (panel.InvokeRequired)
        {
            panel.Invoke(() => FillPanel(panel));
        }
        else
        {
            FillPanel(panel);
        }
        return -1;
    }

    /// <summary>On the panel's thread, the UI thread.</summary>
    private void FillPanel(Control panel)
    {
        // A pushed render waits for the UI thread to go idle, so paint and input run between
        // renders however fast the engine publishes, as in standalone faderio. Idle is the
        // thread's own event, so it is hooked here, on the UI thread, once.
        if (!_idleHooked)
        {
            Application.Idle += OnIdle;
            _idleHooked = true;
        }
        string? failure = _failure ?? (_host is null ? "faderio is not running." : null);
        if (failure is null)
        {
            try
            {
                _host!.AttachView(panel, StopMusicBeesPlayer);
            }
            catch (Exception e)
            {
                // MusicBee would swallow it and leave the panel blank: the panel says why instead.
                failure = $"faderio's view could not be shown: {e}";
            }
        }
        if (failure is not null)
        {
            panel.Controls.Add(new Label { Text = failure, Dock = DockStyle.Fill, Padding = new Padding(8) });
        }
    }

    public void Close(PluginCloseReason reason) => End();

    public void Uninstall() => End();

    public void ReceiveNotification(string sourceFileUrl, NotificationType type)
    {
    }

    /// <summary>The first method to touch a Core type, compiled only once the resolver is in place.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Start() =>
        _host = new Host(Path.Combine(_api.Setting_GetPersistentStoragePath(), "faderio"), _boxes!, _ui!, _outputs);

    /// <summary>Stops and disposes the engine and its output, releasing the device.</summary>
    private void End()
    {
        Application.Idle -= OnIdle;
        _idleHooked = false;
        _host?.Dispose();
        _host = null;
    }

    private void OnIdle(object? sender, EventArgs e) => _host?.UiThreadIdle();

    /// <summary>Never two players at once, nor two on one device: before faderio starts to sound.</summary>
    private void StopMusicBeesPlayer() => _api.Player_Stop();

    private void OnPlay(object? sender, EventArgs e)
    {
        if (Ready() is { } host)
        {
            StopMusicBeesPlayer();
            host.Play();
        }
    }

    private void OnStop(object? sender, EventArgs e) => Ready()?.Stop();

    private Host? Ready()
    {
        if (_host is null && _failure is not null)
        {
            _boxes?.Show(_failure);
        }
        return _host;
    }
}
