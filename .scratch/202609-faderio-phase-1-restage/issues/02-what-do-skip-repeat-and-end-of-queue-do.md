# What do skip, repeat and end-of-queue do?

Type: grilling
Status: open
Blocked by: 01

## Question

v2 issue 0008 settled these rules against v2's engine; decide which hold on v3's seam, using
ticket 01's audit for what the engine does today:

- **Skip during a transition.** Abandon the in-flight transition and start the next adjacency
  with its own, or let the outgoing fade finish first? v2 had both, behind a setting.
- **Skip and Previous at all.** What Previous does mid-track versus near its start.
- **Repeat-all.** The wrap boundary as a real adjacency (last → first).
- **Repeat-one.** Fire an explicitly authored self-adjacency (A → A), or restart plainly.
- **Shuffle.** v2 said transitions fire per adjacency as encountered; confirm, and say what
  the editor's "this → next" means under shuffle.
- **End of queue.** The authored end-of-queue fade-out (`FindForOutgoing`), cut from the slice.
- **Audible time.** Whether queue advance and the end-of-track signal follow the playhead or
  the decode position (ADR 0001).

Each rule must be one engine call per user intent (placement test 1).
