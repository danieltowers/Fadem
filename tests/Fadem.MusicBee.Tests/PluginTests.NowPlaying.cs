using System.Drawing;
using System.Windows.Forms;
using Fadem.Core.Tests;
using Fadem.Shell;
using static MusicBeePlugin.Plugin;

namespace Fadem.MusicBee.Tests;

/// <summary>
/// Fadem in MusicBee's Now Playing tab (phase 6 issue 08), opted into in Fadem's Configure.
/// MusicBee's main form stands in as <see cref="FakeMainForm"/>, which swaps its children on a
/// tab switch as MusicBee's does: the library layout's several out, a Now Playing container of a
/// type nested in the form's in, built afresh on each visit and disposed on leaving. The form is
/// shown off-screen without activation, since visibility is part of the rule.
/// </summary>
public sealed partial class PluginTests
{
    [Fact]
    public void NowPlaying_WithTheSettingOn_ShowsAViewFillingTheTab_OverMusicBeesOwn_AndFollowsItsSize() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);

        var tab = main.ShowNowPlaying();
        Application.DoEvents();

        var view = NowPlayingView(tab);
        Assert.Equal(DockStyle.Fill, view.Dock);
        Assert.Contains(view, _plugin.Host!.Views);
        Assert.Equal(2, view._queueList.Items.Count);
        Assert.All(tab.Own, own => Assert.False(own.Visible)); // covered and hidden, never removed
        Assert.All(tab.Own, own => Assert.Same(tab, own.Parent));

        main.Width += 300; // MusicBee's layout resizes its view, and the view with it
        Assert.Equal(tab.ClientSize, view.Size);
    });

    [Fact]
    public void WhatMusicBeeShowsOrAddsLater_StaysHiddenUnderTheView() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        var tab = main.ShowNowPlaying();
        var view = NowPlayingView(tab);

        tab.Own[0].Visible = true; // the biography's scroll strip, shown once its text has loaded
        var late = new Panel { Dock = DockStyle.Left, Width = 17 };
        tab.Controls.Add(late); // or one built after the view
        Application.DoEvents();

        Assert.False(tab.Own[0].Visible);
        Assert.False(late.Visible);
        Assert.True(view.Visible);
        Assert.Equal(0, tab.Controls.GetChildIndex(view)); // on top
    });

    [Fact]
    public void ThePluginClosingOnTheTab_GivesMusicBeeItsViewBack() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        var tab = main.ShowNowPlaying();
        var view = NowPlayingView(tab);
        tab.Own[0].Visible = true; // MusicBee shows its strip while covered: wanted shown

        _plugin.Close(PluginCloseReason.UserDisabled);

        Assert.True(view.IsDisposed);
        Assert.Empty(tab.Controls.OfType<PluginView>());
        Assert.All(tab.Own, own => Assert.True(own.Visible));
    });

    [Fact]
    public void LibraryTabs_AndTheSidebarPanel_AreUntouched() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        using var sidebar = new Panel();
        _plugin.OnDockablePanelCreated(sidebar);
        var library = main.Controls.Cast<Control>().ToList();

        main.ShowNowPlaying();
        Application.DoEvents();
        main.ShowLibrary();
        Application.DoEvents();

        Assert.Equal(library, main.Controls.Cast<Control>());
        Assert.All(library, part => Assert.Empty(part.Controls));
        Assert.Same(View(sidebar), Assert.Single(_plugin.Host!.Views));
    });

    [Fact]
    public void RepeatedVisits_EachShowAFreshView_AndLeaveNoViewNorDisposedControlBehind() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        var shown = new List<PluginView>();

        for (var visit = 0; visit < 3; visit++)
        {
            shown.Add(NowPlayingView(main.ShowNowPlaying()));
            Assert.Single(_plugin.Host!.Views);
            main.ShowLibrary();
            Application.DoEvents();
            Assert.Empty(_plugin.Host.Views);
        }

        Assert.Equal(3, shown.Distinct().Count());
        Assert.All(shown, view => Assert.True(view.IsDisposed));
        Assert.All(main.Controls.Cast<Control>(), part => Assert.False(part.IsDisposed));
    });

    [Fact]
    public void WithTheSettingOff_NowPlayingIsMusicBeesOwn() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: false);

        var tab = main.ShowNowPlaying();
        Application.DoEvents();

        Assert.Equal(tab.Own, tab.Controls.Cast<Control>());
        Assert.All(tab.Own, own => Assert.True(own.Visible));
        Assert.Empty(_plugin.Host!.Views);
        Assert.Null(_plugin.Host.Notices.Latest);
    });

    [Fact]
    public void TurningItOnOrOff_TakesEffectAtTheNextVisit_WithNoRestart_AndIsReadBackAtTheNextStart() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: false);
        var opened = false;
        Configure(read: dialog => opened = dialog.NowPlayingTab, nowPlayingTab: true);

        main.ShowNowPlaying();
        Application.DoEvents();
        Assert.False(opened);
        Assert.Empty(_plugin.Host!.Views); // OK waits for MusicBee's Save, as its own preferences do
        main.ShowLibrary();

        _plugin.SaveSettings();
        NowPlayingView(main.ShowNowPlaying());
        main.ShowLibrary();
        Application.DoEvents();

        Configure(read: dialog => opened = dialog.NowPlayingTab, nowPlayingTab: false);
        _plugin.SaveSettings();
        var tab = main.ShowNowPlaying();
        Application.DoEvents();
        Assert.True(opened);
        Assert.Equal(tab.Own, tab.Controls.Cast<Control>());

        Configure(nowPlayingTab: true);
        _plugin.SaveSettings();
        _plugin.Close(PluginCloseReason.MusicBeeClosing);
        var next = new MusicBeePlugin.Plugin((_, _, _) => new FakeAudioOutput(), () => _devices);
        try
        {
            Start(next);
            next.ShowSettings = dialog =>
            {
                opened = dialog.NowPlayingTab;
                return false;
            };
            next.Configure(IntPtr.Zero);
            Assert.True(opened);
        }
        finally
        {
            next.Close(PluginCloseReason.MusicBeeClosing);
        }
    });

    [Fact]
    public void TheTransitionOpenInTheEditor_ReopensAtTheNextVisit() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        var first = NowPlayingView(main.ShowNowPlaying());
        first.EditTransitionAt(0);
        var open = first._editor.Adjacency;
        Assert.NotNull(open);

        main.ShowLibrary();
        Application.DoEvents();
        var second = NowPlayingView(main.ShowNowPlaying());

        Assert.Equal(open, second._editor.Adjacency);
    });

    [Fact]
    public void StartingOnTheNowPlayingTab_ItIsTakenOver() => RunSta(() =>
    {
        using var main = new FakeMainForm();
        main.Show();
        var tab = main.ShowNowPlaying();

        StartWithMainForm(main, nowPlayingTab: true);

        Assert.Single(_plugin.Host!.Views);
        NowPlayingView(tab);
    });

    [Fact]
    public void AHiddenLeftover_DoesNotStopTheRule_NorDoesAHiddenNowPlayingCountAsShown() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        var leftover = new Panel { Visible = false };
        main.Controls.Add(leftover);

        var tab = main.ShowNowPlaying();
        Application.DoEvents();
        NowPlayingView(tab);

        main.ShowLibrary();
        main.Controls.Add(new FakeMainForm.NowPlaying { Visible = false }); // built, not shown
        Application.DoEvents();
        Assert.Empty(_plugin.Host!.Views);
    });

    [Fact]
    public void AViewItDoesNotRecognise_IsLeftAlone_AndSaidOnce_InTheStripNotABox() => RunSta(() =>
    {
        using var main = StartWithMainForm(nowPlayingTab: true);
        using var sidebar = new Panel();
        _plugin.OnDockablePanelCreated(sidebar);

        for (var visit = 0; visit < 2; visit++)
        {
            var stranger = new Panel { Dock = DockStyle.Fill, Controls = { new Label() } }; // not a type of the form's
            main.SwapTo(stranger);
            Application.DoEvents();
            Assert.True(stranger.Controls[0].Visible);
            Assert.Empty(stranger.Controls.OfType<PluginView>());
            main.ShowLibrary();
            Application.DoEvents();
        }

        var said = Assert.Single(_plugin.Host!.Notices.Entries);
        Assert.Contains("Now Playing", said.Message);
        Assert.Equal(Presenter.FmtNotice(said), View(sidebar)._status.Text);
    });

    [Fact]
    public void WithNoMainWindowToFind_NowPlayingIsLeftAlone_AndSaidOnce_NotInABox()
    {
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        ChooseNowPlayingTab(true);
        Start();
        Launched();

        _plugin.ReceiveNotification("", NotificationType.PluginStartup);
        _ui.Pump();

        Assert.Contains("Now Playing", Assert.Single(_plugin.Host!.Notices.Entries).Message);
        Assert.Empty(_ui.Messages);
    }

    // ── The stand-ins ───────────────────────────────────────────────────────────────────

    /// <summary>Starts the plugin on a library of two rows, with a new stand-in as MusicBee's main
    /// window, and sends it MusicBee's start-up notification.</summary>
    private FakeMainForm StartWithMainForm(bool nowPlayingTab) => StartWithMainForm(new FakeMainForm(), nowPlayingTab);

    /// <summary>As above, with <paramref name="main"/> as MusicBee's main window.</summary>
    private FakeMainForm StartWithMainForm(FakeMainForm main, bool nowPlayingTab)
    {
        main.Show();
        _musicBee.MainWindow = main.Handle;
        AuthorLibrary();
        ChooseLibrary(LibraryFolder);
        ChooseNowPlayingTab(nowPlayingTab);
        Start();
        Launched();
        _plugin.ReceiveNotification("", NotificationType.PluginStartup);
        Application.DoEvents(); // posted through the main window, as MusicBee's UI thread runs it
        return main;
    }

    /// <summary>The plugin's own settings file, as its dialog leaves it.</summary>
    private void ChooseNowPlayingTab(bool on)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(SettingsFile)!, "plugin.json"), $"{{ \"nowPlayingTab\": {(on ? "true" : "false")} }}");
    }

    /// <summary>The Fadem view in the Now Playing container, once the change has settled.</summary>
    private static PluginView NowPlayingView(FakeMainForm.NowPlaying tab)
    {
        Application.DoEvents();
        return Assert.Single(tab.Controls.OfType<PluginView>());
    }
}

/// <summary>MusicBee's main form as the plugin finds it: a few children, swapped on a tab switch.
/// Shown off-screen, never activated, so it takes no keystrokes from the user's desktop.</summary>
internal sealed class FakeMainForm : Form
{
    public FakeMainForm()
    {
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-32000, -32000);
        Size = new Size(1200, 800);
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        ShowLibrary();
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>The Now Playing view MusicBee builds on each visit: a type nested in the form's,
    /// with its own children (the biography strip among them).</summary>
    internal sealed class NowPlaying : Panel
    {
        public NowPlaying()
        {
            Dock = DockStyle.Fill;
            Own = new Control[] { new Panel { Dock = DockStyle.Top, Height = 40 }, new Panel { Dock = DockStyle.Fill } };
            Controls.AddRange(Own);
        }

        public Control[] Own { get; }
    }

    private readonly Control[] _library =
    {
        new Panel { Dock = DockStyle.Left, Width = 200 },
        new Panel { Dock = DockStyle.Fill },
        new Panel { Dock = DockStyle.Right, Width = 280 },
    };

    /// <summary>The Now Playing tab: the library layout's children out, a new view in.</summary>
    public NowPlaying ShowNowPlaying()
    {
        var tab = new NowPlaying();
        SwapTo(tab);
        return tab;
    }

    /// <summary>A library tab: whatever single view is shown leaves and is disposed, as MusicBee
    /// disposes its Now Playing view, and the library layout's children come back.</summary>
    public void ShowLibrary()
    {
        foreach (var single in Controls.Cast<Control>().Where(c => !_library.Contains(c)).ToList())
        {
            Controls.Remove(single);
            single.Dispose();
        }
        foreach (var part in _library.Where(p => p.Parent != this))
        {
            Controls.Add(part);
        }
    }

    /// <summary>The library layout out, <paramref name="single"/> in.</summary>
    public void SwapTo(Control single)
    {
        foreach (var part in _library)
        {
            Controls.Remove(part);
        }
        Controls.Add(single);
    }
}
