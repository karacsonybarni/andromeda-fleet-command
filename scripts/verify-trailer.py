#!/usr/bin/env python3
"""Reject trailer exports that are unsuitable for a 1080p campaign page."""

from __future__ import annotations

import json
import re
import subprocess
import sys
from pathlib import Path


MINIMUM_BYTES = 1_000_000
MINIMUM_DURATION = 29.0
MAXIMUM_DURATION = 32.0
MINIMUM_LOUDNESS = -19.0
MAXIMUM_LOUDNESS = -13.0


def probe(path: Path) -> dict:
    result = subprocess.run(
        [
            "ffprobe",
            "-v",
            "error",
            "-show_entries",
            "format=duration:stream=index,codec_type,codec_name,width,height,avg_frame_rate,sample_rate,channels",
            "-of",
            "json",
            str(path),
        ],
        check=True,
        capture_output=True,
        text=True,
    )
    return json.loads(result.stdout)


def frame_rate(value: str) -> float:
    numerator, separator, denominator = value.partition("/")
    return float(numerator) / float(denominator) if separator and float(denominator) else float(numerator)


def integrated_loudness(path: Path) -> float:
    result = subprocess.run(
        ["ffmpeg", "-i", str(path), "-filter_complex", "ebur128=peak=true", "-f", "null", "-"],
        check=True,
        capture_output=True,
        text=True,
    )
    summary = result.stderr.rsplit("Summary:", 1)[-1]
    match = re.search(r"\bI:\s*(-?\d+(?:\.\d+)?) LUFS", summary)
    if match is None:
        raise ValueError("FFmpeg did not report integrated loudness")
    return float(match.group(1))


def validate(path: Path) -> list[str]:
    errors: list[str] = []
    if not path.is_file():
        return [f"trailer does not exist: {path}"]
    if path.stat().st_size < MINIMUM_BYTES:
        errors.append(f"trailer is only {path.stat().st_size} bytes; expected at least {MINIMUM_BYTES}")

    try:
        metadata = probe(path)
    except (subprocess.CalledProcessError, FileNotFoundError, json.JSONDecodeError) as error:
        return errors + [f"ffprobe could not inspect trailer: {error}"]

    try:
        duration = float(metadata["format"]["duration"])
    except (KeyError, TypeError, ValueError):
        errors.append("trailer has no readable duration")
        duration = 0
    if not MINIMUM_DURATION <= duration <= MAXIMUM_DURATION:
        errors.append(
            f"trailer duration is {duration:.2f}s; expected {MINIMUM_DURATION:.0f}–{MAXIMUM_DURATION:.0f}s"
        )

    streams = metadata.get("streams", [])
    video = next((stream for stream in streams if stream.get("codec_type") == "video"), None)
    audio = next((stream for stream in streams if stream.get("codec_type") == "audio"), None)
    if video is None:
        errors.append("trailer has no video stream")
    else:
        width, height = video.get("width"), video.get("height")
        if (width, height) != (1920, 1080):
            errors.append(f"video is {width}x{height}; expected 1920x1080")
        if video.get("codec_name") != "h264":
            errors.append(f"video codec is {video.get('codec_name')}; expected h264")
        try:
            fps = frame_rate(video.get("avg_frame_rate", "0"))
        except (TypeError, ValueError, ZeroDivisionError):
            fps = 0
        if fps < 29:
            errors.append(f"video frame rate is {fps:.2f}; expected at least 29 fps")

    if audio is None:
        errors.append("trailer has no audio stream")
    else:
        if audio.get("codec_name") != "aac":
            errors.append(f"audio codec is {audio.get('codec_name')}; expected aac")
        if int(audio.get("sample_rate", 0)) < 44_100:
            errors.append(f"audio sample rate is {audio.get('sample_rate')}; expected at least 44100 Hz")
        if int(audio.get("channels", 0)) < 2:
            errors.append(f"audio has {audio.get('channels')} channel(s); expected stereo")
        try:
            loudness = integrated_loudness(path)
        except (subprocess.CalledProcessError, FileNotFoundError, ValueError) as error:
            errors.append(f"audio loudness could not be measured: {error}")
        else:
            if not MINIMUM_LOUDNESS <= loudness <= MAXIMUM_LOUDNESS:
                errors.append(
                    f"integrated loudness is {loudness:.1f} LUFS; expected "
                    f"{MINIMUM_LOUDNESS:.0f} to {MAXIMUM_LOUDNESS:.0f} LUFS"
                )
    return errors


def main() -> int:
    path = Path(sys.argv[1]) if len(sys.argv) == 2 else Path("trailer/andromeda-fleet-command-trailer.mp4")
    errors = validate(path)
    if errors:
        for error in errors:
            print(f"ERROR: {error}", file=sys.stderr)
        return 1
    metadata = probe(path)
    loudness = integrated_loudness(path)
    print(
        "AFC_TRAILER_VALIDATION_PASS "
        f"file={path} duration={float(metadata['format']['duration']):.2f}s "
        f"resolution=1920x1080 audio=stereo loudness={loudness:.1f}LUFS"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
