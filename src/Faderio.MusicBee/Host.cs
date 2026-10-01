using System.Windows.Forms;
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
/// overwrite each other's output mode or library. MusicBee's plugin preferences change them:
/// <see cref="Configure"/> takes the dialog's choice, and <see cref="SaveSettings"/> sends it.
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
    private readonly List<PluginView> _views = new();

    /// <param name="ui">MusicBee's UI thread, which every publication is posted to.</param>
    /// <param name="outputs">The output to open for a mode, for tests; null opens the device.</param>
    public Host(string settingsFolder, MessageBoxes boxes, SynchronizationContext ui, Func<AudioOutputMode, Action<string>, IAudioOutput>? outputs)
    {
        _boxes = boxes;
        // The shell's own sink, as standalone faderio's: publications reach the UI thread posted,
        // renders coalesced until it has been idle (see Plugin's Application.Idle).
        _sink = new PostingSink(ui);
        _sink.Changed += RenderViews;
        _sink.Noticed += OnNoticed;
        Action<string> notify = ((IEngineSink)_sink).Notice;
        Directory.CreateDirectory(settingsFolder);
        var settingsPath = Path.Combine(settingsFolder, "settings.json");
        var machine = new MachineSettingsStore(settingsPath);

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
        IAudioOutput output;
        NAudioAudioOutput? device = null;
        if (outputs is null)
        {
            device = new NAudioAudioOutput(mode, notify, releaseWhileStopped: true);
            _modeInUse = () => device.Mode;
            output = device;
        }
        else
        {
            output = outputs(mode, notify);
            _modeInUse = ModeAsked(mode); // a fake opens in the mode asked for
        }
        Engine = new PlaybackEngine(output, library, _sink, machine: machine);
        if (device is not null)
        {
            // The strip names the mode in use; a fallback or a reopen changes it between publications.
            device.ModeChanged += () => ui.Post(_ => RenderViews(Engine.View), null);
        }
    }

    /// <summary>What the settings dialog's OK chose, until MusicBee's Save sends it; null when
    /// nothing waits. Not the engine's: the user's choice, not yet applied.</summary>
    private MachineSettings? _chosen;

    public PlaybackEngine Engine { get; }

    /// <summary>This session's notices, as delivered: a record of messages already shown, not
    /// engine state. The views render it.</summary>
    public NoticeLog Notices { get; } = new();

    public void Play() => Engine.Play();

    public void Stop() => Engine.Stop();

    /// <summary>The UI thread has run out of messages: a render published since the last is posted.</summary>
    public void UiThreadIdle() => _sink.UiThreadIdle();

    /// <summary>Fills a panel MusicBee made with a view of the engine, docked to fill, and shows it
    /// the notices already delivered. The view leaves when MusicBee disposes the panel.</summary>
    /// <param name="stopMusicBeesPlayer">MusicBee's <c>Player_Stop</c>, for the view's starts.</param>
    public PluginView AttachView(Control panel, Action stopMusicBeesPlayer)
    {
        var view = new PluginView(Engine, Notices, _modeInUse, stopMusicBeesPlayer);
        _views.Add(view);
        view.Disposed += (_, _) => _views.Remove(view);
        panel.Controls.Add(view);
        return view;
    }

    /// <summary>The views attached now, for tests.</summary>
    internal IReadOnlyList<PluginView> Views => _views;

    /// <summary>Opens the settings dialog on the choice waiting to be saved, or else on the
    /// library open (or the one remembered) and the output mode saved. OK keeps its choice for
    /// <see cref="SaveSettings"/>, as MusicBee's own preferences wait for Save.</summary>
    /// <param name="show">Shows the dialog and says whether the user pressed OK.</param>
    public void Configure(Func<SettingsDialog, bool> show)
    {
        var view = Engine.View;
        var shown = _chosen ?? view.MachineSettings with { Library = view.Library?.Path ?? view.MachineSettings.Library };
        using var dialog = new SettingsDialog(shown.Library, shown.OutputMode);
        if (show(dialog))
        {
            _chosen = new MachineSettings { Library = dialog.Library, OutputMode = dialog.OutputMode };
        }
    }

    /// <summary>Sends what the dialog chose, each change to its own engine command, as standalone
    /// faderio's Open library and Settings do: another library stops playback and opens that
    /// library's open playlist, remembered for the next start; another output mode is saved for
    /// the next start. One the same as now sends nothing, so an Apply that follows another
    /// preference's change never stops faderio.</summary>
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
        if (chosen.OutputMode != view.MachineSettings.OutputMode)
        {
            Engine.ChangeMachineSettings(chosen);
        }
    }

    /// <summary>The views go first: their timers read the engine.</summary>
    public void Dispose()
    {
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

    /// <summary>On the UI thread: into the log, then to the views' strips, or a box with none.</summary>
    private void OnNoticed(string message)
    {
        Notices.Add(message);
        if (_views.Count == 0)
        {
            _boxes.Show(message);
            return;
        }
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
