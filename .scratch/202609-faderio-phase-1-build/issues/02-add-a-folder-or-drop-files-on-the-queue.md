# 02: Add a folder, or drop files on the queue

Status: ready-for-agent
Blocked by: 01

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md) (Adding).
Carries v2 0003's intent ("add folder").

## What to build

`Add` accepts folders:

- Enumerated recursively.
- Only `.wav`, `.flac` and `.mp3` are taken; everything else is ignored silently.
- Ordered by path, folder by folder, with natural sort ("2 - …" before "10 - …").

New in the shell:

- An **Add folder…** button.
- Dropping files or folders from Explorer onto the queue calls the same `Add`.
- Duplicates are allowed as separate rows.

## Where the logic lives

- **Engine:** enumeration and ordering, inside `Add`. The shell never lists a directory.
- **Shell:** a folder picker and a drop handler, each passing paths straight to `Add`.

## Acceptance

- [ ] A folder with nested album folders adds every promised file, recursively, in natural
      path order.
- [ ] Images, logs and other extensions are ignored without a notice. A promised extension that
      fails the probe is a rejection.
- [ ] A drop of mixed files and folders adds them in the order dropped.
- [ ] The same file added twice gives two rows.

## Tests

- **fake:** enumeration, extension filter, natural order (a fixture tree with "2" and "10") and
  duplicates.
- **form:** the drop handler and the button reach `Add`.

## Smoke

- **Looks:** Add folder… on a folder of several albums lists albums in order, tracks "1" to "10" in
  natural order.
