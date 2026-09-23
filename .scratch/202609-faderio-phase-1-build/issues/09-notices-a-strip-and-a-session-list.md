# 09: Notices: a strip and a session list

Status: ready-for-agent
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

- [ ] Two notices in a row are both reachable in the list; the strip shows the latest and a
      count.
- [ ] A notice repeated 50 times shows once, with ×50.
- [ ] A store file made read-only makes `Author` raise a notice, not throw.
- [ ] A corrupt transitions file moved aside raises a notice.

## Tests

- **fake:** `Author` and `Unauthor` with a failing store.
- **presenter:** the corrupt-store notice.
- **form:** the strip, the count, the merge and the list.

## Smoke

- **Looks:** the strip's count and the list are legible, and the list opens and closes cleanly.
