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
- `--selfcheck` is the regression suite (164 checks as of 3.6.0): every new pure helper gets a `Check(...)` line, and
  CI runs it twice (build output + installed copy from the installer).

## Gotchas

- Git pushes from this environment are read-only for `write2mustaf-cpu/ControlPhone` (integration token, 403);
  commits must be pushed by the user or a token with `repo` + `workflow` scope.
- `.github/workflows/windows-release.yml` needs the `workflow` scope to be pushed.
- The repo is a shallow clone; `git fetch --unshallow` before history-dependent operations.
