# Windows installation and first run (ControlPhone 3.0.1)

## What you need

- Windows 10 or Windows 11 (64-bit).
- A USB cable for each phone (or phones reachable over Wi-Fi / Ethernet with ADB over network).
- On each phone: *Developer options → USB debugging* turned on.

Nothing else: the release package is **self-contained** (the .NET runtime, adb, FFmpeg and scrcpy-server are
included). You do not install .NET, Node.js, adb or scrcpy separately.

## Install (Windows installer, recommended)

1. Download `ControlPhone-Setup-v3.0.1-win64.exe` from the [Releases](../../releases) page.
2. Run it. No administrator rights are needed: it installs for the current Windows account into
   `%LOCALAPPDATA%\Programs\ControlPhone`.
3. The wizard speaks English and Arabic (right-to-left). Accept the MIT licence, pick a folder if you want a different
   one, and finish. Leave *Launch ControlPhone* ticked to start it right away.
4. Uninstall later from *Settings → Apps → Installed apps → ControlPhone* (or the Start Menu shortcut).
   **Your `data\` folder is kept** — phone numbers, names, groups, settings, AI providers and chats survive an
   uninstall or an upgrade. Only `logs\` is removed.

Unattended install (a lab or a farm of PCs):

```bat
ControlPhone-Setup-v3.0.1-win64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /DIR="D:\Tools\ControlPhone" /LOG="%TEMP%\cp-setup.log"
ControlPhone-Setup-v3.0.1-win64.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART     :: upgrade an existing install
"%LOCALAPPDATA%\Programs\ControlPhone\unins000.exe" /VERYSILENT                  :: uninstall
```

The installer and the portable ZIP contain exactly the same application files; pick one.

## Install (portable ZIP)

1. Download `ControlPhone-v3.0.1-win64.zip` from the [Releases](../../releases) page.
2. Extract it to a folder you can write to (for example `C:\ControlPhone`). Do **not** run it from inside the ZIP.
3. Start `ControlPhone.exe`.
4. Choose the mode:
   - **Control All Devices** — grid + group sync for every phone;
   - **Show only 1 phone** — pick one phone, shown large (smoothest).
5. Plug in a phone by USB and accept *Allow USB debugging* on the phone (tick *Always allow from this computer*).

The folder layout after extraction:

```
ControlPhone.exe            the application
data\                       your phone numbers, names, groups, settings (created on first run)
data\ai\ai.json             AI providers; API keys encrypted with Windows DPAPI
data\logs\                  operation logs
lang\*.js                   UI translations (vi, en, ar, zh-CN, zh-TW, de, fr)
scrcpy\                     adb.exe, scrcpy-server, FFmpeg DLLs, scrcpy.exe
docs\                       this documentation
```

## Updating

1. Close ControlPhone.
2. Extract the new version to a new folder, then copy your old `data` folder next to the new `ControlPhone.exe`
   (or let ControlPhone offer to import it on first start — it searches for previous data automatically).
3. Your numbers, names, groups, settings and AI providers are kept. API keys stay valid because they are bound to
   your Windows account, not to the folder.

## First connection

- **USB**: instant detection. If the phone asks for authorization, accept it once.
- **Wi-Fi**: right-click the phone → *USB → Wi-Fi*, or *Connect by IP*, or *Scan LAN*. Android 11+ pairing:
  *Pair with code*.
- **Many phones**: each phone keeps a number (`#1`, `#2`…) and an optional name; groups are free-form labels.

## Performance settings

| Setting | Default | When to change |
|---|---|---|
| Grid max size / bitrate / FPS | 480 px · 600 kbps · 15 fps | lower for 100+ phones |
| Large view max size / bitrate / FPS | 1280 px · 4000 kbps · 30 fps | lower on slow PCs |
| Parallel stream starts | 6 | raise on strong PCs, lower on weak ones |
| Parallel batch jobs | 10 | for APK installs across many phones |
| Software encoder | off | turn on if a phone's hardware encoder fails |
| Focus viewer | on | when one phone is enlarged, the others stop decoding |

Only visible tiles are decoded; minimizing the window pauses decoding. *Pause viewing* stops the stream but keeps
control.

## Data and privacy

- `data\config.json` — phone numbers, names, groups, settings (compatible with the 1.x web version).
- `data\ai\ai.json` — AI providers; API keys are DPAPI-encrypted for your Windows account.
- `data\logs\` — operation logs (no API keys).
- No account, no telemetry, no ads. The app talks to your phones (ADB), to the AI provider you configure, and to
  your PC only.

## Troubleshooting

| Symptom | Fix |
|---|---|
| Phone shows *unauthorized* | Unlock the phone, accept *Allow USB debugging*, tick *Always allow*. |
| Phone missing from the grid | Check the cable/port, another ADB program may hold it (the top bar offers to release it). |
| Black tile | The phone screen may be off; the phone is still controllable. |
| "Could not start scrcpy on this phone" | The phone's encoder refused the options; try *Settings → Software encoder*, or check `data\logs`. |
| Wi-Fi phone keeps disconnecting | Enable *Auto-reconnect Wi-Fi phones*; keep the phone on the same network. |
| Very low FPS with many phones | Lower grid quality, use *Show only these phones*, or enable *Focus viewer*. |
| Antivirus blocks the exe | The binary is unsigned; allow it or build it yourself (see `native/README.md`). |

## Verifying a build

Run `ControlPhone.exe --selfcheck` in a terminal. It checks translations/RTL, DPAPI encryption, provider storage,
the three AI protocols, the tool registry and the support data, prints `PASS/FAIL` and returns a non-zero exit code on
failure. The GitHub workflow runs the same check on Windows before publishing a release.
