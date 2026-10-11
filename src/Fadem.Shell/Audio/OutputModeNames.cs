using Fadem.Core.Settings;

namespace Fadem.Shell.Audio;

/// <summary>The name a mode goes by wherever the listener reads it: the title, the settings
/// dialog and the fallback notices.</summary>
public static class OutputModeNames
{
    public static string Of(AudioOutputMode mode) => mode switch
    {
        AudioOutputMode.WasapiExclusive => "WASAPI exclusive",
        AudioOutputMode.WaveOut => "WaveOut",
        _ => "WASAPI shared",
    };
}
