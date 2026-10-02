# 09: Sections organise a playlist

Status: resolved
Blocked by: none

Phase 6, improvements round 1, both hosts (the queue view is shared shell). Raised 2026-10-02:
"id like track groups within a playlist, just for organisation purposes. could just be a
tree-mode, colour coding, naming, icons. For example i would like to have an intro section, a
middle section and an end".

## What to build

A playlist can be divided into named **sections**, for example *Intro*, *Middle*, *End*. They
only organise the playlist. They do not change playback: the queue plays straight through
section boundaries, and the transition at a boundary is chosen and played as it is now.

In the queue, each section starts with a **header row** showing its name and colour, and can
collapse to that header. The rows under a header carry the section's colour as a stripe.

## Design (triaged 2026-10-02)

- **Flat, one level.** A section is a header placed before a row. It holds every row up to the
  next header. Rows above the first header have no section. Sections never nest.
- **Labels only.** There is no "play this section" or "skip to next section". Sections never
  change what plays or how it plays.
- **New term for CONTEXT.md:** **Section**, a named run of consecutive rows in a playlist that
  only organises it. _Avoid_: group, folder, set (CONTEXT.md already avoids *set* for playlist).
- **Colour from a fixed palette** of about 8 colours, picked from the header's menu. A new
  section takes the colour after the section above's, passing over the one below's. No icons,
  and no free colour picker.
- **Collapse is remembered** per section, across restarts. Since it is saved, the engine owns it:
  collapse and expand are engine intents, and the shell only renders what the engine reports.
- **Stored in the playlist's `.m3u8`.** Before the section's first row there is a
  `#EXTGRP:<name>` line, an existing extended-M3U directive, so ADR 0005 interoperability still
  holds. Directly above it, a faderio-only line holds the colour and the collapsed state, e.g.
  `#FADERIO-SECTION:colour=3;collapsed=1`. Other players ignore unknown `#` lines.
  `M3UPlaylist.Read` passes over both today, so older builds still open these files.
- **Engine intents**, one call each (the CLAUDE.md test): add a section at a row, rename it,
  recolour it, collapse or expand it, remove it (its rows join the section above), and move a
  section (its header and all its rows together). Dragging rows across a header moves them into
  the section where they are dropped. None of these can be undone, as with other queue edits
  (moving or removing rows): faderio's undo covers transition changes only (decided 2026-10-02).
- **Faderio's alone.** Sections are not sent to MusicBee's own playlists.

## Acceptance

- [ ] A playlist with Intro / Middle / End sections plays exactly as it would with no sections,
  including the transitions at section boundaries.
- [ ] Sections, their colours and collapsed states survive closing and reopening the playlist
  and restarting either host.
- [ ] A sectioned `.m3u8` still plays in another player, and a plain `.m3u8` opens with no sections.
- [ ] Add, rename, recolour, collapse, remove and move each save to the playlist at once.
- [ ] Both hosts show header rows, colour stripes and collapsing (smoke: Looks).

## Comments

Built 2026-10-02.

- **Engine.** `StartSection`, `RenameSection`, `RecolourSection`, `CollapseSection`,
  `RemoveSection` and `MoveSection(id, ±places)` each save at once. `Move` and the new
  `MoveAfter` put the moved rows into the section where they land. Added files join the section
  of the row they land before, or at the end the last row's. The view's rows carry `Section` and
  `StartsSection`, and a `SectionView` says whether it `CanMoveUp` / `CanMoveDown`. Section edits
  are refused while a playlist is still opening, so a load can never split a section.
- **File.** `#EXTGRP` plus `#FADERIO-SECTION:colour=N;collapsed=1`, recorded in ADR 0014.
  Another player's groups open and import as sections. Export writes sections unless shuffle
  is on.
- **Shell.** The queue list's own groups: the header shows a swatch, the name and a chevron, and
  the rows show a stripe. Rows above the first section sit under a blank header, because a
  grouped list puts every row in a group. Under shuffle no headers show, the stripes stay, and
  Collapse and Move are greyed out. The Section submenu is on the queue menu, and also opens for
  a right-clicked header. Rows dropped on the lower half of a section's last row end that
  section. Files dropped at the same place still start the next one.
- **Found in the MusicBee smoke.** MusicBee closing with groups shown resized the plugin's list
  after its columns had been disposed, and `FitTitle` threw. It now returns when the columns are
  gone.

