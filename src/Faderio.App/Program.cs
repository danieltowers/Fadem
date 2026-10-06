using Faderio.Shell;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;
using Faderio.Shell.Audio;

namespace Faderio.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // The UI thread's context exists before the engine or the form, so no publication is
        // ever dropped for want of a window handle.
        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        var sink = new PostingSink(SynchronizationContext.Current);
        // A pushed render waits for the UI thread to go idle, so paint and input run between
        // renders however fast the engine publishes (build issue 20).
        Application.Idle += (_, _) => sink.UiThreadIdle();

        // What goes wrong before the window exists is posted like any notice, and shown in order
        // once the form's message loop runs. The output's notices (each fallback step) are posted
        // too, so none is handled under the engine lock while the output reopens.
        Action<string> notify = ((IEngineSink)sink).Notice;

        // This machine's files (library issue 03, D11): settings.json, engine-owned, and
        // window.json, the shell's. In %APPDATA%\faderio, or --machine=<folder> for smoke runs.
        // --library=<folder> alone keeps none, so a smoke run neither reads nor changes the
        // user's: the output mode is the default, nothing is remembered and the window opens
        // centred.
        var libraryArgument = Presenter.ParseLibraryArgument(args);
        var machineFolder = Presenter.ParseMachineArgument(args) ??
            (libraryArgument is null
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "faderio")
                : null);
        MachineSettingsStore? machine = null;
        if (machineFolder is not null)
        {
            Directory.CreateDirectory(machineFolder);
            machine = new MachineSettingsStore(Path.Combine(machineFolder, "settings.json"));
        }

        // The library (library issue 02, D12): --library=<folder>, else the one remembered, else
        // the user is asked, in the library chooser (library chooser issue 02), before the engine
        // exists: so the list is Core's own query on the machine's settings, and a removal its
        // own call. Cancelled, the app opens with no library: nothing is saved, and what is
        // queued still plays.
        var launch = LibraryLaunch.Resolve(libraryArgument, machine?.Settings.Library, Directory.Exists);
        var folder = launch.MustAsk
            ? LibraryChooser.Choose(
                owner: null,
                () => KnownLibrary.List(machine?.Settings ?? new MachineSettings(), open: null, Directory.Exists),
                known => Forget(machine, known, notify))
            : launch.Folder;
        var library = folder is null ? null : Library.Open(folder);

        // The engine owns both settings files; only the output mode is read here, since the
        // output is opened before the engine that runs at its rate (ADR 0008).
        var output = new NAudioAudioOutput(
            (machine?.Settings ?? new MachineSettings()).OutputMode, notify,
            Presenter.ParseUnavailableOutputs(args));
        // The engine holds the library for this host (phase 5 issue 15): the plugin in MusicBee
        // is refused it, naming faderio, while it is open here.
        var engine = new PlaybackEngine(output, library, sink, machine: machine, host: "faderio");

        // The title follows the library and the mode in use; the form renders both.
        var form = new MainForm(engine, sink, () => output.Mode,
            machineFolder is null ? null : Path.Combine(machineFolder, "window.json"))
        {
            // The plugin's last read of MusicBee's skin, else the built-in (studio theme issue 01).
            Palette = Faderio.Shell.Theme.Palette.ForMachine(machineFolder),
        };
        output.ModeChanged += () => form.BeginInvoke(() => form.Render());
        form.Shown += (_, _) =>
        {
            // Plain arguments are files to queue (drop them on the executable); --play starts
            // them and --edit-first opens the editor on the first boundary. For smoke runs.
            // Queued behind the posted notices, so a rejection notice from the add is the last
            // thing shown. The add runs in the background; the other two arguments wait for its rows.
            form.BeginInvoke(async () =>
            {
                var paths = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
                if (paths.Count > 0)
                {
                    await form.AddPaths(paths);
                }
                if (args.Contains("--edit-first", StringComparer.OrdinalIgnoreCase))
                {
                    form.EditTransitionAt(0);
                }
                if (args.Contains("--play", StringComparer.OrdinalIgnoreCase))
                {
                    engine.Play();
                }
            });
        };
        Application.Run(form);
    }

    /// <summary>The launch chooser's Remove: the machine's settings forget the folder, or a
    /// notice says why they could not. A run without them keeps no list to remove from.</summary>
    private static void Forget(MachineSettingsStore? machine, string folder, Action<string> notify)
    {
        try
        {
            machine?.Forget(folder);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            notify($"Not removed from the list: {LibraryFolders.Name(folder)}. " +
                   $"The machine settings file could not be written: {e.Message}");
        }
    }
}
