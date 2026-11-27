# Changelog

All notable changes to ControlPhone. This file lists only work that is actually implemented and testable in this
repository — no planned features are listed as done.

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
