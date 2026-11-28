#!/usr/bin/env python3
"""Sinh manifest gói cài từ đúng tệp phát hành: winget (3 YAML) + Scoop (1 JSON).

Chạy trong CI ngay sau khi dựng bộ cài, để số phiên bản và SHA256 luôn khớp tệp thật:

    python tools/package_manifests.py --version 3.14.0 \
        --setup dist/ControlPhone-Setup-v3.14.0-win64.exe \
        --zip   dist/ControlPhone-v3.14.0-win64.zip \
        --out   dist/manifests

Chỉ dùng thư viện chuẩn, nên chạy được cả trên Windows lẫn Linux.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import re
import sys
from pathlib import Path

REPO = "write2mustaf-cpu/ControlPhone"
PACKAGE_ID = "Write2Mustaf.ControlPhone"
PUBLISHER = "Write2Mustaf"
MONIKER = "controlphone"
DESCRIPTION = (
    "Drive a rack of Android phones from one Windows grid: live screen mirroring, "
    "synchronised taps and typing, batch installs, per-phone logs, an AI assistant "
    "that calls the app's own tools, and full Arabic (RTL) support."
)
TAGS = ["android", "adb", "scrcpy", "phone-farm", "device-management", "mirroring", "automation"]
VERSION_RE = re.compile(r"^\d+\.\d+\.\d+$")
HASH_RE = re.compile(r"^[0-9a-f]{64}$")


MIN_SETUP = 5_000_000
MIN_ZIP = 5_000_000


def assert_real_artifact(path: Path, magic: bytes, min_bytes: int, what: str) -> None:
    """Từ chối tệp không phải bộ cài thật: một trang 404 HTML tải về (9 byte) đã từng được đóng gói thành manifest
    "đã xác minh" và làm hỏng bản cài Scoop của người dùng."""
    if not path.exists():
        raise SystemExit(f"{what}: không có tệp {path}")
    size = path.stat().st_size
    if size < min_bytes:
        raise SystemExit(f"{what}: {path.name} chỉ {size:,} byte — không phải bộ cài thật")
    with path.open("rb") as f:
        head = f.read(len(magic))
    if head != magic:
        raise SystemExit(f"{what}: {path.name} không bắt đầu bằng {magic!r} — không phải bộ cài thật")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def release_url(version: str, name: str) -> str:
    return f"https://github.com/{REPO}/releases/download/v{version}/{name}"


def winget_version(version: str) -> str:
    return f"""# Created with tools/package_manifests.py — do not edit by hand.
PackageIdentifier: {PACKAGE_ID}
PackageVersion: {version}
DefaultLocale: en-US
ManifestType: version
ManifestVersion: 1.10.0
"""


def winget_installer(version: str, setup_url: str, setup_hash: str) -> str:
    return f"""# Created with tools/package_manifests.py — do not edit by hand.
PackageIdentifier: {PACKAGE_ID}
PackageVersion: {version}
InstallerType: inno
Scope: user
InstallModes:
  - interactive
  - silent
  - silentWithProgress
UpgradeBehavior: install
Installers:
  - Architecture: x64
    InstallerUrl: {setup_url}
    InstallerSha256: {setup_hash.upper()}
    InstallerSwitches:
      Silent: /VERYSILENT /SUPPRESSMSGBOXES /NORESTART
      SilentWithProgress: /SILENT /SUPPRESSMSGBOXES /NORESTART
ManifestType: installer
ManifestVersion: 1.10.0
"""


def winget_locale(version: str) -> str:
    tags = "\n".join(f"  - {t}" for t in TAGS)
    return f"""# Created with tools/package_manifests.py — do not edit by hand.
PackageIdentifier: {PACKAGE_ID}
PackageVersion: {version}
PackageLocale: en-US
Publisher: {PUBLISHER}
PublisherUrl: https://github.com/write2mustaf-cpu
PublisherSupportUrl: https://github.com/{REPO}/blob/main/docs/SUPPORT.md
PackageName: ControlPhone
PackageUrl: https://github.com/{REPO}
License: MIT
LicenseUrl: https://github.com/{REPO}/blob/main/LICENSE
Copyright: Copyright (c) ControlPhone contributors
ShortDescription: Control many Android phones from one Windows grid — mirroring, batch control, AI assistant.
Description: >-
  {DESCRIPTION}
Moniker: {MONIKER}
Tags:
{tags}
ReleaseNotesUrl: https://github.com/{REPO}/releases/tag/v{version}
ManifestType: locale
ManifestVersion: 1.10.0
"""


def scoop_manifest(version: str, zip_url: str, zip_hash: str) -> str:
    doc = {
        "$schema": "https://raw.githubusercontent.com/ScoopInstaller/Scoop/master/schema.json",
        "version": version,
        "description": DESCRIPTION,
        "homepage": f"https://github.com/{REPO}",
        "license": "MIT",
        "architecture": {
            "64bit": {"url": zip_url, "hash": zip_hash},
        },
        "bin": "ControlPhone.exe",
        "shortcuts": [["ControlPhone.exe", "ControlPhone"]],
        # The app keeps config, logs and chats in <app>\\data; persist it so scoop update keeps user data.
        "persist": "data",
        "checkver": {"github": f"https://github.com/{REPO}"},
        "autoupdate": {
            "architecture": {
                "64bit": {"url": release_url("$version", "ControlPhone-v$version-win64.zip")},
            },
        },
    }
    return json.dumps(doc, indent=4, ensure_ascii=False) + "\n"


def canonical_names(version: str) -> tuple[str, str]:
    return f"ControlPhone-Setup-v{version}-win64.exe", f"ControlPhone-v{version}-win64.zip"


def build(version: str, setup: Path, portable: Path, out: Path) -> dict:
    if not VERSION_RE.match(version):
        raise SystemExit(f"version must look like 1.2.3, got {version!r}")
    setup_name, zip_name = canonical_names(version)
    for f, want in ((setup, setup_name), (portable, zip_name)):
        if not f.is_file() or f.stat().st_size == 0:
            raise SystemExit(f"missing or empty artifact: {f}")
        # The manifest URL must be the real asset name; a renamed local copy would publish a dead link.
        if f.name != want:
            raise SystemExit(f"{f} must be named {want} (release asset names are what the manifest points at)")

    setup_hash, zip_hash = sha256(setup), sha256(portable)
    setup_url = release_url(version, setup_name)
    zip_url = release_url(version, zip_name)

    out.mkdir(parents=True, exist_ok=True)
    files = {
        out / f"{PACKAGE_ID}.yaml": winget_version(version),
        out / f"{PACKAGE_ID}.installer.yaml": winget_installer(version, setup_url, setup_hash),
        out / f"{PACKAGE_ID}.locale.en-US.yaml": winget_locale(version),
        out / f"{MONIKER}.json": scoop_manifest(version, zip_url, zip_hash),
    }
    for path, text in files.items():
        path.write_text(text, encoding="utf-8", newline="\n")

    index = {
        "version": version,
        "setup": {"name": setup_name, "bytes": setup.stat().st_size, "sha256": setup_hash, "url": setup_url},
        "portable": {"name": zip_name, "bytes": portable.stat().st_size, "sha256": zip_hash, "url": zip_url},
    }
    (out / "manifests.json").write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8", newline="\n")
    verify(out, version, setup_hash, zip_hash)
    return index


def verify(out: Path, version: str, setup_hash: str, zip_hash: str) -> None:
    """Kiểm tra lại chính thứ vừa ghi: một manifest sai phiên bản/hash là gói cài hỏng."""
    problems: list[str] = []
    for path in sorted(out.glob("*.yaml")):
        text = path.read_text(encoding="utf-8")
        if f"PackageVersion: {version}" not in text and "ManifestType: version" not in text:
            problems.append(f"{path.name}: version missing")
        if f"/v{version}/" not in text and "installer" in path.name:
            problems.append(f"{path.name}: installer URL is not pinned to v{version}")
        if "installer" in path.name:
            if setup_hash.upper() not in text:
                problems.append(f"{path.name}: SHA256 does not match the setup file")
            if "InstallerType: inno" not in text:
                problems.append(f"{path.name}: installer type lost")
        if not text.startswith("# Created with tools/package_manifests.py"):
            problems.append(f"{path.name}: missing generated header")
    scoop = json.loads((out / f"{MONIKER}.json").read_text(encoding="utf-8"))
    if scoop["version"] != version:
        problems.append("scoop: version mismatch")
    if scoop["architecture"]["64bit"]["hash"] != zip_hash:
        problems.append("scoop: hash does not match the portable zip")
    if not HASH_RE.match(scoop["architecture"]["64bit"]["hash"]):
        problems.append("scoop: hash is not 64 lowercase hex characters")
    if scoop["persist"] != "data":
        problems.append("scoop: user data would be lost on update")
    if scoop["bin"] != "ControlPhone.exe":
        problems.append("scoop: bin entry lost")
    if problems:
        raise SystemExit("manifest verification failed:\n  " + "\n  ".join(problems))


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Generate winget + scoop manifests from release artifacts")
    ap.add_argument("--version", required=True)
    ap.add_argument("--setup", required=True, type=Path)
    ap.add_argument("--zip", required=True, type=Path, dest="portable")
    ap.add_argument("--out", type=Path, default=Path("dist/manifests"))
    a = ap.parse_args(argv)
    assert_real_artifact(a.setup, b"MZ", MIN_SETUP, "setup")
    assert_real_artifact(a.portable, b"PK", MIN_ZIP, "portable")
    index = build(a.version, a.setup, a.portable, a.out)
    print(f"manifests for {a.version} written to {a.out}")
    print(f"  setup    {index['setup']['bytes']:>10,} B  sha256 {index['setup']['sha256']}")
    print(f"  portable {index['portable']['bytes']:>10,} B  sha256 {index['portable']['sha256']}")
    for f in sorted(a.out.iterdir()):
        print(f"  {f.name}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
