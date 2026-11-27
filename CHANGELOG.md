# Changelog

All notable changes to ControlPhone. This file lists only work that is actually implemented and testable in this
repository — no planned features are listed as done.

## 3.7.0 — A separate log for every phone

### Per-phone log (new, requested in the original brief)
- Right-click a phone → **Phone log…**: its own history — every `Status` transition (`offline → connecting → online`,
  `→ error · reason`) plus the result of every operation that ran on that phone (`push: lỗi · …`, `apk: ok`), including
  operations skipped because a batch was stopped.
- Persisted per phone in `data/logs/devices/<safe-id>.log` (256 KB, rotated to `.log.1`), so the history survives a
  restart; the in-memory ring keeps the last 300 lines for the dialog. Wi-Fi ids contain `:`, so file names are sanitised
  (`192.168.1.5:5555` → `192.168.1.5_5555`) and a hostile/empty id becomes `device`.
- Dialog actions: **Refresh**, **Copy**, **Open file**, **Clear log** (with confirmation, clears memory and files).
- New AI tool **`device_log`** (`device`, `lines` 1–200): the assistant can read a phone's own history instead of
  guessing, which is what the "diagnose a phone that keeps dropping" case needed.

### Verification
- Self-check 164 → **175** checks: entries stay per phone, ordering and error flag, `Format`, file append, 300-line cap,
  file-name sanitising for Wi-Fi/hostile ids, 256 KB rotation to `.log.1`, and clearing memory + files.
- CI builds, runs the self-check twice (build + installed copy), builds the installer and uploads both assets.

## 3.6.0 — Safe mode when startup keeps failing

### Safe mode (new)
- The app now records each start and each "window is up" moment in `data/logs/session.json`. If a start never becomes
  ready — the classic driver/codec crash while the first frames are decoded — the counter goes up, and **two in a row put
  the session in safe mode**.
- Safe mode is deliberately boring: grid stream capped at 240p / 5 fps / 300 kbps, viewer at 640p / 10 fps / 1 Mbps,
  per-phone overrides ignored, hardware encoders not preferred. It never makes a light profile heavier (caps are one-way).
- **Nothing is written to your settings**: the profile is applied when the scrcpy options are built, so `config.json`
  keeps your values and the next normal start is exactly as before. A toast and a note in *Links & support* say what is
  happening and how to leave it (close and reopen).
- A graceful exit marks the session ready, so a normal close never leaves the counter set; a missing/corrupt
  `session.json` is treated as a first run instead of an error.

### Verification
- Self-check 146 → **164** checks: counter rules (first run, graceful last run, unfinished last run, growing counter),
  threshold behaviour, a real `session.json` round-trip in a temp folder (begin ×4 → ready → begin resets → clean), and the
  video-profile rules (caps applied, override ignored, never heavier, restored after leaving safe mode).
- CI builds, runs the self-check twice (build + installed copy), builds the installer and uploads both assets.

## 3.5.0 — One-click diagnostics bundle for support

### Diagnostics bundle (new)
- *Links & support → Diagnostics bundle → Export…* writes a single ZIP: `versions.txt` (app, Windows, .NET, ADB path,
  FFmpeg, render tier, CPU, data folder), `devices.txt` (number, label, transport, state, Android — **no serial numbers**),
  `oplog.txt` (last 200 operations), `logs/crash.log` (+ `.1`), `settings/config.json` and `settings/ui.json`.
- **Privacy by construction**: `data/ai/ai.json` is never read or included, and every text file goes through
  `AiSecret.Redact` again before being written (`sk-…`, `AIza…`, `Bearer …`, `?key=` masked). A README inside the ZIP
  states exactly what is and is not inside, so the user knows what they are sending.
- Robust: an existing file is replaced, a missing log/config is skipped instead of failing the export, a file larger than
  2 MB is truncated with a marker, and a read error is recorded as `<name>.error.txt` rather than aborting.
- Errors and successes both go to the operation log, and the export opens the containing folder.

### Verification
- Self-check 137 → **146** checks: the bundle is built for real into a temp folder and read back — expected entries exist,
  `ai.json` is absent, an injected `sk-SELFTESTSECRET…` string is masked, the device list and versions are present, the
  default file name is safe, rebuilding over an existing file works, and an empty device list is explained.
- CI builds, runs the self-check twice (build + installed copy), builds the installer and uploads both assets.

## 3.4.0 — Stop a running batch operation

### Stop button for every batch (new)
- Every batch (install APK, push file, shell, screenshot, battery, Wi-Fi switch, reboot…) now shows a ⏹ button on its
  progress card, and the results window gets a *Stop* button while the task is running.
- Stopping is **queue-level and per-task**: phones that have not started yet are skipped and reported as *stopped*
  (`⏸`), the phone currently being worked on finishes its current command, and other tasks are untouched.
- Three counts are kept apart — ✓ done, ✗ failed, ⏸ stopped — in the card, in the results window and in the operation
  log line (`push: 12/40 ok, 1 failed, 27 stopped`). *Select the failed phones* ignores stopped phones, so a stopped
  batch cannot be mistaken for a broken one.
- `BatchTask.Stop()` is idempotent and safe after the task finished; `TaskResult` carries a `Skipped` flag so the UI never
  has to guess.

### Verification
- Self-check 130 → **137** checks: stop cancels the token and is idempotent, a finished task cannot be stopped, status
  text switches between running/stopping/done, skipped results are flagged and normal results are not.
- CI builds, runs the self-check twice (build + installed copy), builds the installer and uploads both assets.

## 3.3.0 — Update check, and repo notes for future work

### Update check (new)
- *⋯ → Check for updates…* reads `releases/latest` from the project's GitHub API, compares it with the running version
  and shows the release notes, the asset sizes and two working buttons: **Open release page** and **Download installer**.
- Nothing is downloaded or installed by the app: both buttons open the browser, and the dialog says so.
- Automatic check at most once per 24 hours (timestamp in `ui.json`), can be switched off with a checkbox in
  *Links & support*; *Skip this version* keeps a release quiet until the next one. Both preferences are validated by
  `SettingsSanitizer`, so a hand-edited `ui.json` cannot mute or spam the check.
- Failures are reported, not swallowed: HTTP errors, timeouts, unreadable JSON and a missing numeric tag all end in a
  toast (manual check) or one operation-log line (automatic check). Drafts and pre-releases are never offered.
- Version comparison is numeric (`3.10.0 > 3.9.0`, `4.0 > 3.99.99`), not string-based.

### Repository notes
- `AGENTS.md` now documents `Crash`, `SettingsSanitizer`, `DeviceVideo` and `Updates`, the rule that new settings must be
  added to the sanitizer, `Store.MetaOrNull` in hot paths, and the expectation that every pure helper gets a self-check.
- README and the Arabic guide describe the update flow.

### Verification
- Self-check 108 → **130** checks: tag parsing (including pre-release rejection), numeric version comparison, release JSON
  parsing with assets, draft/pre-release rejection, broken responses, skip logic, and the new preference validation.
- CI builds, runs the self-check twice (build + installed copy) and uploads the installer and ZIP.

## 3.2.0 — Per-phone video settings, settings that cannot break the app

### Per-phone video settings (new)
- Right-click a phone → *Per-phone video settings*: grid resolution, grid fps, viewer resolution, viewer fps and viewer
  bitrate for **that one phone**. Empty field = the global value, so nothing changes unless you ask for it.
- Applied where the stream is actually started (`ThumbOptions`/`HqOptions`), not in the UI only: the phone's own
  session is restarted, the other phones keep running.
- Out-of-range or zero overrides are ignored/clamped (`DeviceVideo`), so a typo cannot open a stream with 0 fps.
- The menu marks phones that have their own values; *Use global settings* clears them in one click.

### Settings are validated on load, not only on import
- New `SettingsSanitizer`, used by `config.json`, `ui.json`, `ai.json` **and** the import dialog (one rule set instead of
  duplicated inline clamps): fps 1–60 / 5–60, grid size 160–1920, viewer size 320–2560, bitrates 30k–20M / 500k–40M,
  parallel start 1–30, parallel jobs 1–50, transport ∈ {usb, wifi}, LAN ports normalised to valid numbers
  (`,`-separated, deduplicated, fallback 5555), invalid screenshot folder replaced, unknown theme/sort/filter reset,
  unknown language → system default, shell history capped at 50, AI tool rounds 1–20, extra system prompt ≤ 4000 chars.
- Every correction is written to the operation log (`Đã sửa cài đặt không hợp lệ: …`) instead of failing silently, and
  device names/numbers are never touched.
- `Store.MetaOrNull` — a read-only device-meta lookup, so the stream path never creates empty records.

### Verification
- Self-check 85 → **108** checks: clamping of every numeric range, transport/port/path/theme/sort/filter/language
  fallbacks, "valid settings are left alone", port normalisation, AI limits, and the per-phone video precedence rules
  (override wins, empty inherits, 0/negative ignored, out-of-range clamped, no meta record created by a lookup).
- Build, self-check and installer smoke test run in CI on Windows; the release assets are checked after upload.

## 3.1.0 — Stability, error reporting and assistant hardening

### Crash handling and support reports (new)
- Every unexpected error is now caught (UI thread, background threads, unobserved tasks), written to
  `data\logs\crash.log` with type, message, stack, app version, Windows and .NET version, and also recorded in the
  operation log. The file rotates at 512 KB (`crash.log.1`), so it can never grow without limit.
- API keys (`sk-…`, `AIza…`, `?key=…`, `Bearer …`) are removed from the text before it is written — the log can be
  sent to support as-is.
- A UI-thread error no longer closes the session: the dialog explains what happened, offers to open the log folder and
  the app keeps controlling the phones.
- New *Technical report* card in **Links & support**: opens the log folder and copies a ready-made report
  (version, Windows/.NET, data folder, ADB diagnostics, last 40 operations, keys redacted).

### Assistant hardening
- Tool output is declared untrusted in the system prompt (rule 11): text from a phone screen, a shell result, a log or
  a file can never act as a new instruction, which closes the prompt-injection path through device content.
- Long tool results are trimmed before they are sent back to the model (first 6,000 + last 1,500 characters, with the
  omitted length stated), so a single `dumpsys` cannot blow the context window or the bill.
- Token usage and turn duration are shown under every answer (`1,284 tokens (1,020 in / 264 out) · 4.2 s`), and the
  provider card accumulates requests/tokens per provider from the provider's own counters (`data/ai/ai.json`).

### Fixes
- The *Links & support* card titles and descriptions were not translated (they were shown in Vietnamese in every
  language). They now go through the translation layer.
- Translation gaps closed: German, French, Simplified and Traditional Chinese were each missing 5 strings
  (support links, Sham Cash). All six languages now report **0 missing keys** (`npm run i18n:check`).

### Verification
- Self-check grew from 68 to **85** checks, adding: crash text has type/message/stack and hides keys, crash log is
  written and rotates, long tool output is truncated (head/tail kept), usage adds up and is written to `ai.json`,
  the usage line formatting, and the untrusted-output rule in the system prompt. Runs in CI on Windows with the
  installed copy as well.

## 3.0.1 — Windows installer

### Installer (new)
- Real Windows installer built with Inno Setup 6 (`packaging/ControlPhone.iss`): per-user install into
  `%LOCALAPPDATA%\Programs\ControlPhone`, no administrator rights needed, upgrade-in-place, Start Menu group with
  the two guides, optional desktop shortcut, optional "launch after install", proper uninstaller entry in
  *Apps & features*.
- The setup wizard is bilingual (English + Arabic with right-to-left layout) and accepts `/VERYSILENT`,
  `/DIR=`, `/LOG=` for unattended deployment on a farm of PCs.
- Your data survives uninstall and upgrade: `data\` (phone numbers, names, groups, settings, AI providers and chats)
  is never deleted by the uninstaller; only `logs\` is cleaned.
- The release now ships two files: `ControlPhone-Setup-v3.0.1-win64.exe` (installer) and
  `ControlPhone-v3.0.1-win64.zip` (portable, unchanged layout).

### Verification (GitHub Actions, windows-latest)
- The installer is built in CI, then installed silently into a clean folder, the installed copy is checked for
  `ControlPhone.exe`, `lang\ar.js`, `scrcpy\adb.exe`, `scrcpy\scrcpy-server` and `docs\AI-ASSISTANT.md`,
  `--selfcheck` is run **from the installed location**, and the uninstaller is run and verified to remove the app.

## 3.0.0 — Windows Edition with Arabic UI and multi-provider AI

### Arabic interface and full RTL (new)
- Arabic (`ar`) added as a first-class UI language next to Vietnamese, English, Chinese (Simplified/Traditional),
  German and French — 714 translated strings, including every dialog, menu, toast, error message and tooltip.
- Real right-to-left layout: the whole application mirrors (`FlowDirection` on every window and dialog), while
  technical fields (URLs, ADB commands, API keys, file paths) stay left-to-right so numbers and commands stay readable.
- Language is stored in `data/native-ui.json` and restored on every start; switching language restarts the app
  automatically so every window is rebuilt in the new direction.
- The web build (1.x) gets the same Arabic translation and `dir="rtl"` support, including CSS rules for the sidebar,
  mono fields and inputs.

### Multi-provider AI subsystem (new)
- Central provider manager for **Google Gemini**, **OpenAI**, **Anthropic Claude** and any **OpenAI-compatible**
  service (OpenRouter, DeepSeek, Groq, local Ollama, custom base URL). Presets fill base URL and model; everything is
  editable, and new providers/models can be added at any time.
- Per provider: name, protocol, base URL, model, max answer length, temperature, timeout, default flag, "test
  connection", "fetch model list" (from the provider's `/models` endpoint), last success / last error.
- API keys: entered by the user, encrypted with **Windows DPAPI** (`CryptProtectData`) for the current Windows
  account, stored only in `data/ai/ai.json`, never logged, never shown in full, never sent anywhere except the
  provider the user picked. "Delete all AI data" removes providers, keys and chat history.
- Protocol layer converts one internal message/tool model to OpenAI `chat/completions`, Gemini
  `:generateContent` and Anthropic `messages` (system prompt, tool calls, tool results, usage tokens), with retry on
  429/5xx, timeouts and cancellation. Keys are redacted from every error message.

### Built-in assistant that actually operates the app (new)
- The assistant is wired to the real application through a tool registry (24 tools): list devices, device details,
  groups, assign/rename, selection, stream control (restart/pause/resume/zoom/fit), device keys, typing text, open URL,
  app control, APK install, file push, ADB shell, Wi-Fi connect, LAN scan, screenshots, read/change settings,
  diagnostics, logs, in-app guide, support section, AI settings.
- Sensitive tools (shell, install, uninstall/clear, push file, typing, URL, keys, settings) require explicit user
  approval with the target phones and parameters shown first. Tools never default to "all phones": the current
  selection is used, and "all" must be requested explicitly.
- The system prompt includes live device state, the current selection, the UI language and strict safety rules; the
  assistant answers in the user's language (Arabic, English, Vietnamese…) and never invents device state.
- Chat window: conversation history (create/delete/switch), provider and model picker, markdown-lite rendering,
  per-tool activity cards with result details, stop button, error retry, and Arabic/RTL layout.

### "Our links & support" section (new)
- Official contact card in Settings and the ⋯ menu: email `mwings740@gmail.com`, Telegram channel
  `https://t.me/+SiOMr4JAU8thZDFk`, Telegram group `https://t.me/+ZXt8aHvs5ok2ZjM0`, Facebook page
  `https://www.facebook.com/share/1DuqpyXuTa/` and the Sham Cash support ID `963793dbb5e910b81683981b2c8fd9f6`.
- Working buttons: open each link, write an email, copy the email / each link / the Sham Cash ID / all links, with a
  confirmation toast. The app never transfers money and never asks for financial data.

### Settings, data and diagnostics
- Settings dialog now groups: language, AI providers, support, export/import, grid quality, large view, connection,
  performance, storage, data folder, root/ADB behaviour, OTG/LAN, privacy notes.
- Settings export/import (`JSON`) covers settings and interface preferences only — API keys are excluded by design
  and must be re-entered on a new PC.
- New in-memory operation log (connections, batch results, errors) used by the assistant, plus a `--selfcheck` mode
  that verifies translations/RTL, DPAPI, provider storage, protocol request building/parsing, the tool registry and
  the support data without any phone attached.

### Build and release
- GitHub Actions workflow `Windows build & release` builds the self-contained Windows package on `windows-latest`,
  runs `ControlPhone.exe --selfcheck`, packs `ControlPhone-v<version>-win64.zip` and publishes it as a release asset
  on tags.
- Version 3.0.0; the original engine stack (ADB socket client, scrcpy-server sessions, FFmpeg decoding, device
  manager, group sync, recovery support, OTG/LAN) is unchanged and still the base of the application.

---

## 2.2.5 and earlier

See the git history and the previous README for the 2.x native app and the 1.x web version.
