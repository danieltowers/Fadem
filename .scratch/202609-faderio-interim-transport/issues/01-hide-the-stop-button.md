# 01: Hide the stop button

Status: ready-for-agent
Blocked by: —

Decided in [D3](../../202609-faderio-interim-improvements/decisions.md).

## What to build

- The ⏹ button is removed from the transport row (`MainForm.cs`, the 9-column
  `TableLayoutPanel`). The remaining controls close the gap; nothing else in the row moves.
- `engine.Stop()` is **not** touched, not deprecated, not tested less. It is still what the
  engine does at launch, after the queue ends and when a library is switched (next spec).
- `PlayerView.Transport == Transport.Stopped` still exists and still occurs.

## Where the logic lives

- **Shell only.** One control removed, one grid column fewer.

## Acceptance

- [ ] No ⏹ anywhere in the window.
- [ ] `Stop()` and its tests are unchanged in the diff.
- [ ] The Looks half of `docs/smoke.md` no longer names ⏹; its note that "UI Automation
      matches ⏹ and ⏸ as the same name" is updated to say only ⏸ exists.

## Tests

- **form:** the transport row's button names no longer include the stop glyph.

## Smoke

- **Looks:** the transport row reads ⏮ ▶ ⏭ ⤳ 🔀 🔁, seek bar, time.

## Comments
