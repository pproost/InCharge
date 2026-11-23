# InCharge — project context

Android companion app for interval training cues that relay to a Fitbit Charge 6 (the stock Fitbit app can't do custom interval notifications). Owner: Peter Proost. UI text in English; conversation with the owner in Dutch.

## Stack
- .NET MAUI Blazor Hybrid, `net10.0-android` only — `src/InCharge.Mobile/InCharge.Mobile.csproj`, solution `InCharge.slnx`.
- Notifications via `Plugin.LocalNotification` 14.1.1 (+ `.Core` 1.1.1). Persistence via `Preferences` (JSON per key).
- The earlier PWA (Blazor WASM / GitHub Pages) was dropped: browser timers stop when the screen locks.

## HealthCoach sync (in progress)
- InCharge will sync with the HealthCoach server on Azure: upload executed sessions, import Claude's planned sessions. Handover: `C:\Users\pproost\source\private\ILikeTheWayYouMove\HANDOVER-incharge-backend.md`.
- **`docs/contract.md` is the contract** (version 1); HealthCoach copies it. Model changes that cross the wire must be reflected there first.
- Agreed order: 1. contract (done) · 2. model extensions without network (done: block `Label` ≤20 chars shown as "Move! Zone 2" in the cue, `SessionStructure` snapshot + `Source`/`SessionId`/`PlanId`/`UpdatedAt` on log entries, `Source`/`PlanId`/`PlannedDate`/`Note` on `AdvancedSession`, log times are `DateTimeOffset` read via `LenientDateTimeOffsetConverter` so old offset-less entries still load) · 3. sync basics (done, see below) · 4. plan import + coach list in Advanced (done, see below) · 5. one-time history upload.
- Server: `https://healthcoachcyg45.azurewebsites.net` (repo `ILikeTheWayYouMove`, Azure). The device token is in Peter's password manager; he enters it on the phone, never via Claude.
- `Services/CoachSyncService.cs`: state in Preferences `incharge.coachSync` (Enabled, ServerUrl, AuthFailed, LastSyncAt, LastError, `Outbox`), token in SecureStorage `incharge.coachToken`. `ActivityLogService.EntryChanged` (Add/SaveChanges/Delete/`TouchType`) queues an outbox item (one per id, latest wins) only while sync is enabled. Sync triggers: new change, app launch (MainLayout), `App.OnResume`, network back, "Sync now". Batches of 50; 401 sets AuthFailed (stops until URL/token change), 400 drops the batch with LastError, 5xx/offline retries later. "Test" calls `GET /plans` read-only.
- Renaming/re-iconing/deleting a type in Settings calls `LogService.TouchType`, so those sessions are uploaded again.
- Plan import (step 4): each sync round = upload outbox → `GET /plans?since=<PlanCursor>` → `POST /plans/{id}/delivered` for `PendingDelivered`. New plans (not in `ImportedPlanIds`, not already a session with that `PlanId`) become `AdvancedSession`s with `Source = coach` via `AdvancedSessionService.AddImported` on the main thread; types by name via `ActivityTypeService.FindOrCreate`; `UseMinutes` when all durations are whole minutes. A 400 on the cursor resets it. `AdvancedSessionService` now caches one shared list (the page and the import must not overwrite each other).
- Advanced list: "From your coach" (sorted by `PlannedDate`, Today highlighted, ✓ Done when a log entry has that `PlanId`, hidden a week after the planned day, note shown) above "My sessions". The editor shows the coach note. The page triggers a sync on open.
- Rules: offline-first, the app wins (imported/deleted plan ids are never re-imported), uploads idempotent on log id with `updatedAt`, deletes as tombstones.

## Pages & services
- `Pages/BasicInterval.razor` (`/`): duration unit, move/rest durations, repeats, activity type, optional warm-up/cool-down. With cool-down on, the trailing Rest is skipped.
- `Pages/AdvancedInterval.razor` (`/advanced`): saved named sessions → groups (repeat count) of Move/Rest blocks, optional warm-up/cool-down. `BuildRunBlocks` flattens to a run list (also used for the list summary).
- `Pages/ActivityLog.razor` (`/log`): stat tiles (week + trend, weekly streak, move time, sessions), type filter chips, by-type breakdown, stacked daily bar chart (7/30 days), 12-week heatmap, move/rest split, records, history with delete. Charts are plain CSS/flex, no library.
- `Services/ActivityLogService.cs`: log entries (Preferences key `incharge.activityLog`) plus `ActivityRecorder`, which both interval pages use to track start/pauses and log a run only when it fully completes (automatically, or via "Save to log" on the completion screen when the AutoSaveToLog setting is off).
- `Services/ActivityTypeService.cs`: user-managed activity types (name + emoji icon, seeded with Cycling/Running/Walking once), managed in Settings. Log entries and Advanced sessions store a `TypeId`; `Resolve` skips deleted types. The completion screen (`Shared/CompletionLogPanel.razor`) lets you pick the type, defaulting to the session's type, then the last one picked.
- "Finish now" (flag button in the running overlay) ends a run early and logs only the time actually done (`FinishedEarly`).
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
