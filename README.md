<div align="center">

# 📱 ControlPhone

**Free, open-source control center for many Android phones at once — view every screen live, control one phone or a whole group in sync.**

**Unlimited devices · No ads · No hidden background tasks · Optimized to be as light as possible**

[Tiếng Việt](README.vi.md) · English

UI languages: English · Tiếng Việt · **العربية (RTL)** · 简体中文 · 繁體中文 · Deutsch · Français

![ControlPhone](docs/screenshot-grid.jpg)

<sub>Phone screens in the screenshots are placeholders.</sub>

</div>

---

> **🆕 ControlPhone 2 — native Windows app.** From version 2.0 ControlPhone is a native Windows app (C# / WPF) with **every feature of 1.x**: no Node server, no port, no browser window, no `.bat`. Video is decoded with native **FFmpeg**, one decoding thread per phone. Download **`ControlPhone-v2.x-win64.zip`** from [Releases](https://github.com/mahiepit/ControlPhone/releases), extract and run **`ControlPhone.exe`** (self-contained, nothing to install). Your names, numbers and groups from 1.x are kept: copy the old `data` folder next to `ControlPhone.exe`. Source: [`native/`](native/). The web version (1.x, `ControlPhone.bat`) stays in this repository.

## 🆕 ControlPhone 3.0 — Arabic UI, multi-provider AI, built-in assistant

**What is new in 3.0** (native Windows app, still the same engines: ADB socket client, scrcpy-server sessions,
FFmpeg decoding, device manager, group sync, recovery, OTG/LAN):

- **Full Arabic interface with real RTL** — 714 translated strings, every window mirrored, technical fields (URLs,
  ADB commands, API keys) kept left-to-right. Language is saved and restored; switching restarts the app once.
- **Multi-provider AI** — Google Gemini, OpenAI, Anthropic Claude and any OpenAI-compatible service (OpenRouter,
  DeepSeek, Groq, local Ollama, custom URL). Per provider: model, max answer length, temperature, timeout, test
  connection, model list, default flag. **Your own API key**, encrypted with Windows DPAPI for your Windows account,
  never logged, never embedded in the build, never sent anywhere except the provider you choose.
- **An assistant that actually operates the app** — 24 tools wired to the real functions (devices, details, groups,
  selection, stream control, keys, typing, URLs, apps, APK install, file push, ADB shell, Wi-Fi connect, LAN scan,
  screenshots, settings, diagnostics, logs, guide). Sensitive tools ask for approval first and show the target phones.
  It answers in Arabic, English, Vietnamese… and never invents device state.
- **Our links & support** — official email, Telegram channel and group, Facebook page and the Sham Cash support ID,
  each with working open/copy buttons (Settings → *Our links & support*).
- **Settings export/import** (keys excluded by design), a new operation log, and `ControlPhone.exe --selfcheck`
  which verifies translations/RTL, DPAPI, provider storage, all three AI protocols, the tool registry and the support
  data without a phone attached. The GitHub workflow runs it on Windows before publishing a release.

Docs: [Windows install](docs/WINDOWS-INSTALL.md) · [AI providers & assistant](docs/AI-ASSISTANT.md) ·
[Support](docs/SUPPORT.md) · [الدليل العربي](docs/ar/README-ar.md) · [Changelog](CHANGELOG.md)

## ✨ Why ControlPhone

| | |
|---|---|
| ♾️ **Unlimited devices** | No device cap, no license key, no "pro" version. Designed for 100+ phones on one PC (tested live with 20 phones, benchmarked at 100 streams). |
| 🚫 **No ads, no tracking** | No ads, no telemetry, no account. The app talks only to your phones and your own PC (Internet is used once, to download scrcpy during setup). |
| 💤 **No hidden background tasks** | Nothing is installed as a service, nothing starts with Windows. Close the app window and ControlPhone stops. The ADB server is **left running** so other tools and scripts keep their phone connections (ControlPhone 2.0.10+; tick *Settings → Stop ADB when ControlPhone exits* if you want it stopped too). No app is installed on the phones either — the screen server (one small file in `/data/local/tmp`) runs only while connected. |
| 🪶 **Lightweight by design** | Hardware H.264 encoding on the phone, decoding in your browser (WebCodecs). Idle phones cost ~6 kbps each; phones that are off-screen or paused send nothing. |
| 🆓 **Free & open source** | MIT license. Use it, modify it, share it. |

## 🚀 Features

**Live screen grid**
- See every phone live in a resizable grid, "fit to screen" in one click.
- Light grid streams (360p–720p, adjustable) + a sharp large view (up to 1280px / 30 fps) for the phone you are working on.
- While a phone is enlarged (ControlPhone 2.2+), the grid is hidden and the other phones are neither decoded nor drawn — the PC only processes that one phone. Their sessions stay open, so synced control still reaches them and the grid is back at once when you close the large view (can be turned off in Settings).
- Only visible tiles are decoded; minimizing the window pauses everything.
- **Pause viewing** for one phone or a group (the phone stops streaming but stays controllable).
- **Show only these phones**: hide and pause every other phone, restore with one click.
- **Per-phone video settings (ControlPhone 3.2+)** — a weak phone next to a strong one in the same grid: right-click a
  phone → *Per-phone video settings* and give that one its own grid resolution/fps and viewer resolution/fps/bitrate.
  Empty field = global setting. Saving restarts only that phone's stream; a dot in the menu shows which phones have
  their own values.

**Control — directly on the small screens**
- Mouse = finger on any tile: tap, swipe, long-press. Middle click = Home, side button = Back, Shift + wheel = scroll, Alt + drag = pinch zoom.
- Type on the keyboard while hovering a phone; **Ctrl+V pastes your PC clipboard into all selected phones** (any language, emoji).
- **Click** = select one phone · **Ctrl+click** = add/remove to the group · drag a box to select many · Ctrl+A = all.
- **Group sync is automatic**: when 2+ phones are selected, anything you do on one of them is repeated on the whole group (F3 to pause sync).
- Turn the **physical screen off** while you keep viewing and controlling (saves battery; restored when you exit). Enabled by default.

**Right-click menu on any phone**

![Right-click menu](docs/screenshot-menu.jpg)

1. Zoom (large view of that phone; group sync still applies)
2. Install APK
3. Import file to the phone
4. Export file to the PC
5. ADB commands ▸ (device info, battery, storage, RAM, IP/Wi-Fi, current app, installed apps, show/hide navigation bar, Wi-Fi/data on/off, clear cache, close background apps, set battery to 100% / restore real battery, custom command, reboot)
6. Open network settings
7. Open Settings

**Batch tools (for all selected phones)** — every batch shows a progress card; the ⏹ button on the card (or *Stop* in the
results window) stops it: phones still in the queue are skipped and marked as stopped, the phone currently being worked on
finishes its current command. Results keep the three counts separately (✓ done, ✗ failed, ⏸ stopped), and *select the
failed phones* never picks up the stopped ones.
- Back / Home / Recents / Power / Volume / unlock / screen off-on
- Send text, paste, open URL, launch / stop / clear / uninstall apps
- Install APK (or drag & drop `.apk` files onto the window), push files, file browser (download / upload / delete)
- ADB shell (optionally as root) with per-phone results, batch screenshots
- Zoom view: one click copies a full-resolution screenshot to the PC clipboard — just paste it anywhere with Ctrl+V
- Rename, renumber, groups, filters, search, battery & temperature at a glance

**Connections**
- **USB** — detected instantly.
- **Wi-Fi** — USB → Wi-Fi in one click (and Wi-Fi → USB to switch back), connect by IP, LAN scan, Android 11+ pairing code. A phone connected by both USB and Wi-Fi appears once and fails over automatically; Wi-Fi phones reconnect automatically.
- **Two modes on launch (ControlPhone 2.1+)** — *Control All Devices* (the whole set of phones, grid + sync) or *Show only 1 phone*: pick a phone from the list and ControlPhone connects only to that phone and shows it large — the smoothest way to work on one phone. Switch any time from the top bar.
- **Root** — detected automatically; root shell, permanent ADB over Wi-Fi, and auto-installs this PC's ADB key into `/data/misc/adb/adb_keys` so rooted phones never ask "Allow USB debugging?" again (ideal for screenless box phones). The key is re-checked every time a phone connects and put back if Android removed it, and on Android 11+ the automatic 7-day revocation of USB debugging authorizations is turned off (`adb_allowed_connection_time = 0`). If a phone that runs Xiaowei's `xwdb` module (its own root adbd on Wi-Fi port 15555) still ends up waiting for "Allow USB debugging?", ControlPhone goes in through xwdb, puts the key back, reconnects USB and closes the leftover dialog — the phone is back in a few seconds without anyone tapping Allow. A new phone whose xwdb address is not known yet is found by scanning port 15555 on the PC's local networks and matching its USB serial.
- **Autoboot (rooted, Android 7+, ControlPhone 2.1.2+)** — box phones have no power button: a phone that powers off with the cable in (or sometimes after a reboot) stops on the charging screen and disappears from USB. ControlPhone installs `/system/etc/init/n9autoboot.rc` once per phone (skipped if already there, no reboot), so a phone that lands in charging mode boots straight into Android. Installs are logged to `data\logs\autoboot.log`.
- **OTG / LAN boxes (ControlPhone 2.2+, opt-in)** — phone-farm boxes that switch the phones to Ethernet ("OTG mode") leave no USB link to the PC. With *OTG/LAN mode* on (Settings), ControlPhone scans the PC's local networks every 20 s for phones with ADB over network (port 5555, configurable) and connects them by itself — plug a phone into the box and it appears, no IP to type and no scan button. *OTG/LAN: enable ADB over network* prepares the USB-connected phones first (rooted phones keep the port after a reboot, and rooted phones plugged in for the first time are prepared automatically). A phone reached over the network lands on the same tile and number it has over USB: ControlPhone reads its real USB serial (rooted phones), which also works on ROMs that clone `ro.serialno`. The scan follows the PC's network mask (up to /22), so a 200-phone farm on a /23 is covered. [`tools/mikrotik/farm-router.rsc`](tools/mikrotik/farm-router.rsc) is a one-shot MikroTik (RouterOS 7) config for such a farm: router 192.168.1.1, LAN 192.168.0.0/23 with a single DHCP server for all boxes, NAT to the ISP modem.
- **Recovery / TWRP (ControlPhone 2.2+)** — a phone that boots into recovery keeps its tile and number and shows the recovery screen, with touch, in the grid and in the large view. There is no Android in recovery, so ControlPhone pushes a small native helper (`native/recovery/cprec.c`) to `/tmp`: it reads the framebuffer and injects touch/keys. Boards without a screen have no touch device, so the helper creates a virtual one. *Reboot to Recovery (TWRP)* / *Reboot to Android* are in the tile menu. Framebuffer (fbdev) recoveries only for now.
- **OTG** — launches `scrcpy --otg` (keyboard/mouse over USB without USB debugging).
- Native scrcpy window for any phone as a fallback.

**Updates (ControlPhone 3.3+)**
- *⋯ → Check for updates…* asks GitHub for the newest release and shows what changed, the asset sizes and two buttons:
  open the release page or download the installer. Nothing is downloaded or installed behind your back.
- The check runs by itself at most once per 24 hours (can be switched off in *Links & support*), and *Skip this version*
  keeps it quiet until the next release.

## 📥 Install

### Option 1 — Windows installer (recommended)
1. Download `ControlPhone-Setup-vX.Y.Z-win64.exe` from [Releases](https://github.com/write2mustaf-cpu/ControlPhone/releases) and run it.
2. No administrator rights: it installs for your Windows account, creates Start Menu shortcuts (app + guides) and an
   uninstaller entry. The wizard is English **and Arabic (RTL)**. Your `data\` folder is kept on upgrade and uninstall.
3. Silent deployment: `ControlPhone-Setup-vX.Y.Z-win64.exe /VERYSILENT /DIR="D:\Tools\ControlPhone"`.

### Option 2 — Portable ZIP (nothing else to install)
1. Download `ControlPhone-vX.Y.Z-win64.zip` from [Releases](https://github.com/write2mustaf-cpu/ControlPhone/releases).
2. Extract it to a folder you can write to and start **`ControlPhone.exe`**.
3. Nothing else is needed: the .NET runtime, adb, FFmpeg and scrcpy-server are inside.

### Option 3 — Build from source (native app)
Requirements: Windows 10/11 x64, [.NET 9 SDK](https://dotnet.microsoft.com/download), [Node.js 18+](https://nodejs.org).
```bash
git clone https://github.com/write2mustaf-cpu/ControlPhone.git
cd ControlPhone
npm install
npm run setup          # downloads scrcpy 5.0 from the official release and verifies its SHA-256
dotnet publish native/ControlPhone/ControlPhone.csproj -c Release -r win-x64 --self-contained true -o dist/ControlPhone
dist\ControlPhone\ControlPhone.exe
```
`ControlPhone.exe --selfcheck` runs the built-in verification (translations/RTL, key encryption, AI protocols,
tool registry, support data) without any phone attached.

### Option 4 — Legacy web build (1.x)
The original browser version is still in this repository for reference (`npm start`, `ControlPhone.bat`). It has the
same phone control features but runs in a browser window through a local Node server; the native app (3.x) replaces it
and adds the AI assistant, the Arabic interface and multi-provider support.

### On each phone
Android 5.0 or newer (the minimum of scrcpy-server 5.0).

1. Enable **Developer options** → **USB debugging**.
2. Plug it in and tap **Allow** on the phone.

That's it — the phone appears in the grid within a few seconds. No app is installed on the phone.

## 🖱️ Quick reference

| Action | How |
|---|---|
| Select one phone | Click its tile header (or tap a phone outside the current group) |
| Build a group | Ctrl+click phones, drag a box, or Ctrl+A |
| Control the group | Just operate any phone of the group (sync is automatic, F3 to pause) |
| Large view | Right-click → Zoom, or double-click the tile header |
| Paste to all selected | Ctrl+V |
| Phone menu | Right-click the phone |
| Pause one phone / group | Right-click → Pause viewing |
| Focus on a few phones | Right-click → Show only these phones (purple button at the top to restore) |
| Change language | Language selector at the top right |
| Quality & options | ⚙ Settings |

## ⚡ Performance

Measured on a Windows PC (22 threads) with 20 phones running a 3D game (every phone at its 15 fps cap):

| | 20 phones (Balanced) | 20 phones (Ultra-light) | Estimated 100 phones (Ultra-light) |
|---|---|---|---|
| Total video bandwidth | 12 Mbps | 6.3 Mbps | ~32 Mbps |
| Browser (decode + draw) | 0.8 core | 0.6 core | ~3 cores |
| ControlPhone server | 0.1 core, ~100 MB RAM | 0.07 core | ~0.35 core |
| On the phone (scrcpy) | 1.5–3 % CPU | — | — |

Decoding benchmark: 100 streams × 15 fps (1,500 frames/s) decoded with the UI still at 60 fps.
Idle phones (static screen) cost about 6 kbps each.

**Mixed farms (ControlPhone 3.2+):** keep the grid light for the whole set and give the few phones that need it a
higher viewer resolution of their own, instead of raising the global level for all 100.

**Tips for 100 phones:** use the *Ultra-light* preset, spread the phones over several USB controllers (a single USB controller usually handles only a few dozen phones), and pause viewing for phones you don't need to watch.

## ❓ Troubleshooting

**The app closes while starting up?** ControlPhone 3.6+ notices when a start does not finish (twice in a row) and comes
up in **safe mode**: lightest video profile (240p/5 fps in the grid, 640p/10 fps in the large view), per-phone overrides
ignored, hardware encoders not preferred. Nothing is written to your settings, and closing and reopening the app returns
to normal — so a graphics driver problem can no longer lock you out of the window.


A corrupted or hand-edited `config.json` no longer breaks the app: ControlPhone 3.2+ clamps every value back into its
valid range on load (fps, resolution, bitrate, parallel limits, LAN ports, theme, sort, language), logs what it fixed in
the operation log, and keeps your device names and numbers.


- **Tile stuck on "Connecting…"** — right-click → Reconnect stream, or *Restart ADB* in the sidebar.
- **"Could not start scrcpy on this phone"** — scrcpy-server failed 3 times in a row on that phone; ControlPhone 2 keeps retrying every minute and writes the server's command line and output to `data\logs\scrcpy-<serial>.log`. Please attach that file when [opening an issue](https://github.com/mahiepit/ControlPhone/issues).
- **Phone numbers / names lost after updating ControlPhone** — the new version was extracted into a new folder without your `data`. ControlPhone 2.0.9+ offers to bring the old data over on first start; you can also do it any time in Settings → *Import data from an older version…*.
- **Black screen** — the phone's screen is asleep: use *Unlock* or *Screen on*.
- **Keyboard input not reaching the phone** — hover the phone (or click the large view) first; tap a text field on the phone.
- **Wi-Fi connection refused** — some ROMs block ADB over TCP (common on Samsung Android 11+); use *Pairing code (Android 11+)* instead.
- **Navigation bar visible in games on Android 10** — select the phones → right-click → ADB commands → *Hide 3-button bar for the current app*.
- **No video in the browser** — use a recent Microsoft Edge or Google Chrome (WebCodecs required).

### Putting phones where you want them

The grid sorts by number, name, status or battery — but a farm is rarely in that order. Two new commands put a phone
where you expect it: **Move earlier** and **Move later** (tile right-click menu, or `Ctrl+Shift+←` / `Ctrl+Shift+→` with
exactly one phone selected). The first move switches the sort box to **Manual order** and saves the arrangement, so it
survives restarts and appears in user-data backups.

The stored order is a list of phone ids, normalised every render: a phone you add later appears at the end, a phone you
remove disappears from the list, and the same phone can never appear twice. Moving with a filter active (for example
"USB only") still inserts the phone next to its neighbour in the full list, so nothing hidden jumps position.

### Typing text into phones

Text goes through the mirror session when one is open — that is the only route that carries Arabic, Vietnamese or
Chinese, because the session turns the text into a clipboard paste on the phone. When a phone has no open session,
ControlPhone falls back to `adb shell input text`, which only understands plain ASCII.

The fallback is honest about its limits: ASCII text is delivered, non-ASCII text is refused per phone with the reason
("needs an open mirror session") instead of being silently dropped.

Bulk typing — *Send text to N phones* — is a batch operation like install or shell, not a fire-and-forget loop: it shows a
progress card, keeps one result row per phone, lets you cancel a single phone, and **retries only the phones that failed**
with the same text. The AI `send_text` tool runs through the same engine and answers `OK`, `PARTIAL` or `FAIL` with a
per-phone line, so an assistant answer can never claim it typed Arabic on a phone that never received it.

### Controlling a running batch

A batch (install, shell, push, screenshot…) no longer has to be all-or-nothing. Its results window lists **every** phone
in the batch, including the ones still waiting, and each waiting phone has its own ✕ button: cancel that phone and the
others keep running. "Stop all" still exists for the whole queue.

When the batch ends, **Retry N failed phones** runs the same operation again with the same arguments on exactly the phones
that failed — skipped phones are not included, and a phone that failed twice is retried once. The retry appears as a new
task with its own results, so the first run's log stays intact.

### Keyboard shortcuts

| Keys | Action |
|---|---|
| `Ctrl+F` | focus the search box |
| `Esc` | clear the search box, then the selection |
| `F5` | refresh the information of every online phone |
| `Ctrl+A` / `Ctrl+I` | select all visible phones / invert the selection |
| `Ctrl+L` / `Ctrl+Shift+L` | show only the selected phones / show all again |
| `Ctrl+P` | pause or resume viewing |
| `Ctrl+R` | reconnect the video stream of the selected phones |
| `Ctrl+Shift+A` | open the AI assistant |
| `Ctrl+Shift+S` | screenshot the selected phone |
| `F1` | this list, inside the app |

Shortcuts are ignored while you type in a field (only `Esc`, `F1` and `Ctrl+F` still work), so typing never triggers an
action.

### Backup and restore

*Settings → Export / import settings* now also offers **Back up all data…** and **Restore from a backup…**. The ZIP holds
phone names, numbers, groups, settings, UI preferences and the AI conversation history — never API keys (they are DPAPI
encrypted per machine and stay there). Restoring merges instead of overwriting: phones are matched by serial, groups are
unioned, the next device number never goes down, and conversations already on the machine are left alone. A file that is
not a ControlPhone backup, or comes from a newer version, is refused before anything is written.

### Streaming answers

When the provider supports server-sent events (OpenAI, Anthropic, Gemini and every OpenAI-compatible endpoint), the
assistant's answer is shown while it is being written instead of after a pause. It is a display optimisation only: if the
stream fails, returns nothing or the provider does not support it, the request is retried the normal way, so the feature
can never make an answer disappear. Toggle it in *AI → Assistant options → Show the answer as it is written*.

### Per-phone log

Right-click a phone → *Phone log…* shows that phone's own history: every state change (`offline → connecting → online`,
`→ error · reason`) and the result of each operation run on it, newest last. *Copy*, *Open file* and *Clear log* work on it,
and the file lives in `data\logs\devices\<serial>.log` (256 KB, rotated to `.log.1`) so it survives a restart. The AI
assistant reads the same log with its `device_log` tool — ask it "why does phone #4 keep dropping?" and it can answer from
the phone's own history instead of guessing.

### Reporting a problem

*Links & support → Diagnostics bundle → Export…* writes one ZIP you can attach to an email or send in the Telegram group:
versions (app, Windows, .NET, ADB, FFmpeg), the phone list (numbers, labels, transport, Android — no serial numbers),
the operation log, `crash.log` and your settings files. `ai.json` is excluded entirely and every text file is scrubbed of
`sk-…`, `AIza…`, `Bearer …` and `?key=` tokens before it is written, so the bundle is safe to hand to someone else.

## ❤️ Support the project

ControlPhone is free and always will be. If it saves you time or money, a donation keeps it maintained and improving. Thank you!

<table>
<tr>
<td align="center"><b>PayPal</b><br><img src="public/img/donate-paypal.svg" width="180" alt="PayPal QR"><br><a href="https://paypal.me/thaogia">paypal.me/thaogia</a></td>
<td align="center"><b>BNB (BEP-20) / ETH (ERC-20)</b><br><img src="public/img/donate-crypto.svg" width="180" alt="BNB / ETH QR"><br><code>0xd09c2E60cbC8526976C436e316630FA64296E824</code></td>
</tr>
</table>

Please double-check the network (BNB Smart Chain or Ethereum) before sending crypto.

## 🧩 How it works

```
Phone (scrcpy-server, H.264 hardware encoder) ──adb──► ControlPhone server (Node.js) ──WebSocket──► Browser UI (WebCodecs decode)
                         ▲                                                                              │
                         └──────────────── touch / keys / text / commands ◄────────────────────────────┘
```

```
ControlPhone.bat        launcher (portable Node if present, auto setup)
server/                 HTTP + WebSocket server, ADB client, scrcpy sessions, batch actions
public/                 UI (plain HTML/CSS/JS, no build step), translations in public/js/lang/
tools/setup.js          downloads & verifies scrcpy      tools/pack.js   builds the release ZIP
```

Want to add a language? Copy `public/js/lang/en.js`, translate the values, add it to `LANGS` in `public/js/i18n.js`, and run `npm run i18n:check`.

## 📬 Our links & support

| Channel | Value |
|---|---|
| Email | [mwings740@gmail.com](mailto:mwings740@gmail.com) |
| Telegram channel | https://t.me/+SiOMr4JAU8thZDFk |
| Telegram group | https://t.me/+ZXt8aHvs5ok2ZjM0 |
| Facebook page | https://www.facebook.com/share/1DuqpyXuTa/ |
| Sham Cash support ID | `963793dbb5e910b81683981b2c8fd9f6` |

Inside the app: **Settings → Our links & support** (روابطنا والدعم) with open/copy buttons for every entry.
The Sham Cash ID is support information only — ControlPhone never performs money transfers and never asks for
financial data.

## 📄 License

[MIT](LICENSE) © Thảo Gia. Third-party components (scrcpy, adb, ws, Node.js) are listed in [NOTICE.md](NOTICE.md).
ControlPhone is an independent project, not affiliated with Genymobile or Google.
Use it responsibly and in accordance with the terms of the apps and services you use.
