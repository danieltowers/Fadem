using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Faderio.Core.Playback;

/// <summary>
/// The Windows Media Format runtime's synchronous reader (<c>wmvcore.dll</c>), the decoder
/// behind WMA in both hosts (issue 12). It ships with Windows in both bitnesses, so standalone
/// (net8.0 x64) and the plugin (net48 x86) call the same code; it is not Media Foundation, the
/// route ticket 02 measured. Called through the raw COM vtables rather than runtime-callable
/// wrappers: the reader is created by a flat function, not registered, and a wrapper would try
/// to marshal it between the threads that open, seek and read a track (ADR 0015). The engine
/// never calls one reader from two threads at once.
/// <para>Times are the runtime's: 100 ns units. Output is the runtime's default for the stream,
/// PCM at the file's own rate and channel count.</para>
/// </summary>
internal sealed unsafe class WmSyncReader : IDisposable
{
    private const int FormatTagPcm = 1;

    private IntPtr _reader;
    private readonly uint _output;

    /// <summary>Held for the reader's life: the runtime calls back into it.</summary>
    private StreamAdapter? _adapter;

    private WmSyncReader(IntPtr reader, uint output, WmPcmFormat format, ushort codec)
    {
        _reader = reader;
        _output = output;
        Format = format;
        CodecTag = codec;
    }

    /// <summary>What the runtime decodes to.</summary>
    public WmPcmFormat Format { get; }

    /// <summary>The audio stream's WAVEFORMATEX format tag as encoded: 0x161 is WMA 9 Standard
    /// (wmav2), 0x162 Pro, 0x163 Lossless, 0x0A Voice.</summary>
    public ushort CodecTag { get; }

    /// <summary>Why the runtime cannot be used here, or null when it can: Windows N and KN
    /// editions lack it without the Media Feature Pack.</summary>
    public static string? Unavailable => RuntimeMissing.Value;

    private static readonly Lazy<string?> RuntimeMissing = new(() =>
    {
        try
        {
            Check(WMCreateSyncReader(IntPtr.Zero, 0, out var reader));
            Release(reader);
            return null;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or COMException)
        {
            return "The Windows Media Format runtime is not installed (Windows N editions need the Media Feature Pack).";
        }
    });

    /// <summary>Opens <paramref name="file"/>'s first audio stream for PCM output. The runtime
    /// reads through <paramref name="file"/>, which the caller owns and disposes after this
    /// reader: a file opened by name would be held without delete sharing, and a file read into
    /// memory has no name. <paramref name="name"/> is for messages.</summary>
    public static WmSyncReader Open(Stream file, string name)
    {
        if (Unavailable is { } missing)
        {
            throw new NotSupportedException(missing);
        }
        file.Position = 0;
        Check(WMCreateSyncReader(IntPtr.Zero, 0, out var reader));
        var adapter = new StreamAdapter(file);
        try
        {
            var stream = Marshal.GetComInterfaceForObject(adapter, typeof(IStream));
            try
            {
                CheckFile(Call(reader, 23, stream), name); // OpenStream: the runtime keeps its own reference
            }
            finally
            {
                Marshal.Release(stream);
            }
            var opened = Create(reader);
            opened._adapter = adapter;
            return opened;
        }
        catch
        {
            Release(reader);
            throw;
        }
    }

    private static WmSyncReader Create(IntPtr reader)
    {
        uint outputs;
        Check(Call(reader, 14, (IntPtr)(&outputs)));
        for (uint output = 0; output < outputs; output++)
        {
            IntPtr props;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, IntPtr*, int>)Slot(reader, 15))(reader, output, &props));
            WmPcmFormat? pcm;
            try
            {
                pcm = PcmFormat(props);
            }
            finally
            {
                Release(props);
            }
            if (pcm is { } format)
            {
                ushort stream;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, uint, ushort*, int>)Slot(reader, 20))(reader, output, &stream));
                return new WmSyncReader(reader, output, format, CodecOf(reader, stream));
            }
        }
        throw new InvalidDataException("The file has no audio stream.");
    }

    /// <summary>Starts the next read at <paramref name="time"/>, or as near before it as the
    /// runtime lands; the first sample's time says where.</summary>
    public void SetRange(long time) =>
        Check(((delegate* unmanaged[Stdcall]<IntPtr, ulong, long, int>)Slot(_reader, 5))(_reader, (ulong)time, 0));

    /// <summary>The next decoded sample of the audio output, copied into <paramref name="buffer"/>
    /// (grown as needed): its time, and its length in bytes. False at the end of the file.</summary>
    public bool Next(ref byte[] buffer, out long time, out int bytes)
    {
        while (true)
        {
            IntPtr sample;
            ulong sampleTime, duration;
            uint flags, output;
            ushort stream;
            var hr = ((delegate* unmanaged[Stdcall]<IntPtr, ushort, IntPtr*, ulong*, ulong*, uint*, uint*, ushort*, int>)Slot(_reader, 7))(
                _reader, 0, &sample, &sampleTime, &duration, &flags, &output, &stream);
            if (hr == NsENoMoreSamples)
            {
                time = 0;
                bytes = 0;
                return false;
            }
            Check(hr);
            try
            {
                if (output != _output)
                {
                    continue;
                }
                byte* data;
                uint length;
                Check(((delegate* unmanaged[Stdcall]<IntPtr, byte**, uint*, int>)Slot(sample, 7))(sample, &data, &length));
                if (buffer.Length < length)
                {
                    buffer = new byte[length];
                }
                Marshal.Copy((IntPtr)data, buffer, 0, (int)length);
                time = (long)sampleTime;
                bytes = (int)length;
                return true;
            }
            finally
            {
                Release(sample);
            }
        }
    }

    public void Dispose()
    {
        if (_reader == IntPtr.Zero)
        {
            return;
        }
        Call(_reader, 4); // Close
        Release(_reader);
        _reader = IntPtr.Zero;
        _adapter = null;
    }

    // ── Formats ──────────────────────────────────────────────────────────────────────────

    /// <summary>The output's WAVEFORMATEX when it is PCM audio, else null.</summary>
    private static WmPcmFormat? PcmFormat(IntPtr mediaProps)
    {
        var wave = WaveFormatOf(mediaProps);
        if (wave is not { } w || w.Tag != FormatTagPcm && w.Tag != 0xFFFE)
        {
            return null;
        }
        return new WmPcmFormat(w.Rate, w.Channels, w.Bits);
    }

    /// <summary>The encoded format tag of stream <paramref name="stream"/>, from the reader's
    /// profile (IWMProfile).</summary>
    private static ushort CodecOf(IntPtr reader, ushort stream)
    {
        var profile = QueryInterface(reader, IidProfile);
        try
        {
            IntPtr config;
            Check(((delegate* unmanaged[Stdcall]<IntPtr, ushort, IntPtr*, int>)Slot(profile, 10))(profile, stream, &config)); // GetStreamByNumber
            try
            {
                var props = QueryInterface(config, IidMediaProps);
                try
                {
                    return WaveFormatOf(props)?.Tag ?? 0;
                }
                finally
                {
                    Release(props);
                }
            }
            finally
            {
                Release(config);
            }
        }
        finally
        {
            Release(profile);
        }
    }

    private readonly record struct Wave(ushort Tag, int Channels, int Rate, int Bits);

    /// <summary>IWMMediaProps.GetMediaType's WAVEFORMATEX, when it is audio.</summary>
    private static Wave? WaveFormatOf(IntPtr mediaProps)
    {
        var getMediaType = (delegate* unmanaged[Stdcall]<IntPtr, byte*, uint*, int>)Slot(mediaProps, 4);
        uint size = 0;
        Check(getMediaType(mediaProps, null, &size));
        var block = new byte[size];
        fixed (byte* type = block)
        {
            Check(getMediaType(mediaProps, type, &size));
            var media = (MediaType*)type;
            if (media->MajorType != MediaTypeAudio || media->FormatType != FormatWaveFormatEx || media->CbFormat < 16)
            {
                return null;
            }
            var wave = media->PbFormat;
            var tag = *(ushort*)wave;
            var channels = *(ushort*)(wave + 2);
            var rate = *(int*)(wave + 4);
            var bits = *(ushort*)(wave + 14);
            if (tag == 0xFFFE && media->CbFormat >= 40)
            {
                tag = *(ushort*)(wave + 24); // WAVEFORMATEXTENSIBLE's sub-format GUID's first word
            }
            return new Wave(tag, channels, rate, bits);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MediaType
    {
        public Guid MajorType;
        public Guid SubType;
        public int FixedSizeSamples;
        public int TemporalCompression;
        public uint SampleSize;
        public Guid FormatType;
        public IntPtr Unk;
        public uint CbFormat;
        public byte* PbFormat;
    }

    private static readonly Guid MediaTypeAudio = new("73647561-0000-0010-8000-00AA00389B71");
    private static readonly Guid FormatWaveFormatEx = new("05589F81-C356-11CE-BF01-00AA0055595A");
    private static readonly Guid IidProfile = new("96406BDB-2B2B-11d3-B36B-00C04F6108FF");
    private static readonly Guid IidMediaProps = new("96406BCE-2B2B-11d3-B36B-00C04F6108FF");

    // ── COM by hand ──────────────────────────────────────────────────────────────────────

    private const int NsENoMoreSamples = unchecked((int)0xC00D0BCF);

    private static IntPtr Slot(IntPtr instance, int slot) => (*(IntPtr**)instance)[slot];

    private static int Call(IntPtr instance, int slot) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(instance, slot))(instance);

    private static int Call(IntPtr instance, int slot, IntPtr argument) =>
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Slot(instance, slot))(instance, argument);

    private static IntPtr QueryInterface(IntPtr instance, Guid iid)
    {
        IntPtr result;
        Check(((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(instance, 0))(instance, &iid, &result));
        return result;
    }

    private static void Release(IntPtr instance)
    {
        if (instance != IntPtr.Zero)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, uint>)Slot(instance, 2))(instance);
        }
    }

    /// <summary>Every failure the runtime reports is a <see cref="COMException"/>, whatever its
    /// HRESULT, so a caller catches one type: the probe turns it into a rejection.</summary>
    private static void Check(int hr)
    {
        if (hr < 0)
        {
            throw new COMException($"Windows Media failed (0x{hr:X8}).", hr);
        }
    }

    /// <summary>A file the runtime cannot read is invalid data, which the probe turns into a
    /// rejection with the reason, not a crash.</summary>
    private static void CheckFile(int hr, string path)
    {
        if (hr < 0)
        {
            throw new InvalidDataException(
                $"Windows Media could not open {Path.GetFileName(path)} (0x{hr:X8}).",
                Marshal.GetExceptionForHR(hr));
        }
    }

    /// <summary>The caller's stream as the COM IStream the runtime reads from. Only what a
    /// reader calls is implemented; the rest answers E_NOTIMPL.</summary>
    [ComVisible(true)]
    private sealed class StreamAdapter : IStream
    {
        private readonly Stream _stream;

        public StreamAdapter(Stream stream) => _stream = stream;

        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            var read = 0;
            int n;
            while (read < cb && (n = _stream.Read(pv, read, cb - read)) > 0)
            {
                read += n;
            }
            if (pcbRead != IntPtr.Zero)
            {
                Marshal.WriteInt32(pcbRead, read);
            }
        }

        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            var position = _stream.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero)
            {
                Marshal.WriteInt64(plibNewPosition, position);
            }
        }

        public void Stat(out System.Runtime.InteropServices.ComTypes.STATSTG pstatstg, int grfStatFlag) =>
            pstatstg = new System.Runtime.InteropServices.ComTypes.STATSTG { type = 2 /* STGTY_STREAM */, cbSize = _stream.Length, grfMode = 0 };

        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new NotImplementedException();
        public void SetSize(long libNewSize) => throw new NotImplementedException();
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten) => throw new NotImplementedException();
        public void Commit(int grfCommitFlags) => throw new NotImplementedException();
        public void Revert() => throw new NotImplementedException();
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new NotImplementedException();
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new NotImplementedException();
        public void Clone(out IStream ppstm) => throw new NotImplementedException();
    }

    [DllImport("wmvcore.dll", ExactSpelling = true)]
    private static extern int WMCreateSyncReader(IntPtr certificate, uint rights, out IntPtr reader);
}

/// <summary>The PCM the runtime decodes a WMA stream to.</summary>
internal readonly record struct WmPcmFormat(int SampleRate, int Channels, int BitsPerSample)
{
    public int BlockAlign => Channels * BitsPerSample / 8;
}
