# 10: Each half of the editor names its track

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "make it clearer which section
referes to which track by putting the track name above the relevant section".

## What to build

The editor's title names the boundary ("a → b"), but its fields do not say which track each
belongs to: the fade-out point and duration are the outgoing track's (a), the fade-in point and
duration the incoming track's (b). Put the track's title above each half:

- above **Fade-out point**: a's title;
- above **Fade-in point**: b's title.

Each is a single line in Dim (or Text, matching the editor's other labels), cut short with "…"
and whole in its tooltip, using `Presenter.TitleOf`. With no boundary selected both are empty and
take no height they did not have to.

## Acceptance

- [ ] With a boundary open, the outgoing track's title is above the fade-out fields and the
      incoming track's above the fade-in fields, in both hosts.
- [ ] A long title ends in "…", whole in its tooltip.
- [ ] The editor still fits the App's default window (1240 × 660) and the MusicBee sidebar without
      a new scroll (memory: editor panel height budget); issue 09 frees a row toward this.
- [ ] A shell test checks the two labels follow the open boundary, and empty with none.
