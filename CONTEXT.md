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
The collection of all tracks known to the player, from which playlists are built.

**Playlist**:
A named, saved, ordered sequence of tracks.
_Avoid_: Album, set

**Queue**:
The runtime playback sequence — what the player will play next. Playlists are loaded into the
queue; the queue itself is never saved.
_Avoid_: Up next, playback list

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

**Transition now**:
The user action of starting the current adjacency's transition immediately, instead of at its
fade-out point. Only the fade-out point moves, to the playhead; everything else plays as
authored. It targets the same adjacency a skip would.
_Avoid_: Mix now, crossfade on skip

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
A queued track whose file can no longer be read when the player comes to play it: deleted,
moved, truncated or on a drive that has gone. The player passes over it with a notice and keeps
its row, trying it again next time.
_Avoid_: Broken, missing, rejected, skipped (a skip is the user's action)

**Probe**:
Reading a file's header when it is added to decide whether it is promised or rejected. The
probe decides before the file is queued and never after.
_Avoid_: Validate, scan, check

### Output

**Clip**:
Output the listener hears beyond full scale, attributed to what caused it: the adjacency whose
transition was in flight, or else the attended track. Clips are latched until the user clears
them.
_Avoid_: Overload, distortion, peak