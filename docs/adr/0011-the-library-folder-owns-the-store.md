# The library folder owns the transition store, the playlists and the listening settings

Status: accepted

Through Phase 1 the transition store and settings lived in `%APPDATA%\faderio\`, and a playlist
was any M3U the user pointed at. That made one player with one store, and left overrides with
nothing to be keyed by. A **Library** is now a folder the user chooses; everything faderio
writes goes into `<library>/.faderio/`: `transitions.json`, `playlists/*.m3u8` and
`settings.json` (the listening settings: unauthored-adjacency choice, default crossfade,
ReplayGain, shuffle, repeat). AppData keeps only what is about the machine: the output mode,
which library to open, and window geometry. Audio files may live anywhere; a playlist row is
still a path.

Decided in the interim-improvements grilling
([decisions D10–D13](../../.scratch/202609-faderio-phase-2-interim-improvements/decisions.md)).

## Considered options

**Keep one store in AppData and give playlists stable ids.** Rejected: a store that names every
track on the machine by full path is neither portable nor inspectable, and there is nothing to
copy when you want to carry a library to another machine.

**Make the library the music folder, with `.faderio/` inside it and only files under it
promised.** Rejected as too strong for now: rows are paths and may point anywhere; the folder is
where faderio *writes*, not a boundary on what it plays. Nothing stops a user choosing their
music folder.

**Migrate the AppData store into the first library.** Rejected by the user: the AppData data is
test data. A new library starts empty.

## Consequences

- ADR 0003's addendum "there is no *replace the queue*" is amended: **`OpenPlaylist` replaces
  the queue**, because the queue mirrors the open playlist (`CONTEXT.md`, **Queue**). `Add` still
  appends.
- ADR 0005 is reaffirmed: playlists stay M3U and overrides live in the store, keyed by
  playlist name. The name is the file name; a rename rekeys.
- Switching libraries is switching worlds: stop, clear, reload. No state crosses.
