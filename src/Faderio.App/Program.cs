using Faderio.App.Audio;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Settings;

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

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "faderio");
        Directory.CreateDirectory(appData);
        // What goes wrong before the window exists is posted like any notice, and shown in order
        // once the form's message loop runs. The output's notices (each fallback step) are posted
        // too, so none is handled under the engine lock while the output reopens.
        Action<string> notify = ((IEngineSink)sink).Notice;

        // The library (library issue 02, D12): --library=<folder>, else the one remembered, else
        // the user is asked. --library is for smoke runs, so it neither reads nor changes which
        // library a plain launch opens. Cancelled, the app opens with no library: nothing is
        // saved, and what is queued still plays.
        var libraryArgument = Presenter.ParseLibraryArgument(args);
        var machine = libraryArgument is null ? new MachineSettingsStore(Path.Combine(appData, "settings.json")) : null;
        var launch = LibraryLaunch.Resolve(libraryArgument, machine?.Settings.Library, Directory.Exists);
        var folder = launch.MustAsk ? MainForm.ShowLibraryDialog(owner: null) : launch.Folder;
        var library = folder is null ? null : Library.Open(folder);

        // The engine owns the library and its settings; only the output mode is read here, since
        // the output is opened before the engine that runs at its rate (ADR 0008).
        var output = new NAudioAudioOutput(
            (library?.Settings?.Settings ?? new PlayerSettings()).OutputMode, notify,
            Presenter.ParseUnavailableOutputs(args));
        var engine = new PlaybackEngine(output, library, sink, machine: machine);

        // The title follows the library and the mode in use; the form renders both.
        var form = new MainForm(engine, sink, () => output.Mode);
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
}
