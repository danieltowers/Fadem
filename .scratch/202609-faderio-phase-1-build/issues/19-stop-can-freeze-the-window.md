# 19: Stop can freeze the window

Status: resolved
Blocked by: —

Found during the Looks smoke run for [issue 03](03-open-and-save-playlists.md) on 2026-09-23. Not
caused by it: neither `Stop` nor the output changed there.

## What happened

WASAPI shared, playing the 440 Hz test tone at 0:03. A UI Automation invoke of ⏹ timed out, and
the window stopped updating: the readout stayed at `0:03 / -0:17`, the play button kept its
pause glyph, and no timer render happened for minutes. `WM_NULL` was still answered in 0 ms and
the process reported Responding, but every UI Automation query timed out. The process had to
be killed. Seen once; not reproduced.

## Suspected cause (from reading the code, not from a stack)

`PlaybackEngine.Stop` calls `_output.Stop()` inside `Run`, so under the engine lock.
`NAudioAudioOutput.Stop` then calls `IWavePlayer.Stop()`, and `WasapiOut.Stop` joins its
playback thread. If that thread has already passed `CountingSource.Read`'s stopped check and is
waiting on the engine lock inside `EngineFrameSource.Read`, neither thread can proceed. The
join on the STA UI thread still pumps COM messages, which fits `WM_NULL` being answered while the
timer and UI Automation are not.

`CountingSource.InRead` protects only the case where `Stop` is called from the render thread.

## To decide

- Whether the player is stopped outside the engine lock (the engine records the stop under it,
  the output is told after `Run` releases it), or `Read` stops taking the lock once a stop is
  requested.
- A test: the fake output cannot show this, since it has no second thread joining. A test
  against `NAudioAudioOutput` is out of scope for phase 1 (spec), so this may stay a smoke item:
  press ⏹ repeatedly while playing and confirm the window keeps rendering.

## Comments

- 2026-09-24, during the Looks smoke run for [issue 04](04-remove-rows-and-clear-the-queue.md).
  The suspected cause is confirmed by `dotnet-stack` on a frozen window. **Clear queue → OK**
  while playing froze it:
  - The UI thread was in `MainForm.ClearQueue` → `PlaybackEngine.Clear` → `Run` (holding the
    lock) → `NAudioAudioOutput.Stop` → `WasapiOut.Stop` → `Thread.Join`.
  - The WASAPI `PlayThread` was in `WasapiOut.FillBuffer` → `CountingSource.Read` →
    `EngineFrameSource.Read` → `PlaybackEngine.Run`, waiting on the same lock.
  - As before, `WM_NULL` was answered in 0 ms and UI Automation timed out.
- There are now more ways in. Every `_output.Stop()` runs under the lock: `Stop`, `Clear`, and
  `FinishPlayback`, which is reached by removing the last playing row or the only one, and at
  the natural end of the queue.
- Trials on WASAPI shared, one 12 s tone playing for 2 s, then the action, then 1.5 s to check
  the stacks:
  - **Delete on the only playing row:** froze 1 in 4.
  - **⏹:** froze 0 in 4.
  - **Clear → OK while playing:** froze 1 in 2.
  The race is real and not rare.
- Built 2026-09-24. **Decided:** the second option. Stopping the device outside the lock was
  rejected: `Publish` reads the output's transport under the lock, so a stop published before
  the device is told would show `Playing`, and a command slipping in between could be stopped by
  a late `Stop`.
  - Every device stop and the dispose go through `HaltOutput`, which raises a volatile flag for
    as long as the device takes to stop. `StopOutput` is the only way the device is stopped.
  - The render path takes the lock with `Monitor.TryEnter` in 1 ms steps and gives up while
    the flag is up. It returns zero frames, which is end of stream to NAudio as it already was
    for a stop from inside a render. The `IFrameSource.Read` contract now says so.
  - Nothing is rendered or counted, so the shared origin (ADR 0001) holds.
  - `Run` split into `Run` and `RunWithLockHeld`, so the render path enters the lock itself and
    the sink is still called after it is released.
- **Test:** the fake output could show it after all. `StoppingTheDeviceTests` binds a device
  whose `Stop` joins a real render thread parked just past its stopped check. Stop, Clear,
  removing the only playing row and ⏭ on the last row each froze before the fix (5 s timeout)
  and return after it. Dispose already returned, since `Read` checks `_disposed` first, but it
  is kept as a guard.
- **Device trials** with the script behind the comment above, on the fixed build:
  - ⏹ did not freeze in 10 trials.
  - Delete on the only playing row did not freeze in 9 clean trials. A tenth hit a UI
    Automation error and may not have exercised the path.
  - Clear → OK was not rerun.
  - A smoke item was added.
- **Not covered:** `OnOutputStopped` still takes the lock with a blocking `Run`. If the device
  raised `PlaybackStopped` with an error on its own thread during a halt, that could still wait.
  In the app the event is posted to the UI thread, since the WinForms context exists before
  `Initialise`. WaveOut's stop does not join, so it never froze. One block rendered after its
  reset can still inflate `Rendered` before an immediate Play, which predates this change.
- 2026-09-24, found during [issue 20](20-adding-a-large-folder-stalls-the-window.md)'s Looks
  run: UI Automation matches ⏹ and ⏸ as the same name, and ⏸ comes first in the tree. The
  device trials above found ⏹ by name, so "⏹ did not freeze in 10 trials" may have pressed
  play/pause instead. The Delete trials are unaffected. Issue 20's run pressed the real ⏹ (found
  by an ordinal comparison) three times while playing on WASAPI shared: each stopped, the window
  kept answering in 0 ms, ▶ showed and the readout reset. The ⏹ trials are worth rerunning.
  `docs/smoke.md` now says how to find the button.
