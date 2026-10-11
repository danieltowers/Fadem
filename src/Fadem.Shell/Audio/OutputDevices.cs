using System.Runtime.InteropServices;
using Fadem.Core.Settings;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace Fadem.Shell.Audio;

/// <summary>
/// The output devices behind <see cref="NAudioAudioOutput"/> (phase 5 issue 13): what a settings
/// dialog lists, and what the output opens a player on. The production binding is
/// <see cref="OutputDevices"/>; a test stands in its own, with fake players, as the release tests
/// do.
/// </summary>
internal interface IOutputDevices
{
    /// <summary>The active render devices, by the names Windows shows for them.</summary>
    IReadOnlyList<string> Names();

    /// <summary>The device named, or null when no active device has that name. With null, the
    /// default device, or null when there is none at all.</summary>
    OutputDevice? Find(string? name);
}

/// <summary>A device the output can open: its name, its mix rate when asked, and a player for a
/// mode on it. Disposed with the player opened on it.</summary>
internal sealed class OutputDevice : IDisposable
{
    private readonly Func<int> _mixRate;
    private readonly Func<AudioOutputMode, IWavePlayer> _open;
    private readonly IDisposable? _handle;

    /// <param name="mixRate">The device's shared-mode rate; throws when the device cannot be asked.</param>
    /// <param name="open">A player for a mode on this device, not yet initialised.</param>
    public OutputDevice(string name, Func<int> mixRate, Func<AudioOutputMode, IWavePlayer> open, IDisposable? handle = null)
    {
        Name = name;
        _mixRate = mixRate;
        _open = open;
        _handle = handle;
    }

    /// <summary>The device's name in Windows: what the settings name and the host line shows.</summary>
    public string Name { get; }

    /// <summary>The device's shared-mode rate; throws when the device cannot be asked.</summary>
    public int MixRate() => _mixRate();

    /// <summary>A player for <paramref name="mode"/> on this device, not yet initialised.</summary>
    public IWavePlayer Open(AudioOutputMode mode) => _open(mode);

    /// <summary>Lets go of the device's handle; the player on it is closed first.</summary>
    public void Dispose() => _handle?.Dispose();
}

/// <summary>
/// This machine's render devices through NAudio: WASAPI endpoints by their friendly names, the
/// names MusicBee's own preferences use too. A WaveOut player on a device goes by the waveOut
/// device whose product name (cut to 31 characters by Windows) begins that endpoint's name, or
/// the wave mapper, the default, when none does.
/// </summary>
public sealed class OutputDevices : IOutputDevices
{
    /// <summary>The devices installed, for the hosts' dialogs.</summary>
    public static readonly OutputDevices Installed = new();

    private const int WaveOutProductNameLength = 31; // MAXPNAMELEN - 1

    public IReadOnlyList<string> Names()
    {
        using var enumerator = new MMDeviceEnumerator();
        var names = new List<string>();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                names.Add(device.FriendlyName);
            }
        }
        return names;
    }

    OutputDevice? IOutputDevices.Find(string? name)
    {
        using var enumerator = new MMDeviceEnumerator();
        MMDevice? found = null;
        if (name is null)
        {
            if (enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia))
            {
                found = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
        }
        else
        {
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (found is null && string.Equals(device.FriendlyName, name, StringComparison.Ordinal))
                {
                    found = device;
                }
                else
                {
                    device.Dispose();
                }
            }
        }
        if (found is null)
        {
            return null;
        }
        var friendlyName = found.FriendlyName;
        return new OutputDevice(friendlyName, () => MixRateOf(found), mode => Open(found, friendlyName, mode), found);
    }

    private static int MixRateOf(MMDevice device)
    {
        using var client = device.AudioClient;
        return client.MixFormat.SampleRate;
    }

    private static IWavePlayer Open(MMDevice device, string name, AudioOutputMode mode) => mode switch
    {
        AudioOutputMode.WasapiExclusive =>
            new WasapiOut(device, AudioClientShareMode.Exclusive, true, NAudioAudioOutput.ExclusiveLatencyMs),
        AudioOutputMode.WaveOut =>
            new WaveOutEvent { DesiredLatency = NAudioAudioOutput.WaveOutLatencyMs, DeviceNumber = WaveOutDeviceNumber(name) },
        _ => new WasapiOut(device, AudioClientShareMode.Shared, true, NAudioAudioOutput.SharedLatencyMs),
    };

    /// <summary>The waveOut device for an endpoint, by name, or −1 (the wave mapper) when none
    /// matches.</summary>
    private static int WaveOutDeviceNumber(string endpointName)
    {
        var count = WaveInterop.waveOutGetNumDevs();
        for (var i = 0; i < count; i++)
        {
            WaveInterop.waveOutGetDevCaps((IntPtr)i, out var caps, Marshal.SizeOf(typeof(WaveOutCapabilities)));
            var product = caps.ProductName ?? "";
            var matches = product.Length >= WaveOutProductNameLength
                ? endpointName.StartsWith(product, StringComparison.Ordinal)
                : string.Equals(endpointName, product, StringComparison.Ordinal);
            if (matches)
            {
                return i;
            }
        }
        return -1;
    }
}
