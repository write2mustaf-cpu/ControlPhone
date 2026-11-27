# AGENTS.md

Repository notes for AI agents working on ControlPhone (multi-phone Android control center).

## Layout

- `native/ControlPhone/` — the product: WPF app on .NET 9 (`ControlPhone.exe`), all engines (ADB socket client,
  scrcpy-server sessions, FFmpeg decoding, device manager, group sync, recovery, OTG/LAN).
  - `Ai/` — provider manager, DPAPI secret storage, protocol client, tool registry, agent loop, chat window.
  - `Core/` — devices, ADB, settings store, i18n, operation log, support info, `SelfCheck.cs`.
  - `MainWindow.*.cs` — partials: grid/selection, dialogs, menus, AI host bridge, support dialog, settings export.
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

## Gotchas

- Git pushes from this environment are read-only for `write2mustaf-cpu/ControlPhone` (integration token, 403);
  commits must be pushed by the user or a token with `repo` + `workflow` scope.
- `.github/workflows/windows-release.yml` needs the `workflow` scope to be pushed.
- The repo is a shallow clone; `git fetch --unshallow` before history-dependent operations.
