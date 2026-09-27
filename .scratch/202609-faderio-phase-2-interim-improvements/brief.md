## Improvements
1. playlist
    - state the current playlist name somewhwere
    - ability to rename the playlist directly in the player
    - auto-save playlist changes such as track order
    - list playlists on left had side as per music bee
2. library concept
    - ability to choose library folder
    - this becomes a faderio instance, all settings are stored there, including the global transtions data file
    - each library encapsulates its own transistions and settings
1. highlight the currently playing track when the stop button is pressed.
    - if i click on another track while one is paying, stop the current track then press play, i expect it to play the newly selected track but it plays the current one - see how musicbee works in this state
1. transition manager view
    - select a track and list all the adjacencies it is part of so we can preview each one, with their transitions and any overrides. This is useful for understanding how a track interacts with others in the playlist.
2. currenty transitions are global, how do i override them per playlist?
3. ability to customise the headers in the playlist, e.g. turn on other items like replay gain
4. button to far right of track to open the transition editor
5. buttons on the transition panel to increment or decrement the fade out/in points by 0.1s
6. a button in the main controls which advances the playhead by 5secs
7. ability to click anywhere in the track timeline to skip to that point. currently i can only move the playhead by clicking it and dragging it
8. ability to move the playhead while the track is stopped
9. if STOP is pressed the playhead jumps back to the beginning. pressing stop should leave the track playhead where it is

---

Grilled 2026-09-26 → [decisions.md](decisions.md), which feeds three specs:
[interim-transport](../202609-faderio-phase-2-interim-transport/spec.md) →
[library](../202609-faderio-phase-2-library/spec.md) →
[playlists](../202609-faderio-phase-2-playlists/spec.md).
