namespace Faderio.Core.Playback;

/// <summary>A count of engine frames (one sample per channel). Appears on exactly one seam,
/// <see cref="IAudioOutput"/>, so a sample count can never be handed across it: v2's
/// frames-versus-samples defect becomes a compile error at the adapter edge (ADR 0003).</summary>
public readonly record struct Frames(long Count)
{
    public static Frames Zero => new(0);
    public static Frames operator +(Frames a, Frames b) => new(a.Count + b.Count);
    public static Frames operator -(Frames a, Frames b) => new(a.Count - b.Count);
    public static bool operator <(Frames a, Frames b) => a.Count < b.Count;
    public static bool operator >(Frames a, Frames b) => a.Count > b.Count;
    public static bool operator <=(Frames a, Frames b) => a.Count <= b.Count;
    public static bool operator >=(Frames a, Frames b) => a.Count >= b.Count;
    public override string ToString() => $"{Count} frames";
}

public enum Transport
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>What the output pulls: interleaved float frames at the engine's rate.</summary>
public interface IFrameSource
{
    int SampleRate { get; }
    int Channels { get; }

    /// <summary>Fills <paramref name="wanted"/> frames into <paramref name="interleaved"/> at
    /// <paramref name="offset"/> (in samples) and returns the frames written — the full count,
    /// silence when there is nothing to mix. Zero is end of stream: the device is being stopped
    /// and nothing was written.</summary>
    Frames Read(float[] interleaved, int offset, Frames wanted);
}

/// <summary>
/// The output seam (ADR 0002, S1). Production binds NAudio; tests bind a fake that is the
/// clock; ticket 11 binds a WAV file. The engine runs at <see cref="SampleRate"/>, fixed at
/// launch (ADR 0008). <see cref="FramesEmitted"/> is what the device has actually made audible
/// since <see cref="Stop"/> last reset it — the playhead's only source (ADR 0001). Frames
/// emitted and the mixer timeline share one origin: <see cref="Stop"/> resets both.
/// </summary>
public interface IAudioOutput : IDisposable
{
    int SampleRate { get; }

    void Initialise(IFrameSource source);

    void Play();

    void Pause();

    /// <summary>Stops and resets <see cref="FramesEmitted"/> to zero.</summary>
    void Stop();

    Transport State { get; }

    /// <summary>Frames made audible. When the device fails on its own it holds what was heard at
    /// the failure until <see cref="Stop"/> resets it: the engine lands there (build issue 11).</summary>
    Frames FramesEmitted { get; }

    /// <summary>Opens the output again on the current default device, after <see cref="Stopped"/>
    /// reported a failure, for the same source at the same <see cref="SampleRate"/>: a device at
    /// another rate is converted to. Returns that device's own rate. Throws when no device can be
    /// opened. Leaves the output stopped, ready for <see cref="Play"/>.</summary>
    int Reopen();

    /// <summary>Raised when the device stops on its own; the exception is null for a clean end
    /// and says why for a failure, such as the device being unplugged.</summary>
    event Action<Exception?>? Stopped;
}
