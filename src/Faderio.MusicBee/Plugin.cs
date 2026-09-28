using System.Runtime.CompilerServices;
using System.Windows.Forms;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.MusicBee;

namespace MusicBeePlugin;

/// <summary>
/// The class MusicBee loads (phase 5 issue 03): a second shell on faderio's engine. It plays the
/// open playlist of the library its settings name, through its own output, from a Tools → faderio
/// menu. MusicBee's player never plays it: Play stops MusicBee's first.
/// <para>The engine's dependencies load from a private folder through a handler that
/// <see cref="Initialise"/> registers. MusicBee loads every type in this assembly before calling
/// it, so no type here may need Core to load: none derives from a Core type, implements a Core
/// interface, is a delegate over Core types or holds a Core value type in a field (proven by
/// LoadingTests). And no method MusicBee calls first touches a Core type: everything that does
/// is in <see cref="Host"/>, reached only through <see cref="Start"/>.</para>
/// </summary>
public partial class Plugin
{
    private readonly PluginInfo _about = new();
    private readonly Func<AudioOutputMode, Action<string>, IAudioOutput>? _outputs;
    private MusicBeeApiInterface _api;
    private MessageBoxes? _boxes;
    private Host? _host;

    /// <summary>Why the engine could not be started, or null: repeated when a command is chosen.</summary>
    private string? _failure;

    public Plugin()
    {
    }

    /// <summary>For tests: the output each start opens, in place of the device.</summary>
    internal Plugin(Func<AudioOutputMode, Action<string>, IAudioOutput> outputs) => _outputs = outputs;

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
        _about.TargetApplication = "";
        _about.Type = PluginType.General;
        _about.VersionMajor = 0;
        _about.VersionMinor = 1;
        _about.Revision = 0;
        _about.MinInterfaceVersion = MinInterfaceVersion;
        _about.MinApiRevision = MinApiRevision;
        _about.ReceiveNotifications = ReceiveNotificationFlags.StartupOnly;
        _about.ConfigurationPanelHeight = 0;

        PrivateDependencies.Register();

        // MusicBee calls this on its UI thread: notices are posted back to it, never shown from
        // the engine's threads.
        _boxes = new MessageBoxes(SynchronizationContext.Current ?? new SynchronizationContext(), () => _api.MB_GetWindowHandle());
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

    /// <summary>No settings dialog yet (issue 04): the settings file is written by hand.</summary>
    public bool Configure(IntPtr panelHandle) => false;

    public void SaveSettings()
    {
    }

    public void Close(PluginCloseReason reason) => End();

    public void Uninstall() => End();

    public void ReceiveNotification(string sourceFileUrl, NotificationType type)
    {
    }

    /// <summary>The first method to touch a Core type, compiled only once the resolver is in place.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Start() =>
        _host = new Host(Path.Combine(_api.Setting_GetPersistentStoragePath(), "faderio"), _boxes!, _outputs);

    /// <summary>Stops and disposes the engine and its output, releasing the device.</summary>
    private void End()
    {
        _host?.Dispose();
        _host = null;
    }

    private void OnPlay(object? sender, EventArgs e)
    {
        if (Ready() is { } host)
        {
            _api.Player_Stop(); // never two players at once, nor two on one device
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
