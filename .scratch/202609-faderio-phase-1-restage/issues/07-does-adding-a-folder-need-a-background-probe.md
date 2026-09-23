# Does adding a folder need a background probe?

Type: task
Status: open
Blocked by:

## Question

Nothing to decide until it is measured. Adding a folder probes every file's header to issue
its `Length` (ADR 0010), and anything the matrix forces onto a frame scan reads the whole file
at add time. Ticket 14 of the predecessor found no MP3 row needs a scan, and FLAC pre-scans on
open (about 250 ms for the longest library file), not at add time.

Measure the add-time probe over a real library folder: wall time for the largest album folder
and for the whole library (903 files), and the slowest single file. Leave it behind as a
permanent test with a time budget, per the standing rule in `CLAUDE.md`; point it at checked-in
fixtures if the library cannot be a test input, and record the library numbers here.

The answer is the numbers plus a verdict: synchronous add is fine, or folder add needs a
background probe with the queue filling in as files resolve. Agent alone.
