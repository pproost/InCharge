# InCharge — project context

Android companion app for interval training cues that relay to a Fitbit Charge 6 (the stock Fitbit app can't do custom interval notifications). Owner: Peter Proost. UI text in English; conversation with the owner in Dutch.

## Stack
- .NET MAUI Blazor Hybrid, `net10.0-android` only — `src/InCharge.Mobile/InCharge.Mobile.csproj`, solution `InCharge.slnx`.
- Notifications via `Plugin.LocalNotification` 14.1.1 (+ `.Core` 1.1.1). Persistence via `Preferences` (JSON per key).
- The earlier PWA (Blazor WASM / GitHub Pages) was dropped: browser timers stop when the screen locks.

## Pages & services
- `Pages/BasicInterval.razor` (`/`): duration unit, move/rest durations, repeats, optional warm-up/cool-down. With cool-down on, the trailing Rest is skipped.
- `Pages/AdvancedInterval.razor` (`/advanced`): saved named sessions → groups (repeat count) of Move/Rest blocks, optional warm-up/cool-down. `BuildRunBlocks` flattens to a run list (also used for the list summary).
- `Pages/ActivityLog.razor` (`/log`): stat tiles (week + trend, weekly streak, move time, sessions), stacked daily bar chart (7/30 days), 12-week heatmap, move/rest split, records, history with delete. Charts are plain CSS/flex, no library.
- `Services/ActivityLogService.cs`: log entries (Preferences key `incharge.activityLog`) plus `ActivityRecorder`, which both interval pages use to track start/pauses and log a run only when it fully completes (automatically, or via "Save to log" on the completion screen when the AutoSaveToLog setting is off).
- `Layout/MainLayout.razor`: remembers the last page (`/`, `/advanced`, `/log`; never `/settings`) and reopens it on launch.
- `Pages/Settings.razor` (`/settings`): grouped sections Notifications / Activity / Data (Data is where export/import will go).
- `Services/NotificationService.cs`: one notification per phase acts as both cue and timer. `StartPhaseAsync` posts it under a new id (so the phone/watch buzz; the previous one is cancelled when "only latest" is on), `UpdateTimerAsync` refreshes it silently every second (remaining time in the title + progress bar), `ShowPausedAsync` while paused, `ClearTimerAsync` on stop/complete, `NotifyAsync` for plain one-off messages (completion, test). Not marked Ongoing on purpose: Fitbit tends to skip ongoing notifications.
- `Services/AdvancedSessionService.cs`: migrates old flat `Blocks` sessions into a single group.
- Both pages duplicate the timer-loop pattern deliberately (PeriodicTimer, render before the completion `break`).

## Build / deploy
- Always build **Release** (Debug uses Fast Deployment and crashes when sideloaded):
  `dotnet build src/InCharge.Mobile/InCharge.Mobile.csproj -c Release -f net10.0-android`
- APK: `src/InCharge.Mobile/bin/Release/net10.0-android/com.pproost.incharge-Signed.apk`
- adb lives in `C:\Users\pproost\android-platform-tools\platform-tools` (not on PATH). In Git Bash set `MSYS_NO_PATHCONV=1` for `/sdcard/...` paths.
- Plugin API questions: `ildasm` at `C:\Program Files (x86)\Microsoft SDKs\Windows\v10.0A\bin\NETFX 4.8 Tools\x64\ildasm.exe`, run on the DLLs in `~/.nuget/packages/plugin.localnotification*`.
- Repo: https://github.com/pproost/InCharge (push to `main`).

## Working agreements
- For small UI changes the owner tests manually — build, install, commit, push; skip the adb UI walkthrough.
- WebView content isn't in the accessibility tree; adb taps use screenshot coordinates (× ~1.19), and the layout shifts when the keyboard opens — re-screenshot before each tap.

## Known open items
- No Android foreground service yet (works while the app process stays alive; revisit if long sessions get killed).
- Hardware BACK inside the session editor navigates away and discards unsaved edits.
