# 04: Overrides: resolved by the engine, authored in the panel

Status: ready-for-agent
Blocked by: 01

Decided in [D21, D22, D23](../../202609-faderio-interim-improvements/decisions.md), ADR 0005.
Glossary: **Transition override**.

## What happens today

`ITransitionStore` has `GetOverride / UpsertOverride / RemoveOverride(playlistName, …)` and
`TransitionResolver.Resolve(global, override, …)` merges per parameter — but
`PlaybackEngineBoundary.cs` and `PlaybackEnginePreview.cs` always pass `null`, and no UI
touches overrides.

## What to build

- **Resolution:** wherever the engine resolves an adjacency (boundary scheduling, preview,
  `CanTransitionNow`, the join in issue 01) it passes the open playlist's override for that
  pair. An override with no global transition resolves against the unauthored fallback
  (hard cut or default crossfade per settings) — a **playlist-only** transition (D22).
- **Authoring surface:** `Transition(Adjacency)` returns both layers: `Global` (spec or null)
  and `Override` (partial spec or null) and the `Effective` result. `AuthorOverride(Adjacency,
  partial)` and `UnauthorOverride(Adjacency)` join `Author` / `Unauthor`, returning the same
  refusal shape, validated against the *effective* transition (a fade cannot exceed the
  length, etc.). `IsAuthored` gains the layer.
- **Panel:** a two-way selector at the top, "Editing: ● Global ○ This playlist (*name*)".
  - Global mode: unchanged.
  - Playlist mode: each box shows the effective global value as grey placeholder text; typing
    makes that parameter part of the override; clearing a box drops it from the override. The
    curve combo gets a blank "(global)" entry. Capture and Nudge write into the override.
    **Save** → `AuthorOverride`; **Revert** reloads; **Remove override** → `UnauthorOverride`
    (replaces "Remove transition" in this mode). Preview plays the drafted effective result.
  - The mode is remembered for the session (D23): the row button and the menu open in the
    last mode. Switching modes with unsaved edits prompts Save/Discard/Cancel.
  - A heading line states the effective values when the two layers differ.

## Where the logic lives

- **Engine:** the resolver wiring, `Transition`'s layers, the two override commands and their
  validation against the effective transition.
- **Shell:** the selector, placeholders, the mode memory.

## Acceptance

- [ ] Author A→B globally at 10 s; in playlist P override the fade-out point to 8 s; play P →
      the fade begins at 8 s; play A→B in playlist Q → 10 s.
- [ ] Override only the fade-in duration; the other three fall through and follow later edits
      to the global.
- [ ] No global for C→D; override all four in P → P plays it; Q plays the unauthored fallback.
- [ ] Rename P → R: the override follows (issue 01).
- [ ] Remove override → P plays the global again.

## Tests

- **fake output:** each acceptance line renders through the chain with the right fade start.
- **unit:** validation against the effective transition; a partial override serialises only
  the set parameters.
- **form:** placeholder rendering, save/remove in each mode, mode memory.

## Smoke

- **Looks:** switch the selector to This playlist; the boxes show grey global values; type one;
  Save enables and "Remove override" is shown.

## Comments
