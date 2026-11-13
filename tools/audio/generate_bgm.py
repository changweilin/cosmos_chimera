#!/usr/bin/env python3
"""Generate scene BGM tracks using the Gemini API Lyria models."""

from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

MIME_TO_EXT = {
    "audio/mpeg": ".mp3",
    "audio/mp3": ".mp3",
    "audio/wav": ".wav",
    "audio/x-wav": ".wav",
    "audio/ogg": ".ogg",
    "audio/flac": ".flac",
}


def generate_track(api_key: str, model: str, prompt: str, timeout: int = 180) -> tuple[bytes, str]:
    url = f"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={api_key}"
    payload = json.dumps({"contents": [{"parts": [{"text": prompt}]}]}).encode("utf-8")
    req = urllib.request.Request(url, data=payload, headers={"Content-Type": "application/json"})

    with urllib.request.urlopen(req, timeout=timeout) as resp:
        data = json.loads(resp.read().decode("utf-8"))

    for candidate in data.get("candidates", []):
        for part in candidate.get("content", {}).get("parts", []):
            inline = part.get("inlineData")
            if inline and inline.get("data"):
                mime = inline.get("mimeType", "audio/mpeg")
                return base64.b64decode(inline["data"]), MIME_TO_EXT.get(mime, ".mp3")

    raise RuntimeError(f"No audio inlineData returned in response: {json.dumps(data)[:400]}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Generate BGM tracks with Lyria via Gemini API.")
    parser.add_argument("--manifest", default="tools/audio/bgm_prompts.json")
    parser.add_argument("--out-dir", default="Assets/_Project/Audio/BGM")
    parser.add_argument("--model", default=None, help="Override model (e.g. lyria-3-pro-preview, lyria-3-clip-preview, lyria-3.5)")
    parser.add_argument("--only", nargs="*", help="Only generate specific track IDs")
    parser.add_argument("--overwrite", action="store_true")
    args = parser.parse_args()

    api_key = os.environ.get("GEMINI_API_KEY")
    if not api_key:
        print("Error: GEMINI_API_KEY environment variable is not set.", file=sys.stderr)
        return 1

    manifest = json.loads(Path(args.manifest).read_text(encoding="utf-8"))
    model = args.model or manifest.get("default_model", "lyria-3-pro-preview")
    prefix = manifest.get("global_prefix", "")
    out_dir = Path(args.out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)

    tracks = manifest["tracks"]
    if args.only:
        only_set = set(args.only)
        tracks = [t for t in tracks if t["id"] in only_set]

    for track in tracks:
        tid = track["id"]
        existing = list(out_dir.glob(f"{tid}.*"))
        if existing and not args.overwrite:
            print(f"[SKIP] {tid} already exists ({existing[0].name})")
            continue

        full_prompt = f"{prefix} {track['prompt']}".strip()
        print(f"[GEN ] {tid} ({track['scene']}) using {model}...")
        try:
            audio_bytes, ext = generate_track(api_key, model, full_prompt)
        except urllib.error.HTTPError as e:
            body = e.read().decode("utf-8", errors="replace")
            print(f"[FAIL] {tid}: HTTP {e.code} -> {body}", file=sys.stderr)
            if e.code == 429:
                print("Quota exceeded / Free Tier limit=0. Enable billing on GEMINI_API_KEY and retry.", file=sys.stderr)
                return 2
            continue
        except Exception as e:
            print(f"[FAIL] {tid}: {e}", file=sys.stderr)
            continue

        out_path = out_dir / f"{tid}{ext}"
        out_path.write_bytes(audio_bytes)
        print(f"[ OK ] Saved {out_path} ({len(audio_bytes):,} bytes)")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
