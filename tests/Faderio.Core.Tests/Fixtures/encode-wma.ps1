# Encodes raw 32-bit float PCM to WMA with Windows' own encoder (the Windows Media Format
# runtime, as Windows Media Player rips with), reached through MusicBee's basswma.dll. Called
# by generate.sh; must run in 32-bit PowerShell, because MusicBee's BASS is 32-bit:
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe -File encode-wma.ps1 in.f32 out.wma rate channels bitrate [flags]
# flags are BASS_WMA_ENCODE_*: 0 for WMA 9 Standard, 0x4000 (16384) for WMA 9 Pro, and with
# 0x8000 (24-bit) 49152. A bitrate of 100 or less is VBR quality, and 100 is WMA 9 Lossless.
param([string]$In, [string]$Out, [int]$Rate, [int]$Channels, [int]$Bitrate, [int]$Flags = 0)
$ErrorActionPreference = 'Stop'
if ([Environment]::Is64BitProcess) { throw 'run in 32-bit PowerShell: MusicBee''s BASS is 32-bit' }
$dir = $env:FADERIO_MUSICBEE_DIR
if (-not $dir) { $dir = Join-Path ${env:ProgramFiles(x86)} 'MusicBee' }

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class WmaEncoder
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadLibrary(string path);
    [DllImport("bass.dll")] static extern bool BASS_Init(int device, int freq, int flags, IntPtr win, IntPtr guid);
    [DllImport("bass.dll")] static extern int BASS_ErrorGetCode();
    [DllImport("basswma.dll", CharSet = CharSet.Unicode)] static extern int BASS_WMA_EncodeOpenFile(int freq, int chans, int flags, int bitrate, string file);
    [DllImport("basswma.dll")] static extern bool BASS_WMA_EncodeWrite(int handle, float[] buffer, int length);
    [DllImport("basswma.dll")] static extern void BASS_WMA_EncodeClose(int handle);

    public static void Encode(string dir, byte[] raw, string output, int rate, int channels, int bitrate, int flags)
    {
        if (LoadLibrary(System.IO.Path.Combine(dir, "bass.dll")) == IntPtr.Zero) throw new Exception("bass.dll did not load");
        if (LoadLibrary(System.IO.Path.Combine(dir, "basswma.dll")) == IntPtr.Zero) throw new Exception("basswma.dll did not load");
        BASS_Init(0, rate, 0, IntPtr.Zero, IntPtr.Zero);
        const int SampleFloat = 0x100, Unicode = unchecked((int)0x80000000);
        int handle = BASS_WMA_EncodeOpenFile(rate, channels, SampleFloat | Unicode | flags, bitrate, output);
        if (handle == 0) throw new Exception("BASS_WMA_EncodeOpenFile failed: error " + BASS_ErrorGetCode());
        float[] samples = new float[raw.Length / 4];
        Buffer.BlockCopy(raw, 0, samples, 0, samples.Length * 4);
        if (!BASS_WMA_EncodeWrite(handle, samples, samples.Length * 4)) throw new Exception("BASS_WMA_EncodeWrite failed: error " + BASS_ErrorGetCode());
        BASS_WMA_EncodeClose(handle);
    }
}
'@

[WmaEncoder]::Encode($dir, [IO.File]::ReadAllBytes($In), [IO.Path]::GetFullPath($Out), $Rate, $Channels, $Bitrate, $Flags)
