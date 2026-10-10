# faderio

A music player where the handoff between two tracks is fully author-controlled: not a global
"crossfade N seconds" setting, but per-boundary control over exactly where the outgoing track
starts fading, where the incoming track starts playing from, and how long each fade lasts.

## Language

### Tracks and sequencing

**Track**:
A single audio file known to the player. The unit of metadata and playback; it never owns a
transition by itself.

**Library**:
A folder the user chooses that is one self-contained faderio instance: its playlists, its
transitions and its listening settings live inside it, and its tracks may live anywhere on
disk. Opening a different library is opening a different world; nothing carries across.
_Avoid_: Workspace, collection, instance, profile

**Known libraries**:
The libraries that have opened on this machine for one host, most recently opened first. The
library chooser lists them; the user can take one off the list, which never touches its folder.
_Avoid_: Recent libraries (they never expire), library history

**Host**:
The program playing a library: faderio itself, or MusicBee through the plugin. Both hosts run
the same engine on the same library folder; each keeps its own machine settings. One host
**holds** a library at a time, from opening it until it lets go: the other host is refused it
with a notice naming the holder, and opens it once the holder has let go, a crashed one
included.
_Avoid_: Frontend, client, player (MusicBee's player never plays a library), lock (the file is
how, not what)

**Output device**:
The sound device a host's output opens, named as Windows names it. A host's **default** device
is what it opens with none chosen: Windows' default for faderio, **MusicBee's device** (what
MusicBee's own player plays on, followed as it changes) for the plugin. A chosen device that is
**not found** falls to the default with a notice, and is tried again at the next open.
_Avoid_: Endpoint (NAudio's word), sound card, DAC (one kind of device)

**Playlist**:
A named, ordered sequence of tracks belonging to a library. Its name is how the user refers
to it and how its overrides are found. Exactly one playlist is **open** at a time; edits to
the open playlist are kept without the user asking.
_Avoid_: Album, set, enhanced playlist

**Queue**:
The runtime playback sequence — what the player will play next. The queue mirrors the open
playlist: opening a playlist replaces the queue, and every change to the queue is a change to
that playlist. There is no queue apart from an open playlist.
_Avoid_: Up next, playback list, scratch queue

**Row**:
One place in the queue, holding one track. The same track can fill several rows. A row keeps
its identity when it moves; removing it removes that place, not the track.
_Avoid_: Entry, item, slot

**Section**:
A named run of consecutive rows in a playlist, in its own order, that only organises it: a
section never changes what plays or how. It has a colour from a fixed palette and is shown
collapsed or open, and the playlist keeps all three. Rows above the first section are in none.
A section with no rows left is gone.
_Avoid_: Group, folder, set

**Adjacency**:
An ordered pair of tracks — the **outgoing** and the **incoming** — at a boundary in a
sequence. Directional: A→B and B→A are distinct adjacencies. A track can be adjacent to
itself (A→A) when it repeats.
_Avoid_: Track pair, boundary, junction

**Skip**:
The user action of abandoning the current adjacency to advance immediately to the next one.
During a transition the current adjacency is the one in flight, so a skip lands on its
incoming track at full volume; it never passes over that track.

### Transitions

**Transition**:
The authored handoff between the two tracks of an adjacency. Belongs to the adjacency, never
to either track alone.
_Avoid_: Crossfade (that is one kind of transition), blend, mix

**Point**:
A position *within* a track, never a wall-clock time and never an offset into the overlap
window. A point is authored and stored; contrast **playhead**, which is live and transient.

**Fade-out point**:
The point in the outgoing track where its fade begins. Measured from the start or from the end
of the track.

**Fade-in point**:
The point in the incoming track that it begins playing from.

**Transition override**:
A playlist-scoped modification of a transition, specifying any subset of its parameters; every
parameter it omits falls through to the global transition for that adjacency.
_Avoid_: Local transition, exception

**Fade curve**:
The shape of the gain change across a fade. Canonically **linear** or **equal-power**.

**Crossfade**:
A transition whose outgoing and incoming fades overlap in time, so both tracks are audible at
once. One kind of transition, not a synonym for it.

**Hard cut**:
Switching from outgoing to incoming instantly, with no fade on either side.

**Default crossfade**:
What an unauthored adjacency plays when the user opts in: one crossfade of the same length at
every boundary nobody authored. It never applies to an authored transition, even one with blank
durations: those play the **blank duration**.
_Avoid_: Auto-crossfade, global transition, fallback transition

**Blank duration**:
What an authored transition's fade-out or fade-in duration plays when it is left blank: a
library setting, 5 s unless changed. It fills a blank only; a duration the user typed is kept. In
a playlist override, a blank duration falls through to the global transition's first, as every
omitted parameter does.
_Avoid_: Default fade, default crossfade

**Default curve**:
The fade curve a transition plays when none is set for it, the default crossfade's, and the one
the editor offers a new transition: a library setting, equal-power unless changed. A curve saved
with a transition is kept.
_Avoid_: Global curve

**Transition now**:
The user action of starting the current adjacency's transition immediately, instead of at its
fade-out point. Only the fade-out point moves, to the playhead; everything else plays as
authored. It targets the same adjacency a skip would.
_Avoid_: Mix now, crossfade on skip

**Cue transition**:
The user action of jumping the playing track to 5 s before its coming transition begins (its
fade-out point, or a hard cut's track end) and playing on, so the transition is heard for real
into the next track. Unlike a **Preview**, it moves the queue's own playback and does not stop
after the transition.
_Avoid_: Jump to fade, rehearse

**Preview**:
Hearing one adjacency's transition as currently drafted in the editor, saved or not: from
shortly before its fade-out point to shortly after its fades complete. The queue is held where
it was for the duration and is never moved by it.
_Avoid_: Audition, test play, render (a render writes a file)

### Position

**Voice**:
One track sounding, or about to sound, as part of the current playback. During a crossfade
there are two voices. Position is a property of a voice, never of the player.
_Avoid_: Channel, deck, stream

**Playhead**:
Where the listener is hearing *right now* within a voice's track. The only position the shell
ever sees, and the only one displayed or captured.
_Avoid_: Current position, play position, elapsed

**Decode position**:
How far the decoder has read ahead of the playhead. Engine-internal, used to schedule
transitions; it never crosses the seam into the shell.
_Avoid_: Position, read cursor, current position

**Audible time**:
The clock of what the listener is hearing, which the playhead reads. Everything the player shows
about its sound follows audible time. What the user asks for takes effect as soon as it can and
is heard shortly after; it is never held back to line up with what is showing.
_Avoid_: Real time, wall clock, decode time

**Capture**:
The user action of turning a playhead into a point — "author by ear". Captures the playhead,
never the decode position.
_Avoid_: Set from here, mark, grab

**Attended voice**:
The voice whose playhead the shell displays and the seek bar scrubs. During a crossfade it
remains the outgoing voice until that voice falls silent.

**Attended row**:
The row Play would resume or start: the attended voice's row while anything sounds, otherwise
the row the queue rests on. Marked in the queue in every transport state, so the marker
never means "sounding now".
_Avoid_: Playing row, current row, now playing

**Length**:
How long a track is, as the engine issues it: the span the playhead lives within, the value a
from-end point resolves against, and what the seek bar spans. There is exactly one length per
track; a length read from tags does not exist. "Duration" is reserved for fades.
_Avoid_: Duration (of a track), total time, tag length, decoded length

### Supported audio

**Promised**:
A kind of audio file (format, sample rate, bit depth, channel count together) that the player
guarantees to play and author correctly. Authoring correctly means an authored point plays
where it was placed, to the one tolerance the promise states. The guarantee is written down
and every line of it is proven; a kind of file that is not promised is rejected.
_Avoid_: Supported, compatible, works

**Rejected**:
A file the player refuses at the moment it is added, naming the property that disqualifies it.
Rejection never happens during playback: a queued track was promised when it was probed. If its
file later becomes unreadable, that is not a rejection.
_Avoid_: Unsupported, failed, skipped

**Unreadable**:
A queued track whose file cannot be opened when the player comes to play it: deleted, moved,
on a drive that has gone, or damaged so that none of it plays. The player passes over it with a
notice and keeps its row, trying it again next time. A file that plays but ends before its
length, such as one cut short, is not unreadable: it ends where its audio ends, with a notice.
_Avoid_: Broken, missing, rejected, skipped (a skip is the user's action)

**Gone**:
A track the transition store names whose file cannot be probed when the transition manager asks:
deleted, moved, or no longer promised. It is its path alone. Its transitions can be removed, not
changed. A queued track is never gone: it was probed when added, and a file lost since is unreadable.
The user reads "missing".
_Avoid_: Unreadable (a queued track's), deleted

**Probe**:
Reading a file's header when it is added to decide whether it is promised or rejected. The
probe decides before the file is queued and never after. A promised WMA is also decoded once, for
its length and its index (ADR 0016).
_Avoid_: Validate, scan, check

### Output

**Clip**:
Output the listener hears beyond full scale, attributed to what caused it: the adjacency whose
transition was in flight, or else the attended track. Clips are latched until the user clears
them.
_Avoid_: Overload, distortion, peak