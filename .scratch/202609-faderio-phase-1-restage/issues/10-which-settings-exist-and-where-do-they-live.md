# Which settings exist, and where do they live?

Type: grilling
Status: open
Blocked by: 02, 03, 04, 05, 06

## Question

v2 issue 0013 persisted settings as JSON in the user profile: output mode, default adjacency
behaviour (hard cut, or a global default crossfade of N seconds), skip behaviour, ReplayGain
on/off, preview pre-roll, visualiser on/off; a `schemaVersion`; missing keys fall back to
defaults; a corrupt file warns and resets. v3 has launch arguments instead
(`--output=`, `--play`, `--edit-first`).

Decide, once tickets 02 to 06 have said which behaviours exist:

- **The list.** Which settings survive, which are new, which are dropped.
- **A default crossfade.** Whether an unauthored adjacency can play anything but a hard cut.
  This touches the glossary's **Hard cut** and the resolver.
- **Output mode changes.** ADR 0008 fixes the engine rate at launch. Does changing the output
  device or mode need a restart, or does the engine rebuild?
- **Storage.** A file beside `transitions.json`, its schema and corruption rule, and what
  happens to the launch arguments (kept for the smoke checklist, or removed).
- **The dialog.** What it shows; it is shell, so it adds Looks items to `docs/smoke.md`.
