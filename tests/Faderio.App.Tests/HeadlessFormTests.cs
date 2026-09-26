using System.Reflection;
using System.Runtime.ExceptionServices;
using Faderio.App;
using Faderio.Core.Domain;
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
public sealed class HeadlessFormTests
{
    private sealed class Harness : IDisposable
    {
        public string TempDir { get; } = Path.Combine(Path.GetTempPath(), "faderio-app", Guid.NewGuid().ToString("N"));
        public FakeAudioOutput Output { get; }
        public JsonTransitionStore Store { get; }
        public PlaybackEngine Engine { get; }
        public MainForm Form { get; }
        public IEngineSink Sink { get; }
        public string SettingsPath { get; }

        /// <param name="inUse">The mode the output opened in, as the NAudio adapter reports it.</param>
        public Harness(int latency = 0, AudioOutputMode? inUse = AudioOutputMode.WasapiShared)
        {
            Directory.CreateDirectory(TempDir);
            Output = new FakeAudioOutput { Latency = latency };
            Store = new JsonTransitionStore(Path.Combine(TempDir, "transitions.json"));
            var sink = new PostingSink(null); // inline: no message pump in a headless test
            Sink = sink;
            // The add worker runs inline, so an add completes on this STA thread and the inline
            // sink never touches a control from another thread.
            SettingsPath = Path.Combine(TempDir, "settings.json");
            Engine = new PlaybackEngine(Output, Store, sink, new SettingsStore(SettingsPath)) { StartAddWorker = work => work() };
            Form = new MainForm(Engine, Store, sink, () => inUse);
        }

        public void Dispose()
        {
            Form.Dispose();
            Engine.Dispose();
            try { Directory.Delete(TempDir, recursive: true); } catch (IOException) { }
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
    public void OpenPlaylist_PassesThePickedPlaylistToAdd_AndAppendsItsTracks() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a });
        var playlist = Path.Combine(h.TempDir, "mix † ⭐️.m3u8");
        File.WriteAllLines(playlist, new[] { "#EXTM3U", b, a });
        h.Form.ChoosePlaylists = () => new[] { playlist };

        Click(h.Form._openPlaylist);

        Assert.Equal(new[] { a, b, a }, h.Engine.View.Queue.Select(r => r.Track.FilePath));
        Assert.Equal(3, h.Form._queueList.Items.Count);
    });

    [Fact]
    public void SaveAsPlaylist_WritesTheQueueToThePickedPath_AndSaysSo() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { b, a });
        var playlist = Path.Combine(h.TempDir, "saved.m3u8");
        h.Form.ChoosePlaylistSavePath = () => playlist;

        Click(h.Form._saveAsPlaylist);

        Assert.Equal(new[] { b, a }, File.ReadAllLines(playlist).Where(l => !l.StartsWith('#')));
        Assert.Equal("Saved 2 tracks to saved.m3u8.", h.Form._status.Text);
    });

    [Fact]
    public void SaveAsPlaylist_Cancelled_WritesNothing() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.AddPaths(new[] { TestAudio.WriteDcWav("alpha.wav", 1, 0.4) });
        h.Form.ChoosePlaylistSavePath = () => null;

        Click(h.Form._saveAsPlaylist);

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
        Assert.Equal(SystemColors.Info, h.Form._queueList.Items[1].BackColor);
        Assert.Equal(SystemColors.Window, h.Form._queueList.Items[0].BackColor);
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
        Assert.Equal(SystemColors.GrayText, marked.ForeColor);
        Assert.Contains("Unreadable", marked.ToolTipText);
        Assert.Equal("1", h.Form._queueList.Items[0].Text);
        Assert.Equal(SystemColors.WindowText, h.Form._queueList.Items[0].ForeColor);

        File.Move(b + ".away", b);
        h.Form.PlayRowAt(1); // a double-click tries it
        h.Form.Render();

        Assert.Equal("2", marked.Text);
        Assert.Equal(SystemColors.WindowText, marked.ForeColor);
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

        Click(h.Form._editor._save);

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

        Click(h.Form._editor._save);

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
        Click(h.Form._editor._save);

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
        Click(editor._save);

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

        Click(editor._save);
        h.Form.EditTransitionAt(0);

        Assert.Equal(FadeCurveKind.Linear, h.Store.Get(a, b)!.Curve);
        Assert.Equal("Linear", editor._curve.Text);
    });

    [Fact]
    public void Revert_DiscardsTheDraft_AndReloadsFromTheStore() => RunSta(() =>
    {
        using var h = new Harness();
        EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "1.5", "0.5", "0", "0.5");
        Click(editor._save);
        Fill(editor, "2", "", "", "");
        editor._fadeOutFromEnd.Checked = true;

        Click(editor._revert);

        Assert.Equal("1.5", editor._fadeOutPoint.Text);
        Assert.Equal("0.5", editor._fadeOutDuration.Text);
        Assert.False(editor._fadeOutFromEnd.Checked);
    });

    [Fact]
    public void RemoveTransition_Unauthors_KeepsTheFields_AndSavePutsItBack() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "1.5", "0.5", "0", "0.5");
        Click(editor._save);
        var saved = h.Store.Get(a, b);

        Click(editor._remove);

        Assert.Null(h.Store.Get(a, b));
        Assert.StartsWith("Removed alpha", h.Form._status.Text);
        Assert.Equal("1.5", editor._fadeOutPoint.Text);
        Assert.Equal("0.5", editor._fadeInDuration.Text);

        Click(editor._save);

        Assert.Equal(saved, h.Store.Get(a, b));
    });

    [Fact]
    public void ABlankSave_LeavesNoEntryInTheStore() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "1.5", "0.5", "0", "0.5");
        Click(editor._save);

        Fill(editor, "", "", "", "");
        Click(editor._save);

        Assert.Null(h.Store.Get(a, b));
        Assert.DoesNotContain("alpha", File.ReadAllText(Path.Combine(h.TempDir, "transitions.json")));
    });

    [Fact]
    public void AnImpossibleValue_IsRefused_WithItsReasonBesideItsField() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b) = EditingAlphaToBeta(h);
        var editor = h.Form._editor;
        Fill(editor, "9", "0.5", "7", "0.5");

        Click(editor._save);

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
        Click(editor._save);

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

        Click(editor._save);

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

        Click(editor._save);

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
        Assert.Equal(SystemColors.Info, h.Form._queueList.Items[0].BackColor);
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
        Assert.Equal(new[] { "1", "2", "3", "4" }, list.Items.Cast<ListViewItem>().Select(i => i.Text));
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
        var transport = h.Form._playPause.Parent!;
        // Ordinal: UI text comparison folds ⏹ and ⏸ together.
        Assert.DoesNotContain(transport.Controls.Cast<Control>(), c => string.Equals(c.Text, "⏹", StringComparison.Ordinal));
        Assert.Equal(
            new[] { "⏮", "▶", "⏭", "⤳", "🔀", "🔁" },
            transport.Controls.OfType<ButtonBase>().Select(c => c.Text).ToArray());
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
    public void TheMarker_RestsDimmedWhenStopped_AndIsFullWhilePlayingOrPaused() => RunSta(() =>
    {
        using var h = new Harness();
        var a = TestAudio.WriteDcWav("alpha.wav", 1, 0.4);
        var b = TestAudio.WriteDcWav("beta.wav", 1, 0.2);
        h.Form.AddPaths(new[] { a, b });
        h.Form.Render();
        Color[] Backs() => h.Form._queueList.Items.Cast<ListViewItem>().Select(i => i.BackColor).ToArray();

        Assert.NotEqual(SystemColors.Window, MainForm.RestingMarker);
        Assert.NotEqual(SystemColors.Info, MainForm.RestingMarker);
        Assert.Equal(new[] { MainForm.RestingMarker, SystemColors.Window }, Backs()); // at launch

        h.Form.PlayRowAt(1);
        h.Form.Render();
        Assert.Equal(new[] { SystemColors.Window, SystemColors.Info }, Backs());

        Click(h.Form._playPause); // pause
        h.Form.Render();
        Assert.Equal(new[] { SystemColors.Window, SystemColors.Info }, Backs());

        Click(h.Form._playPause);
        h.Output.Advance(44100 * 2); // the queue runs out
        h.Form.Render();
        Assert.Equal(Transport.Stopped, h.Engine.View.Transport);
        Assert.Equal(new[] { MainForm.RestingMarker, SystemColors.Window }, Backs());
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
        Assert.Equal("0:04 / -0:04", h.Form._time.Text);
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
    public void TheSettingsDialog_OpensOnTheViewsSettings_AndOKSendsTheChoiceInOneCall() => RunSta(() =>
    {
        using var h = new Harness();
        h.Engine.ChangeSettings(new PlayerSettings
        {
            OutputMode = AudioOutputMode.WasapiExclusive,
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
            Assert.True(dialog._replayGain.Checked);

            dialog._outputMode.SelectedIndex = dialog._outputMode.FindStringExact("WaveOut");
            dialog._seconds.Value = 12;
            dialog._replayGain.Checked = false;
            return true;
        };

        Click(h.Form._settings);

        Assert.True(opened);
        var expected = new PlayerSettings
        {
            OutputMode = AudioOutputMode.WaveOut,
            UnauthoredAdjacency = UnauthoredAdjacency.DefaultCrossfade,
            DefaultCrossfadeSeconds = 12,
            ReplayGainEnabled = false,
        };
        Assert.Equal(expected, h.Engine.View.Settings);
        Assert.Equal(expected, new SettingsStore(h.SettingsPath).Settings); // saved for the next launch
    });

    [Fact]
    public void TheReplayGainCheckbox_IsOnByDefault_AndReachesChangeSettings() => RunSta(() =>
    {
        using var h = new Harness();
        h.Form.ShowSettings = dialog =>
        {
            Assert.True(dialog._replayGain.Checked);
            dialog._replayGain.Checked = false;
            return true;
        };

        Click(h.Form._settings);

        Assert.Equal(new PlayerSettings { ReplayGainEnabled = false }, h.Engine.View.Settings);
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
        h.Engine.ChangeSettings(new PlayerSettings { OutputMode = AudioOutputMode.WasapiExclusive });
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
        Assert.False(h.Form._editNext.Enabled);

        h.Engine.SetRepeat(RepeatMode.All);
        OpenMenuOn(h.Form._queueList, 2);
        Assert.True(h.Form._editNext.Enabled);
        Click(h.Form._editNext);

        Assert.Equal((c, a), Opened(h));
    });

    [Fact]
    public void ThisToNext_OnAMiddleRow_OpensTheAdjacencyOutOfIt() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 1);
        Click(h.Form._editNext);

        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void PreviousToThis_OpensTheAdjacencyIntoTheRow_AndIsDisabledOnTheFirstRow() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 0);
        Assert.False(h.Form._editPrevious.Enabled);

        OpenMenuOn(h.Form._queueList, 1);
        Assert.True(h.Form._editPrevious.Enabled);
        Click(h.Form._editPrevious);

        Assert.Equal((a, b), Opened(h));
    });

    [Fact]
    public void ThisToThis_IsShownOnlyUnderRepeatOne_AndOpensTheRowIntoItself() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, _) = ThreeQueued(h);

        OpenMenuOn(h.Form._queueList, 1);
        Assert.False(h.Form._editSelf.Available);

        h.Engine.SetRepeat(RepeatMode.One);
        OpenMenuOn(h.Form._queueList, 1);
        Assert.True(h.Form._editSelf.Available);
        Assert.True(h.Form._editSelf.Enabled);
        Click(h.Form._editSelf);

        Assert.Equal((b, b), Opened(h));
    });

    [Fact]
    public void TheMenuItems_SayWhichAdjacency_AndAreDisabledWithoutASingleRow() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);

        OpenMenu(h.Form._queueList); // nothing selected
        Assert.False(h.Form._editNext.Enabled);
        Assert.False(h.Form._editPrevious.Enabled);
        Assert.Contains("this → next", h.Form._editNext.Text);
        Assert.Contains("previous → this", h.Form._editPrevious.Text);
        Assert.Contains("this → this", h.Form._editSelf.Text);
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
    public void Switching_WithNoUnsavedEdits_AsksNothing() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = ThreeQueued(h);
        h.Form.AskUnsavedEdits = _ => throw new InvalidOperationException("a clean editor must not ask");
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        Click(h.Form._editor._save); // saved: clean again

        h.Form.EditTransitionAt(1);

        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void Switching_WithUnsavedEdits_Asks_AndCancelKeepsTheEdits() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        var asked = new List<(string, string)>();
        h.Form.AskUnsavedEdits = adjacency =>
        {
            asked.Add((adjacency.Outgoing.FilePath, adjacency.Incoming.FilePath));
            return UnsavedEdits.Cancel;
        };

        h.Form.EditTransitionAt(1);

        Assert.Equal(new[] { (a, b) }, asked);
        Assert.Equal((a, b), Opened(h));
        Assert.Equal("2", h.Form._editor._fadeOutPoint.Text);
    });

    [Fact]
    public void Switching_WithUnsavedEdits_DiscardDropsThem() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Discard;

        h.Form.EditTransitionAt(1);

        Assert.Equal((b, c), Opened(h));
        Assert.Equal("", h.Form._editor._fadeOutPoint.Text);
        Assert.Null(h.Store.Get(a, b));
    });

    [Fact]
    public void Switching_WithUnsavedEdits_SaveSavesThem_ThenOpensTheOther() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, c) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Save;

        h.Form.EditTransitionAt(1);

        Assert.Equal(TimeSpan.FromSeconds(2), h.Store.Get(a, b)!.FadeOutPoint!.Position);
        Assert.Equal((b, c), Opened(h));
    });

    [Fact]
    public void Switching_WhenTheSaveIsRefused_StaysWithTheEdits() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "9"; // past alpha's end
        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Save;

        h.Form.EditTransitionAt(1);

        Assert.Null(h.Store.Get(a, b));
        Assert.Equal((a, b), Opened(h));
        Assert.True(IsSetVisible(h.Form._editor.ReasonFor(TransitionField.FadeOutPoint)));
    });

    [Fact]
    public void TheFromEndBox_AndTheCurve_AreUnsavedEditsToo() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        Click(h.Form._editor._save);
        Assert.False(h.Form._editor.HasUnsavedEdits);

        h.Form._editor._curve.SelectedIndex = h.Form._editor._curve.FindStringExact("Linear");
        Assert.True(h.Form._editor.HasUnsavedEdits);
        Click(h.Form._editor._revert);
        Assert.False(h.Form._editor.HasUnsavedEdits);

        h.Form._editor._fadeOutFromEnd.Checked = true;
        Assert.True(h.Form._editor.HasUnsavedEdits);
    });

    [Fact]
    public void AfterRemoveTransition_TheFilledFieldsAreUnsaved_AndLeavingAsks() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        Click(h.Form._editor._save);

        Click(h.Form._editor._remove);

        Assert.True(h.Form._editor.HasUnsavedEdits);
        var asked = 0;
        h.Form.AskUnsavedEdits = _ => { asked++; return UnsavedEdits.Cancel; };
        h.Form.EditTransitionAt(1);
        Assert.Equal(1, asked);
        Assert.Equal((a, b), Opened(h));
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
    public void ReopeningTheSameAdjacency_KeepsTheDraft_WithoutAsking() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        h.Form.AskUnsavedEdits = _ => throw new InvalidOperationException("the same adjacency must not ask");

        h.Form.EditTransitionAt(0);

        Assert.Equal("2", h.Form._editor._fadeOutPoint.Text);
    });

    [Fact]
    public void ClosingTheWindow_WithUnsavedEdits_Asks_AndCancelKeepsItOpen() => RunSta(() =>
    {
        using var h = new Harness();
        ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        h.Form._editor._fadeOutPoint.Text = "2";
        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Cancel;

        var closing = new FormClosingEventArgs(CloseReason.UserClosing, cancel: false);
        typeof(Form).GetMethod("OnFormClosing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(h.Form, new object[] { closing });

        Assert.True(closing.Cancel);
        Assert.Equal("2", h.Form._editor._fadeOutPoint.Text);
        h.Engine.Play(); // the engine was not disposed: it would throw
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
        Click(h.Form._editor._save);
        Assert.Equal(stored, h.Store.Get(a, b));
    });

    [Fact]
    public void ASaveTheStoreCouldNotWrite_LeavesTheEditsUnsaved() => RunSta(() =>
    {
        using var h = new Harness();
        var (a, b, _) = ThreeQueued(h);
        h.Form.EditTransitionAt(0);
        Fill(h.Form._editor, "1.5", "0.5", "0", "0.5");
        Click(h.Form._editor._save);
        var path = Path.Combine(h.TempDir, "transitions.json");
        File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            h.Form._editor._fadeOutPoint.Text = "2";
            Click(h.Form._editor._save);

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
    public void OpeningAnotherAdjacency_EndsThePreview_OnceTheUnsavedEditsAreSettled() => RunSta(() =>
    {
        using var h = new Harness();
        var (_, b, c) = EditingADraft(h);
        Click(h.Form._editor._preview);
        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Cancel;

        h.Form.EditTransitionAt(1);
        Assert.NotNull(h.Engine.View.Preview); // cancelled: still on alpha → beta, still previewing

        h.Form.AskUnsavedEdits = _ => UnsavedEdits.Discard;
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
        Assert.Equal(SystemColors.GrayText, h.Form._clip.ForeColor);
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
        Assert.Equal(SystemColors.GrayText, h.Form._clip.ForeColor);
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
}
