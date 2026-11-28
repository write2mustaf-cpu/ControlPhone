# AGENTS.md

Repository notes for AI agents working on ControlPhone (multi-phone Android control center).

## Layout

- `native/ControlPhone/` — the product: WPF app on .NET 9 (`ControlPhone.exe`), all engines (ADB socket client,
  scrcpy-server sessions, FFmpeg decoding, device manager, group sync, recovery, OTG/LAN).
  - `Ai/` — provider manager, DPAPI secret storage, protocol client, tool registry, agent loop, chat window.
  - `Core/` — devices, ADB, settings store, i18n, operation log, support info, `SelfCheck.cs`, plus:
    - `Crash.cs` — global exception handlers, `data/logs/crash.log` (512 KB rotation, API keys stripped), technical report.
    - `SettingsSanitizer.cs` — one clamp/validate rule set for `config.json`, `ui.json`, `ai.json` and the import dialog;
      corrections are logged to the operation log. **Add new settings here too**, otherwise a hand-edited file wins.
    - `DeviceVideo.cs` — per-phone grid/viewer resolution, fps, bitrate (null override = global). Used by
      `DeviceManager.ThumbOptions/HqOptions`; read metas with `Store.MetaOrNull` (never `Store.Meta` in hot paths).
    - `ManualOrder` (`Core/ManualOrder.cs`) — user-chosen grid order: `Normalize(present, stored)` (stale dropped, new
      appended, no duplicates), `Move(order, id, delta)` (clamped), `MovePast(full, id, neighbour, delta)` for filtered
      grids. Persisted in `Store.Ui.Order`, sort mode `"manual"`.
    - `Typing` (`Core/Typing.cs`) — text delivery: session (Unicode via clipboard paste) when open, `adb shell input text`
      for plain ASCII only; `AdbArg` escapes spaces/metacharacters; `SendAsync` returns (ok, how it went) per phone;
      `Summary(text, results)` formats OK/PARTIAL/FAIL with one line per phone. Typing is the batch op `"text"`
      (`Actions.Ops`), so it gets the queue, per-phone cancel and `Retry` (payload carries text+enter).
    - `BatchTask` (`Core/Actions.cs`) — keeps `Payload` + `DeviceIds` so `Actions.Retry` can re-run the same operation on
      `FailedIds` only; `CancelDevice(id)` cancels one phone (`ShouldSkip`), `Stop()` cancels the queue.
    - `Shortcuts.cs` — shortcut table (`Key`+`ModifierKeys` → action id) with a pure `Match`, `Gesture` for the help
      dialog and `WorksWhileTyping` so keystrokes in a `TextBox` are never hijacked; wired in `MainWindow.InitShortcuts`.
    - `UserBackup.cs` — full user-data backup/restore ZIP: `backup.json` manifest + `data.json` (devices/groups/settings/UI
      prefs/AI settings) + `chats/<id>.json`; never contains API keys; `MergeInto` merges instead of overwriting.
    - `AiStream.cs` — SSE line parser + per-provider `StreamAccumulator` (text deltas, tool-call fragments by index /
      content block, usage); pure, so the self-check feeds it recorded chunks. `AiClient.ChatStreamAsync` returns
      `null` on any stream problem and `AiAgent` then falls back to the non-streamed `ChatAsync`.
    - `DeviceLog.cs` — per-phone log: 300-line memory ring + `data/logs/devices/<safe-id>.log` (256 KB, `.log.1`
      rotation); fed by the `PhoneDevice.Status` setter and by `Actions.RunBatch`; exposed to the AI as `device_log`.
    - `SafeMode.cs` — `SessionGuard` (data/logs/session.json: start/ready marks, consecutive-failure counter) and
      `SafeMode` (session-only light profile applied inside `DeviceVideo`, never written to config.json).
    - `Updates.cs` — GitHub `releases/latest` check: `ParseTag`/`IsNewer`/`Parse`/`ShouldNotify` are pure and covered by
      the self-check; nothing is downloaded or installed automatically.
  - `MainWindow.*.cs` — partials: grid/selection, dialogs, menus, AI host bridge, support dialog, settings export,
    per-phone video (`MainWindow.Video.cs`), update prompts (`MainWindow.Updates.cs`).
- `public/` + `server/` — legacy web build (Node + plain JS). Shares `public/js/lang/*.js` translations.
- `tools/setup.js` — downloads scrcpy/adb into `vendor/` (gitignored, required before building the app).
- `docs/` — user docs; `docs/ar/` Arabic guide.

## Release discipline (learned the hard way)

`<Version>` in `native/ControlPhone/ControlPhone.csproj` **is** the release number: the workflow reads it and names every
asset from it. A tag whose number differs from the csproj produces a release whose assets carry the *old* name — v3.29.0 was
published once with `-v3.28.0-` files because the bump step was skipped by a failing shell chain (`grep -c … && python …`).
The workflow now throws when `refs/tags/vX.Y.Z` and the csproj disagree, and `tools/package_manifests.py` refuses any file
under 5 MB or without `MZ`/`PK` magic, so a downloaded 404 page can never become a "verified" manifest hash.

Order: bump csproj → build → PR → CI green → merge → `git tag -a vX.Y.Z` **on that merge commit** → push tag → wait for the
release job → download and verify the assets → only then regenerate the Scoop bucket manifest. Verify `git show vX.Y.Z:...csproj`
before tagging, not the working tree.

## Build / verify

```bash
export PATH=/workspace/.dotnet:$PATH DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1   # SDK 9 in /workspace/.dotnet; ICU missing
dotnet build native/ControlPhone/ControlPhone.csproj -c Release -p:EnableWindowsTargeting=true
dotnet publish native/ControlPhone/ControlPhone.csproj -c Release -r win-x64 --self-contained true \
  -p:EnableWindowsTargeting=true -p:DebugType=none -o dist/ControlPhone
ControlPhone.exe --selfcheck        # Windows only: translations/RTL, DPAPI, AI store, 3 protocols, tools, support
npm run i18n:check                  # key coverage per language
```

`unzip` is not installed (use Python `zipfile`); `dotnet` is not on the default PATH.

## Conventions

- Source UI strings are **Vietnamese** (`I18n.T("Đóng")`); translations live in `public/js/lang/<code>.js`, copied to
  `lang/` in the output. `ar` is complete; other languages fall back to English for keys they lack.
- RTL: `I18n.FlowDirection` is applied to windows; technical inputs (URLs, commands, keys) use `I18n.ForceLtr`.
- API keys never touch the repo or logs: `AiSecret` (DPAPI) + `AiStore` (`data/ai/ai.json`), redacted in errors.
- New AI features must go through `AiToolHost` (schema + `Sensitive` flag) so the assistant stays auditable.
- Tool output sent back to the model is truncated (`AiAgent.TruncateForModel`) and declared untrusted in the system
  prompt (rule 11) — keep both when touching the agent loop.
- `--selfcheck` is the regression suite (404 checks as of 3.29.0): every new pure helper gets a `Check(...)` line, and
  CI runs it twice (build output + installed copy from the installer).

## "The app exits by itself" — read this first
1. `data/logs/startup.log` (`Core/StartupTrace.cs`) records every startup stage with elapsed ms: `begin` → `second-instance` →
   `pre-data` → `store` → `theme` → `shown` → `ready` → `exit`. The last line says where it stopped; **no `exit` line at all**
   means the process was killed (antivirus, driver, OOM), not shut down.
2. `ControlPhone.exe --doctor` writes `data/logs/doctor.txt`: version, OS, .NET, cores, RAM, whether the data folder is
   writable, whether FFmpeg loads, config errors, crash count, the startup marks. No API keys, no phone names.
3. `Core/CrashForensics.cs` reads Windows' own records — event log 1000 (Application Error: faulting module + exception code)
   and 1026 (.NET Runtime), `%LOCALAPPDATA%\CrashDumps\*ControlPhone*.dmp`, and `Get-MpThreatDetection` — filtered by
   `Relevant()` (ControlPhone, scrcpy-server, adb.exe). Shown in `--doctor` and saved as `logs/forensics.txt` in the bundle.
   A faulting module name (`igd10iumd64.dll`, `nvoglv64.dll`, an AV shim) is the answer when nothing managed was thrown.
4. `Core/Crash.cs` writes `data/logs/crash.log`; every entry ends with the last 8 startup marks. `DiagBundle` carries both.
5. Silent exits that were real bugs and are fixed: a second instance with no reachable window (now signals the first instance
   through the `ControlPhone.Native.ShowWindow` named event, and says so if nobody answers); an exception thrown while building
   `MainWindow` (dialog + `Shutdown(1)` instead of a headless process); an exception on a background thread
   (`AppDomain.UnhandledException` shows a dialog when `IsTerminating`).

## Data files
- Settings: `data/config.json` (shared with the web build), UI prefs: `data/native-ui.json` — read `Store.UiFilePath`,
  never hardcode the name. The old `ui.json` name was a real bug in the diagnostics bundle.

## Translations
- Source language is Vietnamese: `I18n.T("<Vietnamese>")`; packs live in `public/js/lang/<code>.js` and are loaded from
  `lang/<code>.js` next to the exe. Arabic and English are complete (912 keys each); de/fr/zh-CN/zh-TW still miss ~167.
- `node tools/i18n-native.js --strict` runs in CI and fails when ar or en miss a string. The extractor skips
  `Core/SelfCheck.cs` (test fixtures) and interpolation fragments — do not "translate" those.
- Self-check asserts the whole ar pack: no value left in Vietnamese, every value has Arabic script, `{n}` placeholders kept.

## Performance layer
- `Video/StreamStats` is the only place that counts frames/bytes per stream (fps over a 1 s window, kbps stored ×100 so a
  rounding step cannot zero it, EWMA decode time, dropped frames). `H264Decoder.Stats` updates it from `Feed`.
- `Core/AdaptiveQuality.QualityGovernor` is the pure decision engine: 2 consecutive stressed samples lower bitrate → fps →
  resolution, 4 healthy samples raise them back, 4 s/6 s cooldown, hard floor/ceiling, even resolutions. Tested with an
  injected clock — never assert on wall-clock time.
- `Core/AutoQuality` runs it every 2 s for phones whose grid stream is live, stores the learned level in
  `DeviceMeta.AutoMaxSize/AutoFps/AutoBitRate` and reopens only that phone's stream when it has to go lower.
- `DeviceVideo.ThumbBase` = what the user asked for, `DeviceVideo.Thumb` = base clamped by the learned level. A manual
  override always wins over a learned value; never write learned values into the manual override fields.
- A stream that stops sending frames must stop claiming bandwidth: `StreamSnapshot.Kbps` is 0 after 1.5 s of silence and
  `AgeMs` reports how old the last frame is. `H264Decoder` counts pre-key-frame skips with `Skipped()`, never `Dropped()`.
  The governor ignores stalled streams (`AgeMs > 3000`) — a sleeping phone must not drag its quality down.
- `Core/LoadBudget` is the only place that decides what a *group* of streams may cost: `PixelsPerSecond(size, fps)` is the
  cost unit, `SharePerStream(streams, cores)` the per-stream share, `Fit()` the reduction order (long side to 480 → fps →
  size, floor 240p/5 fps). `ActiveShare` is refreshed by the 2 s tick and applied inside `DeviceVideo.Thumb` and as the
  governor's ceiling, so a new stream opens already inside the budget. `Fit` also scales the bitrate with the pixel ratio
  (floor `MinBitRate` = 120 kbps) — cutting the picture while keeping 600 kbps wastes USB on 300-phone rigs. Self-cleaning:
  `SetActive(0, …)` = no cap; `MainWindow.NotifyBudget` tells the user once when the grid gets lighter.
- `Store.Flush`/`FlushUi` run on a `Timer` thread: their bodies are wrapped in try/catch and record `Store.SaveError`, because
  an exception there would terminate the process, not just fail a save.
- UI: `PhoneDevice.PerfText` (fps · kbps · decode ms) in the tile header, refreshed by `RaisePerf()` so 300 tiles do not
  re-render every 2 s. AI tools: `get_stream_stats`, `diagnose_device`, `set_stream_quality`.

## AI conversation window
- `AiAgent.Window(messages, max=60)` is the only thing sent to a provider: it keeps the last N turns, starts at a `user`
  turn, drops unanswered tool calls and orphan tool results. Trimming blindly breaks OpenAI (400 on `role: tool` without
  its `tool_calls`) and Anthropic (first message must be `user`) — a chat then fails forever until deleted.

## Design system
- Every colour comes from `Theme.Palette` (key → dark/light hex) and is applied as `DynamicResource`, so switching theme is a
  value swap; `Theme.Apply` also rebuilds the `AccentGrad` gradient. Never hardcode a hex in XAML or code.
- Tokens: radii `Theme.RCtl/RCard/RPill` (used via `{x:Static local:Theme.*}`), font scale `FsCap < FsBody < FsH2 < FsH1`,
  `Theme.ThemeInvariant` lists colours that are intentionally identical in both themes (screen, scrims on the phone image).
- Self-check enforces WCAG: body text ≥ 7:1, muted/status colours ≥ 4.5:1, white on the gradient ends and on toasts ≥ 4.5:1,
  and that every `Theme.Required` key exists. Adding a colour means adding a check-worthy contrast pair.
- Controls are templated in `App.xaml`: buttons tint on hover with an overlay (works on gradients too) and show a keyboard
  focus ring; shadows are limited to tooltips, popups, primary buttons and the hovered tile so the grid stays cheap.

## AI device targeting
- `AiToolHost.MatchOne(devices, token)` is the only place a device token becomes a phone: id, number (`3` or `#3`),
  exact name, then a unique partial name. Ambiguous or unknown tokens are refused and `Resolve` throws — sensitive tools
  must never run on a phone the user did not name.

## PC keyboard mode
- `PcKeyboard` installs an invisible IME. Turning it off must never leave the phone without an on-screen keyboard:
  `PcKeyboard.RestoreOrder(prev, available)` drops a saved IME that no longer exists and falls through to any other
  installed keyboard. `DisableAsync` keeps our IME enabled when every attempt fails.

## Release assets
- `Updates.PickAssets` chooses the installer and the runnable ZIP from a release's asset list **by name** (`Setup…`,
  `…-win64.zip`), skipping `manifests`/`symbols`/`source` archives and zero-size entries. Never pick "the last .zip" —
  every release since 3.15 also carries `ControlPhone-v<ver>-manifests.zip` (~2 KB).

## Packaging
- `tools/package_manifests.py` — generates winget (3 YAML) + Scoop (1 JSON) + `manifests.json` from the real release
  files; computes SHA256, refuses renamed/missing artefacts, re-reads what it wrote. CI runs it after the installer step
  and attaches `ControlPhone-v<ver>-manifests.zip` to the release.
- Scoop bucket repo: `write2mustaf-cpu/scoop-controlphone` (`bucket/controlphone.json`). Keep `persist: "data"` — the app
  stores config, device logs and API keys in `<app>\data`.
- CI cannot run `winget validate` or `scoop install` (neither exists on the runner) — state that instead of implying it.

## Gotchas

- Git pushes from this environment are read-only for `write2mustaf-cpu/ControlPhone` (integration token, 403);
  commits must be pushed by the user or a token with `repo` + `workflow` scope.
- `.github/workflows/windows-release.yml` needs the `workflow` scope to be pushed.
- The repo is a shallow clone; `git fetch --unshallow` before history-dependent operations.
