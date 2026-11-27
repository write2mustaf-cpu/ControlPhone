# Changelog

All notable changes to ControlPhone. This file lists only work that is actually implemented and testable in this
repository — no planned features are listed as done.

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
