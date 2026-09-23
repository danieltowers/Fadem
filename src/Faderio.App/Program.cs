using Faderio.App.Audio;
using Faderio.Core.Playback;

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

        var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "faderio");
        Directory.CreateDirectory(appData);
        var store = Presenter.OpenTransitionStore(
            Path.Combine(appData, "transitions.json"),
            message => MessageBox.Show(message, "faderio — transition store", MessageBoxButtons.OK, MessageBoxIcon.Warning));

        var startupNotices = new List<string>();
        var mode = Presenter.ParseOutputMode(args);
        var output = new NAudioAudioOutput(mode, startupNotices.Add);
        var engine = new PlaybackEngine(output, store, sink);

        var form = new MainForm(engine, store, sink);
        form.Text = $"faderio — {mode} at {engine.SampleRate} Hz";
        form.Shown += (_, _) =>
        {
            foreach (var notice in startupNotices)
            {
                ((IEngineSink)sink).Notice(notice);
            }

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
