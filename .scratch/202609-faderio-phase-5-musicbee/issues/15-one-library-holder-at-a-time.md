# 15: One library holder at a time

Status: resolved
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 4, ADR
0012). Staged at the verdict; specified below (2026-10-01) before it was built.

## What to build

One host holds a library at a time: while standalone faderio has a library open, the plugin
refuses to open it, and the reverse, each saying which host holds it. Today both can open the
same folder and write its store, playlists and settings under each other.

The holding belongs to the library, in Core, so both hosts get it from one source. A crashed
host must not leave the library held for ever.

## Implementation decisions

- **The hold is a file held open.** A host that takes a library opens
  `<library>/.faderio/host.lock` for writing, shared for reading only, with the OS told to
  delete it when the last handle closes, and keeps it open while the library is open. The
  file's text is the host's name as `CONTEXT.md` names it, "faderio" or "MusicBee". The OS
  closes a crashed or killed host's handles and deletes the file with them, so a dead host
  never holds a library: there is no timestamp to age and no process id to check. A file
  left by some other means (a copy of a library, say) is not held by anyone, so the
  exclusive open succeeds and the file is this host's.
- **Refusal.** A host whose exclusive open is refused reads the file (shared for reading) for
  the holder's name and opens the library **unavailable**, as a gone or unwritable folder
  opens today: no store, authoring refused, playback allowed, and the one notice "The library
  *X* could not be opened (it is open in MusicBee). Nothing is saved until a library opens;
  what is queued still plays." A name that cannot be read says "another host". The title and
  the host line read "*X* (could not be opened)" as they do for any unavailable library; the
  notice names the holder.
- **Who holds.** The engine, for the host that composes it: `PlaybackEngine`'s library
  constructor gains `host`, the name this engine holds libraries under, and holds the library
  it is given and every library `OpenLibrary` opens. Opening another library lets go of the
  one left before taking the new, so the same folder opened again is kept, not refused by its
  own holder. `Dispose` lets go. An engine with no host name (the renderer's, the tests')
  holds nothing, and `Library.Open` on its own holds nothing: the transition manager's render
  and a test's authoring read a library another engine holds.
- **What the second host sees on reopening** (issue 11's comment): a host reads the stores
  once, at open. With one holder at a time there is no second host to miss an edit
  mid-session; a host that opens the library after the other let it go reads what that host
  saved, the open playlist and its last transition edit included. A refused library is tried
  again when the user opens it again (Open library…, or the plugin's Configure and Save,
  whose same-folder check passes an unavailable library through), and at the next launch.
- **Both hosts** pass their name: standalone faderio's composition root "faderio", the
  plugin's `Host` "MusicBee". Nothing else in either shell changes.
- **Vocabulary.** **Held** enters `CONTEXT.md` under **Host**.

## Testing decisions

- **In process, on net8.0 and net48 x86** (`LibraryHolderTests`, Core): a lock file held open
  by someone with no name in it refuses as "another host". Two engines on one
  folder under different host names. The second's library is unavailable with the notice
  naming the first; its Add and Play still play; its authoring is refused. The first's
  `Dispose` frees the folder: the second's `OpenLibrary` of it then opens available. The
  first's `OpenLibrary` of another folder frees the one left. The same folder opened again
  by its holder stays open. An engine with no host name, and `Library.Open` alone, hold
  nothing. A `host.lock` left behind with no handle on it does not block, and is this host's
  after. The file is gone once the holder lets go.
- **Across processes, net8.0 only** (`LibraryHolderAcrossProcessesTests`, on
  `ChildProcess`, which net48 cannot run): a child holds the library as "MusicBee" and
  reports; the parent's engine is refused with the notice naming MusicBee; the child is
  killed; the parent opens the library available. This is the crash proof.
- **The plugin** (`PluginTests`): with a library held as "faderio" by a test engine, the
  plugin starts with the notice naming faderio, and once that engine is disposed, Configure
  and Save on the same folder open it.
- The MusicBee **Looks** half gains an item: with standalone faderio open on the smoke
  library, MusicBee starts with a box "The library library could not be opened (it is open in
  faderio)…" and the strip shows it; faderio closed and Configure… then Save on the same
  folder opens it; the reverse, standalone faderio launched on a library MusicBee holds,
  says "it is open in MusicBee" in its strip.

## Acceptance criteria

- [x] A library held by one host is refused by the other with a notice naming the holder.
- [x] A host that crashed or was killed does not block the next open.
- [x] Tests cover it on net8.0 and net48 x86.

## Comments

- **Seen in issue 11's Sounds run (2026-10-01):** the engine reads the library's transition
  store once at open, so an edit saved by one host is not heard by the other until it reopens
  the library (MusicBee needed a restart to play standalone faderio's 1 s fade-out). With one
  holder at a time this cannot happen mid-session; the spec here should say what the second
  host sees on reopening.
- **Resolved 2026-10-01.** Built as specified above.
  - **Core:** `Library.Hold(host)` opens `.faderio/host.lock` for writing, shared for reading
    only, deleted by the OS with the last handle (`DeleteOnClose`), and writes the host's
    name; a sharing or lock violation reads the holder's name and opens the library
    unavailable with "it is open in *X*" ("another host" when there is no name to read).
    `Library.Dispose` lets go. `PlaybackEngine`'s library constructor takes `host`; the
    engine holds the library it is given and each one `OpenLibrary` opens, letting go of the
    one left only after its last playlist write, and `Dispose` lets go. Standalone faderio's
    root passes "faderio", the plugin's `Host` "MusicBee"; the renderer and the tests pass
    nothing and hold nothing. **holds** is under **Host** in `CONTEXT.md`.
  - **Tests:** `LibraryHolderTests` on both targets (refused with the notice and still
    playing, authoring refused; freed by Dispose; freed by opening another; the same folder
    kept; no host name and `Library.Open` alone hold nothing; a stale file taken; a nameless
    holder as "another host"; a gone folder not held). `LibraryHolderAcrossProcessesTests`
    on net8.0 only, through a new `ChildProcess.Start` that leaves a child running: the
    child holds as MusicBee, the parent is refused naming it, the child is killed, the
    parent opens. `PluginTests.Holder`: refused at start naming faderio, opened by
    Configure and Save once faderio lets go. Mutation: the release on `OpenLibrary` removed
    fails two tests.
  - **Found on the way:** `NAudioAudioOutput.Stop` read `_player` twice, and the render
    thread's own close nulled it between the two under the full suite's load (one
    `ReleasingTheDeviceTests` failure, a null reference out of Stop). It reads the player
    once now.
  - **The MusicBee Looks half** on the portable MusicBee (shared, the view in the right
    sidebar), standalone faderio from the same build on the smoke library:
    - ✅ faderio open first: `host.lock` reads "faderio". MusicBee starts with a box owned by
      it, "The library library could not be opened (it is open in faderio). Nothing is saved
      until a library opens; what is queued still plays.", the strip the same with **1
      notice**, the host line "library (could not be opened) · WASAPI shared at 48000 Hz on
      Speakers (C-Media(R) Audio)", the queue empty, the file still "faderio".
    - ✅ faderio closed: the file is gone. Configure… (the dialog on the same folder), OK,
      Preferences' Save: the queue reads tone-a-440, tone-b-660, the host line "library ·
      …", no new box, the file "MusicBee".
    - ✅ The reverse: faderio launched on the library MusicBee holds reads "faderio — library
      (could not be opened) · …" in its title and "The library library could not be opened
      (it is open in MusicBee). …" in its strip, no box; MusicBee closed, Open library… on
      the folder: "Untitled — faderio — library · …", the two rows, the file "faderio";
      faderio closed, no file.
    - The plugin's earlier items were not re-walked: the change in the plugin is the host
      name it passes.
  - **Review (2026-10-01).** Both axes found the one defect: `OpenLibrary` let go of the
    library left before writing its last playlist, a gap another host could take; the
    release now follows the write. Taken too: the holder's handle shares reading only (a
    shared delete would let the file be unlinked from under it), the engine's `Hold` named
    for what it does, the stores read once in the constructor, the "another host" test, the
    plugin test's holder disposed by `using`, the test csproj's line endings left as they
    were. Recorded, not changed: `Library.Open` still makes `.faderio/` and can move a
    corrupt store aside before the hold is refused (the holder did the same at its own
    open); a holder that lets go while a reader still has the file leaves it delete-pending
    for that instant, refused as access denied, taken by the next open; a lock file that
    cannot be made at all (not a holder's) opens the library unavailable with the error,
    since faderio could not write there anyway; the host names are literals at the two
    roots. The title and host line keep "(could not be opened)" for a held library; the
    notice names the holder, as the issue asked.
