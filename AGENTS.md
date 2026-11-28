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
- `--selfcheck` is the regression suite (364 checks as of 3.23.0): every new pure helper gets a `Check(...)` line, and
  CI runs it twice (build output + installed copy from the installer).

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
