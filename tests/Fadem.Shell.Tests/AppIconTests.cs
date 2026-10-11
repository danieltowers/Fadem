using Fadem.Core.Persistence;
using Fadem.Core.Tests;
using Fadem.Shell;

namespace Fadem.Shell.Tests;

/// <summary>The Fadem icon (2026-10-11): embedded in the shell, worn by every Fadem window on both
/// hosts' runtimes, in each size Windows draws it at.</summary>
public sealed class AppIconTests
{
    /// <summary>The title bar's, the taskbar's and Alt+Tab's sizes at 100 % to 200 % scaling, each
    /// a bitmap System.Drawing reads; Explorer's 256 is PNG, which System.Drawing passes over.</summary>
    [Theory]
    [InlineData(16)]
    [InlineData(20)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    [InlineData(64)]
    public void TheIcon_HasEachSizeWindowsDrawsItAt(int size)
    {
        using var icon = new Icon(AppIcon.Shared, size, size);

        Assert.Equal(new Size(size, size), icon.Size);
    }

    [Fact]
    public void TheIcon_IsOneInstance_SharedByEveryWindow()
    {
        Assert.Same(AppIcon.Shared, AppIcon.Shared);
    }

    [Fact]
    public void TheLibraryChooser_WearsTheFademIcon() => DialogThemeTests.RunSta(() =>
    {
        using var chooser = new LibraryChooser(() => Array.Empty<KnownLibrary>(), _ => { }, _ => null);

        Assert.Same(AppIcon.Shared, chooser.Icon);
    });

    [Fact]
    public void TheNoticeList_WearsTheFademIcon() => DialogThemeTests.RunSta(() =>
    {
        using var list = new NoticeListForm();

        Assert.Same(AppIcon.Shared, list.Icon);
    });

    [Fact]
    public void TheTransitionManager_WearsTheFademIcon() => DialogThemeTests.RunSta(() =>
    {
        using var tc = TestEngine.Create();
        using var manager = new TransitionManagerForm(tc.Engine, _ => { });

        Assert.Same(AppIcon.Shared, manager.Icon);
    });
}
