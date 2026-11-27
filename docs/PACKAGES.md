# Packages: installing ControlPhone through a package manager

ControlPhone ships three artefacts per release. Two are for humans, one is for package managers:

| Asset | For | Size |
|---|---|---|
| `ControlPhone-Setup-v<version>-win64.exe` | normal install (Inno Setup, per-user, no admin) | ~58 MB |
| `ControlPhone-v<version>-win64.zip` | portable — unzip anywhere and run `ControlPhone.exe` | ~80 MB |
| `ControlPhone-v<version>-manifests.zip` | winget + Scoop manifests, generated from the two files above | ~2 KB |

## Scoop

```powershell
scoop bucket add controlphone https://github.com/write2mustaf-cpu/scoop-controlphone
scoop install controlphone
scoop update controlphone      # later
```

The manifest installs the portable build, puts `ControlPhone.exe` on `PATH` as `controlphone`, adds a Start-menu
shortcut, and **persists `data\`** — that is where the app keeps `config.json`, device logs, chat history and API keys,
so `scoop update` does not throw away your setup.

## winget

The release attaches the three manifest files. They are ready to submit, not yet accepted into the public catalogue:

```powershell
# after the manifests are merged into microsoft/winget-pkgs:
winget install Write2Mustaf.ControlPhone
```

Submitting is a pull request to `microsoft/winget-pkgs`:

1. unzip `ControlPhone-v<version>-manifests.zip`
2. copy the three YAML files to `manifests/w/Write2Mustaf/ControlPhone/<version>/`
3. open the PR (their CI runs `winget validate` and an install test)

The manifests are `InstallerType: inno`, `Scope: user`, with the silent switches the installer actually supports
(`/VERYSILENT /SUPPRESSMSGBOXES /NORESTART`), so `winget install` and `winget upgrade` both work.

## Why the manifests are generated, not written by hand

A manifest carries the version and the SHA256 of the exact file users will download. Typed by hand, one of them drifts
and the package fails with a hash mismatch at install time. `tools/package_manifests.py` therefore:

- reads the two release files, computes their SHA256 itself, and refuses to run if either is missing or empty;
- refuses a renamed local copy — the URL must be the real release asset name, or the manifest would point at a dead link;
- writes the version into every file and re-reads what it wrote to verify version, hash, installer type and, for Scoop,
  that `persist` still covers `data\`;
- emits `manifests.json` (name, byte size, hash, URL) so the release page and any future automation can compare.

CI runs it right after building the installer, so the attached manifests always describe the files attached next to them:

```powershell
python tools/package_manifests.py --version 3.15.0 `
  --setup dist/ControlPhone-Setup-v3.15.0-win64.exe `
  --zip   dist/ControlPhone-v3.15.0-win64.zip `
  --out   dist/manifests
```

## Publishing a new version

1. push the tag — CI builds, self-checks, packages and publishes the release with all three assets;
2. run the command above against the published files (or reuse the attached `-manifests.zip`);
3. commit `bucket/controlphone.json` to the Scoop bucket — `checkver`/`autoupdate` handle the next version from there.

## What is verified automatically

- `ControlPhone.exe --selfcheck` — the app's own 275 checks, run on the build and again from the installed copy;
- the installer is silently installed, checked for `ControlPhone.exe`, `lang\ar.js`, `scrcpy\adb.exe`,
  `scrcpy\scrcpy-server` and `docs\AI-ASSISTANT.md`, then uninstalled and checked to leave nothing behind;
- the manifest generator re-verifies every file it writes.

What is *not* verified: `winget validate` and `scoop install` cannot run in this CI (no winget on the runner, no Scoop
either), so the first real install through a package manager is on you. The manifests are plain data — if a field is
wrong it fails loudly at install time, never silently.
