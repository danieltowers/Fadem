# 04: Choosing the library and output mode in MusicBee

Status: ready-for-agent
Blocked by: 03

Part of the [MusicBee spike](../spec.md).

## What to build

MusicBee's Configure for the faderio plugin opens a small WinForms dialog with:

- a library folder picker
- an output mode choice: WASAPI exclusive, WASAPI shared, WaveOut

MusicBee's `SaveSettings` persists both to the plugin's machine settings, and the next
`Initialise` reads them back.

- **Changing the library** stops playback and switches the engine's library, as standalone
  faderio switches libraries.
- **Changing the output mode** takes effect as it does in standalone faderio.
- **A chosen folder that is gone** shows its message.

## Acceptance criteria

- [ ] Headless (the ticket 03 seam):
  - [ ] Settings saved through `SaveSettings` are read back by a fresh `Initialise`.
  - [ ] Choosing another library while playing stops playback and opens the new library's open
        playlist.
  - [ ] A gone folder gives its message and leaves no library open.
- [ ] The MusicBee Looks smoke gains, reported item by item:
  - the dialog opens from MusicBee's plugin preferences
  - a chosen library and output mode survive a MusicBee restart
  - a gone folder shows its message
- [ ] The commit states both counts.
