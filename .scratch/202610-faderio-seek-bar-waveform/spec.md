# Spec — faderio v3: a faint waveform on the seek bar

Status: needs-triage
Blocked by: none

Raised 2026-10-05: "how difficult would it be to show a waveform on the transport? it would need
to be very faint". Scoped the same day: **raw level, seek bar only**.

## Problem

The seek bar (`SeekBar`, shared by both hosts) shows where the playhead is and where the fades
begin and end (the wedges, phase 6 issue 11). It does not show the track itself. To find a quiet
intro, a break or a long fade-out on the recording, you have to listen for it or guess.

## Solution

Behind everything the seek bar already draws, the attended track's **waveform**: one thin
vertical line per pixel column, its height the loudest the audio gets in that slice of the track,
mirrored about the bar's centre line. It is **very faint**, a tint barely off the background, so
the track line, the thumb, the fade wedges and their handles read exactly as they do today.

```
 ┌──────────────────────────────────────────────────────────────┐
 │ ░ ░░▒░░▒▒░▒▒▒░▒▒░▒▒▒▒░▒▒░▒▒▒▒▒▒░▒▒▒░▒▒▒░▒▒▒▒░▒▒▒░▒░░ ░      │  ← faint
 │━━━━━━━━━━━━━━━━━━━━●─────────────────────────────────────────│  ← unchanged
 │ ░ ░░▒░░▒▒░▒▒▒░▒▒░▒▒▒▒░▒▒░▒▒▒▒▒▒░▒▒▒░▒▒▒░▒▒▒▒░▒▒▒░▒░░ ░      │
 └──────────────────────────────────────────────────────────────┘
```

- **Raw level.** The file's own samples. No ReplayGain, no fade, and no master gain.
  Turning ReplayGain on or off, or authoring a fade, never changes the drawing. The wedges
  already show the fades.
- **Seek bar only.** The transition editor and the queue do not draw it.
- **Both hosts.** The seek bar is in `Faderio.Shell`, so the App and the plugin get it from the
  same change.
- **No track, no waveform.** With nothing attended, the bar is empty as it is today. A track
  whose waveform is not known yet draws without one, and the waveform fades in, never jumps in,
  when it arrives. A track that cannot be read for a waveform (decode fails partway) simply has
  none. That is not worth a notice.

## Where the logic lives (CLAUDE.md's three tests)

- **The engine computes; the shell draws.** The engine publishes a summary on the attended
  track's view, `VoiceView.Waveform` (beside `Marks`). It is a fixed number of **bins**, each the peak absolute
  sample across all channels in that span of the track, as a value from 0 to 1 (full scale is
  1). The seek bar maps bin *i* of *n* to its pixel columns and scales the height. It does no
  sums and no decoding, and it keeps nothing between renders apart from what it is showing.
- **No frames cross the seam.** Bins are evenly spaced over the track's `Length`, so the shell
  needs no sample positions.
- `Waveform` is null while the summary is being worked out, or when there is none.

## Making the summary

- **A full decode, once per file, off every path that sounds.** The engine works the summary out
  on a background worker in its **own reader**, never the one playback or a prepared transition
  holds. It runs at below-normal priority and can be cancelled. A transition never waits for it
  (phase 6 issue 10: transitions never wait on the disk).
- **When.** For the attended track when it becomes attended, and then, idle, for the queued
  tracks after it, nearest first. A queue change or a library switch cancels work that is no
  longer wanted.
- **WMA comes free.** A WMA file is already decoded in full at add time to build its index
  (`WmaIndexStore`, ADR 0010). That same pass also makes its summary.
- **Kept.** The engine holds summaries in memory, and on disk in the library's store,
  `.faderio/waveforms/`. It keys them the way the WMA index is keyed: a hash of the file's audio
  (`AudioTail` for WMA; for the other formats, the same idea over the end of the file plus its
  length), not path and modified time. That way a tag edit in MusicBee keeps the waveform. A
  library opened again shows waveforms at once. Without a library, summaries are kept in memory
  only.
- **Resolution.** The engine always makes 2,000 bins, whatever the track's length or the bar's
  width. That is about 1 KB per track on disk as bytes (0–255). It is wider than any seek bar
  faderio shows, and the bar takes the highest bin in each pixel column.

## Decisions (proposed, for the user to accept or change)

1. **Peak, not RMS.** Peak shows silence, breaks and fade-outs most plainly, which is what the
   bar is for. RMS reads as "loudness" but flattens quiet detail.
2. **Linear height, not dB.** A linear peak is the usual look and keeps quiet passages visibly
   quiet. A dB scale would make a fade-out look like it barely fades.
3. **2,000 bins, stored as bytes.** See Resolution.
4. **The tint.** The track line's colour at about 12% opacity over the bar's background, in both
   hosts. The exact value is chosen by eye at the smoke step and kept as one constant.
5. **Queued tracks are worked out ahead, not only the attended one.** Then ⏭ and a transition
   land on a track that already has its waveform. The cost is idle CPU only, at below-normal
   priority.
6. **No setting to turn it off** in this slice. It is faint by design. A toggle can follow if
   it is ever wanted.

## Testing

Every test renders or decodes real audio through the production chain, as the project requires.
They run on net8.0 and net48 x86.

- A generated tone at amplitude 0.3 gives every bin 0.3 (within the bins' rounding). Silence
  gives zeros. A tone then silence gives 0.3 bins then zero bins, splitting at the right bin.
- The same for each format faderio plays: WAV, FLAC, MP3, WMA (both codecs).
- **Raw level.** With ReplayGain on and a tagged track, the bins are the same as with it off.
- **Never in the way.** With the summary worker held back (a test seam like
  `PreparedMemoryCap`'s), playback starts, seeks and transitions on time, and
  `Waveform` stays null until the worker is let go.
- **Kept.** A second engine on the same library finds the summary on disk without decoding.
  Editing a file's tags keeps it. Changing its audio makes a new one.
- **Cancelled.** Removing a queued track, or switching library, cancels its pending work.
- **Shell.** The seek bar with a known `Waveform` draws columns whose heights follow the bins
  (a headless paint into a bitmap), and draws none when it is null.

## Smoke

Add one Looks item to `docs/smoke.md`, in the App's half and the MusicBee half. Playing a track
with a quiet intro shows a faint waveform behind the seek bar within a moment of starting, low
at the intro. The track line, the thumb and the wedges are as clear as before. The second play
of the same track shows it at once.

## Out of scope

- A waveform in the transition editor, or beside the fade points while authoring.
- Zoom, scrubbing by the waveform, or colouring it by played and unplayed.
- A level after ReplayGain or after the fades.
- Stereo drawn as two halves.

## Comments
