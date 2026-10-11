# faderio is renamed Fadem, and what it wrote is renamed on first use

Status: accepted

The product is **Fadem**; "faderio" was a working title (decided 2026-10-05, before the first
public push, since a rename after it breaks links already sent out). Code, assemblies and text
are renamed outright: `Fadem.*`, `fadem.sln`, `fadem.exe`, the plugin `mb_fadem.dll` with its
private `fadem\` folder, `FADEM_MUSICBEE_DIR`. What faderio wrote on the user's disk is not
code, and holds real transitions: a library's `.faderio\`, the machine folders
`%APPDATA%\faderio\` and `<MusicBee storage>\faderio\`, and the `#FADERIO-SECTION` lines in
playlist files.

**Each folder is renamed on first use, never copied.** `Library.Open` renames
`<library>\.faderio\` to `.fadem\` before it creates anything; both hosts rename their machine
folder before reading it (`FormerName`). Where both names exist, the new one is used and the old
one left alone. Playlists are written with `#FADEM-SECTION` and read with either line.

Recorded as part of the public-release work (issue 02 of the release plan).

## Considered options

**Keep the old names on disk.** Rejected: a public product that writes `.faderio\` into the
user's music folder names something they never installed, and every doc would explain why.

**Read the old folder, write the new one.** Rejected: it leaves two copies, and the old one
looks authoritative to anything that reads it. A rename moves the whole folder at once, on one
volume, so there is never a half-moved store to recover from.

## Consequences

- **A library that cannot be renamed opens unavailable**, naming why: in practice faderio still
  running and holding `host.lock`. Nothing is written under `.fadem\` until the rename succeeds,
  so no store forks; the next open renames it.
- **A machine folder that cannot be renamed is used where it is, for that run.** Only the
  output mode, the library list, the window and the palette are at stake, and writing them
  back to the old folder keeps them for the next run's rename.
- A faderio build run after the rename finds no `.faderio\` and starts an empty one. Fadem
  ignores it, since `.fadem\` wins.
- A faderio build reads a Fadem playlist's rows and sections but not their colours or collapsed
  state, as ADR 0014 foresaw for any older reader.
- MusicBee finds plugins by file name, so `mb_faderio.dll` and its `faderio\` folder must be
  removed from `Plugins\` by hand when the new plugin is installed, or MusicBee loads both.
- `.scratch/`, `docs/research/` and the ADRs' dated decisions keep the old name as written.
  They are a record, and the README says the project was called faderio.
