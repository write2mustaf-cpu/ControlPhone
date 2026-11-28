#!/usr/bin/env python3
"""Lấy đúng phần của phiên bản hiện tại trong CHANGELOG.md để làm ghi chú phát hành.

Trước đây CI truyền cả tệp CHANGELOG qua dòng lệnh (`gh release create --notes $notes`). Khi tệp vượt giới hạn
32.767 ký tự của Windows, bước phát hành chết với "The filename or extension is too long" — sau khi đã dựng xong
bộ cài và cài thử. Ghi chú phát hành cũng không nên chứa toàn bộ lịch sử, chỉ phần của phiên bản này.

    python tools/release_notes.py --version 3.16.0 --changelog CHANGELOG.md --out dist/notes.md
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

MAX_BYTES = 60_000


def section(text: str, version: str) -> str | None:
    """Phần bắt đầu bằng '## <version>' cho tới tiêu đề '## ' kế tiếp."""
    lines = text.splitlines()
    start = None
    head = re.compile(rf"^##\s+v?{re.escape(version)}\b")
    for i, line in enumerate(lines):
        if start is None:
            if head.match(line.strip()):
                start = i
        elif line.startswith("## "):
            return "\n".join(lines[start:i]).rstrip() + "\n"
    return None if start is None else "\n".join(lines[start:]).rstrip() + "\n"


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--version", required=True)
    ap.add_argument("--changelog", type=Path, default=Path("CHANGELOG.md"))
    ap.add_argument("--out", type=Path, required=True)
    ap.add_argument("--tag", default="")
    a = ap.parse_args(argv)

    if not a.changelog.is_file():
        print(f"no changelog at {a.changelog}", file=sys.stderr)
        return 1
    body = section(a.changelog.read_text(encoding="utf-8"), a.version)
    if body is None:
        # Không có phần của phiên bản này: vẫn phát hành, nhưng ghi chú tối thiểu thay vì toàn bộ lịch sử.
        body = f"ControlPhone {a.version}\n"
        print(f"warning: no '## {a.version}' section in {a.changelog}", file=sys.stderr)

    raw = body.encode("utf-8")
    if len(raw) > MAX_BYTES:
        body = raw[:MAX_BYTES].decode("utf-8", "ignore") + "\n…\n"
        print(f"warning: notes truncated to {MAX_BYTES} bytes", file=sys.stderr)

    a.out.parent.mkdir(parents=True, exist_ok=True)
    a.out.write_text(body, encoding="utf-8", newline="\n")
    print(f"{a.out}: {len(body.encode('utf-8'))} bytes, starts with {body.splitlines()[0][:60]!r}")
    if a.tag and not body.lstrip().startswith("#") and a.tag not in body:
        print("warning: notes do not mention the tag", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
