using System.Runtime.InteropServices;
using System.Windows.Forms;
using MusicBeePlugin;
using static MusicBeePlugin.Plugin;

namespace Faderio.MusicBee.Tests;

/// <summary>
/// MusicBee as the plugin sees it: the API struct, filled with recording fakes and marshalled to
/// the pointer <see cref="Plugin.Initialise"/> takes. Every call the plugin makes to MusicBee is
/// in <see cref="Calls"/>, in order.
/// </summary>
internal sealed class FakeMusicBee
{
    private Plugin.MusicBeeApiInterface _api;

    public FakeMusicBee(string storagePath)
    {
        StoragePath = storagePath;
        // Held in fields: the struct marshals each to a function pointer that must outlive the call.
        _api.InterfaceVersion = Plugin.MinInterfaceVersion;
        _api.ApiRevision = Plugin.MinApiRevision;
        _api.Setting_GetPersistentStoragePath = () => StoragePath;
        _api.MB_GetWindowHandle = () => IntPtr.Zero;
        _api.MB_AddMenuItem = AddMenuItem;
        _api.Player_Stop = () =>
        {
            Calls.Add("Player_Stop");
            PlayerStopped?.Invoke();
            return true;
        };
    }

    public string StoragePath { get; }

    public List<string> Calls { get; } = new();

    /// <summary>Called as MusicBee's player is stopped: the test records what it needs then.</summary>
    public Action? PlayerStopped { get; set; }

    /// <summary>The menu items the plugin added, by path.</summary>
    public Dictionary<string, ToolStripMenuItem> Menus { get; } = new();

    /// <summary>Runs the plugin's <c>Initialise</c> on this API, as MusicBee does.</summary>
    public PluginInfo Initialise(Plugin plugin)
    {
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Plugin.MusicBeeApiInterface)));
        try
        {
            Marshal.StructureToPtr(_api, pointer, false);
            return plugin.Initialise(pointer);
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
        }
    }

    /// <summary>Clicks Tools → faderio → <paramref name="command"/>.</summary>
    public void Choose(string command) =>
        Menus["mnuTools/faderio"].DropDownItems.Cast<ToolStripItem>().Single(i => i.Text == command).PerformClick();

    private ToolStripItem AddMenuItem(string menuPath, string hotkeyDescription, EventHandler handler)
    {
        Calls.Add($"MB_AddMenuItem {menuPath}");
        var item = new ToolStripMenuItem(menuPath.Split('/').Last());
        if (handler is not null)
        {
            item.Click += handler;
        }
        Menus[menuPath] = item;
        return item;
    }
}

/// <summary>MusicBee's UI thread as the plugin posts to it: each post is kept, not run, so a
/// message box's text is read without one being shown.</summary>
internal sealed class RecordingContext : SynchronizationContext
{
    public List<object?> Posted { get; } = new();

    public IReadOnlyList<string> Messages
    {
        get
        {
            lock (Posted)
            {
                return Posted.OfType<string>().ToList();
            }
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
    {
        lock (Posted)
        {
            Posted.Add(state);
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => Post(d, state);
}
