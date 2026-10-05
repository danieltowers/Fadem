# WMA decodes through the Windows Media Format runtime, its seeks placed by an index

Status: accepted (2026-10-05)

WMA was rejected ([ADR 0007](0007-audio-is-promised-or-rejected.md)) because Media Foundation,
the only WMA decoder measured then, missed seeks by up to 71 ms (ticket 02). It is now a
promised format in both hosts, through one decoder: the Windows Media Format runtime
(`wmvcore.dll`), called directly. Decided and built in
[phase 6 issue 12](../../.scratch/202609-faderio-phase-6-musicbee-improvements-round-1/issues/12-wma-plays-and-authors-exactly-in-both-hosts.md).

## Why this runtime

It ships with Windows, 32- and 64-bit, so standalone (net8.0 x64) and the plugin (net48 x86)
call the same code with no new dependency. It is not Media Foundation. MusicBee's own `basswma`
is a thin wrapper over it ([research](../research/wma-through-musicbee-bass.md)), so BASS would
have added a licence and a second decoder per host, and nothing to the decode.

## Why an index

The runtime seeks anywhere in a file in 1 to 17 ms, even 26 minutes in, but it stamps samples
in whole milliseconds. After a seek it lands up to 2 ms from where its stamps say, which fails
the 1 ms promise ([ADR 0009](0009-authoring-exactness-is-bought-in-the-decoder.md)). Decoding
forward from the start is exact, but it runs at about 320 times real time: 5 s to the end of a
26-minute track, under the engine lock for a seek the user makes.

What a seek delivers is still a decode from the start, cut at the same boundaries (or one of
them split in two), each stamped within a millisecond of the decode from the start. At 128 kbps
it is that decode sample for sample. At low bitrates the codec fills bands with noise from a
generator whose state is everything decoded before, so the samples differ slightly, but where
they sit does not. So one full decode records where every sample starts and how it is stamped: the
`WmaIndex`. A seek asks the runtime for the sample before its target, finds the first sample
after the first that the index names by its stamp, and so knows the exact frame of everything
it read. `WmaRuntimeTests` pins each of these facts.

## Where the decode happens

At add time. The probe decodes a promised WMA once (about 0.3% of its duration, in the add
worker) and that decode is also its length, which no WMA header gives exactly (below). The index
is kept in memory and in the library's store, `<library>\.faderio\wma-index\`, one file per audio
file named by a hash of its path, valid while the file's last mebibyte, its last audio packets,
is unchanged. ASF keeps tags in its header, so a tag edit, routine in MusicBee, keeps the index. A file
with no index when it is opened (a library opened on another machine, or audio changed since it
was added) is decoded in full at that open, over the 500 ms open budget for a long file. A
boundary's incoming track is opened by a worker ([ADR 0015](0015-rendering-never-opens-a-file.md)),
so that cost lands on the listener only when they play such a file directly, and then under the
engine lock: about 0.75 s for a 4-minute track, 5 s for 26 minutes. Opening without an index and
seeking by decoding from the start until a background decode finishes would avoid it, but the
length a reader reports at open would then not be the decoded one; not built.

## Length

The ASF header's duration is in milliseconds, and Windows' encoder leaves a codec tail of 17 to
43 ms past the music that the runtime decodes. The length is what the decode delivers, tail
included, as an untagged MP3's padding is ([ADR 0010](0010-one-length-issued-from-the-header.md)).
ffmpeg's files lose the source's last partial block; their length is likewise what decodes.

## What is promised

WMA 9 Standard (`wmav2`) at 44.1 and 48 kHz, mono and stereo. Mono at 48 kHz rests on ffmpeg's
fixture alone, since Windows' encoder offers none. WMA 9 Lossless joined it the same day (the
addendum). WMA 9 Pro and Voice are rejected at add time, naming the codec: they are unmeasured. On a Windows N edition without the Media Feature
Pack, where the runtime is missing, a WMA is rejected at add time with that reason.

## Consequences

- Adding a WMA costs a full decode, where every other format's probe reads only its header.
- The index files are derived data. Deleting them costs the next open of each file a decode.
- `MusicBeeBass.cs` and `MusicBeeBassWmaTests` stay as the research's record of MusicBee's
  BASS. They are not in the production chain.
- M4A could follow the same route through Media Foundation, if its samples after a seek are
  the decode from the start with stamps close enough to name them. Unmeasured.

## Addendum: WMA 9 Lossless (2026-10-05)

Measured when a library's Lossless files were rejected. Windows' encoder, the only one that
writes it, writes it in stereo only: 44.1 kHz at 16-bit, as Windows Media Player rips a CD, and
44.1, 48 and 96 kHz at 24-bit. Those four cells are promised.

- **The runtime's default output is not the file's.** It decodes 24-bit Lossless to 16-bit and
  96 kHz to 48 kHz. The reader asks for the stream's own rate, channels and depth from the
  formats the runtime offers, and reads 24-bit PCM. Standard is unchanged: 16-bit, as before.
- **The decode is the source**, sample for sample, from sample 0, with no codec tail: its length
  is the source's own count.
- **A seek can land after the time asked for**, up to 0.4 s on the fixtures, where Standard lands
  on it. The reader asks again earlier, by 0.5, 1 and 2 s, then from the start of the file, until
  it lands before the target; the index places it as before.

