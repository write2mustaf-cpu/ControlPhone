# Changelog

All notable changes to ControlPhone. This file lists only work that is actually implemented and testable in this
repository — no planned features are listed as done.

## 3.25.0 — the grid has a budget, not just per-phone luck

Every phone adapted on its own, so nothing limited what the *whole grid* costs. 300 phones at 480p/15 fps is 300 × 1.94 M
pixels/s ≈ 580 Mpixel/s of YUV→BGRA conversion: the PC simply cannot, and the first thing to go is the UI. Each phone was
individually "healthy" while the machine drowned.

### What is new
`Core/LoadBudget` divides one decode budget across the streams that are actually open:

```
cost   = width × height × fps        (480p/15 = 1.94 Mpixel/s)
share  = cores × 45 Mpixel/s ⁄ open streams
order  = long side down to 480  →  fps down  →  resolution down   (floor 240p / 5 fps)
```

| Open streams (12 cores) | Per stream | 480p/15 opens as |
|---|---|---|
| 1 | 540 Mpixel/s | 480p/15 — untouched |
| 300 | 1.8 Mpixel/s | 480p/12 |
| 300, a 720p/30 request | 1.8 Mpixel/s | 480p/11 |
| starved | 0.5 Mpixel/s | 384p/5 |
| almost nothing | ~0 | 240p/5 (never below) |

- Applied in two places: when a stream opens (`DeviceVideo.Thumb`) and as the **ceiling** of the adaptive governor, so
  quality can never climb back above what the machine can decode.
- Recomputed once per 2 s tick from the real number of live decoders — `LoadBudget.SetActive(streams, cores)`.
- New setting (on by default): "share the decode budget across open phones".

### Verification
- Self-check 370 → **379**; the arithmetic was run in the Linux harness first (42 assertions, 0 failed).
- CI on `windows-latest`: build, publish, installer + portable ZIP, install → `--selfcheck` → uninstall.
- ar/en packs: 1 new string (918 keys each, 0 missing).

## 3.24.0 — the numbers stopped lying

3.23.0 added the measurement layer. Using it for one round exposed three ways it reported things that were not true.

### 1. A dead stream kept reporting its old bandwidth
`StreamSnapshot` returned the last kbps forever, so a phone that had gone to sleep still showed `0 fps · 500 kbps` in its
tile and told the AI the same. Now kbps reads **0 after 1.5 s of silence**, and the snapshot carries `AgeMs`, the age of the
last frame. The tile shows `stalled · no frame for 6.0 s` instead of stale numbers.

### 2. "Dropped" was really "waiting for a key frame"
`H264Decoder` counted every frame skipped while waiting for a key frame as a dropped frame, so each reconnect painted a
pile of fake drops on the tile. Those are now `Skipped()` and counted separately: dropped means lost, skipped means not
shown yet.

### 3. A sleeping phone was lowering its own quality
With fps decaying to zero, the adaptive governor read the stall as overload and stepped the quality of a phone that was simply
asleep down to the floor — blurry picture the moment it woke up. A stream with no frame for more than 3 s now produces no
decision at all (`stream is stalled — quality left alone`).

### Also
- CPU load is sampled once per 2-second tick, not once per phone (300 phones = 300 process reads per tick).
- AI `get_stream_stats` reports `stalled` with its skips instead of an invented fps.
- Self-check 364 → **370**; the new maths was first run in the Linux harness (26 assertions, all green).
- ar/en packs: 1 new string (917 keys each, 0 missing).

## 3.23.0 — measured performance, per-phone adaptive quality, log diagnosis

This round delivers phase 1 (performance) and part of phase 4 (AI) of the big brief. **What is not here is listed at the
end** — nothing below is claimed unless it is implemented and tested.

### 1. The stream finally reports what it does
`Video/StreamStats` counts, per stream: frames per second over a one-second window, kbps from the bytes actually received,
decode milliseconds (EWMA, so one slow frame cannot flip a decision), dropped frames and frame size. `H264Decoder` feeds it
from `Feed()`, so the numbers are the real pipeline and not a guess. Found while testing: the bitrate was stored as
`kbit/s / 100` and printed **0.2 instead of 16** — the unit is now stored ×100 and asserted.

### 2. Per-phone adaptive quality, remembered
`Core/AdaptiveQuality.QualityGovernor` is a pure state machine with an injected clock:

```
2 stressed samples  → bitrate -30%, then fps -25%, then resolution -20%   (never below the floor)
4 healthy samples   → bitrate +20%, then fps +17%, then resolution +20%   (never above the ceiling)
4 s / 6 s cooldown  → no flapping; a hard floor of ½ resolution, ¼ bitrate, ⅓ fps
```

`Core/AutoQuality` samples every 2 s for phones whose grid stream is live, and when a phone has to go lower it reopens
**only that phone's** stream. The learned level is stored per phone (`DeviceMeta.AutoMaxSize/AutoFps/AutoBitRate`), so the
next start uses it immediately — and a manual override in the device dialog always wins, because learned values live in
separate fields.

### 3. Visible measurement + a real per-phone bitrate
Each tile header now shows `12 fps · 480 kbps · 3.2 ms` (toggle in settings, `RaisePerf()` refreshes only that line, not the
whole tile). The device video dialog gained the missing **grid bitrate** field, so bitrate can finally be set per phone.

### 4. Log diagnosis instead of guessing
`Core/Diagnostics` maps the log to ten known root causes (unaccepted ADB key, Samsung command-line crash, adb port conflict,
drop-off, no device, server crash, software-only encoder, lost key frame, permission denial, Wi-Fi loss) with cause, fix and
whether ControlPhone can fix it by itself. It picks the **most recent** matching rule and reports how often it appeared.

### 5. Three more AI tools (24 → 27)
`get_stream_stats`, `diagnose_device`, `set_stream_quality` — the last one resolves the phone with the 3.20 rules (ambiguous
name = refuse), rejects a call with no values, and reopens only the targeted phone.

### Verification
- Self-check 334 → **364**, and the new pure logic was first run in a standalone harness on Linux (21 assertions) where the
  kbps unit bug was caught before it shipped.
- CI on `windows-latest`: build, publish, install → `--selfcheck` → uninstall, portable ZIP.

### Not in this release (so the claim stays honest)
Hardware decoding (D3D11VA/NVDEC/QuickSync) with zero-copy D3D11 presentation, H.265/AV1 negotiation, audio streaming,
mDNS discovery, seamless USB↔Wi-Fi handover, macro recorder/scenario editor, OCR/vision automation, local REST/WebSocket/CLI,
plugin system, video recording, 60+ tools, voice input. Those are large pieces; they are not half-built, so they are not
shipped.

## 3.22.0 — a long AI chat (or pressing Stop) broke the conversation for good

### The bug
The history sent to the provider was "the last 60 messages", cut wherever that landed:

```csharp
if (usable.Count > 60) usable = usable.Skip(usable.Count - 60).ToList();
```

Two ways that produces a request the provider rejects:

| Situation | What was sent | What came back |
|---|---|---|
| 60-message cut lands inside a tool exchange | first message is `role: tool` with no preceding `tool_calls` | OpenAI **400**: "messages with role 'tool' must be a response to a preceding message with 'tool_calls'" |
| any history shorter than 60 that starts with a tool result (restored chat, earlier trim) | same | Anthropic **400**: first message must use the `user` role |
| user presses **Stop** while several tool calls are queued | assistant with 3 `tool_calls`, 1 tool result | OpenAI **400** on the next request |

Because the broken messages stay in the chat, every later request failed the same way: the conversation was dead until the
user deleted it. It needed no exotic setup — one long agentic session, or one Stop, was enough.

### The fix
`AiAgent.Window(messages, max = 60)` now builds the request:

```
filter errors / empty assistant turns
  ─► keep the last 60
  ─► start at the first `user` turn (drops a half-cut exchange)
  ─► drop tool calls with no result, drop assistant turns left with no calls
  ─► drop tool results whose call is no longer in the window
```

The current user turn is always the last message, so trimming always leaves a valid, non-empty request.

### Verification
- Self-check 326 → **334**: cut exchange starts at a user turn, 120-message chat capped at 60 and still aligned, unanswered
  calls dropped, assistant with no answered call dropped, orphan tool result dropped, error/empty turns dropped, a valid
  exchange preserved in order with its call id, newest user turn never cut.

## 3.21.0 — new look: one palette, real tokens, and contrast that is measured

### What was wrong with the old look
Colours were a dozen loose hex values, and several places ignored the theme entirely: the drag-and-drop overlay, the viewer
overlay text, the phone-tile overlay and spinner, the root badge and the sync badge were dark-only hex literals, so in light
mode they stayed dark. Buttons had `FocusVisualStyle="{x:Null}"`, i.e. no keyboard focus indication at all. Radii were 5, 6,
7, 8, 9 and 14 depending on the file, font sizes were 10.5…15, and the primary button was flat blue with white text at
**3.16:1** — below the 4.5:1 minimum for text.

### The new design system
| Layer | Before | Now |
|---|---|---|
| Colour | 12 hex values, some hardcoded in views | `Theme.Palette` (28 keys × dark/light), every view uses `DynamicResource` |
| Accent | flat `#4F7CFF` | gradient `#3D6BF0 → #6D3FE0`, white text **4.61:1 / 6.11:1** |
| Radii | 5…14 ad hoc | `RCtl 8` < `RCard 12` < `RPill` |
| Type | 10.5…15 ad hoc | `FsCap 11` < `FsBody 12.5` < `FsH2 15` < `FsH1 18`, `Segoe UI Variable` with fallback |
| Buttons | border change on hover, no focus | hover tint overlay (works on gradients), pressed state, **keyboard focus ring** |
| Surfaces | flat borders | cards + pills, elevation only where it reads: tooltips, dropdowns, primary buttons, hovered tile |
| Controls | default-ish | rounded text fields with hover/focus borders, gradient checkboxes, filled slider track, rounded progress bar, thin scrollbar with hover thumb |
| Phone tile | 5 px corners, text-only header | 12 px card, numbered pill, hover shadow, 2 px selection ring, tokenised overlays |

Light mode is now genuinely light: the overlay, spinner, badges and toasts all switch with the theme.

### Verification
- Self-check 318 → **326**: every brush the UI asks for exists in the palette, every colour parses, light and dark differ for
  every theme-dependent key, and WCAG ratios are asserted in code — body text ≥ 7:1, muted and status colours ≥ 4.5:1,
  white on both gradient ends ≥ 4.5:1, white on all three toast colours ≥ 4.5:1. The contrast helper is checked against the
  black/white extremes (21:1).
- Radii and font sizes are asserted to be a real scale, not random values.

### Not verified
No screenshot comparison: the runner has no interactive desktop. The checks cover tokens and contrast, not pixel layout.

## 3.20.0 — the AI could run a command on a phone you never named

### The bug
Tool arguments name phones by number, name or id. The resolver took the **first** device whose name *contained* the token:

```csharp
?? devices.FirstOrDefault(d => d.Name.Contains(t, StringComparison.OrdinalIgnoreCase));
```

With `Pixel 8` and `Pixel 8 Pro` connected, `"Pixel"` silently picked `#1`. With two phones both labelled `Máy A`, an exact
name silently picked the first. And a token that matched nothing was **dropped without a word**, so
`devices: ["#3", "Pixel 9"]` ran on `#3` only while the tool reported success. Every sensitive tool — `shell`,
`app_control` (clear data, uninstall), `install_apk`, `push_file`, `device_action` — went through this path.

### The fix
`AiToolHost.MatchOne(devices, token)` is now the only way a token becomes a phone:

| Input | Result |
|---|---|
| `SER3` / `3` / `#3` | that phone |
| `galaxy a54` | that phone (case-insensitive exact name) |
| `Galaxy` | that phone — partial name allowed **only** when exactly one matches |
| `Pixel` (with `Pixel 8`, `Pixel 8 Pro`) | **refused**: `"Pixel" matches 2 phones (#1, #2)` |
| `Máy A` on two phones | **refused**: `2 phones are called "Máy A"` |
| `9` (no such number) | **refused**: `no phone has the number 9` |

`Resolve` now throws when any entry cannot be resolved to exactly one phone, so the tool returns an error listing the
problem and the known phones — **nothing runs**, instead of running on a subset. The tool description sent to the model
says so too.

### Verification
- Self-check 308 → **318**: serial, number with and without `#`, case-insensitive name, unique partial name, Vietnamese
  name, ambiguous partial name refused, duplicate names refused, unknown name refused, missing number refused, empty entry
  refused.

## 3.19.0 — Arabic and English are now complete, and CI keeps them that way

### The gap
The app's source language is Vietnamese and every other language is a lookup table. The Arabic pack was missing 84 strings
and so was English — and English is the fallback for every other language, so a German or Chinese user saw Vietnamese in
those 84 places too. The misses were not exotic: *Export settings*, *Import settings*, *Diagnostic information*,
*Connected:* / *Connection failed:* / *Network error:*, *update check*, the file-dialog filters, and the whole "PC
keyboard" restore message added in 3.18.0.

The extractor that reports this (`tools/i18n-native.js`) also counted 8 strings that are not sentences at all: fragments of
interpolated strings (`)} máy mất kết nối`), a regex character class, a performance header and the already trilingual
language label. Translating those would have been noise, and they kept the report permanently non-zero.

### What changed
- **84 strings translated into Arabic and English** (828 → 912 keys each), placeholders `{0}`/`{1}` preserved everywhere.
- The extractor now skips `Core/SelfCheck.cs` (test fixtures such as `#1  Máy A  usb  online`) and non-sentence strings,
  so the count means what it says.
- `node tools/i18n-native.js --strict` exits non-zero when Arabic or English is incomplete, and the Windows workflow runs it
  before the build — a new UI string can no longer ship untranslated in those two languages.
- German, French and both Chinese packs still miss ~167 strings each; the tool prints that, but it does not fail the build.

### Verification
- Self-check 301 → **308**: the whole Arabic pack is checked at runtime — 912 keys, no value left equal to its Vietnamese
  key, every value contains Arabic script, no `{n}` placeholder lost, English matches Arabic key for key, and the newest
  strings (*Export settings*, *keyboard*, *Missing file*, *Network error:*) resolve to Arabic.

## 3.18.0 — turning off the PC keyboard could leave a phone with no keyboard at all

### The bug
"PC keyboard" installs an invisible IME so the phone shows no on-screen keyboard. Turning the mode off restored the
keyboard remembered in `Store.Meta(id).PrevIme` with a single `ime set`. If that keyboard had since been uninstalled — or
renamed by a ROM update — `ime enable`/`ime set` fail silently, `DisableAsync` returned "could not restore", and the
phone was left with *only* the invisible ControlPhone keyboard: no way to type anywhere, not even the phone's own
settings, until the user found another ADB command. The fallback also only ever considered keyboards that were already
enabled, so an installed-but-disabled Gboard was ignored.

### The fix
- `PcKeyboard.RestoreOrder(prev, available)` builds the restore order: the remembered keyboard first, **only if it still
  exists**; then every other non-voice keyboard; voice ones last; duplicates and our own IME removed; `"null"` and blank
  entries ignored. Pure and testable.
- `DisableAsync` collects both `ime list -s` (enabled) and `ime list -a -s` (installed, possibly disabled), then tries
  each candidate in order — enabling it first when needed — and only disables our IME once another keyboard is confirmed
  active.
- When nothing can be restored, the ControlPhone keyboard is deliberately **kept enabled** (a phone without any keyboard
  is worse than one with an invisible keyboard) and the message says so and tells the user to install a keyboard first.

### Verification
- Self-check 292 → **301**: remembered keyboard first; a saved keyboard that no longer exists is never attempted; `null`
  ignored; our own IME never a target; voice last; case-insensitive duplicates removed; empty list → empty plan; voice
  alone still accepted; surrounding whitespace ignored.

## 3.17.0 — the diagnostics bundle was missing the one file support needs

### The bug
`DiagBundle.Build` copied `data/ui.json`. The app writes `data/native-ui.json` (see `Store.UiFile`), so the copy always
found nothing and was skipped silently — while the bundle's own `README.txt` promised "config.json and ui.json as they
are on disk". Every diagnostics bundle ever sent was missing the theme, language, tile size, sort, filter and skipped
version — exactly the settings behind layout, RTL and "the window looks wrong" reports.

### The fix
- The bundle now copies `Store.UiFilePath` (one source of truth for the name, exposed instead of a private field), and
  `Build` takes an optional `dataDir` so the behaviour can be tested against real files.
- `README.txt` is written **last, from the list of entries actually written**. A file that is missing is neither copied
  nor promised; each line now says what the file is for.
- The bundle README lists `settings/native-ui.json` with its contents (theme, language, tile size, sort, filter, skipped
  version) instead of the vague `settings/*.json`.

### Verification
- Self-check 285 → **292**, seven new checks that write a real temp data folder and read the ZIP back: the UI file is
  present, `settings/ui.json` is gone, the values (`light`, `ar`, `group:A`, `v9.9.9`) survive, `config.json` arrives with
  `sk-…` masked, the README lists exactly the entries in the archive, and a missing UI file is neither copied nor listed.

## 3.16.0 — "Download ZIP" can no longer hand you a YAML file

### The bug (self-inflicted in 3.15.0)
`Updates.Parse` took *the last* `.zip` asset in the GitHub response. That was harmless while a release had two files. Then
3.15.0 started attaching a third one — `ControlPhone-v3.15.0-manifests.zip`, 2 KB of YAML. Depending on asset order, the
update dialog would report "ZIP 0.0 MB" and hand the user a manifest archive instead of the 80 MB app. It happened to work
only because GitHub returned the assets in alphabetical order.

### The fix
- `Updates.PickAssets` picks by **name**: the installer is the `.exe` with `setup` in it, the portable package is the
  `.zip` with `win64`/`portable` in it; `manifest`, `symbol`, `source`, `checksum`, `sha256` and `debug` archives are
  rejected outright, and zero-size entries (upload still running) are never offered as a download. Ties go to the larger
  file, so order from the API no longer matters.
- The update dialog also gained a **Download ZIP** button next to *Download installer* — the portable build had no direct
  button before, only the release page.
- `Tải ZIP` translated into the six shipped languages.

### Verification
- Self-check 275 → **285**: ten checks around the picker — three-asset release, reversed order, an adversarial
  manifests archive that is *larger* than the portable ZIP, manifests-only, symbols + plain zip, a zip without the win64
  suffix, zero-size assets, an empty asset list, and a full `Parse` of a three-asset response.

## 3.15.0 — The first published package (Scoop bucket + winget manifests)

### The gap
Every release was three loose files. Nothing a package manager could install, and no way to keep a manifest's version and
SHA-256 honest: typed by hand, one of them drifts and `scoop install` fails with a hash mismatch that looks like a corrupt
download.

### What was added
- `tools/package_manifests.py` — generates the winget manifest set (version / installer / en-US locale) and a Scoop
  manifest from the built artefacts. It hashes the files itself, refuses missing, empty or renamed artefacts (a renamed
  copy would publish a dead URL), writes the version into every file, then re-reads its own output to verify version,
  hash, installer type and that Scoop still persists `data\`. It also emits `manifests.json` (name, bytes, hash, URL).
- CI runs it right after building the installer and attaches `ControlPhone-v<version>-manifests.zip` to the release, so
  the manifests always describe the files shipped next to them.
- **Scoop bucket published**: `scoop bucket add controlphone https://github.com/write2mustaf-cpu/scoop-controlphone` then
  `scoop install controlphone`. The manifest installs the portable build, adds a `controlphone` shim and a Start-menu
  shortcut, and persists `data\` so `scoop update` keeps config, device logs and chat history.
- `docs/PACKAGES.md` (+ Arabic): install commands, how to submit the winget manifests, why they are generated, and what CI
  does **not** verify.

### Verification
- Manifests generated from the real 3.14.0 assets, then checked against the live release: both URLs return HTTP 200, both
  sizes match, and the SHA-256 computed from the downloaded bytes equals the hash written into the manifest.
- Self-check unchanged at 275 (no app code touched); the packaging script verifies its own output on every run.

## 3.14.0 — Bulk typing joins the batch engine

### The gap
Typing was the one operation that bypassed `Actions.RunBatch`: the *Send text to N phones* dialog looped over phones and
showed a toast, and the AI `send_text` tool looped on its own. No progress card, no per-phone rows, no cancel of a single
phone, and — the real loss — no way to retry the phones that failed, even though the batch engine has had per-phone
cancel and retry since 3.11.

### What changed
- `Actions.Ops["text"]` → `Typing.SendAsync(d, text, enter)`, so typing now has the queue limit, the progress card, the
  per-phone results window with ✕ per phone, and `Actions.Retry` (the payload keeps `text` + `enter`, so a retry types the
  same text on exactly the phones that failed).
- `DlgText` sends through `Batch("text", …, openResults: true)` instead of a fire-and-forget loop; its notes now state the
  session/ASCII rule and that per-phone results are available.
- The AI `send_text` tool also runs through the batch engine; `Typing.Summary(text, results)` formats the answer as
  `OK` / `PARTIAL` / `FAIL` with one line per phone, and the progress card title reads *Type text* instead of `text`.

### Verification
- Self-check 269 → **275**: `Summary` is `OK` only when every phone took the text, `PARTIAL` when one is short, `FAIL`
  when none (or when there are no results at all), names the phone that failed, and keeps the preview on one line.

## 3.13.0 — Put a phone where you expect it (manual grid order)

### The gap
The grid could be sorted by number, name, status or battery — never arranged. In a farm the phone labelled "charging
rack" belongs next to the one it is cabled with, and no sort key expresses that.

### What was added
- `Core/ManualOrder.cs`: `Normalize(present, stored)` (saved order wins, phones added later go to the end, removed
  phones are dropped, no duplicates), `Move(order, id, delta)` (clamped at both ends), `MovePast(full, id, neighbour,
  delta)` so moving inside a filtered grid inserts next to the visible neighbour in the full list.
- `Store.Ui.Order` (list of ids) plus a new sort mode `manual`; `RenderAllCore` applies the stored order when that mode
  is active, so the existing incremental `GridOrder.Sync` still moves only the tiles that changed.
- **Move earlier / Move later** in the tile menu and on `Ctrl+Shift+←` / `Ctrl+Shift+→` (single selection). The first
  move switches the sort box to *Manual order*, saves, and toasts which phone moved.
- User-data backups now carry the order (`Doc.Order`), restored through `Normalize` so an old backup cannot inject ids
  for phones that no longer exist.

### Verification
- Self-check 251 → **269**: order normalisation (stale dropped, new appended, duplicates removed, empty grid), `Move`
  at both ends and for unknown ids, `Move` not mutating its input, `MovePast` before/after a neighbour and with a missing
  neighbour, plus the two new shortcut combinations, `Ctrl+Left` staying free, and no shortcut stealing keys from a text
  field.

## 3.12.0 — Typing text that adb cannot carry, and reporting it honestly

### The bug
Text was sent with `ControlSession?.TypeText(...)`. With no open mirror session the `?.` made the call a silent no-op, and
the AI `send_text` tool still answered `OK: typed N character(s) on N phone(s)`. Asking the assistant to type Arabic on a
phone whose mirror was closed produced a success message and nothing on the screen.

### The fix
- New `Core/Typing.cs`: `SendAsync(phone, text, enter)` picks the route per phone — open session (Unicode via the
  session's clipboard paste), otherwise `adb shell input text` when the text is plain ASCII, otherwise a refusal that
  names the reason. `AdbArg` escapes spaces as `%s` and shell metacharacters, flattens tabs/newlines.
- `PasteToPhones` now awaits per-phone delivery and toasts the real counts ("pasted into 3 phones", "2 phones did not
  receive the text — <reason>") instead of assuming the clipboard reached everyone.
- AI `send_text` returns `OK` / `PARTIAL` / `FAIL` with one line per phone, and `AiTools.SendText` maps `PARTIAL` to a
  failed tool result so the assistant cannot claim success on a phone that never got the text. The tool description now
  states the ASCII/session rule.

### Verification
- Self-check 237 → **251**: ASCII detection (Arabic, Vietnamese, multi-line, over-long, empty), the session rule, `%s`
  for spaces, escaped `;|&` and quotes (a phone-side shell injection cannot be typed by accident), tabs flattened,
  `CanUseAdb` agreement with `AdbArg`, and single-line, ellipsis-terminated previews.

## 3.11.0 — A running batch can be steered, and failures can be retried

### Per-phone cancel and retry (queue hardening)
- `BatchTask` now carries `Payload` and `DeviceIds`, and gains `CancelDevice(id)` / `IsCancelled(id)` / `ShouldSkip(id)`:
  one phone leaves the batch while the rest keep running. `Stop()` still cancels the whole queue, and a finished batch
  ignores late cancellations.
- `Actions.Retry(task)` re-runs the same operation with the same arguments on `FailedIds` only — skipped phones are
  excluded and a phone that failed twice is retried once. It appears as a new task, so the first run's results and log
  stay intact.
- Results window: every phone in the batch is listed, waiting phones show *Đang chờ…* with their own ✕ button, then
  switch to *Đã huỷ* / result; the row updates live from the task's `PropertyChanged`. Buttons: *Dừng tất cả*,
  *Thử lại N máy lỗi*, *Chọn các máy lỗi*.
- A `clock` glyph was added for the waiting state.

### Verification
- Self-check 228 → **237**: fresh batch runs everything, cancelling one phone leaves the others (and does not stop the
  queue), stop skips what is left, a finished batch ignores late cancels, `FailedIds` excludes skipped results and
  de-duplicates, a clean batch has nothing to retry, and payload/targets survive for the retry. The seven existing
  stop/token/status checks were kept.

## 3.10.0 — Keyboard shortcuts for the grid

### Shortcuts (12, all working, listed in-app)
`Ctrl+F` search · `Esc` clear search/selection · `F5` refresh phone info · `Ctrl+A` select all visible · `Ctrl+I` invert ·
`Ctrl+L` solo selection · `Ctrl+Shift+L` show all · `Ctrl+P` pause/resume · `Ctrl+R` reconnect streams · `Ctrl+Shift+A`
assistant · `Ctrl+Shift+S` screenshot · `F1` the list itself.

- `Core/Shortcuts.cs` holds the table (`Key` + `ModifierKeys` → action id), a pure `Match` (Windows key ignored, extra
  modifiers rejected), `Gesture` for display, and `WorksWhileTyping` so a `TextBox`/`ComboBox` keeps its keystrokes —
  only `Esc`, `F1` and `Ctrl+F` pass through.
- Help dialog (also in the main menu → *Keyboard shortcuts*): two-column list built from the same table, so the docs and
  the behaviour cannot drift.

### Verification
- Self-check 213 → **228**: every combination matched (including `Ctrl+L` vs `Ctrl+Shift+L` and `Ctrl+A` vs `Ctrl+Shift+A`),
  bare letters and extra modifiers rejected, Windows key ignored, unique action ids, labels present, readable gesture text
  (`Ctrl+Shift+A`, `Esc`), and the typing-safety rule.

## 3.9.0 — Back up everything that is yours

### Full user-data backup / restore
The old export covered settings only: after reinstalling, 50 named and grouped phones came back as "Phone 1…50". Now
*Settings → Export / import settings* offers **Back up all data…** and **Restore from a backup…**.

- `Core/UserBackup.cs`: one ZIP with `backup.json` (manifest: app, version, date, counts, `apiKeys: false`), `data.json`
  (device metadata incl. Wi-Fi serials, groups, next number, settings, theme/language/tile width/sort/hide-offline, AI
  options) and `chats/<id>.json` (conversation history in the exact format `AiStore` writes).
- **No API keys, ever**: the backup never reads `ai.json`, and a check asserts no `KeyProtected`/`sk-` text can appear.
- Restore merges instead of overwriting: phones match by serial, groups are unioned, `NextNum` only grows, and
  conversations already on the machine are skipped. Settings go through the same `SettingsSanitizer` as `config.json`.
- Bad input is refused before anything is written: not a ZIP, missing manifest, wrong application, or a backup from a
  newer format version.
- The dialog reports what happened (`+N phones · +N groups · +N conversations`) and logs it to the operation log.

### Verification
- Self-check 196 → **213**: manifest counts and the `apiKeys: false` flag, device names and Wi-Fi serials, groups, UI prefs
  and AI options surviving a round trip, chat history stored and read back, "no key material anywhere in the ZIP", ZIP
  layout, merge rules (add missing, replace matching, union groups, never lower `NextNum`, skip existing chats), and
  rejection of a non-ZIP file and of a backup from another application.

## 3.8.0 — Answers appear while they are written

### Streaming replies (the last big item of the original brief)
- `Ai/AiStream.cs`: an SSE line parser that survives chunks cut mid-line, CRLF and comment/keep-alive lines, plus a
  `StreamAccumulator` per provider — OpenAI-compatible (`delta.content`, `delta.tool_calls` merged by index), Anthropic
  (`message_start` / `content_block_start` / `text_delta` / `input_json_delta` / `message_delta`) and Gemini
  (`parts[].text`, `functionCall`, `usageMetadata`). Usage tokens are taken from the stream, so the counters stay right.
- `AiClient.ChatStreamAsync` + `StreamUrl` / `StreamBody`: `stream: true` for OpenAI (with `stream_options.include_usage`)
  and Anthropic, `:streamGenerateContent?alt=sse` for Gemini, body untouched there.
- Safety first: any stream failure (HTTP error, empty stream, network, unknown shape) returns `null` and `AiAgent` retries
  with the normal `ChatAsync`, so a broken stream can never swallow an answer. Cancelling still stops immediately.
- Chat window: a temporary plain-text bubble is appended as deltas arrive (throttled to ~60 ms / 160 chars), then replaced
  by the full markdown bubble when the answer completes; the live bubble is dropped on error, stop or a tool card.
- Toggle: *Assistant options → Show the answer as it is written (streaming)*, on by default.

### Also
- The AI assistant options dialog was Vietnamese-only in the other five languages; its nine labels are now translated.

### Verification
- Self-check 175 → **196**: SSE splitting/joining/CRLF/comments/flush, OpenAI text + fragmented tool calls + usage,
  Anthropic text/tool blocks/usage, Gemini parts/functionCall/usage, stream URL and body per provider, and the
  empty-stream fallback contract. No network needed.

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
