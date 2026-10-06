using System.Reflection;
using System.Runtime.ExceptionServices;
using Faderio.Shell;
using Faderio.App;
using Faderio.Core.Domain;
using Faderio.Core.Persistence;
using Faderio.Core.Playback;
using Faderio.Core.Queue;
using Faderio.Core.Settings;
using Faderio.Core.Tests;
using Faderio.Core.Transitions;

using Point = Faderio.Core.Domain.Point;

namespace Faderio.App.Tests;

/// <summary>
/// Seam S5 (ADR 0002): the real <see cref="MainForm"/> on an STA thread, never shown, driven by
/// the fake output and an inline sink. Controls are poked, not pixels: a click is the control's
/// own click handler, and the assertions are the control values a user would see.
/// </summary>
public sealed partial class HeadlessFormTests
{
    private sealed class Harness : IDisposable
    {
        public string TempDir { get; } = Path.Combine(TestTemp.Root, "app", Guid.NewGuid().ToString("N"));
        public Library Library { get; }
        public FakeAudioOutput Output { get; }
        public JsonTransitionStore Store { get; }
        public PlaybackEngine Engine { get; }
        public MainForm Form { get; }
        public IEngineSink Sink { get; }
        public string SettingsPath => Library.SettingsPath;

        /// <summary>The machine's settings.json: AppData's stand-in, beside the library folder
        /// rather than in it.</summary>
        public string MachinePath => Path.Combine(TempDir + "-machine", "settings.json");

        /// <param name="inUse">The mode the output opened in, as the NAudio adapter reports it.</param>
        /// <param name="windowFile">The window.json the form keeps its geometry in, or none.</param>
        public Harness(int latency = 0, AudioOutputMode? inUse = AudioOutputMode.WasapiShared, string? windowFile = null)
        {
            Directory.CreateDirectory(TempDir);
            Directory.CreateDirectory(TempDir + "-machine");
            Output = new FakeAudioOutput { Latency = latency };
            Library = Library.Open(TempDir);
            Store = Library.Transitions!;
            var sink = new PostingSink(null); // inline: no message pump in a headless test
            Sink = sink;
            // The add worker runs inline, so an add completes on this STA thread and the inline
            // sink never touches a control from another thread.
            Engine = new PlaybackEngine(Output, Library, sink, machine: new MachineSettingsStore(MachinePath))
            {
                StartAddWorker = work => work(),
                StartPrepareWorker = work => work(),
            };
            Form = new MainForm(Engine, sink, () => inUse, windowFile);
        }

        public void Dispose()
        {
            Form.Dispose();
            Engine.Dispose();
            try { Directory.Delete(TempDir, recursive: true); } catch (IOException) { }
            try { Directory.Delete(TempDir + "-machine", recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>WinForms wants an STA thread; xunit does not give one.</summary>
    private static void RunSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    /// <summary>Fires the control's own click handler. <c>PerformClick</c> refuses on a form that
    /// was never shown, and showing one is the thing a headless test must not do.</summary>
    private static void Click(ButtonBase button)
    {
        Assert.True(button.Enabled, $"{button.Text} is disabled");
        var onClick = typeof(Control).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        onClick.Invoke(button, new object[] { EventArgs.Empty });
    }

    [Fact]
    public void AddTwoFiles_ShowsTwoRows_WithTitlesAndLengths() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 3, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 2, 0.2);

        h.Form.AddPaths(new[] { a, b });

        Assert.Equal(2, h.Form._queueList.Items.Count);
        Assert.Equal(Path.GetFileNameWithoutExtension(a), h.Form._queueList.Items[0].SubItems[1].Text);
        Assert.Equal("0:03", h.Form._queueList.Items[0].SubItems[3].Text);
        Assert.Equal("0:02", h.Form._queueList.Items[1].SubItems[3].Text);
        Assert.Equal("Ready.", h.Form._status.Text); // a clean add raises no notice: the rows are the news
    });

    /// <summary>Delivers a drag to the list the way Explorer does: DragEnter, then, if the list
    /// accepted it, DragDrop. Returns the effect the list offered.</summary>
    private static DragDropEffects Drop(ListView list, IDataObject data)
    {
        var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        if (args.Effect != DragDropEffects.None)
        {
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        }
        return args.Effect;
    }

    [Fact]
    public void AddFolder_PassesThePickedFolderToAdd_AndListsItsTracks() => RunSta(() =>
    {
        using var h = new Harness();
        var album = Path.Combine(h.TempDir, "Album");
        Directory.CreateDirectory(album);
        File.Copy(TestAudio.WriteDcWav("ten.wav", 1, 0.2), Path.Combine(album, "10 - Ten.wav"));
        File.Copy(TestAudio.WriteDcWav("two.wav", 1, 0.2), Path.Combine(album, "2 - Two.wav"));
        File.WriteAllText(Path.Combine(album, "cover.jpg"), "not audio");
        h.Form.ChooseFolders = () => new[] { album };

        Click(h.Form._addFolder);

        Assert.Equal(new[] { "2 - Two", "10 - Ten" },
            h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.SubItems[1].Text));
        Assert.Equal("Ready.", h.Form._status.Text); // the picture is ignored without a word
    });

    [Fact]
    public void AddFolder_Cancelled_AddsNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ChooseFolders = Array.Empty<string>;

        Click(h.Form._addFolder);

        Assert.Empty(h.Form._queueList.Items);
        Assert.Null(h.Engine.View.Adding);
    });

    [Fact]
    public void AddFiles_PassesThePickedFilesToAdd() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.ChooseFiles = () => new[] { b, a };

        Click(h.Form._addFiles);

        Assert.Equal(new[] { b, a }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
    });

    [Fact]
    public void ImportPlaylist_CopiesThePickedPlaylistIntoTheLibrary_AndOpensIt() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a });
        var playlist = Path.Combine(h.TempDir, "mix † ⭐️.m3u8");
        File.WriteAllLines(playlist, new[] { "#EXTM3U", b, a });
        h.Form.ChoosePlaylists = () => new[] { playlist };

        Click(h.Form._importPlaylist);

        Assert.Equal("mix † ⭐️", h.Engine.View.OpenPlaylist);
        Assert.Equal(new[] { b, a }, Queued(h));
        Assert.Equal(2, h.Form._queueList.Items.Count);
        Assert.Equal(new[] { "mix † ⭐️", "Untitled" }, Names(h.Form._playlists));
        Assert.Equal("mix † ⭐️", h.Form._playlistName._name.Text);
        Assert.True(File.Exists(Path.Combine(h.Library.PlaylistsFolder, "mix † ⭐️.m3u8")));
    });

    [Fact]
    public void ImportPlaylist_Cancelled_ImportsNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ChoosePlaylists = Array.Empty<string>;

        Click(h.Form._importPlaylist);

        Assert.Equal(new[] { "Untitled" }, h.Engine.View.Playlists);
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void ImportPlaylist_SavesABoxBeingTypedIn_ThenImports() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        var playlist = Path.Combine(h.TempDir, "Evening.m3u8");
        File.WriteAllLines(playlist, new[] { "#EXTM3U", TestAudio.WriteDcWav("delta.wav", 1, 0.2) });
        h.Form.ChoosePlaylists = () => new[] { playlist };

        Click(h.Form._importPlaylist);

        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Contains("Evening", h.Engine.View.Playlists);
    });

    [Fact]
    public void ExportPlaylist_OffersTheOpenPlaylistsName_WritesTheQueueThere_AndLeavesTheLibraryCopy() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { b, a });
        h.Engine.RenamePlaylist("Untitled", "Evening");
        var libraryCopy = Path.Combine(h.Library.PlaylistsFolder, "Evening.m3u8");
        var before = File.ReadAllBytes(libraryCopy);
        var playlist = Path.Combine(h.TempDir, "saved.m3u8");
        string? offered = null;
        h.Form.ChooseExportPath = name =>
        {
            offered = name;
            return playlist;
        };

        Click(h.Form._exportPlaylist);

        Assert.Equal("Evening.m3u8", offered);
        Assert.Equal(new[] { b, a }, File.ReadAllLines(playlist).Where(l => !l.StartsWith('#')));
        Assert.Equal("Saved 2 tracks to saved.m3u8.", h.Form._status.Text);
        Assert.Equal(before, File.ReadAllBytes(libraryCopy));
    });

    [Fact]
    public void ExportPlaylist_Cancelled_WritesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 1, 0.4) });
        h.Form.ChooseExportPath = _ => null;

        Click(h.Form._exportPlaylist);

        Assert.Empty(Directory.GetFiles(h.TempDir, "*.m3u8"));
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void DroppingFilesAndFolders_OnTheList_AddsThemInTheOrderDropped() => RunSta(() =>
    {
        using var h = new Harness();
        var loose = TestAudio.WriteDcWav("loose.wav", 1, 0.4);
        var album = Path.Combine(h.TempDir, "Album");
        Directory.CreateDirectory(album);
        File.Copy(TestAudio.WriteDcWav("one.wav", 1, 0.2), Path.Combine(album, "1 - One.wav"));
        var files = new DataObject(DataFormats.FileDrop, new[] { album, loose, loose });

        var effect = Drop(h.Form._queueList, files);

        Assert.Equal(DragDropEffects.Copy, effect);
        Assert.Equal(new[] { Path.Combine(album, "1 - One.wav"), loose, loose },
            h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(3, h.Form._queueList.Items.Count);
    });

    [Fact]
    public void FilesDropped_BetweenTwoRows_LandThere_InTheOrderDropped() => RunSta(() =>
    {
        using var h = new Harness();
        var paths = new[] { "one", "two" }.Select(n => TestAudio.WriteDcWav(n + ".wav", 1, 0.2)).ToArray();
        h.Form.AddPaths(paths);
        var list = h.Form._queueList;
        _ = list.Handle;
        var c = TestAudio.WriteDcWav("three.wav", 1, 0.2);
        var d = TestAudio.WriteDcWav("four.wav", 1, 0.2);

        var effect = DropAt(list, new DataObject(DataFormats.FileDrop, new[] { d, c }), LowerHalfOf(list, 0));

        Assert.Equal(DragDropEffects.Copy, effect);
        Assert.Equal(new[] { paths[0], d, c, paths[1] }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.False(IsSetVisible(h.Form._dropLine));
    });

    [Fact]
    public void DraggingSomethingOtherThanFiles_IsRefused() => RunSta(() =>
    {
        using var h = new Harness();

        var effect = Drop(h.Form._queueList, new DataObject(DataFormats.UnicodeText, "not a path"));

        Assert.Equal(DragDropEffects.None, effect);
        Assert.Empty(h.Engine.View.Queue);
    });

    [Fact]
    public void WhileAdding_TheStripShowsProgress_AndCancelStopsTheAdd() => RunSta(() =>
    {
        using var h = new Harness();
        var files = Enumerable.Range(0, 3).Select(i => TestAudio.WriteDcWav($"add{i}.wav", 1, 0.2)).ToArray();
        Assert.False(h.Form._addProgress.Available);
        Assert.False(h.Form._cancelAdd.Available);
        string? progressMidAdd = null;
        var cancelShownMidAdd = false;
        var real = h.Engine.ResolvePath;
        h.Engine.ResolvePath = path =>
        {
            if (path == files[1])
            {
                // The second file's probe is in flight: the first row is in, and the strip says so.
                progressMidAdd = h.Form._addProgress.Available ? h.Form._addProgress.Text : null;
                cancelShownMidAdd = h.Form._cancelAdd.Available;
                h.Form._cancelAdd.PerformClick();
            }
            return real(path);
        };

        h.Form.AddPaths(files);

        Assert.Equal("Adding 1 of 3…", progressMidAdd);
        Assert.True(cancelShownMidAdd);
        Assert.Single(h.Form._queueList.Items);
        Assert.False(h.Form._addProgress.Available);
        Assert.False(h.Form._cancelAdd.Available);
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void AddingMore_KeepsTheRowsAlreadyListed_AndNumbersOnFromThem() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a });
        var first = h.Form._queueList.Items[0];

        h.Form.AddPaths(new[] { b, a });

        Assert.Same(first, h.Form._queueList.Items[0]);
        Assert.Equal(new[] { "1", "2", "3" }, h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.Text));
        Assert.Equal(h.Engine.View.Queue.Select(r => r.Id), h.Form._queueList.Items.Cast<ListViewItem>().Select(MainForm.RowOf));
    });

    [Fact]
    public void ARejectedFile_LeavesTheListAlone_AndNamesTheReason() => RunSta(() =>
    {
        using var h = new Harness();
        var quad = TestAudio.WriteWavAt("quad", 1, _ => 0.3, 44100, 16, 4);

        h.Form.AddPaths(new[] { quad });

        Assert.Empty(h.Form._queueList.Items);
        Assert.Contains("4 channels", h.Form._status.Text);
        Assert.Contains("supported-audio.md", h.Form._status.Text);
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
    });

    [Fact]
    public void PlayRow_ThenAdvance_TheSeekBarAndReadout_FollowThePlayhead() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });

        h.Form.PlayRowAt(1);
        Assert.Equal(Transport.Playing, h.Engine.View.Transport);
        Assert.Equal("⏸", h.Form._playPause.Text);

        h.Output.Advance(44100 * 3);
        h.Form.Render(); // the display timer's tick

        Assert.True(h.Form._seek.Enabled);
        Assert.Equal(3000, h.Form._seek.Playhead.Milliseconds);
        Assert.Equal("0:03 / -0:02", h.Form._time.Text);
        Assert.Equal("3.0 / 5.0", h.Form._seconds.Text);
        Assert.True(h.Form._seconds.Top >= h.Form._time.Bottom, "the seconds are not below the time"); // below it
        Assert.Equal(new[] { "1", "2" }, h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.Text));
    });

    [Fact]
    public void AboveTheTransport_TheAttendedTrackIsNamed_PausedToo_AndBlankWhenStopped() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        Assert.Equal("", h.Form._attendedTrack.Text);

        h.Form.PlayRowAt(0);
        h.Output.Advance(44100 * 3);
        h.Form.Render();
        // Untagged: the title is the file's name.
        Assert.Equal(Path.GetFileNameWithoutExtension(a), h.Form._attendedTrack.Text);

        h.Output.Advance(44100 * 3); // alpha ends; the hard cut hands over to beta
        h.Form.Render();
        Assert.Equal(Path.GetFileNameWithoutExtension(b), h.Form._attendedTrack.Text);

        h.Form._queueList.SelectedIndices.Clear();
        Click(h.Form._playPause);
        Assert.Equal(Transport.Paused, h.Engine.View.Transport);
        Assert.Equal(Path.GetFileNameWithoutExtension(b), h.Form._attendedTrack.Text);

        h.Form.ConfirmClear = _ => true;
        h.Form._clearQueue.PerformClick();
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
        Assert.Equal("", h.Form._attendedTrack.Text);
    });

    [Fact]
    public void TheAttendedTrack_SitsBetweenTheButtonsAndTheTransport() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ClientSize = new Size(900, 600);
        h.Form.PerformLayout();
        var transport = h.Form._seek.Parent!;
        var track = h.Form._attendedTrack;
        Assert.Equal(transport.Top, track.Bottom);
        Assert.Equal(h.Form._addFiles.Parent!.Bottom, track.Top);
    });

    [Fact]
    public void AnUnreadableRow_IsMarked_UntilItsFileOpensAgain() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        var c = TestAudio.WriteDcWav("gamma.wav", 5, 0.3);
        h.Form.AddPaths(new[] { a, b, c });
        File.Move(b, b + ".away");

        h.Form.PlayRowAt(0); // A's boundary opens B, and passes over it
        h.Form.Render();

        var marked = h.Form._queueList.Items[1];
        Assert.Equal("⚠ 2", marked.Text);
        Assert.Equal(h.Form.Palette.Dim, marked.ForeColor);
        Assert.Contains("Unreadable", marked.ToolTipText);
        Assert.Equal("1", h.Form._queueList.Items[0].Text);
        Assert.Equal(h.Form.Palette.Accent, h.Form._queueList.Items[0].ForeColor); // A plays

        File.Move(b + ".away", b);
        h.Form.PlayRowAt(1); // a double-click tries it
        h.Form.Render();

        Assert.Equal("2", marked.Text); // now playing
        Assert.Equal(h.Form.Palette.Accent, marked.ForeColor); // in the accent, as the playing row
        Assert.Equal("", marked.ToolTipText);
    });

    [Fact]
    public void Capture_WritesThePlayheadIntoTheField_AndKeepsTheExactPoint() => RunSta(() =>
    {
        using var h = new Harness(latency: 4410);
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        Assert.Contains("alpha", h.Form._editor._title.Text);
        Assert.False(h.Form._editor._captureOut.Enabled); // nothing attended yet

        h.Form.PlayRowAt(0);
        h.Output.Advance(66150 + 4410); // audible 1.5 s, rendered 1.6 s
        h.Form.Render();

        Click(h.Form._editor._captureOut);

        Assert.Equal("1.5", h.Form._editor._fadeOutPoint.Text);
        var captured = h.Form._editor.CapturedFadeOut!.Value;
        Assert.Equal(h.Engine.View.Attended!.Playhead.TimeSpan, captured.TimeSpan);
    });

    [Fact]
    public void Save_AuthorsTheAdjacency_AndConfirmsInTheStatusStrip() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "1.5";
        h.Form._editor._fadeOutDuration.Text = "0.5";
        h.Form._editor._fadeInPoint.Text = "0";
        h.Form._editor._fadeInDuration.Text = "0.5";

        h.Form._editor.Commit();

        var spec = h.Store.Get(a, b)!;
        Assert.Equal(TimeSpan.FromSeconds(1.5), spec.FadeOutPoint!.Position);
        Assert.False(spec.FadeOutPoint.FromEnd);
        Assert.Equal(TimeSpan.FromSeconds(0.5), spec.FadeOutDuration);
        Assert.Equal(TimeSpan.Zero, spec.FadeInPoint!.Position);
        Assert.StartsWith("Saved alpha", h.Form._editor._title.Text.Length > 0 ? h.Form._status.Text : "");
    });

    [Fact]
    public void Save_WithABadNumber_SavesNothing_AndSaysWhy() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "abc";

        h.Form._editor.Commit();

        Assert.Null(h.Store.Get(a, b));
        Assert.StartsWith("Not saved:", h.Form._status.Text);
    });

    [Fact]
    public void TheCapturedPoint_IsWhatGetsSaved_NotTheDisplayedText() => RunSta(() =>
    {
        // The field shows three decimals; the store gets the frame-exact point (ticket 06).
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        h.Form.PlayRowAt(0);
        h.Output.Advance(12345); // 0.27993 s: not a whole millisecond
        h.Form.Render();

        Click(h.Form._editor._captureOut);
        h.Form._editor.Commit();

        Assert.Equal("0.28", h.Form._editor._fadeOutPoint.Text);
        var saved = h.Store.Get(a, b)!.FadeOutPoint!.Position;
        Assert.Equal(12345, Ticks.FramesOf(saved, 44100));
    });

    // ── Build issue 14: curve, from-end, revert, remove, validation ────────────────────

    /// <summary>Two DC tracks queued, 5 s each, with the editor open on alpha → beta.</summary>
    private static (string A, string B) EditingAlphaToBeta(Harness h)
    {
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.EditTransitionAt(0);
        return (a, b);
    }

    private static void Fill(TransitionEditorPanel editor, string fadeOutPoint, string fadeOutDuration, string fadeInPoint, string fadeInDuration)
    {
        editor._fadeOutPoint.Text = fadeOutPoint;
        editor._fadeOutDuration.Text = fadeOutDuration;
        editor._fadeInPoint.Text = fadeInPoint;
        editor._fadeInDuration.Text = fadeInDuration;
    }

    [Fact]
    public void FromEnd_TickedAndUnticked_ConvertsTheFadeOutPoint_KeepingTheMoment() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutPoint.Text = "3.5";

        editor._fadeOutFromEnd.Checked = true;
        Assert.Equal("1.5", editor._fadeOutPoint.Text);

        editor._fadeOutFromEnd.Checked = false;
        Assert.Equal("3.5", editor._fadeOutPoint.Text);
    });

    [Fact]
    public void Nudge_MovesATypedPointATenth_ShiftASecond_AndSavesEachStep() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutPoint.Text = "3.5";
        editor._fadeInPoint.Text = "0.05";

        Click(editor._fadeOutMore);
        Assert.Equal("3.6", editor._fadeOutPoint.Text);
        Click(editor._fadeOutLess);
        Click(editor._fadeOutLess);
        Assert.Equal("3.4", editor._fadeOutPoint.Text);
        editor.NudgePoint(fadeOut: true, direction: +1, coarse: true); // Shift-click
        Assert.Equal("4.4", editor._fadeOutPoint.Text);
        editor.NudgePoint(fadeOut: true, direction: +1, coarse: true);
        Assert.Equal("5", editor._fadeOutPoint.Text); // clamped at the 5 s track's end

        Click(editor._fadeInLess);
        Assert.Equal("0", editor._fadeInPoint.Text); // clamped at the start
        Assert.False(editor.HasUnsavedEdits);
        Assert.Equal(TimeSpan.FromSeconds(5), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Equal(TimeSpan.Zero, h.Store.Get(a, b)!.FadeInPoint!.Position);
    });

    [Fact]
    public void TheSecondNudges_BelowTheTenths_MoveAPointAWholeSecond_AndSaveEachStep() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutPoint.Text = "2.5";
        editor._fadeInPoint.Text = "1.5";

        Click(editor._fadeOutMoreSecond);
        Assert.Equal("3.5", editor._fadeOutPoint.Text);
        Click(editor._fadeOutLessSecond);
        Click(editor._fadeOutLessSecond);
        Assert.Equal("1.5", editor._fadeOutPoint.Text);
        Click(editor._fadeInMoreSecond);
        Assert.Equal("2.5", editor._fadeInPoint.Text);
        Click(editor._fadeInLessSecond);
        Click(editor._fadeInLessSecond);
        Assert.Equal("0.5", editor._fadeInPoint.Text);

        Assert.False(editor.HasUnsavedEdits);
        Assert.Equal(TimeSpan.FromSeconds(1.5), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Equal(TimeSpan.FromSeconds(0.5), h.Store.Get(a, b)!.FadeInPoint!.Position);
        // Below the tenths' buttons, in the same column.
        Assert.Equal(editor._fadeOutLess.Left, editor._fadeOutLessSecond.Left);
        Assert.True(editor._fadeOutLessSecond.Top > editor._fadeOutLess.Bottom - 1);
    });

    [Fact]
    public void TheDurations_NudgeAsThePointsDo_StopAtZero_AndSaveEachStep() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutDuration.Text = "2";

        Click(editor._fadeOutDurationMore);
        Assert.Equal("2.1", editor._fadeOutDuration.Text);
        Click(editor._fadeOutDurationLess);
        Click(editor._fadeOutDurationLess);
        Assert.Equal("1.9", editor._fadeOutDuration.Text);
        Click(editor._fadeOutDurationMoreSecond);
        Assert.Equal("2.9", editor._fadeOutDuration.Text);
        Click(editor._fadeOutDurationLessSecond);
        Click(editor._fadeOutDurationLessSecond);
        Click(editor._fadeOutDurationLessSecond);
        Assert.Equal("0", editor._fadeOutDuration.Text); // never below zero
        editor.NudgeDuration(fadeOut: true, direction: +1, coarse: true); // Shift-click
        Assert.Equal("1", editor._fadeOutDuration.Text);

        // A blank duration nudges from what it plays, the 5 s its placeholder shows.
        Click(editor._fadeInDurationLess);
        Assert.Equal("4.9", editor._fadeInDuration.Text);

        Assert.False(editor.HasUnsavedEdits);
        Assert.Equal(TimeSpan.FromSeconds(1), h.Store.Get(a, b)!.FadeOutDuration);
        Assert.Equal(TimeSpan.FromSeconds(4.9), h.Store.Get(a, b)!.FadeInDuration);
        // Below the tenths' buttons, in the same column, as a point's are.
        Assert.Equal(editor._fadeOutDurationLess.Left, editor._fadeOutDurationLessSecond.Left);
        Assert.True(editor._fadeOutDurationLessSecond.Top > editor._fadeOutDurationLess.Bottom - 1);
    });

    [Fact]
    public void Nudge_FromEnd_MovesTheNumberShown_SoPlusIsEarlierInTheTrack() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutPoint.Text = "3.5";
        editor._fadeOutFromEnd.Checked = true;
        Assert.Equal("1.5", editor._fadeOutPoint.Text);

        Click(editor._fadeOutMore);
        Assert.Equal("1.6", editor._fadeOutPoint.Text);
        editor._fadeOutFromEnd.Checked = false;
        Assert.Equal("3.4", editor._fadeOutPoint.Text); // earlier than the 3.5 it started at
    });

    [Fact]
    public void WithNoBoundaryOpen_TheFieldsAndTheLayerChoice_AreDisabled_UntilOneOpens() => RunSta(() =>
    {
        using var h = new Harness();
        var editor = h.Form._editor;
        Control[] boxes = [editor._fadeOutPoint, editor._fadeOutDuration, editor._fadeInPoint, editor._fadeInDuration];
        Control[] choice = [editor._editGlobal, editor._editPlaylist];
        h.Form.Render();
        Assert.All(boxes.Concat(choice), c => Assert.False(c.Enabled, c.Name));

        EditingAlphaToBeta(h);
        h.Form.Render();
        Assert.All(boxes.Concat(choice), c => Assert.True(c.Enabled, c.Name));

        var second = Path.Combine(h.TempDir, "Second library");
        Directory.CreateDirectory(second);
        h.Form.ChooseLibrary = () => second;
        Click(h.Form._openLibrary); // empties the editor
        h.Form.Render();
        Assert.All(boxes.Concat(choice), c => Assert.False(c.Enabled, c.Name));
    });

    [Fact]
    public void Nudge_IsDisabled_WithNoAdjacency_ABlankField_OrTextThatIsNotANumber() => RunSta(() =>
    {
        using var h = new Harness();
        var editor = h.Form._editor;
        Assert.False(editor._fadeOutMore.Enabled); // nothing open

        EditingAlphaToBeta(h);
        Assert.False(editor._fadeOutMore.Enabled); // blank
        editor._fadeOutPoint.Text = "2";
        Assert.True(editor._fadeOutMore.Enabled);
        Assert.True(editor._fadeOutLess.Enabled);
        Assert.True(editor._fadeOutMoreSecond.Enabled);
        Assert.True(editor._fadeOutLessSecond.Enabled);
        editor._fadeOutPoint.Text = "two";
        Assert.False(editor._fadeOutMore.Enabled);
        Assert.False(editor._fadeOutLess.Enabled);
        Assert.False(editor._fadeOutMoreSecond.Enabled);
        Assert.False(editor._fadeOutLessSecond.Enabled);
        Assert.False(editor._fadeInMore.Enabled); // its own field is still blank
        Assert.False(editor._fadeInMoreSecond.Enabled);
    });

    [Fact]
    public void Nudge_OnACapturedPoint_KeepsItExact() => RunSta(() =>
    {
        using var h = new Harness(latency: 4410);
        EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        h.Form.PlayRowAt(0);
        h.Output.Advance(66150 + 4410 + 7); // audible a few frames past 1.5 s
        h.Form.Render();
        Click(editor._captureOut);
        var captured = editor.CapturedFadeOut!.Value;

        Click(editor._fadeOutMore);

        Assert.Equal(captured.TimeSpan + TimeSpan.FromSeconds(0.1), editor.CapturedFadeOut!.Value.TimeSpan);
    });

    [Fact]
    public void CaptureWithFromEnd_StoresTheDistanceToTheEnd() => RunSta(() =>
    {
        using var h = new Harness(latency: 4410);
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutFromEnd.Checked = true;
        h.Form.PlayRowAt(0);
        h.Output.Advance(66150 + 4410); // audible 1.5 s
        h.Form.Render();

        Click(editor._captureOut);
        editor.Commit();

        Assert.Equal("3.5", editor._fadeOutPoint.Text);
        var saved = h.Store.Get(a, b)!.FadeOutPoint!;
        Assert.True(saved.FromEnd);
        Assert.Equal(5 * 44100 - 66150, Ticks.FramesOf(saved.Position, 44100));
    });

    [Fact]
    public void Edit_LoadsAFromEndPoint_WithFromEndTicked_AndTheCurve() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.25), FromEnd: true),
            Curve = FadeCurveKind.Linear,
        });

        h.Form.EditTransitionAt(0);

        Assert.True(h.Form._editor._fadeOutFromEnd.Checked);
        Assert.Equal("1.25", h.Form._editor._fadeOutPoint.Text); // loading converts nothing
        Assert.Equal("Linear", h.Form._editor._curve.Text);
    });

    [Fact]
    public void Linear_Saved_IsStored_AndShownAgainOnReopening() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Assert.Equal("Equal-power", editor._curve.Text);
        Fill(editor, "1.5", "0.5", "0", "0.5");
        editor._curve.SelectedIndex = editor._curve.FindStringExact("Linear");

        editor.Commit();
        h.Form.EditTransitionAt(0);

        Assert.Equal(FadeCurveKind.Linear, h.Store.Get(a, b)!.Curve);
        Assert.Equal("Linear", editor._curve.Text);
    });

    [Fact]
    public void RemoveTransition_Unauthors_EmptiesTheFields_AndUndoPutsItBack() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "1.5", "0.5", "0", "0.5");
        editor.Commit();
        var saved = h.Store.Get(a, b);

        Click(editor._remove);

        Assert.Null(h.Store.Get(a, b));
        Assert.StartsWith("Removed alpha", h.Form._status.Text);
        Assert.Equal("", editor._fadeOutPoint.Text);

        editor.UndoChange();

        Assert.Equal(saved, h.Store.Get(a, b));
        Assert.Equal("1.5", editor._fadeOutPoint.Text);
    });

    [Fact]
    public void ABlankSave_LeavesNoEntryInTheStore() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "1.5", "0.5", "0", "0.5");
        editor.Commit();

        Fill(editor, "", "", "", "");
        editor.Commit();

        Assert.Null(h.Store.Get(a, b));
        Assert.DoesNotContain("alpha", File.ReadAllText(h.Library.TransitionsPath));
    });

    [Fact]
    public void AnImpossibleValue_IsRefused_WithItsReasonBesideItsField() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "9", "0.5", "7", "0.5");

        editor.Commit();

        Assert.Null(h.Store.Get(a, b));
        var outReason = editor.ReasonFor(TransitionField.FadeOutPoint);
        var inReason = editor.ReasonFor(TransitionField.FadeInPoint);
        Assert.True(IsSetVisible(outReason));
        Assert.Contains("past the end of alpha", outReason.Text);
        Assert.True(IsSetVisible(inReason));
        Assert.Contains("past the end of beta", inReason.Text);
        Assert.False(IsSetVisible(editor.ReasonFor(TransitionField.FadeOutDuration)));
        Assert.StartsWith("Not saved: alpha", h.Form._status.Text);

        Fill(editor, "1.5", "0.5", "0", "0.5");
        editor.Commit();

        Assert.NotNull(h.Store.Get(a, b));
        Assert.False(IsSetVisible(outReason)); // a good save clears the reasons
    });

    [Fact]
    public void AFromEndPointLongerThanTheTrack_IsRefusedBesideTheFadeOutPoint() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutFromEnd.Checked = true;
        editor._fadeOutPoint.Text = "6";

        editor.Commit();

        Assert.Null(h.Store.Get(a, b));
        Assert.Contains("measured from the end", editor.ReasonFor(TransitionField.FadeOutPoint).Text);
    });

    [Fact]
    public void ANumberThatCannotBeRead_IsShownBesideItsField() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        editor._fadeInDuration.Text = "abc";

        editor.Commit();

        Assert.True(IsSetVisible(editor.ReasonFor(TransitionField.FadeInDuration)));
        Assert.Contains("\"abc\"", editor.ReasonFor(TransitionField.FadeInDuration).Text);
    });

    /// <summary>Fires the list's context menu Opening, which sets what is enabled.</summary>
    private static void OpenMenu(ListView list)
    {
        var menu = list.ContextMenuStrip!;
        typeof(ToolStripDropDown).GetMethod("OnOpening", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(menu, new object[] { new System.ComponentModel.CancelEventArgs() });
    }

    private static void PressKey(Control control, Keys key)
    {
        typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(control, new object[] { new KeyEventArgs(key) });
    }

    /// <summary>Selects rows as a user would. A list without a handle keeps no selection, so it
    /// gets one here; the form is still never shown.</summary>
    private static void SelectRows(ListView list, params int[] indices)
    {
        _ = list.Handle;
        foreach (var index in indices)
        {
            list.Items[index].Selected = true;
        }
    }

    private static string[] Titles(ListView list) =>
        list.Items.Cast<ListViewItem>().Select(i => i.SubItems[1].Text).ToArray();

    [Fact]
    public void Delete_RemovesEverySelectedRow_InOneGo() => RunSta(() =>
    {
        using var h = new Harness();
        var paths = new[] { "one", "two", "three", "four" }.Select(n => TestAudio.WriteDcWav(n + ".wav", 1, 0.2)).ToArray();
        h.Form.AddPaths(paths);
        Assert.True(h.Form._queueList.MultiSelect);
        SelectRows(h.Form._queueList, 0, 2);

        PressKey(h.Form._queueList, Keys.Delete);

        Assert.Equal(new[] { paths[1], paths[3] }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { Path.GetFileNameWithoutExtension(paths[1]), Path.GetFileNameWithoutExtension(paths[3]) },
            Titles(h.Form._queueList));
        Assert.Equal(new[] { "1", "2" }, h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.Text));
    });

    [Fact]
    public void TheMenusRemove_RemovesTheSelectedRows_AndIsOfferedOnlyWithASelection() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a, b });

        OpenMenu(h.Form._queueList);
        Assert.False(h.Form._removeRows.Enabled);
        Assert.True(h.Form._clearQueue.Enabled);

        SelectRows(h.Form._queueList, 1);
        OpenMenu(h.Form._queueList);
        Assert.True(h.Form._removeRows.Enabled);
        h.Form._removeRows.PerformClick();

        Assert.Equal(new[] { a }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Single(h.Form._queueList.Items);
    });

    [Fact]
    public void DeletingThePlayingRow_PlaysTheRowThatTookItsPlace() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100 * 2);
        SelectRows(h.Form._queueList, 0);

        PressKey(h.Form._queueList, Keys.Delete);
        h.Form.Render();

        Assert.Equal(b, h.Engine.View.Attended!.Row.Track.FilePath);
        Assert.Equal("0:00 / -0:05", h.Form._time.Text);
        Assert.Equal("1", h.Form._queueList.Items[0].Text);
    });

    [Fact]
    public void ClearQueue_BeyondOneRow_AsksFirst_AndDoesNothingIfDeclined() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 5, 0.4), TestAudio.WriteDcWav("beta.wav", 5, 0.2) });
        h.Form.PlayRowAt(0);
        var asked = new List<int>();
        h.Form.ConfirmClear = count =>
        {
            asked.Add(count);
            return false;
        };

        h.Form._clearQueue.PerformClick();

        Assert.Equal(new[] { 2 }, asked);
        Assert.Equal(2, h.Form._queueList.Items.Count);
        Assert.Equal(Transport.Playing, h.Engine.View.Transport);

        h.Form.ConfirmClear = _ => true;
        h.Form._clearQueue.PerformClick();

        Assert.Empty(h.Engine.View.Queue);
        Assert.Empty(h.Form._queueList.Items);
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
        Assert.Equal("0:00 / -0:00", h.Form._time.Text);
    });

    [Fact]
    public void ClearQueue_OfOneRow_ClearsWithoutAsking_AndIsNotOfferedWhenEmpty() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 1, 0.4) });
        h.Form.ConfirmClear = _ => throw new InvalidOperationException("one row must not ask");

        h.Form._clearQueue.PerformClick();

        Assert.Empty(h.Form._queueList.Items);
        OpenMenu(h.Form._queueList);
        Assert.False(h.Form._clearQueue.Enabled);
        Assert.False(h.Form._removeRows.Enabled);
    });

    /// <summary>Starts a drag of the selected rows as a user would, but hands back what the list
    /// would drag instead of entering the modal drag loop.</summary>
    private static IDataObject DragSelectedRows(MainForm form)
    {
        IDataObject? dragged = null;
        form.StartDrag = data => dragged = data;
        typeof(ListView).GetMethod("OnItemDrag", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form._queueList, new object[] { new ItemDragEventArgs(MouseButtons.Left, form._queueList.SelectedItems[0]) });
        return dragged ?? throw new InvalidOperationException("nothing was dragged");
    }

    /// <summary>Carries a drag over the list to a point in it, then drops it there. Returns the
    /// effect the list offered, and leaves the drop line as the drag over it left it.</summary>
    private static DragDropEffects DropAt(ListView list, IDataObject data, System.Drawing.Point client, bool drop = true)
    {
        var screen = list.PointToScreen(client);
        var args = new DragEventArgs(data, 0, screen.X, screen.Y, DragDropEffects.Copy | DragDropEffects.Move, DragDropEffects.None);
        typeof(Control).GetMethod("OnDragEnter", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        typeof(Control).GetMethod("OnDragOver", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        if (drop && args.Effect != DragDropEffects.None)
        {
            typeof(Control).GetMethod("OnDragDrop", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
        }
        return args.Effect;
    }

    /// <summary>Whether the control itself is set visible. <see cref="Control.Visible"/> also
    /// asks every parent, and a form that is never shown has none visible.</summary>
    private static bool IsSetVisible(Control control)
    {
        var getState = typeof(Control).GetMethod("GetState", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var visible = Enum.ToObject(getState.GetParameters()[0].ParameterType, 0x2); // States.Visible
        return (bool)getState.Invoke(control, new[] { visible })!;
    }

    private static System.Drawing.Point UpperHalfOf(ListView list, int index) =>
        new(list.Items[index].Bounds.Left + 8, list.Items[index].Bounds.Top + 2);

    private static System.Drawing.Point LowerHalfOf(ListView list, int index) =>
        new(list.Items[index].Bounds.Left + 8, list.Items[index].Bounds.Bottom - 2);

    [Fact]
    public void DraggingTwoRows_OntoTheLastRowsLowerHalf_MovesThemToTheEnd_AndKeepsThemSelected() => RunSta(() =>
    {
        using var h = new Harness();
        var paths = new[] { "one", "two", "three", "four" }.Select(n => TestAudio.WriteDcWav(n + ".wav", 1, 0.2)).ToArray();
        h.Form.AddPaths(paths);
        SelectRows(h.Form._queueList, 0, 2);
        var list = h.Form._queueList;

        var effect = DropAt(list, DragSelectedRows(h.Form), LowerHalfOf(list, 3));

        Assert.Equal(DragDropEffects.Move, effect);
        Assert.Equal(new[] { paths[1], paths[3], paths[0], paths[2] }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(new[] { "two", "four", "one", "three" }, Titles(list).Select(t => t.Split('-')[0]));
        Assert.Equal(new[] { "1", "2", "3", "4" }, list.Items.Cast<ListViewItem>().Select(i => i.Text)); // the attended row moved
        Assert.Equal(new[] { 2, 3 }, list.SelectedIndices.Cast<int>());
        Assert.False(IsSetVisible(h.Form._dropLine));
    });

    [Fact]
    public void DraggingARow_OverAnotherRowsUpperHalf_ShowsTheLineAboveIt_AndDropsItThere() => RunSta(() =>
    {
        using var h = new Harness();
        var paths = new[] { "one", "two", "three" }.Select(n => TestAudio.WriteDcWav(n + ".wav", 1, 0.2)).ToArray();
        h.Form.AddPaths(paths);
        SelectRows(h.Form._queueList, 2);
        var list = h.Form._queueList;
        var dragged = DragSelectedRows(h.Form);

        DropAt(list, dragged, UpperHalfOf(list, 1), drop: false);
        Assert.True(IsSetVisible(h.Form._dropLine));
        Assert.InRange(h.Form._dropLine.Top, list.Items[1].Bounds.Top - 2, list.Items[1].Bounds.Top);

        DropAt(list, dragged, UpperHalfOf(list, 1));
        Assert.Equal(new[] { paths[0], paths[2], paths[1] }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
    });

    [Fact]
    public void DroppingPastTheLastColumn_LandsByTheRowUnderTheCursor() => RunSta(() =>
    {
        using var h = new Harness();
        var paths = new[] { "one", "two", "three" }.Select(n => TestAudio.WriteDcWav(n + ".wav", 1, 0.2)).ToArray();
        h.Form.AddPaths(paths);
        SelectRows(h.Form._queueList, 2);
        var list = h.Form._queueList;
        var right = list.Columns.Cast<ColumnHeader>().Sum(c => c.Width) + 10;
        Assert.True(right < list.ClientSize.Width, "the list must be wider than its columns");

        DropAt(list, DragSelectedRows(h.Form), new System.Drawing.Point(right, UpperHalfOf(list, 1).Y));

        Assert.Equal(new[] { paths[0], paths[2], paths[1] }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
    });

    [Fact]
    public void ShuffleAndRepeat_ShowTheViewsState_IncludingChangesMadeElsewhere() => RunSta(() =>
    {
        using var h = new Harness();
        Assert.False(h.Form._shuffle.Checked);
        Assert.False(h.Form._repeat.Checked);
        Assert.Equal("🔁", h.Form._repeat.Text);

        // Not through the form: the published view is what the controls follow.
        h.Engine.SetShuffle(true);
        h.Engine.SetRepeat(RepeatMode.One);

        Assert.True(h.Form._shuffle.Checked);
        Assert.True(h.Form._repeat.Checked);
        Assert.Equal("🔁1", h.Form._repeat.Text);

        h.Engine.SetRepeat(RepeatMode.All);
        Assert.True(h.Form._repeat.Checked);
        Assert.Equal("🔁", h.Form._repeat.Text);

        h.Engine.SetShuffle(false);
        h.Engine.SetRepeat(RepeatMode.Off);
        Assert.False(h.Form._shuffle.Checked);
        Assert.False(h.Form._repeat.Checked);
    });

    [Fact]
    public void EachShufflePress_IsOneCall_ThatFlipsIt() => RunSta(() =>
    {
        using var h = new Harness();

        foreach (var expected in new[] { true, false, true })
        {
            var before = h.Engine.View.Version;
            Click(h.Form._shuffle);
            Assert.Equal(expected, h.Engine.View.Shuffle);
            Assert.Equal(before + 1, h.Engine.View.Version); // one publication: one call
            Assert.Equal(expected, h.Form._shuffle.Checked);
        }
    });

    [Fact]
    public void EachRepeatPress_IsOneCall_CyclingOffAllOne() => RunSta(() =>
    {
        using var h = new Harness();

        foreach (var expected in new[] { RepeatMode.All, RepeatMode.One, RepeatMode.Off, RepeatMode.All })
        {
            var before = h.Engine.View.Version;
            Click(h.Form._repeat);
            Assert.Equal(expected, h.Engine.View.Repeat);
            Assert.Equal(before + 1, h.Engine.View.Version);
            Assert.Equal(Presenter.RepeatGlyph(expected), h.Form._repeat.Text);
            Assert.Equal(expected != RepeatMode.Off, h.Form._repeat.Checked);
        }
    });

    [Fact]
    public void ARepeatPress_CyclesFromWhatTheViewSays_NotFromWhatTheButtonLastShowed() => RunSta(() =>
    {
        using var h = new Harness();
        Click(h.Form._repeat);             // off → all
        h.Engine.SetRepeat(RepeatMode.One); // changed elsewhere

        Click(h.Form._repeat);

        Assert.Equal(RepeatMode.Off, h.Engine.View.Repeat);
    });

    [Fact]
    public void TheTransportRow_HasNoStopButton() => RunSta(() =>
    {
        using var h = new Harness();
        var transport = (TableLayoutPanel)h.Form._playPause.Parent!;
        // Ordinal: UI text comparison folds ⏹ and ⏸ together.
        Assert.DoesNotContain(transport.Controls.Cast<Control>(), c => string.Equals(c.Text, "⏹", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "⏮", "▶", "⏭", "⤳", "⏩", "🔀", "🔁", "Sync editor" },
            transport.Controls.OfType<ButtonBase>().OrderBy(transport.GetColumn).Select(c => c.Text).ToArray());
    });

    [Fact]
    public void SyncEditor_IsTheLibrarysSetting_AndTheEditorFollowsThePlayingRow() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100);
        h.Form.Render();
        Assert.False(h.Form._syncEditor.Checked);
        Assert.Equal(Presenter.SyncEditorTip, h.Form._tips.GetToolTip(h.Form._syncEditor));
        Assert.Null(h.Form._editor.Adjacency);

        Click(h.Form._syncEditor);
        h.Form.Render();

        Assert.True(h.Engine.View.Settings.EditorFollowsPlayback);
        Assert.True(h.Form._syncEditor.Checked);
        Assert.Equal(h.Engine.View.EditorFollow, h.Form._editor.Adjacency);
        Assert.Equal(a, h.Form._editor.Adjacency!.Outgoing.FilePath);

        Click(h.Form._syncEditor);
        h.Form.Render();
        Assert.False(h.Form._syncEditor.Checked);
    });

    [Fact]
    public void Play_WithOneOtherRowSelected_PlaysThatRow() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100 * 2);
        Click(h.Form._playPause); // pause
        SelectRows(h.Form._queueList, 1);

        Click(h.Form._playPause);
        h.Form.Render();

        Assert.Equal(b, h.Engine.View.Attended!.Row.Track.FilePath);
        Assert.Equal("0:00 / -0:05", h.Form._time.Text);
    });

    [Fact]
    public void Play_WithNoneOrSeveralRowsSelected_Resumes() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        var c = TestAudio.WriteDcWav("gamma.wav", 5, 0.1);
        h.Form.AddPaths(new[] { a, b, c });
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100 * 2);
        Click(h.Form._playPause); // pause

        Click(h.Form._playPause); // nothing selected
        Assert.Equal(a, h.Engine.View.Attended!.Row.Track.FilePath);
        Assert.Equal(2.0, h.Engine.View.Attended!.Playhead.TimeSpan.TotalSeconds, 2);

        Click(h.Form._playPause); // pause
        SelectRows(h.Form._queueList, 1, 2);
        Click(h.Form._playPause); // two selected
        Assert.Equal(a, h.Engine.View.Attended!.Row.Track.FilePath);
        Assert.Equal(2.0, h.Engine.View.Attended!.Playhead.TimeSpan.TotalSeconds, 2);
    });

    [Fact]
    public void TheMarker_IsASpeakerAtRestWhenStopped_AndSoundingWhilePlayingOrPaused_OnUnshadedRows() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.Render();
        // The speaker's own cell: the last, shown first.
        string[] Labels() => h.Form._queueList.Items.Cast<ListViewItem>()
            .Select(i => i.SubItems[h.Form._queueList.Columns.Count - 1].Text).ToArray();
        Color[] Backs() => h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.BackColor).ToArray();

        Assert.Equal(0, h.Form._queueList.Columns[h.Form._queueList.Columns.Count - 1].DisplayIndex); // at the far left
        Assert.Equal(new[] { "🔈", "" }, Labels()); // at launch
        Assert.Equal(new[] { h.Form.Palette.Well, h.Form.Palette.Well }, Backs());

        h.Form.PlayRowAt(1);
        h.Form.Render();
        Assert.Equal(new[] { "", "🔊" }, Labels());
        Assert.Equal(new[] { h.Form.Palette.Well, h.Form.Palette.Well }, Backs());

        Click(h.Form._playPause); // pause
        h.Form.Render();
        Assert.Equal(new[] { "", "🔊" }, Labels());

        Click(h.Form._playPause);
        h.Output.Advance(44100 * 2); // the queue runs out
        h.Form.Render();
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
        Assert.Equal(new[] { "🔈", "" }, Labels());
        Assert.True(h.Form._queueList.OwnerDraw); // a selected row grey, not blue
    });

    [Fact]
    public void AClickOnTheSeekBar_SeeksTheAttendedVoiceThere_AndOnlyWhileSomethingIsAttended() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 8, 0.4);
        h.Form.AddPaths(new[] { a });
        h.Form.Render();
        var onMouseDown = typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onMouseUp = typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        void ClickAt(int x)
        {
            var at = new MouseEventArgs(MouseButtons.Left, 1, x, 10, 0);
            onMouseDown.Invoke(h.Form._seek, new object[] { at });
            onMouseUp.Invoke(h.Form._seek, new object[] { at });
        }

        Assert.False(h.Form._seek.Enabled); // stopped at launch: nothing attended
        ClickAt(300);
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);

        h.Form.PlayRowAt(0);
        h.Output.Advance(44100);
        Click(h.Form._playPause); // pause
        h.Form.Render();
        ClickAt(SeekBar.Inset + (h.Form._seek.Width - 2 * SeekBar.Inset) / 2); // halfway along the travel
        h.Form.Render();

        Assert.InRange(h.Engine.View.Attended!.Playhead.Milliseconds, 3980, 4020);
        // Halfway can fall half a pixel short of 4 s on an odd travel, so 0:03 or 0:04.
        Assert.Equal(Presenter.TimeReadout(h.Engine.View.Attended, held: false), h.Form._time.Text);
        Assert.Contains(h.Form._time.Text, new[] { "0:03 / -0:05", "0:04 / -0:04" });
    });

    [Fact]
    public void ADragOnTheSeekBarsFadeOutStartMark_MovesTheAttendedRowsFade() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 8, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 8, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(6), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100);
        h.Form.Render();
        var onMouseDown = typeof(Control).GetMethod("OnMouseDown", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var onMouseUp = typeof(Control).GetMethod("OnMouseUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int X(double seconds) => SeekBar.Inset + (int)Math.Round(seconds / 8 * (h.Form._seek.Width - 2 * SeekBar.Inset));

        onMouseDown.Invoke(h.Form._seek, new object[] { new MouseEventArgs(MouseButtons.Left, 1, X(6), 1, 0) });
        onMouseUp.Invoke(h.Form._seek, new object[] { new MouseEventArgs(MouseButtons.Left, 1, X(5), 1, 0) });

        var stored = h.Store.Get(a, b)!;
        Assert.InRange(stored.FadeOutPoint!.Position.TotalSeconds, 4.95, 5.05);
        Assert.InRange((stored.FadeOutPoint.Position + stored.FadeOutDuration!.Value).TotalSeconds, 6.999, 7.001);
    });

    [Fact]
    public void TheJumpButtons_MoveFiveSecondsEachWay_AndFollowTheSeekBarsEnabledState() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 12, 0.4);
        h.Form.AddPaths(new[] { a });
        h.Form.Render();
        Assert.False(h.Form._back5.Enabled); // stopped at launch
        Assert.False(h.Form._forward5.Enabled);

        h.Form.PlayRowAt(0);
        h.Output.Advance(44100);
        h.Form.Render();
        Assert.True(h.Form._back5.Enabled);
        Assert.True(h.Form._forward5.Enabled);

        Click(h.Form._forward5);
        Click(h.Form._forward5);
        h.Form.Render();
        Assert.Equal(11.0, h.Engine.View.Attended!.Playhead.TotalSeconds, 2);
        Assert.Equal("0:11 / -0:01", h.Form._time.Text);

        Click(h.Form._back5);
        h.Form.Render();
        Assert.Equal(6.0, h.Engine.View.Attended!.Playhead.TotalSeconds, 2);
    });

    [Fact]
    public void TheTransportRow_StacksTheJumpButtonsAfterTheSeekBar_BackBelowForward_AsThePluginDoes() => RunSta(() =>
    {
        using var h = new Harness();
        var row = (TableLayoutPanel)h.Form._seek.Parent!;
        var seek = row.GetColumn(h.Form._seek);
        var jumps = h.Form._forward5.Parent!;
        Assert.Same(jumps, h.Form._back5.Parent);
        Assert.Equal(seek + 1, row.GetColumn(jumps));
        Assert.Equal(h.Form._forward5.Left, h.Form._back5.Left);
        Assert.True(h.Form._back5.Top >= h.Form._forward5.Bottom, "−5 s is not below +5 s");
        Assert.True(jumps.Bottom <= row.Height, "the jumps are cut off");
        Assert.Equal(seek + 2, row.GetColumn(h.Form._time.Parent!)); // with the seconds under it
    });

    [Fact]
    public void TransitionNow_IsEnabledOnlyWhilePlaying_WithANextAdjacency_AndNoTransitionInFlight() => RunSta(() =>
    {
        using var h = new Harness();
        Assert.False(h.Form._transitionNow.Enabled); // stopped, empty
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        var c = TestAudio.WriteDcWav("c.wav", 4, 0.3);
        h.Form.AddPaths(new[] { a, b, c });
        h.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        Assert.False(h.Form._transitionNow.Enabled); // stopped

        h.Engine.Play();
        Assert.True(h.Form._transitionNow.Enabled);
        h.Engine.Pause();
        Assert.False(h.Form._transitionNow.Enabled);
        h.Engine.Play();
        Assert.True(h.Form._transitionNow.Enabled);

        h.Output.Advance(44100 + 11025); // into A → B's crossfade
        h.Form.Render();
        Assert.NotNull(h.Engine.View.Incoming);
        Assert.False(h.Form._transitionNow.Enabled);

        h.Engine.PlayRow(h.Engine.View.Queue[2].Id); // the last row, repeat off
        Assert.Null(h.Engine.View.NextAdjacency);
        Assert.False(h.Form._transitionNow.Enabled);
        h.Engine.SetRepeat(RepeatMode.All); // the wrap into the first row
        Assert.True(h.Form._transitionNow.Enabled);
    });

    [Fact]
    public void ATransitionNowPress_IsOneCall_ThatStartsTheNextAdjacency() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("a.wav", 4, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(3), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        h.Engine.Play();
        h.Output.Advance(44100);

        Click(h.Form._transitionNow);

        Assert.Equal(b, h.Engine.View.Incoming?.Row.Track.FilePath);
        Assert.False(h.Form._transitionNow.Enabled);
        Assert.Equal("Transition now", h.Form._tips.GetToolTip(h.Form._transitionNow));
    });

    [Fact]
    public void ACueTransitionPress_IsOneCall_ThatJumpsTo5sBeforeTheFade_AndPlays() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("a.wav", 12, 0.4);
        var b = TestAudio.WriteDcWav("b.wav", 4, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Store.Upsert(a, b, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(8), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(1),
        });
        Assert.False(h.Form._cueTransition.Enabled); // stopped
        h.Engine.Play();
        h.Output.Advance(44100);
        h.Engine.Pause();
        h.Form.Render();
        Assert.True(h.Form._cueTransition.Enabled); // paused too

        Click(h.Form._cueTransition);

        Assert.Equal(Transport.Playing, h.Engine.View.Transport);
        Assert.Equal(3.0, h.Engine.View.Attended!.Playhead.TotalSeconds, 2);
        Assert.Equal("Cue transition: play from 5 s before it", h.Form._tips.GetToolTip(h.Form._cueTransition));
        var row = (TableLayoutPanel)h.Form._transitionNow.Parent!;
        Assert.Equal(row.GetColumn(h.Form._transitionNow) + 1, row.GetColumn(h.Form._cueTransition));
    });

    /// <summary>Clicks a strip item through its own handler, as <see cref="Click"/> does.</summary>
    private static void Click(ToolStripItem item)
    {
        var onClick = typeof(ToolStripItem).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;
        onClick.Invoke(item, new object[] { EventArgs.Empty });
    }

    /// <summary>The list as a user reads it: the notice column of each row.</summary>
    private static string[] ListedNotices(MainForm form) =>
        form._noticeList!._list.Items.Cast<ListViewItem>().Select(i => i.SubItems[1].Text).ToArray();

    [Fact]
    public void TwoNoticesInARow_TheStripShowsTheLatestAndACount_AndTheListHoldsBoth() => RunSta(() =>
    {
        using var h = new Harness();
        Assert.False(h.Form._noticeCount.Available); // nothing to count yet

        h.Sink.Notice("Skipped \"a.wav\": gone.");
        h.Sink.Notice("Saved a → b.");

        Assert.Equal("Saved a → b.", h.Form._status.Text);
        Assert.True(h.Form._noticeCount.Available);
        Assert.Equal("2 notices", h.Form._noticeCount.Text);

        h.Form.ShowNoticeList = _ => { }; // a headless form shows no window
        Click(h.Form._noticeCount);

        Assert.Equal(new[] { "Skipped \"a.wav\": gone.", "Saved a → b." }, ListedNotices(h.Form));
    });

    [Fact]
    public void ANoticeRepeatedFiftyTimes_ShowsOnce_WithItsCount() => RunSta(() =>
    {
        using var h = new Harness();
        h.Sink.Notice("Audio rendering error: boom");
        h.Sink.Notice("Saved a → b.");
        for (var i = 0; i < 49; i++)
        {
            h.Sink.Notice("Audio rendering error: boom");
        }

        Assert.Equal("Audio rendering error: boom ×50", h.Form._status.Text);
        Assert.Equal("2 notices", h.Form._noticeCount.Text);

        h.Form.ShowNoticeList = _ => { };
        Click(h.Form._status);

        var rows = h.Form._noticeList!._list.Items.Cast<ListViewItem>().ToArray();
        Assert.Equal(new[] { "Audio rendering error: boom", "Saved a → b." }, rows.Select(r => r.SubItems[1].Text));
        Assert.Equal(new[] { "×50", "" }, rows.Select(r => r.SubItems[2].Text));
    });

    [Fact]
    public void TheOpenList_FollowsNewNotices_AndSelectingOneShowsItInFull() => RunSta(() =>
    {
        using var h = new Harness();
        Form? shown = null;
        h.Form.ShowNoticeList = form => shown = form;
        h.Sink.Notice("first");
        Click(h.Form._noticeCount);
        Assert.Same(h.Form._noticeList, shown);
        Assert.Equal("first", h.Form._noticeList!._detail.Text); // the latest is selected on open

        h.Sink.Notice("second");
        h.Sink.Notice("first");

        Assert.Equal(new[] { "first", "second" }, ListedNotices(h.Form));
        Assert.Equal("×2", h.Form._noticeList._list.Items[0].SubItems[2].Text);

        h.Form._noticeList._list.Items[1].Selected = true;
        Assert.Equal("second", h.Form._noticeList._detail.Text);

        var list = h.Form._noticeList;
        Click(h.Form._noticeCount); // a second click brings back the same list, not another
        Assert.Same(list, h.Form._noticeList);

        list.Dispose(); // closed by something other than the user, which hides it
        h.Sink.Notice("third");
        Click(h.Form._noticeCount);
        Assert.False(h.Form._noticeList.IsDisposed);
        Assert.Equal(new[] { "first", "second", "third" }, ListedNotices(h.Form));
    });

    [Fact]
    public void BeforeAnyNotice_TheStripOpensNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ShowNoticeList = _ => Assert.Fail("an empty list was opened");

        Click(h.Form._status);

        Assert.Equal("Ready.", h.Form._status.Text);
        Assert.Null(h.Form._noticeList);
    });

    [Theory]
    [InlineData(1)]
    [InlineData(40)]
    public void ALongNotice_IsClipped_AndNeverPushesTheCountOffTheStrip(int words) => RunSta(() =>
    {
        using var h = new Harness();
        _ = h.Form.Handle; // laid out at its default size, still never shown
        h.Sink.Notice(string.Join(" ", Enumerable.Repeat("Transition store file is corrupt.", words)));
        h.Form._noticeCount.Owner!.PerformLayout();

        Assert.Equal(ToolStripItemPlacement.Main, h.Form._status.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, h.Form._noticeCount.Placement);
        Assert.True(h.Form._noticeCount.Bounds.Right <= h.Form._noticeCount.Owner.DisplayRectangle.Right);
    });

    // ── Settings (build issue 12) ───────────────────────────────────────────────────────

    [Fact]
    public void TheSettingsDialog_OpensOnTheViewsSettings_AndOKSendsEachGroupToItsOwnCommand() => RunSta(() =>
    {
        using var h = new Harness();
        h.Engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive });
        h.Engine.ChangeSettings(new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 8,
        });
        var opened = false;
        h.Form.ShowSettings = dialog =>
        {
            opened = true;
            Assert.Equal("WASAPI exclusive", dialog._outputMode.Text);
            Assert.True(dialog._crossfade.Checked);
            Assert.False(dialog._hardCut.Checked);
            Assert.Equal(8, dialog._seconds.Value);
            Assert.True(dialog._seconds.Enabled);
            Assert.Equal("Track", dialog._replayGain.Text);

            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WaveOut");
            dialog._seconds.Value = 12;
            dialog._replayGain.SelectedIndex = dialog._replayGain.FindStringExact("Off");
            return true;
        };

        Click(h.Form._settings);

        Assert.True(opened);
        var expected = new PlayerSettings
        {
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 12,
            ReplayGainMode = ReplayGainMode.Off,
        };
        Assert.Equal(expected, h.Engine.View.Settings);
        Assert.Equal(expected, new SettingsStore(h.SettingsPath).Settings); // saved for the next launch
        Assert.Equal(AudioOutputMode.WaveOut, h.Engine.View.MachineSettings.OutputMode);
        Assert.Equal(AudioOutputMode.WaveOut, new MachineSettingsStore(h.MachinePath).Settings.OutputMode);
    });

    [Fact]
    public void TheBlankDuration_IsSetInSettings_KeptThroughOtherChanges_AndTheEditorsGreyFollows() => RunSta(() =>
    {
        using var h = new Harness();
        var editor = h.Form._editor;
        EditingAlphaToBeta(h);
        Assert.Equal("5", editor._fadeOutDuration.PlaceholderText);

        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal(5, dialog._blankSeconds.Value);
            Assert.True(dialog._blankSeconds.Enabled); // under Hard cut too: it is not N
            dialog._blankSeconds.Value = 8;
            return true;
        };
        Click(h.Form._settings);

        Assert.Equal(8, h.Engine.View.Settings.BlankDurationSeconds);
        Assert.Equal(8, new SettingsStore(h.SettingsPath).Settings.BlankDurationSeconds);
        Assert.Equal("8", editor._fadeOutDuration.PlaceholderText);
        Assert.Equal("8", editor._fadeInDuration.PlaceholderText);

        // Another library setting saved later keeps the blank duration.
        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal(8, dialog._blankSeconds.Value);
            dialog._replayGain.SelectedIndex = dialog._replayGain.FindStringExact("Off");
            return true;
        };
        Click(h.Form._settings);

        Assert.Equal(8, h.Engine.View.Settings.BlankDurationSeconds);
        Assert.Equal(ReplayGainMode.Off, h.Engine.View.Settings.ReplayGainMode);
    });

    [Fact]
    public void TheSettingsDialog_GroupsThisMachine_AndThisLibrary() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal("This machine", dialog._machineGroup.Text);
            Assert.Equal("This library", dialog._libraryGroup.Text);
            Assert.True(dialog._machineGroup.Contains(dialog._outputMode));
            Assert.True(dialog._machineGroup.Contains(dialog._nextLaunch));
            Assert.True(dialog._machineGroup.Contains(dialog._inUse));
            Assert.True(dialog._libraryGroup.Contains(dialog._hardCut));
            Assert.True(dialog._libraryGroup.Contains(dialog._crossfade));
            Assert.True(dialog._libraryGroup.Contains(dialog._seconds));
            Assert.True(dialog._libraryGroup.Contains(dialog._blankSeconds));
            Assert.True(dialog._libraryGroup.Contains(dialog._replayGain));
            return false;
        };
        Click(h.Form._settings);
    });

    [Fact]
    public void EditingOnlyTheMachineGroup_CallsChangeMachineSettings_AndLeavesTheLibrarysFile() => RunSta(() =>
    {
        using var h = new Harness();
        var notices = new List<string>();
        ((PostingSink)h.Sink).Noticed += notices.Add;
        h.Form.ShowSettings = dialog =>
        {
            Assert.False(dialog.MachineChanged || dialog.LibraryChanged); // opening is no change
            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WaveOut");
            Assert.True(dialog.MachineChanged);
            Assert.False(dialog.LibraryChanged);
            return true;
        };

        Click(h.Form._settings);

        Assert.Equal(AudioOutputMode.WaveOut, new MachineSettingsStore(h.MachinePath).Settings.OutputMode);
        Assert.False(File.Exists(h.SettingsPath)); // the library's file is untouched
        Assert.Equal("Saved machine settings.", Assert.Single(notices));
    });

    [Fact]
    public void EditingOnlyTheLibraryGroup_CallsChangeSettings_AndLeavesTheMachinesFile() => RunSta(() =>
    {
        using var h = new Harness();
        var machine = File.ReadAllText(h.MachinePath); // the library, remembered as the engine opened
        var notices = new List<string>();
        ((PostingSink)h.Sink).Noticed += notices.Add;
        h.Form.ShowSettings = dialog =>
        {
            dialog._crossfade.Checked = true;
            Assert.True(dialog.LibraryChanged);
            Assert.False(dialog.MachineChanged);
            return true;
        };

        Click(h.Form._settings);

        Assert.Equal(UnauthoredAdjacency.DefaultCrossfade, new SettingsStore(h.SettingsPath).Settings.UnauthoredAdjacency);
        Assert.Equal(machine, File.ReadAllText(h.MachinePath));
        Assert.Equal("Saved settings.", Assert.Single(notices));
    });

    [Fact]
    public void AGroupChangedAndChangedBack_IsNotSent() => RunSta(() =>
    {
        using var h = new Harness();
        var notices = new List<string>();
        ((PostingSink)h.Sink).Noticed += notices.Add;
        h.Form.ShowSettings = dialog =>
        {
            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WaveOut");
            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WASAPI shared");
            dialog._replayGain.SelectedIndex = dialog._replayGain.FindStringExact("Off");
            dialog._replayGain.SelectedIndex = dialog._replayGain.FindStringExact("Track");
            Assert.False(dialog.MachineChanged || dialog.LibraryChanged);
            return true;
        };

        Click(h.Form._settings);

        Assert.Empty(notices);
        Assert.False(File.Exists(h.SettingsPath));
    });

    [Fact]
    public void TheReplayGainList_IsTrackByDefault_AndReachesChangeSettings() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal("Track", dialog._replayGain.Text);
            dialog._replayGain.SelectedIndex = dialog._replayGain.FindStringExact("Off");
            return true;
        };

        Click(h.Form._settings);

        Assert.Equal(new PlayerSettings { ReplayGainMode = ReplayGainMode.Off }, h.Engine.View.Settings);
    });

    [Fact]
    public void ChoosingHardCut_GreysOutN_AndIsWhatOKSends() => RunSta(() =>
    {
        using var h = new Harness();
        h.Engine.ChangeSettings(new PlayerSettings { UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade });
        h.Form.ShowSettings = dialog =>
        {
            dialog._hardCut.Checked = true;
            Assert.False(dialog._crossfade.Checked);
            Assert.False(dialog._seconds.Enabled);
            return true;
        };

        Click(h.Form._settings);

        Assert.Equal(UnauthoredAdjacency.HardCut, h.Engine.View.Settings.UnauthoredAdjacency);
    });

    [Fact]
    public void CancellingTheSettingsDialog_ChangesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ShowSettings = dialog =>
        {
            dialog._crossfade.Checked = true;
            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WaveOut");
            return false;
        };

        Click(h.Form._settings);

        Assert.Equal(new PlayerSettings(), h.Engine.View.Settings);
        Assert.Equal(AudioOutputMode.WasapiShared, h.Engine.View.MachineSettings.OutputMode);
        Assert.False(File.Exists(h.SettingsPath));
    });

    [Fact]
    public void TheSettingsDialog_SaysTheOutputModeAppliesAtTheNextLaunch_AndNamesNoOtherModeWithoutAFallback() => RunSta(() =>
    {
        using var h = new Harness(inUse: AudioOutputMode.WasapiShared);
        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal("Applies at the next launch.", dialog._nextLaunch.Text);
            Assert.Equal("", dialog._inUse.Text);
            return false;
        };
        Click(h.Form._settings);
    });

    [Theory]
    [InlineData(AudioOutputMode.WaveOut, "In use now: WaveOut")]
    [InlineData(null, "In use now: no audio output")]
    public void WhenAFallbackIsActive_TheSettingsDialogShowsTheModeInUse_BesideTheOneChosen(
        AudioOutputMode? inUse, string shown) => RunSta(() =>
    {
        using var h = new Harness(inUse: inUse);
        h.Engine.ChangeMachineSettings(new MachineSettings { OutputMode = AudioOutputMode.WasapiExclusive });
        h.Form.ShowSettings = dialog =>
        {
            Assert.Equal("WASAPI exclusive", dialog._outputMode.Text);
            Assert.Equal(shown, dialog._inUse.Text);
            return false;
        };
        Click(h.Form._settings);
    });

    // ── Build issue 15: every adjacency, and unsaved edits ─────────────────────────────

    /// <summary>Right-clicks a row: selects it alone and opens the menu.</summary>
    private static void OpenMenuOn(ListView list, int index)
    {
        _ = list.Handle;
        foreach (ListViewItem item in list.Items)
        {
            item.Selected = false;
        }
        SelectRows(list, index);
        OpenMenu(list);
    }

    /// <summary>The adjacency the editor has open, as its two files.</summary>
    private static (string, string) Opened(Harness h) =>
        (h.Form._editor.Adjacency!.Outgoing.FilePath, h.Form._editor.Adjacency.Incoming.FilePath);

    private static (string A, string B, string C) ThreeQueued(Harness h)
    {
        var a = TestAudio.WriteDcWav("alpha.wav", 5, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 5, 0.2);
        var c = TestAudio.WriteDcWav("gamma.wav", 5, 0.3);
        h.Form.AddPaths(new[] { a, b, c });
        return (a, b, c);
    }

    [Fact]
    public void ThisToNext_OnTheLastRow_IsDisabledWithRepeatOff_AndWrapsUnderRepeatAll() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, _, c) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 2);
        Assert.False(h.Form._editMenu.Next.Enabled);

        h.Engine.SetRepeat(RepeatMode.All);
        OpenMenuOn(h.Form._queueList, 2);
        Assert.True(h.Form._editMenu.Next.Enabled);
        Click(h.Form._editMenu.Next);

        Assert.Equal((c, a), Opened(h));
    });

    [Fact]
    public void ThisToNext_OnAMiddleRow_OpensTheAdjacencyOutOfIt() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 1);
        Click(h.Form._editMenu.Next);

        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void PreviousToThis_OpensTheAdjacencyIntoTheRow_AndIsDisabledOnTheFirstRow() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 0);
        Assert.False(h.Form._editMenu.Previous.Enabled);

        OpenMenuOn(h.Form._queueList, 1);
        Assert.True(h.Form._editMenu.Previous.Enabled);
        Click(h.Form._editMenu.Previous);

        Assert.Equal((a, b), Opened(h));
    });

    [Fact]
    public void ThisToThis_IsShownOnlyUnderRepeatOne_AndOpensTheRowIntoItself() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, _) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 1);
        Assert.False(h.Form._editMenu.Self.Available);

        h.Engine.SetRepeat(RepeatMode.One);
        OpenMenuOn(h.Form._queueList, 1);
        Assert.True(h.Form._editMenu.Self.Available);
        Assert.True(h.Form._editMenu.Self.Enabled);
        Click(h.Form._editMenu.Self);

        Assert.Equal((b, b), Opened(h));
    });

    [Fact]
    public void TheMenuItems_SayWhichAdjacency_AndAreDisabledWithoutASingleRow() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);

        OpenMenu(h.Form._queueList); // nothing selected
        Assert.False(h.Form._editMenu.Next.Enabled);
        Assert.False(h.Form._editMenu.Previous.Enabled);
        Assert.Contains("this → next", h.Form._editMenu.Next.Text);
        Assert.Contains("previous → this", h.Form._editMenu.Previous.Text);
        Assert.Contains("this → this", h.Form._editMenu.Self.Text);
    });

    // ── The row button (interim transport 07) ───────────────────────────────────────────

    /// <summary>Presses the row's editor button as a click does: a left click at the middle of
    /// its cell, through the list's own mouse handler.</summary>
    private static void ClickEditButton(ListView list, int index)
    {
        _ = list.Handle;
        var cell = list.Items[index].SubItems[list.Columns.Count - 2].Bounds; // before the speaker's
        var args = new MouseEventArgs(MouseButtons.Left, 1, cell.X + cell.Width / 2, cell.Y + cell.Height / 2, 0);
        typeof(Control).GetMethod("OnMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(list, new object[] { args });
    }

    private static string[] EditGlyphs(ListView list) =>
        list.Items.Cast<ListViewItem>().Select(i => i.SubItems[list.Columns.Count - 2].Text).ToArray();

    [Fact]
    public void TheRowButton_OpensThisToNext_AsTheMenuDoes() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = ThreeQueued(h);

        ClickEditButton(h.Form._queueList, 1);

        Assert.Equal((b, c), Opened(h));
        Assert.Equal(h.Engine.AdjacenciesOf(h.Engine.View.Queue[1].Id).Next!.Name, h.Form._editor._title.Text);
        OpenMenuOn(h.Form._queueList, 1);
        Click(h.Form._editMenu.Next);
        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void TheRowButton_IsTheRightmostColumn_OnEveryRowButTheLast_EvenUnderRepeatAll() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);

        Assert.Equal(h.Form.EditColumn, h.Form._queueList.Columns.Count - 2); // the speaker's is shown first
        Assert.Equal(new[] { "✎", "✎", "" }, EditGlyphs(h.Form._queueList));

        h.Engine.SetRepeat(RepeatMode.All); // the last row has a this → next, but no button (D9)
        ClickEditButton(h.Form._queueList, 2);

        Assert.Equal(new[] { "✎", "✎", "" }, EditGlyphs(h.Form._queueList));
        Assert.Null(h.Form._editor.Adjacency);
    });

    [Fact]
    public void TheRowButton_IsBoldWhileItsAdjacencyIsAuthored_AndGreyWhileNot() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Engine.Author(h.Engine.AdjacenciesOf(h.Engine.View.Queue[0].Id).Next!, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeOutDuration = TimeSpan.FromSeconds(0.5),
        });

        var cells = h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.SubItems[h.Form.EditColumn]).ToArray();
        Assert.True(cells[0].Font.Bold);
        Assert.Equal(h.Form.Palette.Text, cells[0].ForeColor);
        Assert.False(cells[1].Font.Bold);
        Assert.Equal(h.Form.Palette.Dim, cells[1].ForeColor);

        h.Engine.Unauthor(h.Engine.AdjacenciesOf(h.Engine.View.Queue[0].Id).Next!);

        Assert.False(cells[0].Font.Bold);
    });

    [Fact]
    public void TheRowButton_SavesABoxBeingTypedIn_ThenOpensItsAdjacency() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c) = ThreeQueued(h);
        ClickEditButton(h.Form._queueList, 0);
        h.Form._editor._fadeOutPoint.Text = "2";

        ClickEditButton(h.Form._queueList, 1);

        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void AClickElsewhereOnARow_OpensNothing() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        _ = h.Form._queueList.Handle;
        var title = h.Form._queueList.Items[1].SubItems[1].Bounds;

        Assert.Null(h.Form.EditButtonAt(new System.Drawing.Point(title.X + 4, title.Y + 4)));
        Assert.Equal(1, h.Form.EditButtonAt(h.Form._queueList.Items[1].SubItems[h.Form.EditColumn].Bounds.Location + new Size(4, 4)));
    });

    [Fact]
    public void EditTransitionAt_ARowOutOfRange_OpensNothing() => RunSta(() =>
    {
        using var h = new Harness();

        h.Form.EditTransitionAt(0); // --edit-first with nothing queued

        Assert.Null(h.Form._editor.Adjacency);
        Assert.Equal("No boundary selected", h.Form._editor._title.Text);
    });

    [Fact]
    public void Switching_SavesABoxBeingTypedIn_ThenOpensTheOther() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";

        h.Form.EditTransitionAt(1);

        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void Switching_WithTextTheEngineRefuses_DropsIt_AndOpensTheOther() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "9"; // past alpha's end

        h.Form.EditTransitionAt(1);

        Assert.Null(h.Store.Get(a, b));
        Assert.StartsWith("Not saved", h.Form._status.Text);
        Assert.Equal((b, c), Opened(h));
        Assert.Equal("", h.Form._editor._fadeOutPoint.Text);
    });

    [Fact]
    public void TheFromEndBox_AndTheCurve_SaveAtOnce() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        h.Form._editor.Commit();

        h.Form._editor._curve.SelectedIndex = h.Form._editor._curve.FindStringExact("Linear");
        Assert.Equal(FadeCurveKind.Linear, h.Store.Get(a, b)!.Curve);

        h.Form._editor._fadeOutFromEnd.Checked = true;
        Assert.True(h.Store.Get(a, b)!.FadeOutPoint!.FromEnd);
        Assert.False(h.Form._editor.HasUnsavedEdits);
    });

    [Fact]
    public void ABlankDraft_OnAnAdjacencyWithNothingStored_IsNotUnsaved() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);

        h.Form._editor._curve.SelectedIndex = h.Form._editor._curve.FindStringExact("Linear");

        Assert.False(h.Form._editor.HasUnsavedEdits); // saving it would store nothing
    });

    [Fact]
    public void ATypedNumberThatCannotBeRead_IsAnUnsavedEdit() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);

        h.Form._editor._fadeInDuration.Text = "abc";

        Assert.True(h.Form._editor.HasUnsavedEdits);
    });

    [Fact]
    public void ReopeningTheSameAdjacency_KeepsTextThatCouldNotBeSaved() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "abc";

        h.Form.EditTransitionAt(0);

        Assert.Equal("abc", h.Form._editor._fadeOutPoint.Text);
    });

    [Fact]
    public void ClosingTheWindow_SavesABoxBeingTypedIn() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";

        var closing = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Form, new object[] { closing });

        Assert.False(closing.Cancel);
        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(a, b)!.FadeOutPoint!.Position);
    });

    [Fact]
    public void AStoredPoint_LoadsToTheTick_SoAnUntouchedDraftIsNotUnsaved() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        // A captured point whose span does not survive a trip through seconds: loading it that
        // way made the untouched draft differ from the store by a tick.
        var frames = Enumerable.Range(1, 44100).First(f =>
        {
            var span = Ticks.SpanOf(f, 44100);
            return TimeSpan.FromSeconds(span.TotalSeconds) != span;
        });
        var stored = new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(Ticks.SpanOf(frames, 44100), FromEnd: false),
            Curve = FadeCurveKind.EqualPower,
        };
        h.Store.Upsert(a, b, stored);

        h.Form.EditTransitionAt(0);

        Assert.False(h.Form._editor.HasUnsavedEdits);
        h.Form._editor.Commit();
        Assert.Equal(stored, h.Store.Get(a, b));
    });

    [Fact]
    public void ASaveTheStoreCouldNotWrite_LeavesTheEditsUnsaved() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        h.Form._editor.Commit();
        var path = h.Library.TransitionsPath;
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            h.Form._editor._fadeOutPoint.Text = "2";
            h.Form._editor.Commit();

            Assert.StartsWith("Not saved", h.Form._status.Text);
            Assert.True(h.Form._editor.HasUnsavedEdits);
        }
        finally
        {
            File.SetAttributes(path, FileAttributes.Normal);
        }
    });

    // ── Build issue 16: preview ─────────────────────────────────────────────────────────

    /// <summary>Three DC tracks queued, 5 s each, with the editor open on alpha → beta and a
    /// draft that fades out at 3 s.</summary>
    private static (string A, string B, string C) EditingADraft(Harness h)
    {
        var rows = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "3", "1", "0", "1");
        h.Form.Render();
        return rows;
    }

    [Fact]
    public void Preview_PreviewAgain_AndStopPreview_AreTheEditorsButtons_WithThePreviewShownBesideThem() => RunSta(() =>
    {
        using var h = new Harness();
        EditingADraft(h);
        var editor = h.Form._editor;
        Assert.Equal("Preview", editor._preview.Text);
        Assert.False(editor._stopPreview.Enabled);
        Assert.Equal("", editor._previewStatus.Text);

        Click(editor._preview);
        h.Output.Advance(44100);
        h.Form.Render();

        Assert.Equal("Preview again", editor._preview.Text);
        Assert.True(editor._stopPreview.Enabled);
        Assert.Equal("Pre-roll · outgoing 0:01", editor._previewStatus.Text);

        h.Output.Advance(2 * 44100 + 22050); // half a second into the fade at 3 s
        h.Form.Render();
        Assert.Equal("Transition · outgoing 0:03 · incoming 0:00", editor._previewStatus.Text);

        Click(editor._stopPreview);
        h.Form.Render();

        Assert.Null(h.Engine.View.Preview);
        Assert.Equal("Preview", editor._preview.Text);
        Assert.False(editor._stopPreview.Enabled);
        Assert.Equal("", editor._previewStatus.Text);
    });

    [Fact]
    public void Preview_IsDisabledWithNoAdjacencyOpen() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.Render();

        Assert.False(h.Form._editor._preview.Enabled);
        Assert.False(h.Form._editor._stopPreview.Enabled);
    });

    [Fact]
    public void TheMainWindow_ShowsTheQueueHeld_WhileAPreviewRuns() => RunSta(() =>
    {
        using var h = new Harness();
        EditingADraft(h);
        h.Form.PlayRowAt(0);
        h.Output.Advance(44100);
        h.Form.Render();
        Assert.Equal("0:01 / -0:04", h.Form._time.Text);

        Click(h.Form._editor._preview);
        h.Output.Advance(2 * 44100);
        h.Form.Render();

        Assert.Equal("Held 0:01 / -0:04", h.Form._time.Text);
        Assert.Equal("▶", h.Form._playPause.Text);

        Click(h.Form._editor._stopPreview);
        h.Form.Render();

        Assert.Equal("0:01 / -0:04", h.Form._time.Text); // paused where it was held
        Assert.Equal("▶", h.Form._playPause.Text);
    });

    [Fact]
    public void Preview_WithTextThatIsNotANumber_PreviewsNothing_AndSaysWhy() => RunSta(() =>
    {
        using var h = new Harness();
        EditingADraft(h);
        h.Form._editor._fadeOutDuration.Text = "soon";

        Click(h.Form._editor._preview);

        Assert.Null(h.Engine.View.Preview);
        Assert.StartsWith("Not previewed", h.Form._status.Text);
        Assert.True(IsSetVisible(h.Form._editor.ReasonFor(TransitionField.FadeOutDuration)));
    });

    [Fact]
    public void CaptureFadeIn_BeforeThePreviewsIncomingTrackStarts_LeavesTheField_AndSaysWhy() => RunSta(() =>
    {
        using var h = new Harness();
        EditingADraft(h);
        h.Form._editor._fadeInPoint.Text = "0.5";
        Click(h.Form._editor._preview);
        h.Output.Advance(44100);
        h.Form.Render();

        Click(h.Form._editor._captureIn);

        Assert.Equal("0.5", h.Form._editor._fadeInPoint.Text);
        Assert.Contains("has not started yet", h.Form._status.Text);
    });

    [Fact]
    public void OpeningAnotherAdjacency_EndsThePreview() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = EditingADraft(h);
        Click(h.Form._editor._preview);
        Assert.NotNull(h.Engine.View.Preview);

        h.Form.EditTransitionAt(1);

        Assert.Equal((b, c), Opened(h));
        Assert.Null(h.Engine.View.Preview);
    });

    // ── Build issue 17: the clip light ──────────────────────────────────────────────────

    /// <summary>A 3 s track at 0.8 tagged +6 dB: with ReplayGain on, it plays at 1.6 throughout.</summary>
    private static Track Boosted(string path) => new()
    {
        FilePath = path,
        Length = Length.FromFrames(3 * 44100, 44100),
        ReplayGainDb = 6.0,
    };

    [Fact]
    public void TheClipLight_IsDarkByDefault() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.Equal("● No clips", h.Form._clip.Text);
        Assert.Equal(h.Form.Palette.Dim, h.Form._clip.ForeColor);
        Assert.Equal("Lights when the output goes beyond full scale.", h.Form._clip.ToolTipText);
    });

    [Fact]
    public void AClipHeard_LightsIt_WithTheCountAndTheLatestCause_AndAClickClearsIt() => RunSta(() =>
    {
        using var h = new Harness(latency: 4410);
        var c = TestAudio.WriteDcWav("c.wav", 3, 0.8);
        h.Engine.AddTracks(new List<Track> { Boosted(c) });
        h.Engine.Play();
        h.Output.Advance(4410); // rendered, not yet heard
        Assert.Equal("● No clips", h.Form._clip.Text);

        h.Output.Advance(4410);
        h.Form.Render(); // the timer's tick: the view lit when the listener heard it

        var name = Path.GetFileNameWithoutExtension(c);
        Assert.Equal($"● 1 clip · latest: {name}", h.Form._clip.Text);
        Assert.Equal(Color.White, h.Form._clip.ForeColor);
        Assert.Equal(Color.Firebrick, h.Form._clip.BackColor);

        h.Engine.Stop(); // Stop leaves it lit
        Assert.Equal($"● 1 clip · latest: {name}", h.Form._clip.Text);

        Click(h.Form._clip);

        Assert.Null(h.Engine.View.Clip);
        Assert.Equal("● No clips", h.Form._clip.Text);
        Assert.Equal(h.Form.Palette.Dim, h.Form._clip.ForeColor);
    });

    [Fact]
    public void ALitLightWithALongCause_NeverPushesTheNoticeCountOffTheStrip() => RunSta(() =>
    {
        using var h = new Harness();
        _ = h.Form.Handle; // laid out at its default size, still never shown
        var c = Path.Combine(h.TempDir, string.Concat(Enumerable.Repeat("A very long title ", 12)) + ".wav");
        File.Copy(TestAudio.WriteDcWav("c.wav", 3, 0.8), c);
        h.Engine.AddTracks(new List<Track> { Boosted(c) });
        h.Engine.Play();
        h.Output.Advance(4410);
        h.Form.Render();
        h.Sink.Notice("Saved a → b.");
        h.Form._clip.Owner!.PerformLayout();

        Assert.StartsWith("● 1 clip", h.Form._clip.Text);
        Assert.Contains(Path.GetFileNameWithoutExtension(c), h.Form._clip.ToolTipText); // whole there
        Assert.Equal(ToolStripItemPlacement.Main, h.Form._noticeCount.Placement);
        Assert.Equal(ToolStripItemPlacement.Main, h.Form._clip.Placement);
        Assert.True(h.Form._clip.Bounds.Right <= h.Form._clip.Owner.DisplayRectangle.Right);
    });

    // ── The library (library issue 02) ──────────────────────────────────────────────────

    [Fact]
    public void TheTitle_NamesTheOpenLibrary_AndTheModeInUse() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.Equal($"Untitled — faderio — {h.Library.Name} · WASAPI shared at {h.Engine.SampleRate} Hz", h.Form.Text);
    });

    [Fact]
    public void OpenLibrary_SendsThePickedFolder_AndTheTitleAndQueueFollow() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        var second = Path.Combine(h.TempDir, "Second library");
        Directory.CreateDirectory(second);
        h.Form.EditTransitionAt(0);
        h.Form.ChooseLibrary = () => second;

        Click(h.Form._openLibrary);

        Assert.Equal("Second library", h.Engine.View.Library!.Name);
        Assert.StartsWith("Untitled — faderio — Second library · ", h.Form.Text);
        Assert.Empty(h.Form._queueList.Items);
        Assert.Null(h.Form._editor.Adjacency); // what it had open was the other library's
        Assert.Equal("No boundary selected", h.Form._editor._title.Text);
        Assert.False(h.Form._editor._render.Enabled);
    });

    [Fact]
    public void OpenLibrary_Cancelled_ChangesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        var before = h.Engine.View.Library;
        h.Form.ChooseLibrary = () => null;

        Click(h.Form._openLibrary);

        Assert.Equal(before, h.Engine.View.Library);
        Assert.Equal(3, h.Form._queueList.Items.Count);
    });

    [Fact]
    public void OpenLibrary_SavesABoxBeingTypedIn_InTheLibraryLeft() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        var adjacency = h.Form._editor.Adjacency!;
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        var second = Path.Combine(h.TempDir, "Second");
        Directory.CreateDirectory(second);
        h.Form.ChooseLibrary = () => second;

        Click(h.Form._openLibrary);

        Assert.NotNull(h.Store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath));
        Assert.Equal("Second", h.Engine.View.Library!.Name);
        Assert.Null(h.Engine.Transition(adjacency).Global);
    });

    // ── Library issue 03: the window opens where it was closed ─────────────────────────

    private static void CloseWindow(Harness h)
    {
        var closing = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Form, new object[] { closing });
        Assert.False(closing.Cancel);
    }

    private static string TempWindowFile() =>
        Path.Combine(TestTemp.Root, "window", Guid.NewGuid().ToString("N"), "window.json");

    [Fact]
    public void TheWindow_ClosedSomewhere_OpensThereAtTheNextLaunch() => RunSta(() =>
    {
        var file = TempWindowFile();
        var area = Screen.PrimaryScreen!.WorkingArea;
        var placed = new Rectangle(area.X + 40, area.Y + 30, 1000, 560);
        using (var first = new Harness(windowFile: file))
        {
            Assert.Equal(FormStartPosition.CenterScreen, first.Form.StartPosition); // nothing saved yet
            first.Form.Bounds = placed;
            CloseWindow(first);
        }
        Assert.True(File.Exists(file));

        using var relaunched = new Harness(windowFile: file);

        Assert.Equal(FormStartPosition.Manual, relaunched.Form.StartPosition);
        Assert.Equal(placed, relaunched.Form.Bounds);
        Assert.Equal(FormWindowState.Normal, relaunched.Form.WindowState);
    });

    [Fact]
    public void AWindowClosedMaximised_OpensMaximised_OverTheBoundsItHadBefore() => RunSta(() =>
    {
        var file = TempWindowFile();
        var area = Screen.PrimaryScreen!.WorkingArea;
        new WindowGeometry(area.X + 10, area.Y + 10, 800, 500, Maximised: true).Save(file);

        using var h = new Harness(windowFile: file);

        Assert.Equal(FormWindowState.Maximized, h.Form.WindowState);
    });

    [Fact]
    public void AWindowSavedOffEveryScreen_OpensAtTheDefault_Centred() => RunSta(() =>
    {
        var file = TempWindowFile();
        new WindowGeometry(-100_000, -100_000, 900, 560, Maximised: false).Save(file);

        using var h = new Harness(windowFile: file);

        Assert.Equal(FormStartPosition.CenterScreen, h.Form.StartPosition);
        Assert.Equal(new Size(1240, 660), h.Form.Size);
    });

    [Fact]
    public void AnUnreadableWindowFile_OpensAtTheDefault_WithoutANotice() => RunSta(() =>
    {
        var file = TempWindowFile();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, "{ not json");

        using var h = new Harness(windowFile: file);

        Assert.Equal(FormStartPosition.CenterScreen, h.Form.StartPosition);
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void WithNoWindowFile_ClosingWritesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        CloseWindow(h);
        Assert.False(File.Exists(Path.Combine(h.TempDir + "-machine", "window.json")));
    });

    // ── Playlists issue 02: the playlist sidebar ────────────────────────────────────────

    private static string[] Names(PlaylistSidebar sidebar) =>
        sidebar._list.Items.Cast<ListViewItem>().Select(i => i.Text).ToArray();

    /// <summary>The name the sidebar highlights: bold, in the accent on Raised (studio theme issues 02, 03).</summary>
    private static string Highlighted(PlaylistSidebar sidebar)
    {
        var open = sidebar._list.Items.Cast<ListViewItem>().Where(i => i.Font.Bold).ToList();
        Assert.Single(open);
        Assert.Equal(sidebar.Palette!.Raised, open[0].BackColor);
        Assert.Equal(sidebar.Palette!.Accent, open[0].ForeColor);
        return open[0].Text;
    }

    private static int IndexOf(PlaylistSidebar sidebar, string name) => Array.IndexOf(Names(sidebar), name);

    /// <summary>Two playlists: "Evening" with three rows, and "Untitled", with two, open. Returns
    /// each one's files.</summary>
    private static (string[] Evening, string[] Untitled) TwoPlaylists(Harness h)
    {
        var (a, b, c) = ThreeQueued(h);
        h.Engine.RenamePlaylist("Untitled", "Evening");
        h.Form._playlists._new.PerformClick();
        var untitled = new[] { TestAudio.WriteDcWav("delta.wav", 5, 0.2), TestAudio.WriteDcWav("epsilon.wav", 5, 0.2) };
        h.Form.AddPaths(untitled);
        return (new[] { a, b, c }, untitled);
    }

    private static string[] Queued(Harness h) => h.Engine.View.Queue.Select(r => r.Track.FilePath).ToArray();

    private static LabelEditEventArgs EndLabelEdit(PlaylistSidebar sidebar, int index, string? label)
    {
        var args = label is null ? new LabelEditEventArgs(index) : new LabelEditEventArgs(index, label);
        typeof(ListView).GetMethod("OnAfterLabelEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(sidebar._list, new object[] { args });
        return args;
    }

    [Fact]
    public void AtLaunch_TheSidebarShowsUntitled_Highlighted() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.Equal(new[] { "Untitled" }, Names(h.Form._playlists));
        Assert.Equal("Untitled", Highlighted(h.Form._playlists));
    });

    [Fact]
    public void NewPlaylist_AddsUntitled2_AndHighlightsIt() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);

        h.Form._playlists._new.PerformClick();

        Assert.Equal(new[] { "Untitled", "Untitled 2" }, Names(h.Form._playlists));
        Assert.Equal("Untitled 2", Highlighted(h.Form._playlists));
        Assert.Empty(h.Form._queueList.Items);
    });

    [Fact]
    public void AClick_OpensThatPlaylist_AndTheHighlightFollows() => RunSta(() =>
    {
        using var h = new Harness();
        var (evening, _) = TwoPlaylists(h);

        h.Form._playlists.OpenAt(IndexOf(h.Form._playlists, "Evening"));

        Assert.Equal("Evening", h.Engine.View.OpenPlaylist);
        Assert.Equal("Evening", Highlighted(h.Form._playlists));
        Assert.Equal(evening, Queued(h));
        Assert.Equal(3, h.Form._queueList.Items.Count);
    });

    [Fact]
    public void AClick_SavesABoxBeingTypedIn_OpensIt_AndEmptiesTheEditor() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);
        h.Form.EditTransitionAt(0);
        var adjacency = h.Form._editor.Adjacency!;
        h.Form._editor._fadeOutPoint.Text = "2";

        h.Form._playlists.OpenAt(IndexOf(h.Form._playlists, "Evening"));

        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath)!.FadeOutPoint!.Position);
        Assert.Equal("Evening", h.Engine.View.OpenPlaylist);
        Assert.Null(h.Form._editor.Adjacency); // what it had open was the queue left
        Assert.Equal("", h.Form._editor._fadeOutPoint.Text);
    });

    [Fact]
    public void AClickOnTheOpenPlaylist_ChangesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";

        h.Form._playlists.OpenAt(IndexOf(h.Form._playlists, "Untitled"));

        Assert.Equal("Untitled", h.Engine.View.OpenPlaylist);
        Assert.NotNull(h.Form._editor.Adjacency);
    });

    [Fact]
    public void Enter_CommitsARename_WithTheOldNameAndTheNew() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);

        var args = EndLabelEdit(h.Form._playlists, IndexOf(h.Form._playlists, "Evening"), "Morning");

        Assert.True(args.CancelEdit); // the label is the view's: the new name comes back from it
        Assert.Equal(new[] { "Morning", "Untitled" }, h.Engine.View.Playlists);
        Assert.Equal(new[] { "Morning", "Untitled" }, Names(h.Form._playlists));
        Assert.True(File.Exists(Path.Combine(h.Library.PlaylistsFolder, "Morning.m3u8")));
        Assert.Equal("Untitled", Highlighted(h.Form._playlists));
    });

    [Fact]
    public void Esc_EndsARename_WithoutACall() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);

        EndLabelEdit(h.Form._playlists, IndexOf(h.Form._playlists, "Evening"), label: null);

        Assert.Equal(new[] { "Evening", "Untitled" }, h.Engine.View.Playlists);
        Assert.Equal(new[] { "Evening", "Untitled" }, Names(h.Form._playlists));
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void ARefusedRename_ShowsTheEnginesNotice_AndTheLabelKeepsTheOldName() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);

        var args = EndLabelEdit(h.Form._playlists, IndexOf(h.Form._playlists, "Untitled"), "evening");

        Assert.True(args.CancelEdit);
        Assert.Equal(new[] { "Evening", "Untitled" }, Names(h.Form._playlists));
        Assert.StartsWith("Not renamed: Untitled.", h.Form._status.Text);
    });

    [Fact]
    public void ARenameTheUserDidNotAskFor_IsRefused() => RunSta(() =>
    {
        using var h = new Harness();
        var args = new LabelEditEventArgs(0);

        // The list's own slow click on a selected item starts an edit; only F2, a double-click
        // and the menu's Rename may.
        typeof(ListView).GetMethod("OnBeforeLabelEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Form._playlists._list, new object[] { args });

        Assert.True(args.CancelEdit);
    });

    [Fact]
    public void TheMenu_OffersRenameAndDelete_OnlyOnASelectedPlaylist() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);

        OpenMenu(h.Form._playlists._list);
        Assert.True(h.Form._playlists._new.Enabled);
        Assert.False(h.Form._playlists._rename.Enabled);
        Assert.False(h.Form._playlists._delete.Enabled);

        SelectRows(h.Form._playlists._list, 0);
        OpenMenu(h.Form._playlists._list);
        Assert.True(h.Form._playlists._rename.Enabled);
        Assert.True(h.Form._playlists._delete.Enabled);
    });

    [Fact]
    public void Delete_AsksOnce_AndNoKeepsThePlaylist() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);
        var asked = new List<string>();
        h.Form._playlists.ConfirmDelete = name =>
        {
            asked.Add(name);
            return false;
        };
        SelectRows(h.Form._playlists._list, IndexOf(h.Form._playlists, "Evening"));

        h.Form._playlists._delete.PerformClick();

        Assert.Equal(new[] { "Evening" }, asked);
        Assert.Equal(new[] { "Evening", "Untitled" }, h.Engine.View.Playlists);
    });

    [Fact]
    public void Delete_Confirmed_DeletesThePlaylistsFile() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, untitled) = TwoPlaylists(h);
        h.Form._playlists.ConfirmDelete = _ => true;
        SelectRows(h.Form._playlists._list, IndexOf(h.Form._playlists, "Evening"));

        h.Form._playlists._delete.PerformClick();

        Assert.Equal(new[] { "Untitled" }, Names(h.Form._playlists));
        Assert.False(File.Exists(Path.Combine(h.Library.PlaylistsFolder, "Evening.m3u8")));
        Assert.Equal(untitled, Queued(h)); // the open one was not touched
    });

    [Fact]
    public void DeletingTheOpenPlaylist_OpensTheFirstRemaining() => RunSta(() =>
    {
        using var h = new Harness();
        var (evening, _) = TwoPlaylists(h);
        h.Form._playlists.ConfirmDelete = _ => true;
        SelectRows(h.Form._playlists._list, IndexOf(h.Form._playlists, "Untitled"));

        h.Form._playlists._delete.PerformClick();

        Assert.Equal(new[] { "Evening" }, Names(h.Form._playlists));
        Assert.Equal("Evening", Highlighted(h.Form._playlists));
        Assert.Equal(evening, Queued(h));
    });

    [Fact]
    public void DeletingTheOpenPlaylist_AsksOnlyToDelete_AndSavesABoxBeingTypedIn() => RunSta(() =>
    {
        using var h = new Harness();
        TwoPlaylists(h);
        h.Form.EditTransitionAt(0);
        var adjacency = h.Form._editor.Adjacency!;
        h.Form._editor._fadeOutPoint.Text = "2";
        var asks = 0;
        h.Form._playlists.ConfirmDelete = _ =>
        {
            asks++;
            return true;
        };
        SelectRows(h.Form._playlists._list, IndexOf(h.Form._playlists, "Untitled"));

        h.Form._playlists._delete.PerformClick();

        Assert.Equal(1, asks);
        Assert.Equal(new[] { "Evening" }, h.Engine.View.Playlists);
        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath)!.FadeOutPoint!.Position);
    });

    // ── Playlists issue 03: the open playlist's name ────────────────────────────────────

    [Fact]
    public void AtLaunch_TheNameAboveTheQueue_AndTheTitle_NameTheOpenPlaylist() => RunSta(() =>
    {
        using var h = new Harness();

        Assert.Equal("Untitled", h.Form._playlistName._name.Text);
        Assert.Equal($"Untitled — faderio — {Path.GetFileName(h.TempDir)} · WASAPI shared at 44100 Hz", h.Form.Text);

        h.Form._playlists._new.PerformClick(); // an empty Untitled is already new: nothing changes
        ThreeQueued(h);
        h.Form._playlists._new.PerformClick();

        Assert.Equal("Untitled 2", h.Form._playlistName._name.Text);
        Assert.StartsWith("Untitled 2 — faderio — ", h.Form.Text);
    });

    [Fact]
    public void F2InTheQueue_TypeAndEnter_RenamesThePlaylist_InTheSidebarToo() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);

        PressKey(h.Form._queueList, Keys.F2);
        Assert.True(h.Form._playlistName.IsRenaming);
        Assert.Equal("Untitled", h.Form._playlistName._edit.Text);
        h.Form._playlistName._edit.Text = "Evening";
        PressKey(h.Form._playlistName._edit, Keys.Enter);

        Assert.False(h.Form._playlistName.IsRenaming);
        Assert.Equal("Evening", h.Engine.View.OpenPlaylist);
        Assert.Equal("Evening", h.Form._playlistName._name.Text);
        h.Form.Render();
        Assert.Equal(new[] { "Evening" }, Names(h.Form._playlists));
        Assert.StartsWith("Evening — faderio — ", h.Form.Text);
    });

    [Fact]
    public void ADoubleClickOnTheName_StartsTheRename() => RunSta(() =>
    {
        using var h = new Harness();

        typeof(Control).GetMethod("OnDoubleClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Form._playlistName._name, new object[] { EventArgs.Empty });

        Assert.True(h.Form._playlistName.IsRenaming);
        Assert.Equal("Untitled", h.Form._playlistName._edit.Text);
    });

    [Fact]
    public void Esc_PutsTheNameBack_AndRenamesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        PressKey(h.Form._queueList, Keys.F2);
        h.Form._playlistName._edit.Text = "Evening";

        PressKey(h.Form._playlistName._edit, Keys.Escape);

        Assert.False(h.Form._playlistName.IsRenaming);
        Assert.Equal("Untitled", h.Form._playlistName._name.Text);
        Assert.Equal(new[] { "Untitled" }, h.Engine.View.Playlists);
        Assert.Equal("Ready.", h.Form._status.Text);
    });

    [Fact]
    public void ARefusedRename_KeepsTheOldName_WithTheEnginesNotice() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        File.WriteAllText(Path.Combine(h.Library.PlaylistsFolder, "Evening.m3u8"), "#EXTM3U\n");
        PressKey(h.Form._queueList, Keys.F2);
        h.Form._playlistName._edit.Text = "evening";

        PressKey(h.Form._playlistName._edit, Keys.Enter);

        Assert.Equal("Untitled", h.Form._playlistName._name.Text);
        Assert.Equal("Not renamed: Untitled. There is already a playlist called Evening.", h.Form._status.Text);
    });

    [Fact]
    public void WhileTheNameIsEdited_ARenderLeavesWhatIsTyped() => RunSta(() =>
    {
        using var h = new Harness();
        PressKey(h.Form._queueList, Keys.F2);
        h.Form._playlistName._edit.Text = "Eve";

        h.Form.Render();

        Assert.True(h.Form._playlistName.IsRenaming);
        Assert.Equal("Eve", h.Form._playlistName._edit.Text);
    });

    [Fact]
    public void AddFolder_WhileUntitled2IsOpen_GrowsItsFile() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form._playlists._new.PerformClick();
        var album = Path.Combine(h.TempDir, "Album");
        Directory.CreateDirectory(album);
        File.Copy(TestAudio.WriteDcWav("one.wav", 1, 0.2), Path.Combine(album, "1 - One.wav"));
        File.Copy(TestAudio.WriteDcWav("two.wav", 1, 0.2), Path.Combine(album, "2 - Two.wav"));
        h.Form.ChooseFolders = () => new[] { album };

        Click(h.Form._addFolder);

        var file = Path.Combine(h.Library.PlaylistsFolder, "Untitled 2.m3u8");
        Assert.Equal(
            new[] { Path.Combine(album, "1 - One.wav"), Path.Combine(album, "2 - Two.wav") },
            File.ReadAllLines(file).Where(l => !l.StartsWith('#')));
    });

    // ── Overrides in the panel (playlists issue 04) ──────────────────────────────────────

    private static readonly TransitionSpec GlobalAlphaBeta = new()
    {
        FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1.5), FromEnd: false),
        FadeOutDuration = TimeSpan.FromSeconds(0.5),
        FadeInPoint = TimeSpan.Zero,
        FadeInDuration = TimeSpan.FromSeconds(0.5),
        Curve = FadeCurveKind.Linear,
    };

    /// <summary>Alpha, beta and gamma queued in Untitled, alpha → beta authored globally, and the
    /// editor open on it.</summary>
    private static Adjacency EditingAlphaBeta(Harness h)
    {
        ThreeQueued(h);
        var adjacency = h.Engine.AdjacenciesOf(h.Engine.View.Queue[0].Id).Next!;
        h.Engine.Author(adjacency, GlobalAlphaBeta);
        h.Form.EditTransitionAt(0);
        return adjacency;
    }

    [Fact]
    public void BlankDurations_ShowWhatTheyPlayInGrey_FiveSecondsOrTheGlobals() => RunSta(() =>
    {
        using var h = new Harness();
        var editor = h.Form._editor;
        Assert.Equal("", editor._fadeOutDuration.PlaceholderText); // nothing open

        ThreeQueued(h);
        h.Form.EditTransitionAt(0); // nothing authored

        Assert.Equal("", editor._fadeOutDuration.Text);
        Assert.Equal("5", editor._fadeOutDuration.PlaceholderText);
        Assert.Equal("5", editor._fadeInDuration.PlaceholderText);
        Assert.Equal("", editor._fadeOutPoint.PlaceholderText); // points have no default

    });

    [Fact]
    public void AnAuthoredDurationEmptied_ShowsFiveInGrey_AndThePlaylistLayerShowsTheGlobals() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaBeta(h);
        var editor = h.Form._editor;

        Assert.Equal("0.5", editor._fadeOutDuration.Text);
        Assert.Equal("5", editor._fadeOutDuration.PlaceholderText); // shown once the box is emptied

        editor._editPlaylist.Checked = true;

        Assert.Equal("", editor._fadeOutDuration.Text);
        Assert.Equal("0.5", editor._fadeOutDuration.PlaceholderText); // the global's
        Assert.Equal("0.5", editor._fadeInDuration.PlaceholderText);
    });

    [Fact]
    public void PlaylistMode_ShowsTheGlobalValuesGrey_AndOnlyWhatIsTypedIsSavedAsTheOverride() => RunSta(() =>
    {
        using var h = new Harness();
        var adjacency = EditingAlphaBeta(h);
        var editor = h.Form._editor;
        Assert.Equal("This playlist (Untitled)", editor._editPlaylist.Text);

        editor._editPlaylist.Checked = true;

        Assert.Equal(TransitionLayer.Playlist, editor.Layer);
        Assert.Equal("", editor._fadeOutPoint.Text);
        Assert.Equal("1.5", editor._fadeOutPoint.PlaceholderText);
        Assert.Equal("0.5", editor._fadeOutDuration.PlaceholderText);
        Assert.Equal("0", editor._fadeInPoint.PlaceholderText);
        Assert.Equal("0.5", editor._fadeInDuration.PlaceholderText);
        Assert.Equal("(global)", editor._curve.Text);
        Assert.True(IsSetVisible(editor._removeOverride));
        Assert.True(IsSetVisible(editor._remove)); // beside it, not replaced (D21)
        Assert.False(IsSetVisible(editor._effective));
        Assert.False(editor.HasUnsavedEdits);

        editor._fadeOutPoint.Text = "1";
        Assert.True(editor.HasUnsavedEdits);
        editor.Commit();

        var layers = h.Engine.Transition(adjacency);
        Assert.Equal(new TransitionSpec { FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false) }, layers.Override);
        Assert.Equal(GlobalAlphaBeta, layers.Global);
        Assert.False(editor.HasUnsavedEdits);
        Assert.Equal($"Saved {adjacency.Name} in Untitled.", h.Form._status.Text);
        Assert.True(IsSetVisible(editor._effective));
        Assert.Equal("Untitled plays: fade-out at 1 s for 0.5 s, fade-in from 0 s over 0.5 s, linear.", editor._effective.Text);
    });

    [Fact]
    public void PlaylistMode_ClearingABox_DropsItFromTheOverride_AndACurveCanBeOverridden() => RunSta(() =>
    {
        using var h = new Harness();
        var adjacency = EditingAlphaBeta(h);
        h.Engine.AuthorOverride(adjacency, new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            FadeInDuration = TimeSpan.FromSeconds(2),
        });
        var editor = h.Form._editor;
        editor._editPlaylist.Checked = true;
        Assert.Equal("1", editor._fadeOutPoint.Text);
        Assert.Equal("2", editor._fadeInDuration.Text);

        editor._fadeInDuration.Text = "";
        editor._curve.SelectedIndex = editor._curve.FindStringExact("Equal-power");
        editor.Commit();

        Assert.Equal(new TransitionSpec
        {
            FadeOutPoint = new FadePointSpec(TimeSpan.FromSeconds(1), FromEnd: false),
            Curve = FadeCurveKind.EqualPower,
        }, h.Engine.Transition(adjacency).Override);
    });

    [Fact]
    public void RemoveOverride_PlaysTheGlobalAgain_AndUndoPutsItBack() => RunSta(() =>
    {
        using var h = new Harness();
        var adjacency = EditingAlphaBeta(h);
        h.Engine.AuthorOverride(adjacency, new TransitionSpec { FadeOutDuration = TimeSpan.FromSeconds(2) });
        var editor = h.Form._editor;
        editor._editPlaylist.Checked = true;
        Assert.True(IsSetVisible(editor._effective));

        Click(editor._removeOverride);

        Assert.Null(h.Engine.Transition(adjacency).Override);
        Assert.Equal(GlobalAlphaBeta, h.Engine.Transition(adjacency).Global);
        Assert.Equal("", editor._fadeOutDuration.Text); // the global's, as a placeholder
        Assert.False(editor.HasUnsavedEdits);
        Assert.False(IsSetVisible(editor._effective));

        editor.UndoChange();

        Assert.Equal(TimeSpan.FromSeconds(2), h.Engine.Transition(adjacency).Override!.FadeOutDuration);
        Assert.Equal("2", editor._fadeOutDuration.Text);
        Assert.Equal(TransitionLayer.Playlist, editor.Layer);
    });

    [Fact]
    public void GlobalMode_IsUnchanged_WithNoPlaceholders_NoGlobalCurve_AndNoRemoveOverride() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaBeta(h);
        var editor = h.Form._editor;

        Assert.True(editor._editGlobal.Checked);
        Assert.Equal("1.5", editor._fadeOutPoint.Text);
        Assert.Equal("", editor._fadeOutPoint.PlaceholderText);
        Assert.Equal(-1, editor._curve.FindStringExact("(global)"));
        Assert.Equal("Linear", editor._curve.Text);
        Assert.False(IsSetVisible(editor._removeOverride));
    });

    [Fact]
    public void TheMode_IsRemembered_WhenTheEditorOpensAnotherAdjacency() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaBeta(h);
        var editor = h.Form._editor;
        editor._editPlaylist.Checked = true;

        h.Form.EditTransitionAt(1);

        Assert.Equal(h.Engine.AdjacenciesOf(h.Engine.View.Queue[1].Id).Next!.Name, editor._title.Text);
        Assert.True(editor._editPlaylist.Checked);
        Assert.Equal(TransitionLayer.Playlist, editor.Layer);
        Assert.Equal("(global)", editor._curve.Text);
    });

    [Fact]
    public void SwitchingModes_SavesABoxBeingTypedIn_InTheModeLeft_ThenLoadsTheOther() => RunSta(() =>
    {
        using var h = new Harness();
        var adjacency = EditingAlphaBeta(h);
        var editor = h.Form._editor;
        editor._fadeOutPoint.Text = "2";

        editor._editPlaylist.Checked = true;

        Assert.Equal(TimeSpan.FromSeconds(2), h.Engine.Transition(adjacency).Global!.FadeOutPoint!.Position);
        Assert.Null(h.Engine.Transition(adjacency).Override);
        Assert.Equal(TransitionLayer.Playlist, editor.Layer);
        Assert.Equal("", editor._fadeOutPoint.Text);
        Assert.Equal("2", editor._fadeOutPoint.PlaceholderText);
    });

    [Fact]
    public void PlaylistMode_ANudgeOnAnEmptyBox_StartsFromTheGlobalPoint_AndSavesTheOverride() => RunSta(() =>
    {
        using var h = new Harness();
        var adjacency = EditingAlphaBeta(h);
        var editor = h.Form._editor;
        editor._editPlaylist.Checked = true;
        Assert.True(editor._fadeOutMore.Enabled);

        editor.NudgePoint(fadeOut: true, +1, coarse: false);

        Assert.Equal("1.6", editor._fadeOutPoint.Text);
        Assert.Equal(TimeSpan.FromSeconds(1.6), h.Engine.Transition(adjacency).Override!.FadeOutPoint!.Position);
        Assert.Equal(GlobalAlphaBeta, h.Engine.Transition(adjacency).Global);
        Click(editor._preview);
        Assert.Equal(adjacency, h.Engine.View.Preview?.Adjacency);
    });
}
