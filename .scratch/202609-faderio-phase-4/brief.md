## Improvements

1. albums stored as a single file with a cue sheet
    - an album ripped as one FLAC (or WAV) plus a `.cue`, or with the cue sheet embedded in the
      FLAC, should list as its separate tracks, not one long track
    - each cue track should behave like any other track: its own row, its own title and length,
      and transitions authored between it and its neighbours

---

## What happens today (2026-09-27)

Nothing in faderio knows about cue sheets: there is no `.cue` handling in the code, the specs or
`docs/supported-audio.md`.

- **Add folder…** queues the single album file as one track: one row, the whole album's length,
  title and artist from the file's tags. The `.cue` beside it is ignored without a word, as cover
  scans are, since a folder add takes only `.wav`, `.flac` and `.mp3`.
- **A `.cue` added on its own** is rejected: "Not a promised format; only WAV, FLAC and MP3 are."
- **A cue sheet embedded in the FLAC** (a `CUESHEET` metadata block, or a `CUESHEET` tag) is
  ignored too. The probe reads STREAMINFO only, and the tag reader doesn't look for it.

The album plays straight through, with no boundary inside it to author a transition on.

## Why it is not small

The model says a track is a file:

- `CONTEXT.md`: **Track**, "A single audio file known to the player."
- ADR 0005: the transition store is keyed by track pair, which is the pair of file paths. Two
  tracks of one album file would share a key.
- ADR 0010: a track has one length, issued by the add-time probe from the file's header.
- Playlists are M3U files of absolute paths (playlists issues 01 and 03), one entry per file.

A cue track is a file plus a time range. Supporting it touches the store's key, the length, seek
and the playhead's zero, the M3U playlists, the editor's points (measured from the cue track's
start, not the file's), preview and render, and what "promised" means for a range of a file.

## Questions to grill

- **What is a track?** A file plus an optional range, with a range-less file as the whole
  file? Does the glossary change, and does an ADR record it?
- **The store key.** How is a cue track keyed: path plus track number, path plus start time, or
  something else? What happens to stored transitions when a cue sheet is edited and its
  indexes move?
- **Where the cue comes from.** A `.cue` beside the file, one embedded in the FLAC, or both?
  Which wins when they disagree? What of a `.cue` naming several files, or a file that is not
  promised (APE, WavPack)?
- **What gets added.** Does **Add folder…** take the `.cue` and skip the file it names, so the
  album isn't queued twice? Can the single file still be added whole?
- **Gapless joins.** Consecutive cue tracks of one file are one continuous stream, and with no
  transition authored they must join with no gap and no click (compare the MP3 gapless Sounds
  item). Does the engine keep the file open across the boundary, or reopen at the index?
- **Pregap and INDEX 00.** Does a track start at INDEX 00 or INDEX 01, and where does the
  hidden pregap play?
- **Metadata.** Titles and performers from the cue's `TITLE`/`PERFORMER`, with the file's tags
  as fallback? Album from the cue's top-level `TITLE`? What do the Format and Replay gain
  columns show (the file's; per-track gain from `REPLAYGAIN_TRACK_GAIN` lines in the cue)?
- **Playlists.** How does an M3U name a cue track? Other players do it by listing the `.cue`
  itself; faderio's per-track rows and overrides then need a key it can write and read back.
- **The promise.** The authoring promise (ADR 0009) is exact to a tolerance in a file. Is it the
  same within a range of a file, and is it proven on a real cue album in the real-input matrix?

## Out of scope for this brief

Writing or editing cue sheets, splitting an album file into track files, and CD ripping.
