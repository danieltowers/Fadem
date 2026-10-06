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
        _api.MB_GetWindowHandle = () => MainWindow;
        _api.MB_AddMenuItem = AddMenuItem;
        _api.Player_Stop = () =>
        {
            Calls.Add("Player_Stop");
            PlayerStopped?.Invoke();
            return true;
        };
        _api.Player_GetOutputDevices = (out string[] names, out string active) =>
        {
            Calls.Add("Player_GetOutputDevices");
            names = OutputDevices;
            active = ActiveDevice;
            return true;
        };
        _api.Setting_GetSkinElementColour = (element, state, component) =>
            Skin.TryGetValue(((int)element, (int)state, (int)component), out var argb) ? argb : 0;
        _api.Library_QueryFilesEx = (string query, out string[] files) =>
        {
            Calls.Add($"Library_QueryFilesEx {query}");
            files = Selection!;
            return Selection is not null;
        };
    }

    /// <summary>The skin's colours by element, state and component, as ARGB; 0, as MusicBee
    /// answers, for any it does not hold.</summary>
    public Dictionary<(int Element, int State, int Component), int> Skin { get; } = new();

    /// <summary>The tracks selected in MusicBee's main panel, as <c>domain=SelectedFiles</c>
    /// answers them; null when MusicBee cannot say (the query answers false).</summary>
    public string[]? Selection { get; set; } = Array.Empty<string>();

    /// <summary>What MusicBee's own preferences list as output devices, and the active one:
    /// "Primary Sound Driver" when it follows Windows' on DirectSound, as MusicBee says.</summary>
    public string[] OutputDevices { get; set; } = { "Primary Sound Driver", "Speakers (Test)", "DAC" };

    public string ActiveDevice { get; set; } = "Primary Sound Driver";

    public string StoragePath { get; }

    /// <summary>MusicBee's main window: none, unless a test stands a form in for it.</summary>
    public IntPtr MainWindow { get; set; } = IntPtr.Zero;

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

    /// <summary>Right-clicks the main panel's selection and chooses the plugin's item there.</summary>
    public void ChooseOnTheSelection(string item) => Menus[$"context.Main/{item}"].PerformClick();

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
/// message box's text is read without one being shown. <see cref="Pump"/> runs what is queued,
/// as MusicBee's message loop would, and the boxes those posts post are then kept in turn.</summary>
internal sealed class RecordingContext : SynchronizationContext
{
    private readonly Queue<(SendOrPostCallback Callback, object? State)> _queue = new();

    public List<object?> Posted { get; } = new();

    /// <summary>The message boxes posted, in order: the text each carries as its post's state.</summary>
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
            _queue.Enqueue((d, state));
        }
    }

    public override void Send(SendOrPostCallback d, object? state) => Post(d, state);

    /// <summary>Runs every post queued, those queued while running included, on the calling
    /// thread. A message box's post is not run: its text stays in <see cref="Messages"/>.</summary>
    public void Pump()
    {
        while (true)
        {
            (SendOrPostCallback Callback, object? State) post;
            lock (Posted)
            {
                if (_queue.Count == 0)
                {
                    return;
                }
                post = _queue.Dequeue();
            }
            if (post.State is not string)
            {
                post.Callback(post.State);
            }
        }
    }
}
