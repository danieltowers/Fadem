# 02: Add a folder, or drop files on the queue

Status: resolved
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

- [x] A folder with nested album folders adds every promised file, recursively, in natural
      path order.
- [x] Images, logs and other extensions are ignored without a notice. A promised extension that
      fails the probe is a rejection.
- [x] A drop of mixed files and folders adds them in the order dropped.
- [x] The same file added twice gives two rows.

## Tests

- **fake:** enumeration, extension filter, natural order (a fixture tree with "2" and "10") and
  duplicates.
- **form:** the drop handler and the button reach `Add`.

## Smoke

- **Looks:** Add folder… on a folder of several albums lists albums in order, tracks "1" to "10" in
  natural order.

## Comments

- Built 2026-09-23. The add worker lists a folder off the engine lock when it reaches it
  (`FolderContents`) and puts the files where the folder stood, so a mixed drop keeps its
  order. The order is folder by folder: a folder's own files first, then each subfolder, each level in natural order
  (`NaturalOrder`: digit runs as numbers of any length, text without case). This reading of
  "ordered by path, folder by folder" keeps each folder's files together; strict path order
  would put `Albums\…` before a loose `intro.mp3`. Hidden and system entries are left out, as
  Explorer hides them, and so are nested links and junctions, which could loop. A folder that
  cannot be read, at any depth, is a rejection with its reason, in its place. An empty folder
  adds nothing and says nothing. A folder counts as one path in the progress until it is listed,
  so the total grows as folders are reached, and Cancel does not interrupt a listing already
  under way (it discards the result). A file named on its own, dropped or picked, is
  still probed whatever its extension, so a dropped `.jpg` is rejected with a reason. Only
  folder contents are filtered silently.
- **Add folder…** picks one folder, since `FolderBrowserDialog` multi-select needs .NET 9.
  Dropping takes any number of folders. The dialogs sit behind `ChooseFiles` and `ChooseFolders`
  so form tests can reach `Add` through the buttons.
- Tests: `AddFoldersTests` (fake: recursion and natural order, extension filter, probe
  rejection, mixed order, duplicates, progress, empty folder, cancel while listing, a folder
  that cannot be listed, a nested folder denied by ACL, listing off the lock). Five form tests:
  Add folder, Add folder cancelled, Add files, drop of files and folders, and a drag of text
  refused.
- Looks run by the agent. The Explorer drag gesture itself was not performed; the drop handler
  is covered by the form test.
