using System.Runtime.InteropServices;

namespace Fadem.Core.Tests;

/// <summary>
/// MusicBee's own BASS, loaded from its install folder, for measuring how its decoders seek
/// (WMA research). MusicBee hands a plugin a BASS stream handle through
/// <c>Player_OpenStreamHandle</c>; this opens the same kind of stream on the same DLLs without
/// MusicBee, as a decode-only float stream. The DLLs are 32-bit, so this is net48 x86 only.
/// The folder is <c>FADEM_MUSICBEE_DIR</c>, else the default install; on a machine without
/// MusicBee the project leaves these tests out, with a build message, and never skips them
/// (ADR 0019).
/// </summary>
internal static class MusicBeeBass
{
    private const int StreamDecode = 0x200000;
    private const int SampleFloat = 0x100;
    private const int Unicode = unchecked((int)0x80000000);
    private const int DataFloat = 0x40000000;
    private const int PosByte = 0;
    private const int PosDecodeTo = 0x20000000;
    private const int ErrorEnded = 45;

    private static readonly Lazy<string> Loaded = new(Load);

    /// <summary>The last BASS error on this thread, for assertion messages.</summary>
    public static int LastError => BASS_ErrorGetCode();

    public static string Dir =>
        Environment.GetEnvironmentVariable("FADEM_MUSICBEE_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "MusicBee");

    /// <summary>bass.dll's version, as BASS reports it: 0x02041100 is 2.4.17.0.</summary>
    public static int Version
    {
        get
        {
            _ = Loaded.Value;
            return BASS_GetVersion();
        }
    }

    private static string Load()
    {
        var bass = Path.Combine(Dir, "bass.dll");
        Assert.True(File.Exists(bass), $"MusicBee's bass.dll is not at {bass}; install MusicBee or set FADEM_MUSICBEE_DIR");
        // Loaded by full path first, so every [DllImport("bass.dll")] below binds to MusicBee's.
        Assert.NotEqual(IntPtr.Zero, LoadLibrary(bass));
        // Device 0 is "no sound": decode streams need an initialised BASS, never a device.
        if (!BASS_Init(0, 44100, 0, IntPtr.Zero, IntPtr.Zero) && BASS_ErrorGetCode() != 14 /* already */)
        {
            Assert.Fail($"BASS_Init failed: error {BASS_ErrorGetCode()}");
        }
        foreach (var plugin in new[] { "basswma.dll" })
        {
            var handle = BASS_PluginLoad(Path.Combine(Dir, plugin), Unicode);
            Assert.True(handle != 0 || BASS_ErrorGetCode() == 14, $"BASS_PluginLoad({plugin}) failed: error {BASS_ErrorGetCode()}");
        }
        return Dir;
    }

    /// <summary>One file as a BASS decode stream of 32-bit float frames.</summary>
    internal sealed class Stream : IDisposable
    {
        private readonly int _handle;
        private float[] _scratch = new float[0];

        public Stream(string path)
        {
            _ = Loaded.Value;
            _handle = BASS_StreamCreateFile(false, path, 0, 0, StreamDecode | SampleFloat | Unicode);
            Assert.True(_handle != 0, $"BASS could not open {Path.GetFileName(path)}: error {BASS_ErrorGetCode()}");
            var info = new ChannelInfo();
            Assert.True(BASS_ChannelGetInfo(_handle, ref info));
            Rate = info.Freq;
            Channels = info.Chans;
            ChannelType = info.CType;
        }

        public int Rate { get; }
        public int Channels { get; }

        /// <summary>BASS_CTYPE_*: 0x10300 is basswma's WMA stream.</summary>
        public int ChannelType { get; }

        private int BytesPerFrame => 4 * Channels;

        /// <summary>The length BASS reports, in frames.</summary>
        public long LengthFrames => BASS_ChannelGetLength(_handle, PosByte) / BytesPerFrame;

        public long PositionFrames => BASS_ChannelGetPosition(_handle, PosByte) / BytesPerFrame;

        /// <summary>BASS's own seek, as <c>BASS_ChannelSetPosition</c> does it for a player.</summary>
        public bool Seek(long frame) => BASS_ChannelSetPosition(_handle, frame * BytesPerFrame, PosByte);

        /// <summary>BASS_POS_DECODETO: decode forward from where the stream is to the frame.</summary>
        public bool DecodeTo(long frame) => BASS_ChannelSetPosition(_handle, frame * BytesPerFrame, PosByte | PosDecodeTo);

        /// <summary>Reads up to <paramref name="frames"/> frames of the left channel; returns how many.</summary>
        public int ReadLeft(float[] left, int frames)
        {
            var samples = frames * Channels;
            if (_scratch.Length < samples)
            {
                _scratch = new float[samples];
            }
            var got = 0;
            while (got < frames)
            {
                var handle = GCHandle.Alloc(_scratch, GCHandleType.Pinned);
                int bytes;
                try
                {
                    var at = handle.AddrOfPinnedObject() + got * BytesPerFrame;
                    bytes = BASS_ChannelGetData(_handle, at, ((frames - got) * BytesPerFrame) | DataFloat);
                }
                finally
                {
                    handle.Free();
                }
                if (bytes == -1)
                {
                    Assert.True(BASS_ErrorGetCode() == ErrorEnded, $"BASS_ChannelGetData failed: error {BASS_ErrorGetCode()}");
                    break;
                }
                if (bytes == 0)
                {
                    break;
                }
                got += bytes / BytesPerFrame;
            }
            for (var f = 0; f < got; f++)
            {
                left[f] = _scratch[f * Channels];
            }
            return got;
        }

        /// <summary>Reads to the end; returns the left channel.</summary>
        public float[] ReadAllLeft()
        {
            var all = new List<float>();
            var block = new float[4096];
            int n;
            while ((n = ReadLeft(block, block.Length)) > 0)
            {
                all.AddRange(block.Take(n));
            }
            return all.ToArray();
        }

        public void Dispose() => BASS_StreamFree(_handle);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ChannelInfo
    {
        public int Freq;
        public int Chans;
        public int Flags;
        public int CType;
        public int OrigRes;
        public int Plugin;
        public int Sample;
        public IntPtr FileName;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibrary(string path);

    [DllImport("bass.dll")] private static extern int BASS_GetVersion();
    [DllImport("bass.dll")] private static extern bool BASS_Init(int device, int freq, int flags, IntPtr win, IntPtr guid);
    [DllImport("bass.dll")] private static extern int BASS_ErrorGetCode();
    [DllImport("bass.dll", CharSet = CharSet.Unicode)] private static extern int BASS_PluginLoad(string file, int flags);
    [DllImport("bass.dll", CharSet = CharSet.Unicode)] private static extern int BASS_StreamCreateFile(bool mem, string file, long offset, long length, int flags);
    [DllImport("bass.dll")] private static extern bool BASS_StreamFree(int handle);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelGetInfo(int handle, ref ChannelInfo info);
    [DllImport("bass.dll")] private static extern long BASS_ChannelGetLength(int handle, int mode);
    [DllImport("bass.dll")] private static extern long BASS_ChannelGetPosition(int handle, int mode);
    [DllImport("bass.dll")] private static extern bool BASS_ChannelSetPosition(int handle, long pos, int mode);
    [DllImport("bass.dll")] private static extern int BASS_ChannelGetData(int handle, IntPtr buffer, int length);
}
