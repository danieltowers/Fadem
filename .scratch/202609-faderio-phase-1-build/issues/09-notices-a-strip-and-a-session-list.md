# 09: Notices: a strip and a session list

Status: resolved
Blocked by: —

Decided in [What happens when a file or the device fails?](../../202609-faderio-phase-1-restage/issues/05-what-happens-when-a-file-or-the-device-fails.md)
(Notices). Carries v2 0014's intent. Closes audit finding 13.

## What to build

- The status strip shows the **latest notice plus a count**.
- Clicking the strip opens **this session's notice list**.
- A repeated notice merges into its first one, with a count.
- There is no log file.
- **Nothing is swallowed:**
  - store write failures in `Author` and `Unauthor` become notices, so every command stays
    total;
  - the empty `catch (IOException)` in `Presenter.OpenTransitionStore` reports a notice.

## Where the logic lives

- **Engine:** notices are raised as today, and the store failures are caught under `Run`.
- **Shell:** the list is presentation of `IEngineSink.Notice` deliveries for this session.
  The merging and counting are shell formatting of delivered messages, holding no engine state.
  If a build session finds that merging needs engine knowledge, move it into the engine instead.

## Acceptance

- [x] Two notices in a row are both reachable in the list; the strip shows the latest and a
      count.
- [x] A notice repeated 50 times shows once, with ×50.
- [x] A store file made read-only makes `Author` raise a notice, not throw.
- [x] A corrupt transitions file moved aside raises a notice.

## Tests

- **fake:** `Author` and `Unauthor` with a failing store.
- **presenter:** the corrupt-store notice.
- **form:** the strip, the count, the merge and the list.

## Smoke

- **Looks:** the strip's count and the list are legible, and the list opens and closes cleanly.

## Comments

- Built 2026-09-24. The strip shows the latest notice, "×n" once it has repeated, and a link
  "n notices" at its right. Clicking either opens the list: time first delivered, the notice,
  its count, and the selected notice in full below it. Esc and the close box hide it; the next
  click brings back the same list, on the latest notice.
- **The count is the list's entries,** not deliveries: 50 repeats of one notice read "1 notice"
  and "… ×50". Merging is by exact text, so a notice carrying variable text (an exception
  message) only merges with an identical one.
- **The outcome of authoring is the engine's.** `Author` and `Unauthor` catch a store write
  failure under `Run` (the same exceptions `SaveAsPlaylist` catches) and raise "Not saved: …" or
  "Not removed: …". They also report success ("Saved a → b.", "Removed a → b."), and the editor
  no longer says "Saved" itself, which would otherwise have been followed by the failure.
  `JsonTransitionStore` keeps a change only once it is on disk, so "not saved" is true: the
  edit neither persists nor plays. `AtomicJson` removes its temp file when a write fails.
- **The corrupt store.** The empty catch hid a launch crash: when the corrupt file could not be
  moved aside, the fresh store reopened it and threw. Now that is a notice, and the fresh store
  is written to `transitions.json.recovered` beside the untouched file, as for a newer version.
  Store warnings are startup notices, not a message box.
- **A long notice no longer hides the strip's other items.** Sized to its text, the spring
  label pushed the count (and, before this, the add's Cancel) off the strip. `SpringLabel`
  asks for no width of its own; a form test pins it.
- **Open edge.** A store running from `.recovered` is used only while the corrupt file cannot be
  moved. Once a later launch moves it aside, the app starts a fresh `transitions.json` and what
  was authored into `.recovered` is left there unread. The warning names the file, but nothing
  merges it back.
- **Tests.** `StoreFailureTests` (6): a failing fake store for `Author` (three exception
  kinds) and `Unauthor`, the success notices, and a read-only store file that leaves the
  pending boundary as it was. A store test that a failed write changes nothing. Presenter tests
  for a corrupt file that cannot be moved, the merge and the count's wording. Five form tests:
  the strip and count, ×50, the open list following new notices (and a disposed list being
  replaced), no list before any notice, and a long notice keeping the count on the strip.
