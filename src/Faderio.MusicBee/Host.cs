using System.Windows.Forms;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Shell;
using Faderio.Shell.Audio;

namespace Faderio.MusicBee;

/// <summary>
/// The plugin's composition, as standalone faderio's composition root builds it: the output,
/// the library its settings name, a sink for the notices and the machine settings store. Its
/// settings are its own, in a folder of MusicBee's persistent storage, so the two hosts never
/// overwrite each other's output mode, device or library. MusicBee's plugin preferences change
/// them: <see cref="Configure"/> takes the dialog's choice, and <see cref="SaveSettings"/> sends it.
/// <para>The output device (phase 5 issue 13) is the one the settings name, or with none chosen
/// MusicBee's own, asked of MusicBee at each open so a change in its preferences is followed.
/// The output opens the device at every Play from stopped, so a saved device applies there.</para>
/// <para>The view (phase 5 issue 10) comes and goes with MusicBee's panel: <see cref="AttachView"/>
/// puts one in a panel MusicBee made, and it leaves when MusicBee disposes the panel. The engine,
/// the sink and the session's notice log stay here across them. A notice shows in every attached
/// view's strip; with none attached it is a message box, as in the spike, so faderio never fails
/// silently inside MusicBee.</para>
/// </summary>
internal sealed class Host : IDisposable
{
    private readonly PostingSink _sink;
    private readonly MessageBoxes _boxes;
    private readonly Func<AudioOutputMode?> _modeInUse;
    private readonly Func<string?> _deviceInUse;
    private readonly Func<string?> _musicBeesDevice;
    private readonly Func<IReadOnlyList<string>> _devices;
    private readonly List<PluginView> _views = new();
    private readonly SynchronizationContext _ui;
    private readonly PluginSettingsFile _pluginSettings;
    private NowPlayingTakeover? _nowPlaying;

    /// <param name="ui">MusicBee's UI thread, which every publication is posted to.</param>
    /// <param name="musicBeesDevice">MusicBee's own output device by the name its preferences
    /// show, or null when it cannot say: the plugin's device when none is chosen, where it names
    /// one of this machine's. MusicBee's "Default", DirectSound's "Primary Sound Driver" and an
    /// ASIO driver name none, so they are Windows' default device, without a notice.</param>
    /// <param name="outputs">The output to open for a mode and a device, for tests; null opens
    /// the device.</param>
    /// <param name="devices">This machine's output devices by name, for the dialog; null lists
    /// the installed ones.</param>
    public Host(
        string settingsFolder, MessageBoxes boxes, SynchronizationContext ui, Func<string?> musicBeesDevice,
        Func<AudioOutputMode, Func<string?>, Action<string>, IAudioOutput>? outputs, Func<IReadOnlyList<string>>? devices)
    {
        _boxes = boxes;
        _ui = ui;
        _devices = devices ?? OutputDevices.Installed.Names;
        _musicBeesDevice = () => musicBeesDevice() is { } name && _devices().Contains(name) ? name : null;
        // The shell's own sink, as standalone faderio's: publications reach the UI thread posted,
        // renders coalesced until it has been idle (see Plugin's Application.Idle).
        _sink = new PostingSink(ui);
        _sink.Changed += RenderViews;
        _sink.Noticed += OnNoticed;
        Action<string> notify = ((IEngineSink)_sink).Notice;
        Directory.CreateDirectory(settingsFolder);
        var settingsPath = Path.Combine(settingsFolder, "settings.json");
        var machine = new MachineSettingsStore(settingsPath);
        _pluginSettings = new PluginSettingsFile(Path.Combine(settingsFolder, "plugin.json"), notify);

        // A folder that is gone is opened anyway: the engine says so. None chosen is said here,
        // through the same route as the engine's notices.
        var folder = machine.Settings.Library;
        if (folder is null)
        {
            notify("No faderio library is chosen, so there is nothing to play. Choose one in " +
                   "MusicBee's Preferences, under Plugins: faderio's Configure.");
        }
        var library = folder is null ? null : Library.Open(folder);

        // Only the output mode is read here: the output opens before the engine that runs at its
        // rate (ADR 0008). A guest in MusicBee's process lets go of the device while stopped, so
        // MusicBee's own player has it back after faderio's Stop (issue 06, decision A).
        var mode = machine.Settings.OutputMode;
        // The device, asked at each open: the one saved, else MusicBee's own. Before the engine
        // exists (the launch query) the store's; after, the engine's, read and forgotten.
        string? ChosenDevice() => (Engine is null ? machine.Settings : Engine.View.MachineSettings).OutputDevice ?? _musicBeesDevice();
        IAudioOutput output;
        NAudioAudioOutput? device = null;
        if (outputs is null)
        {
            device = new NAudioAudioOutput(mode, notify, releaseWhileStopped: true, device: ChosenDevice);
            _modeInUse = () => device.Mode;
            _deviceInUse = () => device.Device;
            output = device;
        }
        else
        {
            output = outputs(mode, ChosenDevice, notify);
            _modeInUse = ModeAsked(mode); // a fake opens in the mode asked for
            _deviceInUse = ChosenDevice; // and on the device asked for
        }
        // The engine holds the library for this host (phase 5 issue 15): standalone faderio is
        // refused it, naming MusicBee, while it is open here.
        Engine = new PlaybackEngine(output, library, _sink, machine: machine, host: "MusicBee");
        if (device is not null)
        {
            // The strip names the mode in use; a fallback or a reopen changes it between publications.
            device.ModeChanged += () => ui.Post(_ => RenderViews(Engine.View), null);
        }
    }

    /// <summary>What the settings dialog's OK chose, until MusicBee's Save sends it; null when
    /// nothing waits. Not the engine's: the user's choice, not yet applied.</summary>
    private MachineSettings? _chosen;

    /// <summary>Whether the dialog's OK chose the Now Playing tab, waiting as <see cref="_chosen"/> does.</summary>
    private bool _chosenNowPlayingTab;

    public PlaybackEngine Engine { get; }

    /// <summary>This session's notices, as delivered: a record of messages already shown, not
    /// engine state. The views render it.</summary>
    public NoticeLog Notices { get; } = new();

    public void Play() => Engine.Play();

    public void Stop() => Engine.Stop();

    /// <summary>Queues <paramref name="files"/> at the end of the open playlist, in order, probing
    /// each off the UI thread; the run's rejections come as one notice (phase 5 issue 14).</summary>
    public void Add(IEnumerable<string> files) => _ = Engine.Add(files);

    /// <summary>The UI thread has run out of messages: a render published since the last is posted.</summary>
    public void UiThreadIdle() => _sink.UiThreadIdle();

    /// <summary>Fills a panel MusicBee made with a view of the engine, docked to fill, and shows it
    /// the notices already delivered. The view leaves when MusicBee disposes the panel.</summary>
    /// <param name="stopMusicBeesPlayer">MusicBee's <c>Player_Stop</c>, for the view's starts.</param>
    public PluginView AttachView(Control panel, Action stopMusicBeesPlayer)
    {
        var view = new PluginView(Engine, Notices, OnNoticed, _modeInUse, _deviceInUse, stopMusicBeesPlayer);
        _views.Add(view);
        view.Disposed += (_, _) => _views.Remove(view);
        panel.Controls.Add(view);
        return view;
    }

    /// <summary>The views attached now, for tests.</summary>
    internal IReadOnlyList<PluginView> Views => _views;

    /// <summary>Opens the settings dialog on the choice waiting to be saved, or else on the
    /// library open (or the one remembered), the output mode saved and the device saved, among
    /// this machine's devices and MusicBee's own. OK keeps its choice for
    /// <see cref="SaveSettings"/>, as MusicBee's own preferences wait for Save.</summary>
    /// <param name="show">Shows the dialog and says whether the user pressed OK.</param>
    public void Configure(Func<SettingsDialog, bool> show)
    {
        var view = Engine.View;
        var shown = _chosen ?? view.MachineSettings with { Library = view.Library?.Path ?? view.MachineSettings.Library };
        var nowPlayingTab = _chosen is null ? _pluginSettings.Settings.NowPlayingTab : _chosenNowPlayingTab;
        using var dialog = new SettingsDialog(shown.Library, shown.OutputMode, shown.OutputDevice, _devices(), _musicBeesDevice(), nowPlayingTab);
        if (show(dialog))
        {
            _chosen = new MachineSettings { Library = dialog.Library, OutputMode = dialog.OutputMode, OutputDevice = dialog.OutputDevice };
            _chosenNowPlayingTab = dialog.NowPlayingTab;
        }
    }

    /// <summary>Sends what the dialog chose, each change to its own engine command, as standalone
    /// faderio's Open library and Settings do: another library stops playback and opens that
    /// library's open playlist, remembered for the next start; another output mode or device is
    /// saved, the mode for the next start and the device for the next Play from stopped. One
    /// the same as now sends nothing, so an Apply that follows another preference's change never
    /// stops faderio.</summary>
    public void SaveSettings()
    {
        if (_chosen is not { } chosen)
        {
            return;
        }
        _chosen = null;
        var view = Engine.View;
        if (chosen.Library is { } folder && !IsOpen(view.Library, folder))
        {
            Engine.OpenLibrary(folder);
        }
        if (chosen.OutputMode != view.MachineSettings.OutputMode || chosen.OutputDevice != view.MachineSettings.OutputDevice)
        {
            Engine.ChangeMachineSettings(chosen);
        }
        if (_chosenNowPlayingTab != _pluginSettings.Settings.NowPlayingTab)
        {
            SaveNowPlayingTab(_chosenNowPlayingTab);
        }
    }

    // ── The Now Playing tab (phase 6 issue 08) ──────────────────────────────────────────

    /// <summary>The adjacency open in the Now Playing view's editor as its tab was left, and the
    /// library it belongs to, so the next visit's fresh view reopens it. View state, not the
    /// engine's. Two fields, not a tuple: a value type over a Core type in a field would stop
    /// MusicBee loading this class before the resolver exists (LoadingTests).</summary>
    private Adjacency? _nowPlayingEditor;

    private string? _nowPlayingLibrary;

    private bool _saidNowPlaying;

    /// <summary>Watches MusicBee's main form, on its thread, for the Now Playing tab, and while
    /// the setting is on fills each visit's view with a view of the engine (ADR 0013). With no
    /// form to watch it leaves MusicBee alone, and says so once if the setting is on.</summary>
    /// <param name="stopMusicBeesPlayer">MusicBee's <c>Player_Stop</c>, for the views' starts.</param>
    public void TakeOverNowPlaying(Form? main, Action stopMusicBeesPlayer)
    {
        if (main is null)
        {
            if (_pluginSettings.Settings.NowPlayingTab)
            {
                SayNowPlayingIsLeftAlone("faderio could not find MusicBee's main window");
            }
            return;
        }
        _nowPlaying?.Dispose();
        _nowPlaying = new NowPlayingTakeover(
            main,
            () => _pluginSettings.Settings.NowPlayingTab,
            container => AttachNowPlayingView(container, stopMusicBeesPlayer),
            LeaveNowPlayingView,
            () => SayNowPlayingIsLeftAlone("faderio does not recognise this MusicBee's Now Playing view"));
    }

    /// <summary>A fresh view, as a panel's, on the transition left open at the last visit if its
    /// library is still the one open.</summary>
    private Control AttachNowPlayingView(Control container, Action stopMusicBeesPlayer)
    {
        var view = AttachView(container, stopMusicBeesPlayer);
        if (_nowPlayingEditor is { } open && _nowPlayingLibrary is { } folder && IsOpen(Engine.View.Library, folder))
        {
            view.OpenEditor(open);
        }
        return view;
    }

    /// <summary>Its tab is left: a box being typed in is saved, and what the editor has open kept.</summary>
    private void LeaveNowPlayingView(Control left)
    {
        _nowPlayingEditor = ((PluginView)left).CommitEditor();
        _nowPlayingLibrary = Engine.View.Library?.Path;
    }

    private void SaveNowPlayingTab(bool on)
    {
        try
        {
            _pluginSettings.Save(new PluginSettings { NowPlayingTab = on });
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            OnNoticed($"faderio's plugin settings could not be saved ({e.Message}).");
        }
    }

    /// <summary>Once a session, in the views' strips and the notice list, never a box: MusicBee
    /// is only left as it is.</summary>
    private void SayNowPlayingIsLeftAlone(string why)
    {
        if (_saidNowPlaying)
        {
            return;
        }
        _saidNowPlaying = true;
        var message = $"{why}, so its Now Playing tab is left as MusicBee's own.";
        _ui.Post(_ => ShowInStrips(message), null);
    }

    /// <summary>The views go first: their timers read the engine.</summary>
    public void Dispose()
    {
        _nowPlaying?.Dispose();
        foreach (var view in _views.ToList())
        {
            view.Dispose();
        }
        Engine.Dispose();
    }

    private void RenderViews(PlayerView view)
    {
        foreach (var attached in _views)
        {
            attached.Render(view);
        }
    }

    /// <summary>On the UI thread: into the log, then to the views' strips, or a box with none.
    /// The editor's own notices ("Not saved: …") come here too, so they have the same route.</summary>
    private void OnNoticed(string message)
    {
        if (_views.Count == 0)
        {
            Notices.Add(message);
            _boxes.Show(message);
            return;
        }
        ShowInStrips(message);
    }

    /// <summary>On the UI thread: into the log and every attached view's strip.</summary>
    private void ShowInStrips(string message)
    {
        Notices.Add(message);
        foreach (var view in _views)
        {
            view.ShowNotices();
        }
    }

    /// <summary>Always <paramref name="mode"/>, boxed: a lambda closing over the enum itself would
    /// give this assembly a closure class with a Core value type in a field, which MusicBee's
    /// reflection over the plugin cannot load before the resolver exists (LoadingTests).</summary>
    private static Func<AudioOutputMode?> ModeAsked(object mode) => () => (AudioOutputMode)mode;

    /// <summary>Whether <paramref name="folder"/> is the library open now, and opened: one that
    /// could not open is tried again.</summary>
    private static bool IsOpen(LibraryView? open, string folder) =>
        open is { IsAvailable: true } && string.Equals(open.Path, folder, StringComparison.OrdinalIgnoreCase);
}
