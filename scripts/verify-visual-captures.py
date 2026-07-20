#!/usr/bin/env python3
"""Validate that automated gameplay captures are Steam-store ready."""

from __future__ import annotations

import hashlib
import struct
import sys
from pathlib import Path


EXPECTED_NAMES = {
    "01-tutorial-briefing.png",
    "02-live-combat.png",
    "03-pause.png",
    "04-settings.png",
    "05-keyboard-bindings.png",
    "06-controller-bindings.png",
    "07-mission-select.png",
    "08-story-briefing.png",
    "09-victory-debrief.png",
    "10-fleet-battle.png",
}
MINIMUM_WIDTH = 1920
MINIMUM_HEIGHT = 1080


def png_size(path: Path) -> tuple[int, int]:
    with path.open("rb") as handle:
        header = handle.read(24)
    if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
        raise ValueError(f"{path.name} is not a valid PNG")
    return struct.unpack(">II", header[16:24])


def validate(directory: Path) -> list[str]:
    errors: list[str] = []
    captures = sorted(directory.glob("*.png"))
    names = {capture.name for capture in captures}
    if names != EXPECTED_NAMES:
        missing = sorted(EXPECTED_NAMES - names)
        unexpected = sorted(names - EXPECTED_NAMES)
        if missing:
            errors.append(f"missing captures: {', '.join(missing)}")
        if unexpected:
            errors.append(f"unexpected captures: {', '.join(unexpected)}")

    digests: dict[str, str] = {}
    for capture in captures:
        try:
            width, height = png_size(capture)
        except ValueError as error:
            errors.append(str(error))
            continue
        if width < MINIMUM_WIDTH or height < MINIMUM_HEIGHT:
            errors.append(
                f"{capture.name} is {width}x{height}; minimum is {MINIMUM_WIDTH}x{MINIMUM_HEIGHT}"
            )
        if width * 9 != height * 16:
            errors.append(f"{capture.name} is not 16:9 ({width}x{height})")
        digest = hashlib.sha256(capture.read_bytes()).hexdigest()
        if digest in digests:
            errors.append(f"{capture.name} duplicates {digests[digest]}")
        else:
            digests[digest] = capture.name
    return errors


def main() -> int:
    directory = Path(sys.argv[1]) if len(sys.argv) == 2 else Path("visual-qa")
    if not directory.is_dir():
        print(f"capture directory does not exist: {directory}", file=sys.stderr)
        return 1
    errors = validate(directory)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    print(f"AFC_CAPTURE_PASS captures={len(EXPECTED_NAMES)} minimum={MINIMUM_WIDTH}x{MINIMUM_HEIGHT} aspect=16:9")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
