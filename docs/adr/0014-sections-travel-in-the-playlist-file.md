# Sections travel in the playlist file, as #EXTGRP with a faderio line above

Status: accepted (2026-10-02, phase 6 issue 09)

ADR 0005 keeps transitions out of the playlist file, in the store, so a faderio `.m3u8` stays an
ordinary playlist any player reads. Sections are different. A section belongs to one playlist
and is about its rows' order, so it lives in that playlist's file, not in the store.

## Decision

- **The group is `#EXTGRP:<name>`** on the line before the section's first entry. This is the
  extended-M3U group directive, so another player either ignores it or shows the group. Reading
  it, a `#EXTGRP` that repeats the group already in force continues that group, because some
  players write the group above every entry.
- **faderio's own state is one line above it:** `#FADERIO-SECTION:colour=N` with `;collapsed=1`
  when the section is collapsed. Other players pass over an unknown `#` line, so the file still
  plays everywhere. A file with no such line opens with colours chosen as a new section's are,
  expanded.
- **What is written is what is shown**, in the playlist's own order. That is the order shuffle
  returns to, so a shuffled queue saves its sections unchanged. A queue exported under shuffle
  writes no sections, since its order runs across them.

## Considered

- **A sidecar file next to the playlist.** This would keep the `.m3u8` pure, but a playlist
  renamed, copied or imported outside faderio would lose its sections.
- **The store.** It is keyed by tracks, not by rows, and a section is a run of rows.

## Consequences

- Importing another player's playlist keeps its groups as sections.
- Older faderio builds read these files: they pass over both lines, open the rows, and drop the
  sections at their next save.
